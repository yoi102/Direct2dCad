using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

/// <summary>
/// Creates a shared fill style as part of the same undo batch that assigns it.
/// </summary>
public sealed class CreateFillStyleCommand : ICadCommand
{
    private readonly FillStyleKind _kind;
    private readonly string _name;
    private readonly CadColor _color;
    private readonly HatchPatternId _patternId;
    private readonly double _scale;
    private readonly double _angle;
    private readonly CadPointD _origin;
    private readonly bool _annotative;
    private readonly CadGradientKind _gradientKind;
    private readonly CadGradientStop[] _stops;
    private readonly bool _centered;
    private CadFillStyle? _createdStyle;

    public string Name => "Create Fill Style";
    public StyleId? CreatedStyleId => _createdStyle?.Id;

    private CreateFillStyleCommand(
        FillStyleKind kind,
        string name,
        CadColor color = default,
        HatchPatternId patternId = default,
        double scale = 1.0,
        double angle = 0.0,
        CadPointD origin = default,
        bool annotative = false,
        CadGradientKind gradientKind = CadGradientKind.Linear,
        IReadOnlyList<CadGradientStop>? stops = null,
        bool centered = true)
    {
        _kind = kind;
        _name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Style name cannot be empty.", nameof(name))
            : name.Trim();
        _color = color;
        _patternId = patternId;
        _scale = RequirePositiveFinite(scale, nameof(scale));
        _angle = double.IsFinite(angle)
            ? angle
            : throw new ArgumentOutOfRangeException(nameof(angle));
        _origin = origin;
        _annotative = annotative;
        _gradientKind = gradientKind;
        _stops = stops?.ToArray() ?? [];
        _centered = centered;

        if (_kind == FillStyleKind.Hatch && _patternId.Value <= 0)
            throw new ArgumentException("A hatch pattern is required.", nameof(patternId));
        if (_kind == FillStyleKind.Gradient && _stops.Length < 2)
            throw new ArgumentException("A gradient requires at least two stops.", nameof(stops));
    }

    public static CreateFillStyleCommand Solid(string name, CadColor color) =>
        new(FillStyleKind.Solid, name, color: color);

    public static CreateFillStyleCommand Hatch(
        string name,
        HatchPatternId patternId,
        CadColor foregroundColor,
        double scale = 1.0,
        double angle = 0.0,
        CadPointD? origin = null,
        bool annotative = false) =>
        new(
            FillStyleKind.Hatch,
            name,
            color: foregroundColor,
            patternId: patternId,
            scale: scale,
            angle: angle,
            origin: origin ?? CadPointD.Origin,
            annotative: annotative);

    public static CreateFillStyleCommand Gradient(
        string name,
        CadGradientKind kind,
        IReadOnlyList<CadGradientStop> stops,
        double angle = 0.0,
        double scale = 1.0,
        CadPointD? origin = null,
        bool centered = true) =>
        new(
            FillStyleKind.Gradient,
            name,
            gradientKind: kind,
            stops: stops,
            angle: angle,
            scale: scale,
            origin: origin ?? CadPointD.Origin,
            centered: centered);

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_createdStyle is null)
        {
            var styleId = _kind switch
            {
                FillStyleKind.Solid => document.CreateSolidFillStyle(_name, _color),
                FillStyleKind.Hatch => document.CreateHatchFillStyle(
                    _name,
                    _patternId,
                    _color,
                    _scale,
                    _angle,
                    _origin,
                    _annotative),
                FillStyleKind.Gradient => document.CreateGradientFillStyle(
                    _name,
                    _gradientKind,
                    _stops,
                    _angle,
                    _scale,
                    _origin,
                    _centered),
                _ => throw new ArgumentOutOfRangeException()
            };
            _createdStyle = (CadFillStyle)document.Styles[styleId];
        }
        else if (!document.TryGetStyle(_createdStyle.Id, out _))
        {
            document.AddStyleCore(_createdStyle);
        }

        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.Styles);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_createdStyle is null)
            return CadDocumentChangeSet.Empty;

        if (document.Styles.ContainsKey(_createdStyle.Id) &&
            !document.Entities.Values.Any(entity => HasFillStyle(entity, _createdStyle.Id)))
            document.RemoveStyleCore(_createdStyle.Id);

        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.Styles);
    }

    private static double RequirePositiveFinite(double value, string name) =>
        value > 0 && double.IsFinite(value)
            ? value
            : throw new ArgumentOutOfRangeException(name, "Value must be finite and greater than zero.");

    private static bool HasFillStyle(CadEntity entity, StyleId styleId) => entity switch
    {
        CadCircle circle => circle.FillStyleId == styleId,
        CadEllipse ellipse => ellipse.FillStyleId == styleId,
        CadRectangle rectangle => rectangle.FillStyleId == styleId,
        CadPolyline polyline => polyline.FillStyleId == styleId,
        CadSpline spline => spline.FillStyleId == styleId,
        CadCompositePath path => path.FillStyleId == styleId,
        _ => false
    };

    private enum FillStyleKind
    {
        Solid,
        Hatch,
        Gradient
    }
}
