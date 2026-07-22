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
    private const int MaximumPreparedDefinitionKeys = 128;
    private readonly HashSet<BlockId> _visitedBlocks = [];
    private readonly HashSet<BlockId> _cacheabilityVisitedBlocks = [];
    private readonly HashSet<BlockId> _dependencyVisitedBlocks = [];
    private readonly HashSet<BlockId> _recordVisitedBlocks = [];
    private readonly List<BlockVisibilityBuffers> _visibilityBuffers = [];
    private readonly Direct2DBlockDefinitionCommandListCache _definitionCache = new(statistics);
    private readonly List<Direct2DBlockDefinitionCacheRequest> _cacheRequests = [];
    private readonly Dictionary<Direct2DBlockDefinitionCacheKey, RequestCandidate> _requestCandidates = [];
    private readonly Dictionary<BlockId, bool> _definitionCacheability = [];
    private readonly Dictionary<BlockId, IReadOnlySet<EntityId>> _definitionDependencies = [];
    private CadDocument? _preparedDocument;
    private BlockCacheRequestProfileKey _preparedProfileKey;
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

        var profileKey = BlockCacheRequestProfileKey.Create(options, viewport.Zoom);
        if (_requestsDirty ||
            !_hasPreparedProfile ||
            !ReferenceEquals(_preparedDocument, document) ||
            !_preparedProfileKey.Equals(profileKey))
        {
            BuildCacheRequests(document, viewport, options, orderedEntities);
            _preparedDocument = document;
            _preparedProfileKey = profileKey;
            _hasPreparedProfile = true;
            _requestsDirty = false;
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

    public void ApplyChanges(CadDocumentChangeSet changes)
    {
        _definitionCache.ApplyChanges(changes);
        if (!changes.DocumentChanged)
            return;

        _requestsDirty = true;
        _definitionCacheability.Clear();
        _definitionDependencies.Clear();
    }

    public void ClearCache()
    {
        _definitionCache.Clear();
        _cacheRequests.Clear();
        _requestCandidates.Clear();
        _definitionCacheability.Clear();
        _definitionDependencies.Clear();
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
            ReferenceRenderState.From(reference),
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
            new ReferenceRenderState(
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
        ReferenceRenderState reference,
        CadRenderOptions options,
        Direct2DBlockRenderStyle? parentStyle,
        HashSet<BlockId> visited,
        int depth)
    {
        if (!visited.Add(reference.DefinitionBlockId) ||
            !document.TryGetBlock(reference.DefinitionBlockId, out var definition) ||
            definition is null ||
            !TryResolveReferenceStyle(document, reference, parentStyle, out var referenceStyle))
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
                    CreateCacheKey(
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
                if (!IsVisible(document, child, referenceStyle, options))
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
                        ReferenceRenderState.From(nested),
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
                    ? ResolveLayerStrokeWidth(referenceStyle.EffectiveLayer)
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

                var colorOverride = ResolveChildStrokeColor(document, child, referenceStyle);
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
            !TryResolveReferenceStyle(
                document,
                ReferenceRenderState.From(reference),
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

    private static bool TryResolveReferenceStyle(
        CadDocument document,
        ReferenceRenderState reference,
        Direct2DBlockRenderStyle? parentStyle,
        out Direct2DBlockRenderStyle style)
    {
        if (!document.TryGetLayer(reference.LayerId, out var ownLayer) || ownLayer is null)
        {
            style = default;
            return false;
        }

        var effectiveLayer = reference.LayerId.Equals(LayerId.Default) && parentStyle is { } containingStyle
            ? containingStyle.EffectiveLayer
            : ownLayer;
        var layerColor = ResolveLayerStrokeColor(document, effectiveLayer);
        var referenceColor = reference.ColorSource switch
        {
            CadColorSource.Explicit =>
                ResolveGraphicStrokeColor(document, reference.GraphicStyleId) ?? layerColor,
            CadColorSource.ByBlock when parentStyle is { } containingReferenceStyle =>
                containingReferenceStyle.ReferenceColor,
            _ => layerColor
        };

        style = new Direct2DBlockRenderStyle(effectiveLayer, referenceColor);
        return true;
    }

    private static CadColor? ResolveChildStrokeColor(
        CadDocument document,
        CadEntity child,
        Direct2DBlockRenderStyle referenceStyle)
    {
        return child.ColorSource switch
        {
            CadColorSource.ByBlock => referenceStyle.ReferenceColor,
            CadColorSource.ByLayer when child.LayerId.Equals(LayerId.Default) =>
                ResolveLayerStrokeColor(document, referenceStyle.EffectiveLayer),
            _ => null
        };
    }

    private static bool IsVisible(
        CadDocument document,
        CadEntity entity,
        Direct2DBlockRenderStyle referenceStyle,
        CadRenderOptions options)
    {
        var layer = entity.LayerId.Equals(LayerId.Default)
            ? referenceStyle.EffectiveLayer
            : document.TryGetLayer(entity.LayerId, out var childLayer)
                ? childLayer
                : null;

        return !entity.IsErased &&
               entity.IsVisible &&
               !options.HiddenEntityIds.Contains(entity.Id) &&
               layer is { IsVisible: true, IsFrozen: false };
    }

    private static CadColor ResolveLayerStrokeColor(CadDocument document, CadLayer layer)
    {
        return ResolveGraphicStrokeColor(document, layer.DefaultGraphicStyleId) ?? layer.Color;
    }

    private static CadColor? ResolveGraphicStrokeColor(CadDocument document, StyleId? styleId)
    {
        return styleId is { } id &&
               document.TryGetStyle(id, out var style) &&
               style is CadGraphicStyle graphic
            ? graphic.StrokeColor
            : null;
    }

    private static float ResolveLayerStrokeWidth(CadLayer layer)
    {
        var weight = layer.LineWeight;
        if (weight.IsByLayer || weight.Value <= 0)
            weight = CadLineWeight.Default;

        return (float)Math.Max(weight.Value, 0.01);
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

    private void BuildCacheRequests(
        CadDocument document,
        CadViewport viewport,
        CadRenderOptions options,
        IReadOnlyList<CadEntity> orderedEntities)
    {
        _cacheRequests.Clear();
        _requestCandidates.Clear();

        foreach (var entity in orderedEntities)
        {
            if (entity is not CadBlockReference reference ||
                Direct2DEntityLevelOfDetail.Resolve(
                    reference,
                    resources: null,
                    viewport,
                    options) != Direct2DEntityRenderDetail.Full ||
                !TryResolveReferenceStyle(
                    document,
                    ReferenceRenderState.From(reference),
                    parentStyle: null,
                    out var style) ||
                !IsVisible(document, reference, style, options))
            {
                continue;
            }

            _cacheabilityVisitedBlocks.Clear();
            if (!IsDefinitionCacheable(document, reference.DefinitionBlockId))
                continue;

            var key = CreateCacheKey(
                reference.DefinitionBlockId,
                style,
                viewport,
                options,
                Math.Abs(reference.ScaleX) * viewport.Zoom * ResolveScaleMultiplier(options),
                Math.Abs(reference.ScaleY) * viewport.Zoom * ResolveScaleMultiplier(options));
            if (_requestCandidates.TryGetValue(key, out var candidate))
            {
                candidate.ReferenceCount++;
                continue;
            }

            _dependencyVisitedBlocks.Clear();
            var dependencies = ResolveDefinitionDependencies(
                document,
                reference.DefinitionBlockId);
            var buildViewZoom = Direct2DRenderScaleBucket.Quantize(viewport.Zoom);
            var buildScreenScale = Math.Max(
                BitConverter.Int64BitsToDouble(key.ScreenScaleXBits),
                BitConverter.Int64BitsToDouble(key.ScreenScaleYBits));
            var request = new Direct2DBlockDefinitionCacheRequest(
                key,
                style,
                buildViewZoom,
                buildScreenScale,
                dependencies);
            _requestCandidates.Add(key, new RequestCandidate(request));
        }

        foreach (var candidate in _requestCandidates.Values
                     .Where(static candidate => candidate.ReferenceCount >= 2)
                     .OrderByDescending(static candidate => candidate.ReferenceCount)
                     .ThenBy(static candidate => candidate.Request.Key.DefinitionBlockId.Value)
                     .Take(MaximumPreparedDefinitionKeys))
        {
            _cacheRequests.Add(candidate.Request);
        }
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
                if (!IsVisible(document, child, referenceStyle, options))
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
                        !TryResolveReferenceStyle(
                            document,
                            ReferenceRenderState.From(nested),
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
                    ? ResolveLayerStrokeWidth(referenceStyle.EffectiveLayer)
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
                var colorOverride = ResolveChildStrokeColor(document, child, referenceStyle);
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

    private static Direct2DBlockDefinitionCacheKey CreateCacheKey(
        BlockId blockId,
        Direct2DBlockRenderStyle style,
        CadViewport viewport,
        CadRenderOptions options,
        Matrix3x2 localToTarget)
    {
        var multiplier = ResolveScaleMultiplier(options);
        var scaleX = Math.Sqrt(
            localToTarget.M11 * localToTarget.M11 +
            localToTarget.M12 * localToTarget.M12) * multiplier;
        var scaleY = Math.Sqrt(
            localToTarget.M21 * localToTarget.M21 +
            localToTarget.M22 * localToTarget.M22) * multiplier;
        return CreateCacheKey(blockId, style, viewport, options, scaleX, scaleY);
    }

    private static Direct2DBlockDefinitionCacheKey CreateCacheKey(
        BlockId blockId,
        Direct2DBlockRenderStyle style,
        CadViewport viewport,
        CadRenderOptions options,
        double screenScaleX,
        double screenScaleY)
    {
        var viewZoom = Direct2DRenderScaleBucket.Quantize(viewport.Zoom);
        var quantizedScaleX = Direct2DRenderScaleBucket.Quantize(
            Math.Max(screenScaleX, double.Epsilon));
        var quantizedScaleY = Direct2DRenderScaleBucket.Quantize(
            Math.Max(screenScaleY, double.Epsilon));
        return new Direct2DBlockDefinitionCacheKey(
            blockId,
            style.EffectiveLayer.Id,
            style.ReferenceColor,
            BitConverter.DoubleToInt64Bits(ResolveLayerStrokeWidth(style.EffectiveLayer)),
            BitConverter.DoubleToInt64Bits(viewZoom),
            BitConverter.DoubleToInt64Bits(quantizedScaleX),
            BitConverter.DoubleToInt64Bits(quantizedScaleY),
            options.IsAntialiasingEnabled,
            options.IsTextAntialiasingEnabled,
            options.IsLevelOfDetailEnabled,
            options.KeepStrokeWidthScreenConstant,
            BitConverter.DoubleToInt64Bits(options.MinimumScreenStrokeWidth));
    }

    private static double ResolveScaleMultiplier(CadRenderOptions options)
    {
        return double.IsFinite(options.TransformScaleMultiplier) &&
               options.TransformScaleMultiplier > double.Epsilon
            ? options.TransformScaleMultiplier
            : 1.0;
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
        TransformScaleMultiplier = buildScreenScale,
        KeepStrokeWidthScreenConstant = source.KeepStrokeWidthScreenConstant,
        MinimumScreenStrokeWidth = source.MinimumScreenStrokeWidth,
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

    private sealed class RequestCandidate(Direct2DBlockDefinitionCacheRequest request)
    {
        public Direct2DBlockDefinitionCacheRequest Request { get; } = request;
        public int ReferenceCount { get; set; } = 1;
    }

    private readonly record struct BlockCacheRequestProfileKey(
        BlockId OwnerBlockId,
        long ViewZoomBits,
        long TransformScaleMultiplierBits,
        bool IsAntialiasingEnabled,
        bool IsTextAntialiasingEnabled,
        bool IsLevelOfDetailEnabled,
        bool KeepStrokeWidthScreenConstant,
        long MinimumScreenStrokeWidthBits)
    {
        public static BlockCacheRequestProfileKey Create(
            CadRenderOptions options,
            double viewportZoom) => new(
            options.ActiveOwnerBlockId,
            BitConverter.DoubleToInt64Bits(Direct2DRenderScaleBucket.Quantize(viewportZoom)),
            BitConverter.DoubleToInt64Bits(
                Direct2DRenderScaleBucket.Quantize(ResolveScaleMultiplier(options))),
            options.IsAntialiasingEnabled,
            options.IsTextAntialiasingEnabled,
            options.IsLevelOfDetailEnabled,
            options.KeepStrokeWidthScreenConstant,
            BitConverter.DoubleToInt64Bits(options.MinimumScreenStrokeWidth));
    }

    private readonly record struct ReferenceRenderState(
        BlockId DefinitionBlockId,
        CadPointD Position,
        double RotationRadians,
        double ScaleX,
        double ScaleY,
        LayerId LayerId,
        CadColorSource ColorSource,
        StyleId? GraphicStyleId)
    {
        public static ReferenceRenderState From(CadBlockReference reference) => new(
            reference.DefinitionBlockId,
            reference.Position,
            reference.RotationRadians,
            reference.ScaleX,
            reference.ScaleY,
            reference.LayerId,
            reference.ColorSource,
            reference.GraphicStyleId);
    }
}
