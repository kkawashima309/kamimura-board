namespace PdfStudio.Domain.ValueObjects;

/// <summary>
/// PDFテキスト検索の結果。
/// </summary>
public sealed record PdfSearchResult
{
    /// <summary>マッチしたページ番号(0始まり)</summary>
    public int PageIndex { get; init; }

    /// <summary>マッチした位置(ページ内のテキストでの文字オフセット)</summary>
    public int CharOffset { get; init; }

    /// <summary>マッチしたテキスト(検索ワードそのもの)</summary>
    public string MatchedText { get; init; } = string.Empty;

    /// <summary>マッチ周辺のスニペット(前後20文字程度)</summary>
    public string ContextSnippet { get; init; } = string.Empty;

    /// <summary>
    /// マッチした語(複数語の場合は語ごと)のページ内ハイライト矩形。
    /// 座標取得に失敗した場合は空。
    /// </summary>
    public IReadOnlyList<NormalizedRect> Highlights { get; init; } = Array.Empty<NormalizedRect>();
}
