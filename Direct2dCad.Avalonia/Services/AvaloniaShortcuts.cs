using global::Avalonia.Input;
using Direct2dCad.ViewModels.Services.Input;

namespace Direct2dCad.Avalonia.Services;

/// <summary>Avalonia view of the shared <see cref="CadShortcuts"/> catalog (the WPF host has the same adapter).</summary>
internal static class AvaloniaShortcuts
{
    public static CadShortcut? Find(CadShortcutScope scope, Key key, KeyModifiers modifiers) =>
        CadShortcuts.Find(scope, ToCadKey(key), ToCadModifiers(modifiers));

    public static CadKey ToCadKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => CadKey.A + (key - Key.A),
        Key.Escape => CadKey.Escape,
        Key.Enter => CadKey.Enter,
        Key.Back => CadKey.Back,
        Key.Delete => CadKey.Delete,
        Key.Tab => CadKey.Tab,
        Key.Up => CadKey.Up,
        Key.Down => CadKey.Down,
        Key.F4 => CadKey.F4,
        Key.Oem3 => CadKey.Backtick,
        _ => CadKey.None,
    };

    public static CadKeyModifiers ToCadModifiers(KeyModifiers modifiers)
    {
        // Meta (Windows-key) combinations are never catalog gestures; keep them from matching a plain gesture.
        if (modifiers.HasFlag(KeyModifiers.Meta)) return (CadKeyModifiers)(-1);
        return (modifiers.HasFlag(KeyModifiers.Control) ? CadKeyModifiers.Control : 0) |
               (modifiers.HasFlag(KeyModifiers.Shift) ? CadKeyModifiers.Shift : 0) |
               (modifiers.HasFlag(KeyModifiers.Alt) ? CadKeyModifiers.Alt : 0);
    }
}
