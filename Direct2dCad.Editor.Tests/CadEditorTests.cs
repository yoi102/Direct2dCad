using Direct2dCad.Commands;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Rendering;

namespace Direct2dCad.Editor.Tests;

public sealed class CadEditorTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    [InlineData(12)] [InlineData(13)]
    public void CreationPublishesFinalOwnerAndRedoKeepsOriginalDestination(int kind)
    {
        var document = CadDocument.Create("Paper creation");
        var definition = document.CreateBlockDefinition("Part", CadPointD.Origin);
        var editor = new CadEditor(document) { ActiveOwnerBlockId = BlockId.PaperSpace };
        var publications = 0;
        editor.DocumentChanged += (_, changes) =>
        {
            foreach (var change in changes.EntityChanges)
            {
                if ((change.Kind & CadEntityChangeKind.Created) == 0)
                    continue;
                publications++;
                var entity = document.GetEntity(change.EntityId);
                Assert.Equal(BlockId.PaperSpace, entity.OwnerBlockId);
                if (!entity.Bounds.IsEmpty)
                    Assert.Contains(entity.Id, editor.SpatialIndex.Query(BlockId.PaperSpace, entity.Bounds));
                Assert.DoesNotContain(entity.Id, editor.SpatialIndex.Query(BlockId.ModelSpace,
                    CadRectD.FromXYWH(-100, -100, 1000, 1000)));
            }
        };
        CadPointD[] points = [new(0, 0), new(10, 0), new(10, 10)];
        var bounds = CadRectD.FromXYWH(0, 0, 10, 10);
        var id = kind switch
        {
            0 => editor.AddLine(points[0], points[1]),
            1 => editor.AddCircle(points[0], 5),
            2 => editor.AddEllipse(points[0], 5, 3),
            3 => editor.AddEllipseArc(points[0], 5, 3, 0, Math.PI),
            4 => editor.AddArc(points[0], 5, 0, Math.PI),
            5 => editor.AddRectangle(bounds),
            6 => editor.AddImage(bounds, 1, 1, 4, [0, 0, 0, 255]),
            7 => editor.AddOleObject(bounds, [1, 2, 3]),
            8 => editor.AddPolygon(points),
            9 => editor.AddPolyline(points),
            10 => editor.AddSpline(points),
            11 => editor.AddText("Paper", points[0], 5),
            12 => editor.AddShapeText("Paper", points[0], 5),
            _ => editor.InsertBlockReference(definition, points[0], LayerId.Default)
        };
        editor.Undo();
        Assert.True(document.GetEntity(id).IsErased);
        editor.ActiveOwnerBlockId = BlockId.ModelSpace;
        editor.Redo();
        Assert.False(document.GetEntity(id).IsErased);
        Assert.Equal(2, publications);
    }

    [Fact]
    public void InvalidCreationOwnerDoesNotMutateDocumentOrHistory()
    {
        var editor = new CadEditor(CadDocument.Create("Invalid destination"))
        { ActiveOwnerBlockId = new BlockId(99999) };
        Assert.Throws<InvalidOperationException>(() => editor.AddCircle(CadPointD.Origin, 5));
        Assert.Empty(editor.Document.Entities);
        Assert.False(editor.DocumentCommands.CanUndo);
    }

    [Fact]
    public void LayerVersionIgnoresGeometryButTracksMembershipAndUndo()
    {
        var editor = new CadEditor(CadDocument.Create("Layer version"));
        var id = editor.AddLine(CadPointD.Origin, new CadPointD(10, 10));
        var version = editor.LayerChangeVersion;
        editor.MoveEntities([id], new CadVectorD(1, 1));
        Assert.Equal(version, editor.LayerChangeVersion);
        editor.Undo();
        Assert.Equal(version, editor.LayerChangeVersion);
        editor.DeleteEntities([id]);
        Assert.Equal(version + 1, editor.LayerChangeVersion);
        editor.Undo();
        Assert.Equal(version + 2, editor.LayerChangeVersion);
    }

    [Fact]
    public void DocumentChangeVersionTracksCommandsUndoRedoWithoutRendererAttachment()
    {
        var editor = new CadEditor(CadDocument.Create("Versions"));
        var observedVersions = new List<long>();
        editor.DocumentChanged += (_, _) => observedVersions.Add(editor.DocumentChangeVersion);
        editor.Execute(new AddLineCommand(CadPointD.Origin, new CadPointD(10, 0)));
        editor.Execute(new PanViewportCommand(new CadVectorD(10, 20)));
        editor.UndoEditor();
        Assert.Equal(1, editor.DocumentChangeVersion);
        editor.Undo();
        editor.Redo();
        Assert.Equal(new long[] { 1, 2, 3 }, observedVersions);
    }

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
    public void RetentionLimitKeepsTheNewestBatchAndRuntimeUndoMode()
    {
        var editor = new CadEditor(CadDocument.Create("Retention"));
        editor.AddLine(CadPointD.Origin, new(1, 1));
        editor.DocumentHistorySettings.MaximumUndoCommands = 1;
        editor.ExecuteRange(Enumerable.Range(0, 10).Select(i =>
            new AddLineCommand(new(i, 0), new(i, 10))));
        editor.DocumentHistorySettings.UndoMode = CadCommandBatchUndoMode.StepByStep;
        editor.Undo();
        Assert.Equal(10, editor.Document.Entities.Values.Count(e => !e.IsErased));
        editor.DocumentHistorySettings.UndoMode = CadCommandBatchUndoMode.Batch;
        editor.Undo();
        Assert.Single(editor.Document.Entities.Values, e => !e.IsErased);
        Assert.False(editor.DocumentCommands.CanUndo);
        editor.Redo();
        Assert.Equal(11, editor.Document.Entities.Values.Count(e => !e.IsErased));
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
    public void ImageRotationCommand_UpdatesSpatialIndexAndNotifiesResourcesThroughUndoRedo()
    {
        var document = CadDocument.Create("Image rotation");
        var image = document.AddImage(
            CadRectD.FromXYWH(-5, -1, 10, 2),
            1,
            1,
            4,
            [0x20, 0x80, 0xE0, 0xFF]);
        var editor = new CadEditor(document);
        var resources = new RecordingGeometryResourceManager();
        editor.RegisterGeometryResourceManager(resources, rebuildExistingResources: false);
        var horizontalOnlyArea = CadRectD.FromXYWH(3, -0.5, 2, 1);
        var verticalOnlyArea = CadRectD.FromXYWH(-0.5, 3, 1, 2);

        editor.Execute(new SetImageRotationCommand(image.Id, Math.PI / 2));

        Assert.DoesNotContain(
            image.Id,
            editor.SpatialIndex.Query(BlockId.ModelSpace, horizontalOnlyArea));
        Assert.Contains(
            image.Id,
            editor.SpatialIndex.Query(BlockId.ModelSpace, verticalOnlyArea));
        AssertSingleResourceChange(resources, image.Id, CadEntityChangeKind.Rotation);

        editor.UndoDocument();

        Assert.Contains(
            image.Id,
            editor.SpatialIndex.Query(BlockId.ModelSpace, horizontalOnlyArea));
        Assert.DoesNotContain(
            image.Id,
            editor.SpatialIndex.Query(BlockId.ModelSpace, verticalOnlyArea));
        AssertSingleResourceChange(resources, image.Id, CadEntityChangeKind.Rotation);

        editor.RedoDocument();

        Assert.DoesNotContain(
            image.Id,
            editor.SpatialIndex.Query(BlockId.ModelSpace, horizontalOnlyArea));
        Assert.Contains(
            image.Id,
            editor.SpatialIndex.Query(BlockId.ModelSpace, verticalOnlyArea));
        AssertSingleResourceChange(resources, image.Id, CadEntityChangeKind.Rotation);
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

    [Fact]
    public void ClickSelectionUsesBlockInsertionOrderWhenEntitiesMove()
    {
        var document = CadDocument.Create("Test");
        var first = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var second = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var otherBlockId = document.CreateBlockDefinition("Other", CadPointD.Origin);

        // Moving an existing entity appends it to the target block. Its global ID
        // remains smaller, so ID ordering would select the wrong entity here.
        document.MoveEntityToBlock(first.Id, otherBlockId);
        document.MoveEntityToBlock(first.Id, BlockId.ModelSpace);

        var editor = new CadEditor(document);
        var command = new ClickSelectCommand(new CadPointD(5, 0), tolerance: 0.1);

        editor.Execute(command);

        Assert.Equal(first.Id, command.SelectedEntityId);
    }

    private static void AssertSingleResourceChange(
        RecordingGeometryResourceManager resources,
        EntityId entityId,
        CadEntityChangeKind expectedKind)
    {
        var changes = Assert.IsType<CadDocumentChangeSet>(resources.LastChanges);
        var change = Assert.Single(changes.EntityChanges);
        Assert.Equal(entityId, change.EntityId);
        Assert.Equal(expectedKind, change.Kind);
    }

    private sealed class RecordingGeometryResourceManager : ICadGeometryResourceManager
    {
        public CadDocumentChangeSet? LastChanges { get; private set; }

        public void RebuildAll(CadDocument document)
        {
        }

        public void ApplyChanges(CadDocument document, CadDocumentChangeSet changes)
        {
            LastChanges = changes;
        }

        public void RebuildEntity(CadDocument document, EntityId entityId)
        {
        }

        public void RemoveEntity(EntityId entityId)
        {
        }
    }
}
