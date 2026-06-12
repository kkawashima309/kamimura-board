using PdfStudio.Domain.ValueObjects;

namespace PdfStudio.Domain.Interfaces;

/// <summary>
/// PDF注釈管理サービス。
/// </summary>
public interface IPdfAnnotationService
{
    /// <summary>
    /// PDFに注釈を追加して保存する。
    /// </summary>
    Task AddAnnotationAsync(
        string sourceFilePath,
        string outputPath,
        AnnotationOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// 複数注釈を一括追加する。
    /// </summary>
    Task AddAnnotationsAsync(
        string sourceFilePath,
        string outputPath,
        IEnumerable<AnnotationOptions> annotations,
        CancellationToken ct = default);
}
