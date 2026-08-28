using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.HitTesting;

namespace Direct2dCad.Editor.Commands;

public sealed class ClickSelectCommand : SelectionCommandBase
{
    private readonly CadPointD _worldPoint;
    private readonly double _tolerance;
    private readonly CadSelectionMode _mode;
    private readonly bool _clearWhenMiss;
    private readonly Func<CadEntity, bool> _selectionFilter;
    private readonly BlockId _ownerBlockId;
    private readonly CadHitTestOptions? _hitTestOptions;

    public override string Name => "Click Select";
    public IReadOnlyList<EntityId> HitEntityIds { get; private set; } = [];
    public EntityId? SelectedEntityId { get; private set; }

    public ClickSelectCommand(
        CadPointD worldPoint,
        double tolerance,
        CadSelectionMode mode = CadSelectionMode.Replace,
        bool clearWhenMiss = true,
        Func<CadEntity, bool>? selectionFilter = null,
        BlockId? ownerBlockId = null,
        CadHitTestOptions? hitTestOptions = null)
    {
        if (tolerance < 0 || double.IsNaN(tolerance) || double.IsInfinity(tolerance))
            throw new ArgumentOutOfRangeException(nameof(tolerance));

        _worldPoint = worldPoint;
        _tolerance = tolerance;
        _mode = mode;
        _clearWhenMiss = clearWhenMiss;
        _selectionFilter = selectionFilter ?? (_ => true);
        _ownerBlockId = ownerBlockId ?? BlockId.ModelSpace;
        _hitTestOptions = hitTestOptions;
    }

    protected override void ExecuteSelection(CadEditorCommandContext context)
    {
        HitEntityIds = FindHits(context);
        SelectedEntityId = HitEntityIds.Count > 0 ? HitEntityIds[0] : null;

        if (SelectedEntityId is not { } selectedEntityId)
        {
            if (_mode == CadSelectionMode.Replace && _clearWhenMiss)
                context.Selection.Clear();
            return;
        }

        ApplySelection(context.Selection, [selectedEntityId], _mode);
    }

    private IReadOnlyList<EntityId> FindHits(CadEditorCommandContext context)
    {
        var options = _hitTestOptions ?? new CadHitTestOptions(context.Viewport.Zoom);
        var queryPadding = _tolerance + context.HitTesting.GetMaxStrokeHitPadding(options);
        var queryArea = CadRectD.FromCenter(
            _worldPoint,
            queryPadding * 2,
            queryPadding * 2);

        var candidateIds = context.SpatialIndex.Query(_ownerBlockId, queryArea);
        var candidates = new List<HitCandidate>(candidateIds.Count);
        foreach (var entityId in candidateIds)
        {
            if (TryHit(context, entityId, options) is { } candidate)
                candidates.Add(candidate);
        }

        candidates.Sort(static (left, right) =>
        {
            var result = right.LayerPriority.CompareTo(left.LayerPriority);
            if (result != 0)
                return result;

            result = right.ZIndex.CompareTo(left.ZIndex);
            if (result != 0)
                return result;

            result = right.EntityOrder.CompareTo(left.EntityOrder);
            return result != 0
                ? result
                : left.Distance.CompareTo(right.Distance);
        });

        var hitEntityIds = new List<EntityId>(candidates.Count);
        var seenEntityIds = new HashSet<EntityId>();
        foreach (var candidate in candidates)
        {
            if (seenEntityIds.Add(candidate.TopEntityId))
                hitEntityIds.Add(candidate.TopEntityId);
        }

        return hitEntityIds;
    }

    private HitCandidate? TryHit(
        CadEditorCommandContext context,
        EntityId entityId,
        CadHitTestOptions options)
    {
        if (!context.Document.TryGetEntity(entityId, out var entity) ||
            entity is null ||
            !_selectionFilter(entity))
        {
            return null;
        }

        if (context.HitTesting.HitTestEntityEdge(entityId, _worldPoint, _tolerance, options, out var edgeHit))
            return CreateHitCandidate(context, edgeHit);

        if (context.HitTesting.HitTestEntityFill(entityId, _worldPoint, out var fillHit))
            return CreateHitCandidate(context, fillHit);

        return null;
    }

    private static HitCandidate? CreateHitCandidate(
        CadEditorCommandContext context,
        CadHitTestResult hit)
    {
        var topEntityId = hit.TopEntityId;
        if (!context.Document.TryGetEntity(topEntityId, out var topEntity) || topEntity is null)
            return null;

        return new HitCandidate(
            topEntityId,
            hit.Distance,
            context.Document.DocumentSettings.LayerDrawingPriority.GetPriority(topEntity.LayerId),
            topEntity.ZIndex,
            context.Document.GetEntityInsertionIndex(topEntity.Id));
    }

    private readonly record struct HitCandidate(
        EntityId TopEntityId,
        double Distance,
        int LayerPriority,
        int ZIndex,
        long EntityOrder);
}
