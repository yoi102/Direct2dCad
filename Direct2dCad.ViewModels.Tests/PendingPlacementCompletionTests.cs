using Direct2dCad.CommandLine;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.ViewModels.Tools;
using MessagePipe;

namespace Direct2dCad.ViewModels.Tests;

public sealed class PendingPlacementCompletionTests
{
    [Theory]
    [InlineData(CadCanvasToolMode.DimLinearX, 1)]
    [InlineData(CadCanvasToolMode.DimLinearX, 2)]
    [InlineData(CadCanvasToolMode.DimAngular, 1)]
    [InlineData(CadCanvasToolMode.DimAngular, 2)]
    [InlineData(CadCanvasToolMode.DimAngular, 3)]
    [InlineData(CadCanvasToolMode.Leader, 1)]
    [InlineData(CadCanvasToolMode.Leader, 2)]
    public void DonePreservesDimensionReferencesUntilAnExplicitPlacement(CadCanvasToolMode mode, int acceptedPointCount)
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context);
        vm.SetToolMode(mode);
        CadPointD[] points = mode == CadCanvasToolMode.DimAngular
            ? [new(0, 0), new(20, 0), new(20, 20)]
            : [new(0, 0), new(20, 0)];
        foreach (var point in points.Take(acceptedPointCount)) Submit(vm, point);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();

        var result = new CadCommandLineService().Execute("DONE", vm);

        Assert.False(result.Success);
        Assert.NotEmpty(vm.StepInputError);
        Assert.Contains(vm.StepInputError, result.Message);
        Assert.Equal(mode, vm.CadCanvasToolMode);
        Assert.Equal(points[acceptedPointCount - 1], vm.DrawingAnchor);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Assert.Empty(vm.CadEditor.Document.Entities);

        foreach (var point in points.Skip(acceptedPointCount)) Submit(vm, point);
        Submit(vm, new(40, 50));
        var dimension = Assert.IsType<CadDimension>(Assert.Single(vm.CadEditor.Document.Entities.Values));
        Assert.Equal(points, dimension.Definition.Anchors.Select(anchor => anchor.Point));
        Assert.Equal(new CadPointD(40, 50), dimension.Definition.Placement);
    }

    [Theory]
    [InlineData(CadCanvasToolMode.DimRadius)]
    [InlineData(CadCanvasToolMode.DimDiameter)]
    public void DonePreservesSeededCircleReferencesBeforePlacement(CadCanvasToolMode mode)
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context);
        var circle = vm.CadEditor.AddCircle(default, 10);
        vm.SelectEntities([circle]);
        vm.SetToolMode(mode);
        var anchor = vm.DrawingAnchor;
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();

        Assert.False(new CadCommandLineService().Execute("DONE", vm).Success);

        Assert.Equal(anchor, vm.DrawingAnchor);
        Assert.Equal(mode, vm.CadCanvasToolMode);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Submit(vm, new(40, 50));
        var dimension = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadDimension>());
        Assert.All(dimension.Definition.Anchors, item => Assert.NotNull(item.Reference));
    }

    [Theory]
    [InlineData(CadCanvasToolMode.DimLinearX)]
    [InlineData(CadCanvasToolMode.DimRadius)]
    public void DoneCanExitAnIdleDimensionTool(CadCanvasToolMode mode)
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context);
        vm.SetToolMode(mode);

        Assert.True(new CadCommandLineService().Execute("DONE", vm).Success);

        Assert.Equal(CadCanvasToolMode.Select, vm.CadCanvasToolMode);
        Assert.Empty(vm.CadEditor.Document.Entities);
    }

    [Theory]
    [InlineData(CadCanvasToolMode.Select)]
    [InlineData(CadCanvasToolMode.DimLinearX)]
    [InlineData(CadCanvasToolMode.Offset)]
    public void PasteDoneNeverFallsThroughToUnderlyingToolOrGuessesPlacement(CadCanvasToolMode mode)
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context);
        var source = vm.CadEditor.AddLine(default, new(20, 0));
        vm.SelectEntities([source]);
        Assert.NotNull(vm.CopySelection());
        vm.SetToolMode(mode);
        Assert.True(vm.BeginPastePreview().Handled);
        var snapshot = vm.ActivePasteSnapshot;
        var anchor = vm.DrawingAnchor;
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();

        var result = new CadCommandLineService().Execute("DONE", vm);

        Assert.False(result.Success);
        Assert.NotEmpty(vm.StepInputError);
        Assert.Contains(vm.StepInputError, result.Message);
        Assert.Equal(mode, vm.CadCanvasToolMode);
        Assert.Equal(anchor, vm.DrawingAnchor);
        Assert.True(vm.IsPastePreviewActive);
        Assert.Same(snapshot, vm.ActivePasteSnapshot);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Assert.Single(vm.CadEditor.Document.Entities);

        var screen = vm.CadEditor.Viewport.WorldToScreen(new(60, 40));
        vm.PointerMove(screen);
        vm.PointerDown(screen, CadCanvasPointerButton.Left, false);
        vm.PointerUp(screen, CadCanvasPointerButton.Left);
        Assert.False(vm.IsPastePreviewActive);
        Assert.Equal(2, vm.CadEditor.Document.Entities.Values.Count(entity => !entity.IsErased));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyTerminalEnterReportsPendingPlacementWithoutLosingTheSession(bool paste)
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context);
        if (paste)
        {
            var source = vm.CadEditor.AddLine(default, new(20, 0));
            vm.SelectEntities([source]);
            Assert.NotNull(vm.CopySelection());
            vm.SetToolMode(CadCanvasToolMode.DimLinearX);
            Assert.True(vm.BeginPastePreview().Handled);
        }
        else
        {
            vm.SetToolMode(CadCanvasToolMode.DimLinearX);
            Submit(vm, new(10, 20));
        }
        var anchor = vm.DrawingAnchor;
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        using var terminal = new CommandLineToolboxViewModel(context.Platform, context.Platform,
            new CadCommandLineService(), new NoTools(),
            context.GetService<IAsyncSubscriber<CadCommandActivityMessage>>(),
            context.GetService<IAsyncSubscriber<CadInteractionActivityMessage>>());
        terminal.Attach(vm);

        await terminal.ExecuteCommandCommand.ExecuteAsync(null);

        terminal.FlushPendingEntries();
        Assert.Equal(CadCommandLineEntryKind.Error, terminal.Entries[^1].Kind);
        Assert.Equal(vm.StepInputError, terminal.LatestOutputText);
        Assert.Equal(CadCanvasToolMode.DimLinearX, vm.CadCanvasToolMode);
        Assert.Equal(anchor, vm.DrawingAnchor);
        Assert.Equal(paste, vm.IsPastePreviewActive);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    }

    private static CadDocumentViewModel Prepare(CadToolboxTestContext context)
    {
        var vm = context.Document;
        vm.SetViewportSize(800, 600);
        vm.IsObjectSnapEnabled = false;
        vm.IsGridSnapEnabled = false;
        return vm;
    }

    private static void Submit(CadDocumentViewModel vm, CadPointD point) =>
        Assert.True(((ICadCommandLineContext)vm).SubmitDrawingPoint(new(point.X, point.Y)));

    private sealed class NoTools : ICadToolCommandLineService
    {
        public Task<CadToolCommandLineExecution?> TryExecuteAsync(string commandLine, CancellationToken cancellationToken = default) =>
            Task.FromResult<CadToolCommandLineExecution?>(null);
        public IReadOnlyList<string> Complete(string commandText, int maximumCount = 12) => [];
    }
}
