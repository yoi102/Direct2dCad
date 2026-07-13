using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Commands;

public sealed class SetTextStyleCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly StyleId? _textStyleId;
    private StyleId? _previousTextStyleId;

    public string Name => "Set Text Style";

    public SetTextStyleCommand(EntityId entityId, StyleId? textStyleId)
    {
        _entityId = entityId;
        _textStyleId = textStyleId;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        CadCommandEntityAccess.EnsureEditable(document, _entityId);
        var text = GetText(document);
        _previousTextStyleId = text.TextStyleId;
        document.SetTextEntityStyle(_entityId, _textStyleId);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Appearance | CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.SetTextEntityStyle(_entityId, _previousTextStyleId);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Appearance | CadEntityChangeKind.Geometry);
    }

    private CadText GetText(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) is CadText text
            ? text
            : throw new InvalidOperationException($"Entity is not text: {_entityId}");
    }
}
