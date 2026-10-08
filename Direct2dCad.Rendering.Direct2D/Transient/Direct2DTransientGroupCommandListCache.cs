using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Transient;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Transient;

/// <summary>
/// Retains the stable child content of a large translated transient group. Grip moves
/// then replay one command list while only changing the group's world transform.
/// </summary>
internal sealed class Direct2DTransientGroupCommandListCache(
    Direct2DResourceCache resourceCache) : IDisposable
{
    private const int MinimumReferenceCount = 256;

    private CadDocument? _document;
    private IReadOnlyList<CadTransientItem>? _items;
    private int _itemCount;
    private bool _containsText;
    private TransientGroupProfileKey _profileKey;
    private CadMatrixD _recordingTransformInverse = CadMatrixD.Identity;
    private ID2D1CommandList? _commandList;
    private bool _buildFailed;
    private bool _disposed;

    public long EstimatedBytes => _commandList is null ? 0 : 4096L + (long)_itemCount * 256;

    public bool Prepare(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadTransientScene? scene,
        CadRenderOptions options,
        Action<CadTransientEntityReference, CadRenderOptions> drawEntityReference,
        Action<CadTransientBlockReference, CadRenderOptions> drawBlockReference,
        bool buildStep)
    {
        ThrowIfDisposed();
        if (resourceCache.RequiresPrecision(viewport)) { Clear(); return false; }
        if (ReferenceEquals(_document, document) &&
            _items is not null &&
            _items.Count == _itemCount &&
            TryFindGroupByItems(scene, _items, out var existingGroup) &&
            _profileKey.Equals(TransientGroupProfileKey.Create(options, viewport, existingGroup.Transform, _containsText)))
        {
            if (_commandList is not null || _buildFailed || !buildStep)
                return _commandList is null && !_buildFailed;

            _commandList = Record(
                context,
                viewport,
                options,
                existingGroup,
                drawEntityReference,
                drawBlockReference);
            _buildFailed = _commandList is null;
            return false;
        }

        if (!TryFindCacheableGroup(document, scene, out var group))
        {
            Clear();
            return false;
        }

        var containsText = ContainsText(document, group.Items);
        EnsureState(document, group, containsText,
            TransientGroupProfileKey.Create(options, viewport, group.Transform, containsText));
        if (_commandList is not null || _buildFailed)
            return false;
        if (!buildStep)
            return true;

        _commandList = Record(
            context,
            viewport,
            options,
            group,
            drawEntityReference,
            drawBlockReference);
        _buildFailed = _commandList is null;
        return false;
    }

    public bool TryDraw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadTransientGroup group,
        CadRenderOptions options)
    {
        ThrowIfDisposed();
        if (resourceCache.RequiresPrecision(viewport)) return false;
        if (_commandList is null ||
            !ReferenceEquals(_document, document) ||
            !ReferenceEquals(_items, group.Items) ||
            !_profileKey.Equals(TransientGroupProfileKey.Create(options, viewport, group.Transform, _containsText)))
        {
            return false;
        }

        // Commands retain the original world-to-screen transform so DirectWrite
        // records glyphs at their final pixel size. Cancel it before replaying at
        // the current group position and viewport.
        using var coordinates = Direct2DCoordinateSystem.Push(context, _recordingTransformInverse * group.Transform);
        context.DrawImage(
            _commandList,
            null,
            null,
            InterpolationMode.Linear,
            CompositeMode.SourceOver);
        return true;
    }

    public void ApplyChanges(CadDocumentChangeSet changes)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.DocumentChanged)
            Clear();
    }

    public void Clear()
    {
        _commandList?.Dispose();
        _commandList = null;
        _document = null;
        _items = null;
        _itemCount = 0;
        _containsText = false;
        _profileKey = default;
        _recordingTransformInverse = CadMatrixD.Identity;
        _buildFailed = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Clear();
        _disposed = true;
    }

    private bool TryFindCacheableGroup(
        CadDocument document,
        CadTransientScene? scene,
        out CadTransientGroup group)
    {
        if (scene is not null)
        {
            foreach (var item in scene.Items)
            {
                if (item is CadTransientGroup candidate &&
                    IsCacheable(document, candidate))
                {
                    group = candidate;
                    return true;
                }
            }
        }

        group = null!;
        return false;
    }

    private static bool TryFindGroupByItems(
        CadTransientScene? scene,
        IReadOnlyList<CadTransientItem> items,
        out CadTransientGroup group)
    {
        if (scene is not null)
        {
            foreach (var item in scene.Items)
            {
                if (item is CadTransientGroup candidate &&
                    ReferenceEquals(candidate.Items, items))
                {
                    group = candidate;
                    return true;
                }
            }
        }

        group = null!;
        return false;
    }

    private bool IsCacheable(
        CadDocument document,
        CadTransientGroup group)
    {
        var items = group.Items;
        if (items.Count < MinimumReferenceCount)
            return false;

        var recordingTransform = CreateGroupLinearTransform(group.Transform);
        if (!recordingTransform.TryInvert(out _) ||
            !double.IsFinite(recordingTransform.M11) || !double.IsFinite(recordingTransform.M12) ||
            !double.IsFinite(recordingTransform.M21) || !double.IsFinite(recordingTransform.M22))
            return false;

        var blockCacheability = new Dictionary<BlockId, bool>();
        foreach (var item in items)
        {
            switch (item)
            {
                case CadTransientEntityReference reference:
                    if (!document.TryGetEntity(reference.EntityId, out var entity) ||
                        entity is null ||
                        entity.IsErased ||
                        entity is CadOleObject ||
                        resourceCache.RequiresPrecision(document, entity) ||
                        Direct2DCoordinateSystem.NeedsOrigin(recordingTransform.TransformPoint(
                            entity.Bounds.Center + reference.Offset)))
                    {
                        return false;
                    }
                    break;
                case CadTransientBlockReference blockReference:
                    if (resourceCache.RequiresPrecision(document, blockReference.DefinitionBlockId) ||
                        Direct2DCoordinateSystem.NeedsOrigin(recordingTransform.TransformPoint(blockReference.Position)) ||
                        !IsBlockCacheable(
                            document,
                            blockReference.DefinitionBlockId,
                            blockCacheability,
                            []))
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static bool IsBlockCacheable(
        CadDocument document,
        BlockId blockId,
        Dictionary<BlockId, bool> cache,
        HashSet<BlockId> visitingBlocks)
    {
        if (cache.TryGetValue(blockId, out var cached))
            return cached;
        if (!visitingBlocks.Add(blockId) ||
            !document.TryGetBlock(blockId, out var block) ||
            block is null)
        {
            return false;
        }

        var cacheable = true;
        try
        {
            foreach (var entity in document.GetEntitiesInBlock(blockId))
            {
                if (entity is CadOleObject ||
                    entity is CadBlockReference nested &&
                    !IsBlockCacheable(
                        document,
                        nested.DefinitionBlockId,
                        cache,
                        visitingBlocks))
                {
                    cacheable = false;
                    break;
                }
            }
        }
        finally
        {
            visitingBlocks.Remove(blockId);
        }

        cache[blockId] = cacheable;
        return cacheable;
    }

    private static bool ContainsText(CadDocument document, IReadOnlyList<CadTransientItem> items)
    {
        var blocksWithText = new Dictionary<BlockId, bool>();
        var visitingBlocks = new HashSet<BlockId>();
        foreach (var item in items)
        {
            if (item is CadTransientEntityReference reference &&
                document.TryGetEntity(reference.EntityId, out var entity) && entity is not null &&
                HasText(entity) ||
                item is CadTransientBlockReference blockReference &&
                BlockHasText(blockReference.DefinitionBlockId))
                return true;
        }
        return false;

        bool HasText(CadEntity entity) => entity is CadText ||
            entity is CadBlockReference reference && BlockHasText(reference.DefinitionBlockId);

        bool BlockHasText(BlockId blockId)
        {
            if (blocksWithText.TryGetValue(blockId, out var cached)) return cached;
            if (!visitingBlocks.Add(blockId)) return false;
            var containsText = document.GetEntitiesInBlock(blockId).Any(HasText);
            visitingBlocks.Remove(blockId);
            blocksWithText[blockId] = containsText;
            return containsText;
        }
    }

    private ID2D1CommandList? Record(
        ID2D1DeviceContext context,
        CadViewport viewport,
        CadRenderOptions options,
        CadTransientGroup group,
        Action<CadTransientEntityReference, CadRenderOptions> drawEntityReference,
        Action<CadTransientBlockReference, CadRenderOptions> drawBlockReference)
    {
        var previousTarget = context.Target;
        var previousTransform = context.Transform;
        var previousAntialiasMode = context.AntialiasMode;
        var previousTextAntialiasMode = context.TextAntialiasMode;
        var previousPrimitiveBlend = context.PrimitiveBlend;
        var commandList = context.CreateCommandList();
        var recordingTransform = CreateRecordingTransform(group.Transform, viewport);
        var buildOptions = CreateBuildOptions(options);
        using var realizationScaleScope =
            resourceCache.PushGeometryRealizationScale(buildOptions.TransformScaleMultiplier);
        var isDrawing = false;
        var completed = false;
        try
        {
            context.Target = commandList;
            using var recordingCoordinates = Direct2DCoordinateSystem.PushAbsolute(
                context, recordingTransform);
            context.AntialiasMode = options.IsAntialiasingEnabled
                ? AntialiasMode.PerPrimitive
                : AntialiasMode.Aliased;
            context.TextAntialiasMode = options.IsTextAntialiasingEnabled
                ? TextAntialiasMode.Default
                : TextAntialiasMode.Aliased;
            context.PrimitiveBlend = PrimitiveBlend.SourceOver;
            context.BeginDraw();
            isDrawing = true;

            foreach (var item in group.Items)
            {
                if (item is CadTransientEntityReference entityReference)
                    drawEntityReference(entityReference, buildOptions);
                else if (item is CadTransientBlockReference blockReference)
                    drawBlockReference(blockReference, buildOptions);
            }

            var result = context.EndDraw();
            isDrawing = false;
            if (result.Failure)
                return null;

            context.Target = previousTarget;
            commandList.Close();
            _recordingTransformInverse = recordingTransform.Invert();
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

    private void EnsureState(
        CadDocument document,
        CadTransientGroup group,
        bool containsText,
        TransientGroupProfileKey profileKey)
    {
        var items = group.Items;
        if (ReferenceEquals(_document, document) &&
            ReferenceEquals(_items, items) &&
            _profileKey.Equals(profileKey))
        {
            return;
        }

        Clear();
        _document = document;
        _items = items;
        _itemCount = items.Count;
        _containsText = containsText;
        _profileKey = profileKey;
    }

    private static CadMatrixD CreateGroupLinearTransform(CadMatrixD transform) =>
        new(transform.M11, transform.M12, transform.M21, transform.M22, 0, 0);

    private static CadMatrixD CreateRecordingTransform(CadMatrixD groupTransform, CadViewport viewport) =>
        groupTransform * Direct2DCoordinateSystem.ViewportTransform(viewport);

    private static CadRenderOptions CreateBuildOptions(CadRenderOptions source) => new()
    {
        ActiveOwnerBlockId = source.ActiveOwnerBlockId,
        ActiveLayoutId = source.ActiveLayoutId,
        ActiveLayoutViewportId = source.ActiveLayoutViewportId,
        DrawGrid = false,
        DrawOrigin = false,
        DrawGripHandles = false,
        IsAntialiasingEnabled = source.IsAntialiasingEnabled,
        IsTextAntialiasingEnabled = source.IsTextAntialiasingEnabled,
        IsLevelOfDetailEnabled = source.IsLevelOfDetailEnabled,
        EnableGeometryRealizations = source.EnableGeometryRealizations,
        // LOD, proxy sizes and glyph recording use the same context transform as
        // the final draw, so only retain the caller-supplied scale multiplier.
        TransformScaleMultiplier = source.TransformScaleMultiplier,
        KeepStrokeWidthScreenConstant = source.KeepStrokeWidthScreenConstant,
        MinimumScreenStrokeWidth = source.MinimumScreenStrokeWidth,
        EntityLineWeightWorldScale = source.EntityLineWeightWorldScale
    };

    private static double ResolveTransformScaleMultiplier(double value) =>
        double.IsFinite(value) && value > double.Epsilon ? value : 1.0;

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DTransientGroupCommandListCache));
    }

    private readonly record struct TransientGroupProfileKey(
        BlockId OwnerBlockId,
        long ZoomBits,
        bool IsAntialiasingEnabled,
        bool IsTextAntialiasingEnabled,
        bool EnableGeometryRealizations,
        bool IsLevelOfDetailEnabled,
        long TransformScaleMultiplierBits,
        bool KeepStrokeWidthScreenConstant,
        long MinimumScreenStrokeWidthBits,
        long EntityLineWeightWorldScaleBits,
        CadMatrixD RecordingTransform,
        long TextPixelPhaseXBits,
        long TextPixelPhaseYBits)
    {
        public static TransientGroupProfileKey Create(
            CadRenderOptions options,
            CadViewport viewport,
            CadMatrixD groupTransform,
            bool containsText)
        {
            var recordingTransform = CreateRecordingTransform(groupTransform, viewport);
            return new(
                options.ActiveOwnerBlockId,
                BitConverter.DoubleToInt64Bits(viewport.Zoom),
                options.IsAntialiasingEnabled,
                options.IsTextAntialiasingEnabled,
                options.EnableGeometryRealizations,
                options.IsLevelOfDetailEnabled,
                BitConverter.DoubleToInt64Bits(
                    ResolveTransformScaleMultiplier(options.TransformScaleMultiplier)),
                options.KeepStrokeWidthScreenConstant,
                BitConverter.DoubleToInt64Bits(options.MinimumScreenStrokeWidth),
                BitConverter.DoubleToInt64Bits(options.EntityLineWeightWorldScale),
                CreateGroupLinearTransform(groupTransform),
                containsText ? PixelPhaseBits(recordingTransform.OffsetX) : 0,
                containsText ? PixelPhaseBits(recordingTransform.OffsetY) : 0);
        }

        // DirectWrite snaps glyph baselines while recording. Reuse integer pixel
        // translations, but re-record if their fractional screen phase changes.
        private static long PixelPhaseBits(double value) =>
            BitConverter.DoubleToInt64Bits(value - Math.Floor(value));
    }
}
