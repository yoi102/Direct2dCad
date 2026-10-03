using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.ViewModels.Services.Snapping;

namespace Direct2dCad.ViewModels.Services.Tests;

public sealed class ObjectSnapTests
{
    [Fact]
    public void RegionHoleVerticesAndCentersAreSnapCandidates()
    {
        var d = CadDocument.Create("region snap"); var a = d.AddCircle(default, 10); var b = d.AddCircle(new(2, 0), 3);
        d.AddRegion(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Difference)); a.Erase(); b.Erase();
        var e = new CadEditor(d); e.Viewport.SetView(10, default); var snap = new CadObjectSnapController();
        d.ViewSettings.Snap = d.ViewSettings.Snap with { Modes = CadObjectSnapModes.Endpoint };
        var end = snap.Resolve(d, e.ActiveOwnerBlockId, e.Viewport, e.SpatialIndex.Query, new(5.1, .1), null);
        Assert.Equal(5, end.X, 8); Assert.Equal(0, end.Y, 8);
        snap.Clear(); d.ViewSettings.Snap = d.ViewSettings.Snap with { Modes = CadObjectSnapModes.Center };
        var center = snap.Resolve(d, e.ActiveOwnerBlockId, e.Viewport, e.SpatialIndex.Query, new(2.1, .1), null);
        Assert.Equal(new CadPointD(2, 0), center);
    }
    [Theory][InlineData(1)][InlineData(10)][InlineData(.1)]
    public void ScreenToleranceAndHysteresisRemainStableAcrossZoom(double zoom)
    {
        var d=CadDocument.Create("snap");d.AddLine(default,new(1000/zoom,0));var editor=new CadEditor(d);
        editor.Viewport.SetView(zoom,default);var snap=new CadObjectSnapController();
        Assert.Equal(default,snap.Resolve(d,editor.ActiveOwnerBlockId,editor.Viewport,editor.SpatialIndex.Query,new(8/zoom,0),null));
        Assert.Equal(default,snap.Resolve(d,editor.ActiveOwnerBlockId,editor.Viewport,editor.SpatialIndex.Query,new(14/zoom,0),null));
        Assert.Equal(new CadPointD(16/zoom,0),snap.Resolve(d,editor.ActiveOwnerBlockId,editor.Viewport,editor.SpatialIndex.Query,new(16/zoom,0),null));
    }
    [Fact]public void AnalyticIntersectionTangentAndPerpendicularAreAvailable()
    {
        var d=CadDocument.Create("geometry");d.AddLine(new(-20,0),new(20,0));d.AddLine(new(3,-20),new(3,20));d.AddCircle(new(30,0),5);
        var e=new CadEditor(d);var snap=new CadObjectSnapController();
        d.ViewSettings.Snap=d.ViewSettings.Snap with { Modes=CadObjectSnapModes.Intersection };
        Assert.Equal(new CadPointD(3,0),snap.Resolve(d,e.ActiveOwnerBlockId,e.Viewport,e.SpatialIndex.Query,new(3.1,.2),null));
        d.ViewSettings.Snap=d.ViewSettings.Snap with { Modes=CadObjectSnapModes.Perpendicular };snap.Clear();
        Assert.Equal(new CadPointD(7,0),snap.Resolve(d,e.ActiveOwnerBlockId,e.Viewport,e.SpatialIndex.Query,new(7,.2),new(7,15)));
        d.ViewSettings.Snap=d.ViewSettings.Snap with { Modes=CadObjectSnapModes.Tangent };snap.Clear();
        var result=snap.Resolve(d,e.ActiveOwnerBlockId,e.Viewport,e.SpatialIndex.Query,new(32.5,4.33),new(40,0));
        Assert.Equal(5,result.DistanceTo(new(30,0)),7);Assert.Equal(0,(result-new CadPointD(30,0)).Dot(result-new CadPointD(40,0)),7);
    }
    [Fact]public void CandidatesCycleWithoutChangingTheDocument()
    {
        var d=CadDocument.Create("cycle");d.AddLine(default,new(100,0));d.AddLine(new(2,0),new(102,0));var e=new CadEditor(d);var snap=new CadObjectSnapController();
        snap.Resolve(d,e.ActiveOwnerBlockId,e.Viewport,e.SpatialIndex.Query,new(.2,0),null);
        var first=snap.Current;Assert.True(snap.Candidates.Count>1);snap.Cycle();Assert.NotEqual(first,snap.Current);
        var cycled=snap.Current!.Point;Assert.Equal(cycled,snap.Resolve(d,e.ActiveOwnerBlockId,e.Viewport,e.SpatialIndex.Query,new(.3,0),null));
        Assert.Equal(2,d.Entities.Count);
    }
    [Fact]public void HiddenErasedAndOtherOwnerEntitiesDoNotBecomeCandidates()
    {
        var d=CadDocument.Create("visibility");d.AddLine(default,new(100,0)).SetVisible(false);d.AddLine(new(1,0),new(101,0)).Erase();
        var definition=d.CreateBlockDefinition("other",default);var other=d.AddLine(new(2,0),new(102,0));d.MoveEntityToBlock(other.Id,definition);
        var e=new CadEditor(d);var snap=new CadObjectSnapController();var pointer=new CadPointD(.3,.4);
        Assert.Equal(pointer,snap.Resolve(d,e.ActiveOwnerBlockId,e.Viewport,e.SpatialIndex.Query,pointer,null));Assert.Null(snap.Current);
    }
    [Fact]public void NestedRotatedBlocksUseWorldCoordinates()
    {
        var d=CadDocument.Create("nested");var inner=d.CreateBlockDefinition("inner",default);var line=d.AddLine(new(5,0),new(100,0));d.MoveEntityToBlock(line.Id,inner);
        var outer=d.CreateBlockDefinition("outer",default);var nested=d.AddBlockReference(inner,new(10,0),rotationRadians:Math.PI/2);d.MoveEntityToBlock(nested.Id,outer);d.AddBlockReference(outer,new(20,0));d.RefreshBlockReferenceBounds();
        var e=new CadEditor(d);var snap=new CadObjectSnapController();
        var result=snap.Resolve(d,e.ActiveOwnerBlockId,e.Viewport,e.SpatialIndex.Query,new(30.2,5.1),null);
        Assert.Equal(30,result.X,8);Assert.Equal(5,result.Y,8);
    }
    [Fact]public void OrthoAndPolarApplyOnlyWhenNoObjectCandidateWins()
    {
        var d=CadDocument.Create("constraints");var e=new CadEditor(d);var snap=new CadObjectSnapController();
        d.ViewSettings.Snap=d.ViewSettings.Snap with { OrthoEnabled=true,ObjectsEnabled=false };
        Assert.Equal(new CadPointD(10,0),snap.Resolve(d,e.ActiveOwnerBlockId,e.Viewport,e.SpatialIndex.Query,new(10,3),default(CadPointD)));
        d.ViewSettings.Snap=d.ViewSettings.Snap with { OrthoEnabled=false,PolarEnabled=true };
        var point=snap.Resolve(d,e.ActiveOwnerBlockId,e.Viewport,e.SpatialIndex.Query,new(10,9),default(CadPointD));Assert.Equal(point.X,point.Y,8);
    }
}
