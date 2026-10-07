using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.wpf;
using Direct2dCad.wpf.Controls;
using Direct2dCad.wpf.Services.Input;
using Direct2dCad.wpf.Views.Toolboxes.EntityProperty;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed partial class ShortcutRoutingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscapeBubblesFromPanelsAndPreservesDrafts(bool textInput) => RunSta(() =>
    {
        using var context = new Context();
        using var canvas = new CadCanvas { DocumentViewModel = context.Document };
        var draft = new TextBox { Text = "unsent draft" };
        var button = new Button();
        var panel = new StackPanel(); panel.Children.Add(draft); panel.Children.Add(button); panel.Children.Add(canvas);
        var window = new Window { Content = panel };
        using var router = new CadWindowShortcutRouter(window, _ => null, () => false, _ => true,
            focused => CadEscapeHandler.Process(window, context.Document, focused));
        try
        {
            context.Document.SetToolMode(CadCanvasToolMode.Line);
            var e = KeyEvent(Key.Escape);
            (textInput ? (UIElement)draft : button).RaiseEvent(e);
            Assert.True(e.Handled);
            Assert.Equal(CadCanvasToolMode.Select, context.Document.CadCanvasToolMode);
            Assert.Equal("unsent draft", draft.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void EscapeConsumedByChildNeverCancelsTheDrawing() => RunSta(() =>
    {
        using var context = new Context();
        var child = new Button(); var window = new Window { Content = child };
        using var router = new CadWindowShortcutRouter(window, _ => null, () => false, _ => true,
            focused => CadEscapeHandler.Process(window, context.Document, focused));
        child.KeyDown += (_, e) => e.Handled = true;
        context.Document.SetToolMode(CadCanvasToolMode.Line);
        child.RaiseEvent(KeyEvent(Key.Escape));
        Assert.Equal(CadCanvasToolMode.Line, context.Document.CadCanvasToolMode);
        window.Close();
    });

    [Theory]
    [InlineData(Key.Escape, ModifierKeys.Control, false)]
    [InlineData(Key.Escape, ModifierKeys.Shift, false)]
    [InlineData(Key.Escape, ModifierKeys.Alt, false)]
    [InlineData(Key.ImeProcessed, ModifierKeys.None, false)]
    [InlineData(Key.Escape, ModifierKeys.None, true)]
    public void EscapeFallbackRespectsModifiersImeAndModalState(Key key, ModifierKeys modifiers, bool modal) => RunSta(() =>
    {
        using var context = new Context(); var window = new Window();
        using var router = new CadWindowShortcutRouter(window, _ => null, () => modal, _ => true,
            focused => CadEscapeHandler.Process(window, context.Document, focused));
        context.Document.SetToolMode(CadCanvasToolMode.Line);
        Assert.False(router.ProcessEscape(key, modifiers, null));
        Assert.False(router.Process(Key.Escape, ModifierKeys.None, null));
        Assert.Equal(CadCanvasToolMode.Line, context.Document.CadCanvasToolMode);
        window.Close();
    });

    [Fact]
    public void RepeatedEscapeDoesNotCrossAnotherCancellationLayer() => RunSta(() =>
    {
        using var context = new Context(); var window = new Window();
        using var router = new CadWindowShortcutRouter(window, _ => null, () => false, _ => true,
            focused => CadEscapeHandler.Process(window, context.Document, focused));
        context.Document.SetToolMode(CadCanvasToolMode.Line);
        Assert.True(router.ProcessEscape(Key.Escape, ModifierKeys.None, null, isRepeat: true));
        Assert.Equal(CadCanvasToolMode.Line, context.Document.CadCanvasToolMode);
        window.Close();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PropertyEscapeRestoresSourceBeforeFocusLossWithoutCancellingSelection(bool invalid) => RunSta(() =>
    {
        using var context = new Context(); var window = new Window();
        var model = new PropertyModel();
        UserControl section = invalid ? new EntitySettingsPropertySection { ViewModel = model }
            : new EntityHeaderPropertySection { ViewModel = model };
        var text = Descendants(section).OfType<TextBox>().Single(b => !b.IsReadOnly);
        Flush(); text.SetCurrentValue(TextBox.TextProperty, invalid ? "bad angle" : "discard this name");
        if (invalid) Assert.False(MainWindow.TryCommitFocusedPropertyEdit(text));
        var line = context.Document.CadEditor.AddLine(default, new(20, 0));
        context.Document.SelectEntities([line]);
        var history = context.Document.CadEditor.CreateDocumentHistorySnapshot();
        Assert.True(CadEscapeHandler.Process(window, context.Document, text));
        Assert.Equal(invalid ? "12" : "Original", text.Text);
        Assert.False(Validation.GetHasError(text));
        Assert.True(MainWindow.TryCommitFocusedPropertyEdit(text)); // Same source update as LostFocus.
        Assert.Equal("Original", model.EntityName); Assert.Equal(12, model.GeometryRotationDegrees);
        Assert.Single(context.Document.CadEditor.Selection.EntityIds);
        Assert.True(context.Document.CadEditor.DocumentHistoryEquals(history));
        Assert.True(CadEscapeHandler.Process(window, context.Document, null));
        Assert.Empty(context.Document.CadEditor.Selection.EntityIds);
        Assert.False(context.Document.CadEditor.Document.GetEntity(line).IsErased);
        window.Close();
    });

    [Fact]
    public void EscapeWithoutAnActiveDocumentPreservesInput() => RunSta(() =>
    {
        var window = new Window(); var draft = new TextBox { Text = "draft" };
        Assert.False(CadEscapeHandler.Process(window, null, draft));
        Assert.Equal("draft", draft.Text);
        window.Close();
    });
}
