using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.wpf.Services.Input;

namespace Direct2dCad.wpf.Views.Toolboxes;

public partial class AiAssistantToolboxView : UserControl
{
    private INotifyCollectionChanged? _messages;
    private bool _scrollPending;

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
    }

    private void DetachMessages()
    {
        if (_messages is not null)
            _messages.CollectionChanged -= OnMessagesChanged;
        _messages = null;
    }
}
