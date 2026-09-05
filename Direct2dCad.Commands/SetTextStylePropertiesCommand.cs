using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;

namespace Direct2dCad.Commands;

/// <summary>
/// Changes a shared text style as one undoable operation. All referencing CadText
/// entities are reported as changed because their measured bounds can change.
/// </summary>
public sealed class SetTextStylePropertiesCommand : ICadCommand
{
    private readonly StyleId _styleId;
    private readonly string? _fontFamily;
    private readonly double? _textHeight;
    private readonly double? _widthFactor;
    private readonly double? _obliqueAngle;
    private readonly bool? _isBold;
    private readonly bool? _isItalic;
    private TextStyleState? _previousState;

    public string Name => "Set Text Style Properties";

    public SetTextStylePropertiesCommand(
        StyleId styleId,
        string? fontFamily = null,
        double? textHeight = null,
        double? widthFactor = null,
        double? obliqueAngle = null,
        bool? isBold = null,
        bool? isItalic = null)
    {
        _styleId = styleId;
        _fontFamily = fontFamily;
        _textHeight = textHeight;
        _widthFactor = widthFactor;
        _obliqueAngle = obliqueAngle;
        _isBold = isBold;
        _isItalic = isItalic;

        if (_fontFamily is not null && string.IsNullOrWhiteSpace(_fontFamily))
            throw new ArgumentException("Font family cannot be empty.", nameof(fontFamily));
        if (_textHeight is { } height && !IsPositiveFinite(height))
            throw new ArgumentOutOfRangeException(nameof(textHeight));
        if (_widthFactor is { } width && !IsPositiveFinite(width))
            throw new ArgumentOutOfRangeException(nameof(widthFactor));
        if (_obliqueAngle is { } angle && !double.IsFinite(angle))
            throw new ArgumentOutOfRangeException(nameof(obliqueAngle));

        if (_fontFamily is null &&
            _textHeight is null &&
            _widthFactor is null &&
            _obliqueAngle is null &&
            _isBold is null &&
            _isItalic is null)
        {
            throw new ArgumentException("At least one text style property is required.");
        }
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var style = GetStyle(document);
        var current = TextStyleState.From(style);
        _previousState ??= current;

        Apply(style);
        return CreateChangeSet(document);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_previousState is not { } previousState)
            return CadDocumentChangeSet.Empty;

        Apply(GetStyle(document), previousState);
        return CreateChangeSet(document);
    }

    private CadTextStyle GetStyle(CadDocument document)
    {
        if (!document.TryGetStyle(_styleId, out var style) || style is not CadTextStyle textStyle)
            throw new InvalidOperationException($"Text style does not exist: {_styleId}");
        return textStyle;
    }

    private void Apply(CadTextStyle style)
    {
        if (_fontFamily is not null)
            style.SetFontFamily(_fontFamily);
        if (_textHeight is { } textHeight)
            style.SetTextHeight(textHeight);
        if (_widthFactor is { } widthFactor)
            style.SetWidthFactor(widthFactor);
        if (_obliqueAngle is { } obliqueAngle)
            style.SetObliqueAngle(obliqueAngle);
        if (_isBold is { } isBold)
            style.SetBold(isBold);
        if (_isItalic is { } isItalic)
            style.SetItalic(isItalic);
    }

    private static void Apply(CadTextStyle style, TextStyleState state)
    {
        style.SetFontFamily(state.FontFamily);
        style.SetTextHeight(state.TextHeight);
        style.SetWidthFactor(state.WidthFactor);
        style.SetObliqueAngle(state.ObliqueAngle);
        style.SetBold(state.IsBold);
        style.SetItalic(state.IsItalic);
    }

    private static bool IsPositiveFinite(double value) =>
        value > 0 && double.IsFinite(value);

    private CadDocumentChangeSet CreateChangeSet(CadDocument document)
    {
        var references = document.Entities.Values
            .Where(entity => !entity.IsErased && entity is CadText text && text.TextStyleId == _styleId)
            .Select(entity =>
            {
                ((CadText)entity).MarkBoundsForMeasurement();
                return entity.Id;
            })
            .ToArray();

        return CadDocumentChangeSet
            .ForEntities(references, CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance)
            .WithTableChanges(CadDocumentTableChangeKind.Styles);
    }

    private readonly record struct TextStyleState(
        string FontFamily,
        double TextHeight,
        double WidthFactor,
        double ObliqueAngle,
        bool IsBold,
        bool IsItalic)
    {
        public static TextStyleState From(CadTextStyle style) => new(
            style.FontFamily,
            style.TextHeight,
            style.WidthFactor,
            style.ObliqueAngle,
            style.IsBold,
            style.IsItalic);
    }
}
