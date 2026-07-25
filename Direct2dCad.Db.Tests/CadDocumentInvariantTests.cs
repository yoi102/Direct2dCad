using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Tests;

public sealed class CadDocumentInvariantTests
{
    [Fact]
    public void BlockAndLayoutNamesAreUniqueCaseInsensitively()
    {
        var document = CadDocument.Create("Test");
        document.CreateBlockDefinition("Valve", CadPointD.Origin);
        document.CreateLayout("Sheet A");

        Assert.Throws<InvalidOperationException>(() =>
            document.CreateBlockDefinition(" valve ", CadPointD.Origin));
        Assert.Throws<InvalidOperationException>(() =>
            document.CreateLayout(" sheet a "));
    }

    [Fact]
    public void BlockReferencesCannotCreateIndirectCycle()
    {
        var document = CadDocument.Create("Test");
        var firstBlockId = document.CreateBlockDefinition("First", CadPointD.Origin);
        var secondBlockId = document.CreateBlockDefinition("Second", CadPointD.Origin);
        document.AddBlockReference(
            firstBlockId,
            CadPointD.Origin,
            ownerBlockId: secondBlockId);

        Assert.Throws<InvalidOperationException>(() =>
            document.AddBlockReference(
                secondBlockId,
                CadPointD.Origin,
                ownerBlockId: firstBlockId));
    }

    [Fact]
    public void ReferencedBlockDefinitionCannotBeRemovedUntilReferenceIsErased()
    {
        var document = CadDocument.Create("Test");
        var blockId = document.CreateBlockDefinition("Valve", CadPointD.Origin);
        var reference = document.AddBlockReference(blockId, CadPointD.Origin);

        Assert.Throws<InvalidOperationException>(() =>
            document.RemoveBlockDefinition(blockId));

        reference.Erase();

        Assert.True(document.RemoveBlockDefinition(blockId));
    }

    [Fact]
    public void ChangeEntityLayerUpdatesBothLayerIndexes()
    {
        var document = CadDocument.Create("Test");
        var targetLayerId = document.CreateLayer("Target", CadColor.Green, CadLineWeight.Default);
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));

        document.ChangeEntityLayer(line.Id, targetLayerId);

        Assert.DoesNotContain(line.Id, document.GetEntityIdsOnLayer(LayerId.Default));
        Assert.Contains(line.Id, document.GetEntityIdsOnLayer(targetLayerId));
        Assert.Equal(targetLayerId, line.LayerId);
    }

    [Fact]
    public void ArcBoundsIncludeOnlyCardinalPointsInsideSweep()
    {
        var document = CadDocument.Create("Test");
        var counterClockwise = document.AddArcDegrees(
            new CadPointD(10, 20),
            radius: 5,
            startAngleDegrees: 45,
            sweepAngleDegrees: 180);
        var clockwise = document.AddArcDegrees(
            CadPointD.Origin,
            radius: 10,
            startAngleDegrees: 45,
            sweepAngleDegrees: -90);

        Assert.True(counterClockwise.Bounds.NearEquals(CadRectD.FromLTRB(
            5,
            20 - 5 / Math.Sqrt(2),
            10 + 5 / Math.Sqrt(2),
            25)));
        Assert.True(clockwise.Bounds.NearEquals(CadRectD.FromLTRB(
            10 / Math.Sqrt(2),
            -10 / Math.Sqrt(2),
            10,
            10 / Math.Sqrt(2))));
    }

    [Fact]
    public void RotatedTextBoundsAreRebuiltFromMeasuredLocalBounds()
    {
        var document = CadDocument.Create("Test");
        var text = document.AddText(
            "AB",
            new CadPointD(10, 20),
            height: 10,
            rotationRadians: Math.PI / 2);

        text.SetLocalBounds(CadRectD.FromLTRB(0, 0, 20, 10));

        Assert.True(text.Bounds.NearEquals(CadRectD.FromLTRB(0, 20, 10, 40)));
        Assert.False(text.RequiresBoundsMeasurement);
    }

    [Fact]
    public void PolylineLengthTracksPointReplacementAndClosedSegment()
    {
        var document = CadDocument.Create("Test");
        var polyline = document.AddPolyline(
            [new CadPointD(0, 0), new CadPointD(3, 0), new CadPointD(3, 4)],
            isClosed: false);

        Assert.Equal(7, polyline.Length);

        polyline.SetClosed(true);
        Assert.Equal(12, polyline.Length);

        polyline.ReplacePoints([new CadPointD(0, 0), new CadPointD(6, 8)]);
        Assert.Equal(20, polyline.Length);
        Assert.True(polyline.Bounds.NearEquals(CadRectD.FromLTRB(0, 0, 6, 8)));
    }
}
