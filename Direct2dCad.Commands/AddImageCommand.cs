using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class AddImageCommand : ICadCommand
{
    private readonly CadRectD _bounds;
    private readonly int _pixelWidth;
    private readonly int _pixelHeight;
    private readonly int _stride;
    private readonly byte[] _pixels;
    private readonly LayerId? _layerId;
    private readonly string _contentType;
    private readonly string _sourceName;
    private readonly string _name;
    private readonly int _zIndex;
    private readonly bool _isVisible;
    private readonly double _opacity;
    private readonly double _rotationRadians;
    private EntityId? _createdEntityId;

    public string Name => "Add Image";
    public EntityId? CreatedEntityId => _createdEntityId;

    public AddImageCommand(
        CadRectD bounds,
        int pixelWidth,
        int pixelHeight,
        int stride,
        byte[] pixels,
        LayerId? layerId = null,
        string contentType = "image/bgra32",
        string sourceName = "",
        string name = "",
        int zIndex = 0,
        bool isVisible = true,
        double opacity = 1.0,
        double rotationRadians = 0.0)
    {
        _bounds = bounds;
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        _stride = stride;
        _pixels = pixels is null ? throw new ArgumentNullException(nameof(pixels)) : (byte[])pixels.Clone();
        _layerId = layerId;
        _contentType = contentType;
        _sourceName = sourceName;
        _name = name;
        _zIndex = zIndex;
        _isVisible = isVisible;
        _opacity = opacity;
        _rotationRadians = rotationRadians;
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

        var image = document.AddImage(
            _bounds,
            _pixelWidth,
            _pixelHeight,
            _stride,
            _pixels,
            _layerId,
            _contentType,
            _sourceName,
            _name,
            _opacity,
            _rotationRadians);
        image.SetZIndex(_zIndex);
        image.SetVisible(_isVisible);

        _createdEntityId = image.Id;
        return CadDocumentChangeSet.ForEntity(
            image.Id,
            CadEntityChangeKind.Created |
            CadEntityChangeKind.Geometry |
            CadEntityChangeKind.Appearance |
            CadEntityChangeKind.Visibility |
            CadEntityChangeKind.DrawOrder);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_createdEntityId is null || !document.TryGetEntity(_createdEntityId.Value, out var entity) || entity is null)
            return CadDocumentChangeSet.Empty;

        entity.Erase();
        return CadDocumentChangeSet.ForEntity(entity.Id, CadEntityChangeKind.Deleted | CadEntityChangeKind.Visibility);
    }
}
