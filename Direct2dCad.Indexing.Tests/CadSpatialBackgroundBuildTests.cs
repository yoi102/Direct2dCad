using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Indexing.Tests;

public sealed class CadSpatialBackgroundBuildTests
{
    [Fact]
    public async Task BackgroundPublicationPreservesEditsAfterSnapshotIncludingDeleteAndReinsert()
    {
        var index = new CadSpatialIndex();
        var expected = new Dictionary<EntityId, CadRectD>();
        var all = CadRectD.FromXYWH(-1000, -1000, 20000, 20000);
        for (var i = 1; i <= 4000; i++)
        {
            var id = new EntityId(i);
            index.Add(id, expected[id] = CadRectD.FromXYWH(i, 0, 1, 1));
        }
        Assert.Equal(4000, index.CountIntersecting(BlockId.ModelSpace, all));

        for (var round = 0; round < 4; round++)
        {
            for (var i = 1; i <= 600; i++)
            {
                var id = new EntityId(i);
                index.Update(id, expected[id] = CadRectD.FromXYWH(i, round + 10, 2, 2));
            }
            // Query schedules a new immutable snapshot, but does not wait for it.
            Assert.Equal(expected.Count, index.CountIntersecting(BlockId.ModelSpace, all));
            var pending = index.PendingRebuilds;
            for (var i = 1; i <= 200; i++)
            {
                var id = new EntityId(i);
                index.Remove(id);
                expected.Remove(id);
                if (i % 2 == 0)
                    index.Add(id, expected[id] = CadRectD.FromXYWH(i + 5000, 80, 1, 1));
            }
            for (var i = 0; i < 50; i++)
            {
                var id = new EntityId(8000 + round * 50 + i);
                index.Add(id, expected[id] = CadRectD.FromXYWH(i + 500, 90, 3, 3));
            }
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
            foreach (var area in new[] { all, CadRectD.FromXYWH(0, 0, 1000, 50),
                         CadRectD.FromXYWH(5000, 70, 400, 40) })
            {
                var ids = expected.Where(pair => pair.Value.Intersects(area))
                    .Select(pair => pair.Key).OrderBy(id => id.Value).ToArray();
                Assert.Equal(ids.Length, index.CountIntersecting(BlockId.ModelSpace, area));
                Assert.Equal(ids, index.Query(BlockId.ModelSpace, area).OrderBy(id => id.Value));
            }
        }
        index.Clear();
    }

    [Fact]
    public async Task ClearAndOwnerRemovalCannotPublishAbandonedTrees()
    {
        var index = new CadSpatialIndex();
        var all = CadRectD.FromXYWH(-1, -1, 10000, 10000);
        for (var i = 1; i <= 4000; i++)
            index.Add(new EntityId(i), CadRectD.FromXYWH(i, 0, 1, 1));
        Assert.Equal(4000, index.CountIntersecting(BlockId.ModelSpace, all));
        for (var i = 1; i <= 600; i++)
            index.Update(new EntityId(i), CadRectD.FromXYWH(i, 10, 1, 1));
        index.CountIntersecting(BlockId.ModelSpace, all);
        var pending = index.PendingRebuilds;
        index.Clear();
        index.Add(new EntityId(1), CadRectD.FromXYWH(20, 20, 1, 1));
        try { await pending.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (OperationCanceledException) { }
        Assert.Equal(1, index.CountIntersecting(BlockId.ModelSpace, all));
        index.Update(new EntityId(1), BlockId.PaperSpace, CadRectD.FromXYWH(30, 30, 1, 1));
        Assert.Empty(index.Query(BlockId.ModelSpace, all));
        Assert.Equal(1, index.CountIntersecting(BlockId.PaperSpace, all));
    }

    [Fact]
    public void DeltaCountUsesOriginalBoundsAcrossRepeatedMovesDeletesAndUndo()
    {
        var index = new CadSpatialIndex();
        var original = CadRectD.FromXYWH(10, 10, 1, 1);
        var area = CadRectD.FromXYWH(0, 0, 100, 100);
        for (var i = 1; i <= 100; i++)
            index.Add(new EntityId(i), original);
        Assert.Equal(100, index.CountIntersecting(BlockId.ModelSpace, area));
        var changed = new EntityId(1);
        index.Update(changed, CadRectD.FromXYWH(200, 200, 1, 1));
        index.Update(changed, CadRectD.FromXYWH(300, 300, 1, 1));
        Assert.Equal(99, index.CountIntersecting(BlockId.ModelSpace, area));
        index.Remove(changed);
        Assert.Equal(99, index.CountIntersecting(BlockId.ModelSpace, area));
        index.Add(changed, original);
        Assert.Equal(100, index.CountIntersecting(BlockId.ModelSpace, area));
        index.Add(new EntityId(101), original);
        index.Remove(new EntityId(101));
        Assert.Equal(100, index.CountIntersecting(BlockId.ModelSpace, area));
    }
}
