using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Commands;

public sealed class SetImageDataCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly ImageData _next;
    private ImageData? _previous;

    public SetImageDataCommand(
        EntityId entityId,
        int pixelWidth,
        int pixelHeight,
        int stride,
        byte[] pixels,
        string contentType,
        string sourceName)
    {
        _entityId = entityId;
        ArgumentNullException.ThrowIfNull(pixels);
        _next = new ImageData(
            pixelWidth,
            pixelHeight,
            stride,
            (byte[])pixels.Clone(),
            contentType,
            sourceName);
    }

    public string Name => "Update Image Data";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        CadCommandEntityAccess.EnsureEditable(document, _entityId);
        var image = GetImage(document);
        _previous = ImageData.From(image);
        _next.ApplyTo(image);
        return ChangeSet();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previous is null)
            return CadDocumentChangeSet.Empty;

        _previous.ApplyTo(GetImage(document));
        return ChangeSet();
    }

    private CadImage GetImage(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) as CadImage
               ?? throw new InvalidOperationException($"Entity is not an image: {_entityId}");
    }

    private CadDocumentChangeSet ChangeSet() => CadDocumentChangeSet.ForEntity(
        _entityId,
        CadEntityChangeKind.Appearance | CadEntityChangeKind.EmbeddedData);

    private sealed record ImageData(
        int PixelWidth,
        int PixelHeight,
        int Stride,
        byte[] Pixels,
        string ContentType,
        string SourceName)
    {
        public static ImageData From(CadImage image) => new(
            image.PixelWidth,
            image.PixelHeight,
            image.Stride,
            image.CopyPixels(),
            image.ContentType,
            image.SourceName);

        public void ApplyTo(CadImage image) => image.SetImageData(
            PixelWidth,
            PixelHeight,
            Stride,
            (byte[])Pixels.Clone(),
            ContentType,
            SourceName);
    }
}
