using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.CommandLine;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Interactions;

namespace Direct2dCad.ViewModels.Tests;

public sealed class ToolSurfaceCoverageTests
{
    private static string Root([CallerFilePath] string file = "") =>
        Path.GetDirectoryName(Path.GetDirectoryName(file))!;

    private static XElement Read(string path) => XElement.Load(Path.Combine(Root(), path));

    private static string[] Modes(XElement root) => root.Descendants()
        .Where(element => ((string?)element.Attribute("Command"))?.Contains("SetCadCanvasToolModeCommand") == true)
        .Select(element => (string)element.Attribute("CommandParameter")!)
        .Distinct().Order().ToArray();

    [Fact]
    public void EveryRibbonToolIsAvailableInBothContextMenusAndRadialActions()
    {
        var expected = Modes(Read("Direct2dCad.wpf/Views/MainRibbonView.xaml"));
        Assert.Equal(expected, Modes(Read("Direct2dCad.wpf/Views/EditorTabView.xaml")));
        Assert.Equal(expected, Modes(Read("Direct2dCad.Avalonia/Views/Generated/MainRibbonView.axaml")));
        Assert.Equal(expected, Modes(Read("Direct2dCad.Avalonia/Views/Generated/EditorContextMenu.axaml")));
        foreach (var name in expected)
        {
            Assert.True(Enum.TryParse<CadRadialMenuAction>(name, out var action), $"Missing radial action: {name}");
            Assert.True(CadRadialMenuActionMapper.TryGetToolMode(action, out var mode), $"Unroutable action: {name}");
            Assert.Equal(name, mode.ToString());
            Assert.True(Enum.TryParse<CadCommandLineDrawingMode>(name, out _), $"Missing command-line mode: {name}");
        }
    }

    [Fact]
    public void EveryRadialActionHasLocalizedLabelsAndClientIcons()
    {
        var radialIcons = Read("Direct2dCad.Avalonia/Views/Generated/RadialIconResources.axaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var keys = radialIcons.Descendants().Attributes(x + "Key").Select(a => a.Value).ToHashSet();
        var wpfCatalog = File.ReadAllText(Path.Combine(Root(), "Direct2dCad.wpf/Controls/CadRadialMenuActionIcon.cs"));
        var wpfIcons = Read("Direct2dCad.wpf/Views/CadToolIconResources.xaml")
            .Descendants().Attributes(x + "Key").Select(a => a.Value).ToHashSet();
        foreach (var action in Enum.GetValues<CadRadialMenuAction>())
        {
            foreach (var culture in new[] { "en", "zh-CN", "ja-JP" })
                Assert.False(string.IsNullOrWhiteSpace(Strings.ResourceManager.GetString(
                    CadRadialMenuActionCatalog.GetResourceKey(action), CultureInfo.GetCultureInfo(culture))),
                    $"Missing {culture} label: {action}");
            if (action == CadRadialMenuAction.None) continue;
            Assert.Contains(action.ToString(), keys);
            Assert.Contains($"CadRadialMenuAction.{action}", wpfCatalog);
            if (CadRadialMenuActionMapper.TryGetToolMode(action, out var mode) &&
                mode is not (CadCanvasToolMode.Select or CadCanvasToolMode.Rectangle or CadCanvasToolMode.Text or CadCanvasToolMode.SetOrigin))
                Assert.Contains($"Cad{mode}IconTemplate", wpfIcons);
        }
    }
}
