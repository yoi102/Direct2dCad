using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Indexing.Tests;

public sealed class CadSpatialCountTests
{
    [Fact]
    public void CountMatchesQueryThroughPendingMovesDeletesAddsAndOwnerChanges()
    {
        var index = new CadSpatialIndex();
        for (var i = 1; i <= 512; i++)
            index.Add(new EntityId(i), BlockId.ModelSpace, CadRectD.FromXYWH(i, i % 8, 2, 2));
        var entire = CadRectD.FromXYWH(-100, -100, 1000, 1000);
        Assert.Equal(512, index.CountIntersecting(BlockId.ModelSpace, entire));
        var random = new Random(123);
        for (var step = 0; step < 120; step++)
        {
            var id = new EntityId(random.Next(1, 600));
            if (step % 3 == 0)
                index.Remove(id);
            else
                index.Update(id, step % 2 == 0 ? BlockId.ModelSpace : BlockId.PaperSpace,
                    CadRectD.FromXYWH(random.Next(600), random.Next(30), step % 5, step % 7));
            foreach (var owner in new[] { BlockId.ModelSpace, BlockId.PaperSpace, new BlockId(999) })
            foreach (var area in new[] { entire, CadRectD.FromXYWH(step, 0, 40, 10), CadRectD.Empty })
                Assert.Equal(index.Query(owner, area).Count, index.CountIntersecting(owner, area));
        }
    }

    [Fact]
    public void WarmCountQueryDoesNotAllocateResults()
    {
        var index = new CadSpatialIndex();
        for (var i = 1; i <= 2000; i++)
            index.Add(new EntityId(i), CadRectD.FromXYWH(i, 0, 1, 1));
        var area = CadRectD.FromXYWH(0, -1, 3000, 3);
        Assert.Equal(2000, index.CountIntersecting(BlockId.ModelSpace, area));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var total = 0;
        for (var i = 0; i < 100; i++)
            total += index.CountIntersecting(BlockId.ModelSpace, area);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(200000, total);
        Assert.Equal(0, allocated);
    }
}
