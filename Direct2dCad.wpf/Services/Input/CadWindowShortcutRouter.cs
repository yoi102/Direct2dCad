using System.Windows;
using System.Windows.Input;
using AvalonDock.Controls;

namespace Direct2dCad.wpf.Services.Input;

/// <summary>Application gestures reach the main window and its AvalonDock floating windows.</summary>
internal sealed class CadWindowShortcutRouter : IDisposable
{
    private static readonly DependencyProperty RouterProperty = DependencyProperty.RegisterAttached(
        "Router", typeof(CadWindowShortcutRouter), typeof(CadWindowShortcutRouter));
    private readonly Window _owner;
    private readonly Func<CadShortcut, ICommand?> _resolveCommand;
    private readonly Func<bool> _isModal;
    private readonly Func<DependencyObject?, bool> _commitProperty;
    private readonly Func<DependencyObject?, bool>? _cancel;
    private readonly Func<DependencyObject?, bool>? _confirm;

    static CadWindowShortcutRouter()
    {
        EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(OnWindowPreviewKeyDown));
        // Bubble only: controls, popups and input editors get the first opportunity
        // to consume Escape/Enter. Never act on the document through a handled event.
        EventManager.RegisterClassHandler(typeof(Window), Keyboard.KeyDownEvent,
            new KeyEventHandler(OnWindowKeyDown));
        EventManager.RegisterClassHandler(typeof(Window), Keyboard.KeyUpEvent,
            new KeyEventHandler(OnWindowKeyUp), handledEventsToo: true);
    }

    internal CadWindowShortcutRouter(Window owner, Func<CadShortcut, ICommand?> resolveCommand,
        Func<bool> isModal, Func<DependencyObject?, bool> commitProperty,
        Func<DependencyObject?, bool>? cancel = null, Func<DependencyObject?, bool>? confirm = null)
    {
        _owner = owner; _resolveCommand = resolveCommand; _isModal = isModal; _commitProperty = commitProperty;
        _cancel = cancel;
        _confirm = confirm;
        owner.SetValue(RouterProperty, this);
    }

    private static void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || sender is not Window window || e.Key is not (Key.Escape or Key.Enter)) return;
        var router = Resolve(window);
        if (router is null) return;
        var focused = Keyboard.FocusedElement as DependencyObject ?? e.OriginalSource as DependencyObject;
        if (CadEnterKeyGuard.DeferHostedEnter(e, Keyboard.Modifiers, focused)) return;
        if (e.Key == Key.Escape
            ? router.ProcessEscape(e.Key, Keyboard.Modifiers, focused, e.IsRepeat)
            : router.ProcessEnter(e.Key, Keyboard.Modifiers, focused,
                CadEnterKeyGuard.ShouldIgnoreRepeat(e, Keyboard.Modifiers)))
            e.Handled = true;
    }

    private static void OnWindowKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CadEnterKeyGuard.Release(e.KeyboardDevice);
    }

    internal bool ProcessEscape(Key key, ModifierKeys modifiers, DependencyObject? focusedElement, bool isRepeat = false)
    {
        if (key != Key.Escape || modifiers != ModifierKeys.None || _isModal() || _cancel is null) return false;
        return isRepeat || _cancel(focusedElement);
    }

    internal bool ProcessEnter(Key key, ModifierKeys modifiers, DependencyObject? focusedElement, bool isRepeat = false)
    {
        if (key != Key.Enter || modifiers != ModifierKeys.None || _isModal() || _confirm is null) return false;
        if (!CadEnterHandler.CanConfirmFrom(focusedElement) &&
            !CadEnterHandler.IsSingleLinePropertyEditor(focusedElement)) return false;
        return isRepeat || _confirm(focusedElement);
    }

    private static void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || sender is not Window window || e.Key == Key.ImeProcessed) return;
        var router = Resolve(window);
        if (router is not null && router.Process(e.Key == Key.System ? e.SystemKey : e.Key,
            Keyboard.Modifiers, Keyboard.FocusedElement as DependencyObject ?? e.OriginalSource as DependencyObject))
            e.Handled = true;
    }

    internal static CadWindowShortcutRouter? Resolve(Window window)
    {
        if (window.GetValue(RouterProperty) is CadWindowShortcutRouter direct) return direct;
        // Owned dialogs are intentionally excluded; only a docked document/toolbox
        // shares the application's file and toolbox command context.
        return window is LayoutFloatingWindowControl floating && floating.Model.Root?.Manager is { } manager
            ? Window.GetWindow(manager)?.GetValue(RouterProperty) as CadWindowShortcutRouter : null;
    }

    internal bool Process(Key key, ModifierKeys modifiers, DependencyObject? focusedElement)
    {
        var shortcut = CadShortcutCatalog.Find(CadShortcutScope.Application, key, modifiers);
        if (shortcut is null || shortcut.Action is CadShortcutAction.Cancel or CadShortcutAction.Confirm) return false;
        if (_isModal()) return true;
        var command = _resolveCommand(shortcut);
        if (command is null || !command.CanExecute(null)) return true;
        if (shortcut.Action is CadShortcutAction.Save or CadShortcutAction.SaveAs or CadShortcutAction.Print &&
            !_commitProperty(focusedElement)) return true;
        command.Execute(null);
        return true;
    }

    public void Dispose()
    {
        CadEnterKeyGuard.Release(Keyboard.PrimaryDevice);
        if (ReferenceEquals(_owner.GetValue(RouterProperty), this)) _owner.ClearValue(RouterProperty);
    }
}
