using Direct2dCad.CommandLine;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Tools;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CommandLineReliabilityTests
{
    [Fact]
    public async Task CaptureOutputKeepsMetadataWithoutBinaryText()
    {
        const string encoded = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a6x8AAAAASUVORK5CYII=";
        using var workspace = new ToolExecutionWorkspace { CapturedImage = new(Convert.FromBase64String(encoded), "image/png", 1, 1) };
        workspace.CreateDocument("Capture output");
        var result = await new CadToolCommandLineService(workspace).TryExecuteAsync("capture_view {}");
        Assert.NotNull(result);
        Assert.True(result.Success, result.Message);
        Assert.DoesNotContain(encoded, result.Message);
        Assert.DoesNotContain("data_base64", result.Message);
        Assert.Contains("image/png", result.Message);
        Assert.Contains("metadata only", result.Message);
    }

    [Theory]
    [InlineData("SAVE")]
    [InlineData("save_document {}")]
    public async Task CancelledSaveIsNotASuccessfulTerminalCommand(string command)
    {
        using var workspace = new ToolExecutionWorkspace { AllowSave = false };
        workspace.CreateDocument("Cancelled save");
        var result = await new CadToolCommandLineService(workspace).TryExecuteAsync(command);
        Assert.NotNull(result);
        Assert.False(result.Success, result.Message);
        Assert.Contains("false", result.Message);
    }

    [Theory]
    [InlineData("CIRCLE BANANA")]
    [InlineData("ARC WRONG")]
    [InlineData("ELLIPSE TYPO")]
    [InlineData("CIRCLE 2P extra")]
    [InlineData("LINE unexpected")]
    [InlineData("ERASE 1234")]
    [InlineData("CANCEL typo")]
    [InlineData("UNDO extra")]
    [InlineData("ALL extra")]
    [InlineData("HELP LINE extra")]
    [InlineData("CIRCLE \"RADIUS")]
    public void InvalidArgumentsPreserveDrawingSelectionAndHistory(string input)
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        var line = vm.CadEditor.AddLine(default, new(10, 10));
        var service = new CadCommandLineService();
        Assert.True(service.Execute("PL", vm).Success);
        Assert.True(service.Execute("3,4", vm).Success);
        vm.CadEditor.Selection.Replace([line]);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        Assert.False(service.Execute(input, vm).Success);
        Assert.Equal(CadCanvasToolMode.Polyline, vm.CadCanvasToolMode);
        Assert.Equal(new CadPointD(3, 4), vm.DrawingAnchor);
        Assert.Contains(line, vm.CadEditor.Selection.EntityIds);
        Assert.False(vm.CadEditor.Document.GetEntity(line).IsErased);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    }

    [Fact]
    public void UnavailableModesFailWithoutDestroyingPendingInput()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        var service = new CadCommandLineService();
        Assert.True(service.Execute("PL", vm).Success);
        Assert.True(service.Execute("3,4", vm).Success);
        Assert.False(service.Execute("MVIEW", vm).Success);
        Assert.Equal(new CadPointD(3, 4), vm.DrawingAnchor);
        vm.CadEditor.Document.SetCompatibilityReadOnly("review");
        Assert.False(service.Execute("LINE", vm).Success);
        Assert.Equal(CadCanvasToolMode.Polyline, vm.CadCanvasToolMode);
        Assert.Equal(new CadPointD(3, 4), vm.DrawingAnchor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoordinatePasteUsesPreviewBeforeSelectOrOriginalDrawingTool(bool returnToSelect)
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        var service = new CadCommandLineService();
        foreach (var command in new[] { "LINE", "0,0", "10,0", "ALL", "COPY" })
            Assert.True(service.Execute(command, vm).Success, command);
        if (returnToSelect) Assert.True(service.Execute("SELECT", vm).Success);
        Assert.True(service.Execute("PASTE", vm).Success);
        Assert.True(service.Execute("50,50", vm).Success);
        Assert.False(vm.IsPastePreviewActive);
        Assert.Equal(2, vm.CadEditor.Document.Entities.Values.Count(entity => !entity.IsErased));
        Assert.Null(vm.DrawingAnchor);
        Assert.True(service.Execute("UNDO", vm).Success);
        Assert.Single(vm.CadEditor.Document.Entities.Values, entity => !entity.IsErased);
    }

    [Fact]
    public void MviewCoordinatesCreatePaperViewportAndDoneCompletesAdjustment()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600);
        vm.ActivateLayout(vm.CadEditor.Document.Layouts.Values.First().Id);
        var layout = vm.CadEditor.Document.GetLayout(vm.ActiveLayoutId!.Value);
        var count = layout.Viewports.Count;
        var service = new CadCommandLineService();
        foreach (var command in new[] { "MVIEW", "10,10", "@0.1,0.1", "DONE" })
        {
            var result = service.Execute(command, vm);
            Assert.True(result.Success, command + ": " + result.Message);
        }
        Assert.Equal(count + 1, layout.Viewports.Count);
        Assert.Equal(CadCanvasToolMode.Select, vm.CadCanvasToolMode);
        Assert.False(vm.IsLayoutViewportActive);
    }

    [Fact]
    public void PolarAndPolylineScalarHonorDisplayUnitsAndLockedAngle()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.CadEditor.Document.DocumentSettings.SetUnit(CadUnit.Inch);
        var service = new CadCommandLineService();
        Assert.True(service.Execute("PL", vm).Success);
        Assert.True(service.Execute("1<90", vm).Success);
        Assert.Equal(25.4, vm.DrawingAnchor!.Value.Y, 8);
        var angle = Assert.Single(vm.DynamicInputFields, field => field.Key == "Angle");
        angle.Text = "90";
        Assert.True(service.Execute("2", vm).Success);
        Assert.Equal(76.2, vm.DrawingAnchor!.Value.Y, 8);
        Assert.True(service.Execute("DONE", vm).Success);
        Assert.IsType<CadPolyline>(Assert.Single(vm.CadEditor.Document.Entities.Values));
    }

    [Theory]
    [InlineData("LIST_DOCUMENTS {}")]
    [InlineData("TOOL List_Documents {}")]
    [InlineData("._list_documents {}")]
    public async Task ToolNamesResolveToCanonicalExecutorNames(string command)
    {
        using var workspace = new ToolExecutionWorkspace();
        var service = new CadToolCommandLineService(workspace);
        var result = await service.TryExecuteAsync(command);
        Assert.NotNull(result);
        Assert.True(result.Success, result.Message);
        Assert.StartsWith("list_documents:", result.Message);
        Assert.Null(await service.TryExecuteAsync("HELP UNDO"));
    }

    [Fact]
    public async Task FriendlyWorkspaceLayerAndMeasurementCommandsReuseExistingTools()
    {
        using var workspace = new ToolExecutionWorkspace();
        var service = new CadToolCommandLineService(workspace);
        await Run("NEW \"First drawing\"");
        Assert.Equal("First drawing", workspace.GetActiveDocument()!.Name);
        await Run("LAYER NEW \"Outside walls\"");
        await Run("LAYER SET \"Outside walls\"");
        var vm = workspace.GetActiveDocument()!.GetViewModel();
        Assert.Equal("Outside walls", vm.CadEditor.Document.GetLayer(vm.DrawingLayerId).Name);
        await Run("LAYER LOCK \"Outside walls\"");
        Assert.True(vm.CadEditor.Document.GetLayer(vm.DrawingLayerId).IsLocked);
        await Run("LAYER UNLOCK \"Outside walls\"");
        vm.CadEditor.Document.DocumentSettings.SetUnit(CadUnit.Inch);
        Assert.Contains("Distance: 5 in", (await Run("DIST 0,0 3,4")).Message);
        var id = vm.CadEditor.AddCircle(default, 25.4);
        vm.SelectEntities([id]);
        Assert.Contains("Area 3.141592654 in²", (await Run("AREA")).Message);
        await Run("SAVE \"C:\\test drawing.d2cad\"");
        await Run("OPEN \"C:\\second drawing.d2cad\"");
        Assert.Equal("second drawing", workspace.GetActiveDocument()!.Name);

        async Task<CadToolCommandLineExecution> Run(string command)
        {
            var result = await service.TryExecuteAsync(command);
            Assert.NotNull(result);
            Assert.True(result.Success, command + ": " + result.Message);
            return result;
        }
    }

    [Fact]
    public async Task CompletionIncludesSubmodesLayerNamesEntityIdsAndHints()
    {
        var builtins = new CadCommandLineService();
        Assert.Contains("CIRCLE 2P", builtins.Complete("CIRCLE "));
        Assert.Contains("ARC SCE", builtins.Complete("A SC"));
        Assert.Empty(builtins.Complete("CIRCLE", 0));
        using var workspace = new ToolExecutionWorkspace();
        var vm = workspace.CreateDocument("Hints").GetViewModel();
        var tools = new CadToolCommandLineService(workspace);
        Assert.True((await tools.TryExecuteAsync("LAYER NEW \"Outside walls\""))!.Success);
        Assert.Contains("LAYER SET \"Outside walls\"", tools.Complete("LA SET Out"));
        var id = vm.CadEditor.AddCircle(default, 10);
        vm.SelectEntities([id]);
        Assert.Contains($"SUBTRACT {id.Value}", tools.Complete("SUBTRACT "));
        Assert.Contains("argument 2", tools.GetInputHint("ROTATE 90 "));
        Assert.Empty(tools.Complete("LAYER NEW "));
    }
}
