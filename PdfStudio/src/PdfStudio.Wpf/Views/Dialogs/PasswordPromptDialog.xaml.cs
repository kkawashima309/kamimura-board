using System.Windows;
using System.Windows.Input;

namespace PdfStudio.Wpf.Views.Dialogs;

public partial class PasswordPromptDialog : Window
{
    public string Password { get; private set; } = string.Empty;

    public PasswordPromptDialog(string message, string title)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        Loaded += (_, _) => PasswordBox.Focus();
    }

    public static string? Prompt(string message, string title = "パスワード入力")
    {
        var dlg = new PasswordPromptDialog(message, title)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        return dlg.ShowDialog() == true ? dlg.Password : null;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Password = PasswordBox.Password;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OkButton_Click(sender, e);
        }
    }
}
