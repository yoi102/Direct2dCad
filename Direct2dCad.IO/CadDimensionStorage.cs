using System.Text.Json;
using System.Text.Json.Serialization;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO.FileFormat.Sections;
namespace Direct2dCad.IO;

internal static class CadDimensionStorage
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options=new JsonSerializerOptions { MaxDepth=16 };
        options.Converters.Add(new PointConverter());return options;
    }
    public static CadDimensionsSection Capture(IEnumerable<CadEntity> entities) => new()
    {
        Dimensions=entities.OfType<CadDimension>().Select(d=>new CadDimensionData
        { Entity=CadDocumentMapper.ToEntityData(d), DefinitionJson=JsonSerializer.Serialize(d.Definition,Options) }).ToList()
    };
    public static void Restore(CadDocument document,CadDimensionsSection section,CancellationToken token)
    {
        foreach(var item in section.Dimensions)
        {
            token.ThrowIfCancellationRequested();
            if(item.DefinitionJson.Length>65536)throw new InvalidDataException("Dimension definition exceeds its budget.");
            var definition=JsonSerializer.Deserialize<CadDimensionDefinition>(item.DefinitionJson,Options)??throw new InvalidDataException("Missing dimension definition.");
            definition.Validate();
            var e=item.Entity;var d=document.RestoreDimension(new(e.Id),new(e.LayerId),new(e.OwnerBlockId),definition,e.Name);
            CadDocumentMapper.ApplyEntityState(document,d,e);
        }
        foreach(var dimension in document.Entities.Values.OfType<CadDimension>())dimension.RefreshAssociation(document);
    }
    private sealed class PointConverter : JsonConverter<CadPointD>
    {
        public override CadPointD Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
        {
            if(reader.TokenType!=JsonTokenType.StartArray)throw new JsonException();
            reader.Read();var x=reader.GetDouble();reader.Read();var y=reader.GetDouble();
            reader.Read();if(reader.TokenType!=JsonTokenType.EndArray)throw new JsonException();return new(x,y);
        }
        public override void Write(Utf8JsonWriter writer,CadPointD point,JsonSerializerOptions options)
        {writer.WriteStartArray();writer.WriteNumberValue(point.X);writer.WriteNumberValue(point.Y);writer.WriteEndArray();}
    }
}
