using Direct2dCad.Commands.Clipboard;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Tests;

public sealed class BooleanRegionCommandTests
{
    [Theory]
    [InlineData(CadBooleanOperation.Union, false)]
    [InlineData(CadBooleanOperation.Intersection, false)]
    [InlineData(CadBooleanOperation.Difference, false)]
    [InlineData(CadBooleanOperation.Union, true)]
    [InlineData(CadBooleanOperation.Intersection, true)]
    [InlineData(CadBooleanOperation.Difference, true)]
    public async Task EllipticalSubjectKeepsItsExplicitAppearanceThroughPreviewCommitUndoRedo(CadBooleanOperation operation, bool fullArc)
    {
        var document = CadDocument.Create("ellipse appearance");
        var stroke = document.CreateGraphicStyle("Subject red", CadColor.Red, new CadLineWeight(.4), LineTypeId.Continuous);
        var fill = document.CreateSolidFillStyle("Subject green", CadColor.Green);
        var otherStroke = document.CreateGraphicStyle("Cutter blue", CadColor.Blue, new CadLineWeight(.2), LineTypeId.Continuous);
        var otherFill = document.CreateSolidFillStyle("Cutter red", CadColor.Red);
        CadEntity subject;
        if (fullArc)
        {
            var ellipseArc = document.AddEllipseArc(default, 10, 5, .3, -Math.PI * 2, graphicStyleId: stroke, name: "Subject");
            ellipseArc.SetRotation(.6); subject = ellipseArc;
        }
        else
        {
            var ellipse = document.AddEllipse(default, 10, 5, graphicStyleId: stroke, fillStyleId: fill, name: "Subject");
            ellipse.SetRotation(.6); subject = ellipse;
        }
        subject.SetColorSource(CadColorSource.Explicit);
        subject.SetLineWeightState(new CadLineWeight(.7), false);
        subject.SetStrokeStyle(CadStrokeStyle.Default with { DashStyle = CadStrokeDashStyle.DashDot });
        subject.SetZIndex(17);
        var cutter = document.AddCircle(default, 2, graphicStyleId: otherStroke, fillStyleId: otherFill);
        var command = new BooleanRegionsCommand(operation == CadBooleanOperation.Difference ? [cutter.Id, subject.Id] : [subject.Id, cutter.Id], operation,
            operation == CadBooleanOperation.Difference ? subject.Id : null);
        await command.PrepareAsync(document);
        Assert.False(subject.IsErased); Assert.False(cutter.IsErased);
        command.Execute(document);
        var result = Assert.IsType<CadRegion>(document.GetEntity(command.ResultEntityId!.Value));
        void AssertAppearance()
        {
            Assert.Equal(stroke, result.GraphicStyleId);
            Assert.Equal(fullArc ? (StyleId?)null : fill, result.FillStyleId);
            Assert.Equal(CadColor.Red, Assert.IsType<CadGraphicStyle>(document.Styles[result.GraphicStyleId!.Value]).StrokeColor);
            Assert.Equal(CadColorSource.Explicit, result.ColorSource);
            Assert.Equal(new CadLineWeight(.7), result.LineWeight); Assert.False(result.UseLayerLineWeight);
            Assert.Equal(subject.StrokeStyle, result.StrokeStyle); Assert.Equal(17, result.ZIndex);
            Assert.Equal(subject.LayerId, result.LayerId); Assert.Equal(subject.OwnerBlockId, result.OwnerBlockId);
            Assert.Equal("Subject", result.Name); Assert.Equal(subject.IsVisible, result.IsVisible);
        }
        AssertAppearance(); Assert.True(subject.IsErased); Assert.True(cutter.IsErased);
        command.Undo(document);
        Assert.False(subject.IsErased); Assert.False(cutter.IsErased); Assert.True(result.IsErased);
        Assert.Equal(stroke, fullArc ? ((CadEllipseArc)subject).GraphicStyleId : ((CadEllipse)subject).GraphicStyleId);
        if (!fullArc) Assert.Equal(fill, ((CadEllipse)subject).FillStyleId);
        command.Execute(document);
        Assert.False(result.IsErased); Assert.True(subject.IsErased); Assert.True(cutter.IsErased);
        Assert.Equal(result.Id, command.ResultEntityId); AssertAppearance();
    }

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
