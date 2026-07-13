using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class SetLineGeometryCommand : ICadCommand
{
    private readonly EntityId _entityId;
    private readonly CadPointD _start;
    private readonly CadPointD _end;
    private CadPointD? _previousStart;
    private CadPointD? _previousEnd;

    public string Name => "Set Line Geometry";

    public SetLineGeometryCommand(EntityId entityId, CadPointD start, CadPointD end)
    {
        _entityId = entityId;
        _start = start;
        _end = end;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        CadCommandEntityAccess.EnsureEditable(document, _entityId);
        var line = GetLine(document);
        _previousStart = line.Start;
        _previousEnd = line.End;
        line.SetGeometry(_start, _end);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousStart is null || _previousEnd is null)
            return CadDocumentChangeSet.Empty;

        GetLine(document).SetGeometry(_previousStart.Value, _previousEnd.Value);
        return CadDocumentChangeSet.ForEntity(_entityId, CadEntityChangeKind.Geometry);
    }

    private CadLine GetLine(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEntity(_entityId) is CadLine line
            ? line
            : throw new InvalidOperationException($"Entity is not line: {_entityId}");
    }
}
