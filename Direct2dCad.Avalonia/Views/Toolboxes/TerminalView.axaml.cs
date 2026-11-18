using Direct2dCad.ViewModels.Services.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Direct2dCad.ViewModels.Toolboxes;
namespace Direct2dCad.Avalonia.Views.Toolboxes;
public partial class TerminalView : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private ScrollViewer? _outputScroll;
    private bool _following = true, _programmaticScroll;
    public TerminalView()
    {
        InitializeComponent();
        _timer.Tick += (_, _) =>
        {
            AttachScroll();
            if (DataContext is not CommandLineToolboxViewModel vm || !vm.HasPendingEntries) return;
            var follow = _following; if (follow) _programmaticScroll = true;
            if (vm.FlushPendingEntries(100) <= 0) { _programmaticScroll = false; return; }
            if (follow) Dispatcher.UIThread.Post(ScrollToLatest, DispatcherPriority.Background);
            else NewOutput.IsVisible = true;
        };
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => { _timer.Stop(); if (_outputScroll is not null) _outputScroll.ScrollChanged -= OutputScrolled; _outputScroll = null; };
    }
    private void AttachScroll()
    {
        var scroll = Output.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (ReferenceEquals(scroll, _outputScroll)) return;
        if (_outputScroll is not null) _outputScroll.ScrollChanged -= OutputScrolled;
        _outputScroll = scroll; if (scroll is not null) scroll.ScrollChanged += OutputScrolled;
    }
    private void OutputScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (_programmaticScroll || _outputScroll is null || Math.Abs(e.OffsetDelta.Y) < .01) return;
        _following = _outputScroll.Extent.Height - _outputScroll.Viewport.Height - _outputScroll.Offset.Y <= 2;
        if (_following) NewOutput.IsVisible = false;
    }
    private void ScrollToLatest()
    {
        AttachScroll(); if (_following) _outputScroll?.ScrollToEnd();
        Dispatcher.UIThread.Post(() => _programmaticScroll = false, DispatcherPriority.Background);
    }
    private void FollowOutput(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        _following = true; NewOutput.IsVisible = false; _programmaticScroll = true; ScrollToLatest(); Input.Focus();
    }
    private void TerminalKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not CommandLineToolboxViewModel vm) return;
        // Gestures come from the catalog shared with the WPF terminal.
        switch (Services.AvaloniaShortcuts.Find(CadShortcutScope.Terminal, e.Key, e.KeyModifiers)?.Action)
        {
            case CadShortcutAction.Confirm: if (!vm.AcceptSelectedSuggestion()) vm.SubmitCommandInput(); break;
            case CadShortcutAction.SuggestionUp: if (vm.HasSuggestions && !vm.IsNavigatingHistory) vm.SelectPreviousSuggestion(); else vm.ShowPreviousCommand(); break;
            case CadShortcutAction.SuggestionDown: if (vm.HasSuggestions && !vm.IsNavigatingHistory) vm.SelectNextSuggestion(); else vm.ShowNextCommand(); break;
            case CadShortcutAction.Complete: if (!vm.HasSuggestions) return; vm.CompleteCommand(); break;
            case CadShortcutAction.Cancel: if (vm.HasSuggestions) vm.DismissSuggestions(); else if (vm.IsCommandExecuting) vm.CancelCurrentCommand(true); else { App.Window?.FocusCanvas(); return; } break;
            default: return;
        }
        Input.CaretIndex = Input.Text?.Length ?? 0; e.Handled = true;
    }
    private void AcceptSuggestion(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { if (DataContext is CommandLineToolboxViewModel vm) vm.AcceptSelectedSuggestion(); Input.Focus(); Input.CaretIndex = Input.Text?.Length ?? 0; }
}
