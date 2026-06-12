using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PdfStudio.Domain.Entities;

namespace PdfStudio.Wpf.ViewModels;

public partial class PageViewModel : ObservableObject
{
    private readonly PdfPage _page;

    [ObservableProperty]
    private BitmapSource? _thumbnail;

    [ObservableProperty]
    private bool _isThumbnailLoading;

    public PageViewModel(PdfPage page)
    {
        _page = page;
    }

    public PdfPage Model => _page;
    public int Index => _page.Index;
    public int DisplayNumber => _page.Index + 1;
    public double Width => _page.Width;
    public double Height => _page.Height;
    public int Rotation => _page.Rotation;

    public string DisplayName => $"ページ {DisplayNumber}";
}
