using System.IO;

namespace PdfStudio.Domain.Entities;

/// <summary>
/// PDFドキュメントを表すエンティティ。
/// </summary>
public class PdfDocument
{
    public Guid Id { get; } = Guid.NewGuid();

    public string FilePath { get; set; } = string.Empty;

    public string FileName => string.IsNullOrEmpty(FilePath)
        ? "(無題)"
        : Path.GetFileName(FilePath);

    public List<PdfPage> Pages { get; } = new();

    public bool IsModified { get; set; }

    public bool IsEncrypted { get; set; }

    public PdfMetadata Metadata { get; set; } = new();

    public int PageCount => Pages.Count;
}
