using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.ViewModels.Services.Snapping;

namespace Direct2dCad.ViewModels.Services.Tests;

public sealed class EllipticalObjectSnapTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnreliableConicIntersectionsDoNotInterruptEndpointSnapping(bool nearlyCoincident)
    {
        var document = CadDocument.Create("unreliable intersection snap");
        var line = document.AddLine(default, new(20, 0));
        var first = document.AddEllipse(default, 10, nearlyCoincident ? 3 : 1e-9);
        var second = nearlyCoincident ? document.AddEllipse(new(1e-10, 0), 10, 3) : null;
        var left = Assert.Single(CadPlanarCurves.Get(first));
        var right = Assert.Single(CadPlanarCurves.Get((CadEntity?)second ?? line));
        // Establish that the fixture enters the strict solver's rejection path.
        Assert.Throws<InvalidOperationException>(() => CadPlanarGeometry.Intersections(left, right));

        var editor = new CadEditor(document);
        editor.Viewport.SetView(20, default);
        document.ViewSettings.Snap = document.ViewSettings.Snap with
            { Modes = CadObjectSnapModes.Endpoint | CadObjectSnapModes.Intersection };
        var snap = new CadObjectSnapController();
        var actual = snap.Resolve(document, editor.ActiveOwnerBlockId, editor.Viewport,
            editor.SpatialIndex.Query, new(.03, .02), null);

        Assert.Equal(CadPointD.Origin, actual);
        Assert.NotNull(snap.Current);
        Assert.Equal(CadObjectSnapModes.Endpoint, snap.Current.Kind);
        Assert.DoesNotContain(snap.Candidates, candidate => candidate.Kind == CadObjectSnapModes.Intersection);
    }

    [Theory]
    [InlineData(CadObjectSnapModes.Endpoint, false)]
    [InlineData(CadObjectSnapModes.Midpoint, false)]
    [InlineData(CadObjectSnapModes.Center, false)]
    [InlineData(CadObjectSnapModes.Nearest, false)]
    [InlineData(CadObjectSnapModes.Quadrant, false)]
    [InlineData(CadObjectSnapModes.Endpoint, true)]
    [InlineData(CadObjectSnapModes.Midpoint, true)]
    [InlineData(CadObjectSnapModes.Center, true)]
    [InlineData(CadObjectSnapModes.Nearest, true)]
    [InlineData(CadObjectSnapModes.Quadrant, true)]
    public void EllipticalRegionCandidatesUseTrueGeometryIncludingMirroredBlocks(CadObjectSnapModes mode, bool mirrored)
    {
        var document = CadDocument.Create("elliptical snap");
        var original = CadPlanarPrimitive.EllipseArc(new(7, -4), 10, 3, .6, .2, 5.4);
        var contour = new CadRegionContour([original, CadPlanarPrimitive.Line(original.End, original.Start)]);
        var region = document.AddRegion([contour]);
        var edge = original;
        if (mirrored)
        {
            var block = document.CreateBlockDefinition("mirror", default);
            document.MoveEntityToBlock(region.Id, block);
            document.AddBlockReference(block, new(20, 8), scaleX: -1, scaleY: 1);
            edge = contour.Transform(point => new(20 - point.X, 8 + point.Y), mirrored: true).Edges[0];
            document.RefreshBlockReferenceBounds();
        }
        var expected = mode switch
        {
            CadObjectSnapModes.Endpoint => edge.Start,
            CadObjectSnapModes.Midpoint => edge.At(.5),
            CadObjectSnapModes.Center => edge.Center,
            CadObjectSnapModes.Quadrant => edge.At(edge.ExtremaParameters().First()),
            _ => edge.At(.4)
        };
        var pointer = mode == CadObjectSnapModes.Nearest
            ? expected + edge.TangentAt(.4).Normalize().Perpendicular() * .05
            : expected + new CadVectorD(.03, -.02);
        var editor = new CadEditor(document);
        editor.Viewport.SetView(20, default);
        document.ViewSettings.Snap = document.ViewSettings.Snap with { Modes = mode };
        document.ViewSettings.Snap.Validate();
        var snap = new CadObjectSnapController();
        var actual = snap.Resolve(document, editor.ActiveOwnerBlockId, editor.Viewport, editor.SpatialIndex.Query, pointer, null);
        Assert.NotNull(snap.Current);
        Assert.Equal(mode, snap.Current.Kind);
        Assert.InRange(expected.DistanceTo(actual), 0, 1e-7);
    }

    [Theory]
    [InlineData(CadObjectSnapModes.Perpendicular)]
    [InlineData(CadObjectSnapModes.Tangent)]
    public void EllipseDoesNotOfferTheCircularTangentConstruction(CadObjectSnapModes mode)
    {
        var document = CadDocument.Create("ellipse unsupported snaps");
        document.AddEllipse(default, 10, 3);
        var editor = new CadEditor(document);
        document.ViewSettings.Snap = document.ViewSettings.Snap with { Modes = mode };
        var snap = new CadObjectSnapController();
        var pointer = new CadPointD(5, Math.Sqrt(75));
        snap.Resolve(document, editor.ActiveOwnerBlockId, editor.Viewport, editor.SpatialIndex.Query, pointer, new(20, 0));
        Assert.Empty(snap.Candidates);
    }
}
