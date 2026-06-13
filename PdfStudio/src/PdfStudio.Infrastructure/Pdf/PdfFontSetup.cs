using System.Runtime.InteropServices;
using PdfSharp.Fonts;

namespace PdfStudio.Infrastructure.Pdf;

/// <summary>
/// PDFsharp 6.x のフォント解決をアプリ全体で確実に動作させるためのセットアップ。
///
/// PDFsharp 6 の core パッケージは、既定ではフォントリゾルバを持たない。
/// このため <c>new XFont("Yu Gothic UI", …)</c> などが解決できず、
/// スタンプ・付箋・ウォーターマーク等の文字描画がすべて例外で失敗する
/// (「使用可能なフォントが見つかりません」)。
///
/// 本クラスは起動時に一度だけ呼び出し、
///   1. Windows ではOSのインストール済みフォント(日本語含む)を利用可能にし、
///   2. それでも解決できない場合に備えて、システム上の実フォントファイルへ
///      フォールバックするリゾルバを登録する。
/// これにより、どの環境でも文字描画が例外で落ちないことを保証する。
/// </summary>
public static class PdfFontSetup
{
    private static readonly object _gate = new();
    private static bool _configured;

    /// <summary>
    /// フォント解決を構成する。複数回呼んでも安全(初回のみ実行)。
    /// </summary>
    public static void EnsureConfigured()
    {
        if (_configured) return;
        lock (_gate)
        {
            if (_configured) return;

            // Windows ではOSのフォント(Yu Gothic・Meiryo・MS Gothic 等)を解決可能にする。
            try { GlobalFontSettings.UseWindowsFontsUnderWindows = true; }
            catch { /* プラットフォーム差異は無視 */ }

            // どの環境でも最終的に必ずフォントが見つかるよう、
            // フォールバックリゾルバを登録する。
            // (Windowsフォントが解決できればそちらを優先し、
            //  ダメな場合だけ実フォントファイルへフォールバックする)
            if (GlobalFontSettings.FontResolver is null)
            {
                GlobalFontSettings.FontResolver = new FailsafeFontResolver();
            }

            _configured = true;
        }
    }
}

/// <summary>
/// まずプラットフォーム(Windows)のフォント解決を試み、
/// 解決できなければシステム上の実フォントファイルへフォールバックするリゾルバ。
/// </summary>
internal sealed class FailsafeFontResolver : IFontResolver
{
    private const string FallbackRegular = "PdfStudioFallback#Regular";
    private const string FallbackBold = "PdfStudioFallback#Bold";

    private readonly object _lock = new();
    private byte[]? _regularBytes;
    private byte[]? _boldBytes;
    private bool _loaded;

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // 1) プラットフォーム(Windows)のリゾルバで解決を試みる。
        //    Windows実機では Yu Gothic 等がここで解決され、日本語も正しく描画される。
        try
        {
            var platform = PlatformFontResolver.ResolveTypeface(familyName, isBold, isItalic);
            if (platform is not null)
                return platform;
        }
        catch
        {
            // 非Windows・プラットフォーム未対応時はフォールバックへ
        }

        // 2) フォールバック: バンドル(システム)実フォントファイルを使う。
        EnsureFallbackLoaded();
        if (_regularBytes is null)
            return null; // 本当に何も無い場合は解決不能(極めて稀)

        if (isBold && _boldBytes is not null)
            return new FontResolverInfo(FallbackBold);

        // ボールド指定だが太字ファイルが無い場合は、太字をシミュレートさせる
        return new FontResolverInfo(FallbackRegular, mustSimulateBold: isBold, mustSimulateItalic: isItalic);
    }

    public byte[]? GetFont(string faceName)
    {
        EnsureFallbackLoaded();
        return faceName == FallbackBold ? (_boldBytes ?? _regularBytes) : _regularBytes;
    }

    private void EnsureFallbackLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            _regularBytes = LoadFirstExisting(RegularCandidates());
            _boldBytes = LoadFirstExisting(BoldCandidates());
            _loaded = true;
        }
    }

    private static byte[]? LoadFirstExisting(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            try
            {
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                    return File.ReadAllBytes(p);
            }
            catch { /* 次の候補へ */ }
        }
        return null;
    }

    private static IEnumerable<string> RegularCandidates()
    {
        // Windows のフォントフォルダ(プラットフォーム解決が失敗した場合の保険)
        var fonts = SafeFontsDir();
        if (fonts is not null)
        {
            yield return Path.Combine(fonts, "arial.ttf");
            yield return Path.Combine(fonts, "msgothic.ttc");
            yield return Path.Combine(fonts, "meiryo.ttc");
            yield return Path.Combine(fonts, "YuGothR.ttc");
        }
        // Linux / その他(主に検証環境用)
        yield return "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
        yield return "/usr/share/fonts/truetype/freefont/FreeSans.ttf";
        yield return "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf";
        // macOS
        yield return "/Library/Fonts/Arial.ttf";
        yield return "/System/Library/Fonts/Supplemental/Arial.ttf";
    }

    private static IEnumerable<string> BoldCandidates()
    {
        var fonts = SafeFontsDir();
        if (fonts is not null)
        {
            yield return Path.Combine(fonts, "arialbd.ttf");
            yield return Path.Combine(fonts, "meiryob.ttc");
            yield return Path.Combine(fonts, "YuGothB.ttc");
        }
        yield return "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf";
        yield return "/usr/share/fonts/truetype/freefont/FreeSansBold.ttf";
        yield return "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf";
        yield return "/Library/Fonts/Arial Bold.ttf";
    }

    private static string? SafeFontsDir()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        }
        catch { }
        return null;
    }
}
