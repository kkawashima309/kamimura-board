using System.IO;
using Microsoft.Extensions.Logging;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;
using PdfStudio.Domain.Interfaces;
using PdfStudio.Domain.ValueObjects;

namespace PdfStudio.Infrastructure.Pdf;

/// <summary>
/// PDFsharp を使用した PDF 注釈実装。
/// </summary>
public sealed class PdfSharpAnnotationService : IPdfAnnotationService
{
    private readonly ILogger<PdfSharpAnnotationService> _logger;

    public PdfSharpAnnotationService(ILogger<PdfSharpAnnotationService> logger)
    {
        _logger = logger;
    }

    public Task AddAnnotationAsync(
        string sourceFilePath,
        string outputPath,
        AnnotationOptions options,
        CancellationToken ct = default)
    {
        return AddAnnotationsAsync(sourceFilePath, outputPath, new[] { options }, ct);
    }

    public Task AddAnnotationsAsync(
        string sourceFilePath,
        string outputPath,
        IEnumerable<AnnotationOptions> annotations,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException("元PDFが見つかりません。", sourceFilePath);

            // 編集制限(オーナーPWのみ)のPDFでも注釈できるよう、必要なら
            // 編集可能なコピーを構築して開く。
            using var pdfDoc = PdfDocumentOpener.OpenForEdit(sourceFilePath, out var rebuilt);
            if (rebuilt)
            {
                _logger.LogInformation(
                    "編集制限PDFのため編集可能なコピーを作成して注釈を適用します: {Src}",
                    sourceFilePath);
            }
            int annCount = 0;

            foreach (var opt in annotations)
            {
                ct.ThrowIfCancellationRequested();
                if (opt.PageIndex < 0 || opt.PageIndex >= pdfDoc.PageCount)
                {
                    _logger.LogWarning(
                        "ページ {Page} は範囲外、スキップ", opt.PageIndex);
                    continue;
                }

                var page = pdfDoc.Pages[opt.PageIndex];
                var color = ParseColorAsXColor(opt.ColorHex);

                switch (opt.Kind)
                {
                    case AnnotationKind.StickyNote:
                        AddStickyNote(page, opt, color);
                        break;
                    case AnnotationKind.Highlight:
                        DrawHighlight(page, opt, color);
                        break;
                    case AnnotationKind.Stamp:
                        DrawStamp(page, opt);
                        break;
                }
                annCount++;
            }

            pdfDoc.Save(outputPath);
            _logger.LogInformation(
                "注釈を {Count} 件追加: {Out}", annCount, outputPath);
        }, ct);
    }

    private static void AddStickyNote(PdfSharp.Pdf.PdfPage page, AnnotationOptions opt, XColor color)
    {
        // 付箋: 注釈構造(Acrobat等で読める) + 可視のテキストボックス描画(画面で確認できる)
        // の両方を追加する

        // (1) PDF注釈構造として追加
        var textAnn = new PdfTextAnnotation
        {
            Title = "PdfStudio",
            Subject = "コメント",
            Contents = opt.Text,
            Color = color,
            Icon = PdfTextAnnotationIcon.Comment,
        };
        var iconSize = 20.0;
        textAnn.Rectangle = new PdfSharp.Pdf.PdfRectangle(
            new XRect(opt.X, page.Height.Point - opt.Y - iconSize, iconSize, iconSize));
        page.Annotations.Add(textAnn);

        // (2) 可視描画: 黄色の四角形 + アイコン風マーク + コメントテキスト
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

        // PDF座標は左下原点なので、ユーザーが指定した「上から Y」を変換
        var topY = opt.Y;  // 上端からの距離(ユーザー指定)

        // 付箋アイコン (左上に小さな黄色四角)
        var iconBrush = new XSolidBrush(XColor.FromArgb(255, 0xFF, 0xEB, 0x3B));  // 黄色
        var iconPen = new XPen(XColor.FromArgb(255, 0xC8, 0x9B, 0x00), 1);
        gfx.DrawRectangle(iconPen, iconBrush, opt.X, topY, 18, 18);

        // アイコン内の「📌」風マーカー (赤い円)
        var redBrush = new XSolidBrush(XColor.FromArgb(255, 0xCC, 0x00, 0x00));
        gfx.DrawEllipse(redBrush, opt.X + 5, topY + 5, 8, 8);

        // コメント本文を右側に表示 (テキストボックス風)
        if (!string.IsNullOrWhiteSpace(opt.Text))
        {
            var boxX = opt.X + 24;
            var boxY = topY;
            var boxW = 180.0;
            // テキストの長さに応じて高さを決める
            var lineCount = Math.Max(1, opt.Text.Count(c => c == '\n') + 1);
            var boxH = Math.Max(20, lineCount * 14.0);

            // 背景 (薄黄色)
            var bgBrush = new XSolidBrush(XColor.FromArgb(200, 0xFF, 0xF9, 0xC4));
            var borderPen = new XPen(XColor.FromArgb(255, 0xC8, 0x9B, 0x00), 1);
            gfx.DrawRectangle(borderPen, bgBrush, boxX, boxY, boxW, boxH);

            // テキスト
            var font = FontHelper.Create(9);
            var textBrush = new XSolidBrush(XColor.FromArgb(255, 0x33, 0x33, 0x33));
            var textRect = new XRect(boxX + 4, boxY + 2, boxW - 8, boxH - 4);
            var tf = new XTextFormatter(gfx)
            {
                Alignment = XParagraphAlignment.Left,
            };
            tf.DrawString(opt.Text, font, textBrush, textRect);
        }
    }

    private static void DrawHighlight(PdfSharp.Pdf.PdfPage page, AnnotationOptions opt, XColor color)
    {
        // ハイライトは矩形を半透明色で塗りつぶしで描画(注釈ではなく描画)
        // 標準のハイライト注釈は PdfSharp 6 で対応が限定的なため、
        // 半透明矩形を直接描画するアプローチを採用
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var brush = new XSolidBrush(XColor.FromArgb(80, color.R, color.G, color.B));
        // 座標変換(左下原点 → 左上原点)
        var y = page.Height.Point - opt.Y - opt.Height;
        gfx.DrawRectangle(brush, opt.X, y, opt.Width, opt.Height);
    }

    private static void DrawStamp(PdfSharp.Pdf.PdfPage page, AnnotationOptions opt)
    {
        // スタンプは「赤枠＋赤テキスト」の図形として描画
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var redBrush = new XSolidBrush(XColor.FromArgb(180, 0xCC, 0x00, 0x00));
        var redPen = new XPen(XColor.FromArgb(255, 0xCC, 0x00, 0x00), 2);

        var text = string.IsNullOrEmpty(opt.StampLabel) ? "承認" : opt.StampLabel;
        var font = FontHelper.Create(20, bold: true);

        // ページ座標(左下原点 → 左上原点に変換)
        var y = page.Height.Point - opt.Y - opt.Height;

        // 矩形枠
        gfx.DrawRectangle(redPen, opt.X, y, opt.Width, opt.Height);

        // テキストを中央に配置
        var textSize = gfx.MeasureString(text, font);
        var tx = opt.X + (opt.Width - textSize.Width) / 2;
        var ty = y + (opt.Height + textSize.Height / 2) / 2;
        gfx.DrawString(text, font, redBrush, tx, ty);
    }

    private static XColor ParseColorAsXColor(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return XColor.FromArgb(255, 0xFF, 0xFF, 0x00);
        var h = hex.TrimStart('#');
        if (h.Length != 6) return XColor.FromArgb(255, 0xFF, 0xFF, 0x00);
        try
        {
            byte r = Convert.ToByte(h.Substring(0, 2), 16);
            byte g = Convert.ToByte(h.Substring(2, 2), 16);
            byte b = Convert.ToByte(h.Substring(4, 2), 16);
            return XColor.FromArgb(255, r, g, b);
        }
        catch
        {
            return XColor.FromArgb(255, 0xFF, 0xFF, 0x00);
        }
    }
}
