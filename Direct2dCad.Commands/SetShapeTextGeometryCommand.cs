using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class SetShapeTextGeometryCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly CadPointD _position;
    private readonly double _height;
    private readonly double _rotationRadians;
    private readonly double _widthFactor;
    private readonly double _characterSpacingFactor;
    private readonly double _obliqueAngleRadians;
    private CadPointD? _previousPosition;
    private double? _previousHeight;
    private double? _previousRotationRadians;
    private double? _previousWidthFactor;
    private double? _previousCharacterSpacingFactor;
    private double? _previousObliqueAngleRadians;

    public string Name => "Set Shape Text Geometry";

    public SetShapeTextGeometryCommand(
        EntityId entityId,
        CadPointD position,
        double height,
        double rotationRadians,
        double widthFactor,
        double characterSpacingFactor,
        double obliqueAngleRadians)
    {
        _entityId = entityId;
        _position = position;
        _height = height;
        _rotationRadians = rotationRadians;
        _widthFactor = widthFactor;
        _characterSpacingFactor = characterSpacingFactor;
        _obliqueAngleRadians = obliqueAngleRadians;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        CadCommandEntityAccess.EnsureEditable(document, _entityId);
        var text = GetShapeText(document);
        _previousPosition = text.Position;
        _previousHeight = text.Height;
        _previousRotationRadians = text.RotationRadians;
        _previousWidthFactor = text.WidthFactor;
        _previousCharacterSpacingFactor = text.CharacterSpacingFactor;
        _previousObliqueAngleRadians = text.ObliqueAngleRadians;
        Apply(text, _position, _height, _rotationRadians, _widthFactor, _characterSpacingFactor, _obliqueAngleRadians);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousPosition is null ||
            _previousHeight is null ||
            _previousRotationRadians is null ||
            _previousWidthFactor is null ||
            _previousCharacterSpacingFactor is null ||
            _previousObliqueAngleRadians is null)
        {
            return CadDocumentChangeSet.Empty;
        }

        Apply(
            GetShapeText(document),
            _previousPosition.Value,
            _previousHeight.Value,
            _previousRotationRadians.Value,
            _previousWidthFactor.Value,
            _previousCharacterSpacingFactor.Value,
            _previousObliqueAngleRadians.Value);

        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    private static void Apply(
        CadShapeText text,
        CadPointD position,
        double height,
        double rotationRadians,
        double widthFactor,
        double characterSpacingFactor,
        double obliqueAngleRadians)
    {
        text.SetGeometry(position, height, rotationRadians, widthFactor, characterSpacingFactor, obliqueAngleRadians);
    }

    private CadShapeText GetShapeText(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) is CadShapeText text
            ? text
            : throw new InvalidOperationException($"Entity is not shape text: {_entityId}");
    }
}
