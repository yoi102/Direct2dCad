using System.Windows.Input;

namespace Direct2dCad.wpf.Services.Input;

public enum CadShortcutScope { Application, Canvas, Terminal, DynamicInput, AiPrompt }
public enum CadShortcutAction
{
    New, Open, Save, SaveAs, Print, ToggleToolbox,
    Cancel, Confirm, PreviousPoint, Reselect, Delete, NextSelection, PreviousSelection,
    SelectAll, Undo, Redo, Copy, Cut, Paste, SuggestionUp, SuggestionDown, Complete,
    NextField, PreviousField, ClearFixedValues, NextSnapCandidate, SendPrompt, PasteAttachment
}

public sealed record CadShortcut(CadShortcutScope Scope, CadShortcutAction Action,
    Key Key, ModifierKeys Modifiers = ModifierKeys.None, string? ToolboxId = null)
{
    public bool Matches(Key key, ModifierKeys modifiers) => Key == key && Modifiers == modifiers;
}

/// <summary>Exact gestures. An additional modifier never falls back to another action.</summary>
public static class CadShortcutCatalog
{
    public static IReadOnlyList<CadShortcut> All { get; } = Array.AsReadOnly<CadShortcut>([
        new(CadShortcutScope.Application, CadShortcutAction.Cancel, Key.Escape),
        new(CadShortcutScope.Application, CadShortcutAction.Confirm, Key.Enter),
        new(CadShortcutScope.Application, CadShortcutAction.New, Key.N, ModifierKeys.Control),
        new(CadShortcutScope.Application, CadShortcutAction.Open, Key.O, ModifierKeys.Control),
        new(CadShortcutScope.Application, CadShortcutAction.Save, Key.S, ModifierKeys.Control),
        new(CadShortcutScope.Application, CadShortcutAction.SaveAs, Key.S, ModifierKeys.Control | ModifierKeys.Shift),
        new(CadShortcutScope.Application, CadShortcutAction.Print, Key.P, ModifierKeys.Control),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, Key.J, ModifierKeys.Control, "toolbox.command-line"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, Key.Oem3, ModifierKeys.Control, "toolbox.command-line"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, Key.E, ModifierKeys.Control | ModifierKeys.Shift, "toolbox.documents"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, Key.L, ModifierKeys.Control | ModifierKeys.Shift, "toolbox.layers"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, Key.B, ModifierKeys.Control | ModifierKeys.Shift, "toolbox.blocks"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, Key.G, ModifierKeys.Control | ModifierKeys.Shift, "toolbox.entity-properties"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, Key.D, ModifierKeys.Control | ModifierKeys.Shift, "toolbox.drawing-assistant"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, Key.T, ModifierKeys.Control | ModifierKeys.Shift, "toolbox.entity-search"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, Key.F, ModifierKeys.Control | ModifierKeys.Shift, "toolbox.selection-filter"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, Key.M, ModifierKeys.Control | ModifierKeys.Shift, "toolbox.messages"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, Key.A, ModifierKeys.Control | ModifierKeys.Shift, "toolbox.ai-assistant"),
        new(CadShortcutScope.Canvas, CadShortcutAction.Cancel, Key.Escape),
        new(CadShortcutScope.Canvas, CadShortcutAction.Confirm, Key.Enter),
        new(CadShortcutScope.Canvas, CadShortcutAction.PreviousPoint, Key.Back),
        new(CadShortcutScope.Canvas, CadShortcutAction.Reselect, Key.R),
        new(CadShortcutScope.Canvas, CadShortcutAction.Delete, Key.Delete),
        new(CadShortcutScope.Canvas, CadShortcutAction.NextSelection, Key.Tab),
        new(CadShortcutScope.Canvas, CadShortcutAction.PreviousSelection, Key.Tab, ModifierKeys.Shift),
        new(CadShortcutScope.Canvas, CadShortcutAction.SelectAll, Key.A, ModifierKeys.Control),
        new(CadShortcutScope.Canvas, CadShortcutAction.Undo, Key.Z, ModifierKeys.Control),
        new(CadShortcutScope.Canvas, CadShortcutAction.Redo, Key.Y, ModifierKeys.Control),
        new(CadShortcutScope.Canvas, CadShortcutAction.Redo, Key.Z, ModifierKeys.Control | ModifierKeys.Shift),
        new(CadShortcutScope.Canvas, CadShortcutAction.Copy, Key.C, ModifierKeys.Control),
        new(CadShortcutScope.Canvas, CadShortcutAction.Cut, Key.X, ModifierKeys.Control),
        new(CadShortcutScope.Canvas, CadShortcutAction.Paste, Key.V, ModifierKeys.Control),
        new(CadShortcutScope.Terminal, CadShortcutAction.Confirm, Key.Enter),
        new(CadShortcutScope.Terminal, CadShortcutAction.SuggestionUp, Key.Up),
        new(CadShortcutScope.Terminal, CadShortcutAction.SuggestionDown, Key.Down),
        new(CadShortcutScope.Terminal, CadShortcutAction.Complete, Key.Tab),
        new(CadShortcutScope.Terminal, CadShortcutAction.Cancel, Key.Escape),
        new(CadShortcutScope.DynamicInput, CadShortcutAction.NextField, Key.Tab),
        new(CadShortcutScope.DynamicInput, CadShortcutAction.PreviousField, Key.Tab, ModifierKeys.Shift),
        new(CadShortcutScope.DynamicInput, CadShortcutAction.Confirm, Key.Enter),
        new(CadShortcutScope.DynamicInput, CadShortcutAction.Cancel, Key.Escape),
        new(CadShortcutScope.DynamicInput, CadShortcutAction.ClearFixedValues, Key.Delete, ModifierKeys.Control),
        new(CadShortcutScope.DynamicInput, CadShortcutAction.NextSnapCandidate, Key.F4),
        new(CadShortcutScope.AiPrompt, CadShortcutAction.SendPrompt, Key.Enter),
        new(CadShortcutScope.AiPrompt, CadShortcutAction.SendPrompt, Key.Enter, ModifierKeys.Control),
        new(CadShortcutScope.AiPrompt, CadShortcutAction.PasteAttachment, Key.V, ModifierKeys.Control)
    ]);

    public static CadShortcut? Find(CadShortcutScope scope, Key key, ModifierKeys modifiers) =>
        All.FirstOrDefault(shortcut => shortcut.Scope == scope && shortcut.Matches(key, modifiers));

    public static string NewGesture => ApplicationGesture(CadShortcutAction.New);
    public static string OpenGesture => ApplicationGesture(CadShortcutAction.Open);
    public static string SaveGesture => ApplicationGesture(CadShortcutAction.Save);
    public static string SaveAsGesture => ApplicationGesture(CadShortcutAction.SaveAs);
    public static string PrintGesture => ApplicationGesture(CadShortcutAction.Print);

    private static string ApplicationGesture(CadShortcutAction action)
    {
        var shortcut = All.First(s => s.Scope == CadShortcutScope.Application && s.Action == action);
        return new KeyGesture(shortcut.Key, shortcut.Modifiers)
            .GetDisplayStringForCulture(System.Globalization.CultureInfo.InvariantCulture);
    }
}
