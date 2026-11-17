using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Application.Platform;
using Direct2dCad.IO;

using MessagePipe;


namespace Direct2dCad.Application.Tools;

/// <summary>
/// Routes one tool execution session across the open-document workspace. Each touched document
/// receives its own command batch so its undo history remains independent.
/// </summary>

public sealed partial class CadWorkspaceToolExecutor
{
    private object SetEntityCommonProperties(
        CadDocumentToolExecutor executor,
        JsonElement arguments)
    {
        var ids = executor.ResolveEntityIdsForTool(arguments, allowSelectionFallback: false);
        var document = executor.Session.CadEditor.Document;
        var entities = ids.Select(document.GetEntity).ToArray();
        var changed = new List<string>();

        var hasLayer = HasValue(arguments, "layer");
        var hasName = HasValue(arguments, "name");
        var hasColor = HasValue(arguments, "color");
        var hasColorSource = HasValue(arguments, "color_source");
        var hasGraphicStyle = HasValue(arguments, "graphic_style");
        var hasLineWeight = HasValue(arguments, "line_weight");
        var hasZIndex = HasValue(arguments, "z_index");
        var hasVisibility = HasValue(arguments, "visible");
        var hasLocked = HasValue(arguments, "locked");
        if (!hasLayer && !hasName && !hasColor && !hasColorSource && !hasGraphicStyle &&
            !hasLineWeight && !hasZIndex && !hasVisibility && !hasLocked)
        {
            throw new ArgumentException("At least one common property must be supplied.");
        }
        if (hasColor && hasGraphicStyle)
            throw new ArgumentException("color and graphic_style cannot be set in the same call.");
        if ((hasColor || hasColorSource || hasGraphicStyle) && entities.Any(entity => !CadEntityCapabilities.SupportsGraphicStyle(entity)))
            throw new NotSupportedException("One or more entities do not support stroke color or graphic styles.");
        if (hasName && ids.Length != 1)
            throw new ArgumentException("name can only be set when exactly one entity_id is supplied.");

        var targetLayerId = hasLayer
            ? executor.ResolveLayerForTool(RequiredString(arguments, "layer"))
            : (LayerId?)null;
        var entityName = hasName ? RequiredString(arguments, "name") : null;
        var graphicStyleId = hasGraphicStyle
            ? ResolveGraphicStyle(document, RequiredString(arguments, "graphic_style"))
            : null;
        var color = hasColor ? ParseColor(RequiredString(arguments, "color")) : (CadColor?)null;
        var colorSource = hasColorSource
            ? ParseEnum<CadColorSource>(RequiredString(arguments, "color_source"))
            : hasColor ? CadColorSource.Explicit : (CadColorSource?)null;
        var lineWeight = hasLineWeight ? ParseLineWeight(arguments.GetProperty("line_weight")) : (CadLineWeight?)null;
        int? zIndex = null;
        if (hasZIndex)
        {
            if (!arguments.GetProperty("z_index").TryGetInt32(out var requestedZIndex))
                throw new ArgumentException("z_index must be an integer.");
            zIndex = requestedZIndex;
        }
        bool? visible = null;
        if (hasVisibility)
        {
            var value = arguments.GetProperty("visible");
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ArgumentException("visible must be a boolean.");
            visible = value.GetBoolean();
        }
        bool? locked = null;
        if (hasLocked)
        {
            var value = arguments.GetProperty("locked");
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ArgumentException("locked must be a boolean.");
            locked = value.GetBoolean();
        }

        // An unlock must happen before the other mutations, while a lock is
        // applied last so the same call can configure and then protect an entity.
        if (locked == false && entities.Any(entity => entity.IsLocked))
        {
            executor.ExecuteCommand(new SetEntityLockedCommand(ids, false));
            changed.Add("locked");
        }

        if (targetLayerId is { } layerId)
        {
            executor.ExecuteCommand(new ChangeLayerCommand(ids, layerId));
            changed.Add("layer");
        }

        if (entityName is not null)
        {
            executor.ExecuteCommand(new RenameEntityCommand(ids[0], entityName));
            changed.Add("name");
        }

        if (hasGraphicStyle)
        {
            executor.ExecuteCommand(new SetEntityGraphicStyleCommand(ids, graphicStyleId));
            changed.Add("graphic_style");
        }

        if (color is { } parsedColor)
        {
            executor.ExecuteCommand(new SetEntityColorCommand(ids, parsedColor));
            changed.Add("color");
        }

        if (colorSource is { } parsedColorSource)
        {
            executor.ExecuteCommand(new SetEntityColorSourceCommand(ids, parsedColorSource));
            changed.Add("color_source");
        }

        if (lineWeight is { } parsedLineWeight)
        {
            executor.ExecuteCommand(new SetEntityLineWeightCommand(ids, parsedLineWeight));
            changed.Add("line_weight");
        }

        if (zIndex is { } parsedZIndex)
        {
            executor.ExecuteCommand(new SetEntityZIndexCommand(ids, parsedZIndex));
            changed.Add("z_index");
        }

        if (visible is { } parsedVisibility)
        {
            executor.ExecuteCommand(new SetEntityVisibilityCommand(ids, parsedVisibility));
            changed.Add("visible");
        }

        if (locked == true || (locked == false && !changed.Contains("locked")))
        {
            executor.ExecuteCommand(new SetEntityLockedCommand(ids, locked.Value));
            changed.Add("locked");
        }

        executor.Session.SelectEntities(ids);
        return new { entity_ids = ids.Select(id => id.Value).ToArray(), changed_fields = changed };
    }

