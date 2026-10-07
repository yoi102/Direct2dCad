using System.Text.Json;
using Direct2dCad.Commands;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Editor.Tests;

public sealed class ScaleAndLayoutFailureTests
{
    [Theory]
    [InlineData("block-limit")]
    [InlineData("block-overflow")]
    [InlineData("circle-overflow")]
    [InlineData("point-overflow")]
    [InlineData("region-collapse")]
    [InlineData("dimension-collapse")]
    public void FailedScalePreservesEveryEntityIndexVersionAndHistory(string failure)
    {
        var doc = CadDocument.Create("Atomic scale");
        var line = doc.AddLine(new(100, 100), new(200, 100));
        var block = doc.CreateBlockDefinition("Part", default);
        var factor = failure.EndsWith("overflow") ? 1e100 : 1e-10;
        CadEntity invalid = failure switch
        {
            "block-limit" => doc.AddBlockReference(block, new(100, 100), scaleX: 1e-8, scaleY: 1e-8),
            "block-overflow" => doc.AddBlockReference(block, default, scaleX: 1e300),
            "circle-overflow" => doc.AddCircle(default, 1e300),
            "point-overflow" => doc.AddLine(new(1e300, 0), new(1e300, 1)),
            "region-collapse" => doc.AddRegion([new CadRegionContour([
                CadPlanarPrimitive.Line(new(0, 0), new(10, 0)),
                CadPlanarPrimitive.Line(new(10, 0), new(0, 10)),
                CadPlanarPrimitive.Line(new(0, 10), new(0, 0))])]),
            _ => doc.AddDimension(new(CadDimensionKind.Aligned, [new(new(0, 0)), new(new(1, 0))], new(0, 2), new()))
        };
        var editor = new CadEditor(doc);
        // Preserve a pre-existing redo branch as well as the saved history token.
        editor.AddLine(default, new(1, 0));
        editor.UndoDocument();
        var version = editor.DocumentChangeVersion;
        var history = editor.CreateDocumentHistorySnapshot();
        var before = State(doc);
        var index = editor.SpatialIndex.Query(line.OwnerBlockId, line.Bounds).ToArray();
        var publications = 0;
        editor.DocumentChanged += (_, _) => publications++;

        Assert.ThrowsAny<ArgumentException>(() => editor.Execute(new ScaleEntitiesCommand([line.Id, invalid.Id], default, factor)));

        Assert.Equal(before, State(doc));
        Assert.Equal(index, editor.SpatialIndex.Query(line.OwnerBlockId, line.Bounds));
        Assert.Equal(version, editor.DocumentChangeVersion);
        Assert.True(editor.DocumentHistoryEquals(history));
        Assert.False(editor.DocumentCommands.CanUndo);
        Assert.True(editor.DocumentCommands.CanRedo);
        Assert.Equal(0, publications);
        editor.RedoDocument();
        Assert.Equal(3, doc.Entities.Values.Count(e => !e.IsErased));
    }

    [Fact]
    public void ScaleUndoAndRedoRestoreExactGeometryAndMeasuredTextBounds()
    {
        var doc = CadDocument.Create("Exact undo");
        var line = doc.AddLine(new(0.1, 0.3), new(1.7, 4.1));
        var text = doc.AddText("Measured", new(2.1, 3.2), 5);
        text.SetLocalBounds(CadRectD.FromXYWH(-1, -2, 9, 6));
        var editor = new CadEditor(doc);
        var before = State(doc);
        var history = editor.CreateDocumentHistorySnapshot();
        editor.Execute(new ScaleEntitiesCommand([line.Id, text.Id], new(0.2, 0.7), 3.1));
        var scaled = State(doc);
        for (var i = 0; i < 5; i++)
        {
            editor.UndoDocument();
            Assert.Equal(before, State(doc));
            Assert.False(text.RequiresBoundsMeasurement);
            Assert.True(editor.DocumentHistoryEquals(history));
            editor.RedoDocument();
            Assert.Equal(scaled, State(doc));
            Assert.True(text.RequiresBoundsMeasurement);
        }
    }

    [Fact]
    public void DeleteLayoutUndoPreservesPreviouslyErasedEntitiesAndSavedHistory()
    {
        var doc = CadDocument.Create("Layout undo");
        var layoutId = doc.CreateLayout("Sheet");
        var owner = doc.GetLayout(layoutId).PaperSpaceBlockId;
        var erased = doc.AddLine(default, new(10, 0));
        var live = doc.AddCircle(new(30, 30), 5);
        doc.MoveEntityToBlock(erased.Id, owner);
        doc.MoveEntityToBlock(live.Id, owner);
        var editor = new CadEditor(doc);
        editor.Execute(new DeleteEntitiesCommand([erased.Id]));
        var saved = editor.CreateDocumentHistorySnapshot();
        editor.Execute(new DeleteLayoutCommand(layoutId));
        for (var i = 0; i < 3; i++)
        {
            Assert.True(erased.IsErased);
            Assert.True(live.IsErased);
            editor.UndoDocument();
            Assert.True(erased.IsErased);
            Assert.False(live.IsErased);
            Assert.True(editor.DocumentHistoryEquals(saved));
            Assert.DoesNotContain(erased.Id, editor.SpatialIndex.Query(owner, erased.Bounds));
            Assert.Contains(live.Id, editor.SpatialIndex.Query(owner, live.Bounds));
            editor.RedoDocument();
        }
    }

