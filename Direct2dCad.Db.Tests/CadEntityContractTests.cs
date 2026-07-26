using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Tests;

public sealed class CadEntityContractTests
{
    [Theory]
    [InlineData(CadEntityKind.Arc)]
    [InlineData(CadEntityKind.BlockReference)]
    [InlineData(CadEntityKind.Circle)]
    [InlineData(CadEntityKind.Ellipse)]
    [InlineData(CadEntityKind.EllipseArc)]
    [InlineData(CadEntityKind.Image)]
    [InlineData(CadEntityKind.Line)]
    [InlineData(CadEntityKind.OleObject)]
    [InlineData(CadEntityKind.Polyline)]
    [InlineData(CadEntityKind.Rectangle)]
    [InlineData(CadEntityKind.ShapeText)]
    [InlineData(CadEntityKind.Spline)]
    [InlineData(CadEntityKind.Text)]
    public void ConcreteEntity_FollowsDocumentOwnershipAndStateContract(CadEntityKind kind)
    {
        var document = CadDocument.Create("Entity contract");
        var targetLayerId = document.CreateLayer(
            "Target",
            CadColor.Blue,
            new CadLineWeight(0.5));
        var entity = AddEntity(document, kind);

        Assert.IsType(GetExpectedType(kind), entity);
        Assert.Same(entity, document.GetEntity(entity.Id));
        Assert.Equal(LayerId.Default, entity.LayerId);
        Assert.Equal(BlockId.ModelSpace, entity.OwnerBlockId);
        Assert.Contains(entity.Id, document.GetBlock(BlockId.ModelSpace).EntityIds);
        AssertFiniteNonEmptyBounds(entity.Bounds);

        entity.Rename("Renamed");
        entity.SetVisible(false);
        entity.SetLocked(true);
        entity.SetZIndex(17);
        document.ChangeEntityLayer(entity.Id, targetLayerId);

        Assert.Equal("Renamed", entity.Name);
        Assert.False(entity.IsVisible);
        Assert.True(entity.IsLocked);
        Assert.Equal(17, entity.ZIndex);
        Assert.Equal(targetLayerId, entity.LayerId);
        Assert.DoesNotContain(entity.Id, document.GetEntityIdsOnLayer(LayerId.Default));
        Assert.Contains(entity.Id, document.GetEntityIdsOnLayer(targetLayerId));

        entity.Erase();

        Assert.True(entity.IsErased);
        Assert.Same(entity, document.GetEntity(entity.Id));
        Assert.DoesNotContain(entity.Id, document.GetEntityIdsOnLayer(targetLayerId));

        entity.Restore();

        Assert.False(entity.IsErased);
        Assert.Contains(entity.Id, document.GetEntityIdsOnLayer(targetLayerId));
    }

    private static CadEntity AddEntity(CadDocument document, CadEntityKind kind)
    {
        return kind switch
        {
            CadEntityKind.Arc => document.AddArcDegrees(
                new CadPointD(2, 3), 5, 15, 200),
            CadEntityKind.BlockReference => AddBlockReference(document),
            CadEntityKind.Circle => document.AddCircle(new CadPointD(4, 5), 3),
            CadEntityKind.Ellipse => document.AddEllipse(new CadPointD(4, 5), 6, 3),
            CadEntityKind.EllipseArc => document.AddEllipseArc(
                new CadPointD(4, 5), 6, 3, 0.25, 2.5),
            CadEntityKind.Image => document.AddImage(
                CadRectD.FromXYWH(3, 4, 8, 6),
                2,
                2,
                8,
                CreatePixels(2, 2)),
            CadEntityKind.Line => document.AddLine(
                new CadPointD(1, 2), new CadPointD(8, 9)),
            CadEntityKind.OleObject => document.AddOleObject(
                CadRectD.FromXYWH(3, 4, 8, 6), [1, 2, 3, 4]),
            CadEntityKind.Polyline => document.AddPolyline(
                [new CadPointD(0, 0), new CadPointD(8, 1), new CadPointD(4, 7)],
                isClosed: true),
            CadEntityKind.Rectangle => document.AddRectangle(
                CadRectD.FromXYWH(3, 4, 8, 6), 1, 1),
            CadEntityKind.ShapeText => document.AddShapeText(
                "CAD", new CadPointD(2, 3), 5),
            CadEntityKind.Spline => document.AddSpline(
                [
                    new CadPointD(0, 0),
                    new CadPointD(4, 7),
                    new CadPointD(9, 2)
                ]),
            CadEntityKind.Text => document.AddText(
                "Text", new CadPointD(2, 3), 5),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static CadBlockReference AddBlockReference(CadDocument document)
    {
        var blockId = document.CreateBlockDefinition("Definition", CadPointD.Origin);
        var child = document.AddLine(CadPointD.Origin, new CadPointD(8, 6));
        document.MoveEntityToBlock(child.Id, blockId);
        return document.AddBlockReference(blockId, new CadPointD(20, 30));
    }

    private static Type GetExpectedType(CadEntityKind kind) => kind switch
    {
        CadEntityKind.Arc => typeof(CadArc),
        CadEntityKind.BlockReference => typeof(CadBlockReference),
        CadEntityKind.Circle => typeof(CadCircle),
        CadEntityKind.Ellipse => typeof(CadEllipse),
        CadEntityKind.EllipseArc => typeof(CadEllipseArc),
        CadEntityKind.Image => typeof(CadImage),
        CadEntityKind.Line => typeof(CadLine),
        CadEntityKind.OleObject => typeof(CadOleObject),
        CadEntityKind.Polyline => typeof(CadPolyline),
        CadEntityKind.Rectangle => typeof(CadRectangle),
        CadEntityKind.ShapeText => typeof(CadShapeText),
        CadEntityKind.Spline => typeof(CadSpline),
        CadEntityKind.Text => typeof(CadText),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static byte[] CreatePixels(int width, int height)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 0x20;
            pixels[index + 1] = 0x80;
            pixels[index + 2] = 0xE0;
            pixels[index + 3] = 0xFF;
        }

        return pixels;
    }

    private static void AssertFiniteNonEmptyBounds(CadRectD bounds)
    {
        Assert.False(bounds.IsEmpty);
        Assert.True(bounds.Width > 0);
        Assert.True(bounds.Height > 0);
        Assert.True(double.IsFinite(bounds.MinX));
        Assert.True(double.IsFinite(bounds.MinY));
        Assert.True(double.IsFinite(bounds.MaxX));
        Assert.True(double.IsFinite(bounds.MaxY));
    }

    public enum CadEntityKind
    {
        Arc,
        BlockReference,
        Circle,
        Ellipse,
        EllipseArc,
        Image,
        Line,
        OleObject,
        Polyline,
        Rectangle,
        ShapeText,
        Spline,
        Text
    }
}
