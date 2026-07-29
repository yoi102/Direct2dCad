using Direct2dCad.Db.Cad;

namespace Direct2dCad.Db.Tests;

public sealed class CadDocumentConcurrencyTests
{
    [Fact]
    public async Task WriteAccess_WaitsUntilActiveReaderReleasesDocument()
    {
        var document = CadDocument.Create("Concurrency");
        var readAccess = document.AcquireReadAccess();
        var writeAttempted = new ManualResetEventSlim();
        var writeAcquired = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var writer = Task.Run(() =>
        {
            writeAttempted.Set();
            using var access = document.AcquireWriteAccess();
            document.Rename("Updated");
            writeAcquired.SetResult();
        });

        Assert.True(writeAttempted.Wait(TimeSpan.FromSeconds(2)));
        Thread.Sleep(50);
        Assert.False(writeAcquired.Task.IsCompleted);

        readAccess.Dispose();
        await writer.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(writeAcquired.Task.IsCompletedSuccessfully);
        Assert.Equal("Updated", document.Name);
    }

    [Fact]
    public void AccessLeases_AreIdempotentAndSupportWriteRecursion()
    {
        var document = CadDocument.Create("Concurrency");
        var outer = document.AcquireWriteAccess();

        using (document.AcquireWriteAccess())
        using (document.AcquireReadAccess())
            document.Rename("Nested");

        outer.Dispose();
        outer.Dispose();

        using var readAccess = document.AcquireReadAccess();
        Assert.Equal("Nested", document.Name);
    }
}
