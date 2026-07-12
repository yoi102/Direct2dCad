namespace Direct2dCad.CommandLine;

public interface ICadCommandLineContext
{
    string DocumentName { get; }
    int EntityCount { get; }
    int SelectionCount { get; }
    CadCommandLineDrawingMode ToolMode { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }

    void SetToolMode(CadCommandLineDrawingMode mode);
    void Cancel();
    void Undo();
    void Redo();
    void FitToWindow();
    int SelectAll();
    int DeleteSelection();
    bool CopySelection();
    bool BeginPaste();
}
