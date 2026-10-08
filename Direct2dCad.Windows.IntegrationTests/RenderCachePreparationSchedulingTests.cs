using Direct2dCad.wpf.Controls;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class RenderCachePreparationSchedulingTests
{
    [Fact]
    public void PendingWorkerStopsTheOwnerBatchAfterOneAttempt()
    {
        var calls = 0;
        Assert.True(CadCanvas.PrepareRenderCacheBatch(() => { calls++; return true; }, () => true));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ReadyOwnerWorkContinuesWithoutWorkerRetryDelay()
    {
        var calls = 0;
        Assert.False(CadCanvas.PrepareRenderCacheBatch(() => ++calls < 3, () => false, 1000));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void WorkerCompletionAllowsTheNextBatchToFinish()
    {
        var ready = false;
        var calls = 0;
        bool Prepare() { calls++; return !ready; }
        Assert.True(CadCanvas.PrepareRenderCacheBatch(Prepare, () => !ready));
        ready = true;
        Assert.False(CadCanvas.PrepareRenderCacheBatch(Prepare, () => !ready));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ExhaustedOwnerBudgetLeavesWorkPendingForAnotherBatch()
    {
        var calls = 0;
        Assert.True(CadCanvas.PrepareRenderCacheBatch(() => { calls++; return true; }, () => false, 0));
        Assert.Equal(1, calls);
    }
}
