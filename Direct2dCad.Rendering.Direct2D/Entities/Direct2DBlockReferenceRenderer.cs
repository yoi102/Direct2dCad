using System.Numerics;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Ole;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Direct2D.Scene;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Entities;

internal sealed class Direct2DBlockReferenceRenderer(
    Direct2DResourceCache resourceCache,
    Direct2DEntityRenderer entityRenderer,
    Direct2DOleRenderer oleRenderer,
    Direct2DStyleResourceCache styleResources,
    Direct2DEntityOrderCache entityOrderCache,
    Direct2DRenderStatisticsCollector statistics) : IDisposable
{
    private readonly HashSet<BlockId> _visitedBlocks = [];
    private readonly HashSet<BlockId> _cacheabilityVisitedBlocks = [];
    private readonly HashSet<BlockId> _dependencyVisitedBlocks = [];
    private readonly HashSet<BlockId> _recordVisitedBlocks = [];
    private readonly List<BlockVisibilityBuffers> _visibilityBuffers = [];
    private readonly Direct2DBlockDefinitionCommandListCache _definitionCache = new(statistics);
    private readonly List<Direct2DBlockDefinitionCacheRequest> _cacheRequests = [];
    private readonly Dictionary<BlockId, bool> _definitionCacheability = [];
    private readonly Dictionary<BlockId, IReadOnlySet<EntityId>> _definitionDependencies = [];
    private readonly HashSet<BlockId> _staleDefinitionIds = [];
    private Direct2DBlockCacheRequestPlanner? _requestPlanBuilder;
    private CadDocument? _preparedDocument;
    private Direct2DBlockCacheRequestProfileKey _preparedProfileKey;
    private bool _hasPreparedProfile;
    private bool _requestsDirty = true;

    public long EstimatedCacheBytes => _definitionCache.EstimatedBytes;
    public static long CacheBudgetBytes => Direct2DBlockDefinitionCommandListCache.CacheBudgetBytes;

    public bool PrepareCache(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        IReadOnlyList<CadEntity> orderedEntities,
        bool buildStep)
    {
        if (options.HiddenEntityIds.Count > 0)
            return false;

        var profileKey = Direct2DBlockCacheRequestProfileKey.Create(
            options,
            viewport.Zoom);
        if (PrepareRequestPlan(
                document,
                viewport,
                options,
                orderedEntities,
                profileKey,
                buildStep))
        {
            return true;
        }

        ID2D1CommandList? Record(
            Direct2DBlockDefinitionCacheRequest request,
            out int recordedEntityCount) =>
            RecordDefinition(
                context,
                document,
                viewport,
                options,
                request,
                out recordedEntityCount);

        return _definitionCache.Prepare(_cacheRequests, buildStep, Record);
    }

    public void ApplyChanges(CadDocument document, CadDocumentChangeSet changes)
    {
        ArgumentNullException.ThrowIfNull(document);
        _definitionCache.ApplyChanges(changes);
        if (!changes.DocumentChanged)
            return;

        if (changes.AffectsDocumentStructure || changes.AffectsLayerOrder)
        {
            InvalidateAllRequestMetadata();
            return;
        }

        foreach (var change in changes.EntityChanges)
        {
            if ((change.Kind & (CadEntityChangeKind.Created |
                                CadEntityChangeKind.Deleted)) != 0)
            {
                InvalidateAllRequestMetadata();
                return;
            }
        }

        var hasFillChange = changes.EntityChanges.Any(
            static change => (change.Kind & CadEntityChangeKind.Fill) != 0);
        var affectedDefinitionMetadata = InvalidateAffectedDefinitionMetadata(
            changes.EntityChanges,
            removeMetadata: hasFillChange);

        var changedBlockReference = changes.EntityChanges.Any(change =>
            document.TryGetEntity(change.EntityId, out var entity) &&
            entity is CadBlockReference);
        if (changedBlockReference || hasFillChange && affectedDefinitionMetadata)
            MarkRequestPlanDirty();
    }

    public void ClearCache()
    {
        _definitionCache.Clear();
        _cacheRequests.Clear();
        _definitionCacheability.Clear();
        _definitionDependencies.Clear();
        _staleDefinitionIds.Clear();
        _requestPlanBuilder = null;
        _preparedDocument = null;
        _hasPreparedProfile = false;
        _requestsDirty = true;
    }

    public void Draw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadBlockReference reference,
        CadRenderOptions options)
    {
        _visitedBlocks.Clear();
        var detail = Direct2DEntityLevelOfDetail.Resolve(
            reference,
            resources: null,
            context.Transform,
            options);
        if (detail == Direct2DEntityRenderDetail.Skip)
            return;
        if (TryDrawProxy(
                context,
                document,
                reference,
                options,
                parentStyle: null,
                detail))
        {
            return;
        }

        Draw(
            context,
            document,
            viewport,
            Direct2DBlockReferenceRenderState.From(reference),
            options,
            parentStyle: null,
            _visitedBlocks,
            depth: 0);
    }

    public void Draw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        BlockId definitionBlockId,
        CadPointD position,
        double rotationRadians,
        double scaleX,
        double scaleY,
        LayerId layerId,
        CadColorSource colorSource,
        StyleId? graphicStyleId,
        CadRenderOptions options)
    {
        _visitedBlocks.Clear();
        Draw(
            context,
            document,
            viewport,
            new Direct2DBlockReferenceRenderState(
                definitionBlockId,
                position,
                rotationRadians,
                scaleX,
                scaleY,
                layerId,
                colorSource,
                graphicStyleId),
            options,
            parentStyle: null,
            _visitedBlocks,
            depth: 0);
    }

    private void Draw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        Direct2DBlockReferenceRenderState reference,
        CadRenderOptions options,
        Direct2DBlockRenderStyle? parentStyle,
        HashSet<BlockId> visited,
        int depth)
    {
        if (!visited.Add(reference.DefinitionBlockId) ||
            !document.TryGetBlock(reference.DefinitionBlockId, out var definition) ||
            definition is null ||
            !Direct2DBlockReferenceStyleResolver.TryResolve(
                document,
                reference,
                parentStyle,
                out var referenceStyle))
        {
            return;
        }

        var previousTransform = context.Transform;
        context.Transform = CreateTransform(
            definition.BasePoint,
            reference.Position,
            reference.RotationRadians,
            reference.ScaleX,
            reference.ScaleY) * previousTransform;
        try
        {
            if (options.HiddenEntityIds.Count == 0 &&
                _definitionCache.TryDraw(
                    context,
                    Direct2DBlockCacheKeyFactory.Create(
                        reference.DefinitionBlockId,
                        referenceStyle,
                        viewport,
                        options,
                        context.Transform)))
            {
                return;
            }

            var visibilityBuffers = GetVisibilityBuffers(depth);
            foreach (var child in Direct2DBlockEntityVisibility.Resolve(
                         document,
                         reference.DefinitionBlockId,
                         context.Transform,
                         viewport,
                         options,
                         entityOrderCache,
                         visibilityBuffers.CandidateIds,
                         visibilityBuffers.Candidates,
                         visibilityBuffers.OrderedCandidates,
                         visibilityBuffers.CandidateSet,
                         visibilityBuffers.RankedEntities))
            {
                if (!Direct2DBlockReferenceStyleResolver.IsVisible(
                        document,
                        child,
                        referenceStyle,
                        options))
                    continue;

                if (child is CadBlockReference nested)
                {
                    var detail = Direct2DEntityLevelOfDetail.Resolve(
                        nested,
                        resources: null,
                        context.Transform,
                        options);
                    if (detail == Direct2DEntityRenderDetail.Skip)
                        continue;

                    statistics.RecordExpandedBlockEntity();
                    statistics.RecordEntitySubmission();
                    statistics.RecordBlockReference();
                    if (TryDrawProxy(
                            context,
                            document,
                            nested,
                            options,
                            referenceStyle,
                            detail))
                    {
                        continue;
                    }

                    Draw(
                        context,
                        document,
                        viewport,
                        Direct2DBlockReferenceRenderState.From(nested),
                        options,
                        referenceStyle,
                        visited,
                        depth + 1);
                    continue;
                }

                if (child is CadOleObject oleObject)
                {
                    if (Direct2DEntityLevelOfDetail.ResolveOle(
                            oleObject.Bounds,
                            context.Transform,
                            options) == Direct2DEntityRenderDetail.Skip)
                    {
                        continue;
                    }

                    statistics.RecordExpandedBlockEntity();
                    statistics.RecordEntitySubmission();
                    CadColor? proxyColor = child.LayerId.Equals(LayerId.Default)
                        ? referenceStyle.ReferenceColor
                        : null;
                    oleRenderer.DrawEntity(
                        context,
                        document,
                        oleObject,
                        viewport,
                        options,
                        proxyColor);
                    continue;
                }

                if (!resourceCache.TryGetEntityResources(child.Id, out var resources) || resources is null)
                    continue;
                float? strokeWidthOverride = child.UseLayerLineWeight && child.LayerId.Equals(LayerId.Default)
                    ? Direct2DBlockReferenceStyleResolver.ResolveLayerStrokeWidth(
                        referenceStyle.EffectiveLayer)
                    : null;
                if (Direct2DEntityLevelOfDetail.Resolve(
                        child,
                        resources,
                        context.Transform,
                        options,
                        strokeWidthOverride) == Direct2DEntityRenderDetail.Skip)
                {
                    continue;
                }

                statistics.RecordExpandedBlockEntity();
                statistics.RecordEntitySubmission();

                var colorOverride =
                    Direct2DBlockReferenceStyleResolver.ResolveChildStrokeColor(
                        document,
                        child,
                        referenceStyle);
                var brushOverride = colorOverride is { } color
                    ? styleResources.GetBrush(context, color)
                    : null;

                entityRenderer.Draw(
                    context,
                    document,
                    child,
                    resources,
                    viewport,
                    options,
                    brushOverride,
                    strokeWidthOverride,
                    colorOverride);
            }
        }
        finally
        {
            context.Transform = previousTransform;
            visited.Remove(reference.DefinitionBlockId);
        }
    }

    private bool TryDrawProxy(
        ID2D1DeviceContext context,
        CadDocument document,
        CadBlockReference reference,
        CadRenderOptions options,
        Direct2DBlockRenderStyle? parentStyle,
        Direct2DEntityRenderDetail? resolvedDetail = null)
    {
        var detail = resolvedDetail ?? Direct2DEntityLevelOfDetail.Resolve(
            reference,
            resources: null,
            context.Transform,
            options);
        if (detail != Direct2DEntityRenderDetail.Simplified ||
            !Direct2DBlockReferenceStyleResolver.TryResolve(
                document,
                Direct2DBlockReferenceRenderState.From(reference),
                parentStyle,
                out var referenceStyle))
        {
            return false;
        }

        var brush = styleResources.GetBrush(context, referenceStyle.ReferenceColor);
        Direct2DEntityRenderer.DrawRectangularProxy(
            context,
            reference.Bounds,
            brush,
            options.TransformScaleMultiplier);
        return true;
    }

    private static Matrix3x2 CreateTransform(
        CadPointD basePoint,
        CadPointD position,
        double rotationRadians,
        double scaleX,
        double scaleY)
    {
        return Matrix3x2.CreateTranslation((float)-basePoint.X, (float)-basePoint.Y) *
               Matrix3x2.CreateScale((float)scaleX, (float)scaleY) *
               Matrix3x2.CreateRotation((float)rotationRadians) *
               Matrix3x2.CreateTranslation((float)position.X, (float)position.Y);
    }

    private bool PrepareRequestPlan(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        IReadOnlyList<CadEntity> orderedEntities,
        Direct2DBlockCacheRequestProfileKey profileKey,
        bool buildStep)
    {
        var needsBuild = _requestsDirty ||
                         !_hasPreparedProfile ||
                         !ReferenceEquals(_preparedDocument, document) ||
                         !_preparedProfileKey.Equals(profileKey);
        if (!needsBuild)
            return false;

        if (_requestPlanBuilder is null ||
            !_requestPlanBuilder.Matches(document, profileKey, orderedEntities))
        {
            _requestPlanBuilder = new Direct2DBlockCacheRequestPlanner(
                document,
                viewport,
                options,
                orderedEntities,
                profileKey,
                blockId =>
                {
                    _cacheabilityVisitedBlocks.Clear();
                    return IsDefinitionCacheable(document, blockId);
                },
                blockId =>
                {
                    _dependencyVisitedBlocks.Clear();
                    return ResolveDefinitionDependencies(document, blockId);
                });
        }

        if (!buildStep)
            return true;
        if (!_requestPlanBuilder.BuildStep())
            return true;

        _cacheRequests.Clear();
        _cacheRequests.AddRange(_requestPlanBuilder.Requests);
        _requestPlanBuilder = null;
        _preparedDocument = document;
        _preparedProfileKey = profileKey;
        _hasPreparedProfile = true;
        _requestsDirty = false;
        return false;
    }

    private void MarkRequestPlanDirty()
    {
        _requestsDirty = true;
        _requestPlanBuilder = null;
    }

    private void InvalidateAllRequestMetadata()
    {
        MarkRequestPlanDirty();
        _definitionCacheability.Clear();
        _definitionDependencies.Clear();
        _staleDefinitionIds.Clear();
    }

    private bool InvalidateAffectedDefinitionMetadata(
        IReadOnlyList<CadEntityChange> changes,
        bool removeMetadata)
    {
        _staleDefinitionIds.Clear();
        foreach (var pair in _definitionDependencies)
        {
            foreach (var change in changes)
            {
                if (!pair.Value.Contains(change.EntityId))
                    continue;
                _staleDefinitionIds.Add(pair.Key);
                break;
            }
        }

        _definitionCache.InvalidateFailures(_staleDefinitionIds);
        if (!removeMetadata)
            return _staleDefinitionIds.Count > 0;

        foreach (var blockId in _staleDefinitionIds)
        {
            _definitionCacheability.Remove(blockId);
            _definitionDependencies.Remove(blockId);
        }

        return _staleDefinitionIds.Count > 0;
    }

    private bool IsDefinitionCacheable(CadDocument document, BlockId blockId)
    {
        if (_definitionCacheability.TryGetValue(blockId, out var cached))
            return cached;
        if (!_cacheabilityVisitedBlocks.Add(blockId) ||
            !document.TryGetBlock(blockId, out var definition) ||
            definition is null)
        {
            return false;
        }

        var cacheable = true;
        try
        {
            foreach (var child in entityOrderCache.GetOrderedEntities(document, definition.Id))
            {
                if (child is CadOleObject)
                {
                    cacheable = false;
                    break;
                }

                if (child is CadBlockReference nested)
                {
                    if (!IsDefinitionCacheable(document, nested.DefinitionBlockId))
                    {
                        cacheable = false;
                        break;
                    }

                    continue;
                }

                if (!resourceCache.TryGetEntityResources(child.Id, out var resources) ||
                    resources is null ||
                    resources.HatchBrush is not null)
                {
                    cacheable = false;
                    break;
                }
            }
        }
        finally
        {
            _cacheabilityVisitedBlocks.Remove(blockId);
        }

        _definitionCacheability[blockId] = cacheable;
        return cacheable;
    }

    private IReadOnlySet<EntityId> ResolveDefinitionDependencies(
        CadDocument document,
        BlockId blockId)
    {
        if (_definitionDependencies.TryGetValue(blockId, out var cached))
            return cached;
        if (entityOrderCache.GetPreparedDependencyEntityIds(document, blockId) is { } prepared)
        {
            _definitionDependencies[blockId] = prepared;
            return prepared;
        }
        if (!_dependencyVisitedBlocks.Add(blockId))
            return new HashSet<EntityId>();

        var dependencies = new HashSet<EntityId>();
        foreach (var child in entityOrderCache.GetOrderedEntities(document, blockId))
        {
            dependencies.Add(child.Id);
            if (child is not CadBlockReference nested)
                continue;
            foreach (var dependency in ResolveDefinitionDependencies(
                         document,
                         nested.DefinitionBlockId))
            {
                dependencies.Add(dependency);
            }
        }

        _dependencyVisitedBlocks.Remove(blockId);
        _definitionDependencies[blockId] = dependencies;
        return dependencies;
    }

    private ID2D1CommandList? RecordDefinition(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        Direct2DBlockDefinitionCacheRequest request,
        out int recordedEntityCount)
    {
        recordedEntityCount = 0;
        var previousTarget = context.Target;
        var previousTransform = context.Transform;
        var previousAntialiasMode = context.AntialiasMode;
        var previousTextAntialiasMode = context.TextAntialiasMode;
        var previousPrimitiveBlend = context.PrimitiveBlend;
        var commandList = context.CreateCommandList();
        using var realizationScaleScope =
            resourceCache.PushGeometryRealizationScale(request.BuildScreenScale);
        var buildViewport = CreateBuildViewport(viewport, request.BuildViewZoom);
        var buildOptions = CreateBuildOptions(options, request.BuildScreenScale);
        var isDrawing = false;
        var completed = false;
        try
        {
            context.Target = commandList;
            context.Transform = Matrix3x2.Identity;
            context.AntialiasMode = request.Key.IsAntialiasingEnabled
                ? AntialiasMode.PerPrimitive
                : AntialiasMode.Aliased;
            context.TextAntialiasMode = request.Key.IsTextAntialiasingEnabled
                ? TextAntialiasMode.Default
                : TextAntialiasMode.Aliased;
            context.PrimitiveBlend = PrimitiveBlend.SourceOver;
            context.BeginDraw();
            isDrawing = true;

            _recordVisitedBlocks.Clear();
            RecordDefinitionContent(
                context,
                document,
                buildViewport,
                buildOptions,
                request.Key.DefinitionBlockId,
                request.Style,
                _recordVisitedBlocks,
                ref recordedEntityCount);

            var result = context.EndDraw();
            isDrawing = false;
            if (result.Failure)
                return null;

            context.Target = previousTarget;
            commandList.Close();
            completed = true;
            return commandList;
        }
        finally
        {
            if (isDrawing)
                context.EndDraw();
            context.Target = previousTarget;
            context.PrimitiveBlend = previousPrimitiveBlend;
            context.TextAntialiasMode = previousTextAntialiasMode;
            context.AntialiasMode = previousAntialiasMode;
            context.Transform = previousTransform;
            if (!completed)
                commandList.Dispose();
        }
    }

    private void RecordDefinitionContent(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        BlockId blockId,
        Direct2DBlockRenderStyle referenceStyle,
        HashSet<BlockId> visited,
        ref int recordedEntityCount)
    {
        if (!visited.Add(blockId) ||
            !document.TryGetBlock(blockId, out var definition) ||
            definition is null)
        {
            return;
        }

        try
        {
            foreach (var child in entityOrderCache.GetOrderedEntities(document, definition.Id))
            {
                if (!Direct2DBlockReferenceStyleResolver.IsVisible(
                        document,
                        child,
                        referenceStyle,
                        options))
                    continue;

                if (child is CadBlockReference nested)
                {
                    var detail = Direct2DEntityLevelOfDetail.Resolve(
                        nested,
                        resources: null,
                        context.Transform,
                        options);
                    if (detail == Direct2DEntityRenderDetail.Skip)
                    {
                        continue;
                    }

                    recordedEntityCount++;
                    if (TryDrawProxy(
                            context,
                            document,
                            nested,
                            options,
                            referenceStyle,
                            detail) ||
                        !Direct2DBlockReferenceStyleResolver.TryResolve(
                            document,
                            Direct2DBlockReferenceRenderState.From(nested),
                            referenceStyle,
                            out var nestedStyle) ||
                        !document.TryGetBlock(nested.DefinitionBlockId, out var nestedDefinition) ||
                        nestedDefinition is null)
                    {
                        continue;
                    }

                    var previousTransform = context.Transform;
                    context.Transform = CreateTransform(
                        nestedDefinition.BasePoint,
                        nested.Position,
                        nested.RotationRadians,
                        nested.ScaleX,
                        nested.ScaleY) * previousTransform;
                    try
                    {
                        RecordDefinitionContent(
                            context,
                            document,
                            viewport,
                            options,
                            nested.DefinitionBlockId,
                            nestedStyle,
                            visited,
                            ref recordedEntityCount);
                    }
                    finally
                    {
                        context.Transform = previousTransform;
                    }

                    continue;
                }

                if (child is CadOleObject ||
                    !resourceCache.TryGetEntityResources(child.Id, out var resources) ||
                    resources is null)
                {
                    continue;
                }

                float? strokeWidthOverride = child.UseLayerLineWeight &&
                                             child.LayerId.Equals(LayerId.Default)
                    ? Direct2DBlockReferenceStyleResolver.ResolveLayerStrokeWidth(
                        referenceStyle.EffectiveLayer)
                    : null;
                if (Direct2DEntityLevelOfDetail.Resolve(
                        child,
                        resources,
                        context.Transform,
                        options,
                        strokeWidthOverride) == Direct2DEntityRenderDetail.Skip)
                {
                    continue;
                }

                recordedEntityCount++;
                var colorOverride =
                    Direct2DBlockReferenceStyleResolver.ResolveChildStrokeColor(
                        document,
                        child,
                        referenceStyle);
                var brushOverride = colorOverride is { } color
                    ? styleResources.GetBrush(context, color)
                    : null;
                entityRenderer.Draw(
                    context,
                    document,
                    child,
                    resources,
                    viewport,
                    options,
                    brushOverride,
                    strokeWidthOverride,
                    colorOverride);
            }
        }
        finally
        {
            visited.Remove(blockId);
        }
    }

    private static CadViewport CreateBuildViewport(CadViewport source, double zoom)
    {
        var viewport = new CadViewport();
        viewport.SetSize(source.ViewWidth, source.ViewHeight);
        viewport.SetView(zoom, source.Offset);
        return viewport;
    }

    private static CadRenderOptions CreateBuildOptions(
        CadRenderOptions source,
        double buildScreenScale) => new()
        {
            ActiveOwnerBlockId = source.ActiveOwnerBlockId,
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false,
            IsAntialiasingEnabled = source.IsAntialiasingEnabled,
            IsTextAntialiasingEnabled = source.IsTextAntialiasingEnabled,
            IsLevelOfDetailEnabled = source.IsLevelOfDetailEnabled,
            AllowApproximateTileScaleFallback = source.AllowApproximateTileScaleFallback,
            EnableGeometryRealizations = source.EnableGeometryRealizations,
            TransformScaleMultiplier = buildScreenScale,
            KeepStrokeWidthScreenConstant = source.KeepStrokeWidthScreenConstant,
            MinimumScreenStrokeWidth = source.MinimumScreenStrokeWidth,
            EntityLineWeightWorldScale = source.EntityLineWeightWorldScale,
            HiddenEntityIds = CadRenderOptions.NoHiddenEntities
        };

    private BlockVisibilityBuffers GetVisibilityBuffers(int depth)
    {
        while (_visibilityBuffers.Count <= depth)
            _visibilityBuffers.Add(new BlockVisibilityBuffers());
        return _visibilityBuffers[depth];
    }

    private sealed class BlockVisibilityBuffers
    {
        public List<EntityId> CandidateIds { get; } = new(256);
        public List<CadEntity> Candidates { get; } = new(256);
        public List<CadEntity> OrderedCandidates { get; } = new(256);
        public HashSet<EntityId> CandidateSet { get; } = [];
        public List<Direct2DEntityOrderCache.RankedEntity> RankedEntities { get; } = new(256);
    }

    public void Dispose() => _definitionCache.Dispose();
}
