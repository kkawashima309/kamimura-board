using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace PdfStudio.Wpf.Views.Dialogs;

/// <summary>
/// 印刷プレビューダイアログ。
/// FixedDocumentを表示し、ユーザーが内容を確認してから印刷を実行できる。
/// </summary>
public class PrintPreviewDialog : Window
{
    private readonly DocumentViewer _viewer;
    private readonly Button _printButton;
    private readonly Button _cancelButton;

    /// <summary>OKボタン(印刷ボタン)が押された場合 true</summary>
    public bool ShouldPrint { get; private set; }

    public PrintPreviewDialog(FixedDocument document, string title)
    {
        Title = $"印刷プレビュー - {title}";
        Width = 900;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // DocumentViewer: 標準で「次へ/前へ」「ズーム」「ページレイアウト」などの機能を備える
        _viewer = new DocumentViewer
        {
            Document = document,
        };
        Grid.SetRow(_viewer, 0);
        grid.Children.Add(_viewer);

        // 下部のボタン群
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12),
        };

        _printButton = new Button
        {
            Content = "🖨 印刷",
            Width = 120,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        _printButton.Click += (s, e) =>
        {
            ShouldPrint = true;
            DialogResult = true;
            Close();
        };

        _cancelButton = new Button
        {
            Content = "キャンセル",
            Width = 100,
            Height = 32,
            IsCancel = true,
        };
        _cancelButton.Click += (s, e) =>
        {
            ShouldPrint = false;
            DialogResult = false;
            Close();
        };

        buttonPanel.Children.Add(_printButton);
        buttonPanel.Children.Add(_cancelButton);
        Grid.SetRow(buttonPanel, 1);
        grid.Children.Add(buttonPanel);

        Content = grid;

        // 初期表示: ページ全体が見えるようにフィット
        Loaded += (s, e) =>
        {
            try
            {
                if (_viewer.CanGoToPage(1))
                    _viewer.GoToPage(1);
                _viewer.FitToWidth();
            }
            catch { /* 軽微なエラーは無視 */ }
        };
    }
}
