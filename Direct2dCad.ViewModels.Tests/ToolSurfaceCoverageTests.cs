using System.Globalization;
using System.Xml.Linq;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.CommandLine;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Interactions;
using Direct2dCad.Application.Tools;

namespace Direct2dCad.ViewModels.Tests;

public sealed class ToolSurfaceCoverageTests
{
    [Fact]
    public void EveryRibbonDrawingModeHasAnAiOperation()
    {
        foreach (var mode in Modes(Read("Direct2dCad.wpf/Views/MainRibbonView.xaml")))
        {
            var tool = mode switch
            {
                "Select" => "select_entities", "Line" => "add_line", "Rectangle" => "add_rectangle",
                "Polyline" => "add_polyline", "Polygon" => "add_polygon", "Spline" or "SplineCv" => "add_spline",
                "Text" => "add_text", "SetOrigin" => "set_view_settings", "EllipseArc" => "add_ellipse_arc",
                "EllipseAxisEnd" or "EllipseCenter" => "add_ellipse",
                "CircleCenterDiameter" or "CircleCenterRadius" or "CircleThreePoint" or "CircleTwoPoint" => "add_circle",
                "ArcCenterStartAngle" or "ArcCenterStartEnd" or "ArcCenterStartLength" or "ArcContinue" or
                "ArcStartCenterAngle" or "ArcStartCenterEnd" or "ArcStartCenterLength" or "ArcStartEndAngle" or
                "ArcStartEndDirection" or "ArcStartEndRadius" or "ArcThreePoint" => "add_arc",
                "Break" or "Chamfer" or "Extend" or "Fillet" or "Join" or "Offset" or "Trim" => "edit_curves",
                "PolarArray" or "RectArray" => "array_entities",
                "DimAligned" or "DimAngular" or "DimDiameter" or "DimLinearX" or "DimLinearY" or "DimRadius" or "Leader" => "add_dimension",
                _ => throw new InvalidOperationException($"AI coverage is missing for drawing mode {mode}.")
            };
            Assert.Contains(CadWorkspaceToolExecutor.ToolDefinitions, definition => definition.Name == tool);
        }
    }

    private static string Root()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("DIRECT2DCAD_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot) && File.Exists(Path.Combine(configuredRoot, "Direct2dCad.slnx")))
            return Path.GetFullPath(configuredRoot);
        // CI maps source paths to /_/ for deterministic builds. Locate the
        // checkout from the running test assembly instead of CallerFilePath.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Direct2dCad.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the Direct2dCad checkout for tool surface checks.");
    }

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
