using System.Globalization;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Tests;

public sealed class BooleanSelectionAvailabilityTests
{
    [Fact]
    public Task ARegionAndEllipseStayEnabledAndProduceExactEllipticalBoundaries() => CadViewModelTestThread.RunAsync(async () =>
    {
        using var context = new CadToolboxTestContext();
        using var tab = context.CreateEditorTab(new(), new RecordingFileDialogs(), new RecordingDocumentWriter());
        var vm = context.Document;
        var original = await CreateSelectedRegion(vm, tab);
        var ellipse = vm.CadEditor.AddEllipse(new(20, 0), 10, 6);
        Click(vm, new(30, 0), CadCanvasInputModifiers.Shift);
        Assert.Equal(2, vm.CadEditor.Selection.Count);
        AssertAvailability(tab, true);
        await tab.BooleanUnionCommand.ExecuteAsync(null);
        vm.CompleteCurrentDrawing();
        var result = Assert.IsType<CadRegion>(vm.CadEditor.Document.GetEntity(Assert.Single(vm.CadEditor.Selection.EntityIds)));
        Assert.Contains(result.Contours.SelectMany(contour => contour.Edges), edge => edge.IsEllipse);
        Assert.True(vm.CadEditor.Document.GetEntity(original).IsErased);
        Assert.True(vm.CadEditor.Document.GetEntity(ellipse).IsErased);
        vm.Undo();
        Assert.False(vm.CadEditor.Document.GetEntity(original).IsErased);
        Assert.False(vm.CadEditor.Document.GetEntity(ellipse).IsErased);
        Assert.True(result.IsErased);
        vm.Redo();
        Assert.False(result.IsErased);
        Assert.Contains(result.Contours.SelectMany(contour => contour.Edges), edge => edge.IsEllipse);
    });

    [Theory]
    [InlineData(false, CadBooleanOperation.Union)]
    [InlineData(false, CadBooleanOperation.Intersection)]
    [InlineData(false, CadBooleanOperation.Difference)]
    [InlineData(true, CadBooleanOperation.Union)]
    [InlineData(true, CadBooleanOperation.Intersection)]
    [InlineData(true, CadBooleanOperation.Difference)]
    public Task ABooleanResultCanBeShiftSelectedWithAnotherSupportedShapeAndUsedAgain(
        bool rectangle, CadBooleanOperation operation) => CadViewModelTestThread.RunAsync(async () =>
    {
        using var context = new CadToolboxTestContext();
        using var tab = context.CreateEditorTab(new(), new RecordingFileDialogs(), new RecordingDocumentWriter());
        var vm = context.Document;
        var originalRegion = await CreateSelectedRegion(vm, tab);
        var other = rectangle
            ? vm.CadEditor.AddRectangle(CadRectD.FromLTRB(12, -8, 24, 8))
            : vm.CadEditor.AddCircle(new(20, 0), 10);
        var notifications = new int[3];
        tab.BooleanUnionCommand.CanExecuteChanged += (_, _) => notifications[0]++;
        tab.BooleanIntersectionCommand.CanExecuteChanged += (_, _) => notifications[1]++;
        tab.BooleanDifferenceCommand.CanExecuteChanged += (_, _) => notifications[2]++;
        var reasonNotifications = 0;
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(tab.BooleanSelectionDisabledReason)) reasonNotifications++;
        };

        Click(vm, rectangle ? new(24, 0) : new(30, 0), CadCanvasInputModifiers.Shift);

        Assert.Equal(2, vm.CadEditor.Selection.Count);
        Assert.Contains(originalRegion, vm.CadEditor.Selection.EntityIds);
        Assert.Contains(other, vm.CadEditor.Selection.EntityIds);
        AssertAvailability(tab, true);
        Assert.All(notifications, count => Assert.True(count > 0));
        Assert.True(reasonNotifications > 0);
        var command = operation switch
        {
            CadBooleanOperation.Union => tab.BooleanUnionCommand,
            CadBooleanOperation.Intersection => tab.BooleanIntersectionCommand,
            _ => tab.BooleanDifferenceCommand
        };
        await command.ExecuteAsync(null);
        AssertAvailability(tab, false, CadUiText.Get("BooleanSelectionPending"));
        if (operation == CadBooleanOperation.Difference)
        {
            Click(vm, new(-10, 0));
            await vm.BooleanPreviewCompletion;
        }
        Assert.Empty(vm.StepInputError);
        vm.CompleteCurrentDrawing();

