using Direct2dCad.ChangeTracking;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Rendering;

namespace Direct2dCad.Editor.Tests;

public sealed class AtomicUpdateBatchTests
{
    [Fact]
    public void BatchPublishesOnceButSpatialQueriesSeeIntermediateChanges()
    {
        var editor = new CadEditor(CadDocument.Create("Batch updates"));
        var recorder = new ResourceRecorder();
        editor.RegisterGeometryResourceManager(recorder, false);
        var events = new List<CadDocumentChangeSet>();
        var activities = new List<CadCommandActivity>();
        editor.DocumentChanged += (_, changes) => events.Add(changes);
        editor.CommandActivity += (_, activity) => activities.Add(activity);
        var batch = Guid.NewGuid();
        var add = new AddLineCommand(new(0, 0), new(10, 10));
        editor.DocumentCommands.ExecuteAtomicBatch(batch, () =>
        {
            editor.ExecuteInBatch(add, batch);
            var id = add.CreatedEntityId!.Value;
            Assert.Contains(id, editor.SpatialIndex.Query(new(-1, -1, 11, 11)));
            for (var index = 0; index < 100; index++)
                editor.ExecuteInBatch(new SetEntityZIndexCommand([id], index), batch);
            Assert.Empty(events);
            Assert.Empty(recorder.Changes);
            return id;
        });
        Assert.Single(Assert.Single(events).EntityChanges);
        Assert.Single(recorder.Changes);
        Assert.Equal(101, Assert.Single(activities).CommandCount);

        editor.DocumentHistorySettings.UndoMode = CadCommandBatchUndoMode.StepByStep;
        editor.Undo();
        Assert.Equal(98, editor.Document.GetEntity(add.CreatedEntityId!.Value).ZIndex);
        editor.DocumentHistorySettings.RedoMode = CadCommandBatchUndoMode.StepByStep;
        editor.Redo();
        Assert.Equal(99, editor.Document.GetEntity(add.CreatedEntityId.Value).ZIndex);
        editor.DocumentHistorySettings.UndoMode = CadCommandBatchUndoMode.Batch;
        editor.Undo();
        Assert.True(editor.Document.GetEntity(add.CreatedEntityId.Value).IsErased);
    }

    [Fact]
    public void FailedDeletePublishesRestoredLifetimeAndLeavesHistoryIntact()
    {
        var editor = new CadEditor(CadDocument.Create("Rollback updates"));
        var id = editor.AddLine(new(0, 0), new(10, 10));
        var recorder = new ResourceRecorder();
        editor.RegisterGeometryResourceManager(recorder, false);
        var snapshot = editor.CreateDocumentHistorySnapshot();
        var batch = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => editor.DocumentCommands.ExecuteAtomicBatch<int>(batch, () =>
        {
            editor.ExecuteInBatch(new DeleteEntitiesCommand([id]), batch);
            Assert.Empty(editor.SpatialIndex.Query(new(-1, -1, 11, 11)));
            throw new ArgumentException("Later command failed");
        }));
        var change = Assert.Single(Assert.Single(recorder.Changes).EntityChanges);
        Assert.False(change.Kind.HasFlag(CadEntityChangeKind.Deleted));
        Assert.True(change.Kind.HasFlag(CadEntityChangeKind.Created));
        Assert.Contains(id, editor.SpatialIndex.Query(new(-1, -1, 11, 11)));
        Assert.True(editor.DocumentHistoryEquals(snapshot));
        editor.SetEntityZIndex(id, 4);
        Assert.Equal(2, recorder.Changes.Count);
    }

    [Fact]
    public void FailedCreationPublishesDeletedStateAndReadOnlyBatchPublishesNothing()
    {
        var editor = new CadEditor(CadDocument.Create("Canceled creation"));
        var recorder = new ResourceRecorder();
        editor.RegisterGeometryResourceManager(recorder, false);
        editor.DocumentCommands.ExecuteAtomicBatch(Guid.NewGuid(), () => 42);
        Assert.Empty(recorder.Changes);
        var batch = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => editor.DocumentCommands.ExecuteAtomicBatch<int>(batch, () =>
        {
            editor.ExecuteInBatch(new AddLineCommand(new(0, 0), new(10, 10)), batch);
            throw new ArgumentException();
        }));
        var change = Assert.Single(Assert.Single(recorder.Changes).EntityChanges);
        Assert.True(change.Kind.HasFlag(CadEntityChangeKind.Deleted));
        Assert.False(change.Kind.HasFlag(CadEntityChangeKind.Created));
        Assert.False(editor.DocumentCommands.CanUndo);
    }

    private sealed class ResourceRecorder : ICadGeometryResourceManager
    {
        public List<CadDocumentChangeSet> Changes { get; } = [];
        public void ApplyChanges(CadDocument document, CadDocumentChangeSet changes) => Changes.Add(changes);
        public void RebuildAll(CadDocument document) { }
        public void RebuildEntity(CadDocument document, EntityId entityId) { }
        public void RemoveEntity(EntityId entityId) { }
    }
}
