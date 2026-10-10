using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Direct2dCad.ViewModels.Settings;
using Direct2dCad.ViewModels.Settings.UserSettings;

namespace Direct2dCad.Avalonia.Views;

internal static class SettingsWorkspace
{
    public static Control Create(UserSettingsViewModel vm) => Create(vm.Sections, s => s.Title, () => vm.SelectedSection, s => vm.SelectedSection = s, vm);
    public static Control Create(DocumentSettingsViewModel vm) => Create(vm.Sections, s => s.Title, () => vm.SelectedSection, s => vm.SelectedSection = s, vm);
    private static Control Create<T>(IReadOnlyList<T> sections, Func<T, string> title, Func<T> selected, Action<T> select, INotifyPropertyChanged model) where T : class
    {
        var root = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("176,1,*") };
        var navigation = new ListBox { Margin = new Thickness(0, 0, 12, 0), BorderThickness = new Thickness(0) }; navigation.Classes.Add("cadList");
        var pages = sections.Select(section => new ListBoxItem { Tag = section, Content = new TextBlock { Text = title(section), Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Center } }).ToArray();
        foreach (var page in pages) navigation.Items.Add(page);
        var content = new ContentControl { Margin = new Thickness(16, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var cache = new Dictionary<T, Control>();
        void Refresh()
        {
            var section = selected(); navigation.SelectedItem = pages.Single(page => ReferenceEquals(page.Tag, section));
            if (!cache.TryGetValue(section, out var view)) cache[section] = view = KnownViews.Create(section) ?? throw new InvalidOperationException("Missing settings form");
            content.Content = view;
        }
        navigation.SelectionChanged += (_, _) => { if (navigation.SelectedItem is ListBoxItem { Tag: T section } && !ReferenceEquals(selected(), section)) { select(section); Refresh(); } };
        PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName == "SelectedSection") Refresh(); };
        model.PropertyChanged += changed; root.DetachedFromVisualTree += (_, _) => model.PropertyChanged -= changed;
        root.Children.Add(navigation); var separator = new Border(); separator.Bind(Border.BackgroundProperty, separator.GetResourceObservable("MaterialDividerBrush")); Grid.SetColumn(separator, 1); root.Children.Add(separator); Grid.SetColumn(content, 2); root.Children.Add(content); Refresh();
        return root;
    }
}
