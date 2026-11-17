using System.Globalization;
using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor.Commands;

namespace Direct2dCad.Application.Tools;

public sealed partial class CadDocumentToolExecutor
{
    private CadRectD ResolveEntityBounds(JsonElement entityIdElement)
    {
        if (!entityIdElement.TryGetInt64(out var rawId))
            throw new ArgumentException("entity_id must be an integer.");
        var entityId = new EntityId(rawId);
        if (!session.CadEditor.Document.TryGetEntity(entityId, out var entity) ||
            entity is null || entity.IsErased ||
            entity.OwnerBlockId != session.CadEditor.ActiveOwnerBlockId)
        {
            throw new ArgumentException("The entity was not found in the active drawing space.");
        }
        if (entity.Bounds.IsEmpty)
            throw new ArgumentException("The entity has no drawable bounds.");
        return entity.Bounds;
    }

    private string SelectByBounds(JsonElement arguments)
    {
        var area = CadRectD.FromLTRB(
            RequiredDouble(arguments, "min_x"),
            RequiredDouble(arguments, "min_y"),
            RequiredDouble(arguments, "max_x"),
            RequiredDouble(arguments, "max_y"));
        if (area.IsEmpty || area.Width <= 0 || area.Height <= 0)
            throw new ArgumentException("Selection bounds must have positive width and height.");

        var mode = ParseEnumValue<CadSelectionMode>(OptionalString(arguments, "mode") ?? "Replace");
        session.CadEditor.Execute(new BoxSelectCommand(
            area,
            mode,
            OptionalBool(arguments, "require_contained"),
            ownerBlockId: session.CadEditor.ActiveOwnerBlockId));
        session.RequestRender();
        return SelectionResult();
    }

    private string SelectByPolygon(JsonElement arguments)
    {
        var polygon = RequiredPoints(arguments, "points", 3);
        var mode = ParseEnumValue<CadSelectionMode>(OptionalString(arguments, "mode") ?? "Replace");
        session.CadEditor.Execute(new PolygonSelectCommand(
            polygon,
            mode,
            OptionalBool(arguments, "require_contained"),
            ownerBlockId: session.CadEditor.ActiveOwnerBlockId));
        session.RequestRender();
        return SelectionResult();
    }

    private string SelectByFilter(JsonElement arguments)
    {
        var type = OptionalString(arguments, "type");
        var layerName = OptionalString(arguments, "layer");
        var nameContains = OptionalString(arguments, "name_contains");
        var visible = OptionalNullableBool(arguments, "visible");
        var locked = OptionalNullableBool(arguments, "locked");
        if (type is null && layerName is null && nameContains is null && visible is null && locked is null)
            throw new ArgumentException("select_by_filter requires at least one filter property.");

        var layerId = layerName is null ? (LayerId?)null : ResolveLayer(layerName);
        var normalizedType = type is null ? null : NormalizeEntityType(type);
        var normalizedName = nameContains?.Trim();
        var mode = ParseEnumValue<CadSelectionMode>(OptionalString(arguments, "mode") ?? "Replace");
        session.CadEditor.Execute(new FilterSelectionCommand(
            entity =>
                (normalizedType is null || NormalizeEntityType(entity.GetType().Name) == normalizedType) &&
                (layerId is null || entity.LayerId == layerId.Value) &&
                (normalizedName is null || entity.Name.Contains(normalizedName, StringComparison.OrdinalIgnoreCase)) &&
                (visible is null || entity.IsVisible == visible.Value) &&
                (locked is null || entity.IsLocked == locked.Value),
            mode,
            session.CadEditor.ActiveOwnerBlockId));
        session.RequestRender();
        return SelectionResult();
    }

    private static string NormalizeEntityType(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("Cad", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[3..];
        return normalized;
    }

    private string SetDrawingLayer(JsonElement arguments)
    {
        var layerId = ResolveLayer(RequiredString(arguments, "layer"));
        session.DrawingLayerId = layerId;
        var layer = session.CadEditor.Document.GetLayer(layerId);
        return Success(new { layer_id = layerId.Value, layer = layer.Name });
    }

    private string SelectionResult(string? action = null) => Success(new
    {
        action,
        selected_entity_ids = session.CadEditor.Selection.EntityIds
            .Select(id => id.Value)
            .ToArray(),
        count = session.CadEditor.Selection.Count
    });
}
