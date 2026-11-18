namespace Direct2dCad.ViewModels.Services.Input;

public enum CadShortcutScope { Application, Canvas, Terminal, DynamicInput, AiPrompt }

public enum CadShortcutAction
{
    New, Open, Save, SaveAs, Print, ToggleToolbox,
    Cancel, Confirm, PreviousPoint, Reselect, Delete, NextSelection, PreviousSelection,
    SelectAll, Undo, Redo, Copy, Cut, Paste, SuggestionUp, SuggestionDown, Complete,
    NextField, PreviousField, NextSnapCandidate, SendPrompt, PasteAttachment
}

/// <summary>The keys the shortcut catalog uses; each host maps its own key type onto these.</summary>
public enum CadKey
{
    None,
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    Escape, Enter, Back, Delete, Tab, Up, Down, F4,
    /// <summary>The ` / ~ key (WPF and Avalonia <c>Key.Oem3</c>).</summary>
    Backtick,
}

[Flags]
public enum CadKeyModifiers { None = 0, Control = 1, Shift = 2, Alt = 4 }

public sealed record CadShortcut(CadShortcutScope Scope, CadShortcutAction Action,
    CadKey Key, CadKeyModifiers Modifiers = CadKeyModifiers.None, string? ToolboxId = null)
{
    public bool Matches(CadKey key, CadKeyModifiers modifiers) => Key == key && Modifiers == modifiers;

    /// <summary>Invariant gesture text, e.g. "Ctrl+Shift+S" (the format WPF's KeyGestureConverter reads).</summary>
    public string DisplayGesture
    {
        get
        {
            var parts = new List<string>(4);
            if (Modifiers.HasFlag(CadKeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(CadKeyModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(CadKeyModifiers.Shift)) parts.Add("Shift");
            parts.Add(Key switch
            {
                CadKey.Back => "Backspace",
                CadKey.Escape => "Esc",
                CadKey.Delete => "Del",
                CadKey.Backtick => "`",
                _ => Key.ToString(),
            });
            return string.Join("+", parts);
        }
    }
}

/// <summary>
/// The exact gestures both desktop hosts route. An additional modifier never falls back to another action.
/// </summary>
public static class CadShortcuts
{
    public static IReadOnlyList<CadShortcut> All { get; } = Array.AsReadOnly<CadShortcut>([
        new(CadShortcutScope.Application, CadShortcutAction.Cancel, CadKey.Escape),
        new(CadShortcutScope.Application, CadShortcutAction.Confirm, CadKey.Enter),
        new(CadShortcutScope.Application, CadShortcutAction.New, CadKey.N, CadKeyModifiers.Control),
        new(CadShortcutScope.Application, CadShortcutAction.Open, CadKey.O, CadKeyModifiers.Control),
        new(CadShortcutScope.Application, CadShortcutAction.Save, CadKey.S, CadKeyModifiers.Control),
        new(CadShortcutScope.Application, CadShortcutAction.SaveAs, CadKey.S, CadKeyModifiers.Control | CadKeyModifiers.Shift),
        new(CadShortcutScope.Application, CadShortcutAction.Print, CadKey.P, CadKeyModifiers.Control),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, CadKey.J, CadKeyModifiers.Control, "toolbox.command-line"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, CadKey.Backtick, CadKeyModifiers.Control, "toolbox.command-line"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, CadKey.E, CadKeyModifiers.Control | CadKeyModifiers.Shift, "toolbox.documents"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, CadKey.L, CadKeyModifiers.Control | CadKeyModifiers.Shift, "toolbox.layers"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, CadKey.B, CadKeyModifiers.Control | CadKeyModifiers.Shift, "toolbox.blocks"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, CadKey.G, CadKeyModifiers.Control | CadKeyModifiers.Shift, "toolbox.entity-properties"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, CadKey.D, CadKeyModifiers.Control | CadKeyModifiers.Shift, "toolbox.drawing-assistant"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, CadKey.T, CadKeyModifiers.Control | CadKeyModifiers.Shift, "toolbox.entity-search"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, CadKey.F, CadKeyModifiers.Control | CadKeyModifiers.Shift, "toolbox.selection-filter"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, CadKey.M, CadKeyModifiers.Control | CadKeyModifiers.Shift, "toolbox.messages"),
        new(CadShortcutScope.Application, CadShortcutAction.ToggleToolbox, CadKey.A, CadKeyModifiers.Control | CadKeyModifiers.Shift, "toolbox.ai-assistant"),
        new(CadShortcutScope.Canvas, CadShortcutAction.Cancel, CadKey.Escape),
        new(CadShortcutScope.Canvas, CadShortcutAction.Confirm, CadKey.Enter),
        new(CadShortcutScope.Canvas, CadShortcutAction.PreviousPoint, CadKey.Back),
        new(CadShortcutScope.Canvas, CadShortcutAction.Reselect, CadKey.R),
        new(CadShortcutScope.Canvas, CadShortcutAction.Delete, CadKey.Delete),
        new(CadShortcutScope.Canvas, CadShortcutAction.NextSelection, CadKey.Tab),
        new(CadShortcutScope.Canvas, CadShortcutAction.PreviousSelection, CadKey.Tab, CadKeyModifiers.Shift),
        new(CadShortcutScope.Canvas, CadShortcutAction.SelectAll, CadKey.A, CadKeyModifiers.Control),
        new(CadShortcutScope.Canvas, CadShortcutAction.Undo, CadKey.Z, CadKeyModifiers.Control),
        new(CadShortcutScope.Canvas, CadShortcutAction.Redo, CadKey.Y, CadKeyModifiers.Control),
        new(CadShortcutScope.Canvas, CadShortcutAction.Redo, CadKey.Z, CadKeyModifiers.Control | CadKeyModifiers.Shift),
        new(CadShortcutScope.Canvas, CadShortcutAction.Copy, CadKey.C, CadKeyModifiers.Control),
        new(CadShortcutScope.Canvas, CadShortcutAction.Cut, CadKey.X, CadKeyModifiers.Control),
        new(CadShortcutScope.Canvas, CadShortcutAction.Paste, CadKey.V, CadKeyModifiers.Control),
        new(CadShortcutScope.Terminal, CadShortcutAction.Confirm, CadKey.Enter),
        new(CadShortcutScope.Terminal, CadShortcutAction.SuggestionUp, CadKey.Up),
        new(CadShortcutScope.Terminal, CadShortcutAction.SuggestionDown, CadKey.Down),
        new(CadShortcutScope.Terminal, CadShortcutAction.Complete, CadKey.Tab),
        new(CadShortcutScope.Terminal, CadShortcutAction.Cancel, CadKey.Escape),
        new(CadShortcutScope.DynamicInput, CadShortcutAction.NextField, CadKey.Tab),
        new(CadShortcutScope.DynamicInput, CadShortcutAction.PreviousField, CadKey.Tab, CadKeyModifiers.Shift),
        new(CadShortcutScope.DynamicInput, CadShortcutAction.Confirm, CadKey.Enter),
        new(CadShortcutScope.DynamicInput, CadShortcutAction.Cancel, CadKey.Escape),
        new(CadShortcutScope.DynamicInput, CadShortcutAction.NextSnapCandidate, CadKey.F4),
        new(CadShortcutScope.AiPrompt, CadShortcutAction.SendPrompt, CadKey.Enter),
        new(CadShortcutScope.AiPrompt, CadShortcutAction.SendPrompt, CadKey.Enter, CadKeyModifiers.Control),
        new(CadShortcutScope.AiPrompt, CadShortcutAction.PasteAttachment, CadKey.V, CadKeyModifiers.Control)
    ]);

    public static CadShortcut? Find(CadShortcutScope scope, CadKey key, CadKeyModifiers modifiers) =>
        key == CadKey.None ? null : All.FirstOrDefault(shortcut => shortcut.Scope == scope && shortcut.Matches(key, modifiers));

    /// <summary>The first gesture that toggles <paramref name="toolboxId"/> (shown on its tab), if any.</summary>
    public static CadShortcut? ForToolbox(string toolboxId) => All.FirstOrDefault(s => s.ToolboxId == toolboxId);

    public static string NewGesture => ApplicationGesture(CadShortcutAction.New);
    public static string OpenGesture => ApplicationGesture(CadShortcutAction.Open);
    public static string SaveGesture => ApplicationGesture(CadShortcutAction.Save);
    public static string SaveAsGesture => ApplicationGesture(CadShortcutAction.SaveAs);
    public static string PrintGesture => ApplicationGesture(CadShortcutAction.Print);

    private static string ApplicationGesture(CadShortcutAction action) =>
        All.First(s => s.Scope == CadShortcutScope.Application && s.Action == action).DisplayGesture;
}
