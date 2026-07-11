using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Commands;

public sealed class SetImageRotationCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly double _rotationRadians;
    private double? _previousRotationRadians;

    public string Name => "Set Image Rotation";

    public SetImageRotationCommand(EntityId entityId, double rotationRadians)
    {
        if (double.IsNaN(rotationRadians) || double.IsInfinity(rotationRadians))
            throw new ArgumentOutOfRangeException(nameof(rotationRadians));

        _entityId = entityId;
        _rotationRadians = rotationRadians;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        var image = GetImage(document);
        _previousRotationRadians = image.RotationRadians;
        image.SetRotation(_rotationRadians);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Rotation);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousRotationRadians is null)
            return CadDocumentChangeSet.Empty;

        GetImage(document).SetRotation(_previousRotationRadians.Value);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Rotation);
    }

    private CadImage GetImage(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) is CadImage image
            ? image
            : throw new InvalidOperationException($"Entity is not image: {_entityId}");
    }
}
