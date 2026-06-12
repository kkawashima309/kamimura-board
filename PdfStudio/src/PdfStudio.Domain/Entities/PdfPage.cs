namespace PdfStudio.Domain.Entities;

/// <summary>
/// PDFのページを表すエンティティ。
/// </summary>
public class PdfPage
{
    /// <summary>
    /// ドキュメント内での現在の表示位置（0始まり）。
    /// 削除・並び替えのたびに振り直される。
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 読み込み元PDFファイル内でのページ番号（0始まり）。
    /// 削除・並び替えを行っても変化しない。レンダリングと保存は
    /// この値で元ファイルのページを参照する。-1 は元ファイルに
    /// 対応ページが存在しないことを表す（白紙挿入直後など）。
    /// </summary>
    public int SourceIndex { get; set; } = -1;

    /// <summary>
    /// ページ幅（ポイント単位）。
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// ページ高さ（ポイント単位）。
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// ページの回転角度（0 / 90 / 180 / 270）。
    /// </summary>
    public int Rotation { get; set; }
}
