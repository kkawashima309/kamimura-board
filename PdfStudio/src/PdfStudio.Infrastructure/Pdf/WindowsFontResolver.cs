using System.IO;
using System.Reflection;
using PdfSharp.Fonts;

namespace PdfStudio.Infrastructure.Pdf;

/// <summary>
/// 同梱した Noto Sans JP フォントを使う FontResolver。
///
/// PDFsharp 6.x は .ttc(TrueType Collection)を扱えないため、
/// Windows標準の日本語フォント(游ゴシック等、いずれも.ttc)をそのまま使うと失敗する。
/// そこで .otf 形式の Noto Sans JP をアプリに同梱し、それを使うことで
/// 環境に依存せず確実に日本語を描画できるようにする。
///
/// フォント探索の優先順:
///  (1) 同梱フォント(アプリフォルダの Resources/Fonts/NotoSansJP-Regular.ttf)
///  (2) Windowsの .ttf 単体フォント(.ttcは除外)
/// </summary>
public sealed class WindowsFontResolver : IFontResolver
{
    private const string FaceName = "NotoSansJP";
    private static byte[]? _fontData;
    private static readonly object _lock = new();
    private static bool _initialized;

    public static string DefaultFaceName { get; private set; } = "";
    public static bool HasUsableFont => _fontData != null;
    public static string DiagnosticInfo { get; private set; } = "";

    public static void Register()
    {
        lock (_lock)
        {
            if (_initialized) return;

            // (1) アセンブリ埋め込みフォントを最優先で使う。
            //     ディスク配置に依存しないため、exe単体を別フォルダへ移動しても確実に動作する。
            var loaded = TryLoadEmbeddedFont();

            // (2) 念のため実行フォルダの同梱フォントも探す
            if (!loaded)
            {
                loaded = TryLoadBundledFont();
            }

            // (3) それも無ければ Windows の .ttf 単体フォントを探す(.ttcは除外)
            if (!loaded)
            {
                loaded = TryLoadWindowsTtf();
            }

            if (_fontData != null)
            {
                DefaultFaceName = FaceName;
            }

            GlobalFontSettings.FontResolver = new WindowsFontResolver();
            _initialized = true;
        }
    }

    private static bool TryLoadEmbeddedFont()
    {
        try
        {
            const string resourceName = "PdfStudio.Infrastructure.Fonts.NotoSansJP-Regular.ttf";
            var asm = typeof(WindowsFontResolver).Assembly;
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                DiagnosticInfo = $"埋め込みフォントが見つからない: {resourceName} / "
                    + "利用可能リソース=[" + string.Join(", ", asm.GetManifestResourceNames()) + "]";
                return false;
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            _fontData = ms.ToArray();
            DiagnosticInfo = $"埋め込みフォント使用: {resourceName} ({_fontData.Length}B)";
            return _fontData.Length > 0;
        }
        catch (Exception ex)
        {
            DiagnosticInfo = $"埋め込みフォント読込失敗: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static bool TryLoadBundledFont()
    {
        try
        {
            // アプリ実行フォルダからの相対パス
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "Resources", "Fonts", "NotoSansJP-Regular.ttf"),
                Path.Combine(baseDir, "Fonts", "NotoSansJP-Regular.ttf"),
                Path.Combine(baseDir, "NotoSansJP-Regular.ttf"),
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    _fontData = File.ReadAllBytes(path);
                    DiagnosticInfo = $"同梱フォント使用: {path} ({_fontData.Length}B)";
                    return true;
                }
            }

            DiagnosticInfo = "同梱フォントが見つからない: " + string.Join(" / ", candidates);
            return false;
        }
        catch (Exception ex)
        {
            DiagnosticInfo = $"同梱フォント読込失敗: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static bool TryLoadWindowsTtf()
    {
        try
        {
            var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (string.IsNullOrEmpty(fontsDir) || !Directory.Exists(fontsDir))
                fontsDir = @"C:\Windows\Fonts";

            // .ttf 単体で存在しうる日本語対応フォント(.ttcは PDFsharp が扱えないため除外)
            // BIZ UDゴシック等の一部に .ttf 版があるが環境依存。Arial系は日本語不可。
            // ここでは数少ない .ttf 日本語フォントを試す。
            var ttfCandidates = new[]
            {
                "YuGothR.ttf", "YuGothM.ttf",
                "meiryo.ttf",
                "BIZ-UDGothicR.ttf",
            };

            foreach (var name in ttfCandidates)
            {
                var path = Path.Combine(fontsDir, name);
                if (File.Exists(path))
                {
                    _fontData = File.ReadAllBytes(path);
                    DiagnosticInfo = $"Windows .ttf使用: {path}";
                    return true;
                }
            }

            DiagnosticInfo += " / Windows .ttf も無し";
            return false;
        }
        catch (Exception ex)
        {
            DiagnosticInfo += $" / Windows .ttf探索失敗: {ex.Message}";
            return false;
        }
    }

    public byte[]? GetFont(string faceName) => _fontData;

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // どのフォント名で要求されても、確保した唯一のフォントを返す
        if (_fontData != null)
            return new FontResolverInfo(FaceName);
        return null;
    }
}
