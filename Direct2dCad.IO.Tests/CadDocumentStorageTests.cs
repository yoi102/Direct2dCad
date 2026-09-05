using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO.FileFormat.Container;
using Direct2dCad.IO.FileFormat.Sections;
using MessagePack;

namespace Direct2dCad.IO.Tests;

public sealed class CadDocumentStorageTests
{
    [Fact]
    public async Task SectionStreamingProducesIdenticalSyncAndAsyncFilesAndCapturesBeforeYield()
    {
        var syncPath = CreateTempPath();
        var asyncPath = CreateTempPath();
        try
        {
            var document = CadDocument.Create("Snapshot");
            for (var i = 0; i < 2000; i++)
            {
                document.AddLine(new CadPointD(i, 0), new CadPointD(i, 10));
                document.AddCircle(new CadPointD(i, 20), 5);
            }
            var text = document.AddText("Original", CadPointD.Origin, 10);
            var storage = new CadDocumentStorage();
            storage.Save(document, syncPath);
            var saving = storage.SaveAsync(document, asyncPath);
            text.SetText("Changed after capture");
            await saving;
            Assert.Equal(await File.ReadAllBytesAsync(syncPath), await File.ReadAllBytesAsync(asyncPath));
            var loaded = await storage.LoadAsync(asyncPath);
            Assert.Equal(document.Entities.Count, loaded.Entities.Count);
            Assert.Equal("Original", Assert.IsType<CadText>(loaded.GetEntity(text.Id)).Text);
        }
        finally
        {
            DeleteIfExists(syncPath);
            DeleteIfExists(asyncPath);
        }
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsDocumentStructureAndSettings()
    {
        var path = CreateTempPath();
        try
        {
            var document = CadDocument.Create("Assembly");
            document.DocumentSettings.SetUnit(CadUnit.Inch);
            document.DocumentSettings.SetLengthPrecision(6);
            document.DocumentSettings.SetAnglePrecision(5);
            document.ViewSettings.BackgroundColor = CadColor.FromArgb(255, 10, 20, 30);
            document.ViewSettings.Grid.Type = CadGridType.Cross;
            document.ViewSettings.Grid.SpacingX = 25;
            document.ViewSettings.Grid.SpacingY = 50;
            document.ViewSettings.Grid.MinorSpacingX = 2.5;
            document.ViewSettings.Grid.MinorSpacingY = 5;
            document.ViewSettings.Origin.Position = new CadPointD(125, -75);

            var layerId = document.CreateLayer("Mechanical", CadColor.Green, new CadLineWeight(0.35));
            document.DocumentSettings.LayerDrawingPriority.SetPriority(layerId, 42);
            var lineTypeId = document.CreateLineType("Center custom", [6, -2, 1, -2], "Center pattern");
            var graphicStyleId = document.CreateGraphicStyle(
                "Center style",
                CadColor.Red,
                new CadLineWeight(0.2),
                lineTypeId);
            var blockId = document.CreateBlockDefinition("Valve", new CadPointD(1, 2));
            var line = document.AddLine(
                new CadPointD(-3, 4),
                new CadPointD(12, 18),
                layerId,
                graphicStyleId: graphicStyleId,
                name: "CenterLine");
            document.MoveEntityToBlock(line.Id, blockId);
            var reference = document.AddBlockReference(
                blockId,
                new CadPointD(100, 200),
                layerId,
                rotationRadians: 0.25,
                scaleX: 2,
                scaleY: -3,
                name: "ValveRef");

            var storage = new CadDocumentStorage();
            await storage.SaveAsync(document, path);
            var loaded = await storage.LoadAsync(path);

            Assert.Equal(document.Id, loaded.Id);
            Assert.Equal("Assembly", loaded.Name);
            Assert.Equal(CadUnit.Inch, loaded.DocumentSettings.Unit);
            Assert.Equal(6, loaded.DocumentSettings.LengthPrecision);
            Assert.Equal(5, loaded.DocumentSettings.AnglePrecision);
            Assert.Equal(CadGridType.Cross, loaded.ViewSettings.Grid.Type);
            Assert.Equal(25, loaded.ViewSettings.Grid.SpacingX);
            Assert.Equal(5, loaded.ViewSettings.Grid.MinorSpacingY);
            Assert.Equal(new CadPointD(125, -75), loaded.ViewSettings.Origin.Position);
            Assert.Equal(42, loaded.DocumentSettings.LayerDrawingPriority.GetPriority(layerId));
            Assert.Equal([6, -2, 1, -2], loaded.GetLineType(lineTypeId).DashPattern);
            var loadedGraphicStyle = Assert.IsType<CadGraphicStyle>(loaded.Styles[graphicStyleId]);
            Assert.Equal(lineTypeId, loadedGraphicStyle.LineTypeId);
            Assert.Equal("Mechanical", loaded.GetLayer(layerId).Name);
            Assert.Equal(blockId, loaded.GetEntity(line.Id).OwnerBlockId);
            var loadedReference = Assert.IsType<CadBlockReference>(loaded.GetEntity(reference.Id));
            Assert.Equal(blockId, loadedReference.DefinitionBlockId);
            Assert.Equal(-3, loadedReference.ScaleY);
            Assert.Equal("ValveRef", loadedReference.Name);
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public async Task SaveAndLoadAsync_PreservesImageAndOlePayloads()
    {
        var path = CreateTempPath();
        try
        {
            var document = CadDocument.Create("Binary");
            var imagePixels = Enumerable.Range(0, 32).Select(x => (byte)(x * 7)).ToArray();
            var oleBytes = Enumerable.Range(0, 257).Select(x => (byte)(255 - x % 256)).ToArray();
            var image = document.AddImage(
                CadRectD.FromXYWH(10, 20, 4, 2),
                pixelWidth: 4,
                pixelHeight: 2,
                stride: 16,
                imagePixels,
                sourceName: "image.bin",
                opacity: 0.4,
                rotationRadians: 0.5);
            var ole = document.AddOleObject(
                CadRectD.FromXYWH(30, 40, 8, 6),
                oleBytes,
                sourceName: "sheet.ole",
                opacity: 0.65);

            var storage = new CadDocumentStorage();
            await storage.SaveAsync(document, path);
            var loaded = await storage.LoadAsync(path);

            var loadedImage = Assert.IsType<CadImage>(loaded.GetEntity(image.Id));
            Assert.Equal(imagePixels, loadedImage.CopyPixels());
            Assert.Equal(0.4, loadedImage.Opacity);
            Assert.Equal(0.5, loadedImage.RotationRadians);

            var loadedOle = Assert.IsType<CadOleObject>(loaded.GetEntity(ole.Id));
            Assert.Equal(oleBytes, loadedOle.CopyOleBytes());
            Assert.Equal(0.65, loadedOle.Opacity);
            Assert.Equal("sheet.ole", loadedOle.SourceName);
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public async Task LoadAsync_MigratesLegacyLongDocumentIdToStableGuid()
    {
        var path = CreateTempPath();
        try
        {
            const long legacyId = 42;
            var storage = new CadDocumentStorage();
            storage.Save(CadDocument.Create("Legacy document"), path);
            RewriteDocumentSectionAsLegacyVersion1(storage, path, legacyId, "Legacy document");

            var firstLoad = await storage.LoadAsync(path);
            var secondLoad = await storage.LoadAsync(path);

            Assert.NotEqual(Guid.Empty, firstLoad.Id.Value);
            Assert.Equal(firstLoad.Id, secondLoad.Id);
            Assert.Equal("Legacy document", firstLoad.Name);
            Assert.Equal(
                2,
                storage.GetCurrentSectionVersions()
                    .Single(version => version.Kind == CadSectionKind.Document)
                    .CurrentVersion);
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public async Task LoadAsync_AcceptsTransitionalGuidPayloadMarkedAsVersion1()
    {
        var path = CreateTempPath();
        try
        {
            var document = CadDocument.Create("Transitional document");
            var storage = new CadDocumentStorage();
            storage.Save(document, path);
            RewriteSectionVersion(storage, path, CadSectionKind.Document, version: 1);

            var loaded = await storage.LoadAsync(path);

            Assert.Equal(document.Id, loaded.Id);
            Assert.Equal(document.Name, loaded.Name);
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public void ReadSettings_ReturnsSavedSettingsWithoutFullLoad()
    {
        var path = CreateTempPath();
        try
        {
            var document = CadDocument.Create("Settings");
            document.ViewSettings.Grid.Type = CadGridType.Dots;
            document.ViewSettings.Grid.SpacingX = 12.5;
            document.ViewSettings.Grid.MinorSpacingX = 0.25;
            document.ViewSettings.Origin.Position = new CadPointD(17, 29);
            var storage = new CadDocumentStorage();
            storage.Save(document, path);

            var settings = storage.ReadSettings(path);

            Assert.Equal(CadGridType.Dots, settings.GridType);
            Assert.Equal(12.5, settings.GridSpacingX);
            Assert.Equal(0.25, settings.GridMinorSpacingX);
            Assert.Equal(17, settings.OriginPosition?.X);
            Assert.Equal(29, settings.OriginPosition?.Y);
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public void ReadSectionTable_InvalidMagicThrowsInvalidDataException()
    {
        var path = CreateTempPath();
        try
        {
            File.WriteAllBytes(path, "NOT-A-D2CAD-FILE"u8.ToArray());
            var storage = new CadDocumentStorage();

            Assert.Throws<InvalidDataException>(() => storage.ReadSectionTable(path));
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public async Task LoadAsync_TruncatedPayloadThrows()
    {
        var path = CreateTempPath();
        try
        {
            var storage = new CadDocumentStorage();
            await storage.SaveAsync(CadDocument.Create("Truncated"), path);
            await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
                stream.SetLength(Math.Max(1, stream.Length - 8));

            await Assert.ThrowsAsync<InvalidDataException>(async () => await storage.LoadAsync(path));
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public void CurrentSectionVersions_CoverEachSectionKindExactlyOnce()
    {
        var versions = new CadDocumentStorage().GetCurrentSectionVersions();
        var sectionKinds = Enum.GetValues<CadSectionKind>();

        Assert.Equal(sectionKinds.Length, versions.Count);
        Assert.Equal(sectionKinds.Order(), versions.Select(x => x.Kind).Order());
        Assert.All(versions, version => Assert.True(version.CurrentVersion > 0));
        Assert.All(versions, version => Assert.False(string.IsNullOrWhiteSpace(version.CurrentModelType)));
    }

    private static string CreateTempPath() =>
        Path.Combine(Path.GetTempPath(), $"Direct2dCad-{Guid.NewGuid():N}.d2cad");

    private static void RewriteDocumentSectionAsLegacyVersion1(
        CadDocumentStorage storage,
        string path,
        long legacyId,
        string name)
    {
        var entries = storage.ReadSectionTable(path);
        var sections = new List<TestSection>(entries.Count);
        using (var stream = File.OpenRead(path))
        {
            foreach (var entry in entries)
            {
                stream.Position = entry.PayloadOffset;
                var payload = new byte[entry.PayloadLength];
                stream.ReadExactly(payload);
                sections.Add(entry.Kind == CadSectionKind.Document
                    ? new TestSection(
                        entry.Kind,
                        Version: 1,
                        CadCompressionKind.None,
                        MessagePackSerializer.Serialize(new LegacyCadDocumentSection
                        {
                            Id = legacyId,
                            Name = name
                        }))
                    : new TestSection(entry.Kind, entry.Version, entry.Compression, payload));
            }
        }

        WriteSections(path, sections);
    }

    private static void RewriteSectionVersion(
        CadDocumentStorage storage,
        string path,
        CadSectionKind kind,
        int version)
    {
        var entries = storage.ReadSectionTable(path);
        var index = entries.Select((entry, index) => (entry, index))
            .Single(item => item.entry.Kind == kind)
            .index;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Position = 25 + index * 19 + sizeof(ushort);
        using var writer = new BinaryWriter(stream);
        writer.Write(version);
    }

    private static void WriteSections(string path, IReadOnlyList<TestSection> sections)
    {
        const int headerLength = 25;
        const int sectionEntryLength = 19;
        var tableLength = checked(sections.Count * sectionEntryLength);
        var payloadOffset = checked(headerLength + tableLength);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);
        writer.Write("D2CAD"u8.ToArray());
        writer.Write(1);
        writer.Write(sections.Count);
        writer.Write((long)headerLength);
        writer.Write(tableLength);

        foreach (var section in sections)
        {
            writer.Write((ushort)section.Kind);
            writer.Write(section.Version);
            writer.Write((byte)section.Compression);
            writer.Write((long)payloadOffset);
            writer.Write(section.Payload.Length);
            payloadOffset = checked(payloadOffset + section.Payload.Length);
        }

        foreach (var section in sections)
            writer.Write(section.Payload);
    }

    [MessagePackObject]
    public sealed class LegacyCadDocumentSection
    {
        [Key(0)] public long Id { get; set; }
        [Key(1)] public string Name { get; set; } = "Untitled";
    }

    private sealed record TestSection(
        CadSectionKind Kind,
        int Version,
        CadCompressionKind Compression,
        byte[] Payload);

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
