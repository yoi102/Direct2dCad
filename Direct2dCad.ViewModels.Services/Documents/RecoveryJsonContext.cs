using System.Text.Json.Serialization;
namespace Direct2dCad.ViewModels.Services.Documents;
[JsonSerializable(typeof(CadRecoveryEntry))]
internal partial class RecoveryJsonContext : JsonSerializerContext;
