using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Scene;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class Direct2DOwnerRenderPacketTests
{
    [Fact]
    public void IncrementalBoundsMatchFullUnionThroughShrinkVisibilityAndErase()
    {
        var document = CadDocument.Create("Bounds refit");
        var lines = Enumerable.Range(0, 513)
            .Select(i => document.AddLine(new CadPointD(i, i), new CadPointD(i + 1, i + 2))).ToArray();
        var packet = new Direct2DOwnerRenderPacket(document, BlockId.ModelSpace, lines, 0);
        var random = new Random(17);
        for (var step = 0; step < 1000; step++)
        {
            var line = lines[random.Next(lines.Length)];
            var coordinate = random.Next(-200, 200);
            line.SetGeometry(new CadPointD(coordinate, coordinate), new CadPointD(coordinate + 1, coordinate + 2));
            line.SetVisible(step % 3 != 0);
            if (step % 11 == 0)
                line.Erase();
            Assert.True(packet.TryUpdate(document, line.Id, step + 1));
            var expected = lines.Where(entity => entity.IsVisible && !entity.IsErased)
                .Aggregate(CadRectD.Empty, (bounds, entity) => bounds.Union(entity.Bounds));
            Assert.Equal(expected, packet.Bounds);
        }
        foreach (var line in lines)
        {
            line.SetVisible(false);
            packet.TryUpdate(document, line.Id, 1001);
        }
        Assert.True(packet.Bounds.IsEmpty);
        Assert.True(new Direct2DOwnerRenderPacket(document, BlockId.PaperSpace, [], 0).Bounds.IsEmpty);
    }

    [Fact]
    public void SingleEntryRefitHandlesFrozenLayerAndOwnerMismatch()
    {
        var document = CadDocument.Create("Single leaf");
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 10));
        var packet = new Direct2DOwnerRenderPacket(document, BlockId.ModelSpace, [line], 0);
        document.GetLayer(line.LayerId).SetFrozen(true);
        Assert.True(packet.TryUpdate(document, line.Id, 1));
        Assert.True(packet.Bounds.IsEmpty);
        document.GetLayer(line.LayerId).SetFrozen(false);
        Assert.True(packet.TryUpdate(document, line.Id, 2));
        Assert.Equal(line.Bounds, packet.Bounds);
        document.MoveEntityToBlock(line.Id, BlockId.PaperSpace);
        Assert.False(packet.TryUpdate(document, line.Id, 3));
    }
}
