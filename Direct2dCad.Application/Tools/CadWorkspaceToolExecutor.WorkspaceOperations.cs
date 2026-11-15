using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Commands;
using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO;

namespace Direct2dCad.Application.Tools;

public sealed partial class CadWorkspaceToolExecutor
{
    private static CadToolSaveOptions SaveOptions(JsonElement arguments) => new(
        OptionalBool(arguments, "overwrite", false), OptionalString(arguments, "expected_sha256"),
        OptionalBool(arguments, "interactive", true));

    private static object[] OverwriteRequirements() =>
    [
        new { @if = new { required = new[] { "expected_sha256" } },
            then = new { required = new[] { "overwrite" }, properties = new { overwrite = new { @const = true } } } }
    ];

    private static string GetFileRevision(JsonElement arguments)
    {
        var revision = CadFileRevision.Capture(RequiredString(arguments, "file_path"));
        return Success(new { file_path = revision.FullPath, exists = revision.Exists, length = revision.Length, sha256 = revision.Hash });
    }

    private static string ListDocumentTemplates() => Success(new
    {
        templates = CadEngineeringTemplates.Keys.Select(key =>
        {
            var document = CadEngineeringTemplates.Create(key);
            var layout = document.Layouts.Values.Single();
            return new { key, unit = document.DocumentSettings.Unit.ToString(),
                paper_width = layout.PaperWidth, paper_height = layout.PaperHeight,
                viewport_scale = layout.Viewports.Single().Scale };
        }).ToArray()
    });

    private string ListRecoveryCopies() => Success(new
    {
        copies = _workspace.ListRecoveryCopies().Select(entry => new
        {
            recovery_id = entry.RecoveryId, source_document_id = entry.SourceDocumentId,
            name = entry.Name, source_path = entry.SourcePath, saved_at = entry.SavedAt,
            document_version = entry.DocumentVersion
        }).ToArray()
    });

    private async Task<string> OpenRecoveryCopyAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var document = await _workspace.OpenRecoveryCopyAsync(RequiredString(arguments, "recovery_id"), cancellationToken);
        _defaultDocumentId = document.DocumentId;
        return Success(new { document = DocumentDto(document) });
    }

    private async Task<string> ClearRecoveryCopiesAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!OptionalBool(arguments, "confirm", false)) throw new ArgumentException("confirm=true is required.");
        await _workspace.ClearRecoveryCopiesAsync(OptionalString(arguments, "source_document_id"), cancellationToken);
        return Success(new { cleared = true });
    }

    private async Task<string> SaveCompatibleCopyAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!OptionalBool(arguments, "allow_loss", false)) throw new ArgumentException("allow_loss=true is required for a compatible copy.");
        var document = ResolveDocument(arguments);
        var saved = await _workspace.SaveCompatibleCopyAsync(document.DocumentId, RequiredString(arguments, "file_path"),
            SaveOptions(arguments) with { Interactive = false }, cancellationToken);
        return saved ? Success(new { saved, document = DocumentDto(_workspace.GetRequiredDocument(document.DocumentId)) })
            : Error("Compatible copy did not complete.");
    }

    private string TransferEntities(JsonElement arguments)
    {
        var source = _workspace.GetRequiredDocument(RequiredString(arguments, "source_document_id"));
        var target = _workspace.GetRequiredDocument(RequiredString(arguments, "target_document_id"));
        var sourceExecutor = GetExecutor(source);
        var targetExecutor = GetExecutor(target);
        var ids = sourceExecutor.ResolveEntityIdsForTool(arguments, allowSelectionFallback: true);
        var move = RequiredString(arguments, "operation") == "move";
        if (move && !OptionalBool(arguments, "confirm", false)) throw new ArgumentException("Moving requires confirm=true.");
        if (move)
            foreach (var id in ids) CadEntityAccessPolicy.EnsureEditable(source.Session.CadEditor.Document, sourceExecutor.GetEntityForTool(id));
        var targetLayer = OptionalString(arguments, "target_layer") is { } layer
            ? targetExecutor.ResolveLayerForTool(layer) : (LayerId?)null;
        if (target.Session.CadEditor.Document.IsReadOnly) throw new InvalidOperationException("The destination document is read-only.");
        if (targetLayer is { } layerId) CadEntityAccessPolicy.EnsureCanAddToLayer(target.Session.CadEditor.Document, layerId);
        var snapshot = CadClipboardSnapshotFactory.Create(source.Session.CadEditor.Document, ids)
            ?? throw new ArgumentException("The source contains no copyable entities.");
        if (snapshot.Items.Count != ids.Length) throw new NotSupportedException("Every source entity must support clipboard copying.");
        if (targetLayer is null)
            foreach (var item in snapshot.Items)
            {
                var destinationLayer = target.Session.CadEditor.Document.Layers.Values.FirstOrDefault(layer =>
                    item.Layer.IsDefault ? layer.Id == LayerId.Default : string.Equals(layer.Name, item.Layer.Name, StringComparison.OrdinalIgnoreCase));
                if (destinationLayer is not null) CadEntityAccessPolicy.EnsureCanAddToLayer(target.Session.CadEditor.Document, destinationLayer.Id);
            }
        var command = new PasteEntitiesCommand(snapshot,
            new CadVectorD(OptionalFinite(arguments, "delta_x", 0), OptionalFinite(arguments, "delta_y", 0)),
            targetLayer, target.Session.CadEditor.ActiveOwnerBlockId);

        object Paste()
        {
            targetExecutor.ExecuteCommand(command);
            if (command.CreatedEntityIds.Count != ids.Length) throw new InvalidOperationException("The destination did not accept every source entity.");
            if (move) sourceExecutor.ExecuteCommand(new DeleteEntitiesCommand(ids));
            return new
            {
                operation = move ? "move" : "copy", source_document_id = source.DocumentId, target_document_id = target.DocumentId,
                source_entity_ids = ids.Select(id => id.Value).ToArray(),
                created_entity_ids = command.CreatedEntityIds.Select(id => id.Value).ToArray()
            };
        }

        // Both document batches roll back if either half of a cross-document move fails.
        var result = move && source.DocumentId != target.DocumentId
            ? sourceExecutor.ExecuteAtomically(() => targetExecutor.ExecuteAtomically(Paste))
            : targetExecutor.ExecuteAtomically(Paste);
        target.Session.SelectEntities(command.CreatedEntityIds);
        source.Session.RequestRender();
        target.Session.RequestRender();
        return Success(result);
    }

    private static IEnumerable<AiToolDefinition> WorkspaceOperationsToolDefinitions()
    {
        yield return Tool("list_document_templates", "List every engineering template key, display unit and paper layout. Create one with create_document(template=key).",
            ObjectSchema(new Dictionary<string, object>()));
        yield return Tool("get_file_revision", "Read a file's existence, byte length and SHA-256 for explicit save conflict decisions.",
            ObjectSchema(new Dictionary<string, object> { ["file_path"] = StringSchema("Absolute file path") }, ["file_path"]));
        yield return Tool("list_recovery_copies", "List all abandoned recovery versions. recovery_id identifies a particular snapshot; live-session backups are excluded.",
            ObjectSchema(new Dictionary<string, object>()));
        yield return Tool("open_recovery_copy", "Open a recovery version as an independent unsaved drawing, retaining its recovery source. Reuses an already-open recovered tab for that source.",
            ObjectSchema(new Dictionary<string, object> { ["recovery_id"] = StringSchema("Snapshot ID from list_recovery_copies") }, ["recovery_id"]));
        yield return Tool("clear_recovery_copies", "Delete abandoned recovery copies for one source document or all abandoned sources. Live-session backups are preserved. Requires confirm=true.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["source_document_id"] = StringSchema("Recovery source document ID. Omit to clear all abandoned sources."),
                ["confirm"] = new { type = "boolean", @const = true }
            }, ["confirm"]));
        yield return Tool("save_compatible_copy", "Write a compatible copy at a separate path and switch this tab to it. Unsupported sections may be lost. Requires allow_loss=true; replacing an existing destination also requires overwrite=true.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(), ["file_path"] = StringSchema("Separate absolute destination path"),
                ["allow_loss"] = new { type = "boolean", @const = true }, ["overwrite"] = new { type = "boolean" },
                ["expected_sha256"] = StringSchema("Expected existing destination SHA-256. Requires overwrite=true.")
            }, ["file_path", "allow_loss"], allOf: OverwriteRequirements()));
        yield return Tool("transfer_entities", "Copy or cut/paste entities between open documents and their active editing spaces. Imports layers, styles, images and nested blocks through the CAD clipboard. Each document has independent undo. entity_ids defaults to the source selection. Moving requires confirm=true.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["source_document_id"] = DocumentIdSchema(), ["target_document_id"] = DocumentIdSchema(),
                ["operation"] = EnumSchema("copy", "move"), ["entity_ids"] = EntityIdsSchema(),
                ["delta_x"] = NumberSchema("World X translation in millimetres, defaults to 0"),
                ["delta_y"] = NumberSchema("World Y translation in millimetres, defaults to 0"),
                ["target_layer"] = StringSchema("Optional existing destination layer name or ID. Omit to import source layers."),
                ["confirm"] = new { type = "boolean" }
            }, ["source_document_id", "target_document_id", "operation"], allOf:
            [
                new { @if = new { properties = new { operation = new { @const = "move" } } },
                    then = new { required = new[] { "confirm" }, properties = new { confirm = new { @const = true } } } }
            ]));
    }
}
