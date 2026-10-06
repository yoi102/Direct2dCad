using Direct2dCad.CommandLine;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.Rendering.Transient;
namespace Direct2dCad.ViewModels.Tests;
public sealed class DimensionWorkflowTests
{
    [Fact]
    public void CustomTextArrowAndLineWeightSettingsUpdateImmediatelyAndSupportUndo()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600);
        var line = vm.CadEditor.AddLine(default, new(100, 0));
        vm.SelectEntities([line]);
        vm.SetToolMode(CadCanvasToolMode.DimAligned);
        vm.DimensionShapeFont = "monoline";
        vm.DimensionArrow = CadDimensionArrow.Slash;
        vm.DimensionTextHeight = 3;
        vm.DimensionArrowSize = 4;
        vm.DimensionLineWeight = .32;
        vm.DimensionAnnotationScale = 2;
        Click(vm, new(50, 20));
        vm.Escape();
        var dimension = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadDimension>());
        Assert.Equal("monoline", dimension.Definition.Style.ShapeFont);
        Assert.Equal(CadDimensionArrow.Slash, dimension.Definition.Style.Arrow);
        Assert.Equal(3, dimension.Definition.Style.TextHeight);
        Assert.Equal(4, dimension.Definition.Style.ArrowSize);
        Assert.Equal(.32, dimension.Definition.Style.LineWeight);
        Assert.Equal(2, dimension.Definition.AnnotationScale);

        vm.SelectEntities([dimension.Id]);
        vm.DimensionStyleName = "Fine";
        Assert.Equal(.13, vm.DimensionLineWeight);
        vm.DimensionShapeFont = "simplex";
        vm.DimensionArrow = CadDimensionArrow.Closed;
        vm.DimensionLineWeight = .45;
        Assert.Equal("simplex", dimension.Definition.Style.ShapeFont);
        Assert.Equal(CadDimensionArrow.Closed, dimension.Definition.Style.Arrow);
        Assert.Equal(.45, dimension.Definition.Style.LineWeight);
        Assert.Equal(CadAssociationState.Valid, dimension.AssociationState);
        vm.Undo();
        Assert.Equal(.13, vm.DimensionLineWeight);
        vm.Undo();
        Assert.Equal(CadDimensionArrow.Slash, vm.DimensionArrow);
        vm.Undo();
        Assert.Equal("monoline", vm.DimensionShapeFont);
        vm.Undo();
        Assert.Equal(.32, vm.DimensionLineWeight);
        Assert.Equal(3, vm.DimensionTextHeight);
        Assert.Equal(4, vm.DimensionArrowSize);
        Assert.Equal("ISO", vm.DimensionStyleName);
        vm.Redo(); vm.Redo(); vm.Redo();
        vm.Redo();
        Assert.Equal("simplex", vm.DimensionShapeFont);
        Assert.Equal(CadDimensionArrow.Closed, vm.DimensionArrow);
        Assert.Equal(.45, vm.DimensionLineWeight);

        var before = vm.CadEditor.CreateDocumentHistorySnapshot();
        vm.DimensionLineWeight = 0;
        Assert.True(vm.CadEditor.DocumentHistoryEquals(before));
        Assert.Equal(.45, dimension.Definition.Style.LineWeight);
        Assert.NotEmpty(vm.StepInputError);
    }

    [Fact]public void PlacementGripKeepsAssociationAndReassociationCanBeCancelledOrCommitted()
    {
        using var c=new CadToolboxTestContext();var vm=c.Document;vm.SetViewportSize(800,600);var editor=vm.CadEditor;var line=editor.AddLine(default,new(100,0));
        vm.SelectEntities([line]);vm.SetToolMode(CadCanvasToolMode.DimAligned);Click(vm,new(50,20));vm.Escape();var dimension=Assert.Single(editor.Document.Entities.Values.OfType<CadDimension>());
        vm.SelectEntities([dimension.Id]);vm.PointerMove(editor.Viewport.WorldToScreen(dimension.Definition.Placement));
        Click(vm,dimension.Definition.Placement);vm.PointerMove(editor.Viewport.WorldToScreen(new(50,40)));Click(vm,new(50,40));
        Assert.True(dimension.Definition.Placement.NearEquals(new(50,40)));Assert.Equal(CadAssociationState.Valid,dimension.AssociationState);vm.Undo();Assert.True(dimension.Definition.Placement.NearEquals(new(50,20)));
        vm.SelectEntities([dimension.Id]);vm.DetachDimensionCommand.Execute(null);Assert.Equal(CadAssociationState.Detached,dimension.AssociationState);
        vm.ReassociateDimensionCommand.Execute(null);vm.Escape();Assert.Equal(CadAssociationState.Detached,dimension.AssociationState);
        vm.SelectEntities([dimension.Id]);vm.ReassociateDimensionCommand.Execute(null);Click(vm,new(50,0));Click(vm,new(50,30));Assert.Equal(CadAssociationState.Valid,dimension.AssociationState);Assert.Single(editor.Document.Entities.Values.OfType<CadDimension>());
    }
    [Theory]
    [InlineData(CadCanvasToolMode.DimLinearX)][InlineData(CadCanvasToolMode.DimLinearY)][InlineData(CadCanvasToolMode.DimAligned)]
    [InlineData(CadCanvasToolMode.DimRadius)][InlineData(CadCanvasToolMode.DimDiameter)][InlineData(CadCanvasToolMode.DimAngular)][InlineData(CadCanvasToolMode.Leader)]
    public void EachAnnotationToolHasPreviewSingleUndoAndEscapeRetainsCommittedResults(CadCanvasToolMode mode)
    {
        using var c=new CadToolboxTestContext();var vm=c.Document;vm.SetViewportSize(800,600);vm.IsObjectSnapEnabled=false;
        if(mode is CadCanvasToolMode.DimRadius or CadCanvasToolMode.DimDiameter){var circle=vm.CadEditor.AddCircle(default,20);vm.SelectEntities([circle]);}
        vm.SetToolMode(mode);Assert.True(vm.IsDimensionContext);
        var before=vm.CadEditor.CreateDocumentHistorySnapshot();
        if(mode is not (CadCanvasToolMode.DimRadius or CadCanvasToolMode.DimDiameter))
        {Click(vm,new(0,0));Click(vm,new(20,20));if(mode==CadCanvasToolMode.DimAngular)Click(vm,new(-20,20));}
        vm.PointerMove(vm.CadEditor.Viewport.WorldToScreen(new(30,30)));Assert.True(vm.CadEditor.DocumentHistoryEquals(before));
        Click(vm,new(30,30));var dimension=Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadDimension>());
        vm.Escape();Assert.False(dimension.IsErased);Assert.Equal(CadCanvasToolMode.Select,vm.CadCanvasToolMode);
        vm.Undo();Assert.True(dimension.IsErased);vm.Redo();Assert.False(dimension.IsErased);
    }
    private static void Click(CadDocumentViewModel vm,CadPointD p)
    {var s=vm.CadEditor.Viewport.WorldToScreen(p);vm.PointerMove(s);vm.PointerDown(s,CadCanvasPointerButton.Left,false);vm.PointerUp(s,CadCanvasPointerButton.Left);}
    [Fact]public void PreselectedLinePreviewCommitEditDeleteAndUndoUseSameReferences()
    {
        using var c=new CadToolboxTestContext();var vm=c.Document;vm.SetViewportSize(800,600);var line=vm.CadEditor.AddLine(default,new(100,0));
        vm.SelectEntities([line]);vm.SetToolMode(CadCanvasToolMode.DimAligned);var history=vm.CadEditor.CreateDocumentHistorySnapshot();vm.PointerMove(vm.CadEditor.Viewport.WorldToScreen(new(50,20)));Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Click(vm,new(50,20));var d=Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadDimension>());Assert.Equal(100,d.Measurement);Assert.Equal(CadAssociationState.Valid,d.AssociationState);
        vm.Escape();vm.CadEditor.SetLineGeometry(line,default,new(125,0));Assert.Equal(125,d.Measurement);
        vm.SelectEntities([d.Id]);vm.DimensionStyleName="Fine";vm.DimensionTextOverride="CHECK";Assert.True(d.HasTextOverride);Assert.Equal(.13,d.Definition.Style.LineWeight);vm.Undo();Assert.False(d.HasTextOverride);Assert.Equal("",vm.DimensionTextOverride);
        vm.CadEditor.DeleteEntities([line]);Assert.Equal(CadAssociationState.Broken,d.AssociationState);vm.Undo();Assert.Equal(CadAssociationState.Valid,d.AssociationState);
    }
    [Fact]public void ExplicitRelativePointsRemainExactAndFinishExitsWithoutExtraAnnotation()
    {
        using var c=new CadToolboxTestContext();var vm=c.Document;vm.IsGridSnapEnabled=true;var cli=new CadCommandLineService();
        Assert.True(cli.Execute("DIM",vm).Success);Assert.True(cli.Execute("1.25,2.75",vm).Success);Assert.True(cli.Execute("@25.4,0",vm).Success);Assert.True(cli.Execute("@0,10",vm).Success);
        var d=Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadDimension>());Assert.Equal(new CadPointD(1.25,2.75),d.Definition.Anchors[0].Point);Assert.Equal(25.4,d.Measurement,8);Assert.Equal(CadAssociationState.Detached,d.AssociationState);
        Assert.True(cli.Execute("DONE",vm).Success);Assert.Equal(CadCanvasToolMode.Select,vm.CadCanvasToolMode);
    }

    [Theory]
    [InlineData("Unit")][InlineData("Precision")][InlineData("TextHeight")][InlineData("ArrowSize")]
    [InlineData("Scale")][InlineData("Gap")][InlineData("Beyond")][InlineData("LineWeight")]
    [InlineData("Text")][InlineData("Font")][InlineData("Arrow")]
    public void EachParameterUpdatesSelectedDimensionWithoutMovingOrDetachingIt(string parameter)
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        var dimension = CreateSelectedDimension(vm);
        var before = dimension.Definition;
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        switch (parameter)
        {
            case "Unit": vm.DimensionUnit = CadUnit.Inch; break;
            case "Precision": vm.DimensionPrecision = 4; break;
            case "TextHeight": vm.DimensionTextHeight = 4; break;
            case "ArrowSize": vm.DimensionArrowSize = 4; break;
            case "Scale": vm.DimensionAnnotationScale = 2; break;
            case "Gap": vm.DimensionExtensionGap = 2; break;
            case "Beyond": vm.DimensionExtensionBeyond = 2; break;
            case "LineWeight": vm.DimensionLineWeight = .4; break;
            case "Text": vm.DimensionTextOverride = "CHECK"; break;
            case "Font": vm.DimensionShapeFont = "simplex"; break;
            case "Arrow": vm.DimensionArrow = CadDimensionArrow.Closed; break;
        }
        var changed = dimension.Definition;
        Assert.False(vm.CadEditor.DocumentHistoryEquals(history));
        Assert.True(before.Style != changed.Style || before.AnnotationScale != changed.AnnotationScale || before.TextOverride != changed.TextOverride);
        Assert.Equal(before.Kind, changed.Kind);
        Assert.Equal(before.Placement, changed.Placement);
        Assert.Equal(before.LinearRotationRadians, changed.LinearRotationRadians);
        Assert.Equal(before.Anchors, changed.Anchors);
        Assert.Equal(CadAssociationState.Valid, dimension.AssociationState);
        vm.Undo();
        Assert.Equal(before.Style, dimension.Definition.Style);
        Assert.Equal(before.AnnotationScale, dimension.Definition.AnnotationScale);
        Assert.Equal(before.TextOverride, dimension.Definition.TextOverride);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Assert.True(vm.CadEditor.DocumentCommands.CanRedo);
        vm.Redo();
        Assert.Equal(changed.Style, dimension.Definition.Style);
        Assert.Equal(changed.AnnotationScale, dimension.Definition.AnnotationScale);
        Assert.Equal(changed.TextOverride, dimension.Definition.TextOverride);
    }

    [Fact]
    public void FocusedTypingIsImmediateAndOneUndoSelectionRefreshPreservesRedo()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        var dimension = CreateSelectedDimension(vm);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        vm.DimensionTextOverride = "   ";
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.DimensionTextOverride = "";
        vm.BeginDimensionPropertyEdit();
        foreach (var text in new[] { "C", "CH", "CHECK" })
        {
            vm.DimensionTextOverride = text;
            Assert.Equal(text, dimension.Definition.TextOverride);
        }
        vm.EndDimensionPropertyEdit();
        vm.Undo();
        Assert.Null(dimension.Definition.TextOverride);
        Assert.Equal("", vm.DimensionTextOverride);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.SelectEntities([]);
        vm.SelectEntities([dimension.Id]);
        Assert.True(vm.CadEditor.DocumentCommands.CanRedo);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.Redo();
        Assert.Equal("CHECK", dimension.Definition.TextOverride);
        Assert.Equal("CHECK", vm.DimensionTextOverride);
        history = vm.CadEditor.CreateDocumentHistorySnapshot();
        vm.DimensionTextOverride = "   ";
        vm.Undo();
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.DimensionTextOverride = "CHECK";
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    }

    [Theory]
    [InlineData("TextHeight")][InlineData("ArrowSize")][InlineData("Scale")][InlineData("Precision")]
    [InlineData("Gap")][InlineData("Beyond")][InlineData("LineWeight")][InlineData("Font")]
    public void InvalidParametersKeepGeometryAndHistoryAndRecoverAfterCorrection(string parameter)
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        var dimension = CreateSelectedDimension(vm);
        var before = dimension.Definition;
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        switch (parameter)
        {
            case "TextHeight": vm.DimensionTextHeight = 0; break;
            case "ArrowSize": vm.DimensionArrowSize = double.NaN; break;
            case "Scale": vm.DimensionAnnotationScale = double.PositiveInfinity; break;
            case "Precision": vm.DimensionPrecision = 9; break;
            case "Gap": vm.DimensionExtensionGap = -1; break;
            case "Beyond": vm.DimensionExtensionBeyond = -1; break;
            case "LineWeight": vm.DimensionLineWeight = 0; break;
            case "Font": vm.DimensionShapeFont = "missing-font"; break;
        }
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Assert.Equal(before.Style, dimension.Definition.Style);
        Assert.Equal(before.AnnotationScale, dimension.Definition.AnnotationScale);
        Assert.NotEmpty(vm.StepInputError);
        // Reload the selection without writing a command, then continue editing.
        vm.SelectEntities([]); vm.SelectEntities([dimension.Id]);
        vm.DimensionTextHeight = 5;
        Assert.Equal(5, dimension.Definition.Style.TextHeight);
        Assert.Empty(vm.StepInputError);
    }

    [Theory]
    [InlineData("Entity")][InlineData("Layer")][InlineData("ReadOnly")]
    public void LockedOrReadOnlyDimensionCannotBeEdited(string access)
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        var dimension = CreateSelectedDimension(vm);
        if (access == "Entity") dimension.SetLocked(true);
        else if (access == "Layer") vm.CadEditor.Document.GetLayer(dimension.LayerId).SetLocked(true);
        else vm.CadEditor.Document.SetCompatibilityReadOnly("read-only");
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        var before = dimension.Definition.Style;
        Assert.False(vm.CanEditDimensionParameters);
        vm.DimensionTextHeight = 5;
        Assert.Equal(before, dimension.Definition.Style);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
    }

    [Fact]
    public void BeginningAnnotationToolAndEditingDefaultsDoNotModifyPreviouslySelectedDimension()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        var dimension = CreateSelectedDimension(vm);
        vm.DimensionUnit = CadUnit.Inch;
        var before = dimension.Definition.Style;
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        vm.SetToolMode(CadCanvasToolMode.DimAligned);
        vm.DimensionStyleName = "Large";
        vm.DimensionTextHeight = 6;
        vm.DimensionTextOverride = "DEFAULT";
        Assert.Equal(before, dimension.Definition.Style);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Click(vm, new(0, 40)); Click(vm, new(100, 40));
        vm.PointerMove(vm.CadEditor.Viewport.WorldToScreen(new(50, 60)));
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Click(vm, new(50, 60));
        var created = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadDimension>(), d => d.Id != dimension.Id);
        Assert.Equal(6, created.Definition.Style.TextHeight);
        Assert.Equal("DEFAULT", created.Definition.TextOverride);
    }

    [Theory]
    [InlineData(CadCanvasToolMode.DimLinearX)][InlineData(CadCanvasToolMode.DimLinearY)]
    [InlineData(CadCanvasToolMode.DimAligned)][InlineData(CadCanvasToolMode.DimAngular)][InlineData(CadCanvasToolMode.Leader)]
    public void ConfirmedPointsStayMarkedThroughPreviewAndClearAfterPlacement(CadCanvasToolMode mode)
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600);
        vm.IsObjectSnapEnabled = false;
        vm.CadEditor.Document.ViewSettings.Grid.SnapMarkerType = CadSnapMarkerType.None;
        vm.SetToolMode(mode);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        Assert.Empty(AnchorMarkers(vm));
        var first = new CadPointD(10, 10);
        Click(vm, first);
        vm.PointerMove(vm.CadEditor.Viewport.WorldToScreen(new(60, 20)));
        Assert.Equal(first, Assert.Single(AnchorMarkers(vm)).Center);
        vm.PointerLeave();
        Assert.Equal(first, Assert.Single(AnchorMarkers(vm)).Center);
        Click(vm, new(40, 10));
        if (mode == CadCanvasToolMode.DimAngular) Click(vm, new(10, 40));
        vm.PointerMove(vm.CadEditor.Viewport.WorldToScreen(new(50, 50)));
        Assert.Equal(mode == CadCanvasToolMode.DimAngular ? 3 : 2, AnchorMarkers(vm).Length);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        Assert.Empty(vm.CadEditor.Document.Entities);
        Click(vm, new(50, 50));
        Assert.Empty(AnchorMarkers(vm));
        Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadDimension>());
        vm.Undo();
        Assert.Empty(AnchorMarkers(vm));
    }

    [Theory]
    [InlineData("Cancel")][InlineData("Finish")][InlineData("Switch")]
    public void PendingMarkersSurviveIncompleteFinishButClearOnCancelOrToolSwitch(string action)
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600);
        vm.SetToolMode(CadCanvasToolMode.Leader);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        Click(vm, new(10, 10));
        Assert.Single(AnchorMarkers(vm));
        if (action == "Cancel") vm.Escape();
        else if (action == "Finish") vm.CompleteCurrentDrawing();
        else vm.SetToolMode(CadCanvasToolMode.Line);
        if (action == "Finish")
        {
            Assert.Equal(new CadPointD(10, 10), Assert.Single(AnchorMarkers(vm)).Center);
            Assert.Equal(CadCanvasToolMode.Leader, vm.CadCanvasToolMode);
            Assert.NotEmpty(vm.StepInputError);
        }
        else Assert.Empty(AnchorMarkers(vm));
        Assert.Empty(vm.CadEditor.Document.Entities);
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.SetToolMode(CadCanvasToolMode.Leader);
        Assert.Empty(AnchorMarkers(vm));
    }

    [Fact]
    public void PointMarkerKeepsItsScreenSizeAndPreselectedReferencesAreAlsoMarked()
    {
        using var context = new CadToolboxTestContext();
        var vm = context.Document;
        vm.SetViewportSize(800, 600);
        var source = vm.CadEditor.AddLine(new(10, 10), new(100, 10));
        vm.SelectEntities([source]);
        var history = vm.CadEditor.CreateDocumentHistorySnapshot();
        vm.SetToolMode(CadCanvasToolMode.DimAligned);
        var markers = AnchorMarkers(vm);
        Assert.Equal(new[] { new CadPointD(10, 10), new CadPointD(100, 10) }, markers.Select(m => m.Center));
        Assert.All(markers, m => Assert.Equal(4, m.Radius * vm.CadEditor.Viewport.Zoom, 8));
        vm.CadEditor.Viewport.SetView(5, default);
        Assert.All(AnchorMarkers(vm), m => Assert.Equal(4, m.Radius * vm.CadEditor.Viewport.Zoom, 8));
        Assert.True(vm.CadEditor.DocumentHistoryEquals(history));
        vm.Escape();
        var circle = vm.CadEditor.AddCircle(new(50, 50), 10);
        vm.SelectEntities([circle]);
        vm.SetToolMode(CadCanvasToolMode.DimRadius);
        Assert.Equal(new[] { new CadPointD(50, 50), new CadPointD(60, 50) }, AnchorMarkers(vm).Select(m => m.Center));
    }

    private static CadTransientCircle[] AnchorMarkers(CadDocumentViewModel vm) => vm.CreateTransientItems()
        .OfType<CadTransientCircle>().Where(c => c.Style.FillColor is not null).ToArray();

    private static CadDimension CreateSelectedDimension(CadDocumentViewModel vm)
    {
        vm.SetViewportSize(800, 600);
        var line = vm.CadEditor.AddLine(default, new(100, 0));
        vm.SelectEntities([line]); vm.SetToolMode(CadCanvasToolMode.DimAligned);
        Click(vm, new(50, 20)); vm.Escape();
        var dimension = Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadDimension>());
        vm.SelectEntities([dimension.Id]);
        return dimension;
    }
}
