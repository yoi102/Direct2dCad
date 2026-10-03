using Direct2dCad.Commands.Clipboard;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Tests;

public sealed class BooleanRegionCommandTests
{
    [Fact]
    public async Task PreparedPreviewRejectsChangedGeometryAndCrossOwnerSources()
    {
        var d = CadDocument.Create("stale"); var a = d.AddCircle(default, 10); var b = d.AddCircle(default, 3);
        var command = new BooleanRegionsCommand([a.Id, b.Id], CadBooleanOperation.Difference, a.Id);
        await command.PrepareAsync(d); b.SetCenter(new(4, 0));
        Assert.Throws<InvalidOperationException>(() => command.Execute(d)); Assert.False(a.IsErased); Assert.Equal(2, d.Entities.Count);
        var block = d.CreateBlockDefinition("other", default); d.MoveEntityToBlock(b.Id, block);
        Assert.Throws<InvalidOperationException>(() => new BooleanRegionsCommand([a.Id, b.Id], CadBooleanOperation.Union).Execute(d));
        Assert.False(b.IsErased);
    }
    [Fact]
    public void AccurateLifetimeFlagsAndStableRedoWithSubjectAppearance()
    {
        var d = CadDocument.Create("boolean"); var a = d.AddCircle(default, 10); var b = d.AddCircle(default, 3);
        a.SetZIndex(7); a.SetStrokeStyle(CadStrokeStyle.Default with { DashStyle = CadStrokeDashStyle.Dash });
        var command = new BooleanRegionsCommand([b.Id, a.Id], CadBooleanOperation.Difference, a.Id);
        var changes = command.Execute(d); var r = Assert.IsType<CadRegion>(d.GetEntity(command.ResultEntityId!.Value));
        Assert.Equal(Math.PI * 91, r.Area, 7); Assert.Equal(a.StrokeStyle, r.StrokeStyle); Assert.Equal(7, r.ZIndex);
        Assert.Equal(CadEntityChangeKind.Created, changes.EntityChanges.Single(c => c.EntityId == r.Id).Kind);
        Assert.All(changes.EntityChanges.Where(c => c.EntityId != r.Id), c => Assert.Equal(CadEntityChangeKind.Deleted, c.Kind));
        var undo = command.Undo(d); Assert.False(a.IsErased); Assert.False(b.IsErased); Assert.True(r.IsErased);
        Assert.Equal(CadEntityChangeKind.Deleted, undo.EntityChanges.Single(c => c.EntityId == r.Id).Kind);
        command.Execute(d); Assert.False(r.IsErased); Assert.Equal(3, d.Entities.Count);
    }
    [Fact]
    public void EmptyAndLockedInputsNeverMutateSources()
    {
        var d = CadDocument.Create("empty"); var a = d.AddCircle(default, 3); var b = d.AddCircle(new(100, 0), 3);
        Assert.Throws<InvalidOperationException>(() => new BooleanRegionsCommand([a.Id, b.Id], CadBooleanOperation.Intersection).Execute(d));
        Assert.Equal(2, d.Entities.Count); Assert.False(a.IsErased); Assert.False(b.IsErased);
        b.SetLocked(true);
        Assert.Throws<InvalidOperationException>(() => new BooleanRegionsCommand([a.Id, b.Id], CadBooleanOperation.Union).Execute(d));
        Assert.Equal(2, d.Entities.Count); Assert.False(a.IsErased);
        Assert.Throws<ArgumentException>(() => new BooleanRegionsCommand([a.Id, b.Id], CadBooleanOperation.Difference));
    }
    [Fact]
    public void AllContoursSurviveTransformsAndCrossDocumentCopy()
    {
        var d = CadDocument.Create("copy"); var a = d.AddCircle(default, 10); var b = d.AddCircle(default, 3);
        var r = d.AddRegion(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Difference));
        ICadCommand[] transforms = [new MoveEntitiesCommand([r.Id], new(12, 5)), new RotateEntitiesCommand([r.Id], default, .7),
            new ScaleEntitiesCommand([r.Id], default, 2), new MirrorEntitiesCommand([r.Id], default, .3)];
        foreach (var command in transforms) { command.Execute(d); Assert.Equal(2, r.Contours.Count); command.Undo(d); Assert.Equal(Math.PI * 91, r.Area, 6); }
        var snapshot = CadClipboardSnapshotFactory.Create(d, [r.Id])!;
        var target = CadDocument.Create("paste"); var paste = new PasteEntitiesCommand(snapshot, new(20, 0)); paste.Execute(target);
        var copy = Assert.IsType<CadRegion>(target.GetEntity(Assert.Single(paste.CreatedEntityIds)));
        Assert.Equal(2, copy.Contours.Count); Assert.Equal(r.Area, copy.Area, 7); Assert.False(copy.Contains(new(20, 0))); Assert.True(copy.Contains(new(25, 0)));
        paste.Undo(target); Assert.True(copy.IsErased); paste.Execute(target); Assert.False(copy.IsErased);
        Assert.Throws<NotSupportedException>(() => CadCurveEditing.Break(r, new(10, 0)));
    }
}
