using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PdfStudio.Application.Common;
using PdfStudio.Application.Services;
using PdfStudio.Application.UseCases.Pages;
using PdfStudio.Domain.Interfaces;
using PdfStudio.Domain.ValueObjects;
using PdfStudio.Wpf.Services;
using PdfStudio.Wpf.Views.Dialogs;

namespace PdfStudio.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IPdfRenderer _renderer;
    private readonly IPdfEditor _editor;
    private readonly IPdfSecurityService _security;
    private readonly IPdfSearchService _searchService;
    private readonly IPdfAnnotationService _annotationService;
    private readonly PdfStudio.Infrastructure.Pdf.BatchService _batchService;
    private readonly IRecentFilesService _recentFilesService;
    private readonly IDialogService _dialog;
    private readonly UndoRedoStack _undoStack;
    private readonly PdfStudio.Wpf.Services.PrintService _printService;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<PdfDocumentViewModel> _openDocuments = new();

    [ObservableProperty]
    private PdfDocumentViewModel? _activeDocument;

    [ObservableProperty]
    private ObservableCollection<string> _recentFiles = new();

    [ObservableProperty]
    private string _statusMessage = "準備完了";

    [ObservableProperty]
    private bool _isBusy;

    // ---------------------- 検索状態 ----------------------

    [ObservableProperty]
    private ObservableCollection<PdfSearchResult> _searchResults = new();

    [ObservableProperty]
    private int _searchResultIndex = -1;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _searchCaseSensitive;

    [ObservableProperty]
    private bool _isSearchPanelVisible;

    public MainViewModel(
        IPdfRenderer renderer,
        IPdfEditor editor,
        IPdfSecurityService security,
        IPdfSearchService searchService,
        IPdfAnnotationService annotationService,
        PdfStudio.Infrastructure.Pdf.BatchService batchService,
        IRecentFilesService recentFiles,
        IDialogService dialog,
        UndoRedoStack undoStack,
        PdfStudio.Wpf.Services.PrintService printService,
        ILogger<MainViewModel> logger)
    {
        _renderer = renderer;
        _editor = editor;
        _security = security;
        _searchService = searchService;
        _annotationService = annotationService;
        _batchService = batchService;
        _recentFilesService = recentFiles;
        _dialog = dialog;
        _undoStack = undoStack;
        _printService = printService;
        _logger = logger;

        _undoStack.StateChanged += (_, _) =>
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        };

        LoadRecentFiles();
    }

    partial void OnActiveDocumentChanged(PdfDocumentViewModel? value)
    {
        // タブのアクティブ表示を切り替え
        foreach (var d in OpenDocuments)
            d.IsActive = ReferenceEquals(d, value);

        NotifyDocumentCommandsCanExecuteChanged();
    }

    // CurrentPage の変化(ページ移動)に応じてナビゲーション系コマンドの
    // 活性状態を更新するため、アクティブドキュメントの変更を購読する
    partial void OnActiveDocumentChanged(
        PdfDocumentViewModel? oldValue, PdfDocumentViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= OnActiveDocumentPropertyChanged;
        if (newValue is not null)
            newValue.PropertyChanged += OnActiveDocumentPropertyChanged;
    }

    private void OnActiveDocumentPropertyChanged(
        object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PdfDocumentViewModel.CurrentPage))
            NotifyPageCommandsCanExecuteChanged();
    }

    /// <summary>
    /// アクティブドキュメントの有無に依存する全コマンドの活性状態を更新する。
    /// CommunityToolkit の RelayCommand は WPF の CommandManager と連動しないため、
    /// 明示的に通知しないとメニュー・ツールバーが灰色のまま残る。
    /// </summary>
    private void NotifyDocumentCommandsCanExecuteChanged()
    {
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        CloseTabCommand.NotifyCanExecuteChanged();
        SetPasswordCommand.NotifyCanExecuteChanged();
        PrintCommand.NotifyCanExecuteChanged();
        GoToPageCommand.NotifyCanExecuteChanged();
        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();
        ZoomActualCommand.NotifyCanExecuteChanged();
        ZoomFitPageCommand.NotifyCanExecuteChanged();
        ZoomFitWidthCommand.NotifyCanExecuteChanged();
        InsertPagesFromFileCommand.NotifyCanExecuteChanged();
        ExtractPagesCommand.NotifyCanExecuteChanged();
        InsertBlankPageCommand.NotifyCanExecuteChanged();
        EditPropertiesCommand.NotifyCanExecuteChanged();
        ShowPropertiesCommand.NotifyCanExecuteChanged();
        AddWatermarkCommand.NotifyCanExecuteChanged();
        AddPageNumbersCommand.NotifyCanExecuteChanged();
        AddHeaderFooterCommand.NotifyCanExecuteChanged();
        ExportPageAsImageCommand.NotifyCanExecuteChanged();
        AddStampCommand.NotifyCanExecuteChanged();
        AddStickyNoteCommand.NotifyCanExecuteChanged();
        ExecuteSearchCommand.NotifyCanExecuteChanged();
        NotifySearchResultCommandsCanExecuteChanged();
        NotifyPageCommandsCanExecuteChanged();
    }

    /// <summary>
    /// 現在ページ(CurrentPage)に依存するコマンドの活性状態を更新する。
    /// </summary>
    private void NotifyPageCommandsCanExecuteChanged()
    {
        DeletePageCommand.NotifyCanExecuteChanged();
        RotatePageLeftCommand.NotifyCanExecuteChanged();
        RotatePageRightCommand.NotifyCanExecuteChanged();
        FirstPageCommand.NotifyCanExecuteChanged();
        LastPageCommand.NotifyCanExecuteChanged();
        PrevPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    private void NotifySearchResultCommandsCanExecuteChanged()
    {
        NextSearchResultCommand.NotifyCanExecuteChanged();
        PrevSearchResultCommand.NotifyCanExecuteChanged();
    }

    private void LoadRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var f in _recentFilesService.GetRecentFiles())
            RecentFiles.Add(f);
    }

    // ---------------------- Open / Save ----------------------

    [RelayCommand]
    private async Task OpenAsync()
    {
        var path = _dialog.ShowOpenFileDialog("PDFファイル (*.pdf)|*.pdf|すべてのファイル (*.*)|*.*");
        if (string.IsNullOrEmpty(path)) return;
        await OpenFromPathAsync(path);
    }

    public async Task OpenFromPathAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _dialog.ShowError($"ファイルが見つかりません:\n{filePath}");
            _recentFilesService.Remove(filePath);
            LoadRecentFiles();
            return;
        }

        // 既に開いているかチェック
        var existing = OpenDocuments.FirstOrDefault(d =>
            string.Equals(d.Document.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            ActiveDocument = existing;
            return;
        }

        PdfDocumentViewModel? createdVm = null;
        try
        {
            IsBusy = true;
            StatusMessage = $"読み込み中: {Path.GetFileName(filePath)}";

            var doc = await _renderer.LoadAsync(filePath);
            createdVm = new PdfDocumentViewModel(doc, _renderer);

            OpenDocuments.Add(createdVm);
            ActiveDocument = createdVm;

            _recentFilesService.Add(filePath);
            LoadRecentFiles();

            // 初期ページ表示とサムネイル
            // これらは失敗しても致命的ではないため、個別にtry-catchで保護
            try
            {
                await createdVm.ShowPageAsync(0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "初期ページの表示に失敗");
            }

            try
            {
                _ = createdVm.LoadThumbnailsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "サムネイル生成の起動に失敗");
            }

            StatusMessage = $"{Path.GetFileName(filePath)} を開きました ({doc.PageCount}ページ)";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDFオープン失敗: {Path}", filePath);

            // パスワード付きの場合は再試行
            // (レンダラーが例外をラップするため、内部例外も含めて判定する)
            if (IsPasswordProtectedError(ex))
            {
                var pwd = PasswordPromptDialog.Prompt("このPDFはパスワードで保護されています。");
                if (!string.IsNullOrEmpty(pwd))
                {
                    try
                    {
                        var doc = await _renderer.LoadAsync(filePath, pwd);
                        var vm = new PdfDocumentViewModel(doc, _renderer);
                        OpenDocuments.Add(vm);
                        ActiveDocument = vm;
                        _recentFilesService.Add(filePath);
                        LoadRecentFiles();
                        try { await vm.ShowPageAsync(0); } catch { }
                        try { _ = vm.LoadThumbnailsAsync(); } catch { }
                        return;
                    }
                    catch (Exception innerEx)
                    {
                        _dialog.ShowError($"パスワードが正しくないか、ファイルを開けませんでした:\n{innerEx.Message}");
                        return;
                    }
                }
            }

            // 表示VMが作成済みなら表示は出来ている → 警告レベルとし、ポップは出さずステータスバーのみ
            if (createdVm != null && OpenDocuments.Contains(createdVm))
            {
                StatusMessage = $"警告: 一部の機能が制限されています。詳細はログを参照してください。";
                _logger.LogWarning("表示は成功したが、後続処理で例外発生: {Message}", ex.Message);
            }
            else
            {
                // 本当に表示すらできなかった場合のみエラーポップを表示
                _dialog.ShowError($"PDFを開けませんでした:\n{ex.Message}");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task SaveAsync()
    {
        if (ActiveDocument is null) return;

        // プレビュー中(一時PDF)の場合は「名前を付けて保存」へ誘導
        // (一時フォルダに上書き保存しても意味がないため)
        if (IsPreviewDocument(ActiveDocument))
        {
            await SaveAsAsync();
            return;
        }

        try
        {
            IsBusy = true;
            await _editor.SaveAsync(ActiveDocument.Document);
            ActiveDocument.IsModified = false;
            var savedName = ActiveDocument.Document.FileName;

            // 保存によりファイル実体が変わったため、Domain状態(回転・並び順)と
            // レンダラーのキャッシュを保存後のファイルから作り直す。
            // これを行わないと、続けて保存したときに回転が二重適用される。
            await ReloadActiveDocumentAsync();

            StatusMessage = $"保存しました: {savedName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存失敗");
            _dialog.ShowError($"保存に失敗しました:\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task SaveAsAsync()
    {
        if (ActiveDocument is null) return;

        // プレビュー中なら元ファイル名をデフォルトにする
        string defaultName;
        var currentPath = ActiveDocument.Document.FilePath;
        if (!string.IsNullOrEmpty(currentPath)
            && _previewOriginalNames.TryGetValue(currentPath, out var origName))
        {
            defaultName = origName + "_edited.pdf";
        }
        else
        {
            defaultName = Path.GetFileNameWithoutExtension(ActiveDocument.Document.FileName)
                + "_edited.pdf";
        }

        var path = _dialog.ShowSaveFileDialog(
            "PDFファイル (*.pdf)|*.pdf",
            defaultName,
            "名前を付けて保存");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            IsBusy = true;
            var wasPreviewTemp = currentPath;
            await _editor.SaveAsync(ActiveDocument.Document, path);

            // プレビュー登録を解除(保存されたので正式なファイルになった)
            if (!string.IsNullOrEmpty(wasPreviewTemp))
            {
                _previewOriginalNames.Remove(wasPreviewTemp);
            }

            _recentFilesService.Add(path);
            LoadRecentFiles();
            ActiveDocument.IsModified = false;

            // 保存後のファイルを正として開き直す(回転の二重適用防止と
            // プレビュー一時PDF→正式ファイルへのタブ切替を兼ねる)
            await ReloadActiveDocumentAsync();

            StatusMessage = $"保存しました: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "名前を付けて保存に失敗");
            _dialog.ShowError(
                $"保存に失敗しました。\n\n" +
                $"エラー: {ex.GetType().Name}\n" +
                $"メッセージ: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private void CloseTab()
    {
        if (ActiveDocument is null) return;

        if (ActiveDocument.IsModified)
        {
            if (!_dialog.Confirm(
                "変更が保存されていません。閉じてもよろしいですか?",
                "確認"))
                return;
        }

        var docId = ActiveDocument.Document.Id;
        OpenDocuments.Remove(ActiveDocument);
        _renderer.Close(docId);
        ActiveDocument = OpenDocuments.LastOrDefault();
        _undoStack.Clear();
    }

    private bool HasActiveDocument() => ActiveDocument is not null;

    /// <summary>
    /// 例外チェーン全体を見て「パスワード保護による失敗」かどうかを判定する。
    /// PdfiumRenderer は元の例外を InvalidOperationException でラップするため、
    /// 最上位の Message だけを見ると判定できない。
    /// </summary>
    private static bool IsPasswordProtectedError(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e.GetType().Name.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("パスワード", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    // ---------------------- Page Operations ----------------------

    [RelayCommand(CanExecute = nameof(CanModifyPage))]
    private async Task DeletePageAsync()
    {
        if (ActiveDocument?.CurrentPage is null) return;

        var idx = ActiveDocument.CurrentPage.Index;
        if (ActiveDocument.Document.PageCount <= 1)
        {
            _dialog.ShowError("最後の1ページは削除できません。");
            return;
        }

        var cmd = new DeletePageCommand(ActiveDocument.Document, idx);
        await _undoStack.ExecuteAsync(cmd);

        ActiveDocument.RefreshPages();
        ActiveDocument.IsModified = true;

        var nextIdx = Math.Min(idx, ActiveDocument.Document.PageCount - 1);
        await ActiveDocument.ShowPageAsync(nextIdx);
        StatusMessage = cmd.Description;
    }

    [RelayCommand(CanExecute = nameof(CanModifyPage))]
    private async Task RotatePageLeftAsync()
    {
        await RotateAsync(-90);
    }

    [RelayCommand(CanExecute = nameof(CanModifyPage))]
    private async Task RotatePageRightAsync()
    {
        await RotateAsync(90);
    }

    private async Task RotateAsync(int degrees)
    {
        // 回転対象を決定: CurrentPage があればそれ、なければ最初のページ
        if (ActiveDocument is null) return;

        int idx;
        if (ActiveDocument.CurrentPage is not null)
        {
            idx = ActiveDocument.CurrentPage.Index;
        }
        else if (ActiveDocument.Document.Pages.Count > 0)
        {
            idx = 0;
        }
        else
        {
            _dialog.ShowError("回転するページがありません。", "エラー");
            return;
        }

        try
        {
            var cmd = new RotatePageCommand(ActiveDocument.Document, idx, degrees);
            await _undoStack.ExecuteAsync(cmd);

            // ログ: 回転後の各ページのIndex/Rotation状態を記録(消失バグ追跡用)
            var pageStatus = string.Join(", ",
                ActiveDocument.Document.Pages.Select(p => $"[{p.Index}:{p.Rotation}°]"));
            _logger.LogInformation(
                "回転後のページ状態 (合計{Count}ページ): {Status}",
                ActiveDocument.Document.Pages.Count, pageStatus);

            ActiveDocument.IsModified = true;

            // ページビューを再描画(新しいRotationが反映される)
            await ActiveDocument.ShowPageAsync(idx);

            // サムネイルも再生成して回転を反映
            await ActiveDocument.RefreshThumbnailAsync(idx);

            StatusMessage = cmd.Description;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ページ回転に失敗");
            _dialog.ShowError(
                $"ページの回転に失敗しました。\n\n" +
                $"エラー: {ex.GetType().Name}\n" +
                $"メッセージ: {ex.Message}",
                "エラー");
        }
    }

    public async Task MovePageAsync(int from, int to)
    {
        if (ActiveDocument is null) return;
        var cmd = new MovePageCommand(ActiveDocument.Document, from, to);
        await _undoStack.ExecuteAsync(cmd);
        ActiveDocument.RefreshPages();
        ActiveDocument.IsModified = true;
        await ActiveDocument.ShowPageAsync(to);
        StatusMessage = cmd.Description;
    }

    private bool CanModifyPage() =>
        ActiveDocument is not null
        && ActiveDocument.Document.Pages.Count > 0;

    // ---------------------- Undo / Redo ----------------------

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        await _undoStack.UndoAsync();
        if (ActiveDocument != null)
        {
            ActiveDocument.RefreshPages();
            await ActiveDocument.ShowPageAsync(ActiveDocument.CurrentPage?.Index ?? 0);
            ActiveDocument.IsModified = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private async Task RedoAsync()
    {
        await _undoStack.RedoAsync();
        if (ActiveDocument != null)
        {
            ActiveDocument.RefreshPages();
            await ActiveDocument.ShowPageAsync(ActiveDocument.CurrentPage?.Index ?? 0);
            ActiveDocument.IsModified = true;
        }
    }

    private bool CanUndo() => _undoStack.CanUndo;
    private bool CanRedo() => _undoStack.CanRedo;

    // ---------------------- Merge / Split ----------------------

    [RelayCommand]
    private async Task MergePdfsAsync()
    {
        var files = _dialog.ShowOpenMultipleFilesDialog(
            "PDFファイル (*.pdf)|*.pdf",
            "結合するPDFを選択（複数選択可）");
        if (files is null || files.Length < 2)
        {
            if (files != null)
                _dialog.ShowInfo("結合には2つ以上のPDFを選択してください。");
            return;
        }

        var output = _dialog.ShowSaveFileDialog(
            "PDFファイル (*.pdf)|*.pdf",
            "merged.pdf",
            "結合後のPDFを保存");
        if (string.IsNullOrEmpty(output)) return;

        try
        {
            IsBusy = true;
            StatusMessage = "PDFを結合しています...";
            var resultPath = await _editor.MergeAsync(files, output);
            _dialog.ShowInfo($"結合が完了しました:\n{resultPath}", "完了");
            StatusMessage = "結合完了";

            if (_dialog.Confirm("結合したPDFを開きますか?"))
                await OpenFromPathAsync(resultPath);
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"結合に失敗しました:\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SplitPdfAsync()
    {
        var input = _dialog.ShowOpenFileDialog(
            "PDFファイル (*.pdf)|*.pdf",
            "分割するPDFを選択");
        if (string.IsNullOrEmpty(input)) return;

        var outDir = _dialog.ShowFolderDialog("分割後のファイル出力先フォルダー");
        if (string.IsNullOrEmpty(outDir)) return;

        try
        {
            IsBusy = true;
            StatusMessage = "PDFを分割しています...";
            var results = await _editor.SplitAsync(input, SplitMode.EachPage, outDir);
            _dialog.ShowInfo(
                $"{results.Count}ファイルに分割しました。\n出力先: {outDir}",
                "完了");
            StatusMessage = $"分割完了 ({results.Count}ファイル)";
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"分割に失敗しました:\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------------------- Security ----------------------

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task SetPasswordAsync()
    {
        if (ActiveDocument is null) return;

        var pwd = PasswordPromptDialog.Prompt(
            "PDFに設定するパスワードを入力してください:",
            "パスワード設定");
        if (string.IsNullOrEmpty(pwd)) return;

        var defaultName = Path.GetFileNameWithoutExtension(
            ActiveDocument.Document.FileName) + "_protected.pdf";
        var output = _dialog.ShowSaveFileDialog(
            "PDFファイル (*.pdf)|*.pdf",
            defaultName,
            "保護されたPDFを保存");
        if (string.IsNullOrEmpty(output)) return;

        try
        {
            IsBusy = true;

            // まず一時ファイルへ保存（最新の編集を反映）。
            // SaveAsync は document.FilePath を保存先に書き換えるため、
            // 元のパスを退避し、暗号化後に必ず復元する。
            // (復元しないと以後の上書き保存が削除済み一時ファイルを指して失敗する)
            var originalFilePath = ActiveDocument.Document.FilePath;
            var wasModified = ActiveDocument.Document.IsModified;
            var tempInput = CreateTempPdfPath();
            try
            {
                await _editor.SaveAsync(ActiveDocument.Document, tempInput);
            }
            finally
            {
                ActiveDocument.Document.FilePath = originalFilePath;
                ActiveDocument.Document.IsModified = wasModified;
            }

            await _security.EncryptAsync(
                tempInput, output,
                userPassword: pwd,
                ownerPassword: pwd,
                permissions: PdfPermissions.FullAccess);

            try { File.Delete(tempInput); } catch { }

            _dialog.ShowInfo($"パスワード保護されたPDFを保存しました:\n{output}", "完了");
            StatusMessage = "パスワード設定完了";
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"パスワード設定に失敗しました:\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------------------- Recent Files ----------------------

    [RelayCommand]
    private async Task OpenRecentAsync(string filePath)
    {
        if (!string.IsNullOrEmpty(filePath))
            await OpenFromPathAsync(filePath);
    }

    [RelayCommand]
    private void ClearRecentFiles()
    {
        if (_dialog.Confirm("最近使ったファイルの一覧をクリアしますか?"))
        {
            _recentFilesService.Clear();
            LoadRecentFiles();
        }
    }

    // ---------------------- About / License ----------------------

    [RelayCommand]
    private void ShowAbout()
    {
        var msg =
            "PdfStudio v0.5.9\n\n" +
            "Windows向け PDF 編集ツール\n" +
            "\n" +
            "v0.5.9 重要修正:\n" +
            "  - スタンプ・付箋(コメント)・ウォーターマーク・ページ番号・\n" +
            "    ヘッダー/フッターの文字描画が「使用可能なフォントが\n" +
            "    見つかりません」で必ず失敗していた問題を解決\n" +
            "    (PDFsharpのフォントリゾルバを起動時に構成)\n" +
            "\n" +
            "v0.5.8 重要修正:\n" +
            "  - ページ削除・並び替え後の保存で、意図しないページが\n" +
            "    削除・出力される致命的不具合を解決(SourceIndex方式)\n" +
            "  - 削除・並び替え直後の画面表示が実際の内容とずれる問題を解決\n" +
            "  - 保存を連続実行すると回転が二重適用される問題を解決\n" +
            "  - パスワード設定後に上書き保存が必ず失敗する問題を解決\n" +
            "  - パスワード保護されたPDFを開く際にパスワード入力画面が\n" +
            "    表示されない問題を解決\n" +
            "  - PDFを開いた後もメニュー・ツールバーの一部が\n" +
            "    無効(灰色)のままになる問題を解決\n" +
            "  - 未保存PDFからのページ抽出時のエラー処理を改善\n" +
            "\n" +
            "v0.5.7 重要修正 (静的検証済み):\n" +
            "  - 日本語フォント対応: スタンプ「承認」等の日本語描画が\n" +
            "    Arialフォント起因で必ず失敗していた問題を解決\n" +
            "  - 保存フロー修正: プレビュー後の保存が「元PDFが見つからない」\n" +
            "    で必ず失敗していた問題を解決(FilePath保持方式に変更)\n" +
            "  - Ctrl+S はプレビュー中なら自動で名前を付けて保存へ\n" +
            "  - 保存時のデフォルト名に元ファイル名を使用\n" +
            "\n" +
            "v0.5.5 改善:\n" +
            "  - 付箋を可視化(黄色アイコン+コメントテキスト)\n" +
            "  - スタンプ/付箋: CurrentPage がなくても最初のページに配置\n" +
            "  - ステータスメッセージを統一(画面確認後 Ctrl+Shift+S)\n" +
            "  - プレビュー切替時のログ強化\n" +
            "\n" +
            "v0.5.4 (安定版):\n" +
            "  - v0.5.0〜0.5.3 で不具合があったため v0.4.2 ベースに復元\n" +
            "  - tools/tessdata フォルダのインストール対応のみ取込\n" +
            "  - OCR/注釈高度化は次バージョン以降で段階的に再実装\n" +
            "\n" +
            "v0.4.2 修正:\n" +
            "  - SaveAsync の回転反映を強化(明示的な Rotate 設定)\n" +
            "  - 保存後のページ数・回転検証ログを追加\n" +
            "  - パスの完全修飾比較で同一パス判定を厳密化\n" +
            "\n" +
            "v0.4.1 修正:\n" +
            "  - 編集系: ファイル保存ではなくプレビュー表示方式に\n" +
            "    (Ctrl+Shift+S でユーザー保存)\n" +
            "  - ページ消失バグの調査用ログ追加・SaveAsync強化\n" +
            "\n" +
            "v0.4.0 新機能:\n" +
            "  - ビジネス特化:\n" +
            "    ・ウォーターマーク追加 (社外秘、ドラフト 等)\n" +
            "    ・ページ番号自動付与\n" +
            "    ・ヘッダー/フッター追加\n" +
            "    ・PDF→画像エクスポート (PNG/JPEG)\n" +
            "    ・複数PDF一括処理\n" +
            "  - 注釈・編集:\n" +
            "    ・電子スタンプ (承認、却下 等)\n" +
            "    ・付箋 (コメント)\n" +
            "\n" +
            "v0.3.3 修正:\n" +
            "  - ズーム: MaxWidth撤廃 + ScaleTransformで正しく拡縮\n" +
            "  - ページ送り連打時の表示ブレを防止\n" +
            "  - 印刷プレビュー画面を追加\n" +
            "\n" +
            "v0.3.2 改善:\n" +
            "  - ズーム: Ctrl+マウスホイールで上下に対応\n" +
            "  - 印刷: PDF向きを自動判定して用紙の向きを切替\n" +
            "\n" +
            "v0.3.0 新機能 (使い勝手向上):\n" +
            "  - ズーム機能 (Ctrl+ホイール/Ctrl+0, ページ全体/幅に合わせる)\n" +
            "  - 印刷機能 (Ctrl+P, ページ範囲指定, 用紙向き自動)\n" +
            "  - PDF内テキスト検索 (Ctrl+F, F3で次へ)\n" +
            "  - ページ移動ショートカット (PageUp/Down, Home/End, Ctrl+G)\n" +
            "\n" +
            "v0.2.5 修正:\n" +
            "  - ページ回転が画面/ファイルに反映されない問題を解決\n" +
            "  - ページ挿入/白紙挿入のファイルロック問題を解決\n" +
            "  - エラー詳細表示の改善\n" +
            "v0.2.4 修正:\n" +
            "  - XAML Run要素のTwoWayバインド問題を解決\n" +
            "  - メニュー灰色化問題の解消\n" +
            "\n" +
            "v0.2 新機能:\n" +
            "  - ページ挿入(別PDFから)\n" +
            "  - ページ抽出\n" +
            "  - 白紙ページ追加\n" +
            "  - PDFプロパティ編集\n" +
            "  - ズーム機能\n" +
            "\n" +
            "OSS依存ライブラリ:\n" +
            "  - PDFium (BSD-3-Clause)\n" +
            "  - PDFsharp 6 (MIT)\n" +
            "  - SkiaSharp (MIT)\n" +
            "  - CommunityToolkit.Mvvm (MIT)\n" +
            "  - Serilog (Apache 2.0)\n";
        _dialog.ShowInfo(msg, "PdfStudio について");
    }

    // ---------------------- v0.2 新機能 ----------------------

    /// <summary>
    /// ページ挿入(別PDFから)
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task InsertPagesFromFile()
    {
        if (ActiveDocument == null) return;

        var sourcePath = _dialog.ShowOpenFileDialog("PDF ファイル|*.pdf", "挿入するPDFを選択");
        if (string.IsNullOrEmpty(sourcePath)) return;

        // 挿入位置を尋ねる(現在は末尾固定の簡易UI、後続版で位置指定UIを拡張)
        var positionStr = _dialog.ShowInputDialog(
            $"挿入位置(1〜{ActiveDocument.Document.PageCount + 1}、末尾に挿入する場合は{ActiveDocument.Document.PageCount + 1}):",
            "ページ挿入位置",
            (ActiveDocument.Document.PageCount + 1).ToString());
        if (string.IsNullOrEmpty(positionStr)) return;
        if (!int.TryParse(positionStr, out var position) ||
            position < 1 || position > ActiveDocument.Document.PageCount + 1)
        {
            _dialog.ShowError("挿入位置の入力が不正です。", "エラー");
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "ページを挿入しています...";

            // 編集前にPDFiumキャッシュをいったん閉じて、ファイルロックを解放
            _renderer.Close(ActiveDocument.Document.Id);

            await _editor.InsertPagesAsync(
                ActiveDocument.Document,
                position - 1,  // 1-based → 0-based
                sourcePath);

            // サムネイル再生成のためドキュメントを再読み込み
            await ReloadActiveDocumentAsync();

            StatusMessage = "ページを挿入しました。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ページ挿入に失敗");
            _dialog.ShowError(
                $"ページの挿入に失敗しました。\n\n" +
                $"エラー: {ex.GetType().Name}\n" +
                $"メッセージ: {ex.Message}\n\n" +
                $"内部例外: {ex.InnerException?.Message ?? "(なし)"}",
                "エラー");
            StatusMessage = "ページ挿入に失敗しました。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// ページ抽出(選択ページを別PDFとして保存)
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task ExtractPages()
    {
        if (ActiveDocument == null) return;

        // 元PDFのパスを先に確定しておく(未保存ならエラー)
        var sourceFilePath = ActiveDocument.Document.FilePath;
        if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath))
        {
            _dialog.ShowError("先にPDFを保存してから実行してください。", "エラー");
            return;
        }

        // 抽出するページ範囲を尋ねる(例: "1,3,5-7,10")
        var rangeStr = _dialog.ShowInputDialog(
            $"抽出するページ番号(例: 1,3,5-7,10) [1〜{ActiveDocument.Document.PageCount}]:",
            "ページ抽出",
            "1");
        if (string.IsNullOrEmpty(rangeStr)) return;

        List<int> indices;
        try
        {
            indices = ParsePageRange(rangeStr, ActiveDocument.Document.PageCount);
            if (indices.Count == 0)
            {
                _dialog.ShowError("有効なページ番号が指定されていません。", "エラー");
                return;
            }
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"ページ範囲の指定が不正です:\n{ex.Message}", "エラー");
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"{indices.Count}ページを抽出中...";

            // 元ファイル名を先に記憶(タブ切替後はFilePathが変わるため)
            var sourceBaseName = Path.GetFileNameWithoutExtension(sourceFilePath);

            // 一時ファイルに抽出
            var tempPath = CreateTempPdfPath();
            await _editor.ExtractPagesAsync(
                sourceFilePath,
                indices,
                tempPath);

            // 新規タブとして開く(元PDFは閉じない)
            await OpenFromPathAsync(tempPath);

            // 重要: FilePath は一時パスのまま保持(空にすると保存が必ず失敗する)
            // プレビュー対応表に登録し、保存時に元名がデフォルトになるようにする
            if (ActiveDocument != null)
            {
                _previewOriginalNames[tempPath] = sourceBaseName + "_抽出";
                ActiveDocument.IsModified = true;
                ActiveDocument.Document.Metadata.Title = $"{sourceBaseName} (抽出)";
            }
            StatusMessage = $"[反映済み] {indices.Count}ページを抽出。画面で確認後、Ctrl+Shift+S で保存してください。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ページ抽出に失敗");
            _dialog.ShowError(
                $"ページの抽出に失敗しました。\n\nエラー: {ex.GetType().Name}\nメッセージ: {ex.Message}",
                "エラー");
            StatusMessage = "ページ抽出に失敗しました。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 白紙ページを追加
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task InsertBlankPage()
    {
        if (ActiveDocument == null) return;

        // 簡易UI: 位置とサイズを2回に分けて尋ねる
        var positionStr = _dialog.ShowInputDialog(
            $"挿入位置(1〜{ActiveDocument.Document.PageCount + 1}):",
            "白紙ページ挿入位置",
            (ActiveDocument.Document.PageCount + 1).ToString());
        if (string.IsNullOrEmpty(positionStr)) return;
        if (!int.TryParse(positionStr, out var position) ||
            position < 1 || position > ActiveDocument.Document.PageCount + 1)
        {
            _dialog.ShowError("挿入位置の入力が不正です。", "エラー");
            return;
        }

        var sizeStr = _dialog.ShowInputDialog(
            "サイズ(A4/A3/A5/B5/Letter/Legal/Match):",
            "用紙サイズ",
            "A4");
        if (string.IsNullOrEmpty(sizeStr)) return;
        if (!TryParseBlankPageSize(sizeStr, out var size))
        {
            _dialog.ShowError("サイズの指定が不正です。", "エラー");
            return;
        }

        var orientStr = _dialog.ShowInputDialog(
            "向き(縦/横):",
            "用紙の向き",
            "縦");
        if (string.IsNullOrEmpty(orientStr)) return;
        var orientation = orientStr.Contains("横") || orientStr.Equals("L", StringComparison.OrdinalIgnoreCase)
            ? BlankPageOrientation.Landscape
            : BlankPageOrientation.Portrait;

        try
        {
            IsBusy = true;
            StatusMessage = "白紙ページを追加しています...";

            // 編集前にPDFiumキャッシュをいったん閉じて、ファイルロックを解放
            _renderer.Close(ActiveDocument.Document.Id);

            await _editor.InsertBlankPageAsync(
                ActiveDocument.Document,
                position - 1,
                size,
                orientation);

            // ドキュメント再読み込み
            await ReloadActiveDocumentAsync();

            StatusMessage = "白紙ページを追加しました。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "白紙ページ追加に失敗");
            _dialog.ShowError(
                $"白紙ページの追加に失敗しました。\n\n" +
                $"エラー: {ex.GetType().Name}\n" +
                $"メッセージ: {ex.Message}\n\n" +
                $"内部例外: {ex.InnerException?.Message ?? "(なし)"}",
                "エラー");
            StatusMessage = "白紙ページ追加に失敗しました。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// PDFプロパティ編集(タイトル・作者・件名・キーワード)
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task EditProperties()
    {
        if (ActiveDocument == null) return;
        if (string.IsNullOrEmpty(ActiveDocument.Document.FilePath))
        {
            _dialog.ShowError("先にPDFを保存してから編集してください。", "エラー");
            return;
        }

        try
        {
            IsBusy = true;
            // 現在の値を取得
            var current = await _editor.GetPropertiesAsync(ActiveDocument.Document.FilePath);

            // 簡易: 順番にダイアログで尋ねる(後続版で1画面UIに拡張)
            var title = _dialog.ShowInputDialog("タイトル:", "PDFプロパティ - タイトル", current.Title);
            if (title == null) return;  // キャンセル

            var author = _dialog.ShowInputDialog("作成者:", "PDFプロパティ - 作成者", current.Author);
            if (author == null) return;

            var subject = _dialog.ShowInputDialog("件名:", "PDFプロパティ - 件名", current.Subject);
            if (subject == null) return;

            var keywords = _dialog.ShowInputDialog("キーワード(カンマ区切り):", "PDFプロパティ - キーワード", current.Keywords);
            if (keywords == null) return;

            var newProps = current with
            {
                Title = title,
                Author = author,
                Subject = subject,
                Keywords = keywords,
                Creator = "PdfStudio v0.2",
            };

            StatusMessage = "プロパティを更新しています...";
            await _editor.UpdatePropertiesAsync(ActiveDocument.Document, newProps);
            StatusMessage = "プロパティを更新しました。";
            _dialog.ShowInfo("PDFのプロパティを更新しました。", "完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "プロパティ更新に失敗");
            _dialog.ShowError($"プロパティの更新に失敗しました:\n{ex.Message}", "エラー");
            StatusMessage = "プロパティ更新に失敗しました。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// PDFプロパティ表示(読み取り専用)
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task ShowProperties()
    {
        if (ActiveDocument == null) return;
        if (string.IsNullOrEmpty(ActiveDocument.Document.FilePath))
        {
            _dialog.ShowError("先にPDFを保存してください。", "エラー");
            return;
        }

        try
        {
            var props = await _editor.GetPropertiesAsync(ActiveDocument.Document.FilePath);
            var fileInfo = new FileInfo(ActiveDocument.Document.FilePath);

            var msg =
                $"ファイル名: {Path.GetFileName(ActiveDocument.Document.FilePath)}\n" +
                $"パス: {ActiveDocument.Document.FilePath}\n" +
                $"サイズ: {fileInfo.Length / 1024.0:N1} KB\n" +
                $"ページ数: {ActiveDocument.Document.PageCount}\n" +
                "\n--- メタデータ ---\n" +
                $"タイトル: {props.Title}\n" +
                $"作成者: {props.Author}\n" +
                $"件名: {props.Subject}\n" +
                $"キーワード: {props.Keywords}\n" +
                $"作成プログラム: {props.Creator}\n" +
                $"生成プログラム: {props.Producer}\n" +
                $"作成日時: {props.CreationDate?.ToString("yyyy/MM/dd HH:mm:ss") ?? "-"}\n" +
                $"更新日時: {props.ModificationDate?.ToString("yyyy/MM/dd HH:mm:ss") ?? "-"}\n";

            _dialog.ShowInfo(msg, "PDFプロパティ");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "プロパティ取得に失敗");
            _dialog.ShowError($"プロパティの取得に失敗しました:\n{ex.Message}", "エラー");
        }
    }

    // ---------------------- ヘルパー ----------------------

    /// <summary>
    /// "1,3,5-7,10" のような文字列を 0-based ページ番号リストに変換。
    /// </summary>
    private static List<int> ParsePageRange(string input, int totalPages)
    {
        var result = new HashSet<int>();
        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Contains('-'))
            {
                var rangeParts = trimmed.Split('-');
                if (rangeParts.Length != 2)
                    throw new FormatException($"不正な範囲指定: {trimmed}");
                if (!int.TryParse(rangeParts[0], out var start) || !int.TryParse(rangeParts[1], out var end))
                    throw new FormatException($"不正な数値: {trimmed}");
                if (start > end) (start, end) = (end, start);
                for (int i = start; i <= end; i++)
                {
                    if (i >= 1 && i <= totalPages)
                        result.Add(i - 1);
                }
            }
            else
            {
                if (!int.TryParse(trimmed, out var page))
                    throw new FormatException($"不正な数値: {trimmed}");
                if (page >= 1 && page <= totalPages)
                    result.Add(page - 1);
            }
        }
        return result.OrderBy(x => x).ToList();
    }

    private static bool TryParseBlankPageSize(string input, out BlankPageSize size)
    {
        var trimmed = input.Trim();
        switch (trimmed.ToUpperInvariant())
        {
            case "A3": size = BlankPageSize.A3; return true;
            case "A4": size = BlankPageSize.A4; return true;
            case "A5": size = BlankPageSize.A5; return true;
            case "B5": size = BlankPageSize.B5; return true;
            case "LETTER": size = BlankPageSize.Letter; return true;
            case "LEGAL": size = BlankPageSize.Legal; return true;
            case "MATCH":
            case "MATCHFIRST":
            case "MATCHFIRSTPAGE":
            case "同じ":
                size = BlankPageSize.MatchFirstPage; return true;
            default:
                size = BlankPageSize.A4;
                return false;
        }
    }

    /// <summary>
    /// アクティブなドキュメントを閉じて、同じパスから再オープンする。
    /// 編集操作後にビュー(サムネイル)を更新する目的で使用。
    /// </summary>
    private async Task ReloadActiveDocumentAsync()
    {
        if (ActiveDocument == null) return;
        var path = ActiveDocument.Document.FilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        var docId = ActiveDocument.Document.Id;
        OpenDocuments.Remove(ActiveDocument);
        _renderer.Close(docId);

        // 古いドキュメントインスタンスを対象とするUndo履歴は無効になるため破棄
        _undoStack.Clear();

        await OpenFromPathAsync(path);
    }

    // ==================== プレビュー方式のヘルパー (v0.4.1) ====================

    /// <summary>
    /// 一意の一時PDFファイルパスを生成する。
    /// </summary>
    private static string CreateTempPdfPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PdfStudio");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"preview_{Guid.NewGuid():N}.pdf");
    }

    /// <summary>
    /// アクティブドキュメントを一時PDFに置き換えてプレビュー表示する。
    /// ユーザーは Ctrl+Shift+S で任意の場所に保存可能。
    /// </summary>
    /// <param name="tempPath">一時PDFのパス</param>
    /// <param name="actionDescription">操作内容(タイトルバー表示用)</param>
    private async Task ReplaceActiveDocumentWithTempAsync(string tempPath, string actionDescription)
    {
        if (ActiveDocument is null)
        {
            _logger.LogWarning("Replace: ActiveDocument is null - 中断");
            return;
        }
        if (!File.Exists(tempPath))
        {
            _logger.LogError("Replace: 一時PDFが存在しない: {Path}", tempPath);
            throw new FileNotFoundException("プレビュー用の一時PDFが見つかりません。", tempPath);
        }

        var fileSize = new FileInfo(tempPath).Length;
        _logger.LogInformation(
            "プレビュー切替: 一時PDF={Path}, サイズ={Size}B, 操作={Action}",
            tempPath, fileSize, actionDescription);

        // 元ファイル名を記憶
        var originalFilePath = ActiveDocument.Document.FilePath;
        var originalBaseName = string.IsNullOrEmpty(originalFilePath)
            ? "untitled"
            : Path.GetFileNameWithoutExtension(originalFilePath);

        // 元のファイルが既にプレビューだった場合は、最初の元名を引き継ぐ
        if (_previewOriginalNames.TryGetValue(originalFilePath ?? "", out var prevName))
        {
            originalBaseName = prevName;
        }

        // 現在のドキュメントを閉じる
        var docId = ActiveDocument.Document.Id;
        OpenDocuments.Remove(ActiveDocument);
        _renderer.Close(docId);

        // 一時PDFを開く
        await OpenFromPathAsync(tempPath);

        // 重要: FilePath は一時PDFのパスを保持する
        // (空にすると後続の SaveAsync が「元PDFが見つからない」で必ず失敗するため)
        // 代わりに _previewOriginalNames に登録して「プレビュー中」であることを管理する
        if (ActiveDocument != null)
        {
            _previewOriginalNames[tempPath] = originalBaseName;
            ActiveDocument.IsModified = true;
            ActiveDocument.Document.Metadata.Title = $"{originalBaseName} ({actionDescription})";
            _logger.LogInformation(
                "プレビュー表示完了: ページ数={Pages}, タイトル={Title}, 一時パス={Temp}",
                ActiveDocument.Document.PageCount,
                ActiveDocument.Document.Metadata.Title,
                tempPath);
        }
        else
        {
            _logger.LogWarning("プレビュー切替後 ActiveDocument が null");
        }
    }

    /// <summary>
    /// プレビュー中の一時PDFパス → 元ファイルのベース名 の対応表。
    /// このパスを FilePath に持つドキュメントは「未保存のプレビュー」として扱う。
    /// </summary>
    private readonly Dictionary<string, string> _previewOriginalNames =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 指定ドキュメントがプレビュー中(一時PDF)かどうか。
    /// </summary>
    private bool IsPreviewDocument(PdfDocumentViewModel? doc) =>
        doc is not null
        && !string.IsNullOrEmpty(doc.Document.FilePath)
        && _previewOriginalNames.ContainsKey(doc.Document.FilePath);

    // ==================== v0.3 新機能 ====================

    // ---------------------- ズーム ----------------------

    private const double MinZoom = 0.1;
    private const double MaxZoom = 5.0;
    private const double ZoomStep = 0.25;

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private void ZoomIn()
    {
        if (ActiveDocument is null) return;
        var newZoom = Math.Min(MaxZoom, ActiveDocument.ZoomLevel + ZoomStep);
        ActiveDocument.ZoomMode = ZoomMode.Custom;
        ActiveDocument.ZoomLevel = newZoom;
        StatusMessage = $"ズーム: {newZoom:P0}";
    }

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private void ZoomOut()
    {
        if (ActiveDocument is null) return;
        var newZoom = Math.Max(MinZoom, ActiveDocument.ZoomLevel - ZoomStep);
        ActiveDocument.ZoomMode = ZoomMode.Custom;
        ActiveDocument.ZoomLevel = newZoom;
        StatusMessage = $"ズーム: {newZoom:P0}";
    }

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private void ZoomActual()
    {
        if (ActiveDocument is null) return;
        ActiveDocument.ZoomMode = ZoomMode.Actual;
        ActiveDocument.ZoomLevel = 1.0;
        StatusMessage = "ズーム: 100%";
    }

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private void ZoomFitPage()
    {
        if (ActiveDocument?.CurrentPage is null) return;
        var page = ActiveDocument.CurrentPage;
        // ビューポートサイズが0の場合は1.0にフォールバック
        if (ActiveDocument.ViewportWidth <= 0 || ActiveDocument.ViewportHeight <= 0
            || page.Width <= 0 || page.Height <= 0)
        {
            ActiveDocument.ZoomLevel = 1.0;
        }
        else
        {
            // ポイント(1pt = 1/72 inch) を 96 DPI 換算で表示
            var pageWPx = page.Width * 96.0 / 72.0;
            var pageHPx = page.Height * 96.0 / 72.0;
            var scaleW = ActiveDocument.ViewportWidth / pageWPx;
            var scaleH = ActiveDocument.ViewportHeight / pageHPx;
            ActiveDocument.ZoomLevel = Math.Max(MinZoom, Math.Min(scaleW, scaleH) * 0.95);
        }
        ActiveDocument.ZoomMode = ZoomMode.FitPage;
        StatusMessage = $"ズーム: ページ全体 ({ActiveDocument.ZoomLevel:P0})";
    }

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private void ZoomFitWidth()
    {
        if (ActiveDocument?.CurrentPage is null) return;
        var page = ActiveDocument.CurrentPage;
        if (ActiveDocument.ViewportWidth <= 0 || page.Width <= 0)
        {
            ActiveDocument.ZoomLevel = 1.0;
        }
        else
        {
            var pageWPx = page.Width * 96.0 / 72.0;
            ActiveDocument.ZoomLevel = Math.Max(MinZoom, ActiveDocument.ViewportWidth / pageWPx * 0.95);
        }
        ActiveDocument.ZoomMode = ZoomMode.FitWidth;
        StatusMessage = $"ズーム: 幅に合わせる ({ActiveDocument.ZoomLevel:P0})";
    }

    // ---------------------- ページ移動 ----------------------

    // ページ移動中フラグ(連打時の暴走防止)
    private bool _isNavigating;

    [RelayCommand(CanExecute = nameof(CanGoToFirstPage))]
    private async Task FirstPageAsync()
    {
        if (ActiveDocument is null) return;
        if (_isNavigating) return;
        try { _isNavigating = true; await ActiveDocument.ShowPageAsync(0); }
        finally { _isNavigating = false; }
    }

    [RelayCommand(CanExecute = nameof(CanGoToLastPage))]
    private async Task LastPageAsync()
    {
        if (ActiveDocument is null) return;
        if (_isNavigating) return;
        var last = ActiveDocument.Document.Pages.Count - 1;
        if (last < 0) return;
        try { _isNavigating = true; await ActiveDocument.ShowPageAsync(last); }
        finally { _isNavigating = false; }
    }

    [RelayCommand(CanExecute = nameof(CanGoToPrevPage))]
    private async Task PrevPageAsync()
    {
        if (ActiveDocument?.CurrentPage is null) return;
        if (_isNavigating) return;
        var prev = ActiveDocument.CurrentPage.Index - 1;
        if (prev < 0) return;
        try { _isNavigating = true; await ActiveDocument.ShowPageAsync(prev); }
        finally { _isNavigating = false; }
    }

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private async Task NextPageAsync()
    {
        if (ActiveDocument?.CurrentPage is null) return;
        if (_isNavigating) return;
        var next = ActiveDocument.CurrentPage.Index + 1;
        if (next >= ActiveDocument.Document.Pages.Count) return;
        try { _isNavigating = true; await ActiveDocument.ShowPageAsync(next); }
        finally { _isNavigating = false; }
    }

    private bool CanGoToFirstPage() =>
        ActiveDocument?.CurrentPage is not null
        && ActiveDocument.CurrentPage.Index > 0;

    private bool CanGoToLastPage() =>
        ActiveDocument?.CurrentPage is not null
        && ActiveDocument.CurrentPage.Index < ActiveDocument.Document.Pages.Count - 1;

    private bool CanGoToPrevPage() => CanGoToFirstPage();
    private bool CanGoToNextPage() => CanGoToLastPage();

    /// <summary>
    /// 指定ページ番号(1始まり)へ移動するコマンド
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task GoToPageAsync()
    {
        if (ActiveDocument is null) return;
        var input = _dialog.ShowInputDialog(
            $"移動先のページ番号(1〜{ActiveDocument.Document.PageCount}):",
            "ページへ移動",
            (ActiveDocument.CurrentPage?.DisplayNumber ?? 1).ToString());
        if (string.IsNullOrEmpty(input)) return;
        if (!int.TryParse(input, out var page) || page < 1 || page > ActiveDocument.Document.PageCount)
        {
            _dialog.ShowError("ページ番号が不正です。", "エラー");
            return;
        }
        await ActiveDocument.ShowPageAsync(page - 1);
        StatusMessage = $"ページ {page} に移動しました。";
    }

    // ---------------------- 印刷 ----------------------

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task PrintAsync()
    {
        if (ActiveDocument is null) return;
        try
        {
            IsBusy = true;
            StatusMessage = "印刷準備中...";
            var printed = await _printService.PrintAsync(ActiveDocument.Document);
            StatusMessage = printed ? "印刷を実行しました。" : "印刷はキャンセルされました。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "印刷に失敗");
            _dialog.ShowError(
                $"印刷に失敗しました。\n\n" +
                $"エラー: {ex.GetType().Name}\n" +
                $"メッセージ: {ex.Message}",
                "エラー");
            StatusMessage = "印刷に失敗しました。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------------------- 検索 ----------------------

    /// <summary>
    /// 検索パネルの表示切替(Ctrl+F でトリガー)。
    /// </summary>
    [RelayCommand]
    private void ShowSearchPanel()
    {
        IsSearchPanelVisible = !IsSearchPanelVisible;
        if (!IsSearchPanelVisible)
        {
            // パネルを閉じるときは結果もクリア
            SearchResults.Clear();
            SearchResultIndex = -1;
            NotifySearchResultCommandsCanExecuteChanged();
        }
    }

    /// <summary>
    /// 検索を実行する。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecuteSearch))]
    private async Task ExecuteSearchAsync()
    {
        if (ActiveDocument is null) return;
        if (string.IsNullOrEmpty(SearchQuery)) return;
        if (string.IsNullOrEmpty(ActiveDocument.Document.FilePath))
        {
            _dialog.ShowError("検索対象のPDFが保存されていません。", "エラー");
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"'{SearchQuery}' を検索中...";

            var results = await _searchService.SearchAsync(
                ActiveDocument.Document.FilePath,
                SearchQuery,
                SearchCaseSensitive);

            SearchResults.Clear();
            foreach (var r in results)
                SearchResults.Add(r);
            NotifySearchResultCommandsCanExecuteChanged();

            if (SearchResults.Count > 0)
            {
                SearchResultIndex = 0;
                await NavigateToSearchResultAsync(0);
                StatusMessage = $"{SearchResults.Count} 件見つかりました。(1/{SearchResults.Count})";
            }
            else
            {
                SearchResultIndex = -1;
                StatusMessage = "該当なし。";
                _dialog.ShowInfo($"'{SearchQuery}' は見つかりませんでした。", "検索");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "検索に失敗");
            _dialog.ShowError(
                $"検索に失敗しました。\n\n" +
                $"エラー: {ex.GetType().Name}\n" +
                $"メッセージ: {ex.Message}",
                "エラー");
            StatusMessage = "検索に失敗しました。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanExecuteSearch() =>
        HasActiveDocument()
        && !string.IsNullOrEmpty(SearchQuery);

    [RelayCommand(CanExecute = nameof(CanGoNextSearchResult))]
    private async Task NextSearchResultAsync()
    {
        if (SearchResults.Count == 0) return;
        var next = (SearchResultIndex + 1) % SearchResults.Count;
        SearchResultIndex = next;
        await NavigateToSearchResultAsync(next);
        StatusMessage = $"検索結果 {next + 1}/{SearchResults.Count}";
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevSearchResult))]
    private async Task PrevSearchResultAsync()
    {
        if (SearchResults.Count == 0) return;
        var prev = SearchResultIndex - 1;
        if (prev < 0) prev = SearchResults.Count - 1;
        SearchResultIndex = prev;
        await NavigateToSearchResultAsync(prev);
        StatusMessage = $"検索結果 {prev + 1}/{SearchResults.Count}";
    }

    private bool CanGoNextSearchResult() => SearchResults.Count > 0;
    private bool CanGoPrevSearchResult() => SearchResults.Count > 0;

    private async Task NavigateToSearchResultAsync(int index)
    {
        if (ActiveDocument is null) return;
        if (index < 0 || index >= SearchResults.Count) return;
        var result = SearchResults[index];
        if (result.PageIndex >= 0 && result.PageIndex < ActiveDocument.Document.PageCount)
        {
            await ActiveDocument.ShowPageAsync(result.PageIndex);
        }
    }

    // SearchQuery 変更時に検索コマンドの活性を更新
    partial void OnSearchQueryChanged(string value)
    {
        ExecuteSearchCommand.NotifyCanExecuteChanged();
    }

    // ==================== v0.4 新機能 ====================

    // ---------------------- ウォーターマーク ----------------------

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task AddWatermarkAsync()
    {
        if (ActiveDocument is null) return;
        if (string.IsNullOrEmpty(ActiveDocument.Document.FilePath))
        {
            _dialog.ShowError("先にPDFを保存してから実行してください。", "エラー");
            return;
        }

        var text = _dialog.ShowInputDialog(
            "ウォーターマークのテキストを入力:",
            "ウォーターマーク追加", "社外秘");
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            IsBusy = true;
            StatusMessage = "ウォーターマークを適用中...";
            var options = new WatermarkOptions { Text = text };

            // 一時ファイルに変更後のPDFを生成
            var tempPath = CreateTempPdfPath();
            await _editor.AddWatermarkAsync(
                ActiveDocument.Document.FilePath, tempPath, options);

            // 現在のドキュメントを一時PDFに置き換えてプレビュー
            await ReplaceActiveDocumentWithTempAsync(tempPath, "ウォーターマーク済み");
            StatusMessage = "[反映済み] ウォーターマーク。画面で確認後、Ctrl+Shift+S で保存してください。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ウォーターマーク追加失敗");
            _dialog.ShowError(
                $"ウォーターマーク追加に失敗しました。\n\nエラー: {ex.GetType().Name}\nメッセージ: {ex.Message}",
                "エラー");
        }
        finally { IsBusy = false; }
    }

    // ---------------------- ページ番号 ----------------------

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task AddPageNumbersAsync()
    {
        if (ActiveDocument is null) return;
        if (string.IsNullOrEmpty(ActiveDocument.Document.FilePath))
        {
            _dialog.ShowError("先にPDFを保存してから実行してください。", "エラー");
            return;
        }

        var format = _dialog.ShowInputDialog(
            "番号フォーマット (例: '{page} / {total}' または 'Page {page}'):",
            "ページ番号フォーマット",
            "{page} / {total}");
        if (string.IsNullOrEmpty(format)) return;

        var posStr = _dialog.ShowInputDialog(
            "配置位置: TL/TC/TR/BL/BC/BR\n(T=上 B=下 L=左 C=中央 R=右)",
            "配置位置", "BC");
        if (string.IsNullOrEmpty(posStr)) return;
        if (!TryParsePosition(posStr, out var position))
        {
            _dialog.ShowError("配置位置の指定が不正です。", "エラー");
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "ページ番号を適用中...";
            var options = new PageNumberOptions { Format = format, Position = position };

            var tempPath = CreateTempPdfPath();
            await _editor.AddPageNumbersAsync(
                ActiveDocument.Document.FilePath, tempPath, options);

            await ReplaceActiveDocumentWithTempAsync(tempPath, "ページ番号付与済み");
            StatusMessage = "[反映済み] ページ番号。画面で確認後、Ctrl+Shift+S で保存してください。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ページ番号追加失敗");
            _dialog.ShowError(
                $"ページ番号追加に失敗しました。\n\nエラー: {ex.GetType().Name}\nメッセージ: {ex.Message}",
                "エラー");
        }
        finally { IsBusy = false; }
    }

    private static bool TryParsePosition(string input, out PageNumberPosition pos)
    {
        var t = input.Trim().ToUpperInvariant();
        switch (t)
        {
            case "TL": pos = PageNumberPosition.TopLeft; return true;
            case "TC": pos = PageNumberPosition.TopCenter; return true;
            case "TR": pos = PageNumberPosition.TopRight; return true;
            case "BL": pos = PageNumberPosition.BottomLeft; return true;
            case "BC": pos = PageNumberPosition.BottomCenter; return true;
            case "BR": pos = PageNumberPosition.BottomRight; return true;
            default: pos = PageNumberPosition.BottomCenter; return false;
        }
    }

    // ---------------------- ヘッダー/フッター ----------------------

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task AddHeaderFooterAsync()
    {
        if (ActiveDocument is null) return;
        if (string.IsNullOrEmpty(ActiveDocument.Document.FilePath))
        {
            _dialog.ShowError("先にPDFを保存してから実行してください。", "エラー");
            return;
        }

        var headerCenter = _dialog.ShowInputDialog(
            "ヘッダー(中央) - 空欄でスキップ。{date}={今日}, {filename}=ファイル名",
            "ヘッダー", "");
        if (headerCenter == null) return;  // キャンセル

        var footerCenter = _dialog.ShowInputDialog(
            "フッター(中央) - 空欄でスキップ",
            "フッター", "{filename} - {date}");
        if (footerCenter == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = "ヘッダー/フッターを適用中...";
            var options = new HeaderFooterOptions
            {
                HeaderCenter = headerCenter ?? "",
                FooterCenter = footerCenter ?? "",
            };

            var tempPath = CreateTempPdfPath();
            await _editor.AddHeaderFooterAsync(
                ActiveDocument.Document.FilePath, tempPath, options);

            await ReplaceActiveDocumentWithTempAsync(tempPath, "ヘッダー/フッター適用済み");
            StatusMessage = "[反映済み] ヘッダー/フッター。画面で確認後、Ctrl+Shift+S で保存してください。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ヘッダー/フッター追加失敗");
            _dialog.ShowError(
                $"ヘッダー/フッター追加に失敗しました。\n\nエラー: {ex.GetType().Name}\nメッセージ: {ex.Message}",
                "エラー");
        }
        finally { IsBusy = false; }
    }

    // ---------------------- 画像エクスポート ----------------------

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task ExportPageAsImageAsync()
    {
        if (ActiveDocument is null) return;
        if (string.IsNullOrEmpty(ActiveDocument.Document.FilePath))
        {
            _dialog.ShowError("先にPDFを保存してから実行してください。", "エラー");
            return;
        }

        var pageStr = _dialog.ShowInputDialog(
            $"エクスポートするページ番号(1〜{ActiveDocument.Document.PageCount}):",
            "ページ選択",
            (ActiveDocument.CurrentPage?.DisplayNumber ?? 1).ToString());
        if (string.IsNullOrEmpty(pageStr)) return;
        if (!int.TryParse(pageStr, out var pageNum)
            || pageNum < 1 || pageNum > ActiveDocument.Document.PageCount)
        {
            _dialog.ShowError("ページ番号が不正です。", "エラー");
            return;
        }

        var formatStr = _dialog.ShowInputDialog(
            "形式 (PNG または JPEG):", "出力形式", "PNG");
        if (string.IsNullOrEmpty(formatStr)) return;
        var fmt = formatStr.Trim().ToUpperInvariant() == "JPEG"
            || formatStr.Trim().ToUpperInvariant() == "JPG"
            ? ImageExportFormat.Jpeg
            : ImageExportFormat.Png;

        var ext = fmt == ImageExportFormat.Jpeg ? "jpg" : "png";
        var defaultName = $"{Path.GetFileNameWithoutExtension(ActiveDocument.Document.FilePath)}_p{pageNum}.{ext}";
        var filter = fmt == ImageExportFormat.Jpeg
            ? "JPEG 画像|*.jpg;*.jpeg"
            : "PNG 画像|*.png";
        var outputPath = _dialog.ShowSaveFileDialog(filter, defaultName, "画像として保存");
        if (string.IsNullOrEmpty(outputPath)) return;

        try
        {
            IsBusy = true;
            StatusMessage = $"ページ {pageNum} を画像にエクスポート中...";
            await _editor.ExportPageAsImageAsync(
                ActiveDocument.Document.FilePath, pageNum - 1, outputPath, fmt);
            StatusMessage = $"画像をエクスポートしました: {Path.GetFileName(outputPath)}";
            _dialog.ShowInfo($"保存しました:\n{outputPath}", "完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "画像エクスポート失敗");
            _dialog.ShowError(
                $"画像エクスポートに失敗しました。\n\nエラー: {ex.GetType().Name}\nメッセージ: {ex.Message}",
                "エラー");
        }
        finally { IsBusy = false; }
    }

    // ---------------------- 一括処理 ----------------------

    [RelayCommand]
    private async Task BatchWatermarkAsync()
    {
        var files = _dialog.ShowOpenMultipleFilesDialog(
            "PDF ファイル|*.pdf", "ウォーターマークを追加する複数PDFを選択");
        if (files == null || files.Length == 0) return;

        var text = _dialog.ShowInputDialog(
            "ウォーターマークのテキスト:", "一括ウォーターマーク", "社外秘");
        if (string.IsNullOrEmpty(text)) return;

        var outDir = _dialog.ShowFolderDialog("出力フォルダを選択");
        if (string.IsNullOrEmpty(outDir)) return;

        try
        {
            IsBusy = true;
            StatusMessage = $"{files.Length}件のPDFに一括処理中...";
            var options = new WatermarkOptions { Text = text };
            var result = await _batchService.ApplyWatermarkAsync(files, outDir, options);
            StatusMessage = $"一括処理完了: 成功 {result.SuccessCount} / 失敗 {result.FailureCount}";

            var msg = $"成功: {result.SuccessCount}件\n失敗: {result.FailureCount}件\n出力先: {outDir}";
            if (result.Errors.Count > 0)
            {
                msg += "\n\nエラー詳細:\n" + string.Join("\n", result.Errors.Take(5));
                if (result.Errors.Count > 5) msg += $"\n... 他 {result.Errors.Count - 5} 件";
            }
            _dialog.ShowInfo(msg, "一括処理結果");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "一括処理失敗");
            _dialog.ShowError(
                $"一括処理に失敗しました。\n\nエラー: {ex.GetType().Name}\nメッセージ: {ex.Message}",
                "エラー");
        }
        finally { IsBusy = false; }
    }

    // ---------------------- 注釈系 ----------------------

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task AddStampAsync()
    {
        if (ActiveDocument is null) return;
        if (string.IsNullOrEmpty(ActiveDocument.Document.FilePath))
        {
            _dialog.ShowError("先にPDFを保存してから実行してください。", "エラー");
            return;
        }
        if (ActiveDocument.Document.Pages.Count == 0)
        {
            _dialog.ShowError("ページがありません。", "エラー");
            return;
        }

        var label = _dialog.ShowInputDialog(
            "スタンプ文字 (例: 承認、却下、社外秘):",
            "電子スタンプ", "承認");
        if (string.IsNullOrEmpty(label)) return;

        // CurrentPage がない場合は最初のページに配置
        // CurrentPage は PageViewModel、Pages[0] は PdfPage なので、
        // インデックス・幅・高さを各々から取得する
        int targetIdx;
        double targetW;
        if (ActiveDocument.CurrentPage is not null)
        {
            targetIdx = ActiveDocument.CurrentPage.Index;
            targetW = ActiveDocument.CurrentPage.Width;
        }
        else
        {
            var firstPage = ActiveDocument.Document.Pages[0];
            targetIdx = firstPage.Index;
            targetW = firstPage.Width;
        }

        var stampOptions = new AnnotationOptions
        {
            Kind = AnnotationKind.Stamp,
            PageIndex = targetIdx,
            X = targetW - 160,
            Y = 30,
            Width = 130,
            Height = 50,
            StampLabel = label,
        };

        try
        {
            IsBusy = true;
            StatusMessage = "スタンプを適用中...";

            var tempPath = CreateTempPdfPath();
            await _annotationService.AddAnnotationAsync(
                ActiveDocument.Document.FilePath, tempPath, stampOptions);

            await ReplaceActiveDocumentWithTempAsync(tempPath, $"スタンプ「{label}」適用済み");
            StatusMessage = "[反映済み] スタンプ。画面で確認後、Ctrl+Shift+S で保存してください。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "スタンプ追加失敗");
            _dialog.ShowError(
                $"スタンプ追加に失敗しました。\n\nエラー: {ex.GetType().Name}\nメッセージ: {ex.Message}",
                "エラー");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private async Task AddStickyNoteAsync()
    {
        if (ActiveDocument is null) return;
        if (string.IsNullOrEmpty(ActiveDocument.Document.FilePath))
        {
            _dialog.ShowError("先にPDFを保存してから実行してください。", "エラー");
            return;
        }
        if (ActiveDocument.Document.Pages.Count == 0)
        {
            _dialog.ShowError("ページがありません。", "エラー");
            return;
        }

        var comment = _dialog.ShowInputDialog(
            "コメント内容:", "付箋を追加", "");
        if (string.IsNullOrEmpty(comment)) return;

        // CurrentPage(PageViewModel) と Pages[0](PdfPage) は別の型のためIndexだけ取得
        int targetIdx = ActiveDocument.CurrentPage?.Index
            ?? ActiveDocument.Document.Pages[0].Index;

        var noteOptions = new AnnotationOptions
        {
            Kind = AnnotationKind.StickyNote,
            PageIndex = targetIdx,
            X = 30,
            Y = 30,
            Width = 20,
            Height = 20,
            Text = comment,
            ColorHex = "#FFEB3B",
        };

        try
        {
            IsBusy = true;
            StatusMessage = "付箋を適用中...";

            var tempPath = CreateTempPdfPath();
            await _annotationService.AddAnnotationAsync(
                ActiveDocument.Document.FilePath, tempPath, noteOptions);

            await ReplaceActiveDocumentWithTempAsync(tempPath, "付箋付与済み");
            StatusMessage = "[反映済み] 付箋(黄色アイコン+コメント)。画面で確認後、Ctrl+Shift+S で保存してください。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "付箋追加失敗");
            _dialog.ShowError(
                $"付箋追加に失敗しました。\n\nエラー: {ex.GetType().Name}\nメッセージ: {ex.Message}",
                "エラー");
        }
        finally { IsBusy = false; }
    }
}
