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
    private object CreateBlock(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var editor = executor.Session.CadEditor;
        var ids = executor.ResolveEntityIdsForTool(arguments, allowSelectionFallback: true);
        ValidateCreationAppearance("create_block", arguments, executor);
        var blockName = RequiredString(arguments, "name");
        var basePoint = new CadPointD(
            RequiredFinite(arguments, "base_x"),
            RequiredFinite(arguments, "base_y"));
        var referenceLayerId = HasValue(arguments, "reference_layer")
            ? executor.ResolveLayerForTool(RequiredString(arguments, "reference_layer"))
            : executor.Session.DrawingLayerId;
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
        executor.Session.SelectEntities([referenceId]);
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
        var editor = executor.Session.CadEditor;
        ValidateCreationAppearance("insert_block", arguments, executor);
        var definition = ResolveBlock(editor.Document, RequiredString(arguments, "block"));
        var layerId = HasValue(arguments, "layer")
            ? executor.ResolveLayerForTool(RequiredString(arguments, "layer"))
            : executor.Session.DrawingLayerId;
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
        executor.Session.SelectEntities([entityId]);
        return new
        {
            entity_id = entityId.Value,
            block_id = definition.Id.Value,
            block_name = definition.Name,
            applied_appearance = appearance
        };
    }

    private object ListBlocks(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var document = executor.Session.CadEditor.Document;
        return new
        {
            blocks = document.Blocks.Values
                .Where(block => block.Kind == CadBlockKind.User)
                .OrderBy(block => block.Name, StringComparer.OrdinalIgnoreCase)
                .Select(block => new
                {
                    id = block.Id.Value,
                    block.Name,
                    kind = block.Kind.ToString(),
                    is_read_only = block.IsReadOnly,
                    base_point = new { x = block.BasePoint.X, y = block.BasePoint.Y },
                    entity_count = block.EntityIds.Count,
                    reference_count = document.GetBlockReferenceCount(block.Id)
                }).ToArray()
        };
    }

    private object RenameBlock(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var document = executor.Session.CadEditor.Document;
        var block = ResolveBlock(document, RequiredString(arguments, "block"));
        var name = RequiredString(arguments, "new_name");
        var oldName = block.Name;
        executor.ExecuteCommand(new RenameBlockCommand(block.Id, name));
        return new { block_id = block.Id.Value, old_name = oldName, new_name = document.GetBlock(block.Id).Name };
    }

    private static object SetBlockBasePoint(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var block = ResolveBlock(executor.Session.CadEditor.Document, RequiredString(arguments, "block"));
        var point = new CadPointD(RequiredFinite(arguments, "x"), RequiredFinite(arguments, "y"));
        executor.ExecuteCommand(new SetBlockDefinitionBasePointCommand(block.Id, point));
        return new { block_id = block.Id.Value, base_point = new { x = point.X, y = point.Y } };
    }

    private object DeleteBlock(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        if (!OptionalBool(arguments, "confirm", false))
            throw new ArgumentException("delete_block requires confirm=true.");

        var document = executor.Session.CadEditor.Document;
        var block = ResolveBlock(document, RequiredString(arguments, "block"));
        var referenceCount = document.GetBlockReferenceCount(block.Id);
        if (referenceCount > 0)
            throw new InvalidOperationException(
                $"Block '{block.Name}' has {referenceCount} reference(s); remove or replace them before deleting the definition.");

        executor.ExecuteCommand(new DeleteBlockDefinitionCommand(block.Id));
        return new { block_id = block.Id.Value, block_name = block.Name, deleted_entity_count = block.EntityIds.Count };
    }

    private string EditBlock(JsonElement arguments)
    {
        var document = ResolveDocument(arguments);
        var block = ResolveBlock(document.Session.CadEditor.Document, RequiredString(arguments, "block"));
        document.Session.EditBlockDefinition(block.Id);
        _defaultDocumentId = document.DocumentId;
        return Success(new { document_id = document.DocumentId, block_id = block.Id.Value, block_name = block.Name, editing = true });
    }

    private string ExitBlockEdit(JsonElement arguments)
    {
        var document = ResolveDocument(arguments);
        document.Session.ExitBlockEditing();
        _defaultDocumentId = document.DocumentId;
        return Success(new { document_id = document.DocumentId, editing = false });
    }
}
