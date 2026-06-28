using PdfSharp.Drawing;

namespace PdfStudio.Infrastructure.Pdf;

/// <summary>
/// 日本語テキストを描画するためのフォントヘルパー。
/// WindowsFontResolver が同梱フォント(Noto Sans JP)を登録しているので、
/// その既定フェイス名で XFont を生成する。
/// </summary>
internal static class FontHelper
{
    public static string? LastFailureDetail { get; private set; }

    /// <summary>
    /// 日本語対応フォントで XFont を生成する。
    /// </summary>
    public static XFont Create(double size, bool bold = false)
    {
        var style = bold ? XFontStyleEx.Bold : XFontStyleEx.Regular;

        // FontResolver が確保したフェイス名を使う
        var face = WindowsFontResolver.DefaultFaceName;
        if (!string.IsNullOrEmpty(face))
        {
            try
            {
                return new XFont(face, size, style);
            }
            catch (Exception ex)
            {
                LastFailureDetail = $"{face}: {ex.GetType().Name}: {ex.Message}";
                // Regular で再試行
                try
                {
                    return new XFont(face, size, XFontStyleEx.Regular);
                }
                catch (Exception ex2)
                {
                    LastFailureDetail += $" | Regular: {ex2.Message}";
                }
            }
        }

        throw new InvalidOperationException(
            "日本語フォントを初期化できませんでした。詳細: "
            + (LastFailureDetail ?? "FontResolver未登録")
            + " / " + WindowsFontResolver.DiagnosticInfo);
    }
}
