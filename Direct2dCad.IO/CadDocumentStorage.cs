using Direct2dCad.Db.Cad;
using Direct2dCad.IO.FileFormat.Container;
using Direct2dCad.IO.FileFormat.Sections;
using Direct2dCad.IO.Versioning;
using MessagePack;

namespace Direct2dCad.IO;

public sealed partial class CadDocumentStorage : ICadDocumentWriter
{
    private const int MaxSectionCount = 4096;
    private static readonly MessagePackSerializerOptions Lz4Options =
        MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.Lz4BlockArray);

    private static readonly MessagePackSerializerOptions NoCompressionOptions =
        MessagePackSerializerOptions.Standard;

    public void Save(CadDocument document, string filePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        WriteSectionsAsync(CreateSectionPayloads(document), filePath, asyncIo: false, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    public async Task SaveAsync(
        CadDocument document,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        // Capture mutable state on the editor thread. Image/OLE storage is immutable
        // after capture and can be shared until its section is written.
        var payloads = CreateSectionPayloads(document);
        await Task.Run(() => WriteSectionsAsync(payloads, filePath, asyncIo: true, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteSectionsAsync(
        Queue<ISectionPayload> payloads,
        string filePath,
        bool asyncIo,
        CancellationToken cancellationToken)
    {
        var tableOffset = CadContainerFormat.FileHeaderLength;
        var tableLength = checked(payloads.Count * CadContainerFormat.SectionEntryLength);
        var entries = new List<CadSectionEntry>(payloads.Count);
        var destinationPath = PrepareDestinationPath(filePath);
        var temporaryPath = CreateTemporaryPath(destinationPath);
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 64 * 1024,
                       asyncIo ? FileOptions.Asynchronous : FileOptions.None))
            {
                // Reserve the directory, then release each DTO and compressed payload after writing.
                stream.Position = checked(tableOffset + tableLength);
                while (payloads.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var section = payloads.Dequeue().Serialize();
                    entries.Add(new CadSectionEntry(section.Kind,
                        CadSectionMigrationRegistry.GetCurrentVersion(section.Kind), section.Compression,
                        stream.Position, section.Payload.Length));
                    if (asyncIo)
                        await stream.WriteAsync(section.Payload, cancellationToken).ConfigureAwait(false);
                    else
                        stream.Write(section.Payload);
                }

                cancellationToken.ThrowIfCancellationRequested();
                stream.Position = 0;
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    CadContainerFormat.WriteHeader(writer, new CadFileHeader(
                        CadContainerFormat.CurrentContainerVersion, entries.Count, tableOffset, tableLength));
                    foreach (var entry in entries)
                        CadContainerFormat.WriteSectionEntry(writer, entry);
                }
                if (asyncIo)
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                else
                    stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            CommitTemporaryFile(temporaryPath, destinationPath);
        }
        finally
        {
            payloads.Clear();
            DeleteTemporaryFile(temporaryPath);
        }
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
        cancellationToken.ThrowIfCancellationRequested();

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
        var entries = ReadSectionEntries(header, tableBytes, stream.Length);

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

        ValidateSectionEntry(entry, stream.Length);
        stream.Position = entry.PayloadOffset;
        var payload = reader.ReadBytes(entry.PayloadLength);
        if (payload.Length != entry.PayloadLength)
            throw new EndOfStreamException($"Unexpected end of section: {entry.Kind}");
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
        ValidateHeader(header);
        if (header.SectionTableOffset > reader.BaseStream.Length - header.SectionTableLength)
            throw new InvalidDataException("Section table extends beyond the end of the file.");

        reader.BaseStream.Position = header.SectionTableOffset;

        var entries = new List<CadSectionEntry>(header.SectionCount);
        for (var i = 0; i < header.SectionCount; i++)
            entries.Add(CadContainerFormat.ReadSectionEntry(reader));

        ValidateSectionEntries(
            entries,
            reader.BaseStream.Length,
            checked(header.SectionTableOffset + header.SectionTableLength));
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
        var compositePaths = ReadOptionalSection(payloads, CadSectionKind.CompositePaths, new CadCompositePathsSection());
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
            compositePaths,
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
        byte[] tableBytes,
        long fileLength)
    {
        using var reader = new BinaryReader(new MemoryStream(tableBytes, writable: false));
        var entries = new List<CadSectionEntry>(header.SectionCount);
        for (var index = 0; index < header.SectionCount; index++)
            entries.Add(CadContainerFormat.ReadSectionEntry(reader));

        ValidateSectionEntries(
            entries,
            fileLength,
            checked(header.SectionTableOffset + header.SectionTableLength));
        return entries;
    }

    private static void ValidateHeader(CadFileHeader header)
    {
        if (header.ContainerVersion != CadContainerFormat.CurrentContainerVersion)
            throw new NotSupportedException($"Unsupported .d2cad container version: {header.ContainerVersion}");
        if (header.SectionCount < 0 ||
            header.SectionCount > MaxSectionCount ||
            header.SectionCount > int.MaxValue / CadContainerFormat.SectionEntryLength ||
            header.SectionTableOffset < CadContainerFormat.FileHeaderLength ||
            header.SectionTableLength != header.SectionCount * CadContainerFormat.SectionEntryLength)
        {
            throw new InvalidDataException("Invalid .d2cad section table.");
        }
    }

    private static void ValidateSectionEntries(
        IReadOnlyList<CadSectionEntry> entries,
        long fileLength,
        long sectionTableEnd)
    {
        var kinds = new HashSet<CadSectionKind>();
        foreach (var entry in entries)
        {
            if (!kinds.Add(entry.Kind))
                throw new InvalidDataException($"Duplicate section: {entry.Kind}");

            ValidateSectionEntry(entry, fileLength, sectionTableEnd);
        }

        var previousPayloadEnd = sectionTableEnd;
        foreach (var entry in entries.OrderBy(static entry => entry.PayloadOffset))
        {
            if (entry.PayloadOffset < previousPayloadEnd)
            {
                throw new InvalidDataException(
                    $"Overlapping section payload: {entry.Kind}");
            }

            previousPayloadEnd = checked(entry.PayloadOffset + entry.PayloadLength);
        }
    }

    private static void ValidateSectionEntry(
        CadSectionEntry entry,
        long fileLength,
        long minimumPayloadOffset = 0)
    {
        if (entry.PayloadOffset < minimumPayloadOffset ||
            entry.PayloadLength < 0 ||
            entry.PayloadOffset > fileLength - entry.PayloadLength)
        {
            throw new InvalidDataException($"Invalid section bounds: {entry.Kind}");
        }
    }

    private static Queue<ISectionPayload> CreateSectionPayloads(CadDocument document)
    {
        var entities = CadDocumentMapper.IndexEntities(document);

        return new Queue<ISectionPayload>(
        [
            Capture(CadSectionKind.Document, CadDocumentMapper.ToDocumentSection(document)),
            Capture(CadSectionKind.Settings, CadDocumentMapper.ToSettingsSection(document)),
            Capture(CadSectionKind.Layers, CadDocumentMapper.ToLayerSection(document)),
            Capture(CadSectionKind.Styles, CadDocumentMapper.ToStylesSection(document)),
            Capture(CadSectionKind.Layouts, CadDocumentMapper.ToLayoutsSection(document)),
            Capture(CadSectionKind.Blocks, CadDocumentMapper.ToBlocksSection(document)),
            Capture(CadSectionKind.Lines, CadDocumentMapper.ToLinesSection(entities)),
            Capture(CadSectionKind.Circles, CadDocumentMapper.ToCirclesSection(entities)),
            Capture(CadSectionKind.Ellipses, CadDocumentMapper.ToEllipsesSection(entities)),
            Capture(CadSectionKind.Arcs, CadDocumentMapper.ToArcsSection(entities)),
            Capture(CadSectionKind.Rectangles, CadDocumentMapper.ToRectanglesSection(entities)),
            Capture(CadSectionKind.Polylines, CadDocumentMapper.ToPolylinesSection(entities)),
            Capture(CadSectionKind.Splines, CadDocumentMapper.ToSplinesSection(entities)),
            Capture(CadSectionKind.CompositePaths, CadDocumentMapper.ToCompositePathsSection(entities)),
            Capture(CadSectionKind.Texts, CadDocumentMapper.ToTextsSection(entities)),
            Capture(CadSectionKind.ShapeTexts, CadDocumentMapper.ToShapeTextsSection(entities)),
            Capture(CadSectionKind.Images, CadDocumentMapper.ToImagesSection(entities)),
            Capture(CadSectionKind.OleObjects, CadDocumentMapper.ToOleObjectsSection(entities)),
            Capture(CadSectionKind.BlockReferences, CadDocumentMapper.ToBlockReferencesSection(document, entities))
        ]);
    }

    private static ISectionPayload Capture<TPayload>(CadSectionKind kind, TPayload payload) =>
        new SectionPayload<TPayload>(kind, payload);

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

        ValidateSectionEntry(entry, stream.Length);
        stream.Position = entry.PayloadOffset;
        var payload = reader.ReadBytes(entry.PayloadLength);
        if (payload.Length != entry.PayloadLength)
            throw new EndOfStreamException($"Unexpected end of section: {entry.Kind}");
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

    private static string PrepareDestinationPath(string filePath)
    {
        var destinationPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        return destinationPath;
    }

    private static string CreateTemporaryPath(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath)!;
        var fileName = Path.GetFileName(destinationPath);
        return Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
    }

    private static void CommitTemporaryFile(string temporaryPath, string destinationPath) =>
        File.Move(temporaryPath, destinationPath, overwrite: true);

    private static void DeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch
        {
            // A failed save must not hide its original exception because cleanup also failed.
        }
    }

    private sealed record SerializedSection(
        CadSectionKind Kind,
        CadCompressionKind Compression,
        byte[] Payload);

    private interface ISectionPayload
    {
        SerializedSection Serialize();
    }

    private sealed record SectionPayload<TPayload>(
        CadSectionKind Kind,
        TPayload Payload) : ISectionPayload
    {
        public SerializedSection Serialize() =>
            CadDocumentStorage.Serialize(Kind, Payload);
    }

    private sealed record SerializedSectionPayload(
        CadSectionEntry Entry,
        byte[] Payload);
}
