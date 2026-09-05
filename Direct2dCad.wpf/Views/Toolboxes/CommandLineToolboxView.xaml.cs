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
    private static readonly TimeSpan OutputFlushInterval = TimeSpan.FromMilliseconds(50);
    private const int MaximumFlushBatchSize = 100;
    private INotifyCollectionChanged? _entries;
    private readonly DispatcherTimer _outputFlushTimer;
    private bool _scrollToEndPending;
    private bool _isFollowingOutput = true;
    private bool _isProgrammaticScroll;
    private ScrollViewer? _outputScrollViewer;
    private int _scrollGeneration;

    public CommandLineToolboxView()
    {
        InitializeComponent();
        _outputFlushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = OutputFlushInterval
        };
        _outputFlushTimer.Tick += OnOutputFlushTimerTick;
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        CommandInput.PreviewKeyDown += OnCommandInputKeyDown;
        SuggestionList.PreviewMouseLeftButtonUp += OnSuggestionMouseLeftButtonUp;
        OutputList.KeyDown += OnOutputListKeyDown;
        OutputList.PreviewMouseWheel += (_, e) =>
        {
            if (e.Delta <= 0) return;
            CancelPendingScroll();
            _isFollowingOutput = false;
        };
        NewOutputButton.Click += (_, _) => FollowLatestOutput();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        CancelPendingScroll();
        SubscribeToEntries(IsLoaded ? e.NewValue as CommandLineToolboxViewModel : null);
        _isFollowingOutput = true;
        if (IsLoaded) ScheduleScrollToEnd();
    }

    private void SubscribeToEntries(CommandLineToolboxViewModel? viewModel)
    {
        if (_entries is not null)
            _entries.CollectionChanged -= OnEntriesChanged;

        _entries = viewModel?.Entries;
        if (_entries is not null)
            _entries.CollectionChanged += OnEntriesChanged;

    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SubscribeToEntries(DataContext as CommandLineToolboxViewModel);
        CommandInput.Focus();
        AttachOutputScrollViewer();
        FlushPendingOutput();
        _outputFlushTimer.Start();
        if (_isFollowingOutput) ScheduleScrollToEnd();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _outputFlushTimer.Stop();
        CancelPendingScroll();
        SubscribeToEntries(null);
        if (_outputScrollViewer is not null)
            _outputScrollViewer.ScrollChanged -= OnOutputScrollChanged;
        _outputScrollViewer = null;
    }

    private void CancelPendingScroll()
    {
        _scrollGeneration++;
        _scrollToEndPending = false;
        _isProgrammaticScroll = false;
    }

    private void OnOutputFlushTimerTick(object? sender, EventArgs e)
    {
        FlushPendingOutput();
    }

    private void FlushPendingOutput()
    {
        if (DataContext is not CommandLineToolboxViewModel viewModel ||
            !viewModel.HasPendingEntries)
        {
            return;
        }

        var shouldFollowOutput = _isFollowingOutput;
        if (shouldFollowOutput)
            _isProgrammaticScroll = true;

        var flushedCount = viewModel.FlushPendingEntries(MaximumFlushBatchSize);
        if (flushedCount == 0 && shouldFollowOutput)
            _isProgrammaticScroll = false;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (OutputList.Items.Count == 0)
        {
            CancelPendingScroll();
            _isFollowingOutput = true;
            NewOutputButton.Visibility = Visibility.Collapsed;
        }
        if (_isFollowingOutput)
            ScheduleScrollToEnd();
        else
            NewOutputButton.Visibility = Visibility.Visible;
    }

    private void ScheduleScrollToEnd()
    {
        if (_scrollToEndPending || !IsLoaded || !_isFollowingOutput)
            return;

        _scrollToEndPending = true;
        var generation = _scrollGeneration;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            if (!IsLoaded || generation != _scrollGeneration || !_isFollowingOutput) return;
            AttachOutputScrollViewer();
            _isProgrammaticScroll = true;
            if (OutputList.Items.Count > 0)
                OutputList.ScrollIntoView(OutputList.Items[^1]);

            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
            {
                if (!IsLoaded || generation != _scrollGeneration || !_isFollowingOutput) return;
                AttachOutputScrollViewer();
                _outputScrollViewer?.ScrollToEnd();
                _isProgrammaticScroll = false;
                _scrollToEndPending = false;
            });
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
        if (Math.Abs(e.VerticalChange) < 0.01)
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
                if (viewModel.SelectedSuggestion is not null && viewModel.AcceptSelectedSuggestion())
                {
                    MoveCaretToCommandEnd();
                    e.Handled = true;
                    break;
                }
                if (viewModel.ExecuteCommandCommand.CanExecute(null))
                    viewModel.ExecuteCommandCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                if (viewModel.HasSuggestions)
                    MoveSuggestionSelection(viewModel, moveNext: false);
                else
                    viewModel.ShowPreviousCommand();
                MoveCaretToCommandEnd();
                e.Handled = true;
                break;
            case Key.Down:
                if (viewModel.HasSuggestions)
                    MoveSuggestionSelection(viewModel, moveNext: true);
                else
                    viewModel.ShowNextCommand();
                MoveCaretToCommandEnd();
                e.Handled = true;
                break;
            case Key.Tab:
                viewModel.CompleteCommand();
                MoveCaretToCommandEnd();
                e.Handled = true;
                break;
            case Key.Escape:
                if (viewModel.HasSuggestions)
                    viewModel.DismissSuggestions();
                else
                    viewModel.CancelCurrentCommand();
                e.Handled = true;
                break;
        }
    }

    private void OnSuggestionMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not CommandLineToolboxViewModel viewModel ||
            SuggestionList.SelectedItem is not string suggestion)
        {
            return;
        }

        viewModel.SelectedSuggestion = suggestion;
        if (!viewModel.AcceptSelectedSuggestion())
            return;

        CommandInput.Focus();
        MoveCaretToCommandEnd();
        e.Handled = true;
    }

    private void MoveCaretToCommandEnd()
    {
        CommandInput.CaretIndex = CommandInput.Text.Length;
    }

    private void MoveSuggestionSelection(CommandLineToolboxViewModel viewModel, bool moveNext)
    {
        if (moveNext)
            viewModel.SelectNextSuggestion();
        else
            viewModel.SelectPreviousSuggestion();

        if (viewModel.SelectedSuggestion is not null)
            SuggestionList.ScrollIntoView(viewModel.SelectedSuggestion);
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
