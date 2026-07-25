using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor.Commands;

namespace Direct2dCad.Editor.Tests;

public sealed class CadEditorTests
{
    [Fact]
    public void DocumentAndEditorCommandHistoriesUndoIndependently()
    {
        var editor = new CadEditor(CadDocument.Create("Test"));
        var addLine = new AddLineCommand(CadPointD.Origin, new CadPointD(10, 0));
        editor.Execute(addLine);
        editor.Execute(new PanViewportCommand(new CadVectorD(12, -7)));
        var lineId = Assert.IsType<EntityId>(addLine.CreatedEntityId);

        editor.UndoEditor();

        Assert.Equal(CadPointD.Origin, editor.Viewport.Offset);
        Assert.False(editor.Document.GetEntity(lineId).IsErased);

        editor.UndoDocument();

        Assert.True(editor.Document.GetEntity(lineId).IsErased);
    }

    [Fact]
    public void ExecuteRange_UsesCurrentUndoModeAtUndoTime()
    {
        var editor = new CadEditor(CadDocument.Create("Test"));
        var commands = new[]
        {
            new AddLineCommand(new CadPointD(0, 0), new CadPointD(1, 0)),
            new AddLineCommand(new CadPointD(0, 1), new CadPointD(1, 1)),
            new AddLineCommand(new CadPointD(0, 2), new CadPointD(1, 2))
        };
        editor.ExecuteRange(commands);
        editor.DocumentHistorySettings.UndoMode = CadCommandBatchUndoMode.StepByStep;

        editor.UndoDocument();

        Assert.False(editor.Document.GetEntity(commands[0].CreatedEntityId!.Value).IsErased);
        Assert.False(editor.Document.GetEntity(commands[1].CreatedEntityId!.Value).IsErased);
        Assert.True(editor.Document.GetEntity(commands[2].CreatedEntityId!.Value).IsErased);

        editor.DocumentHistorySettings.UndoMode = CadCommandBatchUndoMode.Batch;
        editor.UndoDocument();

        Assert.True(editor.Document.GetEntity(commands[0].CreatedEntityId!.Value).IsErased);
        Assert.True(editor.Document.GetEntity(commands[1].CreatedEntityId!.Value).IsErased);
    }

    [Fact]
    public void MoveCommand_UpdatesSpatialIndexAndUndoRestoresOldLocation()
    {
        var document = CadDocument.Create("Test");
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var editor = new CadEditor(document);
        var oldArea = CadRectD.FromLTRB(-1, -1, 11, 1);
        var newArea = CadRectD.FromLTRB(99, 49, 111, 51);

        editor.Execute(new MoveEntitiesCommand([line.Id], new CadVectorD(100, 50)));

        Assert.DoesNotContain(line.Id, editor.SpatialIndex.Query(BlockId.ModelSpace, oldArea));
        Assert.Contains(line.Id, editor.SpatialIndex.Query(BlockId.ModelSpace, newArea));

        editor.UndoDocument();

        Assert.Contains(line.Id, editor.SpatialIndex.Query(BlockId.ModelSpace, oldArea));
        Assert.DoesNotContain(line.Id, editor.SpatialIndex.Query(BlockId.ModelSpace, newArea));
    }

    [Fact]
    public void ClickSelectionPrefersLayerPriorityThenZIndexThenLaterEntity()
    {
        var document = CadDocument.Create("Test");
        var lowLayerId = document.CreateLayer("Low", CadColor.Green, CadLineWeight.Default);
        var highLayerId = document.CreateLayer("High", CadColor.Green, CadLineWeight.Default);
        document.DocumentSettings.LayerDrawingPriority.SetPriority(lowLayerId, 1);
        document.DocumentSettings.LayerDrawingPriority.SetPriority(highLayerId, 10);

        var low = document.AddLine(CadPointD.Origin, new CadPointD(10, 0), lowLayerId);
        low.SetZIndex(100);
        var firstHigh = document.AddLine(CadPointD.Origin, new CadPointD(10, 0), highLayerId);
        firstHigh.SetZIndex(5);
        var laterHigh = document.AddLine(CadPointD.Origin, new CadPointD(10, 0), highLayerId);
        laterHigh.SetZIndex(5);
        var editor = new CadEditor(document);
        var command = new ClickSelectCommand(new CadPointD(5, 0), tolerance: 0.1);

        editor.Execute(command);

        Assert.Equal(laterHigh.Id, command.SelectedEntityId);
        Assert.DoesNotContain(low.Id, editor.Selection.EntityIds);
    }
}
