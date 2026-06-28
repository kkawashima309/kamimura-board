namespace PdfStudio.Domain.Interfaces;

/// <summary>
/// PDFのOCR(光学文字認識)サービス。
/// スキャンPDFをテキスト検索可能な形式に変換する。
/// </summary>
public interface IPdfOcrService
{
    /// <summary>
    /// PDFにOCR処理を施し、テキスト層を追加した新しいPDFを生成する。
    /// </summary>
    /// <param name="sourceFilePath">元PDFのパス</param>
    /// <param name="outputPath">出力PDFのパス</param>
    /// <param name="language">OCR言語コード(例: "jpn", "eng", "jpn+eng")</param>
    Task PerformOcrAsync(
        string sourceFilePath,
        string outputPath,
        string language = "jpn+eng",
        CancellationToken ct = default);

    /// <summary>
    /// 使用可能な言語データのリストを取得する。
    /// </summary>
    IReadOnlyList<string> GetAvailableLanguages();
}
