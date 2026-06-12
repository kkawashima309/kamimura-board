namespace PdfStudio.Domain.ValueObjects;

/// <summary>
/// 注釈の種類。
/// </summary>
public enum AnnotationKind
{
    /// <summary>付箋(コメント)</summary>
    StickyNote,
    /// <summary>ハイライト</summary>
    Highlight,
    /// <summary>テキストスタンプ(承認・却下等)</summary>
    Stamp,
}

/// <summary>
/// 注釈追加時のオプション。
/// </summary>
public sealed record AnnotationOptions
{
    /// <summary>注釈の種類</summary>
    public AnnotationKind Kind { get; init; }

    /// <summary>対象ページ(0始まり)</summary>
    public int PageIndex { get; init; }

    /// <summary>X座標(ポイント、左下基準)</summary>
    public double X { get; init; }

    /// <summary>Y座標(ポイント、左下基準)</summary>
    public double Y { get; init; }

    /// <summary>幅(ポイント)</summary>
    public double Width { get; init; } = 100;

    /// <summary>高さ(ポイント)</summary>
    public double Height { get; init; } = 20;

    /// <summary>表示テキストまたはコメント本文</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>色(Hex)</summary>
    public string ColorHex { get; init; } = "#FFFF00";

    /// <summary>スタンプテキスト(Kind=Stampの場合)</summary>
    public string StampLabel { get; init; } = "承認";
}
