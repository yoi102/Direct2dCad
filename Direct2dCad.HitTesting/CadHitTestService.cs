using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.HitTesting;

/// <summary>
/// CAD HitTest / Bounds 解析服务。
/// </summary>
public sealed class CadHitTestService
{
    private readonly CadDocument _cadDocument;
    private MaxStrokeHitPaddingCacheKey? _maxStrokeHitPaddingCacheKey;
    private double _maxStrokeHitPaddingCache;

    public CadHitTestService(CadDocument cadDocument)
    {
        _cadDocument = cadDocument
            ?? throw new ArgumentNullException(nameof(cadDocument));
    }

    #region HitTest

    public bool HitTestEntityEdge(
        EntityId entityId,
        CadPointD point,
        double tolerance,
        out CadHitTestResult result)
    {
        return HitTestEntityEdge(
            entityId,
            point,
            tolerance,
            CadHitTestOptions.Default,
            out result);
    }

    public bool HitTestEntityEdge(
        EntityId entityId,
        CadPointD point,
        double tolerance,
        CadHitTestOptions options,
        out CadHitTestResult result)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!_cadDocument.TryGetEntity(entityId, out var entity) ||
            entity is null ||
            !CadEntityAccessPolicy.IsSelectable(_cadDocument, entity))
        {
            result = default;
            return false;
        }

        return CadEntityHitTester.HitTestEdge(
            _cadDocument,
            entity,
            point,
            tolerance,
            options,
            out result);
    }

    public bool HitTestEntityFill(
        EntityId entityId,
        CadPointD point,
        out CadHitTestResult result)
    {
        if (!_cadDocument.TryGetEntity(entityId, out var entity) ||
            entity is null ||
            !CadEntityAccessPolicy.IsSelectable(_cadDocument, entity))
        {
            result = default;
            return false;
        }

        return CadEntityHitTester.HitTestFill(
            _cadDocument,
            entity,
            point,
            out result);
    }

    #endregion HitTest

    #region Resolved Bounds

    /// <summary>
    /// 获取实体的真实 Bounds。
    ///
    /// 普通实体直接返回 entity.Bounds。
    /// BlockReference 会递归解析其 DefinitionBlockId 内部实体，
    /// 再应用 Position / Rotation / Scale / BasePoint 变换。
    /// </summary>
    public CadRectD GetResolvedEntityBounds(EntityId entityId)
    {
        var entity = _cadDocument.GetEntity(entityId);
        return GetResolvedEntityBounds(entity);
    }

    public CadRectD GetResolvedEntityBounds(CadEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return GetResolvedEntityBounds(
            entity,
            new HashSet<BlockId>());
    }

    public CadRectD GetHitTestEntityBounds(
        EntityId entityId,
        CadHitTestOptions? options = null)
    {
        var entity = _cadDocument.GetEntity(entityId);
        return GetHitTestEntityBounds(entity, options);
    }

    public CadRectD GetHitTestEntityBounds(
        CadEntity entity,
        CadHitTestOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return GetHitTestEntityBounds(
            entity,
            options ?? CadHitTestOptions.Default,
            new HashSet<BlockId>());
    }

    public double GetMaxStrokeHitPadding(CadHitTestOptions? options = null)
    {
        var resolvedOptions = options ?? CadHitTestOptions.Default;
        var cacheKey = new MaxStrokeHitPaddingCacheKey(
            resolvedOptions.ViewportZoom,
            resolvedOptions.KeepStrokeWidthScreenConstant,
            resolvedOptions.MinimumScreenStrokeWidth);
        if (_maxStrokeHitPaddingCacheKey == cacheKey)
            return _maxStrokeHitPaddingCache;

        var maxPadding = 0.0;
        var visitedBlocks = new HashSet<BlockId>();

        foreach (var entity in _cadDocument.Entities.Values)
        {
            if (!CadEntityAccessPolicy.IsSelectable(_cadDocument, entity))
                continue;

            maxPadding = Math.Max(
                maxPadding,
                GetMaxStrokeHitPadding(entity, resolvedOptions, visitedBlocks));
        }

        _maxStrokeHitPaddingCacheKey = cacheKey;
        _maxStrokeHitPaddingCache = maxPadding;
        return maxPadding;
    }

    public void InvalidateCaches()
    {
        _maxStrokeHitPaddingCacheKey = null;
        _maxStrokeHitPaddingCache = 0;
    }

    public double GetStrokeHitPadding(
        CadEntity entity,
        CadHitTestOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return GetMaxStrokeHitPadding(
            entity,
            options ?? CadHitTestOptions.Default,
            new HashSet<BlockId>());
    }

    private CadRectD GetResolvedEntityBounds(
        CadEntity entity,
        HashSet<BlockId> visitedBlocks)
    {
        if (entity.IsErased)
            return CadRectD.Empty;

        if (entity is not CadBlockReference blockReference)
            return entity.Bounds;

        if (!visitedBlocks.Add(blockReference.DefinitionBlockId))
            return CadRectD.Empty;

        try
        {
            var definition = _cadDocument.GetBlock(blockReference.DefinitionBlockId);

            var bounds = CadRectD.Empty;

            foreach (var child in _cadDocument.GetEntitiesInBlock(blockReference.DefinitionBlockId))
            {
                bounds = bounds.Union(
                    GetResolvedEntityBounds(child, visitedBlocks));
            }

            if (bounds.IsEmpty)
            {
                return CadRectD.FromLTRB(
                    blockReference.Position.X,
                    blockReference.Position.Y,
                    blockReference.Position.X,
                    blockReference.Position.Y);
            }

            return TransformBlockLocalBoundsToWorld(
                bounds,
                blockReference,
                definition.BasePoint);
        }
        finally
        {
            visitedBlocks.Remove(blockReference.DefinitionBlockId);
        }
    }

    private CadRectD GetHitTestEntityBounds(
        CadEntity entity,
        CadHitTestOptions options,
        HashSet<BlockId> visitedBlocks)
    {
        if (entity.IsErased)
            return CadRectD.Empty;

        if (entity is not CadBlockReference blockReference)
            return CadHitTestStyleResolver.InflateByStroke(_cadDocument, entity, options);

        if (!visitedBlocks.Add(blockReference.DefinitionBlockId))
            return CadRectD.Empty;

        try
        {
            var definition = _cadDocument.GetBlock(blockReference.DefinitionBlockId);
            var bounds = CadRectD.Empty;

            foreach (var child in _cadDocument.GetEntitiesInBlock(blockReference.DefinitionBlockId))
            {
                bounds = bounds.Union(
                    GetHitTestEntityBounds(child, options, visitedBlocks));
            }

            if (bounds.IsEmpty)
            {
                return CadRectD.FromLTRB(
                    blockReference.Position.X,
                    blockReference.Position.Y,
                    blockReference.Position.X,
                    blockReference.Position.Y);
            }

            return TransformBlockLocalBoundsToWorld(
                bounds,
                blockReference,
                definition.BasePoint);
        }
        finally
        {
            visitedBlocks.Remove(blockReference.DefinitionBlockId);
        }
    }

    private double GetMaxStrokeHitPadding(
        CadEntity entity,
        CadHitTestOptions options,
        HashSet<BlockId> visitedBlocks)
    {
        if (entity.IsErased || !entity.IsVisible)
            return 0.0;

        if (entity is not CadBlockReference blockReference)
            return CadHitTestStyleResolver.ResolveStrokeHitPadding(_cadDocument, entity, options);

        if (!visitedBlocks.Add(blockReference.DefinitionBlockId))
            return 0.0;

        try
        {
            var scale = Math.Max(Math.Abs(blockReference.ScaleX), Math.Abs(blockReference.ScaleY));
            if (scale <= 0)
                scale = 1.0;

            var maxPadding = 0.0;
            foreach (var child in _cadDocument.GetEntitiesInBlock(blockReference.DefinitionBlockId))
            {
                maxPadding = Math.Max(
                    maxPadding,
                    GetMaxStrokeHitPadding(child, options, visitedBlocks) * scale);
            }

            return maxPadding;
        }
        finally
        {
            visitedBlocks.Remove(blockReference.DefinitionBlockId);
        }
    }

    private static CadRectD TransformBlockLocalBoundsToWorld(
        CadRectD localBounds,
        CadBlockReference blockReference,
        CadPointD blockBasePoint)
    {
        var p1 = TransformBlockLocalPointToWorld(
            new CadPointD(localBounds.MinX, localBounds.MinY),
            blockReference,
            blockBasePoint);

        var p2 = TransformBlockLocalPointToWorld(
            new CadPointD(localBounds.MaxX, localBounds.MinY),
            blockReference,
            blockBasePoint);

        var p3 = TransformBlockLocalPointToWorld(
            new CadPointD(localBounds.MaxX, localBounds.MaxY),
            blockReference,
            blockBasePoint);

        var p4 = TransformBlockLocalPointToWorld(
            new CadPointD(localBounds.MinX, localBounds.MaxY),
            blockReference,
            blockBasePoint);

        return CadRectD.Empty
            .ExpandToInclude(p1)
            .ExpandToInclude(p2)
            .ExpandToInclude(p3)
            .ExpandToInclude(p4);
    }

    private static CadPointD TransformBlockLocalPointToWorld(
        CadPointD localPoint,
        CadBlockReference blockReference,
        CadPointD blockBasePoint)
    {
        var x = (localPoint.X - blockBasePoint.X) * blockReference.ScaleX;
        var y = (localPoint.Y - blockBasePoint.Y) * blockReference.ScaleY;

        var cos = Math.Cos(blockReference.RotationRadians);
        var sin = Math.Sin(blockReference.RotationRadians);

        var rotatedX = x * cos - y * sin;
        var rotatedY = x * sin + y * cos;

        return new CadPointD(
            rotatedX + blockReference.Position.X,
            rotatedY + blockReference.Position.Y);
    }

    #endregion Resolved Bounds

    private readonly record struct MaxStrokeHitPaddingCacheKey(
        double ViewportZoom,
        bool KeepStrokeWidthScreenConstant,
        double MinimumScreenStrokeWidth);
}
