using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Tests;

public enum TestEntityKind
{
    Line, Circle, Ellipse, EllipseArc, Arc, Rectangle, Polyline, Polygon,
    Spline, Text, ShapeText, Image, Ole, Block, CompositePath
}

public static class CadEntityTestCases
{
    public static IEnumerable<object[]> All => Enum.GetValues<TestEntityKind>().Select(kind => new object[] { kind });
    public static IEnumerable<object[]> Stroked => All.Where(row => row[0] is not (TestEntityKind.Image or TestEntityKind.Ole));
    public static IEnumerable<object[]> Filled => new[] { TestEntityKind.Circle, TestEntityKind.Ellipse,
        TestEntityKind.Rectangle, TestEntityKind.Polygon, TestEntityKind.Spline }.Select(kind => new object[] { kind });

    internal static CadEntity Add(CadDocument document, TestEntityKind kind)
    {
        CadPointD[] points = [new(0, 0), new(40, 0), new(40, 30), new(0, 30)];
        return kind switch
        {
            TestEntityKind.Line => document.AddLine(points[0], points[2]),
            TestEntityKind.Circle => document.AddCircle(new(20, 15), 10),
            TestEntityKind.Ellipse => document.AddEllipse(new(20, 15), 20, 10),
            TestEntityKind.EllipseArc => document.AddEllipseArc(new(20, 15), 20, 10, 0, Math.PI),
            TestEntityKind.Arc => document.AddArc(new(20, 15), 10, 0, Math.PI),
            TestEntityKind.Rectangle => document.AddRectangle(CadRectD.FromLTRB(0, 0, 40, 30)),
            TestEntityKind.Polyline => document.AddPolyline(points),
            TestEntityKind.Polygon => document.AddPolyline(points, true),
            TestEntityKind.Spline => document.AddSpline(points),
            TestEntityKind.Text => document.AddText("CAD", new(0, 0), 10),
            TestEntityKind.ShapeText => document.AddShapeText("CAD", new(0, 0), 10),
            TestEntityKind.Image => document.AddImage(CadRectD.FromLTRB(0, 0, 40, 30), 1, 1, 4, [0, 0, 255, 255]),
            TestEntityKind.Ole => document.AddOleObject(CadRectD.FromLTRB(0, 0, 40, 30), [1, 2, 3]),
            TestEntityKind.Block => AddBlock(document),
            TestEntityKind.CompositePath => document.AddCompositePath(points[0], [new CadCompositeLineSegment(points[1]), new CadCompositeLineSegment(points[2])]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static CadBlockReference AddBlock(CadDocument document)
    {
        var block = document.CreateBlockDefinition("Fixture", CadPointD.Origin);
        var child = document.AddLine(CadPointD.Origin, new(40, 30));
        document.MoveEntityToBlock(child.Id, block);
        return document.AddBlockReference(block, new(20, 15));
    }
}
