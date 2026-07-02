using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class AddTextCommand : ICadCommand
{
    private readonly string _text;
    private readonly CadPointD _position;
    private readonly double _height;
    private readonly double _rotationRadians;
    private readonly LayerId? _layerId;
    private readonly StyleId? _graphicStyleId;
    private readonly StyleId? _textStyleId;
    private readonly string _name;
    private readonly bool _isInverted;
    private readonly double _invertedMarginFactor;
    private readonly CadLineWeight? _lineWeight;
    private readonly int _zIndex;
    private readonly bool _isVisible;
    private EntityId? _createdEntityId;

    public string Name => "Add Text";
    public EntityId? CreatedEntityId => _createdEntityId;

    public AddTextCommand(
        string text,
        CadPointD position,
        double height,
        double rotationRadians = 0,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? textStyleId = null,
        string name = "",
        bool isInverted = false,
        double invertedMarginFactor = CadText.DefaultInvertedMarginFactor,
        CadLineWeight? lineWeight = null,
        int zIndex = 0,
        bool isVisible = true)
    {
        _text = text ?? string.Empty;
        _position = position;
        _height = height;
        _rotationRadians = rotationRadians;
        _layerId = layerId;
        _graphicStyleId = graphicStyleId;
        _textStyleId = textStyleId;
        _name = name;
        _isInverted = isInverted;
        _invertedMarginFactor = invertedMarginFactor;
        _lineWeight = lineWeight;
        _zIndex = zIndex;
        _isVisible = isVisible;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_createdEntityId is not null &&
            document.TryGetEntity(_createdEntityId.Value, out var existing) &&
            existing is not null)
        {
            existing.Restore();
            return CadDocumentChangeSet.ForEntity(
                existing.Id,
                CadEntityChangeKind.Created |
                CadEntityChangeKind.Geometry |
                CadEntityChangeKind.Appearance |
                CadEntityChangeKind.Visibility |
                CadEntityChangeKind.DrawOrder);
        }

        var text = document.AddText(
            _text,
            _position,
            _height,
            _rotationRadians,
            _layerId,
            _graphicStyleId,
            _textStyleId,
            _name,
            _isInverted,
            _invertedMarginFactor);

        text.SetLineWeight(_lineWeight);
        text.SetZIndex(_zIndex);
        text.SetVisible(_isVisible);

        _createdEntityId = text.Id;
        return CadDocumentChangeSet.ForEntity(
            text.Id,
            CadEntityChangeKind.Created |
            CadEntityChangeKind.Geometry |
            CadEntityChangeKind.Appearance |
            CadEntityChangeKind.Visibility |
            CadEntityChangeKind.DrawOrder);
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
