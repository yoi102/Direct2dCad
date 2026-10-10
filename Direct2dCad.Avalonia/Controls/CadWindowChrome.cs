using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
namespace Direct2dCad.Avalonia.Controls;
internal static class CadWindowChrome
{
    private static readonly Cursor VerticalResize = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor HorizontalResize = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor ForwardResize = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor BackwardResize = new(StandardCursorType.TopRightCorner);
    public static Control Wrap(Window window, Control content)
    {
        Apply(window);
        return content;
    }

    public static void Apply(Window window)
    {
        // Let Windows own the caption, resizing frame and caption-button hit testing.
        window.WindowDecorations = WindowDecorations.Full;
        window.ExtendClientAreaToDecorationsHint = false;
    }

    public static void ApplyFloatingDock(Window window)
    {
        // Dock supplies the title grip and actions; keep only its chrome. A small
        // client-area gutter provides resize handles without a Windows frame.
        window.WindowDecorations = WindowDecorations.None;
        window.ExtendClientAreaToDecorationsHint = false;
        window.CanResize = true;
        window.CanMinimize = false;
        window.ShowInTaskbar = false;
        window.Padding = new Thickness(4);
        window.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            var edge = ResizeEdge(window, e.GetPosition(window));
            window.Cursor = edge is { } value ? value switch
            {
                WindowEdge.North or WindowEdge.South => VerticalResize,
                WindowEdge.East or WindowEdge.West => HorizontalResize,
                WindowEdge.NorthWest or WindowEdge.SouthEast => ForwardResize,
                _ => BackwardResize
            } : null;
        }, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.Handled || !e.GetCurrentPoint(window).Properties.IsLeftButtonPressed || ResizeEdge(window, e.GetPosition(window)) is not { } edge) return;
            e.Handled = true;
            window.BeginResizeDrag(edge, e);
        }, RoutingStrategies.Tunnel);
    }

    private static WindowEdge? ResizeEdge(Window window, Point point)
    {
        if (!window.CanResize || window.WindowState != WindowState.Normal || point.X < 0 || point.Y < 0 || point.X >= window.Bounds.Width || point.Y >= window.Bounds.Height) return null;
        var left = point.X < window.Padding.Left;
        var right = point.X >= window.Bounds.Width - window.Padding.Right;
        var top = point.Y < window.Padding.Top;
        var bottom = point.Y >= window.Bounds.Height - window.Padding.Bottom;
        if (top) return left ? WindowEdge.NorthWest : right ? WindowEdge.NorthEast : WindowEdge.North;
        if (bottom) return left ? WindowEdge.SouthWest : right ? WindowEdge.SouthEast : WindowEdge.South;
        return left ? WindowEdge.West : right ? WindowEdge.East : null;
    }
}
