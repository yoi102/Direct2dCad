using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Direct2dCad.Client.Common.Settings;

namespace Direct2dCad.Avalonia;

internal static partial class NativeSmoke
{
    private static async Task CaptureDrawingIconsAsync(MainWindow window, ThemeVariant theme, string output)
    {
        var directory = Path.GetDirectoryName(output)!;
        var previousTab = window.Model.TabControlSelectedIndex;
        try
        {
            window.Model.TabControlSelectedIndex = 1;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(150); window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            frame?.Save(Path.Combine(directory, theme == ThemeVariant.Light ? "drawing-icons-light.png" : "drawing-icons-dark.png"));
        }
        finally { window.Model.TabControlSelectedIndex = previousTab; }
        if (theme != ThemeVariant.Dark) return;
        var resources = new Views.Generated.RadialIconResources();
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,*,*,*,*,*,*,*"), RowDefinitions = RowDefinitions.Parse("*,*,*,*,*") };
        var index = 0;
        foreach (var action in Enum.GetValues<CadRadialMenuAction>().Where(action => action != CadRadialMenuAction.None))
        {
            var template = (IDataTemplate)resources.Resources[action.ToString()]!;
            var tile = new StackPanel { Spacing = 10, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
            tile.Children.Add(template.Build(null)!);
            tile.Children.Add(new TextBlock { Text = Direct2dCad.Lang.CadUiText.Get(CadEnumLabelData.Key(action)), Foreground = Brushes.White, FontSize = 11, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
            var border = new Border { Child = tile, Padding = new global::Avalonia.Thickness(8), BorderThickness = new global::Avalonia.Thickness(.5), BorderBrush = Brushes.DimGray };
            Grid.SetColumn(border, index % 8); Grid.SetRow(border, index++ / 8); grid.Children.Add(border);
        }
        var atlas = new Window { Width = 1040, Height = 560, Background = new SolidColorBrush(Color.Parse("#25252A")), Content = grid };
        try
        {
            atlas.Show(window); atlas.UpdateLayout();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            using var frame = atlas.CaptureRenderedFrame(); frame?.Save(Path.Combine(directory, "radial-icon-atlas.png"));
        }
        finally { atlas.Close(); }
    }
}
