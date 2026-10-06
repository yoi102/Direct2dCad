using System.Text.Json;
using System.Text.Json.Nodes;

namespace Direct2dCad.AI.Contracts;

/// <summary>Separates a CAD capture result from its displayable text and protocol image content.</summary>
public sealed record AiToolResultContent(string Text, IReadOnlyList<AiChatContentPart> Images)
{
    public const int MaximumImageBytes = 2 * 1024 * 1024;

    public static AiToolResultContent Parse(string result)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(result);
        }
        catch (JsonException)
        {
            return new(result, []);
        }

        if (node is not JsonObject root || root["result"] is not JsonObject payload ||
            payload["image"] is not JsonObject image || !image.ContainsKey("data_base64"))
            return new(result, []);

        // Never allow binary payloads into chat history text, logs, or tool summaries.
        var encodedNode = image["data_base64"];
        image.Remove("data_base64");
        var valid = encodedNode is JsonValue encodedValue && encodedValue.TryGetValue<string>(out var encoded) &&
                    image["mime_type"] is JsonValue mimeValue && mimeValue.TryGetValue<string>(out var mime) &&
                    mime is "image/png" or "image/jpeg" or "image/webp" &&
                    encoded.Length > 0 && encoded.Length <= (MaximumImageBytes + 2) / 3 * 4 &&
                    image["width"] is JsonValue widthValue && widthValue.TryGetValue<int>(out var width) && width is > 0 and <= 1024 &&
                    image["height"] is JsonValue heightValue && heightValue.TryGetValue<int>(out var height) && height is > 0 and <= 1024;
        if (valid)
        {
            var encodedText = encodedNode!.GetValue<string>();
            var bytes = new byte[encodedText.Length / 4 * 3 + 3];
            valid = Convert.TryFromBase64String(encodedText, bytes, out var written) && written is > 0 and <= MaximumImageBytes;
        }

        if (!valid)
        {
            root["success"] = false;
            image["error"] = "The captured image is invalid or exceeds the supported image size.";
            return new(root.ToJsonString(), []);
        }

        image["delivery"] = "image content part";
        return new(root.ToJsonString(),
            [AiChatContentPart.Image($"data:{image["mime_type"]!.GetValue<string>()};base64,{encodedNode!.GetValue<string>()}")]);
    }
}
