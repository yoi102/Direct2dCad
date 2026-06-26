using System.Text;
using MessagePack;

namespace Direct2dCad.IO.Header.V1;

public enum CadPayloadFormat : byte
{
    MessagePack = 1
}

public enum CadCompressionKind : byte
{
    None = 0,
    MessagePackLz4BlockArray = 1
}

public readonly record struct CadFileHeader(
    int FileVersion,
    CadPayloadFormat PayloadFormat,
    CadCompressionKind Compression);








public static class CadFileHeaderIO
{
    private static readonly byte[] Magic = "D2CAD"u8.ToArray();

    public static void Write(BinaryWriter writer, CadFileHeader header)
    {
        writer.Write(Magic);
        writer.Write(header.FileVersion);
        writer.Write((byte)header.PayloadFormat);
        writer.Write((byte)header.Compression);
    }

    public static CadFileHeader Read(BinaryReader reader)
    {
        var magic = reader.ReadBytes(Magic.Length);

        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException("Invalid Direct2dCad file.");

        var fileVersion = reader.ReadInt32();
        var payloadFormat = (CadPayloadFormat)reader.ReadByte();
        var compression = (CadCompressionKind)reader.ReadByte();

        return new CadFileHeader(fileVersion, payloadFormat, compression);
    }


  
}


public sealed class CadDocumentStorage
{
    private const int CurrentFileVersion = 2;

    private readonly CadDocumentFileMapper _mapper = new();

    public void Save(CadDocument document, string filePath)
    {
        var fileModel = _mapper.ToFileModel(document);

        var header = new CadFileHeader(
            FileVersion: CurrentFileVersion,
            PayloadFormat: CadPayloadFormat.MessagePack,
            Compression: CadCompressionKind.MessagePackLz4BlockArray);

        using var stream = File.Create(filePath);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        CadFileHeaderIO.Write(writer, header);
        writer.Flush();

        var options = GetMessagePackOptions(header.Compression);

        MessagePackSerializer.Serialize(stream, fileModel, options);
    }


    private static readonly MessagePackSerializerOptions Lz4Options =
      MessagePackSerializerOptions.Standard
          .WithCompression(MessagePackCompression.Lz4BlockArray);

    private static readonly MessagePackSerializerOptions NoCompressionOptions =
        MessagePackSerializerOptions.Standard;

    private static MessagePackSerializerOptions GetMessagePackOptions(
    CadCompressionKind compression)
    {
        return compression switch
        {
            CadCompressionKind.None => NoCompressionOptions,

            CadCompressionKind.MessagePackLz4BlockArray => Lz4Options,

            _ => throw new NotSupportedException(
                $"Unsupported compression: {compression}")
        };
    }














    private readonly CadFileMigrationService _migrationService = new();

    public CadDocument Load(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var header = CadFileHeaderIO.Read(reader);

        if (header.PayloadFormat != CadPayloadFormat.MessagePack)
            throw new NotSupportedException(
                $"Unsupported payload format: {header.PayloadFormat}");

        var options = GetMessagePackOptions(header.Compression);

        CadDocumentFileModel latestModel = header.FileVersion switch
        {
            1 => LoadV1AndMigrate(stream, options),

            2 => MessagePackSerializer.Deserialize<CadDocumentFileModel>(
                stream,
                options),

            _ => throw new NotSupportedException(
                $"Unsupported file version: {header.FileVersion}")
        };

        return _mapper.FromFileModel(latestModel);
    }

    private CadDocumentFileModel LoadV1AndMigrate(
        Stream stream,
        MessagePackSerializerOptions options)
    {
        var v1 = MessagePackSerializer.Deserialize<CadDocumentFileModelV1>(
            stream,
            options);

        return _migrationService.Migrate(v1);
    }








}

internal class CadDocumentFileModelV1
{
}

internal class CadDocumentFileModel
{
}

internal class CadFileMigrationService
{
    internal CadDocumentFileModel Migrate(CadDocumentFileModelV1 v1) => throw new NotImplementedException();
}

public class CadDocument
{
}

internal class CadDocumentFileMapper
{
    internal CadDocument FromFileModel(CadDocumentFileModel latestModel) => throw new NotImplementedException();
    internal object ToFileModel(CadDocument document) => throw new NotImplementedException();
}













[MessagePackObject]

public class CadDocumentSaveModel
{
    [Key(0)]
    public int FileVersion { get; } = 1;



}
