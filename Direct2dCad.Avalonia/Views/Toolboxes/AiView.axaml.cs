using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Direct2dCad.Avalonia.Services;
using Direct2dCad.ViewModels.Toolboxes;
namespace Direct2dCad.Avalonia.Views.Toolboxes;
public partial class AiView : UserControl
{
    private bool _enterHeld;
    public AiView()
    {
        InitializeComponent();
        Prompt.AddHandler(KeyDownEvent, PromptKey, RoutingStrategies.Tunnel);
        Prompt.KeyUp += (_, e) => { if (e.Key == Key.Enter) _enterHeld = false; };
        Prompt.LostFocus += (_, _) => _enterHeld = false;
        DataContextChanged += (_, _) => AttachMessages();
        DetachedFromVisualTree += (_, _) => DetachMessages();
        AttachedToVisualTree += (_, _) => AttachMessages();
    }
    private System.Collections.Specialized.INotifyCollectionChanged? _messages;
    private void AttachMessages() { DetachMessages(); if (DataContext is AiAssistantToolboxViewModel vm) { _messages = vm.Messages; _messages.CollectionChanged += MessagesChanged; Follow(vm.Messages.Count > 0 ? vm.Messages[^1] : null); } }
    private void DetachMessages() { if (_messages is not null) _messages.CollectionChanged -= MessagesChanged; _messages = null; Follow(null); }
    private void MessagesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) { Follow(Messages.Items.Count > 0 ? Messages.Items[^1] as AiChatItemViewModel : null); global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (Messages.Items.Count > 0 && Messages.Items[^1] is { } item) Messages.ScrollIntoView(item); }); }
    // A streamed reply grows in place; keep its newest line in view while the reader is at the bottom.
    private AiChatItemViewModel? _followed;
    private bool _followPending;
    private void Follow(AiChatItemViewModel? item)
    {
        if (ReferenceEquals(item, _followed)) return;
        if (_followed is not null) _followed.PropertyChanged -= FollowedChanged;
        _followed = item;
        if (_followed is not null) _followed.PropertyChanged += FollowedChanged;
    }
    private void FollowedChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AiChatItemViewModel.Content) || _followPending) return;
        if (global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(Messages).OfType<ScrollViewer>().FirstOrDefault() is not { } scroller) return;
        if (scroller.Offset.Y < scroller.Extent.Height - scroller.Viewport.Height - 24) return;
        _followPending = true;
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { _followPending = false; scroller.ScrollToEnd(); }, global::Avalonia.Threading.DispatcherPriority.Background);
    }
    private void PromptKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not AiAssistantToolboxViewModel vm) return;
        if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control && !vm.IsBusy && ClipboardTextService.HasAttachment()) { vm.PasteFileCommand.Execute(null); e.Handled = true; return; }
        if (e.Key == Key.Enter && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Control) { e.Handled = true; if (_enterHeld) return; _enterHeld = true; if (vm.SendCommand.CanExecute(null)) vm.SendCommand.Execute(null); }
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && vm.StopCommand.CanExecute(null)) { vm.StopCommand.Execute(null); e.Handled = true; }
    }
    private void RemoveAttachment(object? sender, RoutedEventArgs e) { if (DataContext is AiAssistantToolboxViewModel vm && sender is Control { DataContext: AiImageAttachmentViewModel attachment }) { if(vm.RemoveImageCommand.CanExecute(attachment)) vm.RemoveImageCommand.Execute(attachment); } }
}
