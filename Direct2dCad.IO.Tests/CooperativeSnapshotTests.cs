using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.IO.Tests;

public sealed class CooperativeSnapshotTests
{
    [Fact]
    public async Task CooperativeSnapshotMatchesSynchronousFormatAndPreservesOrdering()
    {
        var document = CadDocument.Create("Snapshot");
        for (var index = 0; index < 513; index++)
        {
            document.AddLine(new(index, 0), new(index, 10));
            document.AddSpline([new(index, 0), new(index, 2), new(index + 1, 3)]);
        }
        document.AddImage(CadRectD.FromXYWH(0, 0, 1, 1), 1, 1, 4, [0, 0, 0, 255]);
        var path = Path.GetTempFileName();
        var expectedPath = Path.GetTempFileName();
        try
        {
            var storage = new CadDocumentStorage();
            storage.Save(document, expectedPath);
            var yields = 0;
            await storage.SaveAsync(document, path, new CadSnapshotCaptureOptions(() => true, _ => { yields++; return ValueTask.CompletedTask; }));
            Assert.True(yields > 0);
            Assert.Equal(await File.ReadAllBytesAsync(expectedPath), await File.ReadAllBytesAsync(path));
            Assert.Equal(document.Entities.Count, storage.Load(path).Entities.Count);
        }
        finally { File.Delete(path); File.Delete(expectedPath); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrEditAtYieldNeverReplacesExistingFile(bool cancel)
    {
        var document = CadDocument.Create("Original");
        for (var i = 0; i < 300; i++) document.AddLine(new(i, 0), new(i, 1));
        var storage = new CadDocumentStorage();
        var path = Path.GetTempFileName();
        using var cancellation = new CancellationTokenSource();
        try
        {
            storage.Save(document, path);
            var original = await File.ReadAllBytesAsync(path);
            var current = true;
            var yields = 0;
            var capture = new CadSnapshotCaptureOptions(() => current, _ =>
            {
                if (++yields < 6) return ValueTask.CompletedTask;
                document.AddLine(CadPointD.Origin, new(1, 1));
                current = false;
                if (cancel) cancellation.Cancel();
                return ValueTask.CompletedTask;
            }) { MaximumSliceDuration = TimeSpan.Zero };
            if (cancel)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.SaveAsync(document, path, capture, cancellation.Token));
            else
                await Assert.ThrowsAsync<CadSnapshotChangedException>(() => storage.SaveAsync(document, path, capture));
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }
}
