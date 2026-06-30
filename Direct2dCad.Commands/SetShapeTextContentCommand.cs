using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Commands;

public sealed class SetShapeTextContentCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly string _text;
    private string? _previousText;

    public string Name => "Set Shape Text Content";

    public SetShapeTextContentCommand(EntityId entityId, string text)
    {
        _entityId = entityId;
        _text = text ?? string.Empty;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        var text = GetShapeText(document);
        _previousText = text.Text;
        text.SetText(_text);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousText is null)
            return CadDocumentChangeSet.Empty;

        GetShapeText(document).SetText(_previousText);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    private CadShapeText GetShapeText(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) is CadShapeText text
            ? text
            : throw new InvalidOperationException($"Entity is not shape text: {_entityId}");
    }
}
