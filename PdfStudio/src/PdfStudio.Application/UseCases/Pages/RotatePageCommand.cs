using PdfStudio.Application.Common;
using PdfStudio.Domain.Entities;

namespace PdfStudio.Application.UseCases.Pages;

/// <summary>
/// ページ回転コマンド（90/180/270度）。
/// </summary>
public class RotatePageCommand : IUndoableCommand
{
    private readonly PdfDocument _doc;
    private readonly int _pageIndex;
    private readonly int _delta;
    private int _previousRotation;

    public string Description => $"ページ {_pageIndex + 1} を {_delta}度回転";

    public RotatePageCommand(PdfDocument doc, int pageIndex, int delta)
    {
        if (delta % 90 != 0)
            throw new ArgumentException("回転角度は90度単位で指定してください。", nameof(delta));

        _doc = doc;
        _pageIndex = pageIndex;
        _delta = delta;
    }

    public Task ExecuteAsync(CancellationToken ct = default)
    {
        var page = _doc.Pages[_pageIndex];
        _previousRotation = page.Rotation;
        page.Rotation = NormalizeRotation(page.Rotation + _delta);
        _doc.IsModified = true;
        return Task.CompletedTask;
    }

    public Task UndoAsync(CancellationToken ct = default)
    {
        _doc.Pages[_pageIndex].Rotation = _previousRotation;
        _doc.IsModified = true;
        return Task.CompletedTask;
    }

    private static int NormalizeRotation(int degrees)
    {
        var r = degrees % 360;
        return r < 0 ? r + 360 : r;
    }
}
