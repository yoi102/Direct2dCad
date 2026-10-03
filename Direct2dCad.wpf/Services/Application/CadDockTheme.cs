using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Themes;

namespace Direct2dCad.wpf.Services.Application;

/// <summary>Keeps Arc's appearance while binding reparented dock controls to stable sources.</summary>
internal sealed class CadDockTheme : DictionaryTheme
{
    public bool IsDark { get; }

    public CadDockTheme(bool isDark) : base(CreateResources(isDark))
        => IsDark = isDark;

    internal void InstallStableStyles(ToggleDockingManager manager)
    {
        // A theme switch removes the old dictionary before adding the new one. Keep these shared
        // Arc templates in the manager's own resources throughout that interval; their colors are dynamic.
        foreach (var key in ThemeResourceDictionary.Keys)
        {
            manager.Resources[key] = ThemeResourceDictionary[key];
            // Detached buttons temporarily fall back to application resources during a template rebuild.
            if (System.Windows.Application.Current is { } application)
                application.Resources[key] = ThemeResourceDictionary[key];
        }
    }

    private static ResourceDictionary CreateResources(bool isDark)
    {
        var arc = new ResourceDictionary { Source = (isDark ? (Theme)new ArcDarkTheme() : new ArcLightTheme()).GetResourceUri() };
        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(arc);

        var buttonStyle = new Style(typeof(ToggleDockButton), (Style)arc[typeof(ToggleDockButton)]);
        foreach (var property in new[] { FrameworkElement.WidthProperty, FrameworkElement.HeightProperty, FrameworkElement.MinHeightProperty })
            buttonStyle.Setters.Add(new Setter(property, new Binding("Anchorable.Root.Manager.ButtonSize")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.Self), FallbackValue = 28.0, TargetNullValue = 28.0
            }));
        resources[typeof(ToggleDockButton)] = buttonStyle;

        foreach (var key in new[] { "AvalonDockThemeArcAnchorablePaneControlStyle", "ToggleAnchorablePaneControlStyle" })
        {
            var original = (Style)arc[key];
            var container = original.Setters.OfType<Setter>().Single(setter => setter.Property == ItemsControl.ItemContainerStyleProperty).Value as Style;
            if (container is null) continue;
            var tabs = new Style(container.TargetType, container.BasedOn);
            foreach (var setter in container.Setters) tabs.Setters.Add(setter);
            foreach (var trigger in container.Triggers)
            {
                if (trigger is DataTrigger { Binding: Binding { Path.Path: "Items.Count" } } countTrigger)
                {
                    // The TabItem's data is its LayoutAnchorable; its parent pane stays available while detached.
                    var count = new DataTrigger
                    {
                        Binding = new Binding("Parent.Children.Count") { FallbackValue = 1, TargetNullValue = 1 },
                        Value = countTrigger.Value
                    };
                    foreach (var setter in countTrigger.Setters) count.Setters.Add(setter);
                    tabs.Triggers.Add(count);
                }
                else tabs.Triggers.Add(trigger);
            }
            var pane = new Style(original.TargetType, original);
            pane.Setters.Add(new Setter(ItemsControl.ItemContainerStyleProperty, tabs));
            resources[key] = pane;
        }
        return resources;
    }
}
