using PdfStudio.Application.Common;
using PdfStudio.Domain.Entities;

namespace PdfStudio.Application.UseCases.Pages;

/// <summary>
/// ページ削除コマンド（Undo対応）。
/// 注意: MVP段階では Document の Pages リストのみを操作。
/// 物理ファイルへの反映は SaveAsync 時に IPdfEditor が行う。
/// </summary>
public class DeletePageCommand : IUndoableCommand
{
    private readonly PdfDocument _doc;
    private readonly int _pageIndex;
    private PdfPage? _removedPage;

    public string Description => $"ページ {_pageIndex + 1} を削除";

    public DeletePageCommand(PdfDocument doc, int pageIndex)
    {
        _doc = doc;
        _pageIndex = pageIndex;
    }

    public Task ExecuteAsync(CancellationToken ct = default)
    {
        if (_pageIndex < 0 || _pageIndex >= _doc.Pages.Count)
            throw new ArgumentOutOfRangeException(nameof(_pageIndex));

        _removedPage = _doc.Pages[_pageIndex];
        _doc.Pages.RemoveAt(_pageIndex);
        ReindexPages();
        _doc.IsModified = true;
        return Task.CompletedTask;
    }

    public Task UndoAsync(CancellationToken ct = default)
    {
        if (_removedPage is not null)
        {
            _doc.Pages.Insert(_pageIndex, _removedPage);
            ReindexPages();
            _doc.IsModified = true;
        }
        return Task.CompletedTask;
    }

    private void ReindexPages()
    {
        for (int i = 0; i < _doc.Pages.Count; i++)
            _doc.Pages[i].Index = i;
    }
}
