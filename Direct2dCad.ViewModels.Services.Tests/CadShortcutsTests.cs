using Direct2dCad.ViewModels.Services.Input;

namespace Direct2dCad.ViewModels.Services.Tests;

public sealed class CadShortcutsTests
{
    [Fact]
    public void EachGestureMapsToOneActionPerScope()
    {
        Assert.All(CadShortcuts.All.GroupBy(s => (s.Scope, s.Key, s.Modifiers)), group => Assert.Single(group));
        Assert.DoesNotContain(CadShortcuts.All, s => s.Key == CadKey.None);
    }

    [Fact]
    public void FindIsExactAboutModifiers()
    {
        Assert.Equal(CadShortcutAction.Undo, CadShortcuts.Find(CadShortcutScope.Canvas, CadKey.Z, CadKeyModifiers.Control)?.Action);
        Assert.Equal(CadShortcutAction.Redo, CadShortcuts.Find(CadShortcutScope.Canvas, CadKey.Z, CadKeyModifiers.Control | CadKeyModifiers.Shift)?.Action);
        Assert.Null(CadShortcuts.Find(CadShortcutScope.Canvas, CadKey.Z, CadKeyModifiers.Control | CadKeyModifiers.Alt));
        Assert.Null(CadShortcuts.Find(CadShortcutScope.Canvas, CadKey.None, CadKeyModifiers.None));
        Assert.Null(CadShortcuts.Find(CadShortcutScope.Terminal, CadKey.Enter, (CadKeyModifiers)(-1)));
    }

    [Fact]
    public void GestureTextUsesTheInvariantKeyGestureFormat()
    {
        Assert.Equal("Ctrl+N", CadShortcuts.NewGesture);
        Assert.Equal("Ctrl+Shift+S", CadShortcuts.SaveAsGesture);
        Assert.Equal("Ctrl+J", CadShortcuts.ForToolbox("toolbox.command-line")?.DisplayGesture);
        Assert.Equal("Ctrl+Shift+L", CadShortcuts.ForToolbox("toolbox.layers")?.DisplayGesture);
    }

    [Fact]
    public void EveryCatalogToolboxHasAToggleGesture()
    {
        var ids = CadShortcuts.All.Where(s => s.Action == CadShortcutAction.ToggleToolbox).Select(s => s.ToolboxId).ToHashSet();
        Assert.Equal(10, ids.Count);
        Assert.DoesNotContain(null, ids);
    }
}
