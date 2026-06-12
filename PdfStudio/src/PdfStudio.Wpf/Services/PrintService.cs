using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using PdfStudio.Domain.Entities;
using PdfStudio.Domain.Interfaces;
using PdfStudio.Wpf.Views.Dialogs;

namespace PdfStudio.Wpf.Services;

/// <summary>
/// PDF印刷サービス。
/// WPFのPrintDialogを使い、各ページをPdfiumRendererで画像にレンダリングしてから印刷。
/// PDFの向き(縦/横)を自動判定し、印刷時の用紙の向きを合わせる。
/// </summary>
public class PrintService
{
    private readonly IPdfRenderer _renderer;
    private readonly ILogger<PrintService> _logger;

    public PrintService(IPdfRenderer renderer, ILogger<PrintService> logger)
    {
        _renderer = renderer;
        _logger = logger;
    }

    /// <summary>
    /// 印刷ダイアログを表示し、指定PDFを印刷する。
    /// PDFが横向きのページを含む場合は、自動的に横印刷に切り替える。
    /// </summary>
    /// <returns>印刷が実行された場合true、キャンセル時false</returns>
    public async Task<bool> PrintAsync(PdfDocument document)
    {
        if (document.Pages.Count == 0)
            throw new InvalidOperationException("ページがありません。");

        var dlg = new System.Windows.Controls.PrintDialog
        {
            UserPageRangeEnabled = true,
            MinPage = 1,
            MaxPage = (uint)document.Pages.Count,
            PageRangeSelection = PageRangeSelection.AllPages,
        };

        if (dlg.ShowDialog() != true)
            return false;

        // 印刷対象ページを決定
        var pagesToPrint = new List<int>();
        if (dlg.PageRangeSelection == PageRangeSelection.UserPages
            && dlg.PageRange.PageFrom > 0 && dlg.PageRange.PageTo > 0)
        {
            for (int p = dlg.PageRange.PageFrom; p <= dlg.PageRange.PageTo; p++)
            {
                if (p >= 1 && p <= document.Pages.Count)
                    pagesToPrint.Add(p - 1);  // 0-based
            }
        }
        else
        {
            for (int p = 0; p < document.Pages.Count; p++)
                pagesToPrint.Add(p);
        }

        if (pagesToPrint.Count == 0)
            throw new InvalidOperationException("印刷するページがありません。");

        // PDFのページから「向き」を判定
        // 印刷対象ページの中に1つでも横ページがあれば、Landscapeで印刷
        var hasLandscape = false;
        var hasPortrait = false;
        foreach (var idx in pagesToPrint)
        {
            if (idx < 0 || idx >= document.Pages.Count) continue;
            var p = document.Pages[idx];
            // 回転を考慮した実際の幅・高さを計算
            var (effectiveW, effectiveH) = GetEffectiveSize(p.Width, p.Height, p.Rotation);
            if (effectiveW > effectiveH) hasLandscape = true;
            else hasPortrait = true;
        }

        // PrintTicket で用紙の向きを設定
        // 全ページが同じ向きの場合は、その向きに統一
        // 混在している場合は、最初のページの向きを採用(後でページごとに回転処理)
        var printTicket = dlg.PrintTicket;
        if (hasLandscape && !hasPortrait)
        {
            printTicket.PageOrientation = PageOrientation.Landscape;
            _logger.LogInformation("印刷: 全ページが横向き → Landscape");
        }
        else if (hasPortrait && !hasLandscape)
        {
            printTicket.PageOrientation = PageOrientation.Portrait;
            _logger.LogInformation("印刷: 全ページが縦向き → Portrait");
        }
        else
        {
            // 混在: 最初のページに合わせる
            var firstIdx = pagesToPrint[0];
            var firstPage = document.Pages[firstIdx];
            var (w, h) = GetEffectiveSize(firstPage.Width, firstPage.Height, firstPage.Rotation);
            printTicket.PageOrientation = (w > h) ? PageOrientation.Landscape : PageOrientation.Portrait;
            _logger.LogInformation(
                "印刷: 縦横混在 → 最初のページに合わせて {Orient}",
                printTicket.PageOrientation);
        }

        // PrintTicketを適用してから印刷可能領域を取得
        // 注: PrintableAreaWidth/Height はチケット変更後に再評価される必要がある
        var printSize = GetPrintableSize(printTicket, dlg);

        // 印刷用 FixedDocument を構築
        var fixedDoc = new FixedDocument();

        foreach (var pageIdx in pagesToPrint)
        {
            // 印刷品質のため高めのDPI(150)でレンダリング
            // PdfiumRenderer は内部でページの Rotation を反映する
            var bytes = await _renderer.RenderPageAsync(document.Id, pageIdx, 150);
            var bitmap = BytesToBitmap(bytes);

            // ページごとに用紙サイズに合わせて配置
            // 縦横混在の場合、各ページごとに紙の向きと逆ならフィット計算が変わる
            var page = document.Pages[pageIdx];
            var (effW, effH) = GetEffectiveSize(page.Width, page.Height, page.Rotation);
            bool pageIsLandscape = effW > effH;
            bool paperIsLandscape = printTicket.PageOrientation == PageOrientation.Landscape;

            var fixedPage = CreateFixedPage(bitmap, printSize, pageIsLandscape, paperIsLandscape);
            var content = new PageContent();
            ((IAddChild)content).AddChild(fixedPage);
            fixedDoc.Pages.Add(content);
        }

        var docName = string.IsNullOrEmpty(document.FilePath)
            ? "PdfStudio"
            : System.IO.Path.GetFileNameWithoutExtension(document.FilePath);

        // --- 印刷プレビュー表示 ---
        var previewDlg = new PrintPreviewDialog(fixedDoc, docName);
        // アクティブウィンドウをオーナーに
        var owner = System.Windows.Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive);
        if (owner != null) previewDlg.Owner = owner;
        previewDlg.ShowDialog();

