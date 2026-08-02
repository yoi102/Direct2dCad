using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Direct2dCad.AI;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;

namespace Direct2dCad.ViewModels.Tools;

/// <summary>
/// Routes one tool execution session across the open-document workspace. Each touched document
/// receives its own command batch so its undo history remains independent.
/// </summary>
internal sealed class CadWorkspaceToolExecutor
{
    private static readonly HashSet<string> CreationToolNames =
    [
        "add_line", "add_circle", "add_rectangle", "add_text", "add_shape_text", "add_polyline",
        "add_arc", "add_ellipse", "add_ellipse_arc", "add_polygon", "add_spline", "add_composite_path"
    ];

    private readonly ICadToolWorkspace _workspace;
    private readonly IImageImportService? _imageImportService;
    private readonly Dictionary<string, CadDocumentToolExecutor> _documentExecutors = new(StringComparer.Ordinal);
    private string? _defaultDocumentId;

    public CadWorkspaceToolExecutor(
        ICadToolWorkspace workspace,
        IImageImportService? imageImportService = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _imageImportService = imageImportService;
        _defaultDocumentId = workspace.GetActiveDocument()?.DocumentId;
    }

    public static IReadOnlyList<AiToolDefinition> ToolDefinitions { get; } = BuildToolDefinitions();

    public string CreateToolUsageInstructions()
    {
        var documents = _workspace.GetDocuments();
        var documentSummary = documents.Count == 0
            ? "No CAD documents are open. You can create or open one with a workspace tool."
            : string.Join("; ", documents.Select(document =>
                $"document_id={document.DocumentId}, name='{document.Name}', cad_document_id={document.CadDocumentId}, active={document.IsActive}, modified={document.IsModified}"));

        var activeDetails = TryGetDefaultDocument() is { } active
            ? GetExecutor(active).CreateToolUsageInstructions()
            : "CAD coordinates use +X to the right and +Y upward. Angles exposed by tools are counter-clockwise degrees.";

        return $$"""
            You are the workspace-aware CAD editing assistant inside Direct2dCad. Use tools for every inspection or modification and never claim success before a tool confirms it. The machine-readable Agent Contract is available through get_agent_capabilities; call it when the task is unfamiliar, multi-step, or involves many entities. Open documents: {{documentSummary}}

            document_id is the stable tab identifier. Supply it whenever the user names a non-default document. If omitted, document-scoped tools use the document that was active when this request began, or the document most recently created, opened, or activated by a tool. Document lifecycle tools are workspace operations; drawing content and appearance changes use undoable document commands. Each document touched by this request has an independent undo batch. Use add_entities for coherent multi-part drawings, including per-entity styles and fills. Use set_entity_specific_properties for text/font/inversion, embedded-object opacity, and BlockReference definition changes. Use set_text_style_properties or set_graphic_style_properties for shared styles; those operations affect every reference and are undoable. Inspect layers with list_layers before renaming, deleting, or reordering them; delete_layer requires explicit confirmation because it also deletes the layer's entities. Create missing layers with create_layer. Use create_block to turn existing entities into a reusable definition and insert_block for additional references. Use splines for smooth organic outlines, and read exact geometry before modifying existing entities. Keep replies concise and report document_id together with created or changed entity IDs.

            {{activeDetails}}

            Recommended execution order: inspect the workspace and active document, query the relevant entities/layers/styles, choose one coherent mutation or add_entities batch, verify returned IDs and active-space constraints, then report only confirmed changes. For a complex drawing, plan the parts mentally before issuing mutations and prefer add_entities over many single-entity calls.
            """;
    }

    public async Task<string> ExecuteAsync(AiToolCall toolCall, CancellationToken cancellationToken)
    {
        try
        {
            using var arguments = JsonDocument.Parse(string.IsNullOrWhiteSpace(toolCall.ArgumentsJson)
                ? "{}"
                : toolCall.ArgumentsJson);
            var root = arguments.RootElement;

            return toolCall.Name switch
            {
                "list_documents" => ListDocuments(),
                "create_document" => CreateDocument(root),
                "open_document" => await OpenDocumentAsync(root, cancellationToken),
                "activate_document" => ActivateDocument(root),
                "rename_document" => RenameDocument(root),
                "save_document" => await SaveDocumentAsync(root, cancellationToken),
                "close_document" => await CloseDocumentAsync(root),
                "get_agent_capabilities" => GetAgentCapabilities(root),
                "insert_image_from_file" => InsertImageFromFile(root),
                "add_ole_object" => AddOleObject(root),
                "list_document_catalog" => ExecuteForDocument(root, ListDocumentCatalog),
                "set_entity_common_properties" => ExecuteForDocument(root, SetEntityCommonProperties),
                "set_entity_fill" => ExecuteForDocument(root, SetEntityFill),
                "set_entity_stroke_style" => ExecuteForDocument(root, SetEntityStrokeStyle),
                "set_text_style_properties" => ExecuteForDocument(root, CadTextStyleTools.Execute),
                "set_graphic_style_properties" => ExecuteForDocument(root, CadGraphicStyleTools.Execute),
                "set_entity_specific_properties" => ExecuteForDocument(
                    root,
                    CadEntitySpecificPropertyTools.Execute),
                "set_ole_object_data" => ExecuteForDocument(root, SetOleObjectData),
                "create_block" => ExecuteForDocument(root, CreateBlock),
                "insert_block" => ExecuteForDocument(root, InsertBlock),
                "add_composite_path" => ExecuteForDocument(root, AddCompositePath),
                "add_entities" => ExecuteForDocument(root, AddEntities),
                _ when CadLayerTools.ToolDefinitions.Any(definition => definition.Name == toolCall.Name) =>
                    ExecuteForDocument(root, (executor, arguments) =>
                        new CadLayerTools(
                            executor.DocumentViewModel.CadEditor.Document,
                            executor.ExecuteCommand)
                        .Execute(toolCall.Name, arguments)),
                "get_entity_geometry" or "set_entity_geometry" or "transform_entities" or "duplicate_entities" =>
                    ExecuteForDocument(root, (executor, arguments) => CadGeometryTools.Execute(executor, toolCall.Name, arguments)),
                _ when CadDocumentToolExecutor.ToolDefinitions.Any(definition => definition.Name == toolCall.Name) =>
                    ExecuteLegacyDocumentTool(toolCall, root),
                _ => Error($"Unknown CAD tool: {toolCall.Name}")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Error(exception.Message);
        }
    }

    private string ListDocuments() => Success(new
    {
        documents = _workspace.GetDocuments().Select(DocumentDto).ToArray(),
        default_document_id = _defaultDocumentId
    });

    private string GetAgentCapabilities(JsonElement arguments)
    {
        var includeExamples = OptionalBool(arguments, "include_examples", true);
        return Success(CadAgentContract.CreateCapabilities(
            _workspace.GetDocuments(),
            _defaultDocumentId,
            includeExamples));
    }

    private string CreateDocument(JsonElement arguments)
    {
        var document = _workspace.CreateDocument(OptionalString(arguments, "name"));
        _defaultDocumentId = document.DocumentId;
        return Success(new { document = DocumentDto(document) });
    }

    private async Task<string> OpenDocumentAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var document = await _workspace.OpenDocumentAsync(
            RequiredString(arguments, "file_path"),
            cancellationToken);
        _defaultDocumentId = document.DocumentId;
        return Success(new { document = DocumentDto(document) });
    }

    private string ActivateDocument(JsonElement arguments)
    {
        var documentId = RequiredString(arguments, "document_id");
        _workspace.ActivateDocument(documentId);
        _defaultDocumentId = documentId;
        return Success(new { document = DocumentDto(_workspace.GetRequiredDocument(documentId)) });
    }

    private string RenameDocument(JsonElement arguments)
    {
        var document = ResolveDocument(arguments);
        var name = RequiredString(arguments, "name");
        if (!_workspace.RenameDocument(document.DocumentId, name))
            throw new InvalidOperationException("The document could not be renamed.");
        return Success(new { document = DocumentDto(_workspace.GetRequiredDocument(document.DocumentId)) });
    }

