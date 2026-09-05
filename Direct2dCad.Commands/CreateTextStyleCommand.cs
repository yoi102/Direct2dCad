using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;

namespace Direct2dCad.Commands;

/// <summary>
/// Creates a text style as a document command. The same style instance and ID are
/// restored on redo so entities in the same command batch can safely reference it.
/// </summary>
public sealed class CreateTextStyleCommand : ICadCommand
{
    private readonly string _name;
    private readonly string _fontFamily;
    private readonly double _textHeight;
    private readonly double _widthFactor;
    private readonly double _obliqueAngle;
    private readonly bool _isBold;
    private readonly bool _isItalic;
    private CadTextStyle? _createdStyle;

    public string Name => "Create Text Style";
    public StyleId? CreatedStyleId => _createdStyle?.Id;

    public CreateTextStyleCommand(
        string name,
        string fontFamily,
        double textHeight = 1.0,
        double widthFactor = 1.0,
        double obliqueAngle = 0.0,
        bool isBold = false,
        bool isItalic = false)
    {
        _name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Style name cannot be empty.", nameof(name))
            : name.Trim();
        _fontFamily = string.IsNullOrWhiteSpace(fontFamily)
            ? throw new ArgumentException("Font family cannot be empty.", nameof(fontFamily))
            : fontFamily.Trim();
        _textHeight = RequirePositiveFinite(textHeight, nameof(textHeight));
        _widthFactor = RequirePositiveFinite(widthFactor, nameof(widthFactor));
        _obliqueAngle = double.IsFinite(obliqueAngle)
            ? obliqueAngle
            : throw new ArgumentOutOfRangeException(nameof(obliqueAngle));
        _isBold = isBold;
        _isItalic = isItalic;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_createdStyle is null)
        {
            var styleId = document.CreateTextStyle(
                _name,
                _fontFamily,
                _textHeight,
                _widthFactor,
                _obliqueAngle,
                _isBold,
                _isItalic);
            _createdStyle = (CadTextStyle)document.Styles[styleId];
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
            !document.Entities.Values.Any(entity =>
                entity is CadText text && text.TextStyleId == _createdStyle.Id))
            document.RemoveStyleCore(_createdStyle.Id);

        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.Styles);
    }

    private static double RequirePositiveFinite(double value, string name) =>
        value > 0 && double.IsFinite(value)
            ? value
            : throw new ArgumentOutOfRangeException(name, "Value must be finite and greater than zero.");
}
