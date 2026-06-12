using System.Windows;
using Microsoft.Win32;
using PdfStudio.Wpf.Views.Dialogs;

namespace PdfStudio.Wpf.Services;

public class DialogService : IDialogService
{
    public string? ShowOpenFileDialog(string filter, string? title = null)
    {
        var dlg = new OpenFileDialog
        {
            Filter = filter,
            Title = title ?? "ファイルを開く",
            CheckFileExists = true,
            Multiselect = false,
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string[]? ShowOpenMultipleFilesDialog(string filter, string? title = null)
    {
        var dlg = new OpenFileDialog
        {
            Filter = filter,
            Title = title ?? "ファイルを開く",
            CheckFileExists = true,
            Multiselect = true,
        };
        return dlg.ShowDialog() == true ? dlg.FileNames : null;
    }

    public string? ShowSaveFileDialog(string filter, string? defaultName = null, string? title = null)
    {
        var dlg = new SaveFileDialog
        {
            Filter = filter,
            Title = title ?? "名前を付けて保存",
            FileName = defaultName ?? string.Empty,
            OverwritePrompt = true,
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? ShowFolderDialog(string? title = null)
    {
        var dlg = new OpenFolderDialog
        {
            Title = title ?? "フォルダーを選択",
        };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public void ShowInfo(string message, string title = "情報")
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void ShowError(string message, string title = "エラー")
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public bool Confirm(string message, string title = "確認")
    {
        var result = MessageBox.Show(
            message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    public string? ShowInputDialog(string prompt, string title = "入力", string defaultValue = "")
    {
        var dlg = new InputDialog(prompt, title, defaultValue);

        // アクティブウィンドウをオーナーに設定
        // 注意: PdfStudio.Application 名前空間との衝突を避けるため完全修飾名を使用
        var owner = System.Windows.Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive);
        if (owner != null) dlg.Owner = owner;

        var result = dlg.ShowDialog();
        return result == true ? dlg.InputValue : null;
    }
}
