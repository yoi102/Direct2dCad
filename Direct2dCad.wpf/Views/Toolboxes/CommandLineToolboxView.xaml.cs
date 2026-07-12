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

    public CommandLineToolboxView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) =>
        {
            CommandInput.Focus();
            ScheduleScrollToEnd();
        };
        CommandInput.KeyDown += OnCommandInputKeyDown;
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
        ScheduleScrollToEnd();
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
            FindVisualChild<ScrollViewer>(OutputList)?.ScrollToEnd();
        });
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
        }
    }
}
