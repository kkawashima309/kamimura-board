using System.IO;
using System.Collections.Concurrent;
using System.Drawing;
using Microsoft.Extensions.Logging;
using PdfStudio.Domain.Entities;
using PdfStudio.Domain.Interfaces;
using PDFtoImage;
using SkiaSharp;

namespace PdfStudio.Infrastructure.Pdf;

/// <summary>
/// PDFium (PDFtoImage ラッパー経由) を使用したPDFレンダラー実装。
///
/// 重要: PDFium ネイティブライブラリはスレッドセーフではないため、
/// このクラス内のすべての PDFium 呼び出しは _pdfiumLock により直列化される。
/// 並列で複数の Task.Run から呼び出されると例外や予期せぬ動作の原因となる。
/// </summary>
public sealed class PdfiumRenderer : IPdfRenderer
{
    private readonly ILogger<PdfiumRenderer> _logger;

    /// <summary>
    /// 開いているドキュメントのファイル内容をメモリにキャッシュ。
    /// </summary>
    private readonly ConcurrentDictionary<Guid, byte[]> _docBytes = new();

    private readonly ConcurrentDictionary<Guid, string?> _docPasswords = new();

    /// <summary>
    /// 開いている Domain ドキュメントの参照を保持。
    /// レンダリング時に各ページの Rotation を参照するために必要。
    /// </summary>
    private readonly ConcurrentDictionary<Guid, PdfDocument> _docs = new();

    /// <summary>
    /// PDFium API 呼び出しの直列化用ロック。
    /// PDFium は内部的にスレッドセーフではないため、必ず1つの呼び出しずつ処理する。
    /// </summary>
    private static readonly object _pdfiumLock = new();

    public PdfiumRenderer(ILogger<PdfiumRenderer> logger)
    {
        _logger = logger;
    }

    public Task<PdfDocument> LoadAsync(
        string filePath,
        string? password = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException("PDFファイルが見つかりません。", filePath);

                var bytes = File.ReadAllBytes(filePath);

                int pageCount;
                IList<SizeF>? sizes = null;

                // PDFium 呼び出しは全てロック内で実行する
                lock (_pdfiumLock)
                {
                    // ページ数取得
                    try
                    {
                        pageCount = Conversion.GetPageCount(bytes, password: password);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "ページ数の取得に失敗: {Path}", filePath);
                        throw new InvalidOperationException(
                            "PDFのページ数を取得できませんでした。ファイルが破損しているか、サポートされていない形式の可能性があります。", ex);
                    }

                    if (pageCount <= 0)
                        throw new InvalidOperationException("PDFのページ数が0です。空のファイルか、破損している可能性があります。");

                    // ページサイズ取得は失敗しても致命的ではない
                    try
                    {
                        var sizesResult = Conversion.GetPageSizes(bytes, password: password);
                        sizes = sizesResult as IList<SizeF> ?? sizesResult.ToList();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "PDFのページサイズ取得に失敗しました。デフォルトサイズ(A4)を使用します: {Path}",
                            filePath);
                        sizes = null;
                    }
                }

                var doc = new PdfDocument
                {
                    FilePath = filePath,
                    IsEncrypted = !string.IsNullOrEmpty(password),
                };

                // A4縦のポイント単位サイズ
                const float fallbackWidth = 595.27f;
                const float fallbackHeight = 841.89f;

                for (int i = 0; i < pageCount; i++)
                {
                    float w, h;
                    if (sizes != null && i < sizes.Count)
                    {
                        w = sizes[i].Width;
                        h = sizes[i].Height;
                    }
                    else
                    {
                        w = fallbackWidth;
                        h = fallbackHeight;
                    }
                    doc.Pages.Add(new PdfPage
                    {
                        Index = i,
                        Width = w,
                        Height = h,
                        Rotation = 0,
                    });
                }

                _docBytes[doc.Id] = bytes;
                _docPasswords[doc.Id] = password;
                _docs[doc.Id] = doc;

                _logger.LogInformation(
                    "PDFを読み込みました: {Path} ({Count}ページ, サイズ取得: {SizesOk})",
                    filePath, pageCount, sizes != null);

                return doc;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PDFの読み込みに失敗: {Path}", filePath);
                throw;
            }
        }, ct);
    }

    public Task<byte[]> RenderPageAsync(
        Guid documentId,
        int pageIndex,
        int dpi = 96,
        CancellationToken ct = default)
    {
        return Task.Run(() => RenderInternal(documentId, pageIndex, dpi), ct);
    }

    public Task<byte[]> RenderThumbnailAsync(
        Guid documentId,
        int pageIndex,
        int maxSize = 200,
        CancellationToken ct = default)
    {
        // サムネイルは低DPIで描画
        return Task.Run(() => RenderInternal(documentId, pageIndex, 48), ct);
    }

    private byte[] RenderInternal(Guid documentId, int pageIndex, int dpi)
    {
        if (!_docBytes.TryGetValue(documentId, out var bytes))
            throw new InvalidOperationException("ドキュメントが読み込まれていません。");

        _docPasswords.TryGetValue(documentId, out var password);

        // 該当ページの Rotation を取得して、PdfRotation に変換
        var pdfRotation = PdfRotation.Rotate0;
        if (_docs.TryGetValue(documentId, out var doc)
            && pageIndex >= 0 && pageIndex < doc.Pages.Count)
        {
            pdfRotation = doc.Pages[pageIndex].Rotation switch
            {
                90 => PdfRotation.Rotate90,
                180 => PdfRotation.Rotate180,
                270 => PdfRotation.Rotate270,
                _ => PdfRotation.Rotate0,
            };
        }

        // PDFium 呼び出しは直列化する
        lock (_pdfiumLock)
        {
            try
            {
                using var bitmap = Conversion.ToImage(
                    bytes,
                    password: password,
                    page: pageIndex,
                    options: new RenderOptions(Dpi: dpi, Rotation: pdfRotation));

                using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                return data.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ページレンダリングに失敗: ページ={Page}, DPI={Dpi}, Rotation={Rotation}",
                    pageIndex, dpi, pdfRotation);
                throw;
            }
        }
    }

    public void Close(Guid documentId)
    {
        _docBytes.TryRemove(documentId, out _);
        _docPasswords.TryRemove(documentId, out _);
        _docs.TryRemove(documentId, out _);
    }

    public void Dispose()
    {
        _docBytes.Clear();
        _docPasswords.Clear();
        _docs.Clear();
    }
}
