using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.HitTesting;

namespace Direct2dCad.Tests;

public sealed class BooleanRegionIndexTests
{
    [Theory][InlineData(CadBooleanOperation.Union)][InlineData(CadBooleanOperation.Intersection)][InlineData(CadBooleanOperation.Difference)]
    public void ResultsRemainIndexedSelectableAndHoleAwareThroughHistory(CadBooleanOperation operation)
    {
        var d = CadDocument.Create("boolean"); var fill = d.CreateSolidFillStyle("fill", CadColor.Green);
        var a = d.AddCircle(default, 10, fillStyleId: fill); var b = d.AddCircle(new(8, 0), 5);
        var editor = new CadEditor(d); var command = new BooleanRegionsCommand([a.Id, b.Id], operation, a.Id);
        editor.Execute(command); var result = Assert.IsType<CadRegion>(d.GetEntity(command.ResultEntityId!.Value));
        void Verify()
        {
            var indexed = editor.SpatialIndex.Query(BlockId.ModelSpace, CadRectD.FromLTRB(-100, -100, 100, 100));
            Assert.Contains(result.Id, indexed); Assert.DoesNotContain(a.Id, indexed); Assert.DoesNotContain(b.Id, indexed);
            foreach (var scale in new[] { .5, 1, 4 })
                foreach (var edge in result.Contours.SelectMany(c => c.Edges))
                    Assert.True(CadEntityHitTester.HitTestEdge(d, result, edge.At(.5), .1 / scale, out _));
        }
        Verify(); editor.Undo();
        Assert.Contains(a.Id, editor.SpatialIndex.Query(BlockId.ModelSpace, a.Bounds));
        Assert.DoesNotContain(result.Id, editor.SpatialIndex.Query(BlockId.ModelSpace, result.Bounds));
        editor.Redo(); Verify();
    }
    [Fact]
    public void HoleAndIslandSelectionUsesMaterialAndAllEdges()
    {
        var d = CadDocument.Create("hole"); var fill = d.CreateSolidFillStyle("fill", CadColor.Green);
        var a = d.AddCircle(default, 10); var b = d.AddCircle(default, 3);
        var result = d.AddRegion(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Difference), fillStyleId: fill);
        Assert.False(CadEntityHitTester.HitTestFill(d, result, default, out _));
        Assert.True(CadEntityHitTester.HitTestFill(d, result, new(5, 0), out _));
        Assert.True(CadEntityHitTester.HitTestEdge(d, result, new(3, 0), .01, out _));
        Assert.False(CadEntityHitTester.HitTestEdge(d, result, default, .01, out _));
    }
}
