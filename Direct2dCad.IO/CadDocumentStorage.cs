using Direct2dCad.Db.Cad;
using Direct2dCad.IO.FileFormat.Container;
using Direct2dCad.IO.FileFormat.Sections;
using Direct2dCad.IO.Versioning;
using MessagePack;

namespace Direct2dCad.IO;

public sealed class CadDocumentStorage
{
    private static readonly MessagePackSerializerOptions Lz4Options =
        MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.Lz4BlockArray);

    private static readonly MessagePackSerializerOptions NoCompressionOptions =
        MessagePackSerializerOptions.Standard;

    public void Save(CadDocument document, string filePath)
    {
        ArgumentNullException.ThrowIfNull(document);

        var sections = CreateSections(document);
        var tableOffset = CadContainerFormat.FileHeaderLength;
        var tableLength = sections.Count * CadContainerFormat.SectionEntryLength;
        var payloadOffset = tableOffset + tableLength;

        var entries = new List<CadSectionEntry>(sections.Count);
        foreach (var section in sections)
        {
            entries.Add(new CadSectionEntry(
                section.Kind,
                CadSectionMigrationRegistry.GetCurrentVersion(section.Kind),
                section.Compression,
                payloadOffset,
                section.Payload.Length));

            payloadOffset += section.Payload.Length;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);

        using var stream = File.Create(filePath);
        using var writer = new BinaryWriter(stream);

        CadContainerFormat.WriteHeader(
            writer,
            new CadFileHeader(
                CadContainerFormat.CurrentContainerVersion,
                entries.Count,
                tableOffset,
                tableLength));

        foreach (var entry in entries)
            CadContainerFormat.WriteSectionEntry(writer, entry);

        foreach (var section in sections)
            writer.Write(section.Payload);
    }

    public CadDocument Load(string filePath)
    {
        var documentInfo = ReadSection<CadDocumentSection>(filePath, CadSectionKind.Document);
        var settings = ReadSection<CadSettingsSection>(filePath, CadSectionKind.Settings);
        var layers = ReadSection<CadLayerSection>(filePath, CadSectionKind.Layers);
        var styles = ReadSection<CadStylesSection>(filePath, CadSectionKind.Styles);
        var lines = ReadSection<CadLinesSection>(filePath, CadSectionKind.Lines);
        var circles = ReadSection<CadCirclesSection>(filePath, CadSectionKind.Circles);
        var ellipses = ReadOptionalSection(filePath, CadSectionKind.Ellipses, new CadEllipsesSection());
        var arcs = ReadSection<CadArcsSection>(filePath, CadSectionKind.Arcs);
        var rectangles = ReadOptionalSection(filePath, CadSectionKind.Rectangles, new CadRectanglesSection());
        var polylines = ReadOptionalSection(filePath, CadSectionKind.Polylines, new CadPolylinesSection());
        var splines = ReadOptionalSection(filePath, CadSectionKind.Splines, new CadSplinesSection());
        var texts = ReadSection<CadTextsSection>(filePath, CadSectionKind.Texts);
        var shapeTexts = ReadOptionalSection(filePath, CadSectionKind.ShapeTexts, new CadShapeTextsSection());
        var images = ReadOptionalSection(filePath, CadSectionKind.Images, new CadImagesSection());

        return CadDocumentMapper.FromSections(
            documentInfo,
            settings,
            layers,
            styles,
            lines,
            circles,
            ellipses,
            arcs,
            rectangles,
            polylines,
            splines,
            texts,
            shapeTexts,
            images);
    }

    public CadFileHeader ReadHeader(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);
        return CadContainerFormat.ReadHeader(reader);
    }

    public IReadOnlyList<CadSectionEntry> ReadSectionTable(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);
        return ReadSectionTable(reader);
    }

    public IReadOnlyList<CadSectionVersionInfo> GetCurrentSectionVersions()
    {
        return CadSectionMigrationRegistry.GetVersionInfo();
    }

    public TSection ReadSection<TSection>(string filePath, CadSectionKind kind)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);
        var entries = ReadSectionTable(reader);
        var entry = entries.FirstOrDefault(x => x.Kind == kind);

        if (entry.Kind != kind)
            throw new InvalidDataException($"Section not found: {kind}");

        stream.Position = entry.PayloadOffset;
        var payload = reader.ReadBytes(entry.PayloadLength);
        return CadSectionMigrationRegistry.ReadCurrent<TSection>(
            entry.Kind,
            entry.Version,
            payload,
            GetMessagePackOptions(entry.Compression));
    }

    public CadSettingsSection ReadSettings(string filePath)
    {
        return ReadSection<CadSettingsSection>(filePath, CadSectionKind.Settings);
    }

    private static IReadOnlyList<CadSectionEntry> ReadSectionTable(BinaryReader reader)
    {
        var header = CadContainerFormat.ReadHeader(reader);
        if (header.ContainerVersion != CadContainerFormat.CurrentContainerVersion)
            throw new NotSupportedException($"Unsupported .d2cad container version: {header.ContainerVersion}");

        reader.BaseStream.Position = header.SectionTableOffset;

        var entries = new List<CadSectionEntry>(header.SectionCount);
        for (var i = 0; i < header.SectionCount; i++)
            entries.Add(CadContainerFormat.ReadSectionEntry(reader));

        return entries;
    }

    private static List<SerializedSection> CreateSections(CadDocument document)
    {
        return
        [
            Serialize(CadSectionKind.Document, CadDocumentMapper.ToDocumentSection(document)),
            Serialize(CadSectionKind.Settings, CadDocumentMapper.ToSettingsSection(document)),
            Serialize(CadSectionKind.Layers, CadDocumentMapper.ToLayerSection(document)),
            Serialize(CadSectionKind.Styles, CadDocumentMapper.ToStylesSection(document)),
            Serialize(CadSectionKind.Lines, CadDocumentMapper.ToLinesSection(document)),
            Serialize(CadSectionKind.Circles, CadDocumentMapper.ToCirclesSection(document)),
            Serialize(CadSectionKind.Ellipses, CadDocumentMapper.ToEllipsesSection(document)),
            Serialize(CadSectionKind.Arcs, CadDocumentMapper.ToArcsSection(document)),
            Serialize(CadSectionKind.Rectangles, CadDocumentMapper.ToRectanglesSection(document)),
            Serialize(CadSectionKind.Polylines, CadDocumentMapper.ToPolylinesSection(document)),
            Serialize(CadSectionKind.Splines, CadDocumentMapper.ToSplinesSection(document)),
            Serialize(CadSectionKind.Texts, CadDocumentMapper.ToTextsSection(document)),
            Serialize(CadSectionKind.ShapeTexts, CadDocumentMapper.ToShapeTextsSection(document)),
            Serialize(CadSectionKind.Images, CadDocumentMapper.ToImagesSection(document))
        ];
    }

    private static TSection ReadOptionalSection<TSection>(
        string filePath,
        CadSectionKind kind,
        TSection fallback)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);
        var entries = ReadSectionTable(reader);
        var entry = entries.FirstOrDefault(x => x.Kind == kind);

        if (entry.Kind != kind)
            return fallback;

        stream.Position = entry.PayloadOffset;
        var payload = reader.ReadBytes(entry.PayloadLength);
        return CadSectionMigrationRegistry.ReadCurrent<TSection>(
            entry.Kind,
            entry.Version,
            payload,
            GetMessagePackOptions(entry.Compression));
    }

    private static SerializedSection Serialize<TPayload>(CadSectionKind kind, TPayload payload)
    {
        return new SerializedSection(
            kind,
            CadCompressionKind.MessagePackLz4BlockArray,
            MessagePackSerializer.Serialize(payload, Lz4Options));
    }

    private static MessagePackSerializerOptions GetMessagePackOptions(CadCompressionKind compression)
    {
        return compression switch
        {
            CadCompressionKind.None => NoCompressionOptions,
            CadCompressionKind.MessagePackLz4BlockArray => Lz4Options,
            _ => throw new NotSupportedException($"Unsupported compression: {compression}")
        };
    }

    private sealed record SerializedSection(
        CadSectionKind Kind,
        CadCompressionKind Compression,
        byte[] Payload);
}
