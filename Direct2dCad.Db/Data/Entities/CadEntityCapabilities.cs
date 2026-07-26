namespace Direct2dCad.Db.Data.Entities;

[Flags]
public enum CadEntityCapability
{
    None = 0,
    GraphicStyle = 1 << 0,
    StrokeStyle = 1 << 1,
    StartEndCaps = 1 << 2,
    LineJoin = 1 << 3,
    Fill = 1 << 4,
    Opacity = 1 << 5,
    Rotation = 1 << 6,
    GripHandles = 1 << 7,
    RotationHandle = 1 << 8,
    EmbeddedContent = 1 << 9,
    TextContent = 1 << 10
}

public static class CadEntityCapabilities
{
    public static CadEntityCapability GetCapabilities(CadEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var capabilities = CadEntityCapability.None;
        if (entity is CadLine or CadCircle or CadEllipse or CadEllipseArc or CadRectangle or CadArc or
            CadPolyline or CadSpline or CadText or CadShapeText or CadImage or CadOleObject or CadBlockReference)
        {
            capabilities |= CadEntityCapability.GripHandles;
        }
        if (entity is CadLine or CadCircle or CadEllipse or CadEllipseArc or CadRectangle or CadArc or
            CadPolyline or CadSpline or CadText or CadShapeText or CadBlockReference)
        {
            capabilities |= CadEntityCapability.GraphicStyle;
        }

        if (entity is CadLine or CadCircle or CadEllipse or CadEllipseArc or CadRectangle or CadArc or
            CadPolyline or CadSpline)
        {
            capabilities |= CadEntityCapability.StrokeStyle;
        }

        if (entity is CadLine or CadEllipseArc ||
            entity is CadArc { IsFullCircle: false } ||
            entity is CadPolyline { Closed: false } ||
            entity is CadSpline { Closed: false })
        {
            capabilities |= CadEntityCapability.StartEndCaps;
        }

        if (entity is CadRectangle or CadPolyline or CadSpline)
            capabilities |= CadEntityCapability.LineJoin;

        if (entity is CadCircle or CadEllipse or CadRectangle ||
            entity is CadPolyline { Closed: true } ||
            entity is CadSpline { Closed: true })
        {
            capabilities |= CadEntityCapability.Fill;
        }

        if (entity is CadImage or CadOleObject)
            capabilities |= CadEntityCapability.Opacity | CadEntityCapability.EmbeddedContent;

        if (entity is CadText or CadShapeText or CadImage or CadBlockReference)
            capabilities |= CadEntityCapability.Rotation;

        if (entity is CadImage or CadBlockReference)
            capabilities |= CadEntityCapability.RotationHandle;

        if (entity is CadText or CadShapeText)
            capabilities |= CadEntityCapability.TextContent;

        return capabilities;
    }

    public static bool Supports(CadEntity entity, CadEntityCapability capability) =>
        (GetCapabilities(entity) & capability) == capability;

    public static bool SupportsGraphicStyle(CadEntity entity) =>
        Supports(entity, CadEntityCapability.GraphicStyle);

    public static bool SupportsStrokeStyle(CadEntity entity) =>
        Supports(entity, CadEntityCapability.StrokeStyle);

    public static bool SupportsStartEndCaps(CadEntity entity) =>
        Supports(entity, CadEntityCapability.StartEndCaps);

    public static bool SupportsLineJoin(CadEntity entity) =>
        Supports(entity, CadEntityCapability.LineJoin);

    public static bool SupportsFill(CadEntity entity) =>
        Supports(entity, CadEntityCapability.Fill);

    public static bool SupportsOpacity(CadEntity entity) =>
        Supports(entity, CadEntityCapability.Opacity);

    public static bool SupportsRotation(CadEntity entity) =>
        Supports(entity, CadEntityCapability.Rotation);

    public static bool SupportsGripHandles(CadEntity entity) =>
        Supports(entity, CadEntityCapability.GripHandles);

    public static bool SupportsRotationHandle(CadEntity entity) =>
        Supports(entity, CadEntityCapability.RotationHandle);

    public static bool SupportsEmbeddedContent(CadEntity entity) =>
        Supports(entity, CadEntityCapability.EmbeddedContent);
}
