using PdfSharp.Drawing;

namespace PdfStudio.Infrastructure.Pdf;

/// <summary>
/// 日本語テキストを安全に描画するためのフォントヘルパー。
///
/// 重要: Arial 等の欧文フォントは日本語グリフを持たないため、
/// 「承認」「社外秘」などの日本語を描画すると PDFsharp が例外を投げる。
/// このヘルパーは日本語対応フォントを優先順に試行して XFont を生成する。
/// </summary>
internal static class FontHelper
{
    /// <summary>
    /// 日本語対応フォントの候補(優先順)。
    /// Yu Gothic UI: Windows 10/11 標準
    /// Meiryo: Windows Vista 以降
    /// MS Gothic: Windows XP 以降(最終フォールバック)
    /// </summary>
    private static readonly string[] Candidates =
    {
        "Yu Gothic UI",
        "Meiryo UI",
        "Meiryo",
        "MS Gothic",
        "MS UI Gothic",
        "Arial",
    };

    /// <summary>
    /// 日本語対応フォントを生成する。候補を順に試し、失敗したら次へ。
    /// </summary>
    public static XFont Create(double size, bool bold = false)
    {
        var style = bold ? XFontStyleEx.Bold : XFontStyleEx.Regular;

        foreach (var name in Candidates)
        {
            try
            {
                return new XFont(name, size, style);
            }
            catch
            {
                // このフォントが見つからない/スタイル非対応 → 次の候補へ
            }
        }

        // Bold が全滅した場合は Regular で再試行
        if (bold)
        {
            foreach (var name in Candidates)
            {
                try
                {
                    return new XFont(name, size, XFontStyleEx.Regular);
                }
                catch
                {
                }
            }
        }

        throw new InvalidOperationException(
            "使用可能なフォントが見つかりません。Windows のフォント設定を確認してください。");
    }
}
