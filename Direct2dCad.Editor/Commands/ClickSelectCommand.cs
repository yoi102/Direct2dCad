using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.HitTesting;

namespace Direct2dCad.Editor.Commands;

public sealed class ClickSelectCommand : SelectionCommandBase
{
    private readonly CadPointD _worldPoint;
    private readonly double _tolerance;
    private readonly CadSelectionMode _mode;
    private readonly bool _clearWhenMiss;

    public override string Name => "Click Select";

    public ClickSelectCommand(
        CadPointD worldPoint,
        double tolerance,
        CadSelectionMode mode = CadSelectionMode.Replace,
        bool clearWhenMiss = true)
    {
        if (tolerance < 0 || double.IsNaN(tolerance) || double.IsInfinity(tolerance))
            throw new ArgumentOutOfRangeException(nameof(tolerance));

        _worldPoint = worldPoint;
        _tolerance = tolerance;
        _mode = mode;
        _clearWhenMiss = clearWhenMiss;
    }

    protected override void ExecuteSelection(CadEditorCommandContext context)
    {
        var hit = FindTopHit(context);

        if (hit is null)
        {
            if (_mode == CadSelectionMode.Replace && _clearWhenMiss)
                context.Selection.Clear();
            return;
        }

        ApplySelection(context.Selection, [hit.Value], _mode);
    }

    private EntityId? FindTopHit(CadEditorCommandContext context)
    {
        var options = new CadHitTestOptions(context.Viewport.Zoom);
        var queryPadding = _tolerance + context.HitTesting.GetMaxStrokeHitPadding(options);
        var queryArea = CadRectD.FromCenter(
            _worldPoint,
            queryPadding * 2,
            queryPadding * 2);

        var hit = context.SpatialIndex.Query(queryArea)
            .Select(entityId => TryHit(context, entityId, options))
            .Where(x => x is not null)
            .OrderBy(x => x!.Value.Distance)
            .Select(x => x!.Value.TopEntityId)
            .FirstOrDefault();

        return hit.Equals(default(EntityId)) ? null : hit;
    }

    private HitCandidate? TryHit(
        CadEditorCommandContext context,
        EntityId entityId,
        CadHitTestOptions options)
    {
        if (context.HitTesting.HitTestEntityEdge(entityId, _worldPoint, _tolerance, options, out var edgeHit))
            return new HitCandidate(edgeHit.TopEntityId, edgeHit.Distance);

        if (context.HitTesting.HitTestEntityFill(entityId, _worldPoint, out var fillHit))
            return new HitCandidate(fillHit.TopEntityId, fillHit.Distance);

        return null;
    }

    private readonly record struct HitCandidate(EntityId TopEntityId, double Distance);
}
