using System.Windows.Input;
using Direct2dCad.ViewModels.Services.Input;

namespace Direct2dCad.wpf.Services.Input;

/// <summary>
/// WPF view of the shared <see cref="CadShortcuts"/> catalog: maps WPF keys onto <see cref="CadKey"/>. The gesture
/// strings stay here so XAML can keep binding them with <c>x:Static keys:CadShortcutCatalog.*Gesture</c>.
/// </summary>
public static class CadShortcutCatalog
{
    public static IReadOnlyList<CadShortcut> All => CadShortcuts.All;

    public static CadShortcut? Find(CadShortcutScope scope, Key key, ModifierKeys modifiers) =>
        CadShortcuts.Find(scope, ToCadKey(key), ToCadModifiers(modifiers));

    public static string NewGesture => CadShortcuts.NewGesture;
    public static string OpenGesture => CadShortcuts.OpenGesture;
    public static string SaveGesture => CadShortcuts.SaveGesture;
    public static string SaveAsGesture => CadShortcuts.SaveAsGesture;
    public static string PrintGesture => CadShortcuts.PrintGesture;

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

    public static CadKeyModifiers ToCadModifiers(ModifierKeys modifiers)
    {
        // Windows-key combinations are never catalog gestures; keep them from matching a plain gesture.
        if (modifiers.HasFlag(ModifierKeys.Windows)) return (CadKeyModifiers)(-1);
        return (modifiers.HasFlag(ModifierKeys.Control) ? CadKeyModifiers.Control : 0) |
               (modifiers.HasFlag(ModifierKeys.Shift) ? CadKeyModifiers.Shift : 0) |
               (modifiers.HasFlag(ModifierKeys.Alt) ? CadKeyModifiers.Alt : 0);
    }
}
