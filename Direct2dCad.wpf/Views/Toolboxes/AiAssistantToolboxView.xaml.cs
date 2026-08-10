using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Direct2dCad.ViewModels.Toolboxes;

namespace Direct2dCad.wpf.Views.Toolboxes;

public partial class AiAssistantToolboxView : UserControl
{
    private INotifyCollectionChanged? _messages;

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
        Dispatcher.BeginInvoke(() =>
        {
            if (MessagesList.Items.Count > 0)
                MessagesList.ScrollIntoView(MessagesList.Items[^1]);
        });
    }

    private void OnPromptPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            DataContext is AiAssistantToolboxViewModel viewModel &&
            TryPasteAttachment(viewModel))
        {
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return;

        if (DataContext is AiAssistantToolboxViewModel sendViewModel &&
            sendViewModel.SendCommand.CanExecute(null))
        {
            e.Handled = true;
            sendViewModel.SendCommand.Execute(null);
        }
    }

    private static bool TryPasteAttachment(AiAssistantToolboxViewModel viewModel)
    {
        if (viewModel.IsBusy)
            return false;

        if (Clipboard.ContainsImage())
        {
            viewModel.PasteImageCommand.Execute(null);
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
            viewModel.AttachImageFile(file);
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
