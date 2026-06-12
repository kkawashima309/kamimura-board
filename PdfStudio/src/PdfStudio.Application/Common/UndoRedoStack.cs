namespace PdfStudio.Application.Common;

/// <summary>
/// Undo/Redo 可能な操作コマンド。
/// </summary>
public interface IUndoableCommand
{
    string Description { get; }

    Task ExecuteAsync(CancellationToken ct = default);

    Task UndoAsync(CancellationToken ct = default);
}

/// <summary>
/// Undo/Redo スタック管理。
/// </summary>
public class UndoRedoStack
{
    private readonly Stack<IUndoableCommand> _undo = new();
    private readonly Stack<IUndoableCommand> _redo = new();
    private const int MaxHistory = 100;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public string? NextUndoDescription =>
        _undo.Count > 0 ? _undo.Peek().Description : null;

    public string? NextRedoDescription =>
        _redo.Count > 0 ? _redo.Peek().Description : null;

    public event EventHandler? StateChanged;

    public async Task ExecuteAsync(IUndoableCommand cmd, CancellationToken ct = default)
    {
        await cmd.ExecuteAsync(ct).ConfigureAwait(false);
        _undo.Push(cmd);
        _redo.Clear();
        TrimHistory();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task UndoAsync(CancellationToken ct = default)
    {
        if (!CanUndo) return;
        var cmd = _undo.Pop();
        await cmd.UndoAsync(ct).ConfigureAwait(false);
        _redo.Push(cmd);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RedoAsync(CancellationToken ct = default)
    {
        if (!CanRedo) return;
        var cmd = _redo.Pop();
        await cmd.ExecuteAsync(ct).ConfigureAwait(false);
        _undo.Push(cmd);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TrimHistory()
    {
        if (_undo.Count <= MaxHistory) return;

        var keep = _undo.ToArray().Take(MaxHistory).Reverse().ToArray();
        _undo.Clear();
        foreach (var c in keep) _undo.Push(c);
    }
}
