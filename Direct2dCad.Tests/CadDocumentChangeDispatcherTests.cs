using Direct2dCad.ChangeTracking;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Indexing;
using Direct2dCad.Rendering;

namespace Direct2dCad.Tests;

public sealed class CadDocumentChangeDispatcherTests
{
    [Theory]
    [InlineData(CadDocumentTableChangeKind.LayerMetadata)]
    [InlineData(CadDocumentTableChangeKind.LayerAppearance)]
    [InlineData(CadDocumentTableChangeKind.LayerAccess)]
    [InlineData(CadDocumentTableChangeKind.LayerOrder)]
    [InlineData(CadDocumentTableChangeKind.Styles)]
    [InlineData(CadDocumentTableChangeKind.BlockMetadata)]
    public void TableChangesSurviveCombinationDispatchAndDirtyDrain(CadDocumentTableChangeKind kind)
    {
        var document = CadDocument.Create("Tables");
        var dirty = new DirtySet();
        var dispatcher = new CadDocumentChangeDispatcher(document, dirty);
        var changes = CadDocumentChangeSet.Empty.WithTableChanges(kind);
        var combined = CadDocumentChangeSet.Combine([changes, CadDocumentChangeSet.Empty]);
        Assert.True(combined.DocumentChanged);
        Assert.False(combined.AffectsDocumentStructure);
        dispatcher.Publish(combined);
        var result = dirty.Drain();
        Assert.Equal(kind, result.TableChanges);
        Assert.False(dirty.HasChanges);
        Assert.Equal(kind, changes.WithLayoutsChanged().WithLayoutStructureChanged()
            .WithViewSettingsChanged().WithDocumentStructureChanged().TableChanges);
    }

    [Fact]
    public void MetadataLockAndLayerOrderDoNotRebuildSpatialGeometry()
    {
        var document = CadDocument.Create("Metadata");
        document.AddCircle(CadPointD.Origin, 5);
        var index = new RecordingSpatialIndex();
        var dispatcher = new CadDocumentChangeDispatcher(document, new DirtySet(), index);
        var commands = new CadDocumentCommandManager(document, dispatcher);
        var rebuilds = index.RebuildCount;
        commands.Execute(new RenameLayerCommand(LayerId.Default, "Renamed"));
        commands.Execute(new SetLayerDrawingPrioritiesCommand(new Dictionary<LayerId, int> { [LayerId.Default] = 10 }));
        commands.Execute(new SetEntityColorCommand(document.Entities.Keys, CadColor.Red));
        commands.Execute(new SetLayerStateCommand(LayerId.Default, true, true, false));
        for (var i = 0; i < 4; i++)
            commands.Undo();
        Assert.Empty(index.Updated);
        Assert.Equal(rebuilds, index.RebuildCount);
    }

    [Theory]
    [InlineData(CadEntityChangeKind.Geometry, CadEntityChangeKind.Geometry)]
    [InlineData(CadEntityChangeKind.Appearance, CadEntityChangeKind.Appearance)]
    [InlineData(CadEntityChangeKind.Visibility, CadEntityChangeKind.Geometry)]
    [InlineData(CadEntityChangeKind.Layer, CadEntityChangeKind.Appearance)]
    [InlineData(CadEntityChangeKind.DrawOrder, CadEntityChangeKind.Appearance)]
    [InlineData(CadEntityChangeKind.Fill, CadEntityChangeKind.Appearance | CadEntityChangeKind.Fill)]
    [InlineData(CadEntityChangeKind.EmbeddedData, CadEntityChangeKind.Geometry)]
    [InlineData(CadEntityChangeKind.Opacity, CadEntityChangeKind.Appearance)]
    [InlineData(CadEntityChangeKind.Rotation, CadEntityChangeKind.Geometry)]
    public void VisualBlockChildChange_AlsoInvalidatesItsReferences(
        CadEntityChangeKind changeKind, CadEntityChangeKind expectedKind)
    {
        var document = CadDocument.Create("Block invalidation");
        var child = document.AddLine(
            new CadPointD(0, 0),
            new CadPointD(20, 0));
        var definitionId = document.CreateBlockDefinition(
            "Visual changes",
            CadPointD.Origin);
        document.MoveEntityToBlock(child.Id, definitionId);
        var reference = document.AddBlockReference(
            definitionId,
            new CadPointD(500, 300));
        var dispatcher = new CadDocumentChangeDispatcher(
            document,
            new DirtySet());
        var resourceManager = new RecordingGeometryResourceManager();
        dispatcher.RegisterGeometryResourceManager(resourceManager, rebuildExistingResources: false);
        CadDocumentChangeSet? published = null;
        dispatcher.DocumentChanged += (_, changes) => published = changes;

        dispatcher.Publish(CadDocumentChangeSet.ForEntity(child.Id, changeKind));

        Assert.NotNull(published);
        var referenceChange = Assert.Single(
            published.EntityChanges,
            change => change.EntityId.Equals(reference.Id));
        Assert.Equal(expectedKind, referenceChange.Kind);
        Assert.Same(published, resourceManager.LastChanges);
        Assert.Contains(
            resourceManager.LastChanges!.EntityChanges,
            change => change.EntityId.Equals(reference.Id) &&
                      change.Kind == expectedKind);
    }

