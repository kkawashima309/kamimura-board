using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfStudio.Infrastructure.Pdf;

/// <summary>
/// 編集系処理(注釈・ウォーターマーク等)のためにPDFを開くヘルパー。
///
/// 業務PDFには「オーナーパスワードのみ(閲覧は自由だが編集を制限)」のものが多い。
/// この種のPDFは PDFium では閲覧できるが、PDFsharp の Modify モードでは
/// 「To modify the document the owner password is required」で開けず、
/// スタンプ・付箋・ウォーターマーク等の編集がすべて失敗してしまう。
///
/// 利用者が閲覧できている=正当に保有している自社文書であるため、
/// その場合は Import モードで開き直し、新しいドキュメントへページを取り込んで
/// 「編集可能なコピー」を作ることで、注釈・編集を行えるようにする。
/// (出力は暗号化なしの新規PDFになる)
/// </summary>
internal static class PdfDocumentOpener
{
    /// <summary>
    /// 編集用にPDFを開く。通常は Modify で開くが、編集制限により
    /// 開けない場合は Import 方式で編集可能なコピーを構築して返す。
    /// </summary>
    /// <param name="path">対象PDFのパス</param>
    /// <param name="rebuilt">
    /// 編集制限のため再構築した(=暗号化が外れた)場合 true。
    /// </param>
    public static PdfDocument OpenForEdit(string path, out bool rebuilt)
    {
        try
        {
            rebuilt = false;
            return PdfReader.Open(path, PdfDocumentOpenMode.Modify);
        }
        catch (Exception ex) when (IsEditRestricted(ex))
        {
            // 編集制限PDF: Import で開き直し、編集可能なコピーを構築する
            using var src = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            var doc = new PdfDocument();
            try
            {
                doc.Info.Title = src.Info.Title;
                doc.Info.Author = src.Info.Author;
                doc.Info.Subject = src.Info.Subject;
            }
            catch { /* メタデータコピーは失敗しても致命的ではない */ }

            for (int i = 0; i < src.PageCount; i++)
                doc.AddPage(src.Pages[i]);

            rebuilt = true;
            return doc;
        }
    }

    /// <summary>
    /// 例外が「編集にオーナーパスワードが必要」によるものかを判定する。
    /// </summary>
    public static bool IsEditRestricted(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            var m = e.Message;
            if (!string.IsNullOrEmpty(m)
                && (m.Contains("owner password", StringComparison.OrdinalIgnoreCase)
                    || m.Contains("オーナー", StringComparison.Ordinal)))
            {
                return true;
            }
        }
        return false;
    }
}
