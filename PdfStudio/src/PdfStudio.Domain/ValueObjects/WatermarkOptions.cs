namespace PdfStudio.Domain.ValueObjects;

/// <summary>
/// ウォーターマーク追加時のオプション。
/// </summary>
public sealed record WatermarkOptions
{
    /// <summary>表示するテキスト</summary>
    public string Text { get; init; } = "社外秘";

    /// <summary>フォントサイズ(ポイント)</summary>
    public double FontSize { get; init; } = 72;

    /// <summary>透明度(0=完全透明、1=不透明)</summary>
    public double Opacity { get; init; } = 0.25;

    /// <summary>傾き角度(度)。-45で右下から左上の斜め配置</summary>
    public double RotationDegrees { get; init; } = -45;

    /// <summary>色 (RGB の Hex 表記、例: "#FF0000")</summary>
    public string ColorHex { get; init; } = "#888888";
}

/// <summary>
/// ページ番号追加のオプション。
/// </summary>
public sealed record PageNumberOptions
{
    /// <summary>フォーマット文字列。{page}=現ページ、{total}=総ページ数。例: "{page} / {total}"</summary>
    public string Format { get; init; } = "{page} / {total}";

    /// <summary>配置位置</summary>
    public PageNumberPosition Position { get; init; } = PageNumberPosition.BottomCenter;

    /// <summary>フォントサイズ(ポイント)</summary>
    public double FontSize { get; init; } = 10;

    /// <summary>マージン(ポイント、ページ端からの距離)</summary>
    public double Margin { get; init; } = 24;

    /// <summary>開始ページ番号(通常1)</summary>
    public int StartingNumber { get; init; } = 1;

    /// <summary>開始ページ(これより前は番号を付けない、0始まり)</summary>
    public int FirstPageIndex { get; init; } = 0;
}

public enum PageNumberPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

/// <summary>
/// ヘッダー/フッターのオプション。
/// </summary>
public sealed record HeaderFooterOptions
{
    /// <summary>ヘッダー左テキスト</summary>
    public string HeaderLeft { get; init; } = "";
    /// <summary>ヘッダー中央テキスト</summary>
    public string HeaderCenter { get; init; } = "";
    /// <summary>ヘッダー右テキスト</summary>
    public string HeaderRight { get; init; } = "";

    /// <summary>フッター左テキスト</summary>
    public string FooterLeft { get; init; } = "";
    /// <summary>フッター中央テキスト</summary>
    public string FooterCenter { get; init; } = "";
    /// <summary>フッター右テキスト</summary>
    public string FooterRight { get; init; } = "";

    /// <summary>フォントサイズ(ポイント)</summary>
    public double FontSize { get; init; } = 9;

    /// <summary>マージン(ポイント)</summary>
    public double Margin { get; init; } = 20;

    /// <summary>{date}や{filename}などのプレースホルダを使用可能。</summary>
}
