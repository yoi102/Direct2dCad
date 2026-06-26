using System;
using System.Collections.Generic;
using System.Text;
using MessagePack;

namespace Direct2dCad.IO;

public enum CadSectionKind : ushort
{
    Document = 1,
    Settings = 2,

    Layers = 10,
    Styles = 11,
    Blocks = 12,

    Lines = 100,
    Circles = 101,
    Arcs = 102,
    Polylines = 103,
    Texts = 104,
    MTexts = 105,
    Hatches = 106
}
public enum CadCompressionKind : byte
{
    None = 0,
    MessagePackLz4BlockArray = 1
}
public readonly record struct CadPackageHeader(
    int PackageVersion,
    int SectionCount);


public static class CadPackageHeaderIO
{
    private static readonly byte[] Magic = "D2CAD"u8.ToArray();

    public static void Write(BinaryWriter writer, CadPackageHeader header)
    {
        writer.Write(Magic);
        writer.Write(header.PackageVersion);
        writer.Write(header.SectionCount);
    }

    public static CadPackageHeader Read(BinaryReader reader)
    {
        var magic = reader.ReadBytes(Magic.Length);

        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException("Invalid Direct2dCad file.");

        var packageVersion = reader.ReadInt32();
        var sectionCount = reader.ReadInt32();

        return new CadPackageHeader(packageVersion, sectionCount);
    }
}

public readonly record struct CadSectionHeader(
    CadSectionKind Kind,
    int Version,
    CadCompressionKind Compression,
    int PayloadLength);


public static class CadSectionHeaderIO
{
    public static void Write(BinaryWriter writer, CadSectionHeader header)
    {
        writer.Write((ushort)header.Kind);
        writer.Write(header.Version);
        writer.Write((byte)header.Compression);
        writer.Write(header.PayloadLength);
    }

    public static CadSectionHeader Read(BinaryReader reader)
    {
        var kind = (CadSectionKind)reader.ReadUInt16();
        var version = reader.ReadInt32();
        var compression = (CadCompressionKind)reader.ReadByte();
        var payloadLength = reader.ReadInt32();

        return new CadSectionHeader(
            kind,
            version,
            compression,
            payloadLength);
    }
}

public sealed class CadSectionPackageWriter
{
    private static readonly MessagePackSerializerOptions Lz4Options =
        MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.Lz4BlockArray);

    public void Save(CadDocument document, string filePath)
    {
        var sections = new List<CadSerializedSection>
        {
            CreateSection(
                CadSectionKind.Document,
                version: 1,
                payload: CreateDocumentSection(document)),

            CreateSection(
                CadSectionKind.Layers,
                version: 1,
                payload: MapLayers(document)),

            CreateSection(
                CadSectionKind.Styles,
                version: 1,
                payload: MapStyles(document)),

            CreateSection(
                CadSectionKind.Lines,
                version: 1,
                payload: MapLines(document)),

            CreateSection(
                CadSectionKind.Circles,
                version: 1,
                payload: MapCircles(document)),

            CreateSection(
                CadSectionKind.Arcs,
                version: 1,
                payload: MapArcs(document)),

            CreateSection(
                CadSectionKind.Texts,
                version: 1,
                payload: MapTexts(document))
        };

        using var stream = File.Create(filePath);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        CadPackageHeaderIO.Write(
            writer,
            new CadPackageHeader(
                PackageVersion: 1,
                SectionCount: sections.Count));

        foreach (var section in sections)
        {
            CadSectionHeaderIO.Write(writer, section.Header);
            writer.Write(section.PayloadBytes);
        }
    }

    private static CadSerializedSection CreateSection<T>(
        CadSectionKind kind,
        int version,
        T payload)
    {
        var bytes = MessagePackSerializer.Serialize(payload, Lz4Options);

        var header = new CadSectionHeader(
            Kind: kind,
            Version: version,
            Compression: CadCompressionKind.MessagePackLz4BlockArray,
            PayloadLength: bytes.Length);

        return new CadSerializedSection(header, bytes);
    }
}

internal class CadSerializedSection
{
    public CadSerializedSection(CadSectionHeader header, byte[] bytes)
    {
        Header = header;
        Bytes = bytes;
    }

    public CadSectionHeader Header { get; }
    public byte[] Bytes { get; }
}

public sealed class CadSectionPackageReader
{
    private static readonly MessagePackSerializerOptions Lz4Options =
        MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.Lz4BlockArray);

    public CadDocument Load(string filePath)
    {
        var context = new CadLoadContext();

        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var packageHeader = CadPackageHeaderIO.Read(reader);

        for (var i = 0; i < packageHeader.SectionCount; i++)
        {
            var sectionHeader = CadSectionHeaderIO.Read(reader);
            var payloadBytes = reader.ReadBytes(sectionHeader.PayloadLength);

            ReadSection(sectionHeader, payloadBytes, context);
        }

        return BuildDocument(context);
    }

    private CadDocument BuildDocument(CadLoadContext context) => throw new NotImplementedException();

    private static void ReadSection(
        CadSectionHeader header,
        byte[] payloadBytes,
        CadLoadContext context)
    {
        switch (header.Kind)
        {
            case CadSectionKind.Document:
                context.Document = Deserialize<CadDocumentSectionFileModel>(
                    payloadBytes,
                    header);
                break;

            case CadSectionKind.Layers:
                context.Layers = Deserialize<List<CadLayerFileModel>>(
                    payloadBytes,
                    header);
                break;

            case CadSectionKind.Styles:
                context.Styles = ReadStylesSection(header, payloadBytes);
                break;

            case CadSectionKind.Lines:
                context.Lines = Deserialize<List<CadLineFileModel>>(
                    payloadBytes,
                    header);
                break;

            case CadSectionKind.Circles:
                context.Circles = Deserialize<List<CadCircleFileModel>>(
                    payloadBytes,
                    header);
                break;

            case CadSectionKind.Arcs:
                context.Arcs = ReadArcsSection(header, payloadBytes);
                break;

            case CadSectionKind.Texts:
                context.Texts = Deserialize<List<CadTextFileModel>>(
                    payloadBytes,
                    header);
                break;

            default:
                throw new NotSupportedException(
                    $"Unsupported section kind: {header.Kind}");
        }
    }

    private static T Deserialize<T>(
        byte[] payloadBytes,
        CadSectionHeader header)
    {
        var options = header.Compression switch
        {
            CadCompressionKind.None =>
                MessagePackSerializerOptions.Standard,

            CadCompressionKind.MessagePackLz4BlockArray =>
                Lz4Options,

            _ => throw new NotSupportedException(
                $"Unsupported compression: {header.Compression}")
        };

        return MessagePackSerializer.Deserialize<T>(payloadBytes, options);
    }
}

internal class CadLoadContext
{
    public CadLoadContext()
    {
    }

    public CadDocumentSectionFileModel Document { get; internal set; }
    public List<CadLayerFileModel> Layers { get; internal set; }
}

public class CadDocument
{
}
