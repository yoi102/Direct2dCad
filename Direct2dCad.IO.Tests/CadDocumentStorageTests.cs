using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO.FileFormat.Container;

namespace Direct2dCad.IO.Tests;

public sealed class CadDocumentStorageTests
{
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
            var blockId = document.CreateBlockDefinition("Valve", new CadPointD(1, 2));
            var line = document.AddLine(
                new CadPointD(-3, 4),
                new CadPointD(12, 18),
                layerId,
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

            Assert.Equal("Assembly", loaded.Name);
            Assert.Equal(CadUnit.Inch, loaded.DocumentSettings.Unit);
            Assert.Equal(6, loaded.DocumentSettings.LengthPrecision);
            Assert.Equal(5, loaded.DocumentSettings.AnglePrecision);
            Assert.Equal(CadGridType.Cross, loaded.ViewSettings.Grid.Type);
            Assert.Equal(25, loaded.ViewSettings.Grid.SpacingX);
            Assert.Equal(5, loaded.ViewSettings.Grid.MinorSpacingY);
            Assert.Equal(new CadPointD(125, -75), loaded.ViewSettings.Origin.Position);
            Assert.Equal(42, loaded.DocumentSettings.LayerDrawingPriority.GetPriority(layerId));
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

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
