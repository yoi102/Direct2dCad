using System.Numerics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
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
    Direct2DEntityOrderCache entityOrderCache)
{
    private readonly HashSet<BlockId> _visitedBlocks = [];

    public void Draw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadBlockReference reference,
        CadRenderOptions options)
    {
        _visitedBlocks.Clear();
        Draw(
            context,
            document,
            viewport,
            reference.DefinitionBlockId,
            reference.Position,
            reference.RotationRadians,
            reference.ScaleX,
            reference.ScaleY,
            options,
            _visitedBlocks);
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
        CadRenderOptions options)
    {
        _visitedBlocks.Clear();
        Draw(
            context,
            document,
            viewport,
            definitionBlockId,
            position,
            rotationRadians,
            scaleX,
            scaleY,
            options,
            _visitedBlocks);
    }

    private void Draw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        BlockId definitionBlockId,
        CadPointD position,
        double rotationRadians,
        double scaleX,
        double scaleY,
        CadRenderOptions options,
        HashSet<BlockId> visited)
    {
        if (!visited.Add(definitionBlockId) ||
            !document.TryGetBlock(definitionBlockId, out var definition) ||
            definition is null)
        {
            return;
        }

        var previousTransform = context.Transform;
        context.Transform = CreateTransform(
            definition.BasePoint,
            position,
            rotationRadians,
            scaleX,
            scaleY) * previousTransform;
        try
        {
            foreach (var child in entityOrderCache.GetOrderedEntities(document, definitionBlockId))
            {
                if (!IsVisible(document, child, options))
                    continue;

                if (child is CadBlockReference nested)
                {
                    Draw(
                        context,
                        document,
                        viewport,
                        nested.DefinitionBlockId,
                        nested.Position,
                        nested.RotationRadians,
                        nested.ScaleX,
                        nested.ScaleY,
                        options,
                        visited);
                    continue;
                }

                if (child is CadOleObject oleObject)
                {
                    oleRenderer.DrawEntity(context, oleObject, viewport);
                    continue;
                }

                if (resourceCache.TryGetEntityResources(child.Id, out var resources) && resources is not null)
                    entityRenderer.Draw(context, document, child, resources, viewport, options);
            }
        }
        finally
        {
            context.Transform = previousTransform;
            visited.Remove(definitionBlockId);
        }
    }

    private static bool IsVisible(CadDocument document, CadEntity entity, CadRenderOptions options)
    {
        return !entity.IsErased &&
               entity.IsVisible &&
               !options.HiddenEntityIds.Contains(entity.Id) &&
               document.TryGetLayer(entity.LayerId, out var layer) &&
               layer is { IsVisible: true, IsFrozen: false };
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
}
