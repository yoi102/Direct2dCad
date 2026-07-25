using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.HitTesting;

namespace Direct2dCad.HitTesting.Tests;

public sealed class CadHitTestServiceTests
{
    [Fact]
    public void HitTestEntityEdge_RejectsHiddenErasedAndFrozenEntities()
    {
        var document = CadDocument.Create("Test");
        var layerId = document.CreateLayer("Geometry", CadColor.Green, CadLineWeight.Default);
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0), layerId);
        var service = new CadHitTestService(document);
        var point = new CadPointD(5, 0);

        Assert.True(service.HitTestEntityEdge(line.Id, point, 0.1, out _));

        line.SetVisible(false);
        Assert.False(service.HitTestEntityEdge(line.Id, point, 0.1, out _));

        line.SetVisible(true);
        line.Erase();
        Assert.False(service.HitTestEntityEdge(line.Id, point, 0.1, out _));

        line.Restore();
        document.GetLayer(layerId).SetFrozen(true);
        Assert.False(service.HitTestEntityEdge(line.Id, point, 0.1, out _));
    }
}
