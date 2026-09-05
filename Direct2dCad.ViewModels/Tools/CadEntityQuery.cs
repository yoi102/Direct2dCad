using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.ViewModels.Tools;

internal static partial class CadEntityQuery
{
    internal const string CurrentSpaceScope = "current_space";
    internal const string DocumentScope = "document";
    internal static readonly string[] EntityTypeNames =
    [
        "Line", "Circle", "Arc", "Ellipse", "EllipseArc", "Rectangle", "Polyline", "Spline",
        "CompositePath", "Text", "ShapeText", "Image", "OleObject", "BlockReference"
    ];
    internal static readonly string[] CapabilityNames =
    [
        "graphic_style", "stroke_style", "start_end_caps", "line_join", "fill",
        "opacity", "rotation", "grip_handles", "rotation_handle", "embedded_content",
        "text_content"
    ];

    internal static object CreateStatistics(
        CadDocument document,
        BlockId activeOwnerBlockId,
        IReadOnlySet<EntityId> selectedEntityIds,
        CadEntityQueryOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateOptions(options);
        var all = new QueryCounts(includeGroups: true);
        var current = new QueryCounts(includeGroups: true);
        var matching = new QueryCounts(includeGroups: true);
        IEnumerable<CadEntity> ObserveScope()
        {
            foreach (var entity in document.Entities.Values)
            {
                if (entity.IsErased)
                    continue;
                all.Add(entity);
                var inCurrent = entity.OwnerBlockId == activeOwnerBlockId;
                if (inCurrent)
                    current.Add(entity);
                if (options.Scope == DocumentScope || inCurrent)
                    yield return entity;
            }
        }
        foreach (var entity in ApplyFilters(document, ObserveScope(), selectedEntityIds, options))
            matching.Add(entity);
        return new
        {
            requested_scope = options.Scope,
            filters_applied = FilterDto(options),
            current_space = current.Summary(document),
            document = all.Summary(document),
            matching = matching.Summary(document)
        };
    }

    internal static object CreatePage(
        CadDocument document,
        BlockId activeOwnerBlockId,
        IReadOnlySet<EntityId> selectedEntityIds,
        CadEntityQueryOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateOptions(options);
        var scope = new QueryCounts();
        var matching = new QueryCounts();
        IEnumerable<CadEntity> ObserveScope()
        {
            var entities = options.Scope == CurrentSpaceScope
                ? document.GetEntitiesInBlock(activeOwnerBlockId)
                : document.Entities.Values;
            foreach (var entity in entities)
            {
                if (entity.IsErased)
                    continue;
                scope.Add(entity);
                yield return entity;
            }
        }
        var candidates = ApplyFilters(document, ObserveScope(), selectedEntityIds, options);
        var page = SelectPage(document, candidates, matching, options);
        return new
        {
            scope = options.Scope,
            filters_applied = FilterDto(options),
            scope_entity_count = scope.Count,
            scope_type_counts = scope.TypeCounts(),
            total_matches = matching.Count,
            matching_type_counts = matching.TypeCounts(),
            returned_count = page.Length,
            offset = options.Offset,
            limit = options.Limit,
            has_more = (long)options.Offset + page.Length < matching.Count,
            sort = new { by = options.SortBy, descending = options.SortDescending },
            entities = page
        };
    }
}
