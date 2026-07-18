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

    public async Task SaveAsync(
        CadDocument document,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var sections = await Task.Run(() => CreateSections(document), cancellationToken).ConfigureAwait(false);
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

        byte[] headerAndTable;
        using (var buffer = new MemoryStream(tableOffset + tableLength))
        {
            using var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true);
            CadContainerFormat.WriteHeader(
                writer,
                new CadFileHeader(
                    CadContainerFormat.CurrentContainerVersion,
                    entries.Count,
                    tableOffset,
                    tableLength));
            foreach (var entry in entries)
                CadContainerFormat.WriteSectionEntry(writer, entry);
            writer.Flush();
            headerAndTable = buffer.ToArray();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        await using var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await stream.WriteAsync(headerAndTable, cancellationToken).ConfigureAwait(false);
        foreach (var section in sections)
            await stream.WriteAsync(section.Payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public CadDocument Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        using var reader = new BinaryReader(stream);
        var entries = ReadSectionTable(reader);
        var payloads = new Dictionary<CadSectionKind, SerializedSectionPayload>(entries.Count);
        foreach (var entry in entries.OrderBy(static entry => entry.PayloadOffset))
        {
            ValidateSectionEntry(entry, stream.Length);
            stream.Position = entry.PayloadOffset;
            var payload = reader.ReadBytes(entry.PayloadLength);
            if (payload.Length != entry.PayloadLength)
                throw new EndOfStreamException($"Unexpected end of section: {entry.Kind}");
            if (!payloads.TryAdd(entry.Kind, new SerializedSectionPayload(entry, payload)))
                throw new InvalidDataException($"Duplicate section: {entry.Kind}");
        }

        return LoadFromPayloads(payloads);
    }

    public async Task<CadDocument> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var headerBytes = new byte[CadContainerFormat.FileHeaderLength];
        await stream.ReadExactlyAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        CadFileHeader header;
        using (var headerReader = new BinaryReader(new MemoryStream(headerBytes, writable: false)))
            header = CadContainerFormat.ReadHeader(headerReader);
        ValidateHeader(header);
        if (header.SectionTableOffset > stream.Length - header.SectionTableLength)
            throw new InvalidDataException("Section table extends beyond the end of the file.");

        stream.Position = header.SectionTableOffset;
        var tableBytes = new byte[header.SectionTableLength];
        await stream.ReadExactlyAsync(tableBytes, cancellationToken).ConfigureAwait(false);
        var entries = ReadSectionEntries(header, tableBytes);

        var payloads = new Dictionary<CadSectionKind, SerializedSectionPayload>(entries.Count);
        foreach (var entry in entries.OrderBy(x => x.PayloadOffset))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateSectionEntry(entry, stream.Length);
            stream.Position = entry.PayloadOffset;
            var payload = new byte[entry.PayloadLength];
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            if (!payloads.TryAdd(entry.Kind, new SerializedSectionPayload(entry, payload)))
                throw new InvalidDataException($"Duplicate section: {entry.Kind}");
        }

        return await Task.Run(() => LoadFromPayloads(payloads), cancellationToken).ConfigureAwait(false);
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

    private static CadDocument LoadFromPayloads(
        IReadOnlyDictionary<CadSectionKind, SerializedSectionPayload> payloads)
    {
        var documentInfo = ReadRequiredSection<CadDocumentSection>(payloads, CadSectionKind.Document);
        var settings = ReadRequiredSection<CadSettingsSection>(payloads, CadSectionKind.Settings);
        var layers = ReadRequiredSection<CadLayerSection>(payloads, CadSectionKind.Layers);
        var styles = ReadRequiredSection<CadStylesSection>(payloads, CadSectionKind.Styles);
        var layouts = ReadOptionalSection(payloads, CadSectionKind.Layouts, new CadLayoutsSection());
        var blocks = ReadOptionalSection(payloads, CadSectionKind.Blocks, new CadBlocksSection());
        var lines = ReadRequiredSection<CadLinesSection>(payloads, CadSectionKind.Lines);
        var circles = ReadRequiredSection<CadCirclesSection>(payloads, CadSectionKind.Circles);
        var ellipses = ReadOptionalSection(payloads, CadSectionKind.Ellipses, new CadEllipsesSection());
        var arcs = ReadRequiredSection<CadArcsSection>(payloads, CadSectionKind.Arcs);
        var rectangles = ReadOptionalSection(payloads, CadSectionKind.Rectangles, new CadRectanglesSection());
        var polylines = ReadOptionalSection(payloads, CadSectionKind.Polylines, new CadPolylinesSection());
        var splines = ReadOptionalSection(payloads, CadSectionKind.Splines, new CadSplinesSection());
        var texts = ReadRequiredSection<CadTextsSection>(payloads, CadSectionKind.Texts);
        var shapeTexts = ReadOptionalSection(payloads, CadSectionKind.ShapeTexts, new CadShapeTextsSection());
        var images = ReadOptionalSection(payloads, CadSectionKind.Images, new CadImagesSection());
        var oleObjects = ReadOptionalSection(payloads, CadSectionKind.OleObjects, new CadOleObjectsSection());
        var blockReferences = ReadOptionalSection(payloads, CadSectionKind.BlockReferences, new CadBlockReferencesSection());

        return CadDocumentMapper.FromSections(
            documentInfo,
            settings,
            layers,
            styles,
            layouts,
            blocks,
            lines,
            circles,
            ellipses,
            arcs,
            rectangles,
            polylines,
            splines,
            texts,
            shapeTexts,
            images,
            oleObjects,
            blockReferences);
    }

    private static TSection ReadRequiredSection<TSection>(
        IReadOnlyDictionary<CadSectionKind, SerializedSectionPayload> payloads,
        CadSectionKind kind)
    {
        if (!payloads.TryGetValue(kind, out var section))
            throw new InvalidDataException($"Section not found: {kind}");

        return DeserializeSection<TSection>(section);
    }

    private static TSection ReadOptionalSection<TSection>(
        IReadOnlyDictionary<CadSectionKind, SerializedSectionPayload> payloads,
        CadSectionKind kind,
        TSection fallback)
    {
        return payloads.TryGetValue(kind, out var section)
            ? DeserializeSection<TSection>(section)
            : fallback;
    }

    private static TSection DeserializeSection<TSection>(SerializedSectionPayload section)
    {
        return CadSectionMigrationRegistry.ReadCurrent<TSection>(
            section.Entry.Kind,
            section.Entry.Version,
            section.Payload,
            GetMessagePackOptions(section.Entry.Compression));
    }

    private static IReadOnlyList<CadSectionEntry> ReadSectionEntries(
        CadFileHeader header,
        byte[] tableBytes)
    {
        using var reader = new BinaryReader(new MemoryStream(tableBytes, writable: false));
        var entries = new List<CadSectionEntry>(header.SectionCount);
        for (var index = 0; index < header.SectionCount; index++)
            entries.Add(CadContainerFormat.ReadSectionEntry(reader));
        return entries;
    }

    private static void ValidateHeader(CadFileHeader header)
    {
        if (header.ContainerVersion != CadContainerFormat.CurrentContainerVersion)
            throw new NotSupportedException($"Unsupported .d2cad container version: {header.ContainerVersion}");
        if (header.SectionCount < 0 ||
            header.SectionCount > int.MaxValue / CadContainerFormat.SectionEntryLength ||
            header.SectionTableOffset < CadContainerFormat.FileHeaderLength ||
            header.SectionTableLength != header.SectionCount * CadContainerFormat.SectionEntryLength)
        {
            throw new InvalidDataException("Invalid .d2cad section table.");
        }
    }

    private static void ValidateSectionEntry(CadSectionEntry entry, long fileLength)
    {
        if (entry.PayloadOffset < 0 ||
            entry.PayloadLength < 0 ||
            entry.PayloadOffset > fileLength - entry.PayloadLength)
        {
            throw new InvalidDataException($"Invalid section bounds: {entry.Kind}");
        }
    }

    private static List<SerializedSection> CreateSections(CadDocument document)
    {
        var entities = CadDocumentMapper.IndexEntities(document);

        return
        [
            Serialize(CadSectionKind.Document, CadDocumentMapper.ToDocumentSection(document)),
            Serialize(CadSectionKind.Settings, CadDocumentMapper.ToSettingsSection(document)),
            Serialize(CadSectionKind.Layers, CadDocumentMapper.ToLayerSection(document)),
            Serialize(CadSectionKind.Styles, CadDocumentMapper.ToStylesSection(document)),
            Serialize(CadSectionKind.Layouts, CadDocumentMapper.ToLayoutsSection(document)),
            Serialize(CadSectionKind.Blocks, CadDocumentMapper.ToBlocksSection(document)),
            Serialize(CadSectionKind.Lines, CadDocumentMapper.ToLinesSection(entities)),
            Serialize(CadSectionKind.Circles, CadDocumentMapper.ToCirclesSection(entities)),
            Serialize(CadSectionKind.Ellipses, CadDocumentMapper.ToEllipsesSection(entities)),
            Serialize(CadSectionKind.Arcs, CadDocumentMapper.ToArcsSection(entities)),
            Serialize(CadSectionKind.Rectangles, CadDocumentMapper.ToRectanglesSection(entities)),
            Serialize(CadSectionKind.Polylines, CadDocumentMapper.ToPolylinesSection(entities)),
            Serialize(CadSectionKind.Splines, CadDocumentMapper.ToSplinesSection(entities)),
            Serialize(CadSectionKind.Texts, CadDocumentMapper.ToTextsSection(entities)),
            Serialize(CadSectionKind.ShapeTexts, CadDocumentMapper.ToShapeTextsSection(entities)),
            Serialize(CadSectionKind.Images, CadDocumentMapper.ToImagesSection(entities)),
            Serialize(CadSectionKind.OleObjects, CadDocumentMapper.ToOleObjectsSection(entities)),
            Serialize(CadSectionKind.BlockReferences, CadDocumentMapper.ToBlockReferencesSection(document, entities))
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

    private sealed record SerializedSectionPayload(
        CadSectionEntry Entry,
        byte[] Payload);
}
