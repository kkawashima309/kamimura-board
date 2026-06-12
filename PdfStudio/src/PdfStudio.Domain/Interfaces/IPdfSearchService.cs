using PdfStudio.Domain.ValueObjects;

namespace PdfStudio.Domain.Interfaces;

/// <summary>
/// PDFのテキスト検索サービス。
/// </summary>
public interface IPdfSearchService
{
    /// <summary>
    /// PDFファイル内で指定文字列を検索する。
    /// </summary>
    /// <param name="filePath">PDFファイルパス</param>
    /// <param name="query">検索文字列</param>
    /// <param name="caseSensitive">大文字小文字を区別するか</param>
    /// <param name="password">必要な場合のパスワード</param>
    /// <returns>マッチした結果一覧(ページ順)</returns>
    Task<IReadOnlyList<PdfSearchResult>> SearchAsync(
        string filePath,
        string query,
        bool caseSensitive = false,
        string? password = null,
        CancellationToken ct = default);
}
