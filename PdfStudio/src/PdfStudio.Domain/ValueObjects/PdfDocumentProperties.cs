namespace PdfStudio.Domain.ValueObjects;

/// <summary>
/// PDFドキュメントのメタデータ(プロパティ)。
/// </summary>
public sealed record PdfDocumentProperties
{
    /// <summary>タイトル</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>作成者</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>件名/サブタイトル</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>キーワード(カンマ区切り)</summary>
    public string Keywords { get; init; } = string.Empty;

    /// <summary>作成アプリケーション名</summary>
    public string Creator { get; init; } = string.Empty;

    /// <summary>生成プログラム(通常はライブラリ名)</summary>
    public string Producer { get; init; } = string.Empty;

    /// <summary>作成日時(読み取り専用)</summary>
    public DateTime? CreationDate { get; init; }

    /// <summary>更新日時(読み取り専用)</summary>
    public DateTime? ModificationDate { get; init; }

    /// <summary>空のプロパティセット</summary>
    public static PdfDocumentProperties Empty => new();
}
