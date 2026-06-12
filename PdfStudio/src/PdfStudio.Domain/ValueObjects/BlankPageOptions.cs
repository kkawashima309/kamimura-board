namespace PdfStudio.Domain.ValueObjects;

/// <summary>
/// 白紙ページ追加時のサイズ。
/// </summary>
public enum BlankPageSize
{
    /// <summary>A4 (210x297mm)</summary>
    A4,
    /// <summary>A3 (297x420mm)</summary>
    A3,
    /// <summary>A5 (148x210mm)</summary>
    A5,
    /// <summary>B5 (176x250mm)</summary>
    B5,
    /// <summary>US Letter (8.5x11 inch)</summary>
    Letter,
    /// <summary>US Legal (8.5x14 inch)</summary>
    Legal,
    /// <summary>挿入先の最初のページと同じサイズ</summary>
    MatchFirstPage,
}

/// <summary>
/// 白紙ページ追加時の向き。
/// </summary>
public enum BlankPageOrientation
{
    /// <summary>縦</summary>
    Portrait,
    /// <summary>横</summary>
    Landscape,
}
