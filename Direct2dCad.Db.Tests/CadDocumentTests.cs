using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Tests;

public sealed class CadDocumentTests
{
    [Fact]
    public void CreateLayer_RejectsCaseInsensitiveDuplicateName()
    {
        var document = CadDocument.Create("Test");
        document.CreateLayer("Mechanical", CadColor.Green, CadLineWeight.Default);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            document.CreateLayer(" mechanical ", CadColor.Green, CadLineWeight.Default));

        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoveLayerAndDeleteEntities_AllowsDefaultLayerWhenAnotherLayerRemains()
    {
        var document = CadDocument.Create("Test");
        var remainingLayerId = document.CreateLayer("Remaining", CadColor.Green, CadLineWeight.Default);
        var line = document.AddLine(new CadPointD(0, 0), new CadPointD(10, 0));

        var removed = document.RemoveLayerAndDeleteEntities(LayerId.Default);

        Assert.True(removed);
        Assert.False(document.Layers.ContainsKey(LayerId.Default));
        Assert.True(document.Layers.ContainsKey(remainingLayerId));
        Assert.True(line.IsErased);
    }

    [Fact]
    public void RemoveLayerAndDeleteEntities_RejectsRemovingLastLayer()
    {
        var document = CadDocument.Create("Test");

        Assert.Throws<InvalidOperationException>(() =>
            document.RemoveLayerAndDeleteEntities(LayerId.Default));
    }

    [Fact]
    public void MoveEntityToBlock_UpdatesOwnerAndBlockMembership()
    {
        var document = CadDocument.Create("Test");
        var blockId = document.CreateBlockDefinition("Symbol", new CadPointD(1, 2));
        var line = document.AddLine(new CadPointD(0, 0), new CadPointD(10, 5));

        document.MoveEntityToBlock(line.Id, blockId);

        Assert.Equal(blockId, line.OwnerBlockId);
        Assert.Contains(line.Id, document.GetBlock(blockId).EntityIds);
        Assert.DoesNotContain(line.Id, document.GetBlock(BlockId.ModelSpace).EntityIds);
    }

    [Fact]
    public void RefreshAffectedBlockReferenceBounds_TracksChangedDefinitionGeometry()
    {
        var document = CadDocument.Create("Test");
        var blockId = document.CreateBlockDefinition("Symbol", CadPointD.Origin);
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 5));
        document.MoveEntityToBlock(line.Id, blockId);
        var reference = document.AddBlockReference(
            blockId,
            new CadPointD(100, 50),
            scaleX: 2,
            scaleY: 3);

        Assert.True(reference.Bounds.NearEquals(CadRectD.FromLTRB(100, 50, 120, 65)));

        line.SetGeometry(new CadPointD(-5, -2), new CadPointD(20, 8));
        var changedReferenceIds = document.RefreshAffectedBlockReferenceBounds([line.Id]);

        Assert.Contains(reference.Id, changedReferenceIds);
        Assert.True(reference.Bounds.NearEquals(CadRectD.FromLTRB(90, 44, 140, 74)));
    }
}
