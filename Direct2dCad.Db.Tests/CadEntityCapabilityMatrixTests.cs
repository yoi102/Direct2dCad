using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Tests;

public sealed class CadEntityCapabilityMatrixTests
{
    [Theory]
    [MemberData(nameof(CapabilityCases))]
    public void EntityCapabilities_MatchSupportedEditorAndRendererFeatures(
        string entityKind,
        CadEntityCapability expected)
    {
        var document = CadDocument.Create("Capability matrix");
        var entity = AddEntity(document, entityKind);
        var actual = CadEntityCapabilities.GetCapabilities(entity);

        Assert.Equal(expected, actual);
        Assert.Equal(expected.HasFlag(CadEntityCapability.GraphicStyle), CadEntityCapabilities.SupportsGraphicStyle(entity));
        Assert.Equal(expected.HasFlag(CadEntityCapability.StrokeStyle), CadEntityCapabilities.SupportsStrokeStyle(entity));
        Assert.Equal(expected.HasFlag(CadEntityCapability.StartEndCaps), CadEntityCapabilities.SupportsStartEndCaps(entity));
        Assert.Equal(expected.HasFlag(CadEntityCapability.LineJoin), CadEntityCapabilities.SupportsLineJoin(entity));
        Assert.Equal(expected.HasFlag(CadEntityCapability.Fill), CadEntityCapabilities.SupportsFill(entity));
        Assert.Equal(expected.HasFlag(CadEntityCapability.Opacity), CadEntityCapabilities.SupportsOpacity(entity));
        Assert.Equal(expected.HasFlag(CadEntityCapability.Rotation), CadEntityCapabilities.SupportsRotation(entity));
        Assert.Equal(expected.HasFlag(CadEntityCapability.GripHandles), CadEntityCapabilities.SupportsGripHandles(entity));
        Assert.Equal(expected.HasFlag(CadEntityCapability.RotationHandle), CadEntityCapabilities.SupportsRotationHandle(entity));
        Assert.Equal(expected.HasFlag(CadEntityCapability.EmbeddedContent), CadEntityCapabilities.SupportsEmbeddedContent(entity));
    }

    public static TheoryData<string, CadEntityCapability> CapabilityCases => new()
    {
        { "Arc", GraphicStrokeCapsGrip },
        { "BlockReference", GraphicRotationGrip | CadEntityCapability.RotationHandle },
        { "Circle", GraphicStrokeGrip | CadEntityCapability.Fill },
        { "Ellipse", GraphicStrokeGrip | CadEntityCapability.Fill },
        { "EllipseArc", GraphicStrokeCapsGrip },
        { "Image", EmbeddedGrip | CadEntityCapability.Rotation | CadEntityCapability.RotationHandle },
        { "Line", GraphicStrokeCapsGrip },
        { "OleObject", EmbeddedGrip },
        { "Polyline", GraphicStrokeCapsGrip | CadEntityCapability.LineJoin },
        { "Rectangle", GraphicStrokeGrip | CadEntityCapability.Fill | CadEntityCapability.LineJoin },
        { "ShapeText", GraphicRotationGrip | CadEntityCapability.TextContent },
        { "Spline", GraphicStrokeCapsGrip | CadEntityCapability.LineJoin },
        { "Text", GraphicRotationGrip | CadEntityCapability.TextContent }
    };

    private const CadEntityCapability Grip = CadEntityCapability.GripHandles;
    private const CadEntityCapability GraphicStrokeGrip =
        CadEntityCapability.GraphicStyle | CadEntityCapability.StrokeStyle | Grip;
    private const CadEntityCapability GraphicStrokeCapsGrip =
        GraphicStrokeGrip | CadEntityCapability.StartEndCaps;
    private const CadEntityCapability GraphicRotationGrip =
        CadEntityCapability.GraphicStyle | CadEntityCapability.Rotation | Grip;
    private const CadEntityCapability EmbeddedGrip =
        CadEntityCapability.Opacity | CadEntityCapability.EmbeddedContent | Grip;

    private static CadEntity AddEntity(CadDocument document, string entityKind) => entityKind switch
    {
        "Arc" => document.AddArcDegrees(new CadPointD(0, 0), 5, 0, 180),
        "BlockReference" => AddBlockReference(document),
        "Circle" => document.AddCircle(new CadPointD(0, 0), 5),
        "Ellipse" => document.AddEllipse(new CadPointD(0, 0), 8, 4),
        "EllipseArc" => document.AddEllipseArc(new CadPointD(0, 0), 8, 4, 0, Math.PI),
        "Image" => document.AddImage(CadRectD.FromXYWH(0, 0, 8, 6), 1, 1, 4, [0, 0, 0, 255]),
        "Line" => document.AddLine(CadPointD.Origin, new CadPointD(8, 4)),
        "OleObject" => document.AddOleObject(CadRectD.FromXYWH(0, 0, 8, 6), [1]),
        "Polyline" => document.AddPolyline([CadPointD.Origin, new CadPointD(4, 3)]),
        "Rectangle" => document.AddRectangle(CadRectD.FromXYWH(0, 0, 8, 6)),
        "ShapeText" => document.AddShapeText("CAD", CadPointD.Origin, 4),
        "Spline" => document.AddSpline([CadPointD.Origin, new CadPointD(4, 3), new CadPointD(8, 0)]),
        "Text" => document.AddText("Text", CadPointD.Origin, 4),
        _ => throw new ArgumentOutOfRangeException(nameof(entityKind), entityKind, null)
    };

    private static CadBlockReference AddBlockReference(CadDocument document)
    {
        var blockId = document.CreateBlockDefinition("Block", CadPointD.Origin);
        var child = document.AddLine(CadPointD.Origin, new CadPointD(4, 3));
        document.MoveEntityToBlock(child.Id, blockId);
        return document.AddBlockReference(blockId, new CadPointD(10, 10));
    }
}
