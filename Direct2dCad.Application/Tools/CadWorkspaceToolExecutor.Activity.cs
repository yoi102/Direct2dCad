using System.Text.Json;
using Direct2dCad.AI.Contracts;


namespace Direct2dCad.Application.Tools;

public sealed partial class CadWorkspaceToolExecutor
{
    private string? ActivityDocumentName(string arguments)
    {
        try
        {
            using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments);
            return ResolveDocument(json.RootElement).Name;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
        {
            return null;
        }
    }

    private async ValueTask PublishActivityAsync(AiToolCall call, string? document, string outcome, string summary)
    {
        if (_activityPublisher is null) return;
        try
        {
            // Completion/cancellation must remain visible after the operation token is canceled.
            await _activityPublisher.PublishAsync(new(call.Id, call.Name, document, outcome, summary));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.TraceError($"Unable to record CAD tool activity: {exception.Message}");
        }
    }

    private string? ActivityResultDocumentName(JsonElement root)
    {
        if (root.TryGetProperty("result", out var result)) root = result;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("document", out var document) && document.ValueKind == JsonValueKind.Object && document.TryGetProperty("name", out var name))
            return name.GetString();
        if (root.TryGetProperty("document_id", out var id) && id.ValueKind == JsonValueKind.String)
            return _workspace.GetDocuments().FirstOrDefault(d => d.DocumentId == id.GetString())?.Name;
        return null;
    }

    private static string SummarizeJson(string text)
    {
        try
        {
            using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Compact(root.ToString(), 600);
            if (root.TryGetProperty("error", out var error)) return Compact(error.ToString(), 600);
            if (root.TryGetProperty("result", out var result)) root = result;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("document_id", out _) && root.TryGetProperty("result", out result)) root = result;
            if (root.ValueKind != JsonValueKind.Object) return Compact(root.ToString(), 600);
            return Compact(string.Join(", ", root.EnumerateObject().Take(14).Select(property =>
                $"{property.Name}={property.Value.ValueKind switch
                {
                    JsonValueKind.Array => SummarizeArray(property.Value),
                    JsonValueKind.Object => "{…}",
                    _ => Compact(property.Value.ToString(), 80)
                }}")), 600);
        }
        catch (JsonException) { return "Invalid JSON arguments."; }
    }

    private static string SummarizeArray(JsonElement array)
    {
        var count = array.GetArrayLength();
        if (count > 8 || array.EnumerateArray().Any(v => v.ValueKind is JsonValueKind.Object or JsonValueKind.Array))
            return $"[{count} items]";
        return "[" + string.Join(",", array.EnumerateArray().Select(v => Compact(v.ToString(), 24))) + "]";
    }

    private static string Compact(string text, int limit)
    {
        var singleLine = text.Replace('\r', ' ').Replace('\n', ' ');
        return singleLine.Length <= limit ? singleLine : singleLine[..limit] + "…";
    }
}
