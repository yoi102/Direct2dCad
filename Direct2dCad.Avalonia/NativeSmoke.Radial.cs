using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Direct2dCad.Avalonia.Controls;
using Direct2dCad.Client.Common.Settings;

namespace Direct2dCad.Avalonia;

internal static partial class NativeSmoke
{
    private static async Task<byte[]> RadialPixelsAsync(MainWindow window, Point center, string? path = null)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Radial menu frame is missing.");
        if (path is not null) frame.Save(path);
        var scale = window.RenderScaling;
        var rect = new PixelRect((int)Math.Round((center.X-124)*scale), (int)Math.Round((center.Y-124)*scale), (int)Math.Round(248*scale), (int)Math.Round(248*scale));
        var stride = rect.Width*4;
        var pixels = new byte[stride*rect.Height];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { frame.CopyPixels(rect, pin.AddrOfPinnedObject(), pixels.Length, stride); }
        finally { pin.Free(); }
        return pixels;
    }

    private static async Task CheckRadialModifierReleaseAsync(MainWindow window, CadCanvas canvas, Point center, NativeSmokeReport report, Action<bool,string> assert, string output)
    {
        window.MouseMove(center + new Vector(0, -76), global::Avalonia.Input.RawInputModifiers.MiddleMouseButton);
        var baseline = await RadialPixelsAsync(window, center);
        void SendKey(bool down, Key key, KeyModifiers modifiers, bool handled = false) => canvas.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = down ? InputElement.KeyDownEvent : InputElement.KeyUpEvent,
            Key = key, KeyModifiers = modifiers, Handled = handled
        });
        var cases = new[]
        {
            (Key.LeftCtrl, KeyModifiers.Control, CadRadialMenuGesture.ControlMiddle),
            (Key.RightCtrl, KeyModifiers.Control, CadRadialMenuGesture.ControlMiddle),
            (Key.LeftShift, KeyModifiers.Shift, CadRadialMenuGesture.ShiftMiddle),
            (Key.RightShift, KeyModifiers.Shift, CadRadialMenuGesture.ShiftMiddle),
            (Key.LeftAlt, KeyModifiers.Alt, CadRadialMenuGesture.AltMiddle),
            (Key.RightAlt, KeyModifiers.Alt, CadRadialMenuGesture.AltMiddle)
        };
        foreach (var (key, modifiers, gesture) in cases)
        {
            SendKey(true, key, modifiers);
            assert(canvas.RadialGesture == gesture, $"{key} did not switch the radial profile.");
            var pressed = await RadialPixelsAsync(window, center, key==Key.LeftCtrl ? Path.Combine(Path.GetDirectoryName(output)!,"radial-control-stationary.png") : null);
            assert(!baseline.AsSpan().SequenceEqual(pressed), $"{key} changed state without repainting the visible radial menu.");
            // Reproduce a consumed KeyUp with a pre-release modifier snapshot.
            // No pointer move is sent between pressing, releasing and capturing.
            SendKey(false, key, modifiers, handled: true);
            assert(canvas.RadialGesture == CadRadialMenuGesture.Middle, $"{key} release retained a stale radial profile until pointer movement.");
            var released = await RadialPixelsAsync(window, center, key==Key.LeftCtrl ? Path.Combine(Path.GetDirectoryName(output)!,"radial-released-stationary.png") : null);
            assert(baseline.AsSpan().SequenceEqual(released), $"{key} release did not repaint the original radial icons with a stationary pointer.");
        }
        report.Passed.Add("Left/right Ctrl, Shift and Alt releases ignore stale/handled KeyUp masks and restore the actual radial pixels without any pointer movement");

        SendKey(true, Key.LeftAlt, KeyModifiers.Alt);
        SendKey(true, Key.LeftCtrl, KeyModifiers.Control|KeyModifiers.Alt);
        SendKey(true, Key.LeftShift, KeyModifiers.Control|KeyModifiers.Alt|KeyModifiers.Shift);
        assert(canvas.RadialGesture==CadRadialMenuGesture.ShiftMiddle,"Combined radial modifiers lost Shift priority.");
        SendKey(false, Key.LeftShift, KeyModifiers.Control|KeyModifiers.Alt|KeyModifiers.Shift, true);
        assert(canvas.RadialGesture==CadRadialMenuGesture.ControlMiddle,"Shift release did not fall back immediately to held Ctrl.");
        SendKey(false, Key.LeftCtrl, KeyModifiers.Control|KeyModifiers.Alt, true);
        assert(canvas.RadialGesture==CadRadialMenuGesture.AltMiddle,"Ctrl release did not fall back immediately to held Alt.");
        SendKey(false, Key.LeftAlt, KeyModifiers.Alt, true);
        SendKey(true, Key.LeftShift, KeyModifiers.Shift);
        SendKey(true, Key.RightShift, KeyModifiers.Shift);
        SendKey(false, Key.LeftShift, KeyModifiers.Shift, true);
        assert(canvas.RadialGesture==CadRadialMenuGesture.ShiftMiddle,"Releasing one Shift key ignored the other held Shift key.");
        SendKey(false, Key.RightShift, KeyModifiers.Shift, true);
        assert(canvas.RadialGesture==CadRadialMenuGesture.Middle,"Releasing the final Shift key left the profile stuck.");
        report.Passed.Add("Combined modifier releases preserve Shift/Ctrl/Alt priority and a still-held opposite Shift key");

        var count = window.Model.LayoutService.Documents.Count();
        SendKey(true, Key.N, KeyModifiers.Control);
        assert(window.Model.LayoutService.Documents.Count()==count && canvas.IsRadialMenuActive,"Ctrl+N escaped the active radial gesture into the application shortcuts.");
        SendKey(false, Key.N, KeyModifiers.None);
        SendKey(true, Key.Escape, KeyModifiers.None);
        assert(!canvas.IsRadialMenuActive && !canvas.HasCapturedPointer,"Radial Escape failed to close its overlay and release capture.");
        SendKey(false, Key.Escape, KeyModifiers.None);
        window.MouseUp(center, MouseButton.Middle);
        window.MouseDown(center, MouseButton.Middle);
        assert(canvas.IsRadialMenuActive && canvas.RadialGesture==CadRadialMenuGesture.Middle,"Reopening the radial menu inherited stale modifiers.");
        report.Passed.Add("Active radial input suppresses application shortcuts; Escape releases capture and the menu reopens with fresh modifiers");
    }
}
