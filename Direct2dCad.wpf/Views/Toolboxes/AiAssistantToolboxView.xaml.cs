using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.wpf.Services.Input;

namespace Direct2dCad.wpf.Views.Toolboxes;

public partial class AiAssistantToolboxView : UserControl
{
    private INotifyCollectionChanged? _messages;
    private AiChatItemViewModel? _followedMessage;
    private bool _scrollPending;
    private bool _followPending;

    public AiAssistantToolboxView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachMessages();
        AttachMessages(e.NewValue as AiAssistantToolboxViewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        AttachMessages(DataContext as AiAssistantToolboxViewModel);

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        FollowLastMessage();
        if (_scrollPending)
            return;

        _scrollPending = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (IsLoaded && MessagesList.Items.Count > 0)
                    MessagesList.ScrollIntoView(MessagesList.Items[^1]);
            }
            finally
            {
                _scrollPending = false;
            }
        }), DispatcherPriority.Background);
    }

    // A streamed reply grows in place; keep its newest line in view while the reader is at the bottom.
    private void FollowLastMessage()
    {
        var last = MessagesList.Items.Count > 0 ? MessagesList.Items[^1] as AiChatItemViewModel : null;
        if (ReferenceEquals(last, _followedMessage))
            return;
        if (_followedMessage is not null)
            _followedMessage.PropertyChanged -= OnFollowedMessageChanged;
        _followedMessage = last;
        if (_followedMessage is not null)
            _followedMessage.PropertyChanged += OnFollowedMessageChanged;
    }

    private void OnFollowedMessageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AiChatItemViewModel.Content) || _followPending ||
            FindScrollViewer(MessagesList) is not { } scrollViewer ||
            scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight - 24)
        {
            return;
        }

        _followPending = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _followPending = false;
            if (IsLoaded)
                scrollViewer.ScrollToEnd();
        }), DispatcherPriority.Background);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scrollViewer)
                return scrollViewer;
            if (FindScrollViewer(child) is { } nested)
                return nested;
        }
        return null;
    }

    private void OnPromptPreviewKeyDown(object sender, KeyEventArgs e)
        => HandlePromptKey(e.Key, Keyboard.Modifiers, e);

    internal void HandlePromptKey(Key key, ModifierKeys modifiers, KeyEventArgs e)
        => HandlePromptKey(DataContext as AiAssistantToolboxViewModel, key, modifiers, e);

    internal static void HandlePromptKey(AiAssistantToolboxViewModel? viewModel,
        Key key, ModifierKeys modifiers, KeyEventArgs e)
    {
        // IME confirmation belongs to the text editor; never reinterpret ImeProcessedKey as Enter.
        if (e.Handled || key is Key.ImeProcessed or Key.DeadCharProcessed) return;

        var shortcut = CadShortcutCatalog.Find(CadShortcutScope.AiPrompt, key, modifiers);
        if (shortcut?.Action == CadShortcutAction.PasteAttachment &&
            viewModel is not null &&
            TryPasteAttachment(viewModel))
        {
            e.Handled = true;
            return;
        }

        if (shortcut?.Action != CadShortcutAction.SendPrompt)
            return;

        // A disabled send still owns this gesture, so it cannot confirm a drawing underneath.
        e.Handled = true;
        if (!CadEnterKeyGuard.ShouldIgnoreRepeat(e, modifiers) && viewModel?.SendCommand.CanExecute(null) == true)
        {
            viewModel.SendCommand.Execute(null);
        }
    }

    private static bool TryPasteAttachment(AiAssistantToolboxViewModel viewModel)
    {
        if (viewModel.IsBusy)
            return false;

        if (Clipboard.ContainsImage())
        {
            viewModel.PasteFileCommand.Execute(null);
            return true;
        }

        if (!Clipboard.ContainsFileDropList())
            return false;

        var files = Clipboard.GetFileDropList()
            .Cast<string>()
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .ToArray();
        if (files.Length == 0)
            return false;

        foreach (var file in files)
            viewModel.AttachFile(file);
        return true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachMessages();

    private void AttachMessages(AiAssistantToolboxViewModel? viewModel)
    {
        if (viewModel is null || ReferenceEquals(_messages, viewModel.Messages))
            return;

        DetachMessages();
        _messages = viewModel.Messages;
        _messages.CollectionChanged += OnMessagesChanged;
        FollowLastMessage();
    }

    private void DetachMessages()
    {
        if (_messages is not null)
            _messages.CollectionChanged -= OnMessagesChanged;
        _messages = null;
        if (_followedMessage is not null)
            _followedMessage.PropertyChanged -= OnFollowedMessageChanged;
        _followedMessage = null;
    }
}
