namespace Direct2dCad.Avalonia.Views.Toolboxes;
public partial class BlocksView : global::Avalonia.Controls.UserControl { public BlocksView() => InitializeComponent(); }
public partial class SearchView : global::Avalonia.Controls.UserControl { public SearchView() => InitializeComponent(); private void PanResult(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { if (DataContext is Direct2dCad.ViewModels.Toolboxes.EntitySearchToolboxViewModel vm) vm.PanToResultCommand.Execute(null); } }
public partial class FilterView : global::Avalonia.Controls.UserControl { public FilterView() => InitializeComponent(); }
public partial class MessagesView : global::Avalonia.Controls.UserControl { public MessagesView() => InitializeComponent(); private void AllMessages(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { if (DataContext is Direct2dCad.ViewModels.Toolboxes.MessageToolboxViewModel vm) vm.SelectedLevel = null; } }
