namespace PdfStudio.Wpf.Services;

public interface IDialogService
{
    string? ShowOpenFileDialog(string filter, string? title = null);
    string[]? ShowOpenMultipleFilesDialog(string filter, string? title = null);
    string? ShowSaveFileDialog(string filter, string? defaultName = null, string? title = null);
    string? ShowFolderDialog(string? title = null);
    void ShowInfo(string message, string title = "情報");
    void ShowError(string message, string title = "エラー");
    bool Confirm(string message, string title = "確認");

    /// <summary>
    /// 文字列入力ダイアログを表示する。キャンセル時はnullを返す。
    /// </summary>
    string? ShowInputDialog(string prompt, string title = "入力", string defaultValue = "");
}
