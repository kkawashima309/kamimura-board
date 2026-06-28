namespace PdfStudio.Wpf.ViewModels;

/// <summary>
/// 検索ヒットのハイライト矩形1件分(表示用)。
/// 座標はページ幅・高さに対する比率(0.0〜1.0、左上原点)。
/// </summary>
public sealed class PageHighlightViewModel
{
    public double Left { get; init; }
    public double Top { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }

    /// <summary>現在選択中の検索ヒットに属する矩形かどうか</summary>
    public bool IsSelected { get; init; }
}
