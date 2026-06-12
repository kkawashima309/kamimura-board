using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PdfStudio.Wpf.Views.Dialogs;

/// <summary>
/// シンプルな文字列入力ダイアログ。
/// </summary>
public class InputDialog : Window
{
    private readonly TextBox _textBox;

    public string InputValue => _textBox.Text;

    public InputDialog(string prompt, string title, string defaultValue)
    {
        Title = title;
        Width = 480;
        Height = 200;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // プロンプト
        var promptLabel = new TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(promptLabel, 0);
        grid.Children.Add(promptLabel);

        // 入力ボックス
        _textBox = new TextBox
        {
            Text = defaultValue ?? string.Empty,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(6),
            FontSize = 14,
        };
        _textBox.SelectAll();
        _textBox.Focus();
        _textBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                DialogResult = true;
                Close();
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        };
        Grid.SetRow(_textBox, 1);
        grid.Children.Add(_textBox);

        // ボタン群
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        okButton.Click += (s, e) =>
        {
            DialogResult = true;
            Close();
        };
        var cancelButton = new Button
        {
            Content = "キャンセル",
            Width = 80,
            IsCancel = true,
        };
        cancelButton.Click += (s, e) =>
        {
            DialogResult = false;
            Close();
        };
        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        Grid.SetRow(buttonPanel, 2);
        grid.Children.Add(buttonPanel);

        Content = grid;

        Loaded += (s, e) =>
        {
            _textBox.Focus();
            _textBox.SelectAll();
        };
    }
}
