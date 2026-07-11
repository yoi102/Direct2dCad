using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.ViewModels.Services.Interactions;

public sealed record CadSelectionEntityTypeDescriptor(
    string Key,
    Type EntityType,
    string ResourceKey,
    string FallbackName);

public static class CadSelectionEntityTypeCatalog
{
    public static IReadOnlyList<CadSelectionEntityTypeDescriptor> All { get; } =
    [
        new("line", typeof(CadLine), "Line", "Line"),
        new("circle", typeof(CadCircle), "Circle", "Circle"),
        new("arc", typeof(CadArc), "Arc", "Arc"),
        new("ellipse", typeof(CadEllipse), "Ellipse", "Ellipse"),
        new("ellipseArc", typeof(CadEllipseArc), "EllipseArc", "Ellipse Arc"),
        new("rectangle", typeof(CadRectangle), "Rectangle", "Rectangle"),
        new("polyline", typeof(CadPolyline), "Polyline", "Polyline"),
        new("spline", typeof(CadSpline), "Spline", "Spline"),
        new("text", typeof(CadText), "Text", "Text"),
        new("shapeText", typeof(CadShapeText), "ShapeText", "Shape Text"),
        new("image", typeof(CadImage), "Image", "Image"),
        new("oleObject", typeof(CadOleObject), "OleObject", "OLE Object"),
        new("blockReference", typeof(CadBlockReference), "BlockReference", "Block Reference")
    ];

    private static readonly IReadOnlyDictionary<Type, string> KeysByType = All
        .ToDictionary(descriptor => descriptor.EntityType, descriptor => descriptor.Key);

    private static readonly IReadOnlyDictionary<string, Type> TypesByKey = All
        .ToDictionary(descriptor => descriptor.Key, descriptor => descriptor.EntityType, StringComparer.Ordinal);

    public static string? GetKey(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return KeysByType.GetValueOrDefault(entityType);
    }

    public static Type? GetEntityType(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        return TypesByKey.GetValueOrDefault(key.Trim());
    }
}