    [Fact]
    public void NestedAppearanceAndFill_PropagateWithoutUpdatingReferenceGeometry()
    {
        var document = CadDocument.Create("Nested blocks");
        var inner = document.CreateBlockDefinition("Inner", CadPointD.Origin);
        var outer = document.CreateBlockDefinition("Outer", CadPointD.Origin);
        var first = document.AddCircle(CadPointD.Origin, 10);
        var second = document.AddCircle(new(30, 0), 5);
        document.MoveEntityToBlock(first.Id, inner);
        document.MoveEntityToBlock(second.Id, inner);
        var nested = document.AddBlockReference(inner, CadPointD.Origin);
        document.MoveEntityToBlock(nested.Id, outer);
        var reference = document.AddBlockReference(outer, new(200, 100));
        var oldBounds = reference.Bounds;
        var index = new RecordingSpatialIndex();
        var dispatcher = new CadDocumentChangeDispatcher(document, new DirtySet(), index);
        CadDocumentChangeSet? result = null;
        dispatcher.DocumentChanged += (_, changes) => result = changes;

        dispatcher.Publish(new CadDocumentChangeSet([
            new(first.Id, CadEntityChangeKind.Appearance),
            new(second.Id, CadEntityChangeKind.Fill)]));

        Assert.NotNull(result);
        foreach (var id in new[] { nested.Id, reference.Id })
            Assert.Equal(CadEntityChangeKind.Appearance | CadEntityChangeKind.Fill,
                Assert.Single(result.EntityChanges, change => change.EntityId == id).Kind);
        Assert.Empty(index.Updated);
        Assert.Equal(oldBounds, reference.Bounds);

        first.SetGeometry(new(80, 0), 10);
        dispatcher.Publish(CadDocumentChangeSet.ForEntity(first.Id, CadEntityChangeKind.Geometry));
        Assert.Contains(nested.Id, index.Updated);
        Assert.Contains(reference.Id, index.Updated);
        Assert.NotEqual(oldBounds, reference.Bounds);
    }

    private sealed class RecordingSpatialIndex : ICadSpatialIndex
    {
        public int RebuildCount { get; private set; }
        public List<EntityId> Updated { get; } = [];
        public void Add(EntityId id, CadRectD bounds) { }
        public void Add(EntityId id, BlockId owner, CadRectD bounds) { }
        public void Remove(EntityId id) { }
        public void Update(EntityId id, CadRectD bounds) => Updated.Add(id);
        public void Update(EntityId id, BlockId owner, CadRectD bounds) => Updated.Add(id);
        public IReadOnlyList<EntityId> Query(CadRectD area) => [];
        public IReadOnlyList<EntityId> Query(BlockId owner, CadRectD area) => [];
        public void Query(BlockId owner, CadRectD area, List<EntityId> results) => results.Clear();
        public void Clear() { }
        public void Rebuild(CadDocument document) => RebuildCount++;
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
