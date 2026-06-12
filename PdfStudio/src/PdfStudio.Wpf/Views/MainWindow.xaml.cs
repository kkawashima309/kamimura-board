using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PdfStudio.Wpf.ViewModels;

namespace PdfStudio.Wpf.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    // ドラッグ＆ドロップ状態
    private Point _dragStartPoint;
    private bool _isDragging;
    private PageViewModel? _draggedItem;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        _vm = vm;
    }

    private void ExitMenu_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ==================== ファイルドロップ ====================

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        foreach (var file in files.Where(f =>
            f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
        {
            await _vm.OpenFromPathAsync(file);
        }
    }

    // ==================== タブ切り替え ====================

    private void TabBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PdfDocumentViewModel doc)
        {
            _vm.ActiveDocument = doc;
        }
    }

    // ==================== サムネイル選択でページ表示 ====================

    private async void ThumbnailList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm.ActiveDocument?.CurrentPage is { } page)
        {
            await _vm.ActiveDocument.ShowPageAsync(page.Index);
        }
    }

    // ==================== サムネイル並び替えD&D ====================

    private void ThumbnailList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDragging) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStartPoint.X) <= SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStartPoint.Y) <= SystemParameters.MinimumVerticalDragDistance)
            return;

        if (sender is not ListBox listBox) return;
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not PageViewModel pageVm) return;

        _draggedItem = pageVm;
        _isDragging = true;
        try
        {
            DragDrop.DoDragDrop(item, pageVm, DragDropEffects.Move);
        }
        finally
        {
            _isDragging = false;
            _draggedItem = null;
        }
    }

    private async void ThumbnailList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PageViewModel)) is not PageViewModel source) return;
        if (_vm.ActiveDocument is null) return;

        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not PageViewModel target) return;

        var fromIndex = source.Index;
        var toIndex = target.Index;
        if (fromIndex == toIndex) return;

        await _vm.MovePageAsync(fromIndex, toIndex);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        base.OnPreviewMouseLeftButtonDown(e);
    }

    // ==================== Ctrl+マウスホイールでズーム ====================

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        // Ctrlキーが押されているときだけズーム動作
        if (Keyboard.Modifiers == ModifierKeys.Control && _vm.ActiveDocument is not null)
        {
            if (e.Delta > 0)
            {
                if (_vm.ZoomInCommand.CanExecute(null))
                    _vm.ZoomInCommand.Execute(null);
            }
            else if (e.Delta < 0)
            {
                if (_vm.ZoomOutCommand.CanExecute(null))
                    _vm.ZoomOutCommand.Execute(null);
            }
            e.Handled = true;
            return;
        }
        base.OnPreviewMouseWheel(e);
    }

    /// <summary>
    /// PDFスクロールビュー上での Ctrl+マウスホイール (PreviewMouseWheelイベント直接ハンドリング)。
    /// ScrollViewer がイベントを横取りすることを防ぐため、ハンドラを直接登録している。
    /// </summary>
    private void PdfScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && _vm.ActiveDocument is not null)
        {
            if (e.Delta > 0)
            {
                if (_vm.ZoomInCommand.CanExecute(null))
                    _vm.ZoomInCommand.Execute(null);
            }
            else if (e.Delta < 0)
            {
                if (_vm.ZoomOutCommand.CanExecute(null))
                    _vm.ZoomOutCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // 未保存の変更をチェック
        var unsaved = _vm.OpenDocuments.Where(d => d.IsModified).ToList();
        if (unsaved.Count > 0)
        {
            var msg = $"{unsaved.Count}個の文書に未保存の変更があります。\n終了してもよろしいですか?";
            var result = MessageBox.Show(
                msg, "終了確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        base.OnClosing(e);
    }
}
