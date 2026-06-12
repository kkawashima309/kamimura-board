using PdfStudio.Domain.Entities;

namespace PdfStudio.Domain.Interfaces;

/// <summary>
/// PDFレンダリング機能の抽象。実装はInfrastructure層で差し替え可能。
/// </summary>
public interface IPdfRenderer : IDisposable
{
    /// <summary>
    /// PDFを読み込む。
    /// </summary>
    /// <param name="filePath">ファイルパス。</param>
    /// <param name="password">必要な場合のパスワード。</param>
    Task<PdfDocument> LoadAsync(
        string filePath,
        string? password = null,
        CancellationToken ct = default);

    /// <summary>
    /// 指定ページをPNGバイト列としてレンダリング。
    /// </summary>
    Task<byte[]> RenderPageAsync(
        Guid documentId,
        int pageIndex,
        int dpi = 96,
        CancellationToken ct = default);

    /// <summary>
    /// 指定ページのサムネイル画像をPNGバイト列として取得。
    /// </summary>
    Task<byte[]> RenderThumbnailAsync(
        Guid documentId,
        int pageIndex,
        int maxSize = 200,
        CancellationToken ct = default);

    /// <summary>
    /// ドキュメントを閉じてリソースを解放。
    /// </summary>
    void Close(Guid documentId);
}
