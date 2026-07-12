using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Direct2dCad.ViewModels.Toolboxes;

namespace Direct2dCad.wpf.Views.Toolboxes;

public partial class CommandLineToolboxView : UserControl
{
    private INotifyCollectionChanged? _entries;
    private bool _scrollToEndPending;
    private bool _isFollowingOutput = true;
    private bool _isProgrammaticScroll;
    private ScrollViewer? _outputScrollViewer;

    public CommandLineToolboxView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) =>
        {
            CommandInput.Focus();
            AttachOutputScrollViewer();
            ScheduleScrollToEnd();
        };
        CommandInput.KeyDown += OnCommandInputKeyDown;
        OutputList.KeyDown += OnOutputListKeyDown;
        NewOutputButton.Click += (_, _) => FollowLatestOutput();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_entries is not null)
            _entries.CollectionChanged -= OnEntriesChanged;

        _entries = (e.NewValue as CommandLineToolboxViewModel)?.Entries;
        if (_entries is not null)
            _entries.CollectionChanged += OnEntriesChanged;

        ScheduleScrollToEnd();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isFollowingOutput)
            ScheduleScrollToEnd();
        else
            NewOutputButton.Visibility = Visibility.Visible;
    }

    private void ScheduleScrollToEnd()
    {
        if (_scrollToEndPending)
            return;

        _scrollToEndPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _scrollToEndPending = false;
            OutputList.UpdateLayout();
            AttachOutputScrollViewer();
            _isProgrammaticScroll = true;
            _outputScrollViewer?.ScrollToEnd();
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => _isProgrammaticScroll = false);
        });
    }

    private void AttachOutputScrollViewer()
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(OutputList);
        if (ReferenceEquals(_outputScrollViewer, scrollViewer))
            return;

        if (_outputScrollViewer is not null)
            _outputScrollViewer.ScrollChanged -= OnOutputScrollChanged;

        _outputScrollViewer = scrollViewer;
        if (_outputScrollViewer is not null)
            _outputScrollViewer.ScrollChanged += OnOutputScrollChanged;
    }

    private void OnOutputScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isProgrammaticScroll || _outputScrollViewer is null)
            return;

        _isFollowingOutput = _outputScrollViewer.ScrollableHeight - _outputScrollViewer.VerticalOffset <= 2;
        if (_isFollowingOutput)
            NewOutputButton.Visibility = Visibility.Collapsed;
    }

    private void FollowLatestOutput()
    {
        _isFollowingOutput = true;
        NewOutputButton.Visibility = Visibility.Collapsed;
        ScheduleScrollToEnd();
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                return match;

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
                return descendant;
        }

        return null;
    }

    private void OnCommandInputKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CommandLineToolboxViewModel viewModel)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                if (viewModel.ExecuteCommandCommand.CanExecute(null))
                    viewModel.ExecuteCommandCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                viewModel.ShowPreviousCommand();
                CommandInput.CaretIndex = CommandInput.Text.Length;
                e.Handled = true;
                break;
            case Key.Down:
                viewModel.ShowNextCommand();
                CommandInput.CaretIndex = CommandInput.Text.Length;
                e.Handled = true;
                break;
            case Key.Tab:
                viewModel.CompleteCommand();
                CommandInput.CaretIndex = CommandInput.Text.Length;
                e.Handled = true;
                break;
            case Key.Escape:
                viewModel.CancelCurrentCommand();
                e.Handled = true;
                break;
        }
    }

    private void OnOutputListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || Keyboard.Modifiers != ModifierKeys.Control)
            return;

        var text = string.Join(
            Environment.NewLine,
            OutputList.SelectedItems
                .OfType<CadCommandLineEntryViewModel>()
                .Select(entry => $"{entry.Timestamp:HH:mm:ss} {entry.Text}"));
        if (text.Length == 0)
            return;

        Clipboard.SetText(text);
        e.Handled = true;
    }
}
