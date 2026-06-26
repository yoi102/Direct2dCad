using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class SetTextGeometryCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly CadPointD _position;
    private readonly double _height;
    private readonly double _rotationRadians;
    private CadPointD? _previousPosition;
    private double? _previousHeight;
    private double? _previousRotationRadians;

    public string Name => "Set Text Geometry";

    public SetTextGeometryCommand(
        EntityId entityId,
        CadPointD position,
        double height,
        double rotationRadians)
    {
        _entityId = entityId;
        _position = position;
        _height = height;
        _rotationRadians = rotationRadians;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        var text = GetText(document);
        _previousPosition = text.Position;
        _previousHeight = text.Height;
        _previousRotationRadians = text.RotationRadians;
        Apply(text, _position, _height, _rotationRadians);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousPosition is null || _previousHeight is null || _previousRotationRadians is null)
            return CadDocumentChangeSet.Empty;

        Apply(GetText(document), _previousPosition.Value, _previousHeight.Value, _previousRotationRadians.Value);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    private static void Apply(CadText text, CadPointD position, double height, double rotationRadians)
    {
        text.SetPosition(position);
        text.SetHeight(height);
        text.SetRotation(rotationRadians);
    }

    private CadText GetText(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) is CadText text
            ? text
            : throw new InvalidOperationException($"Entity is not text: {_entityId}");
    }
}
