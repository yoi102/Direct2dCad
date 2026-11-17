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
    private static readonly HashSet<string> CreationToolNames =
    [
        "add_line", "add_circle", "add_rectangle", "add_text", "add_shape_text", "add_polyline",
        "add_arc", "add_ellipse", "add_ellipse_arc", "add_polygon", "add_spline", "add_composite_path"
    ];

    private readonly ICadToolWorkspace _workspace;
    private readonly IImageImportService? _imageImportService;
    private readonly IAsyncPublisher<CadToolActivityMessage>? _activityPublisher;
    private readonly Dictionary<string, CadDocumentToolExecutor> _documentExecutors = new(StringComparer.Ordinal);
    private string? _defaultDocumentId;

    public CadWorkspaceToolExecutor(
        ICadToolWorkspace workspace,
        IImageImportService? imageImportService = null,
        IAsyncPublisher<CadToolActivityMessage>? activityPublisher = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _imageImportService = imageImportService;
        _activityPublisher = activityPublisher;
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

            Use transfer_entities for cross-document copy or cut/paste. Discover engineering templates with list_document_templates and recovery versions with list_recovery_copies. For unattended file work use save_document(interactive=false) with an explicit destination for new drawings, and close_document(on_unsaved=save/discard/cancel). Inspect file_conflict.current_revision or get_file_revision before explicitly authorizing overwrite, optionally with expected_sha256. save_compatible_copy requires allow_loss=true. get_application_settings and set_application_settings cover general, rendering, interaction, radial-menu and MCP preferences. Inspect get_application_settings.mcp_service for the actual listener state, endpoint and startup error. MCP listener changes take effect after the response completes. Reconnect at the configured port after changing it.

            {{activeDetails}}

            Arguments are validated before editing. Unknown or duplicate keys are errors. Legacy enum casing and hyphen/underscore spellings normalize to the canonical schema value. Known optional non-nullable fields may use null to mean omitted; explicitly nullable fields keep their documented clear/reset meaning.

            Recommended execution order: inspect the workspace and active document, query the relevant entities/layers/styles, choose one coherent mutation or add_entities batch, verify returned IDs and active-space constraints, then report only confirmed changes. For a complex drawing, plan the parts mentally before issuing mutations and prefer add_entities over many single-entity calls.
            """;
    }

    public async Task<string> ExecuteAsync(AiToolCall toolCall, CancellationToken cancellationToken)
    {
        var documentName = ActivityDocumentName(toolCall.ArgumentsJson);
        await PublishActivityAsync(toolCall, documentName, "Started", SummarizeJson(toolCall.ArgumentsJson));
        try
        {
            var result = await ExecuteCoreAsync(toolCall, cancellationToken);
            using var json = JsonDocument.Parse(result);
            var root = json.RootElement;
            // A printer submission is already externally queued. Preserve its truthful receipt
            // if cancellation raced with submission; never relabel it as an unsubmitted cancel.
            var printSubmitted = toolCall.Name == "print_document" && root.TryGetProperty("result", out var print) &&
                print.TryGetProperty("submitted", out var submitted) && submitted.ValueKind == JsonValueKind.True;
            if (!printSubmitted) cancellationToken.ThrowIfCancellationRequested();
            var success = !root.TryGetProperty("success", out var flag) || flag.ValueKind != JsonValueKind.False;
            if (success) documentName = ActivityResultDocumentName(root) ?? documentName;
            await PublishActivityAsync(toolCall, documentName, success ? "Completed" : "Failed", SummarizeJson(AiToolResultContent.Parse(result).Text));
            return result;
        }
        catch (OperationCanceledException)
        {
            await PublishActivityAsync(toolCall, documentName, "Canceled", "Operation canceled.");
            throw;
        }
    }

    private async Task<string> ExecuteCoreAsync(AiToolCall toolCall, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var arguments = JsonDocument.Parse(string.IsNullOrWhiteSpace(toolCall.ArgumentsJson)
                ? "{}"
                : toolCall.ArgumentsJson);
            var root = arguments.RootElement;
            root = CadToolSchemaValidator.NormalizeAndValidate(toolCall.Name, root, ToolDefinitions);
            toolCall = toolCall with { ArgumentsJson = root.GetRawText() };

            return toolCall.Name switch
            {
                "list_documents" => ListDocuments(),
                "list_entities" or "get_entity_statistics" => await QueryEntitiesAsync(toolCall.Name,root,cancellationToken),
                "create_document" => CreateDocument(root),
                "open_document" => await OpenDocumentAsync(root, cancellationToken),
                "open_dxf" => await OpenDxfAsync(root,cancellationToken),
                "export_dxf" => await ExportDxfAsync(root,cancellationToken),
                "activate_document" => ActivateDocument(root),
                "rename_document" => RenameDocument(root),
                "save_document" => await SaveDocumentAsync(root, cancellationToken),
                "close_document" => await CloseDocumentAsync(root, cancellationToken),
                "list_document_templates" => ListDocumentTemplates(),
                "list_recovery_copies" => ListRecoveryCopies(),
                "open_recovery_copy" => await OpenRecoveryCopyAsync(root, cancellationToken),
                "clear_recovery_copies" => await ClearRecoveryCopiesAsync(root, cancellationToken),
                "save_compatible_copy" => await SaveCompatibleCopyAsync(root, cancellationToken),
                "transfer_entities" => TransferEntities(root),
                "get_file_revision" => GetFileRevision(root),
                "get_application_settings" => Success(_workspace.GetApplicationSettings()),
                "set_application_settings" => Success(_workspace.SetApplicationSettings(root.GetProperty("settings"), OptionalBool(root, "reset", false))),
                "get_agent_capabilities" => GetAgentCapabilities(root),
                "boolean_regions" => await BooleanRegionsAsync(root, cancellationToken),
                "capture_view" => await CaptureViewAsync(root, cancellationToken),
                "print_document" => await PrintDocumentAsync(root, cancellationToken),
                "activate_space" => ActivateSpace(root),
                "list_layouts" or "create_layout" or "rename_layout" or "delete_layout" or
                    "set_layout_paper" or "add_layout_viewport" or "set_layout_viewport" or
                    "delete_layout_viewport" or "reassociate_dimension" =>
                    ExecuteForDocument(root, (executor, args) => ExecutePresentationTool(executor, toolCall.Name, args)),
                "insert_image_from_file" => InsertImage(root, fromClipboard: false),
                "insert_image_from_clipboard" => InsertImage(root, fromClipboard: true),
                "add_ole_object" => AddOleObject(root),
                "list_document_catalog" => ExecuteForDocument(root, ListDocumentCatalog),
                "set_entity_common_properties" => ExecuteForDocument(root, SetEntityCommonProperties),
                "set_entity_fill" => ExecuteForDocument(root, SetEntityFill),
                "set_entity_stroke_style" => ExecuteForDocument(root, SetEntityStrokeStyle),
                "set_text_style_properties" => ExecuteForDocument(root, CadTextStyleTools.Execute),
                "set_graphic_style_properties" => ExecuteForDocument(root, CadGraphicStyleTools.Execute),
                "list_styles" => ExecuteForDocument(root, (executor, _) => CadStyleManagementTools.Execute(executor, "list_styles", root)),
                "create_graphic_style" or "create_line_type" or "rename_line_type" or "delete_line_type" or
                    "create_text_style" or "create_fill_style" or "create_hatch_pattern" or
                    "rename_style" or "delete_style" or "delete_hatch_pattern" =>
                    ExecuteForDocument(root, (executor, arguments) => CadStyleManagementTools.Execute(executor, toolCall.Name, arguments)),
                "list_system_fonts" => Success(CadStyleManagementTools.ListSystemFonts()),
                "set_entity_specific_properties" => ExecuteForDocument(
                    root,
                    CadEntitySpecificPropertyTools.Execute),
                "set_ole_object_data" => ExecuteForDocument(root, SetOleObjectData),
                "create_block" => ExecuteForDocument(root, CreateBlock),
                "insert_block" => ExecuteForDocument(root, InsertBlock),
                "list_blocks" => ExecuteForDocument(root, ListBlocks),
                "rename_block" => ExecuteForDocument(root, RenameBlock),
                "set_block_base_point" => ExecuteForDocument(root, SetBlockBasePoint),
                "delete_block" => ExecuteForDocument(root, DeleteBlock),
                "edit_block" => EditBlock(root),
                "exit_block_edit" => ExitBlockEdit(root),
                "add_composite_path" => ExecuteForDocument(root, AddCompositePath),
                "add_entities" => ExecuteForDocument(root, AddEntities),
                _ when CadLayerTools.ToolDefinitions.Any(definition => definition.Name == toolCall.Name) =>
                    ExecuteForDocument(root, (executor, arguments) =>
                        new CadLayerTools(
                            executor.Session.CadEditor.Document,
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
        catch (CadFileConflictException conflict)
        {
            return CadJson.Serialize(new { success = false, code = "file_conflict", error = conflict.Message,
                current_revision = new { file_path = conflict.CurrentRevision.FullPath, exists = conflict.CurrentRevision.Exists,
                    length = conflict.CurrentRevision.Length, sha256 = conflict.CurrentRevision.Hash } });
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
            includeExamples,
            _workspace.SupportsViewCapture,
            _workspace.SupportsPrinting));
    }

    private string CreateDocument(JsonElement arguments)
    {
        var document = _workspace.CreateDocument(OptionalString(arguments, "name"), OptionalString(arguments, "template"));
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
            SaveOptions(arguments),
            cancellationToken);
        var result = new
        {
            saved,
            document = DocumentDto(_workspace.GetRequiredDocument(document.DocumentId))
        };
        return saved ? Success(result) : Direct2dCad.AI.Contracts.CadJson.Serialize(new
        {
            success = false, code = "save_not_completed", error = "Save did not complete; it was cancelled or failed.", result
        });
    }

    private async Task<string> CloseDocumentAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var document = ResolveDocument(arguments);
        var closed = await _workspace.CloseDocumentAsync(document.DocumentId,
            new(OptionalString(arguments, "on_unsaved") ?? "ask", OptionalString(arguments, "file_path"),
                OptionalBool(arguments, "overwrite", false), OptionalString(arguments, "expected_sha256")), cancellationToken);
        if (closed)
        {
            _documentExecutors.Remove(document.DocumentId);
            if (string.Equals(_defaultDocumentId, document.DocumentId, StringComparison.Ordinal))
                _defaultDocumentId = _workspace.GetActiveDocument()?.DocumentId ?? _workspace.GetDocuments().FirstOrDefault()?.DocumentId;
        }

        var result = new
        {
            closed,
            document_id = document.DocumentId,
            default_document_id = _defaultDocumentId
        };
        return closed ? Success(result) : Direct2dCad.AI.Contracts.CadJson.Serialize(new
        {
            success = false, code = "cancelled", error = "Close was cancelled; the document remains open.", result
        });
    }

    private string ExecuteLegacyDocumentTool(AiToolCall toolCall, JsonElement arguments)
    {
        var document = ResolveDocument(arguments);
        var executor = GetExecutor(document);

        if (!CreationToolNames.Contains(toolCall.Name))
            return AddDocumentId(executor.ExecuteValidated(toolCall), document.DocumentId);

        var (createdEntityId, changedFields) = executor.ExecuteAtomically(
            () => ExecuteCreationTool(executor, toolCall.Name, arguments));
        return Success(new
        {
            document_id = document.DocumentId,
            created_entity_id = createdEntityId.Value,
            applied_appearance = changedFields
        });
    }

    private async Task<string> QueryEntitiesAsync(string name,JsonElement arguments,CancellationToken token)
    {
        var document=ResolveDocument(arguments);
        var result=await document.Host.RunAsync(Direct2dCad.Lang.CadUiText.Get("QueryingDrawing"),
            ct=>GetExecutor(document).QueryAsync(name,arguments,ct),token);
        if(!_workspace.GetDocuments().Any(d=>d.DocumentId==document.DocumentId && ReferenceEquals(d.Host,document.Host)))
            throw new OperationCanceledException("The document was closed.",token);
        return AddDocumentId(result,document.DocumentId);
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

        executor.Session.SelectEntities(createdIds);
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
        var creationResult = executor.ExecuteValidated(toolCall);
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
            : executor.Session.DrawingLayerId;
        var command = new AddCompositePathCommand(
            geometry.StartPoint,
            geometry.Segments,
            geometry.Closed,
            layerId,
            name: OptionalString(arguments, "name") ?? NextEntityName(
                executor.Session.CadEditor.Document,
                "CompositePath"));
        executor.ExecuteCreationCommand(command);
        var entityId = command.CreatedEntityId ?? throw new InvalidOperationException("The composite path was not created.");
        executor.Session.SelectEntities([entityId]);
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
        var executor = GetExecutor(document);
        var result = executor.ExecuteAtomically(() => operation(executor, arguments));
        return Success(new { document_id = document.DocumentId, result });
    }

    private string InsertImage(JsonElement arguments, bool fromClipboard)
    {
        if (_imageImportService is null)
            throw new NotSupportedException("Image import is unavailable in the current host.");

        var image = fromClipboard
            ? _imageImportService.LoadFromClipboard() ?? throw new InvalidOperationException("The clipboard does not contain an image.")
            : _imageImportService.LoadFromFile(Path.GetFullPath(RequiredString(arguments, "file_path")));
        if (image.PixelWidth <= 0 || image.PixelHeight <= 0 || image.Stride <= 0 || image.Pixels.Length == 0)
            throw new InvalidDataException("The image importer returned empty pixel data.");

        return ExecuteForDocument(arguments, (executor, documentArguments) =>
        {
            var bounds = RequiredRect(documentArguments, "bounds");
            var layerId = HasValue(documentArguments, "layer")
                ? executor.ResolveLayerForTool(RequiredString(documentArguments, "layer"))
                : executor.Session.DrawingLayerId;
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
            executor.ExecuteCreationCommand(command);
            var entityId = command.CreatedEntityId ??
                           throw new InvalidOperationException("The image entity was not created.");
            if (HasValue(documentArguments, "locked") &&
                OptionalBool(documentArguments, "locked", false))
            {
                executor.ExecuteCommand(new SetEntityLockedCommand([entityId], true));
            }
            executor.Session.SelectEntities([entityId]);
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
                : executor.Session.DrawingLayerId;
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
            executor.ExecuteCreationCommand(command);
            var entityId = command.CreatedEntityId ??
                           throw new InvalidOperationException("The OLE entity was not created.");
            if (HasValue(documentArguments, "locked") &&
                OptionalBool(documentArguments, "locked", false))
            {
                executor.ExecuteCommand(new SetEntityLockedCommand([entityId], true));
            }
            executor.Session.SelectEntities([entityId]);
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
        var entity = executor.GetEntityForTool(entityId);
        var oleObject = entity as CadOleObject
            ?? throw new NotSupportedException("set_ole_object_data only supports OleObject entities.");
        var oleBytes = DecodeBase64(RequiredString(arguments, "ole_base64"), "ole_base64");
        executor.ExecuteCommand(new SetOleObjectDataCommand(
            entityId,
            oleBytes,
            OptionalString(arguments, "content_type") ?? oleObject.ContentType,
            OptionalString(arguments, "source_name") ?? oleObject.SourceName));
        executor.Session.SelectEntities([entityId]);
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
        var document = executor.Session.CadEditor.Document;
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
            ReferenceEquals(executor.Session, document.Session))
        {
            return executor;
        }

        executor = new CadDocumentToolExecutor(document.Session, Guid.NewGuid());
        _documentExecutors[document.DocumentId] = executor;
        return executor;
    }

    private static JsonDocument AddEntityIds(JsonElement source, EntityId entityId)
    {
        var node = JsonNode.Parse(source.GetRawText())!.AsObject();
        node["entity_ids"] = new JsonArray(JsonValue.Create(entityId.Value));
        return JsonDocument.Parse(node.ToJsonString());
    }

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

    private static readonly string[] CommonAppearanceFields =
    [
        "color_source", "color", "line_weight", "graphic_style", "z_index", "visible"
        , "locked"
    ];

    private static readonly string[] StrokeFields =
    [
        "start_cap", "end_cap", "dash_cap", "dash_style", "line_join"
    ];

    private static string Success(object value) => Direct2dCad.AI.Contracts.CadJson.Serialize(new { success = true, result = value });
    private static string Error(string message) => Direct2dCad.AI.Contracts.CadJson.Serialize(new { success = false, error = message });
}
