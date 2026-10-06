using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class BooleanRegionsCommand : ICadCommand
{
    private readonly EntityId[] _sources;
    private readonly CadBooleanOperation _operation;
    private readonly EntityId _subject;
    private IReadOnlyList<CadRegionContour>[]? _snapshots;
    private IReadOnlyList<CadRegionContour>? _prepared;
    public string Name => _operation.ToString();
    public EntityId? ResultEntityId { get; private set; }

    public BooleanRegionsCommand(IEnumerable<EntityId> sources, CadBooleanOperation operation, EntityId? subject = null)
    {
        _sources = sources.Distinct().ToArray();
        if (_sources.Length < 2 || !Enum.IsDefined(operation)) throw new ArgumentException("Choose at least two closed entities.");
        if (operation == CadBooleanOperation.Difference && (subject is null || !_sources.Contains(subject.Value)))
            throw new ArgumentException("Choose the subject to keep for difference.", nameof(subject));
        _operation = operation; _subject = subject ?? _sources[0];
    }

    /// <summary>Capture immutable geometry on the document thread, then compute off that thread.</summary>
    public async Task<IReadOnlyList<CadRegionContour>> PrepareAsync(CadDocument document, CancellationToken token = default)
    {
        var snapshots = _sources.Select(id => CadRegionBoolean.GetContours(document.GetEntity(id))).ToArray();
        var result = await Task.Run(() => CadRegionBoolean.Compute(snapshots, _operation, Array.IndexOf(_sources, _subject), token), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        _snapshots = snapshots; _prepared = result;
        return result;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        var entities = _sources.Select(document.GetEntity).ToArray();
        foreach (var e in entities)
        {
            CadEntityAccessPolicy.EnsureEditable(document, e);
            CadEntityAccessPolicy.EnsureCanAddToLayer(document, e.LayerId);
            if (!CadRegionBoolean.Supports(e) || e.OwnerBlockId != entities[0].OwnerBlockId)
                throw new InvalidOperationException("Choose supported closed entities in the same owner space.");
        }
        if (ResultEntityId is { } existing)
        {
            document.GetEntity(existing).Restore();
            foreach (var e in entities) e.Erase();
            return Changes(document, false);
        }
        if (_snapshots is not null && entities.Where((e, i) => !SameGeometry(CadRegionBoolean.GetContours(e), _snapshots[i])).Any())
            throw new InvalidOperationException("Operand geometry changed after the preview; start the operation again.");
        var contours = _prepared ?? CadRegionBoolean.Compute(entities, _operation, Array.IndexOf(_sources, _subject));
        if (contours.Count == 0) throw new InvalidOperationException("The Boolean result is empty; original entities were retained.");
        var source = document.GetEntity(_subject);
        CadRegion? result = null;
        try
        {
            var fill = source switch { CadCircle e => e.FillStyleId, CadEllipse e => e.FillStyleId, CadRectangle e => e.FillStyleId,
                CadPolyline e => e.FillStyleId, CadCompositePath e => e.FillStyleId, CadRegion e => e.FillStyleId, _ => null };
            result = document.AddRegion(contours, source.LayerId, CadCurveEditing.GraphicStyle(source), fill, source.Name);
            document.MoveEntityToBlock(result.Id, source.OwnerBlockId);
            result.SetLineWeightState(source.LineWeight, source.UseLayerLineWeight);
            result.SetColorSource(source.ColorSource); result.SetStrokeStyle(source.StrokeStyle);
            result.SetZIndex(source.ZIndex); result.SetVisible(source.IsVisible);
            foreach (var e in entities) e.Erase();
            ResultEntityId = result.Id;
            return Changes(document, false);
        }
        catch
        {
            if (result is not null) document.RemoveEntity(result.Id);
            foreach (var e in entities) e.Restore();
            ResultEntityId = null;
            throw;
        }
    }
    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (ResultEntityId is not { } result) throw new InvalidOperationException("Operation was not executed.");
        foreach (var id in _sources) document.GetEntity(id).Restore();
        document.GetEntity(result).Erase();
        return Changes(document, true);
    }
    private CadDocumentChangeSet Changes(CadDocument document, bool undo) => CadCommandGeometryChanges.Resolve(document,
        _sources.Select(id => new CadEntityChange(id, undo ? CadEntityChangeKind.Created : CadEntityChangeKind.Deleted))
            .Append(new CadEntityChange(ResultEntityId!.Value, undo ? CadEntityChangeKind.Deleted : CadEntityChangeKind.Created)).ToArray());
    private static bool SameGeometry(IReadOnlyList<CadRegionContour> a, IReadOnlyList<CadRegionContour> b) =>
        a.Count == b.Count && a.Where((c, i) => !c.Edges.SequenceEqual(b[i].Edges)).Any() == false;
}
