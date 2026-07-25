using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Services.Geometry;

namespace Direct2dCad.ViewModels.Services.Tests;

public sealed class CadDrawingGeometryFactoryTests
{
    [Fact]
    public void CircleFromDiameterPointsCalculatesCenterAndRadius()
    {
        var result = CadDrawingGeometryFactory.TryCreateCircleFromDiameterPoints(
            new CadPointD(-5, 2),
            new CadPointD(15, 2),
            out var center,
            out var radius);

        Assert.True(result);
        Assert.Equal(new CadPointD(5, 2), center);
        Assert.Equal(10, radius);
    }

    [Fact]
    public void CircleFromThreePointsRejectsCollinearInput()
    {
        Assert.False(CadDrawingGeometryFactory.TryCreateCircleFromThreePoints(
            new CadPointD(0, 0),
            new CadPointD(5, 0),
            new CadPointD(10, 0),
            out _,
            out _));
    }

    [Fact]
    public void CircleFromThreePointsCalculatesCircumcircle()
    {
        var result = CadDrawingGeometryFactory.TryCreateCircleFromThreePoints(
            new CadPointD(5, 0),
            new CadPointD(0, 5),
            new CadPointD(-5, 0),
            out var center,
            out var radius);

        Assert.True(result);
        Assert.True(center.NearEquals(CadPointD.Origin));
        Assert.Equal(5, radius, 8);
    }

    [Fact]
    public void ArcFromThreePointsPassesThroughMiddlePoint()
    {
        var result = CadDrawingGeometryFactory.TryCreateArcFromThreePoints(
            new CadPointD(5, 0),
            new CadPointD(0, 5),
            new CadPointD(-5, 0),
            out var geometry);

        Assert.True(result);
        Assert.True(geometry.Center.NearEquals(CadPointD.Origin));
        Assert.Equal(5, geometry.Radius, 8);
        Assert.Equal(Math.PI, geometry.SweepAngleRadians, 8);
    }

    [Fact]
    public void EllipseFromAxisEndpointsCalculatesRadii()
    {
        var result = CadDrawingGeometryFactory.TryCreateEllipseFromAxisEnd(
            new CadPointD(-10, 0),
            new CadPointD(10, 0),
            new CadPointD(0, 4),
            out var geometry);

        Assert.True(result);
        Assert.Equal(CadPointD.Origin, geometry.Center);
        Assert.Equal(10, geometry.RadiusX);
        Assert.Equal(4, geometry.RadiusY);
    }

    [Fact]
    public void EllipseArcFromPointsCalculatesQuarterSweep()
    {
        var result = CadDrawingGeometryFactory.TryCreateEllipseArcFromPoints(
            new CadPointD(-10, 0),
            new CadPointD(10, 0),
            new CadPointD(0, 4),
            new CadPointD(10, 0),
            new CadPointD(0, 4),
            out var geometry);

        Assert.True(result);
        Assert.Equal(10, geometry.RadiusX);
        Assert.Equal(4, geometry.RadiusY);
        Assert.Equal(0, geometry.StartAngleRadians, 8);
        Assert.Equal(Math.PI / 2, geometry.SweepAngleRadians, 8);
    }
}
