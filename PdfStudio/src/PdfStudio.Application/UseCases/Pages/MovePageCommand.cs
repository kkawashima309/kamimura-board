using PdfStudio.Application.Common;
using PdfStudio.Domain.Entities;

namespace PdfStudio.Application.UseCases.Pages;

/// <summary>
/// ページ並び替えコマンド。1ページの移動を表現する。
/// </summary>
public class MovePageCommand : IUndoableCommand
{
    private readonly PdfDocument _doc;
    private readonly int _fromIndex;
    private readonly int _toIndex;

    public string Description => $"ページ {_fromIndex + 1} を {_toIndex + 1} に移動";

    public MovePageCommand(PdfDocument doc, int fromIndex, int toIndex)
    {
        _doc = doc;
        _fromIndex = fromIndex;
        _toIndex = toIndex;
    }

    public Task ExecuteAsync(CancellationToken ct = default)
    {
        Move(_fromIndex, _toIndex);
        _doc.IsModified = true;
        return Task.CompletedTask;
    }

    public Task UndoAsync(CancellationToken ct = default)
    {
        Move(_toIndex, _fromIndex);
        _doc.IsModified = true;
        return Task.CompletedTask;
    }

    private void Move(int from, int to)
    {
        if (from < 0 || from >= _doc.Pages.Count) return;
        if (to < 0 || to >= _doc.Pages.Count) return;
        if (from == to) return;

        var page = _doc.Pages[from];
        _doc.Pages.RemoveAt(from);
        _doc.Pages.Insert(to, page);
        for (int i = 0; i < _doc.Pages.Count; i++)
            _doc.Pages[i].Index = i;
    }
}
