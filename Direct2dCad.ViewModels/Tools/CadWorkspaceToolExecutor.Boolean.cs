using System.Text.Json;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Tools;

internal sealed partial class CadWorkspaceToolExecutor
{
    private static object BooleanRegionsSchema() => ObjectSchema(new Dictionary<string, object>
    {
        ["document_id"] = DocumentIdSchema(),
        ["operation"] = new { type = "string", @enum = new[] { "union", "intersection", "difference" } },
        ["entity_ids"] = new { type = "array", minItems = 2, maxItems = CadRegionBoolean.MaximumInputEdges,
            uniqueItems = true, items = new { type = "integer", minimum = 1 } },
        ["subject_entity_id"] = new { type = "integer", minimum = 1,
            description = "Required for difference: the operand to keep, minus all other operands." }
    }, ["operation", "entity_ids"]);

    private async Task<string> BooleanRegionsAsync(JsonElement arguments, CancellationToken token)
    {
        var document = ResolveDocument(arguments);
        var executor = GetExecutor(document);
        var ids = executor.ResolveEntityIdsForTool(arguments, allowSelectionFallback: false);
        var operation = RequiredString(arguments, "operation").ToLowerInvariant() switch
        {
            "union" => CadBooleanOperation.Union,
            "intersection" => CadBooleanOperation.Intersection,
            "difference" => CadBooleanOperation.Difference,
            _ => throw new ArgumentException("Use union, intersection, or difference.")
        };
        EntityId? subject = arguments.TryGetProperty("subject_entity_id", out var value) && value.ValueKind != JsonValueKind.Null ? new(value.GetInt64()) : null;
        if (ids.Length > CadRegionBoolean.MaximumInputEdges) throw new ArgumentException("Too many Boolean operands.");
        var editor = document.DocumentViewModel.CadEditor;
        var owner = editor.ActiveOwnerBlockId;
        foreach (var id in ids)
        {
            var entity = executor.GetEntityForTool(id);
            CadEntityAccessPolicy.EnsureEditable(editor.Document, entity);
            CadEntityAccessPolicy.EnsureCanAddToLayer(editor.Document, entity.LayerId);
            if (!CadRegionBoolean.Supports(entity)) throw new NotSupportedException($"Unsupported Boolean operand: {id.Value}");
        }
        var command = new BooleanRegionsCommand(ids, operation, subject);
        var result = await document.EditorTab.Operation.RunAsync(Direct2dCad.Lang.CadUiText.Get("BooleanCalculating"), async ct =>
        {
            await command.PrepareAsync(editor.Document, ct);
            ct.ThrowIfCancellationRequested();
            if (!_workspace.GetDocuments().Any(d => ReferenceEquals(d.EditorTab, document.EditorTab)))
                throw new OperationCanceledException("The document was closed.", ct);
            if (editor.ActiveOwnerBlockId != owner) throw new InvalidOperationException("The editing space changed during the operation.");
            return executor.ExecuteAtomically(() =>
            {
                executor.ExecuteCommand(command);
                var region = (CadRegion)editor.Document.GetEntity(command.ResultEntityId!.Value);
                document.DocumentViewModel.SelectEntities([region.Id]);
                return new { operation = operation.ToString(), source_entity_ids = ids.Select(id => id.Value).ToArray(),
                    subject_entity_id = subject?.Value, result_entity_id = region.Id.Value,
                    area = region.Area, contour_count = region.Contours.Count };
            });
        }, token);
        return Success(new { document_id = document.DocumentId, result });
    }
}
