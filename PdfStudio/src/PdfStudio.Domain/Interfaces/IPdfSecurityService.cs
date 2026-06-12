using PdfStudio.Domain.ValueObjects;

namespace PdfStudio.Domain.Interfaces;

/// <summary>
/// PDFセキュリティ機能（パスワード設定・権限制御）。
/// </summary>
public interface IPdfSecurityService
{
    /// <summary>
    /// PDFにパスワードと権限を設定する。
    /// </summary>
    /// <param name="sourcePath">入力PDFパス。</param>
    /// <param name="outputPath">出力PDFパス。</param>
    /// <param name="userPassword">閲覧パスワード（空欄でパスワードなし）。</param>
    /// <param name="ownerPassword">編集制限パスワード。</param>
    /// <param name="permissions">権限設定。</param>
    Task EncryptAsync(
        string sourcePath,
        string outputPath,
        string userPassword,
        string ownerPassword,
        PdfPermissions permissions,
        CancellationToken ct = default);

    /// <summary>
    /// PDFのパスワードを解除する。
    /// </summary>
    Task DecryptAsync(
        string sourcePath,
        string outputPath,
        string password,
        CancellationToken ct = default);
}
