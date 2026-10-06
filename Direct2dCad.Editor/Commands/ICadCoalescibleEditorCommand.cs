namespace Direct2dCad.Editor.Commands;

/// <summary>A gesture command that can retain the result of a subsequent successful command.</summary>
public interface ICadCoalescibleEditorCommand : ICadEditorCommand
{
    /// <summary>
    /// Merges an already executed command without changing editor state. Preserve
    /// the original undo state and the combined redo effect; return false if incompatible.
    /// </summary>
    bool TryMergeExecuted(ICadEditorCommand subsequent);
}
