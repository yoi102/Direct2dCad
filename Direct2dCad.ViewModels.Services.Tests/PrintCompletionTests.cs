using Direct2dCad.ViewModels.Services.Platform.Printing;

namespace Direct2dCad.ViewModels.Services.Tests;

public class PrintCompletionTests
{
    [Fact]
    public async Task SubmittedWriteReportsItsEventualOutcome()
    {
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = CadPrintCompletion.ObserveAsync(writing.Task);
        Assert.False(result.IsCompleted);
        writing.SetException(new IOException("Printer offline"));
        var failure = await result;
        Assert.Equal(CadPrintCompletionStatus.Failed, failure.Status);
        Assert.Equal("Printer offline", failure.Error);
        Assert.Equal(CadPrintCompletionStatus.Cancelled,
            (await CadPrintCompletion.ObserveAsync(Task.FromCanceled(new CancellationToken(true)))).Status);
        Assert.Equal(CadPrintCompletionStatus.Completed,
            (await CadPrintCompletion.ObserveAsync(Task.CompletedTask)).Status);
    }
}
