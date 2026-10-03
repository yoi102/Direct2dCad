using System.Windows;
using System.Windows.Media;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.wpf.Services.Printing;
using Direct2dCad.wpf.Services.Printing.Vector;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class RegionPrintIntegrationTests
{
    [Fact][Trait("Category", "WindowsIntegration")]
    public async Task VectorPrintingKeepsCircularHoleAndDisconnectedIsland()
    {
        await CadPrintService.RunOnStaThreadAsync(() =>
        {
            var d = CadDocument.Create("print"); var a = d.AddCircle(default, 10); var b = d.AddCircle(default, 3); var c = d.AddCircle(new(30, 0), 2);
            var ring = d.AddRegion(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Difference));
            var r = d.AddRegion(CadRegionBoolean.Compute([ring, c], CadBooleanOperation.Union));
            var geometry = Assert.IsType<StreamGeometry>(CadVectorPrintGeometryFactory.Create(r));
            Assert.Equal(FillRule.EvenOdd, geometry.FillRule); Assert.False(geometry.FillContains(new Point(0, 0)));
            Assert.True(geometry.FillContains(new Point(5, 0))); Assert.True(geometry.FillContains(new Point(30, 0)));
            Assert.False(geometry.FillContains(new Point(20, 0)));
            // WPF converts circular arcs to cubic Beziers internally for area evaluation.
            Assert.InRange(Math.Abs(geometry.GetArea(.0001, ToleranceType.Absolute) - r.Area) / r.Area, 0, .001);
            return true;
        });
    }
}
