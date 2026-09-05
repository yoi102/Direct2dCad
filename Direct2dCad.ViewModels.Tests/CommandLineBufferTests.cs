using System.Collections.Specialized;
using Direct2dCad.CommandLine;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.ViewModels.Tools;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CommandLineBufferTests
{
    [Fact]
    public async Task PublishedActivityIsBufferedAndFlushedWithOneCollectionNotification()
    {
        using var context = new Context();
        context.View.FlushPendingEntries();
        var changes = new List<NotifyCollectionChangedEventArgs>();
        context.View.Entries.CollectionChanged += (_, args) => changes.Add(args);
        for (var i = 0; i < 50; i++) await context.Publish($"move {i}");
        Assert.Empty(changes);
        Assert.True(context.View.HasPendingEntries);
        Assert.Equal(50, context.View.FlushPendingEntries());
        Assert.Single(changes);
        Assert.EndsWith("move 49", context.View.Entries[^1].Text);
        Assert.False(context.View.HasPendingEntries);
    }

    [Fact]
    public async Task OverflowReportsDroppedEntriesAndHistoryRemainsBounded()
    {
        using var context = new Context();
        context.View.FlushPendingEntries();
        for (var i = 0; i < 4100; i++) await context.Publish($"entry {i}");
        Assert.Equal(100, context.View.FlushPendingEntries());
        Assert.Contains(context.View.Entries, item => item.Kind == CadCommandLineEntryKind.Warning && item.Text.Contains("100 buffered"));
        while (context.View.HasPendingEntries) context.View.FlushPendingEntries();
        Assert.Equal(1000, context.View.Entries.Count);
        Assert.EndsWith("entry 4099", context.View.Entries[^1].Text);
    }

    [Fact]
    public async Task ClearRemovesQueuedOutputAndLateResultsAfterDisposeAreIgnored()
    {
        using var context = new Context();
        await context.Publish("old entry");
        context.View.CommandText = "CLEAR";
        await context.View.ExecuteCommandCommand.ExecuteAsync(null);
        Assert.Empty(context.View.Entries);
        Assert.False(context.View.HasPendingEntries);
        context.Tools.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        context.View.CommandText = "tool";
        var running = context.View.ExecuteCommandCommand.ExecuteAsync(null);
        context.View.Dispose();
        context.View.Dispose();
        context.Tools.Pending.SetResult(new(true, "late output"));
        await running;
        await context.Publish("after dispose");
        Assert.Equal(0, context.View.FlushPendingEntries());
        Assert.False(context.View.HasPendingEntries);
        Assert.Empty(context.View.LatestOutputText);
    }

    private sealed class Context : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly CadToolboxTestContext _document = new();
        public ToolService Tools { get; } = new();
        public CommandLineToolboxViewModel View { get; }
        public Context()
        {
            var services = new ServiceCollection();
            services.AddMessagePipe();
            _provider = services.BuildServiceProvider();
            var platform = new CadTestPlatform();
            View = new(platform, platform, new BuiltIns(), Tools,
                _provider.GetRequiredService<IAsyncSubscriber<CadCommandActivityMessage>>(),
                _provider.GetRequiredService<IAsyncSubscriber<CadInteractionActivityMessage>>());
            View.Attach(_document.Document);
        }
        public ValueTask Publish(string name) => _provider.GetRequiredService<IAsyncPublisher<CadInteractionActivityMessage>>()
            .PublishAsync(new(_document.Document, "test", name));
        public void Dispose() { View.Dispose(); _document.Dispose(); _provider.Dispose(); }
    }

    private sealed class ToolService : ICadToolCommandLineService
    {
        public TaskCompletionSource<CadToolCommandLineExecution?>? Pending;
        public Task<CadToolCommandLineExecution?> TryExecuteAsync(string commandLine, CancellationToken cancellationToken = default) =>
            Pending?.Task ?? Task.FromResult<CadToolCommandLineExecution?>(null);
        public IReadOnlyList<string> Complete(string commandText, int maximumCount = 12) => [];
    }
    private sealed class BuiltIns : ICadCommandLineService
    {
        public IReadOnlyList<CadCommandLineDescriptor> Commands => [];
        public CadCommandLineResult Execute(string commandLine, ICadCommandLineContext? context) => new(true, "", ClearOutput: commandLine == "CLEAR");
        public IReadOnlyList<string> Complete(string commandPrefix, int maximumCount = 12) => [];
    }
}
