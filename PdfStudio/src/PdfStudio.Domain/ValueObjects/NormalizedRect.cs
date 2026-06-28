namespace PdfStudio.Domain.ValueObjects;

/// <summary>
/// ページ上の矩形領域を、ページ幅・高さに対する比率(0.0〜1.0)で表す。
/// 原点は左上、Y軸は下向き(WPF座標系に合わせる)。
/// </summary>
public sealed record NormalizedRect
{
    public double Left { get; init; }
    public double Top { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}