        Assert.False(vm.IsBooleanTool);
        var result = Assert.Single(vm.CadEditor.Selection.EntityIds);
        Assert.NotEqual(originalRegion, result);
        Assert.IsType<CadRegion>(vm.CadEditor.Document.GetEntity(result));
        Assert.True(vm.CadEditor.Document.GetEntity(originalRegion).IsErased);
        Assert.True(vm.CadEditor.Document.GetEntity(other).IsErased);
        AssertAvailability(tab, false, CadUiText.Get("BooleanSelectionMinimum"));
    });

    [Theory]
    [InlineData("en-US", "Spline", "not supported")]
    [InlineData("zh-CN", "样条曲线", "暂不支持")]
    [InlineData("ja-JP", "スプライン", "対応していません")]
    public Task RegionAndSplineExplainTheUnsupportedTypeInTheCurrentLanguage(
        string culture, string typeName, string explanation) => CadViewModelTestThread.RunAsync(async () =>
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        using var context = new CadToolboxTestContext();
        using var tab = context.CreateEditorTab(new(), new RecordingFileDialogs(), new RecordingDocumentWriter());
        var vm = context.Document;
        var region = await CreateSelectedRegion(vm, tab);
        var spline = vm.CadEditor.AddSpline([new(20, -10), new(25, 5), new(30, 0)]);
        Click(vm, new(30, 0), CadCanvasInputModifiers.Shift);

        Assert.Equal(2, vm.CadEditor.Selection.Count);
        Assert.Contains(region, vm.CadEditor.Selection.EntityIds);
        Assert.Contains(spline, vm.CadEditor.Selection.EntityIds);
        Assert.Equal(CadCanvasToolMode.Select, vm.CadCanvasToolMode);
        AssertAvailability(tab, false);
        Assert.Contains(typeName, tab.BooleanSelectionDisabledReason);
        Assert.Contains(explanation, tab.BooleanSelectionDisabledReason);
        Assert.DoesNotContain("CadSpline", tab.BooleanSelectionDisabledReason);
    });

    [Theory]
    [InlineData("Empty", "BooleanSelectionMinimum")]
    [InlineData("Single", "BooleanSelectionMinimum")]
    [InlineData("ReadOnly", "BooleanSelectionReadOnly")]
    [InlineData("EntityLocked", "BooleanSelectionEntityLocked")]
    [InlineData("LayerLocked", "BooleanSelectionLayerLocked")]
    [InlineData("LayerFrozen", "BooleanSelectionLayerFrozen")]
    [InlineData("WrongSpace", "BooleanSelectionWrongSpace")]
    [InlineData("RoundedRectangle", "BooleanSelectionRoundedRectangle")]
    [InlineData("OpenPolyline", "BooleanSelectionOpenBoundary")]
    public void ADisabledCommandExplainsTheActualSelectionConstraint(string constraint, string reasonKey)
    {
        using var context = new CadToolboxTestContext();
        using var tab = context.CreateEditorTab(new(), new RecordingFileDialogs(), new RecordingDocumentWriter());
        var vm = context.Document;
        var editor = vm.CadEditor;
        var first = editor.AddCircle(default, 10);
        var second = constraint switch
        {
            "RoundedRectangle" => editor.AddRectangle(CadRectD.FromLTRB(5, -8, 20, 8), 2, 2),
            "OpenPolyline" => editor.AddPolyline([new(0, 0), new(10, 0), new(10, 10)]),
            _ => editor.AddCircle(new(10, 0), 10)
        };
        vm.SelectEntities([first, second]);
        var expectedArgument = string.Empty;
        switch (constraint)
        {
            case "Empty": vm.SelectEntities([]); break;
            case "Single": vm.SelectEntities([first]); break;
            case "ReadOnly": editor.Document.SetCompatibilityReadOnly("Future format"); break;
            case "EntityLocked":
                editor.Document.GetEntity(first).SetLocked(true);
                expectedArgument = CadUiText.Get("Circle");
                break;
            case "LayerLocked":
                editor.Document.GetLayer(LayerId.Default).SetLocked(true);
                expectedArgument = editor.Document.GetLayer(LayerId.Default).Name;
                break;
            case "LayerFrozen":
                editor.Document.GetLayer(LayerId.Default).SetFrozen(true);
                expectedArgument = editor.Document.GetLayer(LayerId.Default).Name;
                break;
            case "WrongSpace": editor.ActiveOwnerBlockId = BlockId.PaperSpace; break;
            case "OpenPolyline": expectedArgument = CadUiText.Get("Polyline"); break;
        }
        AssertAvailability(tab, false,
            string.Format(CultureInfo.CurrentUICulture, CadUiText.Get(reasonKey), expectedArgument));
    }

    [Fact]
    public void LanguageChangesRefreshHelpTextEvenWhenSelectionAndCanExecuteStayUnchanged()
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            using var context = new MainWindowTestContext();
            var (tab, _) = context.AddDocument("Boolean language");
            var vm = tab.CadDocumentViewModel;
            var circle = vm.CadEditor.AddCircle(default, 10);
            var spline = vm.CadEditor.AddSpline([new(10, 0), new(15, 5), new(20, 0)]);
            vm.SelectEntities([circle, spline]);
            Assert.Contains("Spline", tab.BooleanUnionHelpText);
            var publishedHelp = new List<string>();
            tab.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(tab.BooleanUnionHelpText)) publishedHelp.Add(tab.BooleanUnionHelpText);
            };

            // The recording culture service does not change the thread culture.
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            context.ViewModel.ChangeCultureCommand.Execute("2052");
            Assert.Contains(publishedHelp, text => text.Contains("暂不支持样条曲线", StringComparison.Ordinal));
            publishedHelp.Clear();
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
            tab.ApplyUserSettings(CadUserSettings.CreateDefault());
            Assert.Contains(publishedHelp, text => text.Contains("スプラインに対応していません", StringComparison.Ordinal));
            AssertAvailability(tab, false);
            Assert.Equal(2, vm.CadEditor.Selection.Count);
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }

    private static async Task<EntityId> CreateSelectedRegion(CadDocumentViewModel vm, EditorTabViewModel tab)
    {
        vm.SetViewportSize(800, 600);
        vm.CadEditor.Viewport.SetView(10, new(300, 300));
        var first = vm.CadEditor.AddCircle(default, 10);
        var second = vm.CadEditor.AddCircle(new(10, 0), 10);
        vm.SelectEntities([first, second]);
        await tab.BooleanUnionCommand.ExecuteAsync(null);
        vm.CompleteCurrentDrawing();
        var result = Assert.Single(vm.CadEditor.Selection.EntityIds);
        Assert.IsType<CadRegion>(vm.CadEditor.Document.GetEntity(result));
        Assert.False(vm.IsBooleanTool);
        return result;
    }

    private static void Click(CadDocumentViewModel vm, CadPointD world, CadCanvasInputModifiers modifiers = CadCanvasInputModifiers.None)
    {
        var screen = vm.CadEditor.Viewport.WorldToScreen(world);
        vm.PointerDown(screen, CadCanvasPointerButton.Left, false, modifiers);
        vm.PointerUp(screen, CadCanvasPointerButton.Left);
    }

    private static void AssertAvailability(EditorTabViewModel tab, bool expected, string? reason = null)
    {
        Assert.Equal(expected, tab.CanBooleanSelection);
        Assert.Equal(expected, tab.CadDocumentViewModel.CanBooleanSelection);
        Assert.Equal(expected, tab.BooleanUnionCommand.CanExecute(null));
        Assert.Equal(expected, tab.BooleanIntersectionCommand.CanExecute(null));
        Assert.Equal(expected, tab.BooleanDifferenceCommand.CanExecute(null));
        Assert.Equal(expected, string.IsNullOrEmpty(tab.BooleanSelectionDisabledReason));
        if (reason is not null) Assert.Equal(reason, tab.BooleanSelectionDisabledReason);
        if (!expected)
        {
            Assert.All(new[] { tab.BooleanSelectionHelpText, tab.BooleanUnionHelpText, tab.BooleanIntersectionHelpText, tab.BooleanDifferenceHelpText },
                help => Assert.EndsWith(tab.BooleanSelectionDisabledReason, help));
        }
    }
}
