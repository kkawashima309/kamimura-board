namespace PdfStudio.Domain.ValueObjects;

/// <summary>
/// PDF分割モード。
/// </summary>
public enum SplitMode
{
    /// <summary>1ページごとに分割。</summary>
    EachPage,

    /// <summary>奇数ページ・偶数ページで分割。</summary>
    OddEven,

    /// <summary>指定範囲で分割。</summary>
    ByRange
}

/// <summary>
/// 分割範囲指定（ByRange用）。
/// </summary>
public record PageRange(int StartPage, int EndPage)
{
    public bool IsValid => StartPage >= 0 && EndPage >= StartPage;
}
