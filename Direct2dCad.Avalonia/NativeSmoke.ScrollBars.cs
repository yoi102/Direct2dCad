using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Direct2dCad.Avalonia;

internal static partial class NativeSmoke
{
    private static async Task CheckCompactScrollBarAsync(ScrollViewer viewer,Action<bool,string> assert,string output,string name)
    {
        var root=TopLevel.GetTopLevel(viewer)!;
        root.UpdateLayout();
        var bar=viewer.GetVisualDescendants().OfType<ScrollBar>().Single(item=>item.Orientation==Orientation.Vertical && item.IsEffectivelyVisible);
        var thumb=bar.GetVisualDescendants().OfType<Thumb>().Single(item=>item.Name=="PART_Thumb");
        assert(bar.Maximum>0 && bar.Bounds.Width<=16,$"{name}: vertical scroll bar is oversized or not scrollable: {bar.Bounds}, maximum={bar.Maximum}.");
        var point=thumb.TranslatePoint(new Point(thumb.Bounds.Width/2,thumb.Bounds.Height/2),root)!.Value;
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseMove(root,point);
        await Task.Delay(bar.ShowDelay+TimeSpan.FromMilliseconds(180));root.UpdateLayout();
        assert(bar.IsExpanded && bar.Bounds.Width<=16 && thumb.Bounds.Width<=12,$"{name}: hovering expands the scroll bar beyond its bounded width: bar={bar.Bounds}, thumb={thumb.Bounds}, expanded={bar.IsExpanded}.");
        point=thumb.TranslatePoint(new Point(thumb.Bounds.Width/2,thumb.Bounds.Height/2),root)!.Value;
        var previous=viewer.Offset.Y;
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(root,point,MouseButton.Left);
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseMove(root,new Point(point.X,point.Y+12),RawInputModifiers.LeftMouseButton);
        await Task.Delay(150);root.UpdateLayout();
        assert(bar.Bounds.Width<=16 && thumb.Bounds.Width<=12,$"{name}: pressed scroll thumb became oversized: bar={bar.Bounds}, thumb={thumb.Bounds}.");
        using(var frame=global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(root))frame?.Save(Path.Combine(Path.GetDirectoryName(output)!,name+"-pressed.png"));
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(root,new Point(point.X,point.Y+12),MouseButton.Left);
        assert(viewer.Offset.Y>previous,$"{name}: dragging the compact thumb did not scroll the content.");
    }
}
