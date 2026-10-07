using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.wpf;
using Direct2dCad.wpf.Controls;
using Direct2dCad.wpf.Services.Input;
using Direct2dCad.wpf.Views.Toolboxes;
using Direct2dCad.wpf.Views.Toolboxes.EntityProperty;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed partial class ShortcutRoutingTests
{
    [Theory]
    [InlineData(Key.Enter, ModifierKeys.Control, false)]
    [InlineData(Key.Enter, ModifierKeys.Shift, false)]
    [InlineData(Key.Enter, ModifierKeys.Alt, false)]
    [InlineData(Key.ImeProcessed, ModifierKeys.None, false)]
    [InlineData(Key.DeadCharProcessed, ModifierKeys.None, false)]
    [InlineData(Key.Enter, ModifierKeys.None, true)]
    public void EnterFallbackRespectsModifiersImeAndModalState(Key key, ModifierKeys modifiers, bool modal) => RunSta(() =>
    {
        var calls = 0; var panel = new Border(); var owner = new Window { Content = panel };
        using var router = new CadWindowShortcutRouter(owner, _ => null, () => modal, _ => true,
            confirm: _ => { calls++; return true; });
        Assert.False(router.ProcessEnter(key, modifiers, panel));
        Assert.False(router.Process(Key.Enter, ModifierKeys.None, panel)); // Preview must not preempt the focused control.
        Assert.Equal(0, calls);
        owner.Close();
    });

    [Fact]
    public void EnterBubblesOnlyAfterTheFocusedChildDeclinesIt() => RunSta(() =>
    {
        var calls = 0; var panel = new Border { Focusable = true }; var owner = new Window { Content = panel };
        using var router = new CadWindowShortcutRouter(owner, _ => null, () => false, _ => true,
            confirm: _ => { calls++; return true; });
        var first = KeyEvent(Key.Enter); panel.RaiseEvent(first);
        Assert.True(first.Handled); Assert.Equal(1, calls);
        panel.KeyDown += (_, e) => e.Handled = true;
        panel.RaiseEvent(KeyEvent(Key.Enter));
        Assert.Equal(1, calls);
        owner.Close();
    });

    [Fact]
    public void HeldEnterDoesNotConfirmAgainOrSwallowNativeEditorGestures() => RunSta(() =>
    {
        var calls = 0; var panel = new Border(); var owner = new Window { Content = panel };
        using var router = new CadWindowShortcutRouter(owner, _ => null, () => false, _ => true,
            confirm: _ => { calls++; return true; });
        Assert.True(router.ProcessEnter(Key.Enter, ModifierKeys.None, panel, isRepeat: true));
        Assert.False(router.ProcessEnter(Key.Enter, ModifierKeys.None, new TextBox { AcceptsReturn = true }, isRepeat: true));
        Assert.False(router.ProcessEnter(Key.Enter, ModifierKeys.None, new Button(), isRepeat: true));
        Assert.Equal(0, calls);
        owner.Close();
    });

    [Fact]
    public void EnterFallbackProtectsNativeInputControlsAndTheirChildren() => RunSta(() =>
    {
        using var context = new Context(); var owner = new Window();
        context.Document.SetToolMode(CadCanvasToolMode.Polyline);
        DependencyObject[] controls = [new TextBox(), new TextBox { IsReadOnly = true },
            new RichTextBox(), new PasswordBox(), new Button(), new CheckBox(), new RadioButton(),
            new ComboBox(), new ListBox(), new ListView(), new TreeView(), new DataGrid(),
            new Menu(), new MenuItem(), new Slider(), new System.Windows.Controls.Primitives.ScrollBar()];
        var history = context.Document.CadEditor.CreateDocumentHistorySnapshot();
        foreach (var control in controls)
        {
            Assert.False(CadEnterHandler.CanConfirmFrom(control));
            Assert.False(CadEnterHandler.Process(owner, context.Document, control));
        }
        var buttonLabel = new TextBlock { Text = "Confirm" };
        var button = new Button { Content = buttonLabel };
        Assert.False(CadEnterHandler.CanConfirmFrom(buttonLabel));
        var itemLabel = new TextBlock { Text = "Selected entity" };
        var list = new ListBox(); list.Items.Add(itemLabel);
        Assert.False(CadEnterHandler.CanConfirmFrom(itemLabel));
        Assert.False(CadEnterHandler.CanConfirmFrom(null));
        using var canvas = new CadCanvas { DocumentViewModel = context.Document };
        Assert.False(CadEnterHandler.CanConfirmFrom(canvas));
        Assert.Equal(CadCanvasToolMode.Polyline, context.Document.CadCanvasToolMode);
        Assert.Empty(context.Document.StepInputError);
        Assert.True(context.Document.CadEditor.DocumentHistoryEquals(history));
        owner.Close();
    });

    [Fact]
    public void DockTabContentAllowsPassivePanelsButStillProtectsEditorsAndTabHeaders() => RunSta(() =>
    {
        var label = new TextBlock { Text = "Properties" };
        var input = new TextBox { Text = "draft" };
        var panel = new StackPanel(); panel.Children.Add(label); panel.Children.Add(input);
        var tab = new TabItem { Content = panel };
        var tabs = new TabControl(); tabs.Items.Add(tab);
        Assert.True(CadEnterHandler.CanConfirmFrom(panel));
        Assert.True(CadEnterHandler.CanConfirmFrom(label));
        Assert.False(CadEnterHandler.CanConfirmFrom(input));
        Assert.False(CadEnterHandler.CanConfirmFrom(tab));
        Assert.False(CadEnterHandler.CanConfirmFrom(tabs));
    });

    [Fact]
    public void PassivePanelEnterCompletesPendingPolylineAndReturnsFocusToCanvas() => RunSta(() =>
    {
        using var context = new Context();
        using var canvas = new CadCanvas { DocumentViewModel = context.Document, Height = 200 };
        var panel = new Border { Focusable = true, Height = 30 };
        var content = new StackPanel(); content.Children.Add(panel); content.Children.Add(canvas);
        var owner = ShowEnterWindow(content);
        try
        {
            FocusEnterControl(owner, panel);
            Assert.False(CadEnterHandler.Process(owner, context.Document, panel));
            Assert.True(panel.IsKeyboardFocused);
            Assert.Empty(context.Document.StepInputError);
            StartEnterPolyline(context.Document);
            FocusEnterControl(owner, panel);
            Assert.True(CadEnterHandler.Process(owner, context.Document, panel));
            Assert.Single(context.Document.CadEditor.Document.Entities.Values, entity => !entity.IsErased);
            Assert.Empty(context.Document.StepInputError);
            Assert.True(canvas.IsKeyboardFocused);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public void EnterWithoutActiveDrawingOrVisibleCanvasDoesNotCreateAnErrorOrChangeSelection() => RunSta(() =>
    {
        using var context = new Context(); var panel = new Border(); var owner = new Window { Content = panel };
        var line = context.Document.CadEditor.AddLine(default, new(20, 0));
        context.Document.SelectEntities([line]);
        var history = context.Document.CadEditor.CreateDocumentHistorySnapshot();
        Assert.False(CadEnterHandler.Process(owner, null, panel));
        Assert.False(CadEnterHandler.Process(owner, context.Document, panel));
        context.Document.SetToolMode(CadCanvasToolMode.Polyline);
        Assert.False(CadEnterHandler.Process(owner, context.Document, panel));
        Assert.Empty(context.Document.StepInputError);
        Assert.True(context.Document.CadEditor.DocumentHistoryEquals(history));
        owner.Close();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PropertyEnterValidatesBeforeMovingFocusAndNeverCompletesPendingDrawing(bool invalid) => RunSta(() =>
    {
        using var context = new Context(); var model = new PropertyModel();
        var section = new EntitySettingsPropertySection { ViewModel = model };
        var text = Descendants(section).OfType<TextBox>().Single(control => !control.IsReadOnly);
        using var canvas = new CadCanvas { DocumentViewModel = context.Document, Height = 160 };
        var content = new StackPanel(); content.Children.Add(section); content.Children.Add(canvas);
        var owner = ShowEnterWindow(content);
        using var router = new CadWindowShortcutRouter(owner, _ => null, () => false, MainWindow.TryCommitFocusedPropertyEdit,
            confirm: focused => CadEnterHandler.Process(owner, context.Document, focused));
        try
        {
            StartEnterPolyline(context.Document);
            FocusEnterControl(owner, text);
            text.SetCurrentValue(TextBox.TextProperty, invalid ? "invalid angle" : "37");
            Assert.Equal(12, model.GeometryRotationDegrees);
            var enter = KeyEvent(Key.Enter); text.RaiseEvent(enter);
            Assert.True(enter.Handled);
            Assert.Equal(invalid ? 12 : 37, model.GeometryRotationDegrees);
            Assert.Equal(invalid, Validation.GetHasError(text));
            Assert.Equal(invalid, text.IsKeyboardFocused);
            Assert.Equal(!invalid, canvas.IsKeyboardFocused);
            Assert.Equal(invalid ? "invalid angle" : "37", text.Text);
            Assert.DoesNotContain(context.Document.CadEditor.Document.Entities.Values, entity => !entity.IsErased);
            Assert.True(context.Document.CanCompletePointSequence);
            Assert.Empty(context.Document.StepInputError);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public void MultilinePropertyEnterRemainsOwnedByTextEditor() => RunSta(() =>
    {
        using var context = new Context(); var model = new PropertyModel();
        var section = new EntityHeaderPropertySection { ViewModel = model };
        var text = Descendants(section).OfType<TextBox>().Single(box => !box.IsReadOnly);
        text.AcceptsReturn = true;
        Flush(); text.SetCurrentValue(TextBox.TextProperty, "first\nsecond");
        var owner = new Window { Content = section };
        context.Document.SetToolMode(CadCanvasToolMode.Polyline);
        Assert.False(CadEnterHandler.Process(owner, context.Document, text));
        Assert.Equal("Original", model.EntityName);
        Assert.Equal("first\nsecond", text.Text);
        Assert.Empty(context.Document.StepInputError);
        owner.Close();
    });

    [Fact]
    public void CanvasHeldEnterCannotCompletePendingPolylineButFreshEnterCan() => RunSta(() =>
    {
        using var context = new Context(); using var canvas = new CadCanvas { DocumentViewModel = context.Document };
        StartEnterPolyline(context.Document);
        var repeat = RepeatedEnter(); canvas.HandleCanvasKey(Key.Enter, ModifierKeys.None, repeat);
        Assert.True(repeat.Handled);
        Assert.DoesNotContain(context.Document.CadEditor.Document.Entities.Values, entity => !entity.IsErased);
        Assert.True(context.Document.CanCompletePointSequence);
        var first = KeyEvent(Key.Enter); canvas.HandleCanvasKey(Key.Enter, ModifierKeys.None, first);
        Assert.True(first.Handled);
        Assert.Single(context.Document.CadEditor.Document.Entities.Values, entity => !entity.IsErased);
    });

    [Fact]
    public void CanvasEnterWithTooFewPointsConsumesTheGestureAndPreservesPendingPoints() => RunSta(() =>
    {
        using var context = new Context(); using var canvas = new CadCanvas { DocumentViewModel = context.Document };
        context.Document.SetToolMode(CadCanvasToolMode.Polyline);
        var first = KeyEvent(Key.Enter); canvas.HandleCanvasKey(Key.Enter, ModifierKeys.None, first);
        Assert.True(first.Handled);
        Assert.NotEmpty(context.Document.StepInputError);
        Assert.Equal(CadCanvasToolMode.Polyline, context.Document.CadCanvasToolMode);
        Assert.DoesNotContain(context.Document.CadEditor.Document.Entities.Values, entity => !entity.IsErased);
    });

    [Theory]
    [InlineData(ModifierKeys.None)]
    [InlineData(ModifierKeys.Control)]
    public void AiDisabledEnterIsConsumedAndDoesNotSubmitTheDraft(ModifierKeys modifiers) => RunSta(() =>
    {
        var platform = new PlatformStub();
        // None of these service dependencies may be called for a disabled send gesture.
        using var model = new AiAssistantToolboxViewModel(platform, platform, null!, null!, null!, new EnterAiSettings(),
            null!, null!, platform, null!, null!) { UserInput = "unsent draft" };
        Assert.False(model.SendCommand.CanExecute(null));
        var e = KeyEvent(Key.Enter); AiAssistantToolboxView.HandlePromptKey(model, Key.Enter, modifiers, e);
        Assert.True(e.Handled); Assert.Equal("unsent draft", model.UserInput); Assert.False(model.IsBusy);
    });

    [Fact]
    public void AiShiftEnterImeAndHeldEnterPreserveTheDraft() => RunSta(() =>
    {
        var platform = new PlatformStub();
        using var model = new AiAssistantToolboxViewModel(platform, platform, null!, null!, null!, new EnterAiSettings(),
            null!, null!, platform, null!, null!) { UserInput = "unsent draft", SelectedModel = "test-model" };
        Assert.True(model.SendCommand.CanExecute(null));
        var newline = KeyEvent(Key.Enter); AiAssistantToolboxView.HandlePromptKey(model, Key.Enter, ModifierKeys.Shift, newline);
        var ime = KeyEvent(Key.ImeProcessed); AiAssistantToolboxView.HandlePromptKey(model, Key.ImeProcessed, ModifierKeys.None, ime);
        var repeat = RepeatedEnter(); AiAssistantToolboxView.HandlePromptKey(model, Key.Enter, ModifierKeys.None, repeat);
        Assert.False(newline.Handled); Assert.False(ime.Handled); Assert.True(repeat.Handled);
        Assert.Equal("unsent draft", model.UserInput); Assert.False(model.IsBusy);
    });

    [Fact]
    public void TerminalHeldEnterCannotAcceptCompletionOrSubmitDraft() => RunSta(() =>
    {
        using var context = new Context(); using var terminal = context.CreateTerminal();
        context.Document.SetToolMode(CadCanvasToolMode.Polyline);
        terminal.CommandText = "CIR"; terminal.SelectNextSuggestion();
        var selected = terminal.SelectedSuggestion; Assert.NotNull(selected);
        Assert.True(CommandLineToolboxView.HandleTerminalKey(terminal, Key.Enter, ModifierKeys.None,
            () => throw new Exception("Repeated Enter moved the caret."),
            _ => throw new Exception("Repeated Enter moved the suggestion."), isRepeat: true));
        Assert.Equal("CIR", terminal.CommandText);
        Assert.Equal(selected, terminal.SelectedSuggestion); Assert.False(terminal.IsCommandExecuting);
        Assert.Empty(context.Document.StepInputError);
    });

    [Fact]
    public void InvalidDynamicNumberCannotFallThroughToDocumentConfirmation() => RunSta(() =>
    {
        using var context = new Context(); var owner = new Window();
        context.Document.SetToolMode(CadCanvasToolMode.Polyline);
        var field = context.Document.DynamicInputFields.First();
        field.Text = "invalid coordinate";
        var input = new TextBox { DataContext = field, Text = field.Text };
        Assert.False(context.Document.SubmitDynamicInput());
        Assert.NotEmpty(context.Document.DynamicInputError);
        Assert.False(CadEnterHandler.Process(owner, context.Document, input));
        Assert.Equal("invalid coordinate", field.Text);
        Assert.Empty(context.Document.StepInputError);
        Assert.Null(context.Document.DrawingAnchor);
        owner.Close();
    });

    private static Window ShowEnterWindow(UIElement content)
    {
        var window = new Window { Content = content, Width = 540, Height = 440, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen };
        window.Show(); window.Activate(); Flush();
        return window;
    }

    private static void FocusEnterControl(Window window, FrameworkElement element)
    {
        var handle = new WindowInteropHelper(window).Handle;
        window.UpdateLayout();
        Assert.True(handle != IntPtr.Zero && window.IsVisible && EnterFocusNative.IsWindowVisible(handle),
            "The Enter test window must have a visible native HWND before exercising keyboard focus.");
        Assert.True(window.IsEnabled && EnterFocusNative.IsWindowEnabled(handle) &&
            element.IsLoaded && element.IsVisible && element.IsEnabled && element.Focusable &&
            element.ActualWidth > 0 && element.ActualHeight > 0,
            $"Enter focus target is not ready: loaded={element.IsLoaded}, visible={element.IsVisible}, " +
            $"enabled={element.IsEnabled}, focusable={element.Focusable}, size={element.ActualWidth}x{element.ActualHeight}.");

        // These control tests raise a WPF routed event; they do not inject a desktop key.
        // Require actual WPF keyboard focus and this STA thread's native input focus, not
        // global desktop foreground ownership (which belongs to the separate UIA tests).
        // Logical FocusManager state alone would not satisfy these checks.
        window.Activate();
        Flush();
        element.Focus();
        Flush();
        Assert.True(EnterFocusNative.GetAncestor(EnterFocusNative.GetFocus(), 2 /* GA_ROOT */) == handle &&
            element.IsKeyboardFocused && ReferenceEquals(Keyboard.FocusedElement, element),
            $"Enter test failed to acquire real focus: active={window.IsActive}, hwnd={handle}, " +
            $"foreground={EnterFocusNative.GetForegroundWindow()}, nativeFocus={EnterFocusNative.GetFocus()}, " +
            $"keyboardFocus={Keyboard.FocusedElement?.GetType().Name ?? "null"}, " +
            $"targetFocus={element.IsKeyboardFocused}, application={System.Windows.Application.Current?.Dispatcher.Thread.ManagedThreadId.ToString() ?? "none"}.");
    }

    private static class EnterFocusNative
    {
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetFocus();
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowEnabled(IntPtr window);
    }

    private static void StartEnterPolyline(CadDocumentViewModel document)
    {
        document.SetToolMode(CadCanvasToolMode.Polyline);
        foreach (var point in new[] { new CadPointD(0, 0), new CadPointD(20, 0) })
        {
            var screen = document.CadEditor.Viewport.WorldToScreen(point);
            document.PointerMove(screen);
            document.PointerDown(screen, CadCanvasPointerButton.Left, false);
            document.PointerUp(screen, CadCanvasPointerButton.Left);
        }
        Assert.True(document.CanCompletePointSequence);
    }

    private static KeyEventArgs RepeatedEnter()
    {
        var e = KeyEvent(Key.Enter);
        typeof(KeyEventArgs).GetMethod("SetRepeat", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(e, [true]);
        Assert.True(e.IsRepeat);
        return e;
    }

    private sealed class EnterAiSettings : IAiAssistantSettingsStore
    {
        public AiAssistantSettings Load() => new() { Provider = AiAssistantProvider.LmStudio, Model = string.Empty };
        public void Save(AiAssistantSettings settings) => throw new InvalidOperationException("Enter unexpectedly saved AI settings.");
    }
}
