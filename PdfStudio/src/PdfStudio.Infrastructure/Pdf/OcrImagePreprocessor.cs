using SkiaSharp;

namespace PdfStudio.Infrastructure.Pdf;

/// <summary>
/// OCR前の画像前処理。
/// 実証済みPythonスクリプト(auto_order_import.py)のノウハウをC#に移植。
///
/// strong: グレースケール → コントラスト強調 → メディアンフィルター(3x3) → シャープ → 二値化(閾値175)
/// light : グレースケール → 軽いコントラスト強調(二値化なし)
///
/// 高速化のため Pixels(SKColor[]) で一括取得し、結果は RGBA8888 で再構成する
/// (Gray8 への直接書き込みは SkiaSharp の一部バージョンで不具合があるため避ける)。
/// </summary>
internal static class OcrImagePreprocessor
{
    public static byte[] PreprocessStrong(byte[] pngBytes)
    {
        using var original = SKBitmap.Decode(pngBytes);
        if (original == null) return pngBytes;

        var (gray, w, h) = ToGrayscaleArray(original);
        ApplyContrast(gray, 1.5f);
        ApplyMedianFilter(gray, w, h);
        ApplySharpen(gray, w, h);
        Binarize(gray, 175);

        return EncodeGray(gray, w, h);
    }

    public static byte[] PreprocessLight(byte[] pngBytes)
    {
        using var original = SKBitmap.Decode(pngBytes);
        if (original == null) return pngBytes;

        var (gray, w, h) = ToGrayscaleArray(original);
        ApplyContrast(gray, 1.3f);

        return EncodeGray(gray, w, h);
    }

    private static (byte[] data, int w, int h) ToGrayscaleArray(SKBitmap src)
    {
        int w = src.Width, h = src.Height;
        var gray = new byte[w * h];
        var pixels = src.Pixels;  // SKColor[]
        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            gray[i] = (byte)((c.Red * 299 + c.Green * 587 + c.Blue * 114) / 1000);
        }
        return (gray, w, h);
    }

    private static void ApplyContrast(byte[] gray, float factor)
    {
        long sum = 0;
        for (int i = 0; i < gray.Length; i++) sum += gray[i];
        byte mean = (byte)(sum / gray.Length);

        for (int i = 0; i < gray.Length; i++)
        {
            int v = (int)(mean + (gray[i] - mean) * factor);
            gray[i] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
        }
    }

    /// <summary>
    /// 3x3 メディアンフィルター。スキャン特有のごま塩ノイズを、
    /// 後段のシャープ処理がエッジとして増幅する前に均す。
    /// </summary>
    private static void ApplyMedianFilter(byte[] gray, int w, int h)
    {
        var src = (byte[])gray.Clone();
        var window = new byte[9];
        for (int y = 1; y < h - 1; y++)
        {
            int row = y * w;
            for (int x = 1; x < w - 1; x++)
            {
                int idx = row + x;
                int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int r = idx + dy * w;
                    window[n++] = src[r - 1];
                    window[n++] = src[r];
                    window[n++] = src[r + 1];
                }
                Array.Sort(window);
                gray[idx] = window[4];
            }
        }
    }

    private static void ApplySharpen(byte[] gray, int w, int h)
    {
        var src = (byte[])gray.Clone();
        for (int y = 1; y < h - 1; y++)
        {
            int row = y * w;
            for (int x = 1; x < w - 1; x++)
            {
                int idx = row + x;
                int v = 5 * src[idx]
                        - src[idx - w] - src[idx + w]
                        - src[idx - 1] - src[idx + 1];
                gray[idx] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
            }
        }
    }

    private static void Binarize(byte[] gray, byte threshold)
    {
        for (int i = 0; i < gray.Length; i++)
            gray[i] = gray[i] > threshold ? (byte)255 : (byte)0;
    }

    /// <summary>
    /// グレースケール配列を RGBA8888 の画像として PNG エンコードする。
    /// R=G=B=輝度, A=255。byte[] を直接渡す FromPixelCopy で内部コピーされるため安全
    /// (ポインタのピン留めが不要)。
    /// </summary>
    private static byte[] EncodeGray(byte[] gray, int w, int h)
    {
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < gray.Length; i++)
        {
            byte v = gray[i];
            int o = i * 4;
            rgba[o] = v;       // R
            rgba[o + 1] = v;   // G
            rgba[o + 2] = v;   // B
            rgba[o + 3] = 255; // A
        }

        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var image = SKImage.FromPixelCopy(info, rgba);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
