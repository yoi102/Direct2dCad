using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Rendering.Direct2D.Scene;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class Direct2DBackgroundPreparationTests
{
    [Fact]
    public void RetargetedDefinitionDoesNotReuseOldTransitiveDependencies()
    {
        var document = CadDocument.Create("Retarget dependency");
        var a = new BlockId(20);
        var b = new BlockId(21);
        var root = new OwnerPreparationSnapshot(BlockId.ModelSpace, [Snapshot(document, a)]);
        var first = new OwnerPreparationSnapshot(a, [Snapshot(document)]);
        var second = new OwnerPreparationSnapshot(b, [Snapshot(document), Snapshot(document)]);
        using var service = new Direct2DBackgroundPreparationService();
        var before = Prepare(service, document, [root, first, second]);
        var changedRoot = root with { Entities = [root.Entities[0] with { DefinitionBlockId = b }] };
        var after = Prepare(service, document, [changedRoot, first, second], 2);
        var dependencies = after.Owners[BlockId.ModelSpace].DependencyEntityIds;
        Assert.NotSame(before.Owners[BlockId.ModelSpace].DependencyEntityIds, dependencies);
        Assert.DoesNotContain(first.Entities[0].Entity.Id, dependencies);
        Assert.All(second.Entities, item => Assert.Contains(item.Entity.Id, dependencies));
        Assert.Equal(3, after.Owners[BlockId.ModelSpace].EstimatedRenderWork);
    }

    [Fact]
    public void LocalOrderChangesPreserveOtherOwnerPacketsAndPreparedArrays()
    {
        var document = CadDocument.Create("Local order");
        var modelLine = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var paperLine = document.AddLine(CadPointD.Origin, new CadPointD(20, 0));
        document.MoveEntityToBlock(paperLine.Id, BlockId.PaperSpace);
        using var cache = new Direct2DEntityOrderCache();
        var paperPacket = cache.GetRenderPacket(document, BlockId.PaperSpace);
        cache.ScheduleBackgroundPreparation(document);
        var initialPaper = WaitForOwner(cache, document, BlockId.PaperSpace);
        foreach (var kind in new[] { CadEntityChangeKind.Created, CadEntityChangeKind.DrawOrder,
                     CadEntityChangeKind.Layer, CadEntityChangeKind.Deleted })
        {
            cache.ApplyChanges(document, CadDocumentChangeSet.ForEntity(modelLine.Id, kind));
            Assert.Same(paperPacket, cache.GetRenderPacket(document, BlockId.PaperSpace));
            cache.ScheduleBackgroundPreparation(document);
            Assert.Same(initialPaper.OrderedEntities,
                WaitForOwner(cache, document, BlockId.PaperSpace).OrderedEntities);
        }
    }

    [Fact]
    public void SnapshotPagesRemainImmutableAndOrderChangesResort()
    {
        var document = CadDocument.Create("Paged snapshots");
        var original = Enumerable.Range(0, 600).Select(_ => Snapshot(document)).ToArray();
        var pages = new EntityPreparationSnapshots(original);
        var replacement = original[256] with { Bounds = CadRectD.FromXYWH(100, 100, 1, 1) };
        var updated = pages.WithUpdates(new Dictionary<int, EntityPreparationSnapshot> { [256] = replacement });
        Assert.Equal(original[256], pages[256]);
        Assert.Equal(replacement, updated[256]);
        Assert.Equal(original[255], updated[255]);
        Assert.Equal(original[599], updated[599]);
        using var cache = new Direct2DEntityOrderCache();
        cache.ScheduleBackgroundPreparation(document);
        var first = WaitForOwner(cache, document, BlockId.ModelSpace);
        var entity = original[0].Entity;
        entity.SetZIndex(100);
        cache.ApplyChanges(document, CadDocumentChangeSet.ForEntity(entity.Id, CadEntityChangeKind.DrawOrder));
        cache.ScheduleBackgroundPreparation(document);
        var second = WaitForOwner(cache, document, BlockId.ModelSpace);
        Assert.NotSame(first.OrderedSourceIndices, second.OrderedSourceIndices);
        Assert.Equal(entity.Id, second.OrderedEntities[^1].Id);
    }

    [Fact]
    public void CyclicWorkRemainsPathDependentAndIndependentOfOwnerIterationOrder()
    {
        var document = CadDocument.Create("Cycle cost");
        var a = new BlockId(20);
        var b = new BlockId(21);
        OwnerPreparationSnapshot[] owners =
        [
            new(a, [Snapshot(document, b)]),
            new(b, [Snapshot(document, a), Snapshot(document)])
        ];
        using var service = new Direct2DBackgroundPreparationService();
        var first = Prepare(service, document, owners);
        var second = Prepare(service, document, owners.Reverse().ToArray(), 2);
        Assert.Equal(4, first.Owners[a].EstimatedRenderWork);
        Assert.Equal(4, first.Owners[b].EstimatedRenderWork);
        Assert.Equal(first.Owners[a].EstimatedRenderWork, second.Owners[a].EstimatedRenderWork);
        Assert.Equal(first.Owners[b].EstimatedRenderWork, second.Owners[b].EstimatedRenderWork);
    }

    [Fact]
    public void SharedNestedSubtreesStillCountAllInstances()
    {
        var document = CadDocument.Create("Shared work");
        var middle = new BlockId(20);
        var leaf = new BlockId(21);
        using var service = new Direct2DBackgroundPreparationService();
        var plan = Prepare(service, document,
        [
            new(BlockId.ModelSpace, Enumerable.Range(0, 50).Select(_ => Snapshot(document, middle)).ToArray()),
            new(middle, Enumerable.Range(0, 20).Select(_ => Snapshot(document, leaf)).ToArray()),
            new(leaf, Enumerable.Range(0, 10).Select(_ => Snapshot(document)).ToArray())
        ]);
        Assert.Equal(50 * (1 + 20 * 11), plan.Owners[BlockId.ModelSpace].EstimatedRenderWork);
        Assert.Equal(220, plan.Owners[middle].EstimatedRenderWork);
        Assert.Equal(10, plan.Owners[leaf].EstimatedRenderWork);
    }

    [Fact]
    public void OrderCacheRecapturesOnlyChangedOwnerAndRefreshesItsBounds()
    {
        var document = CadDocument.Create("Owner snapshots");
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var paperLine = document.AddLine(CadPointD.Origin, new CadPointD(20, 0));
        document.MoveEntityToBlock(paperLine.Id, BlockId.PaperSpace);
        using var cache = new Direct2DEntityOrderCache();
        cache.ScheduleBackgroundPreparation(document);
        var initialModel = WaitForOwner(cache, document, BlockId.ModelSpace);
        var initialPaper = WaitForOwner(cache, document, BlockId.PaperSpace);
        line.SetGeometry(new CadPointD(30, 0), new CadPointD(40, 0));
        cache.ApplyChanges(document, CadDocumentChangeSet.ForEntity(line.Id, CadEntityChangeKind.Geometry));
        cache.ScheduleBackgroundPreparation(document);
        var updatedModel = WaitForOwner(cache, document, BlockId.ModelSpace);
        var updatedPaper = WaitForOwner(cache, document, BlockId.PaperSpace);
        Assert.NotSame(initialModel.SourceSnapshot, updatedModel.SourceSnapshot);
        Assert.Same(initialModel.OrderedSourceIndices, updatedModel.OrderedSourceIndices);
        Assert.Same(initialModel.OrderedEntities, updatedModel.OrderedEntities);
        Assert.Same(initialModel.OwnedEntityIds, updatedModel.OwnedEntityIds);
        Assert.Same(initialModel.NestedDefinitionBlockIds, updatedModel.NestedDefinitionBlockIds);
        Assert.Same(initialModel.DependencyEntityIds, updatedModel.DependencyEntityIds);
        Assert.Same(initialPaper.DependencyEntityIds, updatedPaper.DependencyEntityIds);
        Assert.Equal(CadRectD.FromLTRB(0, 0, 10, 0), initialModel.SourceSnapshot!.Entities[0].Bounds);
        Assert.Same(initialPaper.SourceSnapshot, updatedPaper.SourceSnapshot);
        Assert.Same(initialPaper.OrderedEntities, updatedPaper.OrderedEntities);
        Assert.Equal(line.Bounds, updatedModel.Bounds);
    }

    private static PreparedOwnerPlan WaitForOwner(Direct2DEntityOrderCache cache, CadDocument document, BlockId id)
    {
        PreparedOwnerPlan? owner = null;
        Assert.True(SpinWait.SpinUntil(() => cache.TryGetPreparedOwner(document, id, out owner!),
            TimeSpan.FromSeconds(5)));
        return owner!;
    }

    [Fact]
    public void IncrementalPlanReusesUnchangedLocalArraysButRecalculatesNestedMetrics()
    {
        var document = CadDocument.Create("Incremental");
        var childId = new BlockId(20);
        var root = new OwnerPreparationSnapshot(BlockId.ModelSpace, [Snapshot(document, childId)]);
        var child = new OwnerPreparationSnapshot(childId, [Snapshot(document)]);
        using var service = new Direct2DBackgroundPreparationService();
        var first = Prepare(service, document, [root, child]);
        Assert.Equal(2, first.Owners[BlockId.ModelSpace].EstimatedRenderWork);

        service.Invalidate(preserveReusableOwners: true);
        var changedChild = new OwnerPreparationSnapshot(childId, [.. child.Entities, Snapshot(document)]);
        var second = Prepare(service, document, [root, changedChild], 2);

        Assert.Same(first.Owners[BlockId.ModelSpace].OrderedEntities,
            second.Owners[BlockId.ModelSpace].OrderedEntities);
        Assert.NotSame(first.Owners[childId].OrderedEntities, second.Owners[childId].OrderedEntities);
        Assert.Equal(3, second.Owners[BlockId.ModelSpace].EstimatedRenderWork);
        Assert.Equal(3, second.Owners[BlockId.ModelSpace].DependencyEntityIds.Count);
        Assert.Equal(2, first.Owners[BlockId.ModelSpace].EstimatedRenderWork);
        Assert.Null(service.TryGet(document, 1));
    }

    [Fact]
    public void FullInvalidationAndDocumentSwitchCannotReuseOldOwnerArrays()
    {
        var document = CadDocument.Create("First");
        var owner = new OwnerPreparationSnapshot(BlockId.ModelSpace, [Snapshot(document)]);
        using var service = new Direct2DBackgroundPreparationService();
        var first = Prepare(service, document, [owner]);
        service.Invalidate();
        var second = Prepare(service, document, [owner], 2);
        Assert.NotSame(first.Owners[BlockId.ModelSpace].OrderedEntities,
            second.Owners[BlockId.ModelSpace].OrderedEntities);
        service.Invalidate(preserveReusableOwners: true);
        var third = Prepare(service, CadDocument.Create("Second"), [owner], 3);
        Assert.NotSame(second.Owners[BlockId.ModelSpace].OrderedEntities,
            third.Owners[BlockId.ModelSpace].OrderedEntities);
    }

    [Fact]
    public void SupersededWorkNeverPublishesAnOlderVersion()
    {
        var document = CadDocument.Create("Superseded");
        var owner = new OwnerPreparationSnapshot(BlockId.ModelSpace,
            Enumerable.Range(0, 1000).Select(_ => Snapshot(document)).ToArray());
        using var service = new Direct2DBackgroundPreparationService();
        for (var version = 1; version < 20; version++)
        {
            service.Schedule(document, version, [owner]);
            service.Invalidate(preserveReusableOwners: true);
        }
        var latest = new OwnerPreparationSnapshot(BlockId.ModelSpace, [Snapshot(document)]);
        var plan = Prepare(service, document, [latest], 20);
        Assert.Single(plan.Owners[BlockId.ModelSpace].OrderedEntities);
        Assert.Null(service.TryGet(document, 19));
        Assert.False(service.NeedsSchedule(document, 20));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void RepeatedBlockReferences_ShareDependenciesButCountEveryInstance(int repetitions)
    {
        var document = CadDocument.Create("Repeated blocks");
        var definition = new BlockId(20);
        var children = Enumerable.Range(0, 64).Select(_ => Snapshot(document)).ToArray();
        var instances = Enumerable.Range(0, repetitions)
            .Select(_ => Snapshot(document, definition)).ToArray();
        using var service = new Direct2DBackgroundPreparationService();

        var plan = Prepare(service, document,
        [
            new(BlockId.ModelSpace, instances),
            new(definition, children)
        ]);

        var model = plan.Owners[BlockId.ModelSpace];
        Assert.Equal(repetitions * 65, model.EstimatedRenderWork);
        Assert.Equal(
            instances.Concat(children).Select(item => item.Entity.Id).OrderBy(id => id.Value),
            model.DependencyEntityIds.OrderBy(id => id.Value));
        Assert.Equal(children.Length, plan.Owners[definition].DependencyEntityIds.Count);
    }

    [Fact]
    public void SharedAndCyclicDefinitions_CollectEveryReachableEntityOnce()
    {
        var document = CadDocument.Create("Shared graph");
        var left = new BlockId(20);
        var right = new BlockId(21);
        var shared = new BlockId(22);
        var missing = new BlockId(23);
        EntityPreparationSnapshot[] rootItems = [Snapshot(document, left), Snapshot(document, right)];
        EntityPreparationSnapshot[] leftItems = [Snapshot(document, shared)];
        EntityPreparationSnapshot[] rightItems = [Snapshot(document, shared), Snapshot(document, missing)];
        EntityPreparationSnapshot[] sharedItems = [Snapshot(document, left), Snapshot(document)];
        using var service = new Direct2DBackgroundPreparationService();

        var plan = Prepare(service, document,
        [
            new(BlockId.ModelSpace, rootItems),
            new(left, leftItems),
            new(right, rightItems),
            new(shared, sharedItems)
        ]);

        Assert.Equal(
            rootItems.Concat(leftItems).Concat(rightItems).Concat(sharedItems)
                .Select(item => item.Entity.Id).OrderBy(id => id.Value),
            plan.Owners[BlockId.ModelSpace].DependencyEntityIds.OrderBy(id => id.Value));
        Assert.Equal(
            leftItems.Concat(sharedItems).Select(item => item.Entity.Id).OrderBy(id => id.Value),
            plan.Owners[left].DependencyEntityIds.OrderBy(id => id.Value));
    }

    private static PreparedDocumentPlan Prepare(
        Direct2DBackgroundPreparationService service,
        CadDocument document,
        IReadOnlyList<OwnerPreparationSnapshot> owners,
        long version = 1)
    {
        service.Schedule(document, version, owners);
        PreparedDocumentPlan? plan = null;
        Assert.True(SpinWait.SpinUntil(
            () => (plan = service.TryGet(document, version)) is not null,
            TimeSpan.FromSeconds(5)));
        return plan!;
    }

    private static EntityPreparationSnapshot Snapshot(CadDocument document, BlockId? definition = null)
    {
        var entity = document.AddLine(CadPointD.Origin, new CadPointD(1, 1));
        return new(entity, 0, 0, (int)entity.Id.Value, entity.Bounds, false, true, 1, definition);
    }
}
