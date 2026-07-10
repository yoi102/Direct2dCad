using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class AddOleObjectCommand : ICadCommand
{
    private readonly CadRectD _bounds;
    private readonly int _pixelWidth;
    private readonly int _pixelHeight;
    private readonly int _stride;
    private readonly byte[] _pixels;
    private readonly byte[] _oleBytes;
    private readonly LayerId? _layerId;
    private readonly string _contentType;
    private readonly string _sourceName;
    private readonly string _name;
    private readonly int _zIndex;
    private readonly bool _isVisible;
    private EntityId? _createdEntityId;

    public string Name => "Add OLE Object";
    public EntityId? CreatedEntityId => _createdEntityId;

    public AddOleObjectCommand(
        CadRectD bounds,
        int pixelWidth,
        int pixelHeight,
        int stride,
        byte[] pixels,
        byte[] oleBytes,
        LayerId? layerId = null,
        string contentType = "application/x-ole-storage",
        string sourceName = "",
        string name = "",
        int zIndex = 0,
        bool isVisible = true)
    {
        _bounds = bounds;
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        _stride = stride;
        _pixels = pixels is null ? throw new ArgumentNullException(nameof(pixels)) : (byte[])pixels.Clone();
        _oleBytes = oleBytes is null ? throw new ArgumentNullException(nameof(oleBytes)) : (byte[])oleBytes.Clone();
        _layerId = layerId;
        _contentType = contentType;
        _sourceName = sourceName;
        _name = name;
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

        var oleObject = document.AddOleObject(
            _bounds,
            _pixelWidth,
            _pixelHeight,
            _stride,
            _pixels,
            _oleBytes,
            _layerId,
            _contentType,
            _sourceName,
            _name);
        oleObject.SetZIndex(_zIndex);
        oleObject.SetVisible(_isVisible);

        _createdEntityId = oleObject.Id;
        return CadDocumentChangeSet.ForEntity(
            oleObject.Id,
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
