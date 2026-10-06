using Direct2dCad.Editor.Commands;

namespace Direct2dCad.Editor.History;

public sealed class CommandHistorySettings
{
    /// <summary>Soft managed payload budget. Newest batch stays intact. Zero disables this limit.</summary>
    public long MaximumUndoBytes {get;set=>field=value>=0?value:throw new ArgumentOutOfRangeException(nameof(value));}=256L*1024*1024;
    public CadCommandBatchUndoMode UndoMode { get; set; } = CadCommandBatchUndoMode.Batch;
    public CadCommandBatchUndoMode RedoMode { get; set; } = CadCommandBatchUndoMode.Batch;

    /// <summary>
    /// Maximum retained undo commands. Zero is unlimited. The newest batch is
    /// always retained in full, even when it alone exceeds this soft limit.
    /// Changes take effect on the next successful command in the owning manager.
    /// </summary>
    public int MaximumUndoCommands
    {
        get;
        set => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
}
