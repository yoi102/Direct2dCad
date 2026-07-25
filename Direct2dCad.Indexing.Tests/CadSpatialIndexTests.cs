using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Indexing;

namespace Direct2dCad.Indexing.Tests;

public sealed class CadSpatialIndexTests
{
    [Fact]
    public void AddAndQuery_ReturnOnlyIntersectingEntries()
    {
        var index = new CadSpatialIndex();
        var first = new EntityId(101);
        var second = new EntityId(102);
        index.Add(first, CadRectD.FromLTRB(0, 0, 10, 10));
        index.Add(second, CadRectD.FromLTRB(100, 100, 110, 110));

        var result = index.Query(CadRectD.FromLTRB(5, 5, 20, 20));

        Assert.Equal([first], result);
    }

    [Fact]
    public void Update_RemovesOldLocationAndAddsNewLocation()
    {
        var index = new CadSpatialIndex();
        var entityId = new EntityId(101);
        var oldArea = CadRectD.FromLTRB(0, 0, 10, 10);
        var newArea = CadRectD.FromLTRB(100, 100, 110, 110);
        index.Add(entityId, oldArea);

        index.Update(entityId, newArea);

        Assert.DoesNotContain(entityId, index.Query(oldArea));
        Assert.Contains(entityId, index.Query(newArea));
    }

    [Fact]
    public void Update_WithNewOwnerRemovesEntryFromPreviousOwner()
    {
        var index = new CadSpatialIndex();
        var entityId = new EntityId(101);
        var ownerA = new BlockId(20);
        var ownerB = new BlockId(21);
        var bounds = CadRectD.FromLTRB(0, 0, 10, 10);
        index.Add(entityId, ownerA, bounds);

        index.Update(entityId, ownerB, bounds);

        Assert.Empty(index.Query(ownerA, bounds));
        Assert.Equal([entityId], index.Query(ownerB, bounds));
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void EmptyOrNonFiniteBoundsRemoveExistingEntry()
    {
        var index = new CadSpatialIndex();
        var entityId = new EntityId(101);
        index.Add(entityId, CadRectD.FromLTRB(0, 0, 10, 10));

        index.Update(entityId, CadRectD.Empty);

        Assert.Equal(0, index.Count);

        index.Add(entityId, new CadRectD(0, 0, double.PositiveInfinity, 10));

        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void Rebuild_SkipsHiddenAndErasedEntitiesAndSeparatesOwners()
    {
        var document = CadDocument.Create("Test");
        var modelLine = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var hiddenLine = document.AddLine(new CadPointD(0, 5), new CadPointD(10, 5));
        hiddenLine.SetVisible(false);
        var erasedCircle = document.AddCircle(new CadPointD(20, 20), 2);
        erasedCircle.Erase();
        var blockId = document.CreateBlockDefinition("Definition", CadPointD.Origin);
        var blockLine = document.AddLine(new CadPointD(30, 30), new CadPointD(40, 30));
        document.MoveEntityToBlock(blockLine.Id, blockId);
        var index = new CadSpatialIndex();

        index.Rebuild(document);

        Assert.Equal(2, index.Count);
        Assert.Equal([modelLine.Id], index.Query(
            BlockId.ModelSpace,
            CadRectD.FromLTRB(-1, -1, 11, 6)));
        Assert.Equal([blockLine.Id], index.Query(
            blockId,
            CadRectD.FromLTRB(29, 29, 41, 31)));
    }

    [Fact]
    public void QueriesMatchNaiveIntersectionForDeterministicData()
    {
        var random = new Random(1729);
        var index = new CadSpatialIndex();
        var entries = new Dictionary<EntityId, CadRectD>();
        for (var i = 1; i <= 200; i++)
        {
            var x = random.NextDouble() * 1000;
            var y = random.NextDouble() * 1000;
            var bounds = CadRectD.FromXYWH(
                x,
                y,
                1 + random.NextDouble() * 30,
                1 + random.NextDouble() * 30);
            var entityId = new EntityId(i);
            entries.Add(entityId, bounds);
            index.Add(entityId, bounds);
        }

        for (var queryIndex = 0; queryIndex < 40; queryIndex++)
        {
            var area = CadRectD.FromXYWH(
                random.NextDouble() * 900,
                random.NextDouble() * 900,
                100,
                100);
            var expected = entries
                .Where(x => x.Value.Intersects(area))
                .Select(x => x.Key)
                .OrderBy(x => x.Value);
            var actual = index.Query(area).OrderBy(x => x.Value);

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Update_AfterTreeWasQueriedDoesNotReturnStaleLocation()
    {
        var index = new CadSpatialIndex();
        var entityId = new EntityId(101);
        var oldBounds = CadRectD.FromLTRB(0, 0, 10, 10);
        var newBounds = CadRectD.FromLTRB(100, 100, 110, 110);
        index.Add(entityId, oldBounds);
        Assert.Contains(entityId, index.Query(oldBounds));

        index.Update(entityId, newBounds);

        Assert.DoesNotContain(entityId, index.Query(oldBounds));
        Assert.Contains(entityId, index.Query(newBounds));
    }

    [Fact]
    public void Remove_AfterTreeWasQueriedDoesNotReturnRemovedEntry()
    {
        var index = new CadSpatialIndex();
        var entityId = new EntityId(101);
        var bounds = CadRectD.FromLTRB(0, 0, 10, 10);
        index.Add(entityId, bounds);
        Assert.Contains(entityId, index.Query(bounds));

        index.Remove(entityId);

        Assert.Empty(index.Query(bounds));
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void IncrementalRandomOperationsMatchNaiveOwnerQueries()
    {
        var random = new Random(8675309);
        var index = new CadSpatialIndex();
        var owners = new[] { new BlockId(20), new BlockId(21), new BlockId(22) };
        var entries = new Dictionary<EntityId, (BlockId Owner, CadRectD Bounds)>();
        var nextEntityId = 1L;

        for (var operation = 0; operation < 600; operation++)
        {
            var action = entries.Count == 0 ? 0 : random.Next(3);
            if (action == 0)
            {
                var entityId = new EntityId(nextEntityId++);
                var owner = owners[random.Next(owners.Length)];
                var bounds = CreateRandomBounds(random);
                entries.Add(entityId, (owner, bounds));
                index.Add(entityId, owner, bounds);
            }
            else
            {
                var pair = entries.ElementAt(random.Next(entries.Count));
                if (action == 1)
                {
                    var owner = owners[random.Next(owners.Length)];
                    var bounds = CreateRandomBounds(random);
                    entries[pair.Key] = (owner, bounds);
                    index.Update(pair.Key, owner, bounds);
                }
                else
                {
                    entries.Remove(pair.Key);
                    index.Remove(pair.Key);
                }
            }

            if (operation % 7 != 0)
                continue;

            var queryOwner = owners[random.Next(owners.Length)];
            var area = CreateRandomBounds(random).Inflate(40);
            var expected = entries
                .Where(entry =>
                    entry.Value.Owner == queryOwner &&
                    entry.Value.Bounds.Intersects(area))
                .Select(entry => entry.Key)
                .OrderBy(id => id.Value);
            var actual = index.Query(queryOwner, area).OrderBy(id => id.Value);
            Assert.Equal(expected, actual);
        }

        Assert.Equal(entries.Count, index.Count);
    }

    private static CadRectD CreateRandomBounds(Random random)
    {
        var x = random.NextDouble() * 500;
        var y = random.NextDouble() * 500;
        return CadRectD.FromXYWH(
            x,
            y,
            1 + random.NextDouble() * 25,
            1 + random.NextDouble() * 25);
    }
}
