using System.Text.Json.Serialization;
using Direct2dCad.AI.Contracts;
namespace Direct2dCad.AI.LmStudio;
[JsonSerializable(typeof(AiAssistantSettings))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
internal partial class AiSettingsJsonContext : JsonSerializerContext;
