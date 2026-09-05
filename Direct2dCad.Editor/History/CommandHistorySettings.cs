using Direct2dCad.Editor.Commands;

namespace Direct2dCad.Editor.History;

public sealed class CommandHistorySettings
{
    public CadCommandBatchUndoMode UndoMode { get; set; } = CadCommandBatchUndoMode.Batch;
    public CadCommandBatchUndoMode RedoMode { get; set; } = CadCommandBatchUndoMode.Batch;

    /// <summary>
    /// Maximum retained undo commands. Zero is unlimited. The newest batch is
    /// always retained in full, even when it alone exceeds this soft limit.
    /// Changes take effect on the next successful document command.
    /// </summary>
    public int MaximumUndoCommands
    {
        get;
        set => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
}
