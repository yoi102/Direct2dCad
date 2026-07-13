namespace Direct2dCad.ViewModels;

public enum CadCanvasPointerButton
{
    None,
    Left,
    Middle,
    Right
}

[Flags]
public enum CadCanvasInputModifiers
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alt = 1 << 2
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