        if (!previewDlg.ShouldPrint)
        {
            _logger.LogInformation("印刷プレビューでキャンセル: {Name}", docName);
            return false;
        }

        // PrintTicketを反映して印刷
        dlg.PrintDocument(fixedDoc.DocumentPaginator, docName);

        _logger.LogInformation(
            "印刷を実行しました: {Name}, {Count}ページ, 向き={Orient}",
            docName, pagesToPrint.Count, printTicket.PageOrientation);

        return true;
    }

    /// <summary>
    /// 回転を考慮した実際の幅・高さを計算する。
    /// 90/270度回転している場合は幅と高さが入れ替わる。
    /// </summary>
    private static (double width, double height) GetEffectiveSize(double width, double height, int rotation)
    {
        var r = ((rotation % 360) + 360) % 360;
        return (r == 90 || r == 270) ? (height, width) : (width, height);
    }

    /// <summary>
    /// PrintTicketに従って印刷可能領域サイズを取得。
    /// </summary>
    private static Size GetPrintableSize(PrintTicket ticket, System.Windows.Controls.PrintDialog dlg)
    {
        // ダイアログのデフォルト値を使うが、Landscape指定時は幅高さを入れ替える
        var w = dlg.PrintableAreaWidth;
        var h = dlg.PrintableAreaHeight;

        // dlg.PrintableArea は最初のPageOrientationに基づいて返される。
        // PrintTicketで向きを変えた場合、論理的に幅高さを入れ替える必要がある。
        bool currentIsLandscape = w > h;
        bool wantLandscape = ticket.PageOrientation == PageOrientation.Landscape;

        if (currentIsLandscape != wantLandscape)
        {
            return new Size(h, w);
        }
        return new Size(w, h);
    }

    private static FixedPage CreateFixedPage(
        BitmapSource bitmap,
        Size printSize,
        bool pageIsLandscape,
        bool paperIsLandscape)
    {
        var fixedPage = new FixedPage
        {
            Width = printSize.Width,
            Height = printSize.Height,
        };

        // 画像をページに収まるように縦横比を維持してスケーリング
        var imageRatio = bitmap.PixelWidth / (double)bitmap.PixelHeight;
        var pageRatio = printSize.Width / printSize.Height;

        double targetW, targetH;
        if (imageRatio > pageRatio)
        {
            // 画像のほうが横長 → 幅にフィット
            targetW = printSize.Width;
            targetH = printSize.Width / imageRatio;
        }
        else
        {
            // 画像のほうが縦長 → 高さにフィット
            targetH = printSize.Height;
            targetW = printSize.Height * imageRatio;
        }

        var image = new Image
        {
            Source = bitmap,
            Width = targetW,
            Height = targetH,
            Stretch = Stretch.Uniform,
        };

        // ページ中央に配置
        FixedPage.SetLeft(image, (printSize.Width - targetW) / 2);
        FixedPage.SetTop(image, (printSize.Height - targetH) / 2);

        fixedPage.Children.Add(image);
        return fixedPage;
    }

    private static BitmapSource BytesToBitmap(byte[] bytes)
    {
        using var ms = new System.IO.MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = ms;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
