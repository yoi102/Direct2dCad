using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Direct2dCad.wpf.Services.Input;

/// <summary>Distinguishes a held Enter from AvalonDock forwarding one press into a child HWND.</summary>
internal static class CadEnterKeyGuard
{
    private sealed record Forward(WeakReference<DependencyObject> Focus, WeakReference<PresentationSource> Source,
        ModifierKeys Modifiers);
    private static readonly ConditionalWeakTable<KeyboardDevice, Forward> Pending = new();

    internal static bool DeferHostedEnter(KeyEventArgs e, ModifierKeys modifiers, DependencyObject? focusedElement)
    {
        if (e.Key != Key.Enter || e.OriginalSource is not HwndHost host || focusedElement is not Visual focused ||
            PresentationSource.FromVisual(focused) is not { } source ||
            ReferenceEquals(source, PresentationSource.FromVisual(host))) return false;

        // The proxy is never a confirmation target, including its repeated messages.
        // The child's first KeyDown may already have IsRepeat=true in WPF's keyboard state.
        if (!e.IsRepeat) RememberForward(e.KeyboardDevice, focusedElement, source, modifiers);
        return true;
    }

    internal static void RememberForward(KeyboardDevice keyboard, DependencyObject focus,
        PresentationSource source, ModifierKeys modifiers)
    {
        Pending.Remove(keyboard);
        Pending.Add(keyboard, new(new(focus), new(source), modifiers));
    }

    internal static bool ShouldIgnoreRepeat(KeyEventArgs e, ModifierKeys modifiers)
    {
        var forwarded = Pending.TryGetValue(e.KeyboardDevice, out var pending);
        Pending.Remove(e.KeyboardDevice);
        if (!e.IsRepeat) return false;
        return !(forwarded && e.Key == Key.Enter && pending!.Modifiers == modifiers &&
            pending.Focus.TryGetTarget(out var focus) && ReferenceEquals(focus, Keyboard.FocusedElement) &&
            pending.Source.TryGetTarget(out var source) && focus is Visual focused &&
            ReferenceEquals(source, PresentationSource.FromVisual(focused)) && e.OriginalSource is Visual origin &&
            ReferenceEquals(source, PresentationSource.FromVisual(origin)));
    }

    internal static void Release(KeyboardDevice keyboard) => Pending.Remove(keyboard);
}
