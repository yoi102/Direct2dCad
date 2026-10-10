using System.Text.Json.Serialization;
using Direct2dCad.Db.Data.Entities;
namespace Direct2dCad.IO;
[JsonSerializable(typeof(CadDimensionDefinition))]
internal partial class DimensionJsonContext : JsonSerializerContext;
