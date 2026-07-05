namespace Direct2dCad.ViewModels;

public enum CadCanvasPointerButton
{
    None,
    Left,
    Middle,
    Right
}

public enum CadCanvasCursorKind
{
    Cross,
    Hand
}

public readonly record struct CadCanvasInteractionResult(
    bool Handled,
    bool CaptureMouse = false,
    bool ReleaseMouseCapture = false,
    CadCanvasCursorKind? Cursor = null)
{
    public static CadCanvasInteractionResult NotHandled { get; } = new(false);
    public static CadCanvasInteractionResult HandledOnly { get; } = new(true);
}
