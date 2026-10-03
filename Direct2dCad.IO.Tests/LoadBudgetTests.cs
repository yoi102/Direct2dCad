using System.Buffers;
using Direct2dCad.Db.Cad;
using Direct2dCad.IO.FileFormat.Container;
using MessagePack;

namespace Direct2dCad.IO.Tests;

public class LoadBudgetTests
{
    [Fact]
    public async Task UnknownContentIsReadOnlyAndCannotBeOrdinarilySaved()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".d2cad");
        try
        {
            var storage = new CadDocumentStorage();
            storage.Save(CadDocument.Create("Unknown"), path);
            var entries = storage.ReadSectionTable(path);
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.BaseStream.Position = 25 + entries.ToList().FindIndex(e => e.Kind == CadSectionKind.OleObjects) * 19;
                writer.Write((ushort)65000);
            }
            var document = await storage.LoadAsync(path);
            Assert.True(document.IsReadOnly);
            Assert.Throws<InvalidOperationException>(() => storage.Save(document, path));
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveAsync(document, path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task HugeDeclaredCollectionIsRejectedBeforeDtoAllocation()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".d2cad");
        try
        {
            var storage = new CadDocumentStorage();
            storage.Save(CadDocument.Create("Budget"), path);
            var entries = storage.ReadSectionTable(path);
            var buffer = new ArrayBufferWriter<byte>();
            var pack = new MessagePackWriter(buffer);
            pack.WriteArrayHeader(1);
            pack.WriteArrayHeader(1_000_001);
            for (var i = 0; i < 1_000_001; i++) pack.WriteNil();
            pack.Flush();
            using (var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
            using (var writer = new BinaryWriter(stream))
            {
                var offset = stream.Length;
                stream.Position = offset;
                writer.Write(buffer.WrittenSpan);
                stream.Position = 25 + entries.ToList().FindIndex(e => e.Kind == CadSectionKind.Lines) * 19 + 6;
                writer.Write((byte)CadCompressionKind.None);
                writer.Write(offset);
                writer.Write(buffer.WrittenCount);
            }
            var error = await Assert.ThrowsAnyAsync<Exception>(() => storage.LoadAsync(path));
            Assert.True(error.ToString().Contains("item budget"), error.ToString());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task FileAndTotalDecodedBudgetsApplyToSyncAndAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".d2cad");
        try
        {
            new CadDocumentStorage().Save(CadDocument.Create("Budget"), path);
            var tinyFile = new CadDocumentStorage { LoadLimits = new() { MaximumFileBytes = 50 } };
            Assert.Throws<InvalidDataException>(() => tinyFile.Load(path));
            await Assert.ThrowsAsync<InvalidDataException>(() => tinyFile.LoadAsync(path));
            var tinyDecoded = new CadDocumentStorage { LoadLimits = new() { MaximumDecodedBytes = 10 } };
            await Assert.ThrowsAsync<InvalidDataException>(() => tinyDecoded.LoadAsync(path));
        }
        finally { File.Delete(path); }
    }
}
