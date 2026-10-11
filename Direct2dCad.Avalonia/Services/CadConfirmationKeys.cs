using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Direct2dCad.Avalonia.Services;

/// <summary>Track confirm/cancel keys at the window, across focus changes and handled releases.</summary>
internal sealed class CadConfirmationKeys
{
    private static readonly ConditionalWeakTable<Window, CadConfirmationKeys> Guards = new();
    private static readonly HashSet<Key> Held = [];
    private static readonly ConditionalWeakTable<KeyEventArgs, object> Observed = new();

    static CadConfirmationKeys()
    {
        // Avalonia 12 routes through logical TopLevelHost/Popup elements too,
        // outside the Window's route. Observe the whole input route so closing
        // a menu cannot lose its first Escape before repeats reach the canvas.
        InputElement.KeyDownEvent.AddClassHandler<InputElement>((source, e) => OnKeyDown(source, e), RoutingStrategies.Tunnel | RoutingStrategies.Direct | RoutingStrategies.Bubble, true);
        InputElement.KeyUpEvent.AddClassHandler<InputElement>((_, e) => { if (e.Key is Key.Enter or Key.Escape) Held.Remove(e.Key); }, RoutingStrategies.Tunnel | RoutingStrategies.Direct | RoutingStrategies.Bubble, true);
    }

    internal static void Attach(Window window) => Guards.GetValue(window, static owner => new(owner));

    private CadConfirmationKeys(Window window)
    {
        window.Deactivated += (_, _) => Held.Clear();
    }

    private static void OnKeyDown(InputElement source, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        if (Observed.TryGetValue(e, out _)) return;
        Observed.Add(e, new object());
        var repeated = !Held.Add(e.Key);
        CanvasInputTrace.Write($"confirmation key={e.Key} repeated={repeated} route={e.Route} observer={source.GetType().Name} source={e.Source?.GetType().Name} handled={e.Handled}");
        // Multiline editors keep ordinary newline repeats; their own send gesture
        // (for example the AI prompt) still decides which Enter variants to consume.
        if (repeated && e.KeyModifiers == KeyModifiers.None &&
            (e.Key == Key.Escape || e.Source is not TextBox { AcceptsReturn: true }))
            e.Handled = true;
    }

}
