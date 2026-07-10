using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Commands;

/// <summary>Replaces an embedded OLE object's persisted storage.</summary>
public sealed class SetOleObjectDataCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly OleObjectData _next;
    private OleObjectData? _previous;

    public string Name => "Update OLE Object";

    public SetOleObjectDataCommand(
        EntityId entityId,
        byte[] oleBytes,
        string contentType,
        string sourceName)
    {
        _entityId = entityId;
        _next = new OleObjectData(oleBytes, contentType, sourceName);
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        var oleObject = GetOleObject(document);
        _previous = OleObjectData.From(oleObject);
        _next.ApplyTo(oleObject);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Appearance);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previous is null)
            return CadDocumentChangeSet.Empty;

        _previous.ApplyTo(GetOleObject(document));
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Appearance);
    }

    private CadOleObject GetOleObject(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) as CadOleObject
               ?? throw new InvalidOperationException($"Entity is not an OLE object: {_entityId}");
    }

    private sealed record OleObjectData(
        byte[] OleBytes,
        string ContentType,
        string SourceName)
    {
        public static OleObjectData From(CadOleObject oleObject) => new(
            oleObject.CopyOleBytes(),
            oleObject.ContentType,
            oleObject.SourceName);

        public void ApplyTo(CadOleObject oleObject) => oleObject.SetOleData(
            (byte[])OleBytes.Clone(),
            ContentType,
            SourceName);
    }
}
