using System.Globalization;
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
        var currentSpaceEntities = ActiveEntities(document)
            .Where(entity => entity.OwnerBlockId.Equals(activeOwnerBlockId))
            .ToArray();
        var documentEntities = ActiveEntities(document).ToArray();
        var scopeEntities = ResolveScope(options.Scope, currentSpaceEntities, documentEntities);
        var matchingEntities = ApplyFilters(
            document,
            scopeEntities,
            selectedEntityIds,
            options).ToArray();

        return new
        {
            requested_scope = options.Scope,
            filters_applied = FilterDto(options),
            current_space = Summary(document, currentSpaceEntities),
            document = Summary(document, documentEntities),
            matching = Summary(document, matchingEntities)
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
        var scopeEntities = ResolveScope(
                options.Scope,
                ActiveEntities(document)
                    .Where(entity => entity.OwnerBlockId.Equals(activeOwnerBlockId)),
                ActiveEntities(document))
            .ToArray();
        var matchingEntities = ApplyFilters(
                document,
                scopeEntities,
                selectedEntityIds,
                options)
            .ToArray();
        var orderedEntities = OrderEntities(document, matchingEntities, options);
        var page = orderedEntities
            .Skip(options.Offset)
            .Take(options.Limit)
            .Select(entity => EntityDto(document, entity))
            .ToArray();

        return new
        {
            scope = options.Scope,
            filters_applied = FilterDto(options),
            scope_entity_count = scopeEntities.Length,
            scope_type_counts = TypeCounts(scopeEntities),
            total_matches = matchingEntities.Length,
            matching_type_counts = TypeCounts(matchingEntities),
            returned_count = page.Length,
            offset = options.Offset,
            limit = options.Limit,
            has_more = options.Offset + page.Length < matchingEntities.Length,
            sort = new { by = options.SortBy, descending = options.SortDescending },
            entities = page
        };
    }

    private static IEnumerable<CadEntity> ActiveEntities(CadDocument document) =>
        document.Entities.Values.Where(entity => !entity.IsErased);

    private static IEnumerable<CadEntity> ResolveScope(
        string scope,
        IEnumerable<CadEntity> currentSpaceEntities,
        IEnumerable<CadEntity> documentEntities) => scope switch
    {
        CurrentSpaceScope => currentSpaceEntities,
        DocumentScope => documentEntities,
        _ => throw new ArgumentException($"Unsupported entity query scope: {scope}")
    };

    private static object Summary(CadDocument document, IReadOnlyCollection<CadEntity> entities) => new
    {
        entity_count = entities.Count,
        type_counts = TypeCounts(entities),
        layer_counts = entities
            .GroupBy(entity => entity.LayerId)
            .Select(group => new
            {
                layer_id = group.Key.Value,
                layer = LayerName(document, group.Key),
                count = group.Count()
            })
            .OrderByDescending(item => item.count)
            .ThenBy(item => item.layer, StringComparer.OrdinalIgnoreCase)
            .ToArray(),
        owner_space_counts = entities
            .GroupBy(entity => entity.OwnerBlockId)
            .Select(group =>
            {
                var block = document.TryGetBlock(group.Key, out var value) ? value : null;
                return new
                {
                    owner_block_id = group.Key.Value,
                    owner = block?.Name ?? group.Key.Value.ToString(CultureInfo.InvariantCulture),
                    kind = block?.Kind.ToString() ?? "Unknown",
                    count = group.Count()
                };
            })
            .OrderByDescending(item => item.count)
            .ThenBy(item => item.owner, StringComparer.OrdinalIgnoreCase)
            .ToArray()
    };

    private static object[] TypeCounts(IEnumerable<CadEntity> entities) =>
        entities
            .GroupBy(EntityType, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { type = group.Key, count = group.Count() })
            .OrderByDescending(item => item.count)
            .ThenBy(item => item.type, StringComparer.OrdinalIgnoreCase)
            .Cast<object>()
            .ToArray();
}
