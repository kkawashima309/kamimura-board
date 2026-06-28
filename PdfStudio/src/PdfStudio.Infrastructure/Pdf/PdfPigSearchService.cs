using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using PdfStudio.Domain.Interfaces;
using PdfStudio.Domain.ValueObjects;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

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

            // OCR結果は単語間に半角スペースが入る(「オー クマ」等)ため、
            // 検索クエリ側・抽出テキスト側の双方からスペースを除去して比較する。
            var compactQuery = new string(query.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (compactQuery.Length == 0)
                return Array.Empty<PdfSearchResult>();

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

                    // 単語単位で抽出し、座標(BoundingBox)を保持したまま検索する。
                    // ContentOrderTextExtractor はプレーンテキストのみを返し座標情報を捨ててしまうため、
                    // ハイライト矩形が必要な検索には単語抽出を使う。
                    var words = page.GetWords().ToList();
                    if (words.Count == 0) continue;

                    // 単語を半角スペースで連結したテキストと、各単語が占める文字範囲の対応表を作る
                    var sb = new StringBuilder();
                    var wordRanges = new List<(int Start, int End, Word Word)>(words.Count);
                    foreach (var w in words)
                    {
                        if (sb.Length > 0) sb.Append(' ');
                        int start = sb.Length;
                        sb.Append(w.Text);
                        wordRanges.Add((start, sb.Length, w));
                    }
                    var text = sb.ToString();

                    // スペースを除去した照合用テキストと、各文字が text 側で何文字目に
                    // 対応するかのマップを作る(ハイライト座標は text 側のオフセットで扱うため)。
                    var compactSb = new StringBuilder(text.Length);
                    var compactToOriginal = new List<int>(text.Length);
                    for (int i = 0; i < text.Length; i++)
                    {
                        if (!char.IsWhiteSpace(text[i]))
                        {
                            compactSb.Append(text[i]);
                            compactToOriginal.Add(i);
                        }
                    }
                    var compactText = compactSb.ToString();

                    int idx = 0;
                    while (idx <= compactText.Length - compactQuery.Length)
                    {
                        int foundCompact = compactText.IndexOf(compactQuery, idx, comparison);
                        if (foundCompact < 0) break;
                        int matchCompactEnd = foundCompact + compactQuery.Length;

                        int found = compactToOriginal[foundCompact];
                        int matchEnd = compactToOriginal[matchCompactEnd - 1] + 1;
                        int matchLen = matchEnd - found;

                        // スニペット作成(前後20文字)
                        int snippetStart = Math.Max(0, found - 20);
                        int snippetLen = Math.Min(text.Length - snippetStart, matchLen + 40);
                        var snippet = text.Substring(snippetStart, snippetLen)
                            .Replace("\r", " ")
                            .Replace("\n", " ")
                            .Replace("\t", " ");
                        while (snippet.Contains("  ")) snippet = snippet.Replace("  ", " ");

                        // マッチ範囲に重なる単語のバウンディングボックスをハイライト矩形に変換
                        var highlights = new List<NormalizedRect>();
                        foreach (var (start, end, word) in wordRanges)
                        {
                            if (start < matchEnd && end > found)
                            {
                                var rect = ToNormalizedRect(GetLooseBoundingBox(word), page.Width, page.Height);
                                if (rect != null) highlights.Add(rect);
                            }
                        }

                        results.Add(new PdfSearchResult
                        {
                            PageIndex = pageNum - 1,  // 0-based
                            CharOffset = found,
                            MatchedText = text.Substring(found, matchLen),
                            ContextSnippet = snippet.Trim(),
                            Highlights = highlights,
                        });

                        idx = foundCompact + compactQuery.Length;
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

    /// <summary>
    /// 単語のハイライト矩形を、フォントのアセント/ディセント(行の高さ)基準で計算する。
    /// <see cref="Word.BoundingBox"/>はグリフの実アウトラインに基づくため、Tesseract等が
    /// 生成する不可視OCRテキスト層(実際の字形を持たない代替フォントを使う)では文字位置と
    /// 一致しない矩形になることがある。<see cref="Letter.GlyphRectangleLoose"/>はフォントが
    /// 宣言するアセント/ディセントに基づくため、字形に依存せず一貫した位置が得られる。
    /// </summary>
    private static PdfRectangle GetLooseBoundingBox(Word word)
    {
        double left = double.MaxValue, right = double.MinValue;
        double bottom = double.MaxValue, top = double.MinValue;

        foreach (var letter in word.Letters)
        {
            var box = letter.GlyphRectangleLoose;
            if (box.Left < left) left = box.Left;
            if (box.Right > right) right = box.Right;
            if (box.Bottom < bottom) bottom = box.Bottom;
            if (box.Top > top) top = box.Top;
        }

        return new PdfRectangle(left, bottom, right, top);
    }

    /// <summary>
    /// PdfPig の PDF座標(左下原点、ポイント単位)を、ページ比率(左上原点、0.0〜1.0)に変換する。
    /// </summary>
    private static NormalizedRect? ToNormalizedRect(PdfRectangle box, double pageWidth, double pageHeight)
    {
        if (pageWidth <= 0 || pageHeight <= 0) return null;

        var left = box.Left / pageWidth;
        var width = box.Width / pageWidth;
        var top = (pageHeight - box.Top) / pageHeight;
        var height = box.Height / pageHeight;

        return new NormalizedRect
        {
            Left = Math.Clamp(left, 0.0, 1.0),
            Top = Math.Clamp(top, 0.0, 1.0),
            Width = Math.Clamp(width, 0.0, 1.0),
            Height = Math.Clamp(height, 0.0, 1.0),
        };
    }
}
