using System.IO;
using Microsoft.Extensions.Logging;
using PdfStudio.Domain.Interfaces;
using TesseractOCR;
using TesseractOCR.Enums;

namespace PdfStudio.Infrastructure.Pdf;

/// <summary>
/// TesseractOCR を使用した PDF OCR の実装。
///
/// 実証済みPythonスクリプト(auto_order_import.py)のノウハウを移植:
///   - 400 DPI でラスタライズ(従来300→向上)
///   - 二値化を含む前処理(OcrImagePreprocessor)
///   - PSM=SingleBlock(単一テキストブロック)、OEM=LstmOnly
///   - 複数言語 jpn+eng(数字・英字の品番を正しく認識)
///
/// Tesseract 内蔵 PDF レンダラーで「検索可能PDF」を生成する。
/// </summary>
public sealed class TesseractOcrService : IPdfOcrService
{
    private readonly ILogger<TesseractOcrService> _logger;
    private readonly string _tessdataPath;
    private static readonly object PdfiumLock = new();

    private const int OcrDpi = 400;

    public TesseractOcrService(ILogger<TesseractOcrService> logger)
    {
        _logger = logger;

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "tools", "tessdata"),
            Path.Combine(baseDir, "tessdata"),
            Path.Combine(baseDir, "..", "tools", "tessdata"),
        };

        _tessdataPath = candidates.FirstOrDefault(Directory.Exists)
            ?? Path.Combine(baseDir, "tools", "tessdata");

        _logger.LogInformation("Tesseract tessdataパス: {Path}", _tessdataPath);
    }

    public Task PerformOcrAsync(
        string sourceFilePath,
        string outputPath,
        string language = "jpn+eng",
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException("元PDFが見つかりません。", sourceFilePath);

            if (!Directory.Exists(_tessdataPath))
            {
                throw new DirectoryNotFoundException(
                    "Tesseractの言語データフォルダが見つかりません。\n" +
                    $"期待するパス: {_tessdataPath}\n" +
                    "インストール先の tools\\tessdata フォルダに言語ファイル(.traineddata)を配置してください。");
            }

            var langCodes = language.Split('+', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().TrimStart('~'))
                .Where(s => s.Length > 0)
                .ToList();
            if (langCodes.Count == 0) langCodes.Add("jpn");

            foreach (var code in langCodes)
            {
                var traineddata = Path.Combine(_tessdataPath, $"{code}.traineddata");
                if (!File.Exists(traineddata))
                {
                    throw new FileNotFoundException(
                        $"言語データファイルが見つかりません: {code}.traineddata\n" +
                        "https://github.com/tesseract-ocr/tessdata から入手し、\n" +
                        $"次のフォルダに配置してください: {_tessdataPath}");
                }
            }

            var normalizedLang = string.Join("+", langCodes);
            _logger.LogInformation(
                "OCR開始: 元={Src}, 言語={Lang}, DPI={Dpi}",
                sourceFilePath, normalizedLang, OcrDpi);

            var bytes = File.ReadAllBytes(sourceFilePath);
            int pageCount;
            lock (PdfiumLock)
            {
                pageCount = PDFtoImage.Conversion.GetPageCount(bytes);
            }

            var outputNoExt = outputPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                ? outputPath.Substring(0, outputPath.Length - 4)
                : outputPath;

            var outDir = Path.GetDirectoryName(outputNoExt);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            using var engine = CreateEngine(normalizedLang);
            // PSM=SingleBlock: 単一の均一なテキストブロックとみなす(帳票・表で有効)
            engine.DefaultPageSegMode = PageSegMode.SingleBlock;

            using (var renderer = TesseractOCR.Renderers.Result.CreatePdfRenderer(
                outputNoExt, _tessdataPath, false))
            using (renderer.BeginDocument("PdfStudio OCR"))
            {
                for (int i = 0; i < pageCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    _logger.LogInformation("OCR処理中: ページ {Page}/{Total}", i + 1, pageCount);

                    // 400 DPI でラスタライズ
                    byte[] rawPng;
                    lock (PdfiumLock)
                    {
                        using var bitmap = PDFtoImage.Conversion.ToImage(
                            bytes, page: i,
                            options: new PDFtoImage.RenderOptions(Dpi: OcrDpi));
                        using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                        rawPng = data.ToArray();
                    }

                    // 前処理(strong: 二値化あり)を適用
                    byte[] processedPng;
                    try
                    {
                        processedPng = OcrImagePreprocessor.PreprocessStrong(rawPng);
                    }
                    catch (Exception preEx)
                    {
                        _logger.LogWarning(preEx,
                            "ページ {Page} の前処理に失敗、生画像でOCRします", i + 1);
                        processedPng = rawPng;
                    }

                    using var img = TesseractOCR.Pix.Image.LoadFromMemory(processedPng);
                    using var page = engine.Process(img, "PdfStudio OCR");
                    renderer.AddPage(page);

                    _logger.LogInformation(
                        "ページ {Page} OCR完了 (信頼度 {Conf:F1}%)",
                        i + 1, page.MeanConfidence * 100);
                }
            }

            var generatedPdf = outputNoExt + ".pdf";
            if (!File.Exists(generatedPdf))
                throw new FileNotFoundException("OCR後のPDFが生成されませんでした。", generatedPdf);

            if (!string.Equals(Path.GetFullPath(generatedPdf), Path.GetFullPath(outputPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
                File.Move(generatedPdf, outputPath);
            }

            _logger.LogInformation("OCR完了: 出力={Out} ({Count}ページ)", outputPath, pageCount);
        }, ct);
    }

    /// <summary>
    /// Engine を生成する。TesseractOCR が提供する文字列言語指定の
    /// コンストラクタ(jpn+eng のような複数言語指定をそのまま渡せる)を直接使用し、
    /// OEM は LstmOnly を明示指定する。
    /// </summary>
    private Engine CreateEngine(string normalizedLang)
    {
        return new Engine(_tessdataPath, normalizedLang, EngineMode.LstmOnly);
    }

    public IReadOnlyList<string> GetAvailableLanguages()
    {
        if (!Directory.Exists(_tessdataPath))
            return Array.Empty<string>();

        return Directory.GetFiles(_tessdataPath, "*.traineddata")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .ToList();
    }
}