    [Fact]
    public void MixedScaleStagesEverySupportedGeometryAndPreservesEmbeddedContent()
    {
        var doc = CadDocument.Create("All scale geometry");
        doc.AddLine(new(1, 2), new(10, 3));
        doc.AddCircle(new(4, 5), 3);
        var ellipse = doc.AddEllipse(new(6, 4), 6, 2); ellipse.SetRotation(.4);
        var ellipseArc = doc.AddEllipseArc(new(8, 2), 5, 3, .2, 1.7); ellipseArc.SetRotation(.3);
        doc.AddArc(new(4, 2), 4, .2, 1.9);
        var rectangle = doc.AddRectangle(CadRectD.FromXYWH(2, 3, 8, 6), 2, 1); rectangle.SetRotation(.5);
        doc.AddPolyline([new(1, 2), new(5, 6), new(8, 4)]);
        doc.AddSpline([new(2, 1), new(5, 8), new(10, 4)]);
        doc.AddCompositePath(new(2, 3), [new CadCompositeLineSegment(new(5, 3)), new CadCompositeArcSegment(new(5, 5), 1.2)], false);
        doc.AddRegion([new CadRegionContour([CadPlanarPrimitive.Arc(new(6, 6), 3, 0, Math.PI * 2)])]);
        doc.AddText("Text", new(2, 5), 3, .2);
        doc.AddShapeText("Shape", new(3, 6), 4, .3);
        var image = doc.AddImage(CadRectD.FromXYWH(2, 4, 8, 6), 1, 1, 4, [1, 2, 3, 255]); image.SetRotation(.4);
        var ole = doc.AddOleObject(CadRectD.FromXYWH(3, 5, 6, 4), [1, 2, 3]);
        var block = doc.CreateBlockDefinition("Part", default);
        doc.AddBlockReference(block, new(4, 3), scaleX: -2, scaleY: 3);
        doc.AddDimension(new(CadDimensionKind.Aligned, [new(new(0, 0)), new(new(10, 0))], new(5, 4), new()));
        var editor = new CadEditor(doc);
        var before = State(doc);
        var bounds = doc.Entities.Values.ToDictionary(e => e.Id, e => e.Bounds);
        var pivot = new CadPointD(1, 1);
        const double factor = 2.3;
        editor.Execute(new ScaleEntitiesCommand(doc.Entities.Keys, pivot, factor));
        foreach (var entity in doc.Entities.Values)
        {
            if (entity is CadDimension dimension)
            {
                // Its measured label changes from 10 to 23, so its text bounds
                // need not be a scaled copy of the original label's bounds.
                Assert.True(new CadPointD(-1.3, -1.3).NearEquals(dimension.Definition.Anchors[0].Point));
                Assert.True(new CadPointD(21.7, -1.3).NearEquals(dimension.Definition.Anchors[1].Point));
                Assert.Equal(23, dimension.Measurement, 9);
                Assert.Equal(factor, dimension.Definition.AnnotationScale);
                continue;
            }
            var original = bounds[entity.Id];
            var expected = CadRectD.FromLTRB(1 + (original.MinX - 1) * factor, 1 + (original.MinY - 1) * factor,
                1 + (original.MaxX - 1) * factor, 1 + (original.MaxY - 1) * factor);
            Assert.True(expected.NearEquals(entity.Bounds, 1e-8), entity.GetType().Name);
        }
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, image.CopyPixels());
        Assert.Equal(new byte[] { 1, 2, 3 }, ole.CopyOleBytes());
        editor.UndoDocument();
        Assert.Equal(before, State(doc));
        editor.RedoDocument();
        Assert.Equal(16, doc.Entities.Count);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(20, 297)]
    [InlineData(420, 20)]
    [InlineData(double.PositiveInfinity, 297)]
    public void InvalidLayoutCreationLeavesNoBlocksHistoryOrVersion(double width, double height)
    {
        var doc = CadDocument.Create("Failed layout");
        var editor = new CadEditor(doc);
        var blocks = doc.Blocks.Keys.ToArray();
        var layouts = doc.Layouts.Keys.ToArray();
        for (var i = 0; i < 3; i++)
            Assert.ThrowsAny<ArgumentException>(() => editor.Execute(new CreateLayoutCommand("Invalid", width, height)));
        Assert.Equal(blocks, doc.Blocks.Keys);
        Assert.Equal(layouts, doc.Layouts.Keys);
        Assert.Equal(0, editor.DocumentChangeVersion);
        Assert.False(editor.DocumentCommands.CanUndo);
        editor.Execute(new CreateLayoutCommand("Valid"));
        Assert.Equal(layouts.Max(id => id.Value) + 1, doc.Layouts.Keys.Max(id => id.Value));
        Assert.Equal(blocks.Max(id => id.Value) + 1, doc.Blocks.Keys.Max(id => id.Value));
    }

    private static string State(CadDocument doc) => JsonSerializer.Serialize(doc.Entities.Values.Select(e => e switch
    {
        CadText v => (object)new { v.Position, v.Height, v.LocalBounds, v.RequiresBoundsMeasurement },
        _ => (object)e
    }));
}
