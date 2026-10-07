using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Direct2dCad.wpf.Controls;
using Direct2dCad.wpf.Services.Input;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed partial class ShortcutRoutingTests
{
    [Fact]
    public void ForwardedFirstEnterMarkedRepeatedConfirmsOnceAndFurtherRepeatsDoNotConfirm() => RunSta(() =>
    {
        using var context = new Context();
        using var canvas = new CadCanvas { DocumentViewModel = context.Document };
        var owner = ShowEnterWindow(canvas);
        try
        {
            StartEnterPolyline(context.Document);
            FocusEnterControl(owner, canvas);
            var source = PresentationSource.FromVisual(canvas);
            Assert.NotNull(source);

            // AvalonDock's proxy has seen the physical first press. WPF labels the
            // forwarded child-HWND delivery as repeated although no confirmation ran yet.
            CadEnterKeyGuard.RememberForward(Keyboard.PrimaryDevice, canvas, source, ModifierKeys.None);
            var forwarded = RepeatedEnterFrom(canvas);
            canvas.HandleCanvasKey(Key.Enter, ModifierKeys.None, forwarded);
            Assert.True(forwarded.Handled);
            Assert.Single(context.Document.CadEditor.Document.Entities.Values, entity => !entity.IsErased);
            Assert.Empty(context.Document.StepInputError);
            var history = context.Document.CadEditor.CreateDocumentHistorySnapshot();

            var held = RepeatedEnterFrom(canvas);
            canvas.HandleCanvasKey(Key.Enter, ModifierKeys.None, held);
            Assert.True(held.Handled);
            Assert.Single(context.Document.CadEditor.Document.Entities.Values, entity => !entity.IsErased);
            Assert.Empty(context.Document.StepInputError);
            Assert.True(context.Document.CadEditor.DocumentHistoryEquals(history));
        }
        finally { CadEnterKeyGuard.Release(Keyboard.PrimaryDevice); owner.Close(); }
    });

    [Theory]
    [InlineData("released")]
    [InlineData("focus-moved")]
    [InlineData("modifiers-changed")]
    [InlineData("different-origin-source")]
    [InlineData("different-remembered-source")]
    [InlineData("detached-origin")]
    public void ForwardedEnterExceptionCannotSurviveReleaseOrCrossInputContexts(string change) => RunSta(() =>
    {
        using var context = new Context();
        using var canvas = new CadCanvas { DocumentViewModel = context.Document, Height = 200 };
        var otherFocus = new Border { Focusable = true, Height = 30 };
        var content = new StackPanel(); content.Children.Add(otherFocus); content.Children.Add(canvas);
        var owner = ShowEnterWindow(content);
        var otherOrigin = new Border { Focusable = true };
        var otherWindow = ShowEnterWindow(otherOrigin);
        try
        {
            FocusEnterControl(owner, canvas);
            var source = PresentationSource.FromVisual(canvas);
            var otherSource = PresentationSource.FromVisual(otherOrigin);
            Assert.NotNull(source); Assert.NotNull(otherSource); Assert.NotSame(source, otherSource);
            CadEnterKeyGuard.RememberForward(Keyboard.PrimaryDevice, canvas,
                change == "different-remembered-source" ? otherSource : source, ModifierKeys.None);

            if (change == "released") CadEnterKeyGuard.Release(Keyboard.PrimaryDevice);
            if (change == "focus-moved") FocusEnterControl(owner, otherFocus);
            DependencyObject origin = change switch
            {
                "different-origin-source" => otherOrigin,
                "detached-origin" => new Border(),
                _ => canvas
            };
            Assert.True(CadEnterKeyGuard.ShouldIgnoreRepeat(RepeatedEnterFrom(origin),
                change == "modifiers-changed" ? ModifierKeys.Control : ModifierKeys.None));

            // A rejected match must also consume the one-shot exception, so restoring
            // the original context later cannot revive an old physical press.
            FocusEnterControl(owner, canvas);
            Assert.True(CadEnterKeyGuard.ShouldIgnoreRepeat(RepeatedEnterFrom(canvas), ModifierKeys.None));
        }
        finally
        {
            CadEnterKeyGuard.Release(Keyboard.PrimaryDevice);
            otherWindow.Close(); owner.Close();
        }
    });

    [Fact]
    public void ForwardedFreshEnterConsumesItsExceptionWithoutWhitelistingTheNextRepeat() => RunSta(() =>
    {
        var target = new Border { Focusable = true };
        var owner = ShowEnterWindow(target);
        try
        {
            FocusEnterControl(owner, target);
            var source = PresentationSource.FromVisual(target); Assert.NotNull(source);
            CadEnterKeyGuard.RememberForward(Keyboard.PrimaryDevice, target, source, ModifierKeys.None);
            var fresh = KeyEvent(Key.Enter); fresh.Source = target;
            Assert.False(CadEnterKeyGuard.ShouldIgnoreRepeat(fresh, ModifierKeys.None));
            Assert.True(CadEnterKeyGuard.ShouldIgnoreRepeat(RepeatedEnterFrom(target), ModifierKeys.None));
        }
        finally { CadEnterKeyGuard.Release(Keyboard.PrimaryDevice); owner.Close(); }
    });

    private static KeyEventArgs RepeatedEnterFrom(DependencyObject origin)
    {
        var e = RepeatedEnter(); e.Source = origin;
        Assert.Same(origin, e.OriginalSource);
        return e;
    }
}
