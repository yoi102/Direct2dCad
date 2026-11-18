namespace Direct2dCad.ViewModels.Tests;

public sealed class CadApplicationLifetimeTests
{
    [Fact]
    public async Task RepeatedCloseReusesThePendingConfirmationAndCancelAllowsRetry()
    {
        var answer = new TaskCompletionSource<bool>();
        var shutdowns = 0;
        var exit = new CadApplicationExit(() => answer.Task, () => { shutdowns++; return Task.CompletedTask; });
        var closes = 0;

        Assert.False(exit.OnClosing(() => Task.CompletedTask, () => closes++, _ => { }));
        var first = exit.Pending;
        Assert.True(exit.IsConfirming);
        Assert.False(exit.OnClosing(() => Task.CompletedTask, () => closes++, _ => { }));
        Assert.Same(first, exit.Pending);

        answer.SetResult(false);
        await first!;
        Assert.False(exit.IsConfirming);
        Assert.False(exit.IsAuthorized);
        Assert.Equal(0, closes);
        Assert.Equal(0, shutdowns);

        answer = new TaskCompletionSource<bool>();
        Assert.False(exit.OnClosing(() => Task.CompletedTask, () => closes++, _ => { }));
        Assert.NotSame(first, exit.Pending);
    }

    [Fact]
    public async Task ConfirmedExitSavesLayoutStopsServicesThenClosesOnce()
    {
        var order = new List<string>();
        CadApplicationExit? exit = null;
        exit = new CadApplicationExit(() => Task.FromResult(true), () => { order.Add("shutdown"); return Task.CompletedTask; });

        Assert.False(exit.OnClosing(
            () => { order.Add("layout"); return Task.CompletedTask; },
            () =>
            {
                order.Add("close");
                // The real window raises Closing again from Close(); it must now go through.
                Assert.True(exit.OnClosing(() => Task.CompletedTask, () => order.Add("second close"), _ => { }));
            },
            _ => { }));
        await exit.Pending!;

        Assert.Equal(["layout", "shutdown", "close"], order);
        Assert.True(exit.IsAuthorized);
    }

    [Fact]
    public async Task FailuresAreReportedAndLeaveTheWindowOpen()
    {
        var exit = new CadApplicationExit(() => throw new InvalidOperationException("boom"), () => Task.CompletedTask);
        Exception? reported = null;

        Assert.False(exit.OnClosing(() => Task.CompletedTask, () => throw new Xunit.Sdk.XunitException("closed"), e => reported = e));
        await exit.Pending!;

        Assert.Equal("boom", reported?.Message);
        Assert.False(exit.IsAuthorized);
        Assert.False(exit.IsConfirming);
    }

    [Fact]
    public async Task PeriodicWorkRunsOnTheStartingContextWithoutOverlapAndStops()
    {
        var context = new QueueSynchronizationContext();
        var runs = 0;
        var concurrent = 0;
        var maxConcurrent = 0;
        var errors = 0;
        var periodic = new CadPeriodicWork(TimeSpan.FromMilliseconds(5), async () =>
        {
            Assert.Same(context, SynchronizationContext.Current);
            maxConcurrent = Math.Max(maxConcurrent, ++concurrent);
            await Task.Delay(15);
            concurrent--;
            if (++runs == 2) throw new InvalidOperationException("reported, loop continues");
        }, _ => errors++);

        await context.RunAsync(() => periodic.Start(), until: () => runs >= 3);
        periodic.Stop();

        Assert.Equal(1, maxConcurrent);
        Assert.Equal(1, errors);
        Assert.False(periodic.IsRunning);
    }

    [Fact]
    public void PeriodicWorkRefusesToStartWithoutAContext()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            using var periodic = new CadPeriodicWork(TimeSpan.FromSeconds(1), () => Task.CompletedTask, _ => { });
            Assert.Throws<InvalidOperationException>(periodic.Start);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>A single-threaded context, standing in for the UI dispatcher.</summary>
    private sealed class QueueSynchronizationContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public Task RunAsync(Action start, Func<bool> until) => Task.Factory.StartNew(() =>
        {
            SetSynchronizationContext(this);
            start();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!until() && DateTime.UtcNow < deadline)
                if (_queue.TryTake(out var item, TimeSpan.FromMilliseconds(50)))
                    item.Item1(item.Item2);
            Assert.True(until(), "periodic work did not run in time");
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
}
