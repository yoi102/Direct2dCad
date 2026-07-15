using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Direct2D;

internal static class Direct2DEntityVisibility
{
    public static IEnumerable<CadEntity> Enumerate(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache resourceCache)
    {
        var dirtyWorldBounds = ResolveDirtyWorldBounds(viewport, options);
        var blockDefinitionPaintBounds = new Dictionary<BlockId, CadRectD>();
        return document.Entities.Values
            .Where(entity =>
                !entity.IsErased &&
                entity.OwnerBlockId.Equals(options.ActiveOwnerBlockId) &&
                entity.IsVisible &&
                !options.HiddenEntityIds.Contains(entity.Id) &&
                (dirtyWorldBounds is null || IntersectsDirtyBounds(
                    document,
                    entity,
                    dirtyWorldBounds.Value,
                    viewport,
                    options,
                    resourceCache,
                    blockDefinitionPaintBounds)) &&
                document.TryGetLayer(entity.LayerId, out var layer) &&
                layer is { IsVisible: true, IsFrozen: false })
            .OrderBy(entity => document.DocumentSettings.LayerDrawingPriority.GetPriority(entity.LayerId))
            .ThenBy(entity => entity.ZIndex)
            .ThenBy(entity => entity.Id.Value);
    }

    private static CadRectD? ResolveDirtyWorldBounds(CadViewport viewport, CadRenderOptions options)
    {
        if (options.DirtyWorldBounds is not { } dirty || dirty.IsEmpty)
            return null;

        var padding = Math.Max(
            options.MinimumScreenStrokeWidth,
            options.KeepStrokeWidthScreenConstant ? 6.0 : 2.0) /
            Math.Max(viewport.Zoom, double.Epsilon);
        return dirty.Inflate(padding);
    }

    private static bool IntersectsDirtyBounds(
        CadDocument document,
        CadEntity entity,
        CadRectD dirtyWorldBounds,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache resourceCache,
        Dictionary<BlockId, CadRectD> blockDefinitionPaintBounds)
    {
        resourceCache.TryGetEntityResources(entity.Id, out var resources);
        var bounds = ResolvePaintBounds(
            document,
            entity,
            resources,
            viewport,
            options,
            resourceCache,
            blockDefinitionPaintBounds,
            []);
        return bounds.Intersects(dirtyWorldBounds) ||
               bounds.Contains(dirtyWorldBounds.Center) ||
               dirtyWorldBounds.Contains(bounds);
    }

    private static CadRectD ResolvePaintBounds(
        CadDocument document,
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket? resources,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache resourceCache,
        Dictionary<BlockId, CadRectD> blockDefinitionPaintBounds,
        HashSet<BlockId> visitedBlocks)
    {
        if (entity is CadBlockReference blockReference)
        {
            return ResolveBlockReferencePaintBounds(
                document,
                blockReference,
                viewport,
                options,
                resourceCache,
                blockDefinitionPaintBounds,
                visitedBlocks);
        }

        var bounds = entity.Bounds;
        if (bounds.IsEmpty)
            return bounds;

        var padding = 0.0;
        if (resources?.StrokeBrush is not null && UsesStrokeWidth(entity))
            padding = Math.Max(padding, ResolveStrokeWidth(resources.StrokeWidth, viewport, options) * 0.5);

        if (resources is { FillBrush: not null } or { HatchBrush: not null })
        {
            padding = Math.Max(
                padding,
                Math.Max(options.MinimumScreenStrokeWidth, 2.0) /
                Math.Max(viewport.Zoom, double.Epsilon));
        }

        return padding > 0 ? bounds.Inflate(padding) : bounds;
    }

    private static CadRectD ResolveBlockReferencePaintBounds(
        CadDocument document,
        CadBlockReference reference,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache resourceCache,
        Dictionary<BlockId, CadRectD> blockDefinitionPaintBounds,
        HashSet<BlockId> visitedBlocks)
    {
        if (!document.TryGetBlock(reference.DefinitionBlockId, out var definition) ||
            definition is null)
        {
            return reference.Bounds;
        }

        var localPaintBounds = ResolveBlockDefinitionPaintBounds(
            document,
            reference.DefinitionBlockId,
            viewport,
            options,
            resourceCache,
            blockDefinitionPaintBounds,
            visitedBlocks);

        if (localPaintBounds.IsEmpty)
            return reference.Bounds;

        return CadBlockTransform.TransformBounds(definition, reference, localPaintBounds)
            .Union(reference.Bounds);
    }

    private static CadRectD ResolveBlockDefinitionPaintBounds(
        CadDocument document,
        BlockId definitionBlockId,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DResourceCache resourceCache,
        Dictionary<BlockId, CadRectD> blockDefinitionPaintBounds,
        HashSet<BlockId> visitedBlocks)
    {
        if (blockDefinitionPaintBounds.TryGetValue(definitionBlockId, out var cachedBounds))
            return cachedBounds;

        if (!visitedBlocks.Add(definitionBlockId))
            return CadRectD.Empty;

        var bounds = CadRectD.Empty;
        try
        {
            foreach (var child in document.GetEntitiesInBlock(definitionBlockId))
            {
                if (child.IsErased ||
                    !child.IsVisible ||
                    options.HiddenEntityIds.Contains(child.Id) ||
                    !document.TryGetLayer(child.LayerId, out var layer) ||
                    layer is not { IsVisible: true, IsFrozen: false })
                {
                    continue;
                }

                Direct2DResourceCache.EntityResourceBucket? childResources = null;
                if (child is not CadBlockReference)
                    resourceCache.TryGetEntityResources(child.Id, out childResources);

                bounds = bounds.Union(ResolvePaintBounds(
                    document,
                    child,
                    childResources,
                    viewport,
                    options,
                    resourceCache,
                    blockDefinitionPaintBounds,
                    visitedBlocks));
            }
        }
        finally
        {
            visitedBlocks.Remove(definitionBlockId);
        }

        blockDefinitionPaintBounds[definitionBlockId] = bounds;
        return bounds;
    }

    private static bool UsesStrokeWidth(CadEntity entity)
    {
        return entity is CadLine or
            CadCircle or
            CadEllipse or
            CadEllipseArc or
            CadRectangle or
            CadArc or
            CadPolyline or
            CadSpline or
            CadShapeText;
    }

    private static float ResolveStrokeWidth(
        float modelStrokeWidth,
        CadViewport viewport,
        CadRenderOptions options)
    {
        var zoom = Math.Max((float)viewport.Zoom, float.Epsilon);
        var strokeWidth = options.KeepStrokeWidthScreenConstant
            ? modelStrokeWidth / zoom
            : modelStrokeWidth;
        return Math.Max(strokeWidth, (float)options.MinimumScreenStrokeWidth / zoom);
    }
}
