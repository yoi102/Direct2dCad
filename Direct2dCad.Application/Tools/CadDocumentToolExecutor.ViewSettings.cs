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
    private string GetViewSettings() => Success(CreateViewSettingsObject());

    private string SetViewport(JsonElement arguments)
    {
        var editor = session.CadEditor;
        var operation = RequiredString(arguments, "operation").ToLowerInvariant();
        switch (operation)
        {
            case "fit":
                if (arguments.TryGetProperty("padding", out _))
                {
                    editor.Execute(new FitViewportCommand(
                        OptionalNonNegative(arguments, "padding", 32),
                        editor.ActiveOwnerBlockId));
                }
                else
                {
                    session.FitToWindow();
                }
                break;
            case "fit_bounds":
            {
                var bounds = arguments.TryGetProperty("entity_id", out var entityIdElement)
                    ? ResolveEntityBounds(entityIdElement)
                    : CadRectD.FromLTRB(
                        RequiredDouble(arguments, "min_x"),
                        RequiredDouble(arguments, "min_y"),
                        RequiredDouble(arguments, "max_x"),
                        RequiredDouble(arguments, "max_y"));
                editor.Execute(new FitViewportBoundsCommand(
                    bounds,
                    OptionalNonNegative(arguments, "padding", 32)));
                break;
            }
            case "zoom_entity":
            {
                var bounds = ResolveEntityBounds(arguments.GetProperty("entity_id"));
                editor.Execute(new FitViewportBoundsCommand(
                    bounds,
                    OptionalNonNegative(arguments, "padding", 32)));
                break;
            }
            case "zoom":
            {
                var factor = RequiredPositive(arguments, "factor");
                var anchor = arguments.TryGetProperty("anchor_x", out _) || arguments.TryGetProperty("anchor_y", out _)
                    ? new CadPointD(
                        RequiredDouble(arguments, "anchor_x"),
                        RequiredDouble(arguments, "anchor_y"))
                    : editor.Viewport.VisibleWorldBounds.Center;
                editor.Execute(new ZoomViewportCommand(editor.Viewport.WorldToScreen(anchor), factor));
                break;
            }
            case "pan":
            {
                var worldDelta = new CadVectorD(
                    RequiredDouble(arguments, "delta_x"),
                    RequiredDouble(arguments, "delta_y"));
                editor.Execute(new PanViewportCommand(new CadVectorD(
                    worldDelta.X * editor.Viewport.Zoom,
                    -worldDelta.Y * editor.Viewport.Zoom)));
                break;
            }
            case "center":
            {
                var point = new CadPointD(
                    RequiredDouble(arguments, "x"),
                    RequiredDouble(arguments, "y"));
                var screenDelta = new CadVectorD(
                    editor.Viewport.ViewWidth * 0.5 - editor.Viewport.WorldToScreen(point).X,
                    editor.Viewport.ViewHeight * 0.5 - editor.Viewport.WorldToScreen(point).Y);
                editor.Execute(new PanViewportCommand(screenDelta));
                break;
            }
            default:
                throw new ArgumentException("operation must be fit, fit_bounds, zoom, zoom_entity, pan, or center.");
        }

        session.RequestRender();
        return Success(new
        {
            operation,
            zoom = editor.Viewport.Zoom,
            offset = new { x = editor.Viewport.Offset.X, y = editor.Viewport.Offset.Y },
            visible_bounds = RectDto(editor.Viewport.VisibleWorldBounds)
        });
    }

    private string SetViewSettings(JsonElement arguments)
    {
        var document = session.CadEditor.Document;
        if (!arguments.EnumerateObject().Any())
            throw new ArgumentException("At least one view setting is required.");

        var target = CloneViewSettings(document.ViewSettings);
        var grid = target.Grid;
        var snapping=target.Snap;
        if(arguments.TryGetProperty("grid_snap_enabled",out var gs)) snapping=snapping with { GridEnabled=gs.GetBoolean() };
        if(arguments.TryGetProperty("object_snap_enabled",out var os)) snapping=snapping with { ObjectsEnabled=os.GetBoolean() };
        if(arguments.TryGetProperty("ortho_enabled",out var ortho)) snapping=snapping with { OrthoEnabled=ortho.GetBoolean(),PolarEnabled=ortho.GetBoolean() ? false : snapping.PolarEnabled };
        if(arguments.TryGetProperty("polar_enabled",out var polar)) snapping=snapping with { PolarEnabled=polar.GetBoolean(),OrthoEnabled=polar.GetBoolean() ? false : snapping.OrthoEnabled };
        if(arguments.TryGetProperty("polar_increment_degrees",out _)) snapping=snapping with { PolarIncrementDegrees=RequiredDouble(arguments,"polar_increment_degrees") };
        if(arguments.TryGetProperty("snap_screen_tolerance",out _)) snapping=snapping with { ScreenTolerance=RequiredDouble(arguments,"snap_screen_tolerance") };
        if(arguments.TryGetProperty("object_snap_modes",out var modes)) snapping=snapping with { Modes=modes.EnumerateArray().Aggregate(CadObjectSnapModes.None,(flags,e)=>flags|ParseEnumValue<CadObjectSnapModes>(e.GetString()!)) };
        snapping.Validate(); target.Snap=snapping;
        var origin = target.Origin;
        var requestedUnit = arguments.TryGetProperty("unit", out _)
            ? ParseEnumValue<CadUnit>(RequiredString(arguments, "unit"))
            : (CadUnit?)null;
        var requestedLengthPrecision = arguments.TryGetProperty("length_precision", out _)
            ? RequiredPrecision(arguments, "length_precision")
            : (int?)null;
        var requestedAnglePrecision = arguments.TryGetProperty("angle_precision", out _)
            ? RequiredPrecision(arguments, "angle_precision")
            : (int?)null;
        if (arguments.TryGetProperty("grid_type", out _))
            grid.Type = ParseEnumValue<CadGridType>(RequiredString(arguments, "grid_type"));
        if (arguments.TryGetProperty("snap_marker_type", out _))
            grid.SnapMarkerType = ParseEnumValue<CadSnapMarkerType>(RequiredString(arguments, "snap_marker_type"));
        if (arguments.TryGetProperty("background_color", out _))
            target.BackgroundColor = CadWorkspaceToolExecutor.ParseColor(RequiredString(arguments, "background_color"));
        if (arguments.TryGetProperty("grid_minor_line_color", out _))
            grid.MinorLineColor = CadWorkspaceToolExecutor.ParseColor(RequiredString(arguments, "grid_minor_line_color"));
        if (arguments.TryGetProperty("grid_major_line_color", out _))
            grid.MajorLineColor = CadWorkspaceToolExecutor.ParseColor(RequiredString(arguments, "grid_major_line_color"));
        if (arguments.TryGetProperty("snap_marker_color", out _))
            grid.SnapMarkerColor = CadWorkspaceToolExecutor.ParseColor(RequiredString(arguments, "snap_marker_color"));
        if (arguments.TryGetProperty("origin_display_type", out _))
            origin.DisplayType = ParseEnumValue<CadOriginDisplayType>(RequiredString(arguments, "origin_display_type"));
        if (arguments.TryGetProperty("origin_marker_type", out _))
            origin.MarkerType = ParseEnumValue<CadOriginMarkerType>(RequiredString(arguments, "origin_marker_type"));
        if (arguments.TryGetProperty("origin_line_pattern", out _))
            origin.LinePattern = ParseEnumValue<CadOriginLinePattern>(RequiredString(arguments, "origin_line_pattern"));
        if (arguments.TryGetProperty("origin_color", out _))
            origin.Color = CadWorkspaceToolExecutor.ParseColor(RequiredString(arguments, "origin_color"));
        if (arguments.TryGetProperty("origin_position_x_millimeters", out _))
            origin.Position = new CadPointD(
                RequiredDouble(arguments, "origin_position_x_millimeters"),
                origin.Position.Y);
        if (arguments.TryGetProperty("origin_position_y_millimeters", out _))
            origin.Position = new CadPointD(
                origin.Position.X,
                RequiredDouble(arguments, "origin_position_y_millimeters"));
        if (arguments.TryGetProperty("origin_size", out _))
            origin.Size = RequiredPositive(arguments, "origin_size");
        if (arguments.TryGetProperty("origin_stroke_width", out _))
            origin.StrokeWidth = RequiredPositive(arguments, "origin_stroke_width");
        if (arguments.TryGetProperty("grid_minimum_screen_spacing", out _))
            grid.MinimumScreenSpacing = RequiredPositive(arguments, "grid_minimum_screen_spacing");
        if (arguments.TryGetProperty("grid_minimum_world_spacing_millimeters", out _))
            grid.MinimumWorldSpacing = RequiredPositive(arguments, "grid_minimum_world_spacing_millimeters");
        if (arguments.TryGetProperty("grid_minor_line_width", out _))
            grid.MinorLineWidth = RequiredPositive(arguments, "grid_minor_line_width");
        if (arguments.TryGetProperty("grid_major_line_width", out _))
            grid.MajorLineWidth = RequiredPositive(arguments, "grid_major_line_width");
        if (arguments.TryGetProperty("snap_marker_length", out _))
            grid.SnapMarkerLength = RequiredPositive(arguments, "snap_marker_length");
        if (arguments.TryGetProperty("snap_marker_stroke_width", out _))
            grid.SnapMarkerStrokeWidth = RequiredPositive(arguments, "snap_marker_stroke_width");
        if (arguments.TryGetProperty("grid_snap_spacing_x_millimeters", out _))
            grid.SnapSpacingX = OptionalNonNegative(arguments, "grid_snap_spacing_x_millimeters", grid.SnapSpacingX);
        if (arguments.TryGetProperty("grid_snap_spacing_y_millimeters", out _))
            grid.SnapSpacingY = OptionalNonNegative(arguments, "grid_snap_spacing_y_millimeters", grid.SnapSpacingY);

        var hasMajorSpacing = arguments.TryGetProperty("grid_spacing_x_millimeters", out _) ||
                              arguments.TryGetProperty("grid_spacing_y_millimeters", out _);
        var hasMinorSpacing = arguments.TryGetProperty("grid_minor_spacing_x_millimeters", out _) ||
                              arguments.TryGetProperty("grid_minor_spacing_y_millimeters", out _);
        if (hasMajorSpacing || hasMinorSpacing)
        {
            if (arguments.TryGetProperty("major_grid_preset", out _) || arguments.TryGetProperty("minor_grid_preset", out _))
                throw new ArgumentException("Grid spacing values cannot be combined with grid presets.");

            grid.SpacingX = arguments.TryGetProperty("grid_spacing_x_millimeters", out _)
                ? RequiredPositive(arguments, "grid_spacing_x_millimeters")
                : grid.SpacingX;
            grid.SpacingY = arguments.TryGetProperty("grid_spacing_y_millimeters", out _)
                ? RequiredPositive(arguments, "grid_spacing_y_millimeters")
                : grid.SpacingY;
            grid.MinorSpacingX = arguments.TryGetProperty("grid_minor_spacing_x_millimeters", out _)
                ? RequiredPositive(arguments, "grid_minor_spacing_x_millimeters")
                : grid.GetMinorSpacingX();
            grid.MinorSpacingY = arguments.TryGetProperty("grid_minor_spacing_y_millimeters", out _)
                ? RequiredPositive(arguments, "grid_minor_spacing_y_millimeters")
                : grid.GetMinorSpacingY();
            if (!TryResolveGridSubdivision(grid.SpacingX, grid.MinorSpacingX, out var subdivisionX) ||
                !TryResolveGridSubdivision(grid.SpacingY, grid.MinorSpacingY, out var subdivisionY))
                throw new ArgumentException("Major and minor grid spacing must have an integer subdivision between 2 and 100.");
            grid.Subdivision = Math.Max(subdivisionX, subdivisionY);
            grid.MinimumWorldSpacing = Math.Min(grid.MinorSpacingX, grid.MinorSpacingY);
            grid.MajorSpacingPresetId = null;
            grid.MinorSpacingPresetId = null;
            grid.EnsurePresetSelections();
        }
        else if (arguments.TryGetProperty("major_grid_preset", out _) || arguments.TryGetProperty("minor_grid_preset", out _))
        {
            var majorPresetId = arguments.TryGetProperty("major_grid_preset", out _)
                ? ResolveGridPresetId(RequiredString(arguments, "major_grid_preset"))
                : grid.MajorSpacingPresetId;
            var minorPresetId = arguments.TryGetProperty("minor_grid_preset", out _)
                ? ResolveGridPresetId(RequiredString(arguments, "minor_grid_preset"))
                : grid.MinorSpacingPresetId;
            var major = grid.SpacingPresets.FirstOrDefault(preset => preset.Id == majorPresetId);
            var minor = grid.SpacingPresets.FirstOrDefault(preset => preset.Id == minorPresetId);
            if (major is null || minor is null ||
                !TryResolveGridSubdivision(major.SpacingX, minor.SpacingX, out var presetSubdivisionX) ||
                !TryResolveGridSubdivision(major.SpacingY, minor.SpacingY, out var presetSubdivisionY))
                throw new ArgumentException("The selected grid presets are incompatible.");
            grid.SpacingX = major.SpacingX;
            grid.SpacingY = major.SpacingY;
            grid.MinorSpacingX = minor.SpacingX;
            grid.MinorSpacingY = minor.SpacingY;
            grid.Subdivision = Math.Max(presetSubdivisionX, presetSubdivisionY);
            grid.MajorSpacingPresetId = major.Id;
            grid.MinorSpacingPresetId = minor.Id;
            grid.SnapSpacingX = 0;
            grid.SnapSpacingY = 0;
            grid.MinimumWorldSpacing = Math.Min(minor.SpacingX, minor.SpacingY);
        }

        var hasViewFields = arguments.EnumerateObject().Any(property =>
            property.Name is not ("unit" or "length_precision" or "angle_precision"));
        if (hasViewFields)
        {
            session.CadEditor.ExecuteInBatch(
                new SetViewSettingsCommand(target),
                batchId);
        }
        if (requestedUnit is not null ||
            requestedLengthPrecision is not null ||
            requestedAnglePrecision is not null)
        {
            var settings = document.DocumentSettings;
            session.CadEditor.ExecuteInBatch(
                new SetDocumentSettingsCommand(
                    requestedUnit ?? settings.Unit,
                    requestedLengthPrecision ?? settings.LengthPrecision,
                    requestedAnglePrecision ?? settings.AnglePrecision),
                batchId);
        }
        session.RequestRender();

        return GetViewSettings();
    }

    private string ManageGridPresets(JsonElement arguments)
    {
        var operation = RequiredString(arguments, "operation").ToLowerInvariant();
        var document = session.CadEditor.Document;
        var grid = document.ViewSettings.Grid;
        if (operation == "list")
        {
            return Success(new
            {
                presets = grid.SpacingPresets.Select(GridPresetDto).ToArray(),
                major_preset_id = grid.MajorSpacingPresetId,
                minor_preset_id = grid.MinorSpacingPresetId
            });
        }

        var presets = grid.SpacingPresets.ToList();
        var presetId = arguments.TryGetProperty("preset_id", out var idElement)
            ? ParseGuid(idElement, "preset_id")
            : Guid.Empty;
        switch (operation)
        {
            case "create":
            {
                var name = RequiredString(arguments, "name");
                EnsureGridPresetNameAvailable(presets, name);
                var spacingX = RequiredGridSpacing(arguments, "spacing_x_millimeters");
                var linkAxes = OptionalBool(arguments, "link_axes");
                var spacingY = linkAxes
                    ? spacingX
                    : RequiredGridSpacing(arguments, "spacing_y_millimeters");
                presets.Add(new CadGridSpacingPreset(Guid.NewGuid(), name, spacingX, spacingY, linkAxes));
                break;
            }
            case "rename":
            {
                var name = RequiredString(arguments, "name");
                EnsureGridPresetNameAvailable(presets, name, presetId);
                var index = FindGridPresetIndex(presets, presetId);
                presets[index] = presets[index] with { Name = name };
                break;
            }
            case "update":
            {
                var index = FindGridPresetIndex(presets, presetId);
                var current = presets[index];
                var name = current.Name;
                if (OptionalString(arguments, "name") is { } suppliedName)
                {
                    EnsureGridPresetNameAvailable(presets, suppliedName, presetId);
                    name = suppliedName;
                }
                var spacingX = arguments.TryGetProperty("spacing_x_millimeters", out _)
                    ? RequiredGridSpacing(arguments, "spacing_x_millimeters") : current.SpacingX;
                var linkAxes = arguments.TryGetProperty("link_axes", out var linked) ? linked.GetBoolean() : current.LinkAxes;
                var spacingY = current.SpacingY;
                if (arguments.TryGetProperty("spacing_y_millimeters", out _)) spacingY = RequiredGridSpacing(arguments, "spacing_y_millimeters");
                if (linkAxes) spacingY = spacingX;
                presets[index] = current with { Name = name, SpacingX = spacingX, SpacingY = spacingY, LinkAxes = linkAxes };
                break;
            }
            case "reorder":
            {
                var orderedIds = arguments.GetProperty("preset_ids").EnumerateArray().Select(value => ParseGuid(value, "preset_ids")).ToArray();
                if (orderedIds.Length != presets.Count || orderedIds.Distinct().Count() != presets.Count ||
                    orderedIds.Any(id => presets.All(preset => preset.Id != id)))
                    throw new ArgumentException("preset_ids must contain every preset ID exactly once.");
                presets = orderedIds.Select(id => presets.Single(preset => preset.Id == id)).ToList();
                break;
            }
            case "delete":
            {
                var index = FindGridPresetIndex(presets, presetId);
                if (presets.Count <= 2)
                    throw new ArgumentException("At least two grid presets must remain.");
                if (grid.MajorSpacingPresetId == presetId || grid.MinorSpacingPresetId == presetId)
                    throw new ArgumentException("Select another major/minor preset before deleting this preset.");
                presets.RemoveAt(index);
                break;
            }
            default:
                throw new ArgumentException("operation must be list, create, rename, update, reorder, or delete.");
        }

        var target = CloneViewSettings(document.ViewSettings);
        target.Grid.ReplaceSpacingPresets(presets, grid.MajorSpacingPresetId, grid.MinorSpacingPresetId);
        session.CadEditor.ExecuteInBatch(new SetViewSettingsCommand(target), batchId);
        session.RequestRender();
        return Success(new
        {
            operation,
            presets = target.Grid.SpacingPresets.Select(GridPresetDto).ToArray(),
            major_preset_id = target.Grid.MajorSpacingPresetId,
            minor_preset_id = target.Grid.MinorSpacingPresetId
        });
    }

    private static object GridPresetDto(CadGridSpacingPreset preset) => new
    {
        id = preset.Id,
        name = preset.Name,
        spacing_x_millimeters = preset.SpacingX,
        spacing_y_millimeters = preset.SpacingY,
        link_axes = preset.LinkAxes
    };

    private static void EnsureGridPresetNameAvailable(
        IReadOnlyList<CadGridSpacingPreset> presets,
        string name,
        Guid? except = null)
    {
        if (presets.Any(preset => preset.Id != except &&
                                 string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"A grid preset named '{name}' already exists.");
    }

    private static int FindGridPresetIndex(IReadOnlyList<CadGridSpacingPreset> presets, Guid id)
    {
        var index = presets.ToList().FindIndex(preset => preset.Id == id);
        return index >= 0 ? index : throw new ArgumentException("Grid preset was not found.");
    }

    private static Guid ParseGuid(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.String || !Guid.TryParse(element.GetString(), out var value))
            throw new ArgumentException($"{name} must be a valid grid preset ID.");
        return value;
    }

    private static double RequiredGridSpacing(JsonElement arguments, string name)
    {
        var value = RequiredDouble(arguments, name);
        return value is >= CadGridSettings.MinimumSpacingMillimeters and <= CadGridSettings.MaximumSpacingMillimeters
            ? value
            : throw new ArgumentOutOfRangeException(name, "Grid spacing is outside the supported range.");
    }

    private object CreateViewSettingsObject()
    {
        var document = session.CadEditor.Document;
        var grid = document.ViewSettings.Grid;
        var origin = document.ViewSettings.Origin;
        var unit = document.DocumentSettings.Unit;
        return new
        {
            unit = unit.ToString(),
            unit_symbol = CadUnitConversion.GetSymbol(unit),
            geometry_input_unit = "millimeter",
            tool_mode = session.ToolMode,
            drawing_layer = ResolveLayerName(session.DrawingLayerId),
            pointer = new
            {
                x = CadUnitConversion.FromMillimeters(session.CurrentPointerWorldX, unit),
                y = CadUnitConversion.FromMillimeters(session.CurrentPointerWorldY, unit),
                x_millimeters = session.CurrentPointerWorldX,
                y_millimeters = session.CurrentPointerWorldY
            },
            length_precision = document.DocumentSettings.LengthPrecision,
            angle_precision = document.DocumentSettings.AnglePrecision,
            snapping = new { grid_enabled=document.ViewSettings.Snap.GridEnabled,objects_enabled=document.ViewSettings.Snap.ObjectsEnabled,
                ortho_enabled=document.ViewSettings.Snap.OrthoEnabled,polar_enabled=document.ViewSettings.Snap.PolarEnabled,
                polar_increment_degrees=document.ViewSettings.Snap.PolarIncrementDegrees,screen_tolerance=document.ViewSettings.Snap.ScreenTolerance,
                modes=Enum.GetValues<CadObjectSnapModes>().Where(m=>m!=CadObjectSnapModes.None && m!=CadObjectSnapModes.Default && (document.ViewSettings.Snap.Modes&m)==m).Select(m=>m.ToString()).ToArray() },
            grid = new
            {
                type = grid.Type.ToString(),
                snap_marker_type = grid.SnapMarkerType.ToString(),
                minimum_screen_spacing = grid.MinimumScreenSpacing,
                minimum_world_spacing_millimeters = grid.MinimumWorldSpacing,
                minor_line_color = FormatColor(grid.MinorLineColor),
                major_line_color = FormatColor(grid.MajorLineColor),
                minor_line_width = grid.MinorLineWidth,
                major_line_width = grid.MajorLineWidth,
                snap_marker_color = FormatColor(grid.SnapMarkerColor),
                snap_marker_length = grid.SnapMarkerLength,
                snap_marker_stroke_width = grid.SnapMarkerStrokeWidth,
                snap_spacing_x_millimeters = grid.SnapSpacingX,
                snap_spacing_y_millimeters = grid.SnapSpacingY,
                spacing_x_millimeters = grid.SpacingX,
                spacing_y_millimeters = grid.SpacingY,
                minor_spacing_x_millimeters = grid.GetMinorSpacingX(),
                minor_spacing_y_millimeters = grid.GetMinorSpacingY(),
                major_preset_id = grid.MajorSpacingPresetId,
                minor_preset_id = grid.MinorSpacingPresetId,
                presets = grid.SpacingPresets.Select(preset => new
                {
                    id = preset.Id,
                    preset.Name,
                    spacing_x = CadUnitConversion.FromMillimeters(preset.SpacingX, unit),
                    spacing_y = CadUnitConversion.FromMillimeters(preset.SpacingY, unit),
                    spacing_x_millimeters = preset.SpacingX,
                    spacing_y_millimeters = preset.SpacingY,
                    preset.LinkAxes
                }).ToArray()
            },
            origin = new
            {
                display_type = origin.DisplayType.ToString(),
                marker_type = origin.MarkerType.ToString(),
                line_pattern = origin.LinePattern.ToString(),
                position_x = CadUnitConversion.FromMillimeters(origin.Position.X, unit),
                position_y = CadUnitConversion.FromMillimeters(origin.Position.Y, unit),
                position_x_millimeters = origin.Position.X,
                position_y_millimeters = origin.Position.Y,
                color = FormatColor(origin.Color),
                size = origin.Size,
                stroke_width = origin.StrokeWidth
            },
            background_color = FormatColor(document.ViewSettings.BackgroundColor),
            visible_bounds = RectDto(session.CadEditor.Viewport.VisibleWorldBounds)
        };
    }

    private Guid? ResolveGridPresetId(string value)
    {
        var presets = session.CadEditor.Document.ViewSettings.Grid.SpacingPresets;
        if (Guid.TryParse(value, out var id))
        {
            if (presets.Any(preset => preset.Id == id))
                return id;

            throw new KeyNotFoundException($"Grid spacing preset not found: {value}");
        }

        var preset = presets.FirstOrDefault(item =>
            string.Equals(item.Name, value, StringComparison.OrdinalIgnoreCase));
        return preset?.Id ?? throw new KeyNotFoundException($"Grid spacing preset not found: {value}");
    }

    private Guid? ResolveCurrentGridPresetId(Guid? id, double spacingX, double spacingY)
    {
        var presets = session.CadEditor.Document.ViewSettings.Grid.SpacingPresets;
        return presets.FirstOrDefault(preset => preset.Id == id)?.Id ??
               presets.FirstOrDefault(preset =>
                   NearlyEqual(preset.SpacingX, spacingX) && NearlyEqual(preset.SpacingY, spacingY))?.Id;
    }

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right))) * 1e-9;

    private static T ParseEnumValue<T>(string value) where T : struct, Enum
    {
        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        throw new ArgumentException($"Unsupported {typeof(T).Name} value: {value}");
    }

    private object ActiveSpaceDetails()
    {
        var editor = session.CadEditor;
        var document = editor.Document;
        var owner = document.TryGetBlock(editor.ActiveOwnerBlockId, out var block) && block is not null
            ? block.Name
            : editor.ActiveOwnerBlockId.ToString();
        var kind = session.IsEditingBlock
            ? "block_edit"
            : session.IsModelSpaceActive
                ? "model_space"
                : session.IsLayoutViewportActive
                    ? "layout_viewport_model_space"
                    : "paper_space";

        CadLayout? layout = session.ActiveLayoutId is { } layoutId
            ? document.GetLayout(layoutId)
            : null;
        return new
        {
            kind,
            owner_block_id = editor.ActiveOwnerBlockId.Value,
            owner,
            editing_block_id = session.EditingBlockId?.Value,
            editing_block_name = session.IsEditingBlock
                ? session.EditingBlockName
                : null,
            layout_id = layout?.Id.Value,
            layout_name = layout?.Name,
            viewport_id = session.ActiveLayoutViewportId?.Value,
            is_model_space = session.IsModelSpaceActive || session.IsLayoutViewportActive,
            is_paper_space = session.IsPaperSpaceActive
        };
    }

    private static CadViewSettings CloneViewSettings(CadViewSettings source)
    {
        var result = new CadViewSettings
        {
            Snap = source.Snap,
            BackgroundColor = source.BackgroundColor,
            Grid = new CadGridSettings
            {
                Type = source.Grid.Type,
                SpacingX = source.Grid.SpacingX,
                SpacingY = source.Grid.SpacingY,
                MinorSpacingX = source.Grid.MinorSpacingX,
                MinorSpacingY = source.Grid.MinorSpacingY,
                Subdivision = source.Grid.Subdivision,
                SnapSpacingX = source.Grid.SnapSpacingX,
                SnapSpacingY = source.Grid.SnapSpacingY,
                MinimumScreenSpacing = source.Grid.MinimumScreenSpacing,
                MinimumWorldSpacing = source.Grid.MinimumWorldSpacing,
                MinorLineColor = source.Grid.MinorLineColor,
                MajorLineColor = source.Grid.MajorLineColor,
                MinorLineWidth = source.Grid.MinorLineWidth,
                MajorLineWidth = source.Grid.MajorLineWidth,
                SnapMarkerColor = source.Grid.SnapMarkerColor,
                SnapMarkerLength = source.Grid.SnapMarkerLength,
                SnapMarkerStrokeWidth = source.Grid.SnapMarkerStrokeWidth,
                SnapMarkerType = source.Grid.SnapMarkerType
            },
            Origin = new CadOriginSettings
            {
                Position = source.Origin.Position,
                DisplayType = source.Origin.DisplayType,
                MarkerType = source.Origin.MarkerType,
                LinePattern = source.Origin.LinePattern,
                Color = source.Origin.Color,
                Size = source.Origin.Size,
                StrokeWidth = source.Origin.StrokeWidth
            }
        };
        result.Grid.ReplaceSpacingPresets(
            source.Grid.SpacingPresets,
            source.Grid.MajorSpacingPresetId,
            source.Grid.MinorSpacingPresetId);
        return result;
    }

    private static bool TryResolveGridSubdivision(double major, double minor, out int subdivision)
    {
        subdivision = 0;
        if (!double.IsFinite(major) || !double.IsFinite(minor) || minor <= 0)
            return false;
        var ratio = major / minor;
        var rounded = Math.Round(ratio);
        if (ratio < CadGridSettings.MinimumSubdivision ||
            ratio > CadGridSettings.MaximumSubdivision ||
            Math.Abs(ratio - rounded) > 1e-9)
            return false;
        subdivision = (int)rounded;
        return true;
    }

    private static int RequiredPrecision(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || !value.TryGetInt32(out var precision) || precision is < 0 or > 12)
            throw new ArgumentOutOfRangeException(name, "Precision must be an integer from 0 to 12.");
        return precision;
    }
}
