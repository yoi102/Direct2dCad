using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Db.Cad;

public static class CadEntityAccessPolicy
{
    public static bool IsSelectable(CadDocument document, CadEntity entity)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entity);

        return !entity.IsErased &&
               entity.IsVisible &&
               document.TryGetLayer(entity.LayerId, out var layer) &&
               layer is { IsVisible: true, IsFrozen: false };
    }

    public static bool IsEditable(CadDocument document, CadEntity entity)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entity);

        return !entity.IsErased &&
               !entity.IsLocked &&
               document.TryGetLayer(entity.LayerId, out var layer) &&
               layer is { IsLocked: false, IsFrozen: false };
    }

    public static void EnsureEditable(CadDocument document, CadEntity entity)
    {
        if (IsEditable(document, entity))
            return;

        if (entity.IsLocked)
            throw new InvalidOperationException($"Entity is locked: {entity.Id}");

        var layer = document.GetLayer(entity.LayerId);
        if (layer.IsLocked)
            throw new InvalidOperationException($"Layer is locked: {layer.Name}");
        if (layer.IsFrozen)
            throw new InvalidOperationException($"Layer is frozen: {layer.Name}");

        throw new InvalidOperationException($"Entity cannot be edited: {entity.Id}");
    }

    public static bool CanAddToLayer(CadDocument document, LayerId layerId)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.TryGetLayer(layerId, out var layer) &&
               layer is { IsLocked: false, IsFrozen: false };
    }

    public static void EnsureCanAddToLayer(CadDocument document, LayerId layerId)
    {
        if (CanAddToLayer(document, layerId))
            return;

        var layer = document.GetLayer(layerId);
        throw new InvalidOperationException(layer.IsFrozen
            ? $"Layer is frozen: {layer.Name}"
            : $"Layer is locked: {layer.Name}");
    }
}
