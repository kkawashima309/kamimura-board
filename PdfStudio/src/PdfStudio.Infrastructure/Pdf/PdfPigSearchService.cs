using System.IO;
using Microsoft.Extensions.Logging;
using PdfStudio.Domain.Interfaces;
using PdfStudio.Domain.ValueObjects;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace PdfStudio.Infrastructure.Pdf;

/// <summary>
/// PdfPig を使用したPDFテキスト検索の実装。
/// </summary>
public sealed class PdfPigSearchService : IPdfSearchService
{
    private readonly ILogger<PdfPigSearchService> _logger;

    public PdfPigSearchService(ILogger<PdfPigSearchService> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<PdfSearchResult>> SearchAsync(
        string filePath,
        string query,
        bool caseSensitive = false,
        string? password = null,
        CancellationToken ct = default)
    {
        return Task.Run<IReadOnlyList<PdfSearchResult>>(() =>
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("PDFファイルが見つかりません。", filePath);
            if (string.IsNullOrEmpty(query))
                return Array.Empty<PdfSearchResult>();

            var results = new List<PdfSearchResult>();
            var comparison = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            // PdfPig でPDFを開く
            var parsingOptions = string.IsNullOrEmpty(password)
                ? new ParsingOptions { UseLenientParsing = true }
                : new ParsingOptions { Password = password, UseLenientParsing = true };

            using var document = PdfDocument.Open(filePath, parsingOptions);

            for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var page = document.GetPage(pageNum);
                    // ContentOrderTextExtractor を使うと、PDF内のテキスト順序を尊重した抽出ができる
                    var text = ContentOrderTextExtractor.GetText(page) ?? string.Empty;

                    // ページ内で複数マッチを検出
                    int idx = 0;
                    while (idx <= text.Length - query.Length)
                    {
                        int found = text.IndexOf(query, idx, comparison);
                        if (found < 0) break;

                        // スニペット作成(前後20文字)
                        int snippetStart = Math.Max(0, found - 20);
                        int snippetLen = Math.Min(text.Length - snippetStart, query.Length + 40);
                        var snippet = text.Substring(snippetStart, snippetLen)
                            .Replace("\r", " ")
                            .Replace("\n", " ")
                            .Replace("\t", " ");
                        // 連続スペース整形
                        while (snippet.Contains("  ")) snippet = snippet.Replace("  ", " ");

                        results.Add(new PdfSearchResult
                        {
                            PageIndex = pageNum - 1,  // 0-based
                            CharOffset = found,
                            MatchedText = text.Substring(found, query.Length),
                            ContextSnippet = snippet.Trim(),
                        });

                        idx = found + query.Length;
                    }
                }
                catch (Exception ex)
                {
                    // 個別ページのエラーは警告レベル(全体検索は続行)
                    _logger.LogWarning(ex,
                        "ページ {Page} のテキスト抽出に失敗。スキップします。",
                        pageNum);
                }
            }

            _logger.LogInformation(
                "検索完了: {File} で '{Query}' を {Count} 件発見",
                Path.GetFileName(filePath), query, results.Count);

            return results;
        }, ct);
    }
}