    private async Task<string> SaveDocumentAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var document = ResolveDocument(arguments);
        var saved = await _workspace.SaveDocumentAsync(
            document.DocumentId,
            OptionalString(arguments, "file_path"),
            cancellationToken);
        return Success(new
        {
            saved,
            document = DocumentDto(_workspace.GetRequiredDocument(document.DocumentId))
        });
    }

    private async Task<string> CloseDocumentAsync(JsonElement arguments)
    {
        var document = ResolveDocument(arguments);
        var closed = await _workspace.CloseDocumentAsync(document.DocumentId);
        if (closed)
        {
            _documentExecutors.Remove(document.DocumentId);
            if (string.Equals(_defaultDocumentId, document.DocumentId, StringComparison.Ordinal))
                _defaultDocumentId = _workspace.GetActiveDocument()?.DocumentId ?? _workspace.GetDocuments().FirstOrDefault()?.DocumentId;
        }

        return Success(new
        {
            closed,
            document_id = document.DocumentId,
            default_document_id = _defaultDocumentId
        });
    }

    private string ExecuteLegacyDocumentTool(AiToolCall toolCall, JsonElement arguments)
    {
        var document = ResolveDocument(arguments);
        var executor = GetExecutor(document);

        if (!CreationToolNames.Contains(toolCall.Name))
            return AddDocumentId(executor.Execute(toolCall), document.DocumentId);

        var (createdEntityId, changedFields) = ExecuteCreationTool(executor, toolCall.Name, arguments);
        return Success(new
        {
            document_id = document.DocumentId,
            created_entity_id = createdEntityId.Value,
            applied_appearance = changedFields
        });
    }

    private object AddEntities(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var items = CadBulkCreationTools.Parse(arguments);

        foreach (var item in items)
        {
            if (item.ToolName == "add_composite_path")
                _ = CadCompositePathTools.Parse(item.Arguments);
            else
                executor.ValidateCreationTool(item.ToolName, item.Arguments);
            ValidateCreationProperties(item.ToolName, item.Arguments, executor);
        }

        var created = new List<object>(items.Count);
        var createdIds = new List<EntityId>(items.Count);
        foreach (var item in items)
        {
            var (entityId, appearance) = ExecuteCreationTool(executor, item.ToolName, item.Arguments);
            createdIds.Add(entityId);
            created.Add(new
            {
                type = item.ToolName[4..],
                entity_id = entityId.Value,
                applied_appearance = appearance
            });
        }

        executor.DocumentViewModel.SelectEntities(createdIds);
        return new { count = created.Count, created_entities = created };
    }

    private (EntityId EntityId, IReadOnlyList<string> Appearance) ExecuteCreationTool(
        CadDocumentToolExecutor executor,
        string toolName,
        JsonElement arguments)
    {
        ValidateCreationProperties(toolName, arguments, executor);
        if (toolName == "add_composite_path")
        {
            var createdId = CreateCompositePath(executor, arguments);
            return (createdId, ApplyCreationProperties(executor, createdId, toolName, arguments));
        }
        var toolCall = new AiToolCall(Guid.NewGuid().ToString("N"), toolName, arguments.GetRawText());
        var creationResult = executor.Execute(toolCall);
        if (!TryReadCreatedEntityId(creationResult, out var createdEntityId))
            throw new InvalidOperationException(ReadToolError(creationResult));

        var changedFields = ApplyCreationProperties(executor, createdEntityId, toolName, arguments);
        return (createdEntityId, changedFields);
    }

    private EntityId CreateCompositePath(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var geometry = CadCompositePathTools.Parse(arguments);
        var layerId = HasValue(arguments, "layer")
            ? executor.ResolveLayerForTool(RequiredString(arguments, "layer"))
            : executor.DocumentViewModel.DrawingLayerId;
        var command = new AddCompositePathCommand(
            geometry.StartPoint,
            geometry.Segments,
            geometry.Closed,
            layerId,
            name: OptionalString(arguments, "name") ?? NextEntityName(
                executor.DocumentViewModel.CadEditor.Document,
                "CompositePath"));
        executor.ExecuteCommand(command);
        var entityId = command.CreatedEntityId ?? throw new InvalidOperationException("The composite path was not created.");
        executor.DocumentViewModel.SelectEntities([entityId]);
        return entityId;
    }

    private object AddCompositePath(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        ValidateCreationProperties("add_composite_path", arguments, executor);
        var id = CreateCompositePath(executor, arguments);
        var appearance = ApplyCreationProperties(executor, id, "add_composite_path", arguments);
        return new { created_entity_id = id.Value, applied_appearance = appearance };
    }

    private static string ReadToolError(string result)
    {
        using var json = JsonDocument.Parse(result);
        return json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
            ? error.GetString() ?? "The entity could not be created."
            : "The entity could not be created.";
    }

    private string ExecuteForDocument(
        JsonElement arguments,
        Func<CadDocumentToolExecutor, JsonElement, object> operation)
    {
        var document = ResolveDocument(arguments);
        var result = operation(GetExecutor(document), arguments);
        return Success(new { document_id = document.DocumentId, result });
    }

    private string InsertImageFromFile(JsonElement arguments)
    {
        if (_imageImportService is null)
            throw new NotSupportedException("Image file import is unavailable in the current host.");

        var filePath = Path.GetFullPath(RequiredString(arguments, "file_path"));
        var image = _imageImportService.LoadFromFile(filePath);
        if (image.PixelWidth <= 0 || image.PixelHeight <= 0 || image.Stride <= 0 || image.Pixels.Length == 0)
            throw new InvalidDataException("The image importer returned empty pixel data.");

        return ExecuteForDocument(arguments, (executor, documentArguments) =>
        {
            var bounds = RequiredRect(documentArguments, "bounds");
            var layerId = HasValue(documentArguments, "layer")
                ? executor.ResolveLayerForTool(RequiredString(documentArguments, "layer"))
                : executor.DocumentViewModel.DrawingLayerId;
            var command = new AddImageCommand(
                bounds,
                image.PixelWidth,
                image.PixelHeight,
                image.Stride,
                image.Pixels,
                layerId,
                image.ContentType,
                image.SourceName,
                OptionalString(documentArguments, "name") ?? "Image",
                OptionalInt(documentArguments, "z_index", 0),
                OptionalBool(documentArguments, "visible", true),
                OptionalUnitInterval(documentArguments, "opacity", 1.0),
                OptionalFinite(documentArguments, "rotation_degrees", 0) * Math.PI / 180.0);
            executor.ExecuteCommand(command);
            var entityId = command.CreatedEntityId ??
                           throw new InvalidOperationException("The image entity was not created.");
            if (HasValue(documentArguments, "locked") &&
                OptionalBool(documentArguments, "locked", false))
            {
                executor.ExecuteCommand(new SetEntityLockedCommand([entityId], true));
            }
            executor.DocumentViewModel.SelectEntities([entityId]);
            return new
            {
                created_entity_id = entityId.Value,
                source_name = image.SourceName,
                content_type = image.ContentType,
                pixel_width = image.PixelWidth,
                pixel_height = image.PixelHeight,
                bounds = RectDto(bounds)
            };
        });
    }

    private string AddOleObject(JsonElement arguments)
    {
        var oleBytes = DecodeBase64(RequiredString(arguments, "ole_base64"), "ole_base64");
        return ExecuteForDocument(arguments, (executor, documentArguments) =>
        {
            var bounds = RequiredRect(documentArguments, "bounds");
            var layerId = HasValue(documentArguments, "layer")
                ? executor.ResolveLayerForTool(RequiredString(documentArguments, "layer"))
                : executor.DocumentViewModel.DrawingLayerId;
            var command = new AddOleObjectCommand(
                bounds,
                oleBytes,
                layerId,
                OptionalString(documentArguments, "content_type") ?? "application/x-ole-storage",
                OptionalString(documentArguments, "source_name") ?? string.Empty,
                OptionalString(documentArguments, "name") ?? "OleObject",
                OptionalInt(documentArguments, "z_index", 0),
                OptionalBool(documentArguments, "visible", true),
                OptionalUnitInterval(documentArguments, "opacity", 1.0));
            executor.ExecuteCommand(command);
            var entityId = command.CreatedEntityId ??
                           throw new InvalidOperationException("The OLE entity was not created.");
            if (HasValue(documentArguments, "locked") &&
                OptionalBool(documentArguments, "locked", false))
            {
                executor.ExecuteCommand(new SetEntityLockedCommand([entityId], true));
            }
            executor.DocumentViewModel.SelectEntities([entityId]);
            return new
            {
                created_entity_id = entityId.Value,
                bounds = RectDto(bounds),
                content_type = OptionalString(documentArguments, "content_type") ?? "application/x-ole-storage",
                byte_length = oleBytes.Length
            };
        });
    }

    private object SetOleObjectData(
        CadDocumentToolExecutor executor,
        JsonElement arguments)
    {
        var entityId = RequiredEntityId(arguments);
        var document = executor.DocumentViewModel.CadEditor.Document;
        var entity = document.GetEntity(entityId);
        var oleObject = entity as CadOleObject
            ?? throw new NotSupportedException("set_ole_object_data only supports OleObject entities.");
        var oleBytes = DecodeBase64(RequiredString(arguments, "ole_base64"), "ole_base64");
        executor.ExecuteCommand(new SetOleObjectDataCommand(
            entityId,
            oleBytes,
            OptionalString(arguments, "content_type") ?? oleObject.ContentType,
            OptionalString(arguments, "source_name") ?? oleObject.SourceName));
        executor.DocumentViewModel.SelectEntities([entityId]);
        return new
        {
            entity_id = entityId.Value,
            content_type = OptionalString(arguments, "content_type") ?? oleObject.ContentType,
            source_name = OptionalString(arguments, "source_name") ?? oleObject.SourceName,
            byte_length = oleBytes.Length
        };
    }

    private object ListDocumentCatalog(
        CadDocumentToolExecutor executor,
        JsonElement _)
    {
        var document = executor.DocumentViewModel.CadEditor.Document;
        return new
        {
            layers = document.Layers.Values.Select(layer => new
            {
                id = layer.Id.Value,
                layer.Name,
                layer.IsVisible,
                layer.IsLocked,
                layer.IsFrozen,
                color = ColorText(layer.Color),
                line_weight = LineWeightValue(layer.LineWeight),
                line_type_id = layer.DefaultGraphicStyleId is { } styleId &&
                               document.TryGetStyle(styleId, out var layerStyle) &&
                               layerStyle is CadGraphicStyle layerGraphic
                    ? layerGraphic.LineTypeId.Value
                    : LineTypeId.Continuous.Value
            }).ToArray(),
            graphic_styles = document.Styles.Values.OfType<CadGraphicStyle>().Select(style => new
            {
                id = style.Id.Value,
                style.Name,
                color = ColorText(style.StrokeColor),
                line_weight = LineWeightValue(style.LineWeight),
                line_type_id = style.LineTypeId.Value
            }).ToArray(),
            fill_styles = document.Styles.Values.OfType<CadFillStyle>().Select(FillStyleDto).ToArray(),
            hatch_patterns = document.HatchPatterns.Values.Select(pattern => new
            {
                id = pattern.Id.Value,
                pattern.Name,
                pattern.Description,
                line_count = pattern.Lines.Count,
                lines = pattern.Lines.Select(line => new
                {
                    angle_degrees = line.Angle,
                    origin = new { x = line.Origin.X, y = line.Origin.Y },
                    offset = new { x = line.Offset.X, y = line.Offset.Y },
                    dash_pattern = line.DashPattern.ToArray(),
                    is_solid = line.IsSolidLine
                }).ToArray()
            }).ToArray(),
            text_styles = document.Styles.Values.OfType<CadTextStyle>().Select(style => new
            {
                id = style.Id.Value,
                style.Name,
                style.FontFamily,
                style.TextHeight,
                style.WidthFactor,
                oblique_angle_degrees = style.ObliqueAngle * 180.0 / Math.PI,
                style.IsBold,
                style.IsItalic
            }).ToArray(),
            shape_fonts = CadShapeFontRegistry.Defaults.Select(font => new
            {
                id = font.Id.Value,
                font.Name,
                glyph_set = font.GlyphSet.ToString(),
                font.SupportsUnicode
            }).ToArray(),
            blocks = document.Blocks.Values
                .Where(block => block.Kind == CadBlockKind.User)
                .Select(block => new
                {
                    id = block.Id.Value,
                    block.Name,
                    base_point = new { x = block.BasePoint.X, y = block.BasePoint.Y },
                    entity_count = block.EntityIds.Count
                }).ToArray()
        };
    }

    private object CreateBlock(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var editor = executor.DocumentViewModel.CadEditor;
        var ids = executor.ResolveEntityIdsForTool(arguments, allowSelectionFallback: true);
        ValidateCreationAppearance("create_block", arguments, executor);
        var blockName = RequiredString(arguments, "name");
        var basePoint = new CadPointD(
            RequiredFinite(arguments, "base_x"),
            RequiredFinite(arguments, "base_y"));
        var referenceLayerId = HasValue(arguments, "reference_layer")
            ? executor.ResolveLayerForTool(RequiredString(arguments, "reference_layer"))
            : executor.DocumentViewModel.DrawingLayerId;
        var command = new CreateBlockCommand(
            ids,
            blockName,
            basePoint,
            editor.ActiveOwnerBlockId,
            referenceLayerId,
            OptionalString(arguments, "reference_name") ?? blockName);
        executor.ExecuteCommand(command);
        var blockId = command.CreatedBlockId ?? throw new InvalidOperationException("The block was not created.");
        var referenceId = command.CreatedReferenceId ?? throw new InvalidOperationException("The block reference was not created.");
        var appearance = ApplyCreationAppearance(executor, referenceId, arguments);
        executor.DocumentViewModel.SelectEntities([referenceId]);
        return new
        {
            block_id = blockId.Value,
            block_name = blockName,
            reference_entity_id = referenceId.Value,
            source_entity_ids = ids.Select(id => id.Value).ToArray(),
            applied_appearance = appearance
        };
    }

    private object InsertBlock(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var editor = executor.DocumentViewModel.CadEditor;
        ValidateCreationAppearance("insert_block", arguments, executor);
        var definition = ResolveBlock(editor.Document, RequiredString(arguments, "block"));
        var layerId = HasValue(arguments, "layer")
            ? executor.ResolveLayerForTool(RequiredString(arguments, "layer"))
            : executor.DocumentViewModel.DrawingLayerId;
        var command = new InsertBlockReferenceCommand(
            definition.Id,
            editor.ActiveOwnerBlockId,
            new CadPointD(RequiredFinite(arguments, "x"), RequiredFinite(arguments, "y")),
            layerId,
            OptionalFinite(arguments, "rotation_degrees", 0) * Math.PI / 180.0,
            OptionalNonZero(arguments, "scale_x", 1),
            OptionalNonZero(arguments, "scale_y", 1),
            OptionalString(arguments, "name") ?? definition.Name);
        executor.ExecuteCommand(command);
        var entityId = command.CreatedEntityId ?? throw new InvalidOperationException("The block reference was not created.");
        var appearance = ApplyCreationAppearance(executor, entityId, arguments);
        executor.DocumentViewModel.SelectEntities([entityId]);
        return new
        {
            entity_id = entityId.Value,
            block_id = definition.Id.Value,
            block_name = definition.Name,
            applied_appearance = appearance
        };
    }

    private object SetEntityCommonProperties(
        CadDocumentToolExecutor executor,
        JsonElement arguments)
    {
        var ids = executor.ResolveEntityIdsForTool(arguments, allowSelectionFallback: false);
        var document = executor.DocumentViewModel.CadEditor.Document;
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

        executor.DocumentViewModel.SelectEntities(ids);
        return new { entity_ids = ids.Select(id => id.Value).ToArray(), changed_fields = changed };
    }

    private object SetEntityFill(
        CadDocumentToolExecutor executor,
        JsonElement arguments)
    {
        var ids = executor.ResolveEntityIdsForTool(arguments, allowSelectionFallback: false);
        var document = executor.DocumentViewModel.CadEditor.Document;
        if (ids.Select(document.GetEntity).Any(entity => !CadEntityCapabilities.SupportsFill(entity)))
            throw new NotSupportedException("One or more entities do not support fill styles.");

        ValidateFillArguments(document, arguments);
        var fillStyleId = ResolveFillStyle(executor, arguments);
        executor.ExecuteCommand(new SetEntityFillStyleCommand(ids, fillStyleId));
        executor.DocumentViewModel.SelectEntities(ids);
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
        var document = executor.DocumentViewModel.CadEditor.Document;
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

        executor.DocumentViewModel.SelectEntities(ids);
        return new { entity_ids = ids.Select(id => id.Value).ToArray(), changed_fields = changedFields };
    }

    private void ValidateCreationProperties(
        string toolName,
        JsonElement arguments,
        CadDocumentToolExecutor executor)
    {
        ValidateCreationAppearance(toolName, arguments, executor);
        CadEntitySpecificPropertyTools.ValidateCreationArguments(
            executor.DocumentViewModel.CadEditor.Document,
            toolName,
            arguments);
    }

    private void ValidateCreationAppearance(string toolName, JsonElement arguments, CadDocumentToolExecutor executor)
    {
        var document = executor.DocumentViewModel.CadEditor.Document;
        if (HasValue(arguments, "color") && HasValue(arguments, "graphic_style"))
            throw new ArgumentException("color and graphic_style cannot be set in the same call.");
        if (HasValue(arguments, "graphic_style"))
            _ = ResolveGraphicStyle(document, RequiredString(arguments, "graphic_style"));
        if (HasValue(arguments, "color"))
            _ = ParseColor(RequiredString(arguments, "color"));
        if (HasValue(arguments, "color_source"))
            _ = ParseEnum<CadColorSource>(RequiredString(arguments, "color_source"));
        if (arguments.TryGetProperty("line_weight", out var lineWeight) && lineWeight.ValueKind != JsonValueKind.Null)
            _ = ParseLineWeight(lineWeight);
        if (HasValue(arguments, "z_index") &&
            (!arguments.TryGetProperty("z_index", out var zIndex) || !zIndex.TryGetInt32(out _)))
        {
            throw new ArgumentException("z_index must be an integer.");
        }
        if (HasValue(arguments, "visible") &&
            arguments.GetProperty("visible").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ArgumentException("visible must be a boolean.");
        }
        if (HasValue(arguments, "locked") &&
            arguments.GetProperty("locked").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ArgumentException("locked must be a boolean.");
        }

        if (HasValue(arguments, "fill") &&
            (!arguments.TryGetProperty("fill", out var fill) || fill.ValueKind != JsonValueKind.Object))
        {
            throw new ArgumentException("fill must be an object.");
        }
        if (arguments.TryGetProperty("fill", out fill) && fill.ValueKind == JsonValueKind.Object)
        {
            if (toolName is not ("add_circle" or "add_ellipse" or "add_rectangle" or "add_polygon" or "add_polyline" or "add_spline" or "add_composite_path"))
                throw new NotSupportedException($"{toolName} does not support fill.");
            if (toolName is "add_polyline" or "add_spline" or "add_composite_path" &&
                (!arguments.TryGetProperty("closed", out var closed) || closed.ValueKind != JsonValueKind.True))
            {
                throw new NotSupportedException($"Only a closed {toolName[4..]} supports fill.");
            }
            ValidateFillArguments(document, fill);
        }

        if (HasValue(arguments, "stroke_style") &&
            (!arguments.TryGetProperty("stroke_style", out var stroke) || stroke.ValueKind != JsonValueKind.Object))
        {
            throw new ArgumentException("stroke_style must be an object.");
        }
        if (arguments.TryGetProperty("stroke_style", out stroke) && stroke.ValueKind == JsonValueKind.Object)
        {
            if (toolName is "add_text" or "add_shape_text")
                throw new NotSupportedException($"{toolName} does not support stroke_style.");
            foreach (var field in StrokeFields)
                if (HasValue(stroke, field))
                    ValidateStrokeEnum(field, RequiredString(stroke, field));

            var changesStartOrEnd = HasValue(stroke, "start_cap") || HasValue(stroke, "end_cap");
            var isClosedPath = toolName is "add_polyline" or "add_spline" or "add_composite_path" &&
                                   arguments.TryGetProperty("closed", out var closed) &&
                                   closed.ValueKind == JsonValueKind.True;
            if (changesStartOrEnd && toolName is not ("add_line" or "add_arc" or "add_ellipse_arc") &&
                (toolName is not ("add_polyline" or "add_spline" or "add_composite_path") || isClosedPath))
                throw new NotSupportedException($"{toolName} does not support start/end caps.");
            if (HasValue(stroke, "line_join") && toolName is not ("add_rectangle" or "add_polygon" or "add_polyline" or "add_spline" or "add_composite_path"))
                throw new NotSupportedException($"{toolName} does not support line joins.");
        }
    }

    private static void ValidateFillArguments(CadDocument document, JsonElement arguments)
    {
        var mode = RequiredString(arguments, "mode").Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        if (mode is "none" or "no_fill")
            return;
        if (mode == "style")
        {
            _ = ResolveExistingFillStyle(document, RequiredString(arguments, "style"));
            return;
        }
        if (mode is not ("solid" or "hatch" or "gradient"))
            throw new ArgumentException($"Unsupported fill mode: {mode}");
        if (HasValue(arguments, "color"))
            _ = ParseColor(RequiredString(arguments, "color"));
        if (mode == "gradient" && HasValue(arguments, "color"))
            throw new ArgumentException("Gradient fill uses stops[].color instead of color.");
        _ = OptionalPositive(arguments, "scale", 1.0);
        _ = OptionalFinite(arguments, "angle_degrees", 0.0);
        _ = OptionalFinite(arguments, "origin_x", 0.0);
        _ = OptionalFinite(arguments, "origin_y", 0.0);

        if (mode != "hatch")
        {
            if (mode == "gradient")
            {
                _ = ParseGradientKind(arguments);
                _ = ParseGradientStops(arguments);
                _ = OptionalPositive(arguments, "gradient_scale", 1.0);
                _ = OptionalFinite(arguments, "gradient_angle_degrees", 0.0);
                _ = OptionalFinite(arguments, "gradient_origin_x", 0.0);
                _ = OptionalFinite(arguments, "gradient_origin_y", 0.0);
                if (HasValue(arguments, "gradient_centered") &&
                    arguments.GetProperty("gradient_centered").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw new ArgumentException("gradient_centered must be a boolean.");
                }
            }
            return;
        }
        var requestedPattern = OptionalString(arguments, "pattern") ?? "ANSI31";
        var exists = document.HatchPatterns.Values.Any(pattern =>
            string.Equals(pattern.Name, requestedPattern, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(pattern.Id.Value.ToString(CultureInfo.InvariantCulture), requestedPattern, StringComparison.Ordinal));
        var isDefault = FillStyleCatalog.BuildFillStyleOptions(document).Any(option =>
            option.Kind == FillStyleOptionKind.Hatch &&
            (string.Equals(option.StyleName, requestedPattern, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(option.Name, requestedPattern, StringComparison.OrdinalIgnoreCase)));
        if (!exists && !isDefault)
            throw new ArgumentException($"Hatch pattern not found: {requestedPattern}");
    }

    private static void ValidateStrokeEnum(string field, string value)
    {
        if (field is "start_cap" or "end_cap" or "dash_cap")
            _ = ParseEnum<CadStrokeCap>(value);
        else if (field == "dash_style")
            _ = ParseEnum<CadStrokeDashStyle>(value);
        else if (field == "line_join")
            _ = ParseEnum<CadStrokeLineJoin>(value);
    }

    private IReadOnlyList<string> ApplyCreationAppearance(
        CadDocumentToolExecutor executor,
        EntityId entityId,
        JsonElement arguments)
    {
        var changed = new List<string>();
        var common = new JsonObject
        {
            ["entity_ids"] = new JsonArray(JsonValue.Create(entityId.Value))
        };
        foreach (var field in CommonAppearanceFields)
        {
            if (arguments.TryGetProperty(field, out var value))
                common[field] = JsonNode.Parse(value.GetRawText());
        }

        if (common.Count > 1)
        {
            using var commonDocument = JsonDocument.Parse(common.ToJsonString());
            SetEntityCommonProperties(
                executor,
                commonDocument.RootElement);
            changed.AddRange(CommonAppearanceFields.Where(common.ContainsKey));
            if (common.ContainsKey("color") && !common.ContainsKey("color_source"))
                changed.Add("color_source");
        }

        if (arguments.TryGetProperty("fill", out var fill) && fill.ValueKind == JsonValueKind.Object)
        {
            using var fillDocument = AddEntityIds(fill, entityId);
            SetEntityFill(
                executor,
                fillDocument.RootElement);
            changed.Add("fill");
        }

        if (arguments.TryGetProperty("stroke_style", out var stroke) && stroke.ValueKind == JsonValueKind.Object)
        {
            using var strokeDocument = AddEntityIds(stroke, entityId);
            SetEntityStrokeStyle(
                executor,
                strokeDocument.RootElement);
            changed.Add("stroke_style");
        }

        return changed;
    }

    private IReadOnlyList<string> ApplyCreationProperties(
        CadDocumentToolExecutor executor,
        EntityId entityId,
        string toolName,
        JsonElement arguments)
    {
        var changed = ApplyCreationAppearance(executor, entityId, arguments).ToList();
        var fields = toolName switch
        {
            "add_text" => new[] { "text_style", "font_family", "inverted", "inverted_margin_factor" },
            "add_shape_text" => new[] { "shape_font", "inverted", "inverted_margin_factor" },
            _ => []
        };
        var supplied = fields.Where(field => HasValue(arguments, field)).ToArray();
        if (supplied.Length == 0)
            return changed;

        var properties = new JsonObject
        {
            ["entity_ids"] = new JsonArray(JsonValue.Create(entityId.Value))
        };
        foreach (var field in supplied)
            properties[field] = JsonNode.Parse(arguments.GetProperty(field).GetRawText());

        using var document = JsonDocument.Parse(properties.ToJsonString());
        _ = CadEntitySpecificPropertyTools.Execute(executor, document.RootElement);
        changed.AddRange(supplied);
        return changed;
    }

    private CadToolWorkspaceDocument ResolveDocument(JsonElement arguments)
    {
        var documentId = OptionalString(arguments, "document_id") ?? _defaultDocumentId;
        if (documentId is null)
            throw new InvalidOperationException("No CAD document is open. Create or open a document first.");
        return _workspace.GetRequiredDocument(documentId);
    }

    private CadToolWorkspaceDocument? TryGetDefaultDocument()
    {
        if (_defaultDocumentId is null)
            return null;
        try
        {
            return _workspace.GetRequiredDocument(_defaultDocumentId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private CadDocumentToolExecutor GetExecutor(CadToolWorkspaceDocument document)
    {
        if (_documentExecutors.TryGetValue(document.DocumentId, out var executor) &&
            ReferenceEquals(executor.DocumentViewModel, document.DocumentViewModel))
        {
            return executor;
        }

        executor = new CadDocumentToolExecutor(document.DocumentViewModel, Guid.NewGuid());
        _documentExecutors[document.DocumentId] = executor;
        return executor;
    }

    private static JsonDocument AddEntityIds(JsonElement source, EntityId entityId)
    {
        var node = JsonNode.Parse(source.GetRawText())!.AsObject();
        node["entity_ids"] = new JsonArray(JsonValue.Create(entityId.Value));
        return JsonDocument.Parse(node.ToJsonString());
    }

    private static StyleId? ResolveGraphicStyle(CadDocument document, string value)
    {
        if (IsNone(value))
            return null;
        var style = document.Styles.Values.OfType<CadGraphicStyle>().FirstOrDefault(candidate =>
            string.Equals(candidate.Name, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Id.Value.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal));
        return style?.Id ?? throw new ArgumentException($"Graphic style not found: {value}");
    }

    private static StyleId? ResolveFillStyle(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var document = executor.DocumentViewModel.CadEditor.Document;
        var mode = RequiredString(arguments, "mode").Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        if (mode is "none" or "no_fill")
            return null;
        if (mode == "style")
            return ResolveExistingFillStyle(document, RequiredString(arguments, "style"));

        var color = HasValue(arguments, "color")
            ? ParseColor(RequiredString(arguments, "color"))
            : FillStyleCatalog.DefaultFillColor;
        var options = FillStyleCatalog.BuildFillStyleOptions(document);
        if (mode == "solid")
        {
            var option = options.First(candidate => candidate.Kind == FillStyleOptionKind.Solid);
            var solidStyle = document.Styles.Values.OfType<CadGradientFillStyle>().FirstOrDefault(style =>
                style.IsSolid && style.Stops.Count > 0 && style.Stops[0].Color == color);
            if (solidStyle is not null)
                return solidStyle.Id;

            var command = CreateFillStyleCommand.Solid(
                option.StyleName.Length == 0 ? option.Name : option.StyleName,
                color);
            executor.ExecuteCommand(command);
            return command.CreatedStyleId;
        }
        if (mode == "gradient")
        {
            var kind = ParseGradientKind(arguments);
            var stops = ParseGradientStops(arguments);
            var gradientScale = OptionalPositive(arguments, "gradient_scale", 1.0);
            var gradientAngle = OptionalFinite(arguments, "gradient_angle_degrees", 0.0) * Math.PI / 180.0;
            var gradientOrigin = new CadPointD(
                OptionalFinite(arguments, "gradient_origin_x", 0.0),
                OptionalFinite(arguments, "gradient_origin_y", 0.0));
            var centered = OptionalBool(arguments, "gradient_centered", true);
            var existingGradient = document.Styles.Values.OfType<CadGradientFillStyle>().FirstOrDefault(style =>
                style.GradientKind == kind &&
                style.GradientAngle.Equals(gradientAngle) &&
                style.GradientScale.Equals(gradientScale) &&
                style.GradientOrigin.Equals(gradientOrigin) &&
                style.IsCentered == centered &&
                style.Stops.SequenceEqual(stops));
            if (existingGradient is not null)
                return existingGradient.Id;

            var command = CreateFillStyleCommand.Gradient(
                $"{kind} gradient scale {gradientScale:0.###} angle {gradientAngle:0.###}",
                kind,
                stops,
                gradientAngle,
                gradientScale,
                gradientOrigin,
                centered);
            executor.ExecuteCommand(command);
            return command.CreatedStyleId;
        }
        if (mode != "hatch")
            throw new ArgumentException($"Unsupported fill mode: {mode}");

        var requestedPattern = OptionalString(arguments, "pattern") ?? "ANSI31";
        var pattern = ResolveHatchPattern(executor, document, options, requestedPattern, color);
        var scale = OptionalPositive(arguments, "scale", 1.0);
        var angle = OptionalFinite(arguments, "angle_degrees", 0.0) * Math.PI / 180.0;
        var origin = new CadPointD(
            OptionalFinite(arguments, "origin_x", 0.0),
            OptionalFinite(arguments, "origin_y", 0.0));

        var hatchStyle = document.Styles.Values.OfType<CadHatchFillStyle>().FirstOrDefault(style =>
            style.PatternId.Equals(pattern.Id) &&
            style.ForegroundColor.Equals(color) &&
            style.HatchScale.Equals(scale) &&
            style.HatchAngle.Equals(angle) &&
            style.HatchOrigin.Equals(origin) &&
            !style.IsAnnotative);
        if (hatchStyle is not null)
            return hatchStyle.Id;

        var hatchCommand = CreateFillStyleCommand.Hatch(
            $"{pattern.Name} {ColorText(color)} scale {scale:0.###} angle {angle:0.###}",
            pattern.Id,
            color,
            scale,
            angle,
            origin);
        executor.ExecuteCommand(hatchCommand);
        return hatchCommand.CreatedStyleId;
    }

    private static StyleId ResolveExistingFillStyle(CadDocument document, string value)
    {
        var style = document.Styles.Values.OfType<CadFillStyle>().FirstOrDefault(candidate =>
            string.Equals(candidate.Name, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Id.Value.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal));
        return style?.Id ?? throw new ArgumentException($"Fill style not found: {value}");
    }

    private static CadHatchPatternDefinition ResolveHatchPattern(
        CadDocumentToolExecutor executor,
        CadDocument document,
        IReadOnlyList<FillStyleOption> options,
        string requestedPattern,
        CadColor color)
    {
        var existing = document.HatchPatterns.Values.FirstOrDefault(pattern =>
            string.Equals(pattern.Name, requestedPattern, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(pattern.Id.Value.ToString(CultureInfo.InvariantCulture), requestedPattern, StringComparison.Ordinal));
        if (existing is not null)
            return existing;

        var option = options.FirstOrDefault(candidate =>
            candidate.Kind == FillStyleOptionKind.Hatch &&
            (string.Equals(candidate.StyleName, requestedPattern, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(candidate.Name, requestedPattern, StringComparison.OrdinalIgnoreCase)));
        if (option is null)
            throw new ArgumentException($"Hatch pattern not found: {requestedPattern}");

        var definition = FillStyleCatalog.GetDefaultHatchDefinition(option.StyleName)
            ?? throw new InvalidOperationException($"Hatch pattern could not be created: {requestedPattern}");
        var command = new CreateHatchPatternCommand(
            definition.Name,
            definition.Lines,
            definition.Description);
        executor.ExecuteCommand(command);
        var patternId = command.CreatedPatternId
            ?? throw new InvalidOperationException($"Hatch pattern could not be created: {requestedPattern}");
        return document.HatchPatterns[patternId];
    }

    private static CadGradientKind ParseGradientKind(JsonElement arguments)
    {
        var value = OptionalString(arguments, "gradient_kind") ?? "linear";
        return value.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant() switch
        {
            "linear" => CadGradientKind.Linear,
            "radial" => CadGradientKind.Radial,
            _ => throw new ArgumentException("gradient_kind must be linear or radial.")
        };
    }

    private static CadGradientStop[] ParseGradientStops(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("stops", out var value) || value.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("stops is required for gradient fill and must be an array.");

        var stops = value.EnumerateArray().Select((item, index) =>
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"stops[{index}] must be an object.");
            if (!item.TryGetProperty("offset", out var offsetValue) ||
                !offsetValue.TryGetDouble(out var offset) ||
                !double.IsFinite(offset))
            {
                throw new ArgumentException($"stops[{index}].offset must be a finite number.");
            }
            var color = ParseColor(RequiredString(item, "color"));
            return new CadGradientStop(offset, color);
        }).OrderBy(stop => stop.Offset).ToArray();

        if (stops.Length < 2 || stops[0].Offset != 0.0 || stops[^1].Offset != 1.0)
            throw new ArgumentException("Gradient stops require at least two entries with offsets 0 and 1.");
        return stops;
    }

    private static CadLineWeight ParseLineWeight(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!.Trim();
            if (text.Replace("-", "_", StringComparison.Ordinal).Equals("by_layer", StringComparison.OrdinalIgnoreCase))
                return CadLineWeight.ByLayer;
            throw new ArgumentException("line_weight string must be 'by_layer'.");
        }

        if (!value.TryGetDouble(out var weight) || !double.IsFinite(weight) || weight <= 0)
            throw new ArgumentException("line_weight must be 'by_layer' or a finite number greater than zero.");
        return new CadLineWeight(weight);
    }

    internal static CadColor ParseColor(string value)
    {
        var text = value.Trim();
        if (text.StartsWith('#'))
            text = text[1..];
        if (text.Length == 6 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return CadColor.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        if (text.Length == 8 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            return CadColor.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        return text.ToLowerInvariant() switch
        {
            "black" => CadColor.Black,
            "white" => CadColor.White,
            "red" => CadColor.Red,
            "green" => CadColor.Green,
            "blue" => CadColor.Blue,
            "transparent" => CadColor.Transparent,
            _ => throw new ArgumentException("color must be #RRGGBB, #AARRGGBB, or a supported named color.")
        };
    }

    private static T ParseEnum<T>(string value) where T : struct, Enum
    {
        var normalized = value.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        throw new ArgumentException($"Unsupported {typeof(T).Name} value: {value}");
    }

    private static T OptionalEnum<T>(JsonElement arguments, string name, T fallback) where T : struct, Enum =>
        HasValue(arguments, name) ? ParseEnum<T>(RequiredString(arguments, name)) : fallback;

    private static bool TryReadCreatedEntityId(string result, out EntityId entityId)
    {
        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        if (root.TryGetProperty("success", out var success) && success.GetBoolean() &&
            root.TryGetProperty("result", out var resultElement) &&
            resultElement.TryGetProperty("created_entity_id", out var idElement) &&
            idElement.TryGetInt64(out var id))
        {
            entityId = new EntityId(id);
            return true;
        }

        entityId = default;
        return false;
    }

    private static string AddDocumentId(string json, string documentId)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
        root["document_id"] = documentId;
        return root.ToJsonString();
    }

    private static object DocumentDto(CadToolWorkspaceDocument document) => new
    {
        document_id = document.DocumentId,
        cad_document_id = document.CadDocumentId,
        name = document.Name,
        file_path = document.FilePath,
        is_modified = document.IsModified,
        is_active = document.IsActive
    };

    private static object FillStyleDto(CadFillStyle style) => style switch
    {
        CadHatchFillStyle hatch => new
        {
            id = style.Id.Value,
            style.Name,
            kind = "hatch",
            pattern_id = (long?)hatch.PatternId.Value,
            color = (string?)ColorText(hatch.ForegroundColor),
            scale = (double?)hatch.HatchScale,
            angle_degrees = (double?)(hatch.HatchAngle * 180.0 / Math.PI),
            origin = new { x = hatch.HatchOrigin.X, y = hatch.HatchOrigin.Y },
            annotative = hatch.IsAnnotative,
            stops = (object?)null
        },
        CadGradientFillStyle gradient when gradient.IsSolid => new
        {
            id = style.Id.Value,
            style.Name,
            kind = "solid",
            pattern_id = (long?)null,
            color = (string?)ColorText(gradient.Stops[0].Color),
            scale = (double?)null,
            angle_degrees = (double?)null,
            origin = (object?)null,
            annotative = false,
            stops = (object?)null
        },
        CadGradientFillStyle gradient => new
        {
            id = style.Id.Value,
            style.Name,
            kind = "gradient",
            gradient_kind = gradient.GradientKind.ToString().ToLowerInvariant(),
            pattern_id = (long?)null,
            color = (string?)null,
            scale = (double?)gradient.GradientScale,
            angle_degrees = (double?)(gradient.GradientAngle * 180.0 / Math.PI),
            origin = new { x = gradient.GradientOrigin.X, y = gradient.GradientOrigin.Y },
            centered = gradient.IsCentered,
            annotative = false,
            stops = gradient.Stops.Select(stop => new
            {
                offset = stop.Offset,
                color = ColorText(stop.Color)
            }).ToArray()
        },
        _ => new
        {
            id = style.Id.Value,
            style.Name,
            kind = style.FillKind.ToString(),
            pattern_id = (long?)null,
            color = (string?)null,
            scale = (double?)null,
            angle_degrees = (double?)null,
            origin = (object?)null,
            annotative = false,
            stops = (object?)null
        }
    };

    private static object? LineWeightValue(CadLineWeight lineWeight) => lineWeight.IsByLayer ? "by_layer" : lineWeight.Value;

    private static string NextEntityName(CadDocument document, string prefix)
    {
        var names = document.Entities.Values
            .Select(entity => entity.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; ; index++)
        {
            var candidate = $"{prefix}{index}";
            if (!names.Contains(candidate))
                return candidate;
        }
    }

    private static string ColorText(CadColor color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool HasValue(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException($"{name} is required.");
        return value.GetString()!.Trim();
    }

    private static EntityId RequiredEntityId(JsonElement element)
    {
        if (!element.TryGetProperty("entity_id", out var value) ||
            !value.TryGetInt64(out var id) ||
            id <= 0)
        {
            throw new ArgumentException("entity_id must be a positive integer.");
        }

        return new EntityId(id);
    }

    private static byte[] DecodeBase64(string value, string name)
    {
        try
        {
            var bytes = Convert.FromBase64String(value);
            return bytes.Length == 0
                ? throw new ArgumentException($"{name} cannot be empty.")
                : bytes;
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"{name} must be valid base64.", name, exception);
        }
    }

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static int OptionalInt(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : fallback;

    private static bool OptionalBool(JsonElement element, string name, bool fallback)
    {
        if (!element.TryGetProperty(name, out var value))
            return fallback;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ArgumentException($"{name} must be a boolean.");
        return value.GetBoolean();
    }

    private static double OptionalUnitInterval(JsonElement element, string name, double fallback)
    {
        var result = OptionalFinite(element, name, fallback);
        return result is >= 0 and <= 1
            ? result
            : throw new ArgumentOutOfRangeException(name, "Value must be between 0 and 1.");
    }

    private static CadRectD RequiredRect(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"{name} must be an object with min_x, min_y, max_x, and max_y.");

        var minX = RequiredFinite(value, "min_x");
        var minY = RequiredFinite(value, "min_y");
        var maxX = RequiredFinite(value, "max_x");
        var maxY = RequiredFinite(value, "max_y");
        if (maxX <= minX || maxY <= minY)
            throw new ArgumentException($"{name} max values must be greater than min values.");
        return CadRectD.FromLTRB(minX, minY, maxX, maxY);
    }

    private static double OptionalFinite(JsonElement element, string name, double fallback)
    {
        if (!element.TryGetProperty(name, out var value))
            return fallback;
        if (!value.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new ArgumentException($"{name} must be a finite number.");
        return result;
    }

    private static double RequiredFinite(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            !value.TryGetDouble(out var result) ||
            !double.IsFinite(result))
        {
            throw new ArgumentException($"{name} must be a finite number.");
        }

        return result;
    }

    private static double OptionalNonZero(JsonElement element, string name, double fallback)
    {
        var result = OptionalFinite(element, name, fallback);
        if (Math.Abs(result) <= 1e-9)
            throw new ArgumentOutOfRangeException(name, "Value must not be zero.");
        return result;
    }

    private static CadBlockDefinition ResolveBlock(CadDocument document, string value)
    {
        CadBlockDefinition? block = null;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId))
            document.Blocks.TryGetValue(new BlockId(numericId), out block);

        block ??= document.Blocks.Values.FirstOrDefault(candidate =>
            candidate.Kind == CadBlockKind.User &&
            string.Equals(candidate.Name, value, StringComparison.OrdinalIgnoreCase));

        if (block is null || block.Kind != CadBlockKind.User)
            throw new KeyNotFoundException($"User block not found: {value}");
        return block;
    }

    private static double OptionalPositive(JsonElement element, string name, double fallback)
    {
        var result = OptionalFinite(element, name, fallback);
        return result > 0 ? result : throw new ArgumentOutOfRangeException(name, "Value must be greater than zero.");
    }

    private static bool IsNone(string value) => value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                                                 value.Equals("by_layer", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<AiToolDefinition> BuildToolDefinitions()
    {
        var tools = CadDocumentToolExecutor.ToolDefinitions.Select(AddDocumentAndAppearanceParameters).ToList();
        tools.AddRange(WorkspaceToolDefinitions());
        tools.Add(Tool("insert_image_from_file", "Import an image file and add it as an undoable CadImage in the active editing space. The image is stored in the document; it is not a live external link.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["file_path"] = StringSchema("Absolute image file path"),
                ["bounds"] = RectSchema("Target CAD world-coordinate image frame"),
                ["layer"] = StringSchema("Existing target layer name or ID"),
                ["name"] = StringSchema("Optional image entity name"),
                ["rotation_degrees"] = NumberSchema("Counter-clockwise image rotation"),
                ["opacity"] = new { type = "number", minimum = 0.0, maximum = 1.0 },
                ["z_index"] = new { type = "integer" },
                ["visible"] = new { type = "boolean" },
                ["locked"] = new { type = "boolean" }
            }, ["file_path", "bounds"])));
        tools.Add(Tool("add_ole_object", "Add an embedded OLE object from persisted OLE storage bytes as an undoable CadOleObject.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["ole_base64"] = Base64Schema("Base64-encoded persisted OLE storage"),
                ["bounds"] = RectSchema("Target CAD world-coordinate OLE frame"),
                ["layer"] = StringSchema("Existing target layer name or ID"),
                ["name"] = StringSchema("Optional OLE entity name"),
                ["source_name"] = StringSchema("Optional source or application name"),
                ["content_type"] = StringSchema("Optional OLE content type"),
                ["opacity"] = new { type = "number", minimum = 0.0, maximum = 1.0 },
                ["z_index"] = new { type = "integer" },
                ["visible"] = new { type = "boolean" },
                ["locked"] = new { type = "boolean" }
            }, ["ole_base64", "bounds"])));
        tools.AddRange(CadGeometryTools.ToolDefinitions);
        tools.Add(CadEntitySpecificPropertyTools.ToolDefinition);
        tools.Add(Tool("set_ole_object_data", "Replace persisted OLE storage bytes as one undoable entity update.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["entity_id"] = new { type = "integer", minimum = 1 },
                ["ole_base64"] = Base64Schema("Base64-encoded persisted OLE storage"),
                ["source_name"] = StringSchema("Optional source or application name"),
                ["content_type"] = StringSchema("Optional OLE content type")
            }, ["entity_id", "ole_base64"])));
        tools.Add(CadTextStyleTools.ToolDefinition);
        tools.Add(CadGraphicStyleTools.ToolDefinition);
        tools.AddRange(CadLayerTools.ToolDefinitions);
        tools.Add(CadBulkCreationTools.ToolDefinition);
        tools.Add(CadCompositePathTools.ToolDefinition);
        tools.Add(Tool("list_document_catalog", "List layers and reusable graphic, fill, hatch, and text styles for a document.",
            ObjectSchema(new Dictionary<string, object> { ["document_id"] = DocumentIdSchema() })));
        tools.Add(Tool("set_entity_common_properties", "Set undoable common entity properties in one document batch. color and graphic_style are mutually exclusive; use one or the other.",
            ObjectSchema(
                CommonPropertySchema(includeEntityIds: true),
                ["entity_ids"],
                not: MutuallyExclusive("color", "graphic_style"))));
        tools.Add(Tool("set_entity_fill", "Set no fill, an existing fill style, solid fill, or hatch fill on fill-capable entities.",
            ObjectSchema(
                FillSchema(includeEntityIds: true),
                ["entity_ids", "mode"],
                allOf: FillRequirements())));
        tools.Add(Tool("set_entity_stroke_style", "Set one or more stroke cap, dash, or join properties while preserving omitted values.",
            ObjectSchema(StrokeSchema(includeEntityIds: true), ["entity_ids"])));
        tools.Add(Tool("create_block", "Create an undoable reusable Block from entities in the active drawing space and replace them with one reference. Uses the current selection when entity_ids is omitted.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["entity_ids"] = EntityIdsSchema(),
                ["name"] = StringSchema("Unique Block definition name"),
                ["base_x"] = new { type = "number" },
                ["base_y"] = new { type = "number" },
                ["reference_layer"] = StringSchema("Existing layer name or ID for the created reference"),
                ["reference_name"] = StringSchema("Optional name for the created reference entity"),
                ["color_source"] = EnumSchema("by_layer", "explicit", "by_block"),
                ["color"] = StringSchema("Reference stroke color"),
                ["line_weight"] = new
                {
                    oneOf = new object[]
                    {
                        new { type = "number", exclusiveMinimum = 0.0 },
                        new { type = "string", @enum = new[] { "by_layer" } }
                    }
                },
                ["graphic_style"] = StringSchema("Existing graphic style name or ID; use none to clear"),
                ["z_index"] = new { type = "integer" },
                ["visible"] = new { type = "boolean" },
                ["locked"] = new { type = "boolean" }
            }, ["name", "base_x", "base_y"], not: MutuallyExclusive("color", "graphic_style"))));
        tools.Add(Tool("insert_block", "Insert an undoable reference to an existing user Block.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["block"] = StringSchema("Existing user Block name or ID"),
                ["x"] = new { type = "number" },
                ["y"] = new { type = "number" },
                ["layer"] = StringSchema("Existing target layer name or ID"),
                ["rotation_degrees"] = new { type = "number" },
                ["scale_x"] = new { type = "number" },
                ["scale_y"] = new { type = "number" },
                ["name"] = StringSchema("Optional Block reference entity name"),
                ["color_source"] = EnumSchema("by_layer", "explicit", "by_block"),
                ["color"] = StringSchema("Reference stroke color"),
                ["line_weight"] = new
                {
                    oneOf = new object[]
                    {
                        new { type = "number", exclusiveMinimum = 0.0 },
                        new { type = "string", @enum = new[] { "by_layer" } }
                    }
                },
                ["graphic_style"] = StringSchema("Existing graphic style name or ID; use none to clear"),
                ["z_index"] = new { type = "integer" },
                ["visible"] = new { type = "boolean" },
                ["locked"] = new { type = "boolean" }
            }, ["block", "x", "y"], not: MutuallyExclusive("color", "graphic_style"))));
        return tools;
    }

    private static AiToolDefinition AddDocumentAndAppearanceParameters(AiToolDefinition definition)
    {
        var schema = JsonNode.Parse(definition.Parameters.GetRawText())!.AsObject();
        var properties = schema["properties"]!.AsObject();
        properties["document_id"] = JsonSerializer.SerializeToNode(DocumentIdSchema());
        if (CreationToolNames.Contains(definition.Name))
        {
            foreach (var (name, value) in CommonPropertySchema(includeEntityIds: false))
                if (name is not ("layer" or "name"))
                    properties[name] = JsonSerializer.SerializeToNode(value);
            if (definition.Name is not ("add_text" or "add_shape_text"))
                properties["stroke_style"] = JsonSerializer.SerializeToNode(ObjectSchema(StrokeSchema(false)));
            if (definition.Name is "add_circle" or "add_ellipse" or "add_rectangle" or "add_polygon" or "add_polyline" or "add_spline" or "add_composite_path")
                properties["fill"] = JsonSerializer.SerializeToNode(ObjectSchema(
                    FillSchema(false),
                    ["mode"],
                    allOf: FillRequirements()));
            if (definition.Name == "add_text")
            {
                properties["text_style"] = JsonSerializer.SerializeToNode(StringSchema("Existing Text style name or ID; none selects the default"));
                properties["font_family"] = JsonSerializer.SerializeToNode(StringSchema("Font family; creates or reuses a matching Text style"));
                properties["inverted"] = JsonSerializer.SerializeToNode(new { type = "boolean" });
                properties["inverted_margin_factor"] = JsonSerializer.SerializeToNode(new { type = "number", minimum = 0.0 });
            }
            else if (definition.Name == "add_shape_text")
            {
                properties["shape_font"] = JsonSerializer.SerializeToNode(StringSchema("Shape font ID or name"));
                properties["inverted"] = JsonSerializer.SerializeToNode(new { type = "boolean" });
                properties["inverted_margin_factor"] = JsonSerializer.SerializeToNode(new { type = "number", minimum = 0.0 });
            }
        }

        if (CreationToolNames.Contains(definition.Name))
            schema["not"] = JsonSerializer.SerializeToNode(MutuallyExclusive("color", "graphic_style"));

        return new AiToolDefinition(
            definition.Name,
            definition.Description,
            JsonSerializer.SerializeToElement(schema));
    }

    private static IEnumerable<AiToolDefinition> WorkspaceToolDefinitions()
    {
        yield return Tool("get_agent_capabilities", "Return the CAD Agent Contract: coordinate rules, editable-space rules, undo and selection behavior, tool groups, and examples.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["include_examples"] = new { type = "boolean", description = "Include representative tool-call examples; defaults to true." }
            }));
        yield return Tool("list_documents", "List all open CAD documents and their stable document_id values.",
            ObjectSchema(new Dictionary<string, object>()));
        yield return Tool("create_document", "Create and activate a new CAD document.",
            ObjectSchema(new Dictionary<string, object> { ["name"] = StringSchema("Optional document name") }));
        yield return Tool("open_document", "Open and activate a .d2cad file from an absolute path.",
            ObjectSchema(new Dictionary<string, object> { ["file_path"] = StringSchema("Absolute .d2cad file path") }, ["file_path"]));
        yield return Tool("activate_document", "Activate an open CAD document.",
            ObjectSchema(new Dictionary<string, object> { ["document_id"] = DocumentIdSchema() }, ["document_id"]));
        yield return Tool("rename_document", "Rename an open CAD document. This changes document metadata, not drawing command history.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(), ["name"] = StringSchema("New document name")
            }, ["name"]));
        yield return Tool("save_document", "Save a document, optionally to a new absolute file path.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(), ["file_path"] = StringSchema("Optional absolute destination path")
            }));
        yield return Tool("close_document", "Close a document, using the existing unsaved-document confirmation flow.",
            ObjectSchema(new Dictionary<string, object> { ["document_id"] = DocumentIdSchema() }));
    }

    private static Dictionary<string, object> CommonPropertySchema(bool includeEntityIds)
    {
        var properties = new Dictionary<string, object>();
        if (includeEntityIds)
        {
            properties["document_id"] = DocumentIdSchema();
            properties["entity_ids"] = EntityIdsSchema();
            properties["layer"] = StringSchema("Existing layer name or ID");
            properties["name"] = StringSchema("Entity name; only valid for one entity");
        }
        properties["color_source"] = EnumSchema("by_layer", "explicit", "by_block");
        properties["color"] = StringSchema("#RRGGBB, #AARRGGBB, or black/white/red/green/blue/transparent");
        properties["line_weight"] = new
        {
            oneOf = new object[]
            {
                new { type = "number", exclusiveMinimum = 0.0 },
                new { type = "string", @enum = new[] { "by_layer" } }
            }
        };
        properties["graphic_style"] = StringSchema("Existing graphic style name or ID; use none to clear");
        properties["z_index"] = new { type = "integer" };
        properties["visible"] = new { type = "boolean" };
        properties["locked"] = new { type = "boolean", description = "Prevent direct entity edits when true; unlocking is undoable." };
        return properties;
    }

    private static Dictionary<string, object> FillSchema(bool includeEntityIds)
    {
        var properties = new Dictionary<string, object>();
        if (includeEntityIds)
        {
            properties["document_id"] = DocumentIdSchema();
            properties["entity_ids"] = EntityIdsSchema();
        }
        properties["mode"] = EnumSchema("none", "style", "solid", "hatch", "gradient");
        properties["style"] = StringSchema("Existing fill style name or ID when mode is style");
        properties["color"] = StringSchema("Solid or hatch foreground color");
        properties["pattern"] = StringSchema("Hatch pattern name or ID; defaults to ANSI31");
        properties["scale"] = new { type = "number", exclusiveMinimum = 0.0 };
        properties["angle_degrees"] = new { type = "number" };
        properties["origin_x"] = new { type = "number" };
        properties["origin_y"] = new { type = "number" };
        properties["gradient_kind"] = EnumSchema("linear", "radial");
        properties["stops"] = new
        {
            type = "array",
            minItems = 2,
            items = new
            {
                type = "object",
                properties = new
                {
                    offset = new { type = "number", minimum = 0.0, maximum = 1.0 },
                    color = StringSchema("Stop color: #RRGGBB, #AARRGGBB, or a named protocol color")
                },
                required = new[] { "offset", "color" },
                additionalProperties = false
            }
        };
        properties["gradient_scale"] = new { type = "number", exclusiveMinimum = 0.0 };
        properties["gradient_angle_degrees"] = new { type = "number" };
        properties["gradient_origin_x"] = new { type = "number" };
        properties["gradient_origin_y"] = new { type = "number" };
        properties["gradient_centered"] = new { type = "boolean" };
        return properties;
    }

    private static object[] FillRequirements() =>
    [
        new Dictionary<string, object>
        {
            ["if"] = new Dictionary<string, object>
            {
                ["properties"] = new Dictionary<string, object>
                {
                ["mode"] = new Dictionary<string, object> { ["const"] = "style" }
                }
            },
            ["then"] = new Dictionary<string, object> { ["required"] = new[] { "style" } }
        },
        new Dictionary<string, object>
        {
            ["if"] = new Dictionary<string, object>
            {
                ["properties"] = new Dictionary<string, object>
                {
                    ["mode"] = new Dictionary<string, object> { ["const"] = "gradient" }
                }
            },
            ["then"] = new Dictionary<string, object> { ["required"] = new[] { "stops" } }
        }
    ];

    private static Dictionary<string, object> StrokeSchema(bool includeEntityIds)
    {
        var properties = new Dictionary<string, object>();
        if (includeEntityIds)
        {
            properties["document_id"] = DocumentIdSchema();
            properties["entity_ids"] = EntityIdsSchema();
        }
        properties["start_cap"] = EnumSchema("flat", "square", "round", "triangle");
        properties["end_cap"] = EnumSchema("flat", "square", "round", "triangle");
        properties["dash_cap"] = EnumSchema("flat", "square", "round", "triangle");
        properties["dash_style"] = EnumSchema("solid", "dash", "dot", "dash_dot", "dash_dot_dot");
        properties["line_join"] = EnumSchema("miter", "bevel", "round", "miter_or_bevel");
        return properties;
    }

    private static object ObjectSchema(
        IReadOnlyDictionary<string, object> properties,
        IReadOnlyList<string>? required = null,
        object? not = null,
        IReadOnlyList<object>? allOf = null)
    {
        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required ?? [],
            ["additionalProperties"] = false
        };
        if (not is not null)
            schema["not"] = not;
        if (allOf is { Count: > 0 })
            schema["allOf"] = allOf;
        return schema;
    }

    private static object MutuallyExclusive(params string[] properties) => new Dictionary<string, object>
    {
        ["required"] = properties
    };

    private static object RectDto(CadRectD bounds) => new
    {
        min_x = bounds.MinX,
        min_y = bounds.MinY,
        max_x = bounds.MaxX,
        max_y = bounds.MaxY
    };

    private static object DocumentIdSchema() => StringSchema("Stable open-document ID from list_documents");
    private static object StringSchema(string description) => new { type = "string", description };
    private static object Base64Schema(string description) => new
    {
        type = "string",
        description,
        contentEncoding = "base64",
        minLength = 4
    };
    private static object NumberSchema(string description) => new { type = "number", description };
    private static object EnumSchema(params string[] values) => new { type = "string", @enum = values };
    private static object RectSchema(string description) => new
    {
        type = "object",
        description,
        properties = new
        {
            min_x = NumberSchema("Minimum X"),
            min_y = NumberSchema("Minimum Y"),
            max_x = NumberSchema("Maximum X"),
            max_y = NumberSchema("Maximum Y")
        },
        required = new[] { "min_x", "min_y", "max_x", "max_y" },
        additionalProperties = false
    };
    private static object EntityIdsSchema() => new
    {
        type = "array",
        minItems = 1,
        uniqueItems = true,
        items = new { type = "integer" }
    };

    private static AiToolDefinition Tool(string name, string description, object parameters) =>
        new(name, description, JsonSerializer.SerializeToElement(parameters));

    private static readonly string[] CommonAppearanceFields =
    [
        "color_source", "color", "line_weight", "graphic_style", "z_index", "visible"
        , "locked"
    ];

    private static readonly string[] StrokeFields =
    [
        "start_cap", "end_cap", "dash_cap", "dash_style", "line_join"
    ];

    private static string Success(object value) => JsonSerializer.Serialize(new { success = true, result = value });
    private static string Error(string message) => JsonSerializer.Serialize(new { success = false, error = message });
}
