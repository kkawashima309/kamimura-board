namespace PdfStudio.Domain.ValueObjects;

/// <summary>
/// PDFの権限設定。
/// </summary>
public record PdfPermissions(
    bool AllowPrint = true,
    bool AllowCopy = true,
    bool AllowEdit = true,
    bool AllowAnnotations = true,
    bool AllowFormFilling = true,
    bool AllowAssembly = true)
{
    public static PdfPermissions ReadOnly => new(
        AllowPrint: true,
        AllowCopy: false,
        AllowEdit: false,
        AllowAnnotations: false,
        AllowFormFilling: false,
        AllowAssembly: false);

    public static PdfPermissions FullAccess => new();
}
