using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Commands;

public sealed class SetTextContentCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly string _text;
    private string? _previousText;

    public string Name => "Set Text Content";

    public SetTextContentCommand(EntityId entityId, string text)
    {
        _entityId = entityId;
        _text = text ?? string.Empty;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        var text = GetText(document);
        _previousText = text.Text;
        text.SetText(_text);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousText is null)
            return CadDocumentChangeSet.Empty;

        GetText(document).SetText(_previousText);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance);
    }

    private CadText GetText(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) is CadText text
            ? text
            : throw new InvalidOperationException($"Entity is not text: {_entityId}");
    }
}
