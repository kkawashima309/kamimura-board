namespace PdfStudio.Domain.Entities;

/// <summary>
/// PDFのページを表すエンティティ。
/// </summary>
public class PdfPage
{
    /// <summary>
    /// ドキュメント内でのページ番号（0始まり）。
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// ページ幅（ポイント単位）。
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// ページ高さ（ポイント単位）。
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// ページの回転角度（0 / 90 / 180 / 270）。
    /// </summary>
    public int Rotation { get; set; }
}
