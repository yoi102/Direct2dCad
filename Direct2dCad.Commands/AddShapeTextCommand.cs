using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class AddShapeTextCommand : ICadCommand
{
    private readonly string _text;
    private readonly CadPointD _position;
    private readonly double _height;
    private readonly double _rotationRadians;
    private readonly double _widthFactor;
    private readonly double _characterSpacingFactor;
    private readonly double _obliqueAngleRadians;
    private readonly LayerId? _layerId;
    private readonly StyleId? _graphicStyleId;
    private readonly string _name;
    private readonly bool _isInverted;
    private readonly double _invertedMarginFactor;
    private readonly CadShapeFontId _shapeFontId;
    private EntityId? _createdEntityId;

    public string Name => "Add Shape Text";
    public EntityId? CreatedEntityId => _createdEntityId;

    public AddShapeTextCommand(
        string text,
        CadPointD position,
        double height,
        double rotationRadians = 0,
        double widthFactor = CadStrokeFont.DefaultWidthFactor,
        double characterSpacingFactor = CadStrokeFont.DefaultCharacterSpacingFactor,
        double obliqueAngleRadians = CadStrokeFont.DefaultObliqueAngleRadians,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        string name = "",
        bool isInverted = false,
        double invertedMarginFactor = CadShapeText.DefaultInvertedMarginFactor,
        CadShapeFontId shapeFontId = default)
    {
        _text = text ?? string.Empty;
        _position = position;
        _height = height;
        _rotationRadians = rotationRadians;
        _widthFactor = widthFactor;
        _characterSpacingFactor = characterSpacingFactor;
        _obliqueAngleRadians = obliqueAngleRadians;
        _layerId = layerId;
        _graphicStyleId = graphicStyleId;
        _name = name;
        _isInverted = isInverted;
        _invertedMarginFactor = invertedMarginFactor;
        _shapeFontId = CadShapeFontRegistry.GetOrDefault(shapeFontId).Id;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CadEntityAccessPolicy.EnsureCanAddToLayer(document, _layerId ?? LayerId.Default);

        if (_createdEntityId is not null &&
            document.TryGetEntity(_createdEntityId.Value, out var existing) &&
            existing is not null)
        {
            existing.Restore();
            return CadDocumentChangeSet.ForEntity(
                existing.Id,
                CadEntityChangeKind.Created | CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance | CadEntityChangeKind.Visibility);
        }

        var text = document.AddShapeText(
            _text,
            _position,
            _height,
            _rotationRadians,
            _widthFactor,
            _characterSpacingFactor,
            _obliqueAngleRadians,
            _layerId,
            _graphicStyleId,
            _name,
            _isInverted,
            _invertedMarginFactor,
            _shapeFontId);

        _createdEntityId = text.Id;
        return CadDocumentChangeSet.ForEntity(
            text.Id,
            CadEntityChangeKind.Created | CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_createdEntityId is null ||
            !document.TryGetEntity(_createdEntityId.Value, out var entity) ||
            entity is null)
        {
            return CadDocumentChangeSet.Empty;
        }

        entity.Erase();
        return CadDocumentChangeSet.ForEntity(entity.Id, CadEntityChangeKind.Deleted | CadEntityChangeKind.Visibility);
    }
}
