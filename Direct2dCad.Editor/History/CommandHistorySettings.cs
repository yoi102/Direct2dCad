using Direct2dCad.Editor.Commands;

namespace Direct2dCad.Editor.History;

public sealed class CommandHistorySettings
{
    public CadCommandBatchUndoMode UndoMode { get; set; } = CadCommandBatchUndoMode.Batch;
    public CadCommandBatchUndoMode RedoMode { get; set; } = CadCommandBatchUndoMode.Batch;
}
