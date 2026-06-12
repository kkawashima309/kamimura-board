using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PdfStudio.Domain.Entities;
using PdfStudio.Domain.Interfaces;

namespace PdfStudio.Wpf.ViewModels;

public partial class PdfDocumentViewModel : ObservableObject
{
    private readonly IPdfRenderer _renderer;

    [ObservableProperty]
    private ObservableCollection<PageViewModel> _pages = new();

    [ObservableProperty]
    private PageViewModel? _currentPage;

    [ObservableProperty]
    private BitmapSource? _currentPageImage;

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private bool _isPageLoading;

    /// <summary>
    /// ズームモード（ユーザーが選択したモード）
    /// </summary>
    [ObservableProperty]
    private ZoomMode _zoomMode = ZoomMode.Custom;

    /// <summary>
    /// 表示エリアのサイズ（フィット計算用）。MainWindow から設定される。
    /// </summary>
    public double ViewportWidth { get; set; } = 800;
    public double ViewportHeight { get; set; } = 600;

    // ZoomLevel は ScaleTransform で反映されるため、変更時の再描画は不要

    public PdfDocument Document { get; }

    public string Title => Document.IsModified
        ? $"{Document.FileName} *"
        : Document.FileName;

    public PdfDocumentViewModel(PdfDocument document, IPdfRenderer renderer)
    {
        Document = document;
        _renderer = renderer;

        foreach (var p in Document.Pages)
            Pages.Add(new PageViewModel(p));
    }

    /// <summary>
    /// 全ページのサムネイルを非同期に読み込む。
    /// </summary>
    public async Task LoadThumbnailsAsync(CancellationToken ct = default)
    {
        foreach (var pageVm in Pages.ToList())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                pageVm.IsThumbnailLoading = true;
                var bytes = await _renderer.RenderThumbnailAsync(
                    Document.Id, pageVm.Index, 200, ct);
                pageVm.Thumbnail = BytesToBitmap(bytes);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // 個別失敗はスキップ
            }
            finally
            {
                pageVm.IsThumbnailLoading = false;
            }
        }
    }

    /// <summary>
    /// 指定ページのサムネイルだけを再生成する(回転後の更新用)。
    /// </summary>
    public async Task RefreshThumbnailAsync(int pageIndex, CancellationToken ct = default)
    {
        if (pageIndex < 0 || pageIndex >= Pages.Count) return;
        var pageVm = Pages[pageIndex];
        try
        {
            pageVm.IsThumbnailLoading = true;
            var bytes = await _renderer.RenderThumbnailAsync(
                Document.Id, pageIndex, 200, ct);
            pageVm.Thumbnail = BytesToBitmap(bytes);
        }
        catch
        {
            // 失敗してもエラーにしない
        }
        finally
        {
            pageVm.IsThumbnailLoading = false;
        }
    }

    /// <summary>
    /// 指定インデックスのページを高解像度で表示。
    /// </summary>
    public async Task ShowPageAsync(int pageIndex, CancellationToken ct = default)
    {
        if (pageIndex < 0 || pageIndex >= Pages.Count) return;

        try
        {
            IsPageLoading = true;
            var dpi = (int)(96 * ZoomLevel);
            // 1.0倍で96dpi、2.0倍で192dpi。極端な値を抑制。
            dpi = Math.Clamp(dpi, 48, 300);

            var bytes = await _renderer.RenderPageAsync(
                Document.Id, pageIndex, dpi, ct);
            CurrentPageImage = BytesToBitmap(bytes);
            CurrentPage = Pages[pageIndex];
        }
        catch (OperationCanceledException) { /* ignore */ }
        finally
        {
            IsPageLoading = false;
        }
    }

    /// <summary>
    /// Document の Pages リストの変更を ObservableCollection に同期。
    /// </summary>
    public void RefreshPages()
    {
        Pages.Clear();
        foreach (var p in Document.Pages)
            Pages.Add(new PageViewModel(p));

        // サムネイル再ロードはバックグラウンドで
        _ = LoadThumbnailsAsync();
    }

    private static BitmapSource BytesToBitmap(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = ms;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    partial void OnIsModifiedChanged(bool value)
    {
        Document.IsModified = value;
        OnPropertyChanged(nameof(Title));
    }
}