    private object SetEntityFill(
        CadDocumentToolExecutor executor,
        JsonElement arguments)
    {
        var ids = executor.ResolveEntityIdsForTool(arguments, allowSelectionFallback: false);
        var document = executor.Session.CadEditor.Document;
        if (ids.Select(document.GetEntity).Any(entity => !CadEntityCapabilities.SupportsFill(entity)))
            throw new NotSupportedException("One or more entities do not support fill styles.");

        ValidateFillArguments(document, arguments);
        var fillStyleId = ResolveFillStyle(executor, arguments);
        executor.ExecuteCommand(new SetEntityFillStyleCommand(ids, fillStyleId));
        executor.Session.SelectEntities(ids);
        return new
        {
            entity_ids = ids.Select(id => id.Value).ToArray(),
            fill_style_id = fillStyleId?.Value,
            mode = RequiredString(arguments, "mode")
        };
    }

    private object SetEntityStrokeStyle(
        CadDocumentToolExecutor executor,
        JsonElement arguments)
    {
        var ids = executor.ResolveEntityIdsForTool(arguments, allowSelectionFallback: false);
        var document = executor.Session.CadEditor.Document;
        var entities = ids.Select(document.GetEntity).ToArray();
        if (entities.Any(entity => !CadEntityCapabilities.SupportsStrokeStyle(entity)))
            throw new NotSupportedException("One or more entities do not support stroke styles.");

        var changesStartOrEnd = HasValue(arguments, "start_cap") || HasValue(arguments, "end_cap");
        if (changesStartOrEnd && entities.Any(entity => !CadEntityCapabilities.SupportsStartEndCaps(entity)))
            throw new NotSupportedException("One or more entities do not support start/end caps.");
        if (HasValue(arguments, "line_join") && entities.Any(entity => !CadEntityCapabilities.SupportsLineJoin(entity)))
            throw new NotSupportedException("One or more entities do not support line joins.");

        var changedFields = StrokeFields.Where(field => HasValue(arguments, field)).ToArray();
        if (changedFields.Length == 0)
            throw new ArgumentException("At least one stroke style property must be supplied.");

        foreach (var entity in entities)
        {
            var current = entity.StrokeStyle;
            var style = new CadStrokeStyle(
                OptionalEnum(arguments, "start_cap", current.StartCap),
                OptionalEnum(arguments, "end_cap", current.EndCap),
                OptionalEnum(arguments, "dash_cap", current.DashCap),
                OptionalEnum(arguments, "dash_style", current.DashStyle),
                OptionalEnum(arguments, "line_join", current.LineJoin));
            executor.ExecuteCommand(new SetEntityStrokeStyleCommand([entity.Id], style));
        }

        executor.Session.SelectEntities(ids);
        return new { entity_ids = ids.Select(id => id.Value).ToArray(), changed_fields = changedFields };
    }
}
