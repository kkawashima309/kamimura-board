using System.IO;
using Microsoft.Extensions.Logging;
using PdfStudio.Domain.Interfaces;
using PdfStudio.Domain.ValueObjects;

namespace PdfStudio.Infrastructure.Pdf;

/// <summary>
/// 複数PDFに同じ操作を一括適用するためのバッチサービス。
/// 結果サマリを返し、個別失敗があっても処理を継続する。
/// </summary>
public sealed class BatchService
{
    private readonly IPdfEditor _editor;
    private readonly ILogger<BatchService> _logger;

    public BatchService(IPdfEditor editor, ILogger<BatchService> logger)
    {
        _editor = editor;
        _logger = logger;
    }

    /// <summary>
    /// 複数PDFファイルにウォーターマークを一括追加する。
    /// </summary>
    /// <returns>(成功数, 失敗数, エラーリスト)</returns>
    public async Task<BatchResult> ApplyWatermarkAsync(
        IEnumerable<string> sourceFiles,
        string outputDirectory,
        WatermarkOptions options,
        string outputSuffix = "_watermark",
        CancellationToken ct = default)
    {
        return await ApplyBatchAsync(
            sourceFiles, outputDirectory, outputSuffix,
            (src, dst) => _editor.AddWatermarkAsync(src, dst, options, ct),
            "ウォーターマーク");
    }

    /// <summary>
    /// 複数PDFにページ番号を一括追加。
    /// </summary>
    public async Task<BatchResult> ApplyPageNumbersAsync(
        IEnumerable<string> sourceFiles,
        string outputDirectory,
        PageNumberOptions options,
        string outputSuffix = "_pagenum",
        CancellationToken ct = default)
    {
        return await ApplyBatchAsync(
            sourceFiles, outputDirectory, outputSuffix,
            (src, dst) => _editor.AddPageNumbersAsync(src, dst, options, ct),
            "ページ番号");
    }

    /// <summary>
    /// 複数PDFにヘッダー/フッターを一括追加。
    /// </summary>
    public async Task<BatchResult> ApplyHeaderFooterAsync(
        IEnumerable<string> sourceFiles,
        string outputDirectory,
        HeaderFooterOptions options,
        string outputSuffix = "_hf",
        CancellationToken ct = default)
    {
        return await ApplyBatchAsync(
            sourceFiles, outputDirectory, outputSuffix,
            (src, dst) => _editor.AddHeaderFooterAsync(src, dst, options, ct),
            "ヘッダー/フッター");
    }

    private async Task<BatchResult> ApplyBatchAsync(
        IEnumerable<string> sourceFiles,
        string outputDirectory,
        string outputSuffix,
        Func<string, string, Task> action,
        string operationName)
    {
        if (!Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        int success = 0, failure = 0;
        var errors = new List<string>();

        foreach (var src in sourceFiles)
        {
            try
            {
                var baseName = Path.GetFileNameWithoutExtension(src);
                var outFile = Path.Combine(outputDirectory, $"{baseName}{outputSuffix}.pdf");
                await action(src, outFile);
                success++;
            }
            catch (Exception ex)
            {
                failure++;
                errors.Add($"{Path.GetFileName(src)}: {ex.Message}");
                _logger.LogError(ex,
                    "{Op} 失敗: {File}", operationName, src);
            }
        }

        _logger.LogInformation(
            "{Op} 一括処理完了: 成功 {Success} / 失敗 {Failure}",
            operationName, success, failure);

        return new BatchResult(success, failure, errors);
    }
}

/// <summary>
/// バッチ処理結果。
/// </summary>
public sealed record BatchResult(int SuccessCount, int FailureCount, List<string> Errors)
{
    public int TotalCount => SuccessCount + FailureCount;
}
