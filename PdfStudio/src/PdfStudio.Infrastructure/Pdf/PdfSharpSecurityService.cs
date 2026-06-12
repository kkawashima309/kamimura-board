using Microsoft.Extensions.Logging;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Security;
using PdfStudio.Domain.Interfaces;
using PdfStudio.Domain.ValueObjects;

namespace PdfStudio.Infrastructure.Pdf;

/// <summary>
/// PDFsharp 6.x を使用したPDFセキュリティ実装。
/// 公式ドキュメント https://docs.pdfsharp.net/PDFsharp/Topics/PDF-Features/Encryption.html
/// に準拠した API 呼び出し。
/// </summary>
public sealed class PdfSharpSecurityService : IPdfSecurityService
{
    private readonly ILogger<PdfSharpSecurityService> _logger;

    public PdfSharpSecurityService(ILogger<PdfSharpSecurityService> logger)
    {
        _logger = logger;
    }

    public Task EncryptAsync(
        string sourcePath,
        string outputPath,
        string userPassword,
        string ownerPassword,
        PdfPermissions permissions,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var document = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Modify);

            // PDFsharp 6.2 では、暗号化は document.SecurityHandler から直接設定する。
            // パスワードを設定すると自動的に AES 128bit (Version 4) 暗号化が有効になるが、
            // 明示的に SetEncryptionToV4UsingAES() を呼んでも良い。
            var securityHandler = document.SecurityHandler;

            // パスワード設定
            if (!string.IsNullOrEmpty(userPassword))
                securityHandler.UserPassword = userPassword;
            if (!string.IsNullOrEmpty(ownerPassword))
                securityHandler.OwnerPassword = ownerPassword;

            // 明示的にAES 128bitを指定（PDF 1.6互換）
            securityHandler.SetEncryptionToV4UsingAES(encryptMetadata: true);

            // 権限設定は document.SecuritySettings から
            var security = document.SecuritySettings;
            security.PermitPrint = permissions.AllowPrint;
            security.PermitExtractContent = permissions.AllowCopy;
            security.PermitModifyDocument = permissions.AllowEdit;
            security.PermitAnnotations = permissions.AllowAnnotations;
            security.PermitFormsFill = permissions.AllowFormFilling;
            security.PermitAssembleDocument = permissions.AllowAssembly;

            document.Save(outputPath);
            _logger.LogInformation("PDFを暗号化しました: {Output}", outputPath);
        }, ct);
    }

    public Task DecryptAsync(
        string sourcePath,
        string outputPath,
        string password,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            // 元PDFをパスワード付きで開く（Import モード）
            using var sourceDoc = PdfReader.Open(
                sourcePath,
                password,
                PdfDocumentOpenMode.Import);

            // 新しい無暗号化のPDFを作成し、ページをコピーする
            // これにより SetEncryption 系APIの仕様差異を回避
            using var outputDoc = new PdfSharp.Pdf.PdfDocument();
            outputDoc.Info.Title = sourceDoc.Info.Title;
            outputDoc.Info.Author = sourceDoc.Info.Author;
            outputDoc.Info.Subject = sourceDoc.Info.Subject;

            for (int i = 0; i < sourceDoc.PageCount; i++)
            {
                outputDoc.AddPage(sourceDoc.Pages[i]);
            }

            outputDoc.Save(outputPath);
            _logger.LogInformation("PDFを復号しました: {Output}", outputPath);
        }, ct);
    }
}
