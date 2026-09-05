using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO.FileFormat.Entities;
using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.Tests;

public sealed class BinarySnapshotTests
{
    [Fact]
    public async Task SaveSnapshotKeepsOriginalBinaryDataAfterReplacement()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cad-snapshot-{Guid.NewGuid():N}.d2cad");
        try
        {
            var document = CadDocument.Create("Binary snapshot");
            var pixels = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
            var bytes = new byte[] { 10, 20, 30, 40 };
            var bounds = CadRectD.FromXYWH(0, 0, 4, 4);
            var image = document.AddImage(bounds, 4, 4, 16, pixels);
            var ole = document.AddOleObject(bounds, bytes);
            var storage = new CadDocumentStorage();
            var save = storage.SaveAsync(document, path);
            image.SetImageData(4, 4, 16, new byte[64]);
            ole.SetOleData([99]);
            image.SetOpacity(0.25);
            await save;

            var loaded = await storage.LoadAsync(path);
            Assert.Equal(pixels, Assert.IsType<CadImage>(loaded.GetEntity(image.Id)).CopyPixels());
            Assert.Equal(bytes, Assert.IsType<CadOleObject>(loaded.GetEntity(ole.Id)).CopyOleBytes());
            Assert.Equal(1.0, Assert.IsType<CadImage>(loaded.GetEntity(image.Id)).Opacity);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BinaryMemoryIsReplacedNotMutatedAndPublicListsAreReadOnly()
    {
        var document = CadDocument.Create("Ownership");
        var pixels = new byte[] { 1, 2, 3, 4 };
        var image = document.AddImage(CadRectD.FromXYWH(0, 0, 1, 1), 1, 1, 4, pixels);
        var ole = document.AddOleObject(image.Bounds, pixels);
        var imageMemory = image.PixelMemory;
        var oleMemory = ole.OleMemory;
        pixels[0] = 99;
        Assert.Equal(1, imageMemory.Span[0]);
        Assert.Equal(1, oleMemory.Span[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<byte>)image.Pixels)[0] = 99);
        Assert.Throws<NotSupportedException>(() => ((IList<byte>)ole.OleBytes)[0] = 99);
        image.SetImageData(1, 1, 4, [5, 6, 7, 8]);
        ole.SetOleData([5]);
        Assert.Equal(1, imageMemory.Span[0]);
        Assert.Equal(1, oleMemory.Span[0]);
        Assert.Equal(5, image.Pixels[0]);
        Assert.Equal(5, ole.OleBytes[0]);
        Assert.Throws<ArgumentException>(() => image.SetImageData(100, 100, 400, [1]));
        Assert.Equal(1, image.PixelWidth);
        Assert.Equal(1, image.PixelHeight);
        Assert.Equal(4, image.Stride);
        Assert.Equal(5, image.Pixels[0]);
    }

    [Fact]
    public void ReadOnlyMemoryRetainsTheExistingMessagePackBinaryFormat()
    {
        byte[] bytes = [1, 2, 3, 4];
        var image = new CadImageData { Pixels = bytes };
        var legacyImage = MessagePackSerializer.Deserialize<LegacyImage>(MessagePackSerializer.Serialize(image));
        Assert.Equal(bytes, legacyImage.Pixels);
        Assert.Equal(bytes, MessagePackSerializer.Deserialize<CadImageData>(
            MessagePackSerializer.Serialize(legacyImage)).Pixels.ToArray());
        var ole = new CadOleObjectData { OleBytes = bytes };
        var legacyOle = MessagePackSerializer.Deserialize<LegacyOle>(MessagePackSerializer.Serialize(ole));
        Assert.Equal(bytes, legacyOle.OleBytes);
        Assert.Equal(bytes, MessagePackSerializer.Deserialize<CadOleObjectData>(
            MessagePackSerializer.Serialize(legacyOle)).OleBytes.ToArray());
    }

    [MessagePackObject]
    public sealed class LegacyImage
    {
        [Key(0)] public CadEntityData Entity { get; set; } = new();
        [Key(1)] public CadPointData Min { get; set; }
        [Key(2)] public CadPointData Max { get; set; }
        [Key(3)] public int PixelWidth { get; set; }
        [Key(4)] public int PixelHeight { get; set; }
        [Key(5)] public int Stride { get; set; }
        [Key(6)] public byte[] Pixels { get; set; } = [];
        [Key(7)] public string ContentType { get; set; } = "image/bgra32";
        [Key(8)] public string SourceName { get; set; } = "";
        [Key(9)] public double Opacity { get; set; } = 1;
        [Key(10)] public double RotationRadians { get; set; }
    }

    [MessagePackObject]
    public sealed class LegacyOle
    {
        [Key(0)] public CadEntityData Entity { get; set; } = new();
        [Key(1)] public CadPointData Min { get; set; }
        [Key(2)] public CadPointData Max { get; set; }
        [Key(7)] public byte[] OleBytes { get; set; } = [];
        [Key(8)] public string ContentType { get; set; } = "application/x-ole-storage";
        [Key(9)] public string SourceName { get; set; } = "";
        [Key(10)] public double Opacity { get; set; } = 1;
    }
}
