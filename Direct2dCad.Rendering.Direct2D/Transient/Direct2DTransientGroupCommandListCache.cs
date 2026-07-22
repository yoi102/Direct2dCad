using System.Numerics;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Direct2D.Scene;
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
    private TransientGroupProfileKey _profileKey;
    private ID2D1CommandList? _commandList;
    private bool _buildFailed;
    private bool _disposed;

    public bool Prepare(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadTransientScene? scene,
        CadRenderOptions options,
        Action<CadTransientEntityReference> drawEntityReference,
        Action<CadTransientBlockReference> drawBlockReference,
        bool buildStep)
    {
        ThrowIfDisposed();
        var profileKey = TransientGroupProfileKey.Create(options, viewport.Zoom);
        if (ReferenceEquals(_document, document) &&
            _items is not null &&
            _items.Count == _itemCount &&
            _profileKey.Equals(profileKey) &&
            TryFindGroupByItems(scene, _items, out var existingGroup))
        {
            if (_commandList is not null || _buildFailed || !buildStep)
                return _commandList is null && !_buildFailed;

            _commandList = Record(
                context,
                viewport,
                options,
                existingGroup.Items,
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

        EnsureState(document, group.Items, profileKey);
        if (_commandList is not null || _buildFailed)
            return false;
        if (!buildStep)
            return true;

        _commandList = Record(
            context,
            viewport,
            options,
            group.Items,
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
        if (_commandList is null ||
            !ReferenceEquals(_document, document) ||
            !ReferenceEquals(_items, group.Items) ||
            !_profileKey.Equals(TransientGroupProfileKey.Create(options, viewport.Zoom)))
        {
            return false;
        }

        var previousTransform = context.Transform;
        context.Transform = ToMatrix3x2(group.Transform) * previousTransform;
        try
        {
            context.DrawImage(
                _commandList,
                null,
                null,
                InterpolationMode.Linear,
                CompositeMode.SourceOver);
        }
        finally
        {
            context.Transform = previousTransform;
        }

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
        _profileKey = default;
        _buildFailed = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Clear();
        _disposed = true;
    }

    private static bool TryFindCacheableGroup(
        CadDocument document,
        CadTransientScene? scene,
        out CadTransientGroup group)
    {
        if (scene is not null)
        {
            foreach (var item in scene.Items)
            {
                if (item is CadTransientGroup candidate &&
                    IsCacheable(document, candidate.Items))
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

    private static bool IsCacheable(
        CadDocument document,
        IReadOnlyList<CadTransientItem> items)
    {
        if (items.Count < MinimumReferenceCount)
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
                        entity is CadOleObject)
                    {
                        return false;
                    }
                    break;
                case CadTransientBlockReference blockReference:
                    if (!IsBlockCacheable(
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

    private ID2D1CommandList? Record(
        ID2D1DeviceContext context,
        CadViewport viewport,
        CadRenderOptions options,
        IReadOnlyList<CadTransientItem> items,
        Action<CadTransientEntityReference> drawEntityReference,
        Action<CadTransientBlockReference> drawBlockReference)
    {
        var previousTarget = context.Target;
        var previousTransform = context.Transform;
        var previousAntialiasMode = context.AntialiasMode;
        var previousTextAntialiasMode = context.TextAntialiasMode;
        var previousPrimitiveBlend = context.PrimitiveBlend;
        var commandList = context.CreateCommandList();
        using var realizationScaleScope =
            resourceCache.PushGeometryRealizationScale(viewport.Zoom);
        var isDrawing = false;
        var completed = false;
        try
        {
            context.Target = commandList;
            context.Transform = Matrix3x2.Identity;
            context.AntialiasMode = options.IsAntialiasingEnabled
                ? AntialiasMode.PerPrimitive
                : AntialiasMode.Aliased;
            context.TextAntialiasMode = options.IsTextAntialiasingEnabled
                ? TextAntialiasMode.Default
                : TextAntialiasMode.Aliased;
            context.PrimitiveBlend = PrimitiveBlend.SourceOver;
            context.BeginDraw();
            isDrawing = true;

            foreach (var item in items)
            {
                if (item is CadTransientEntityReference entityReference)
                    drawEntityReference(entityReference);
                else if (item is CadTransientBlockReference blockReference)
                    drawBlockReference(blockReference);
            }

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

    private void EnsureState(
        CadDocument document,
        IReadOnlyList<CadTransientItem> items,
        TransientGroupProfileKey profileKey)
    {
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
        _profileKey = profileKey;
    }

    private static Matrix3x2 ToMatrix3x2(CadMatrixD transform) => new(
        (float)transform.M11,
        (float)transform.M12,
        (float)transform.M21,
        (float)transform.M22,
        (float)transform.OffsetX,
        (float)transform.OffsetY);

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
        bool IsLevelOfDetailEnabled,
        bool KeepStrokeWidthScreenConstant,
        long MinimumScreenStrokeWidthBits)
    {
        public static TransientGroupProfileKey Create(
            CadRenderOptions options,
            double zoom) => new(
                options.ActiveOwnerBlockId,
                BitConverter.DoubleToInt64Bits(Direct2DRenderScaleBucket.Quantize(zoom)),
                options.IsAntialiasingEnabled,
                options.IsTextAntialiasingEnabled,
                options.IsLevelOfDetailEnabled,
                options.KeepStrokeWidthScreenConstant,
                BitConverter.DoubleToInt64Bits(options.MinimumScreenStrokeWidth));
    }
}
