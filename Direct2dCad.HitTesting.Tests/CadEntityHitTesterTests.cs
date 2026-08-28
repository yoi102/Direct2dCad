using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.HitTesting;

namespace Direct2dCad.HitTesting.Tests;

public sealed class CadEntityHitTesterTests
{
    [Fact]
    public void HitTestEdge_IncludesExplicitLineWeightPadding()
    {
        var document = CadDocument.Create("Test");
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        line.SetLineWeight(new CadLineWeight(4));
        var options = new CadHitTestOptions
        {
            KeepStrokeWidthScreenConstant = false,
            MinimumScreenStrokeWidth = 0
        };

        var hit = CadEntityHitTester.HitTestEdge(
            document,
            line,
            new CadPointD(5, 1.75),
            tolerance: 0,
            options,
            out var result);

        Assert.True(hit);
        Assert.Equal(line.Id, result.LeafEntityId);
        Assert.Equal(1.75, result.Distance, 6);
    }

    [Fact]
    public void HitTestEdge_ConvertsMillimeterLineWeightToScreenUnits()
    {
        var document = CadDocument.Create("Test");
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        line.SetLineWeight(new CadLineWeight(0.25));
        var options = new CadHitTestOptions(viewportZoom: 2.0)
        {
            KeepStrokeWidthScreenConstant = true,
            MinimumScreenStrokeWidth = 0
        };
        var halfWorldWidth =
            0.25 * CadHitTestOptions.DefaultScreenUnitsPerMillimeter / 2.0 / 2.0;

        Assert.True(CadEntityHitTester.HitTestEdge(
            document,
            line,
            new CadPointD(5, halfWorldWidth - 1e-6),
            tolerance: 0,
            options,
            out _));
        Assert.False(CadEntityHitTester.HitTestEdge(
            document,
            line,
            new CadPointD(5, halfWorldWidth + 1e-6),
            tolerance: 0,
            options,
            out _));
    }

    [Fact]
    public void HitTestFill_CircleRequiresFillStyle()
    {
        var document = CadDocument.Create("Test");
        var unfilled = document.AddCircle(CadPointD.Origin, 10);
        var fillStyleId = document.CreateSolidFillStyle("Solid", CadColor.Green);
        var filled = document.AddCircle(new CadPointD(30, 0), 10, fillStyleId: fillStyleId);

        Assert.False(CadEntityHitTester.HitTestFill(
            document,
            unfilled,
            CadPointD.Origin,
            out _));
        Assert.True(CadEntityHitTester.HitTestFill(
            document,
            filled,
            new CadPointD(30, 0),
            out var result));
        Assert.Equal(filled.Id, result.LeafEntityId);
    }

    [Fact]
    public void HitTestFill_TextUsesTextBoundsInsteadOfInvertedBackgroundMargin()
    {
        var document = CadDocument.Create("Test");
        var text = document.AddText(
            "ABC",
            new CadPointD(10, 20),
            height: 10,
            isInverted: true,
            invertedMarginFactor: 0.5);
        Assert.True(text.SetLocalBounds(CadRectD.FromLTRB(0, 0, 20, 10)));

        Assert.True(CadEntityHitTester.HitTestFill(
            document,
            text,
            new CadPointD(15, 25),
            out _));
        Assert.False(CadEntityHitTester.HitTestFill(
            document,
            text,
            new CadPointD(7, 25),
            out _));
        Assert.True(text.InvertedBackgroundBounds.Contains(new CadPointD(7, 25)));
    }

    [Fact]
    public void HitTestEdge_NestedBlockReturnsFullEntityPath()
    {
        var document = CadDocument.Create("Test");
        var innerBlockId = document.CreateBlockDefinition("Inner", CadPointD.Origin);
        var leaf = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        document.MoveEntityToBlock(leaf.Id, innerBlockId);

        var outerBlockId = document.CreateBlockDefinition("Outer", CadPointD.Origin);
        var nestedReference = document.AddBlockReference(
            innerBlockId,
            new CadPointD(5, 0),
            ownerBlockId: outerBlockId);
        var outerReference = document.AddBlockReference(
            outerBlockId,
            new CadPointD(100, 0),
            scaleX: 2,
            scaleY: 2);

        var hit = CadEntityHitTester.HitTestEdge(
            document,
            outerReference,
            new CadPointD(120, 0),
            tolerance: 0.1,
            out var result);

        Assert.True(hit);
        Assert.Equal([outerReference.Id, nestedReference.Id, leaf.Id], result.EntityPath);
        Assert.Equal(outerReference.Id, result.TopEntityId);
        Assert.Equal(leaf.Id, result.LeafEntityId);
    }

    [Fact]
    public void HitTestEdge_NestedBlockUsesLayerPriorityAndZIndexBeforeInsertionOrder()
    {
        var document = CadDocument.Create("Test");
        var lowLayerId = document.CreateLayer("Low", CadColor.Green, CadLineWeight.Default);
        var highLayerId = document.CreateLayer("High", CadColor.Green, CadLineWeight.Default);
        document.DocumentSettings.LayerDrawingPriority.SetPriority(lowLayerId, 1);
        document.DocumentSettings.LayerDrawingPriority.SetPriority(highLayerId, 10);

        var blockId = document.CreateBlockDefinition("Definition", CadPointD.Origin);
        var high = document.AddLine(
            CadPointD.Origin,
            new CadPointD(10, 0),
            highLayerId);
        document.MoveEntityToBlock(high.Id, blockId);
        var low = document.AddLine(
            CadPointD.Origin,
            new CadPointD(10, 0),
            lowLayerId);
        document.MoveEntityToBlock(low.Id, blockId);
        var reference = document.AddBlockReference(blockId, CadPointD.Origin);

        Assert.True(CadEntityHitTester.HitTestEdge(
            document,
            reference,
            new CadPointD(5, 0),
            tolerance: 0.1,
            out var result));
        Assert.Equal([reference.Id, high.Id], result.EntityPath);
        Assert.NotEqual(low.Id, result.LeafEntityId);
    }
}
