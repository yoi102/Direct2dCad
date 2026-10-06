using Direct2dCad.CommandLine;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Tests;

public sealed class ToolSessionRecoveryTests
{
    [Theory]
    [InlineData(CadCanvasToolMode.Polyline, "BACK")]
    [InlineData(CadCanvasToolMode.Polygon, "UNDOPOINT")]
    [InlineData(CadCanvasToolMode.Spline, "BACK")]
    public void PointUndoKeepsDocumentHistoryAndResetsRelativeCoordinateAnchor(CadCanvasToolMode mode, string command)
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context);
        var committed = vm.CadEditor.AddLine(new(-40, -40), new(-20, -20));
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        vm.SetToolMode(mode);
        Submit(vm, new(0, 0), new(20, 0), new(20, 20));

        var commands = new CadCommandLineService();
        Assert.True(commands.Execute(command, vm).Success);
        Assert.Equal(new CadPointD(20, 0), vm.DrawingAnchor);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Assert.False(vm.CadEditor.Document.GetEntity(committed).IsErased);
        Assert.True(commands.Execute("@0,30", vm).Success);
        Assert.True(commands.Execute("DONE", vm).Success);

        var created = Assert.Single(vm.CadEditor.Document.Entities.Values, item => item.Id != committed && !item.IsErased);
        var points = created is CadSpline spline ? spline.FitPoints : Assert.IsType<CadPolyline>(created).Points;
        Assert.Equal(new CadPointD[] { new(0, 0), new(20, 0), new(20, 30) }, points);
        vm.Undo();
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    }

    [Fact]
    public void BackNeverFallsThroughToDocumentUndoAndUStillUndoesTheDocument()
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context);
        var line = vm.CadEditor.AddLine(default, new(10, 0));
        var commands = new CadCommandLineService();
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        Assert.False(commands.Execute("BACK", vm).Success);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.SetToolMode(CadCanvasToolMode.Polyline);
        Submit(vm, new CadPointD(20, 20));
        Assert.True(commands.Execute("U", vm).Success);
        Assert.True(vm.CadEditor.Document.GetEntity(line).IsErased);
        Assert.Equal(new CadPointD(20, 20), vm.DrawingAnchor);
        Assert.True(vm.UndoCurrentDrawingStep().Handled);
        Assert.Null(vm.DrawingAnchor);
        Assert.False(vm.CanUndoCurrentDrawingStep);
        Assert.False(vm.UndoCurrentDrawingStep().Handled);
    }

    public static IEnumerable<object[]> StagedModes => Enum.GetValues<CadCanvasToolMode>()
        .Where(mode => mode is >= CadCanvasToolMode.Line and <= CadCanvasToolMode.ArcCenterStartLength || mode == CadCanvasToolMode.Rectangle)
        .Select(mode => new object[] { mode });

    [Theory]
    [MemberData(nameof(StagedModes))]
    public void StagedDrawingCanRetractEveryUncommittedPointAndStartAgain(CadCanvasToolMode mode)
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context);
        vm.SetToolMode(mode);
        var points = PendingPoints(mode);
        Submit(vm, points);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        for (var remaining = points.Length - 1; remaining >= 0; remaining--)
        {
            Assert.True(vm.UndoCurrentDrawingStep().Handled);
            Assert.Equal(remaining == 0 ? (CadPointD?)null : points[remaining - 1], vm.DrawingAnchor);
            Assert.Equal(remaining > 0, vm.CanUndoCurrentDrawingStep);
            Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        }
        Assert.Null(((ICadCommandLineContext)vm).LastInputPoint);
        Assert.Equal(mode, vm.CadCanvasToolMode);
        Assert.Empty(vm.CadEditor.Document.Entities);
        Submit(vm, new CadPointD(80, 70));
        Assert.Equal(new CadPointD(80, 70), vm.DrawingAnchor);
        Assert.Empty(vm.StepInputError);
    }

    [Theory]
    [InlineData(CadCanvasToolMode.Polyline, 2)]
    [InlineData(CadCanvasToolMode.Polygon, 3)]
    [InlineData(CadCanvasToolMode.Spline, 2)]
    public void CanvasAndTextCompletionReportMissingPointsAndOnlyAdvertiseValidCompletion(CadCanvasToolMode mode, int minimum)
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context);
        vm.SetToolMode(mode);
        CadPointD[] points = [new(0, 0), new(20, 0), new(20, 20)];
        for (var count = 0; count < minimum; count++)
        {
            Assert.False(vm.CanCompletePointSequence);
            Assert.DoesNotContain(CadUiText.Get("SpecifyNextOrFinish"), vm.CurrentStepPrompt);
            Assert.False(vm.CompleteCurrentDrawing().Handled);
            var expected = string.Format(CadUiText.Get("DrawingMorePointsRequired"), minimum - count);
            Assert.Equal(expected, vm.StepInputError);
            vm.StepInput = "";
            vm.SubmitStepInputCommand.Execute(null);
            Assert.Equal(expected, vm.StepInputError);
            Assert.False(new CadCommandLineService().Execute("DONE", vm).Success);
            Assert.Equal(expected, vm.StepInputError);
            Submit(vm, points[count]);
        }
        Assert.True(vm.CanCompletePointSequence);
        Assert.Contains(CadUiText.Get("SpecifyNextOrFinish"), vm.CurrentStepPrompt);
        Assert.True(vm.CompleteCurrentDrawing().Handled);
        Assert.Empty(vm.StepInputError);
        Assert.Single(vm.CadEditor.Document.Entities.Values, item => !item.IsErased);
    }

    [Fact]
    public void DoubleClickCannotSwitchSpaceDuringDrawingButStillNavigatesWhenIdle()
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context);
        var layout = vm.CadEditor.Document.Layouts.Values.First();
        var viewport = vm.CadEditor.Document.AddLayoutViewport(layout.Id, CadRectD.FromXYWH(0, 0, 100, 100), default, 1);
        vm.ActivateLayout(layout.Id);
        vm.SetToolMode(CadCanvasToolMode.Polyline);
        Submit(vm, new(10, 10), new(20, 10));
        var inside = vm.CadEditor.Viewport.WorldToScreen(new(30, 20));
        Assert.False(vm.CanActivateDoubleClickObjectOrSpace);
        Assert.False(vm.HandleDoubleClick(inside).Handled);
        Assert.Null(vm.ActiveLayoutViewportId);
        Assert.Equal(new CadPointD(20, 10), vm.DrawingAnchor);
        Assert.True(vm.CompleteCurrentDrawing().Handled);

        vm.SetToolMode(CadCanvasToolMode.Select);
        Assert.True(vm.HandleDoubleClick(inside).Handled);
        Assert.Equal(viewport, vm.ActiveLayoutViewportId);
        vm.SetToolMode(CadCanvasToolMode.Line);
        Submit(vm, new CadPointD(10, 10));
        var outside = vm.CadEditor.Viewport.WorldToScreen(new(200, 200));
        Assert.False(vm.HandleDoubleClick(outside).Handled);
        Assert.Equal(viewport, vm.ActiveLayoutViewportId);
        Assert.Equal(new CadPointD(10, 10), vm.DrawingAnchor);
        vm.Escape();
        Assert.True(vm.HandleDoubleClick(outside).Handled);
        Assert.Null(vm.ActiveLayoutViewportId);
    }

    [Theory]
    [InlineData("drawing")]
    [InlineData("grip")]
    [InlineData("paste")]
    public void OleDoubleClickCannotInterruptActiveToolSession(string session)
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context);
        var ole = vm.CadEditor.AddOleObject(CadRectD.FromXYWH(0, 0, 40, 30), [1, 2, 3]);
        if (session == "drawing")
        {
            vm.SetToolMode(CadCanvasToolMode.Polyline);
            Submit(vm, new CadPointD(80, 70));
        }
        else if (session == "grip")
        {
            var line = vm.CadEditor.AddLine(new(60, 0), new(80, 0));
            vm.SelectEntities([line]);
            var grip = new CadHandleSceneBuilder().BuildSelectionHandles(vm.CadEditor.Document, [line])
                .OfType<CadGripHandle>().First(item => item.Type == CadHandleType.Vertex);
            Click(vm, grip.Position);
            Assert.True(vm.IsGripEditing);
        }
        else
        {
            vm.SelectEntities([ole]);
            Assert.NotNull(vm.CopySelection());
            Assert.True(vm.BeginPastePreview().Handled);
            Assert.True(vm.IsPastePreviewActive);
        }

        var hit = vm.CadEditor.Viewport.WorldToScreen(new(10, 10));
        Assert.False(vm.CanActivateDoubleClickObjectOrSpace);
        Assert.False(vm.OpenOleObjectAt(hit).Handled);
        Assert.Equal(session == "grip", vm.IsGripEditing);
        Assert.Equal(session == "paste", vm.IsPastePreviewActive);
        if (session == "drawing") Assert.Equal(new CadPointD(80, 70), vm.DrawingAnchor);
        vm.Escape();
        Assert.True(vm.CanActivateDoubleClickObjectOrSpace);
        Assert.True(vm.OpenOleObjectAt(hit).Handled);
    }

    [Fact]
    public void CaptureCancellationStopsPanWithoutLosingConfirmedDrawingPoints()
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context);
        vm.SetToolMode(CadCanvasToolMode.Polyline);
        Submit(vm, new(0, 0), new(20, 0));
        vm.PointerDown(new(100, 100), CadCanvasPointerButton.Right, false);
        vm.PointerMove(new(130, 140));
        Assert.True(vm.IsPanning);
        var result = vm.CancelCapturedPointerGesture();
        Assert.True(result.ReleaseMouseCapture);
        Assert.False(vm.IsPanning);
        Assert.Equal(new CadPointD(20, 0), vm.DrawingAnchor);
        var origin = vm.CadEditor.Viewport.WorldToScreen(default);
        vm.PointerMove(new(200, 220));
        Assert.Equal(origin, vm.CadEditor.Viewport.WorldToScreen(default));
        Assert.True(vm.CompleteCurrentDrawing().Handled);
        Assert.Equal(new CadPointD[] { new(0, 0), new(20, 0) }, Assert.IsType<CadPolyline>(Assert.Single(vm.CadEditor.Document.Entities.Values)).Points);
    }

    [Fact]
    public void CaptureCancellationDropsSelectionGestureAndUncommittedGripWithoutChangingTheDocument()
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context);
        var id = vm.CadEditor.AddCircle(default, 10);
        vm.PointerDown(vm.CadEditor.Viewport.WorldToScreen(new(-30, -30)), CadCanvasPointerButton.Left, false);
        Assert.False(vm.CanActivateDoubleClickObjectOrSpace);
        vm.CancelCapturedPointerGesture();
        vm.PointerUp(vm.CadEditor.Viewport.WorldToScreen(new(30, 30)), CadCanvasPointerButton.Left);
        Assert.Empty(vm.CadEditor.Selection.EntityIds);
        Assert.True(vm.CanActivateDoubleClickObjectOrSpace);
        vm.SelectEntities([id]);
        Click(vm, new(10, 0));
        Assert.True(vm.IsGripEditing);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        vm.PointerMove(vm.CadEditor.Viewport.WorldToScreen(new(20, 0)));
        vm.CancelCapturedPointerGesture();
        Assert.False(vm.IsGripEditing);
        Assert.Equal(10, Assert.IsType<CadCircle>(vm.CadEditor.Document.GetEntity(id)).Radius);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Assert.Contains(id, vm.CadEditor.Selection.EntityIds);
    }

    [Fact]
    public void CaptureCancellationAfterViewModelDisposalOnlyRequestsRelease()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.Dispose();
        Assert.True(vm.CancelCapturedPointerGesture().ReleaseMouseCapture);
    }

    [Fact]
    public void CaptureCancellationRollsBackLayoutPanPreviewWithoutCommittingDocumentHistory()
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context);
        var layout = vm.CadEditor.Document.Layouts.Values.First();
        var viewportId = vm.CadEditor.Document.AddLayoutViewport(layout.Id, CadRectD.FromXYWH(0, 0, 100, 100), default, 1);
        vm.ActivateLayout(layout.Id);
        vm.ActivateLayoutViewport(viewportId);
        vm.SetToolMode(CadCanvasToolMode.Polyline);
        Submit(vm, new CadPointD(10, 10));
        var viewport = layout.GetViewport(viewportId);
        var center = viewport.ModelCenter;
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        vm.PointerDown(new(100, 100), CadCanvasPointerButton.Right, false);
        vm.PointerMove(new(180, 140));
        Assert.NotEqual(center, viewport.ModelCenter);
        vm.CancelCapturedPointerGesture();
        Assert.Equal(center, viewport.ModelCenter);
        Assert.False(vm.IsPanning);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Assert.Equal(new CadPointD(10, 10), vm.DrawingAnchor);
    }

    [Theory]
    [InlineData(CadCanvasToolMode.ArcContinue)]
    [InlineData(CadCanvasToolMode.Text)]
    [InlineData(CadCanvasToolMode.SetOrigin)]
    [InlineData(CadCanvasToolMode.InsertBlock)]
    [InlineData(CadCanvasToolMode.LayoutViewport)]
    [InlineData(CadCanvasToolMode.DimLinearX)]
    [InlineData(CadCanvasToolMode.Offset)]
    public void BackDoesNotCancelOrUndoOtherToolSessions(CadCanvasToolMode mode)
    {
        using var context = new CadToolboxTestContext();
        var vm = Prepare(context);
        vm.SetToolMode(mode);
        if (mode == CadCanvasToolMode.DimLinearX)
            Submit(vm, new CadPointD(10, 10));
        var anchor = vm.DrawingAnchor;
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        Assert.False(vm.CanUndoCurrentDrawingStep);
        Assert.False(new CadCommandLineService().Execute("BACK", vm).Success);
        Assert.Equal(mode, vm.CadCanvasToolMode);
        Assert.Equal(anchor, vm.DrawingAnchor);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    }

    private static CadPointD[] PendingPoints(CadCanvasToolMode mode) => mode switch
    {
        CadCanvasToolMode.EllipseArc => [new(-20, 0), new(20, 0), new(0, 10), new(20, 0)],
        CadCanvasToolMode.CircleThreePoint or CadCanvasToolMode.EllipseCenter or CadCanvasToolMode.EllipseAxisEnd or
            >= CadCanvasToolMode.ArcThreePoint and <= CadCanvasToolMode.ArcCenterStartLength => [new(0, 0), new(20, 0)],
        _ => [new(0, 0)]
    };

    private static CadDocumentViewModel Prepare(CadToolboxTestContext context)
    {
        var vm = context.Document;
        vm.SetViewportSize(800, 600);
        vm.CadEditor.Viewport.SetView(10, new(200, 400));
        vm.IsObjectSnapEnabled = false;
        vm.IsGridSnapEnabled = false;
        return vm;
    }

    private static void Submit(CadDocumentViewModel vm, params CadPointD[] points)
    {
        foreach (var point in points)
            Assert.True(((ICadCommandLineContext)vm).SubmitDrawingPoint(new(point.X, point.Y)));
    }

    private static void Click(CadDocumentViewModel vm, CadPointD point)
    {
        var screen = vm.CadEditor.Viewport.WorldToScreen(point);
        vm.PointerMove(screen);
        vm.PointerDown(screen, CadCanvasPointerButton.Left, false);
        vm.PointerUp(screen, CadCanvasPointerButton.Left);
    }
}
