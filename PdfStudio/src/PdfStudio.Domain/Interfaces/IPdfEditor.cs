using PdfStudio.Domain.Entities;
using PdfStudio.Domain.ValueObjects;

namespace PdfStudio.Domain.Interfaces;

/// <summary>
/// PDF編集機能の抽象。
/// </summary>
public interface IPdfEditor
{
    /// <summary>
    /// 現在のドキュメント状態を保存（上書き、または別名）。
    /// </summary>
    Task SaveAsync(
        PdfDocument document,
        string? newPath = null,
        CancellationToken ct = default);

    /// <summary>
    /// 複数PDFを1つに結合し、結合後のファイルパスを返す。
    /// </summary>
    Task<string> MergeAsync(
        IEnumerable<string> filePaths,
        string outputPath,
        CancellationToken ct = default);

    /// <summary>
    /// PDFを分割し、出力されたファイルパス一覧を返す。
    /// </summary>
    Task<IReadOnlyList<string>> SplitAsync(
        string filePath,
        SplitMode mode,
        string outputDirectory,
        IEnumerable<PageRange>? ranges = null,
        CancellationToken ct = default);

    /// <summary>
    /// 指定ページを回転（90度単位）。
    /// </summary>
    Task RotatePageAsync(
        PdfDocument document,
        int pageIndex,
        int degrees,
        CancellationToken ct = default);

    /// <summary>
    /// 指定ページを削除。
    /// </summary>
    Task DeletePageAsync(
        PdfDocument document,
        int pageIndex,
        CancellationToken ct = default);

    /// <summary>
    /// ページを並び替え。newOrderは元のIndexの新しい順序。
    /// </summary>
    Task ReorderPagesAsync(
        PdfDocument document,
        IList<int> newOrder,
        CancellationToken ct = default);

    // ========== v0.2 新機能 ==========

    /// <summary>
    /// 指定位置に別PDFファイルのページを挿入する。
    /// </summary>
    /// <param name="document">挿入先のPDFドキュメント</param>
    /// <param name="insertAtIndex">挿入位置(0=先頭、document.PageCount=末尾)</param>
    /// <param name="sourceFilePath">挿入元PDFのパス</param>
    /// <param name="sourcePageIndices">挿入する元PDFのページ番号一覧。nullの場合は全ページ</param>
    Task InsertPagesAsync(
        PdfDocument document,
        int insertAtIndex,
        string sourceFilePath,
        IEnumerable<int>? sourcePageIndices = null,
        CancellationToken ct = default);

    /// <summary>
    /// 指定ページを抽出して新しいPDFファイルとして保存。
    /// </summary>
    /// <param name="sourceFilePath">元PDFのパス</param>
    /// <param name="pageIndices">抽出するページ番号一覧</param>
    /// <param name="outputPath">出力PDFのパス</param>
    Task ExtractPagesAsync(
        string sourceFilePath,
        IEnumerable<int> pageIndices,
        string outputPath,
        CancellationToken ct = default);

    /// <summary>
    /// 指定位置に白紙ページを追加する。
    /// </summary>
    Task InsertBlankPageAsync(
        PdfDocument document,
        int insertAtIndex,
        BlankPageSize size,
        BlankPageOrientation orientation,
        CancellationToken ct = default);

    /// <summary>
    /// PDFのプロパティ(メタデータ)を更新する。
    /// </summary>
    Task UpdatePropertiesAsync(
        PdfDocument document,
        PdfDocumentProperties properties,
        CancellationToken ct = default);

    /// <summary>
    /// PDFのプロパティ(メタデータ)を取得する。
    /// </summary>
    Task<PdfDocumentProperties> GetPropertiesAsync(
        string filePath,
        CancellationToken ct = default);

    // ========== v0.4 新機能 ==========

    /// <summary>
    /// 全ページにウォーターマークを追加する。
    /// </summary>
    Task AddWatermarkAsync(
        string sourceFilePath,
        string outputPath,
        WatermarkOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// 全ページにページ番号を追加する。
    /// </summary>
    Task AddPageNumbersAsync(
        string sourceFilePath,
        string outputPath,
        PageNumberOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// ヘッダー/フッターを全ページに追加する。
    /// </summary>
    Task AddHeaderFooterAsync(
        string sourceFilePath,
        string outputPath,
        HeaderFooterOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// 指定ページを画像ファイルとしてエクスポートする。
    /// </summary>
    Task ExportPageAsImageAsync(
        string sourceFilePath,
        int pageIndex,
        string outputPath,
        ImageExportFormat format,
        int dpi = 150,
        CancellationToken ct = default);
}

/// <summary>
/// 画像エクスポート形式。
/// </summary>
public enum ImageExportFormat
{
    Png,
    Jpeg,
}
