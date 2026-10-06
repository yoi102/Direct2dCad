using Direct2dCad.CommandLine;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.ViewModels.Tools;
using MessagePipe;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CommandLineInteractionTests
{
    [Fact]
    public async Task ScriptStopsWhenSaveIsCancelled()
    {
        using var workspace = new ToolExecutionWorkspace { AllowSave = false };
        var document = workspace.CreateDocument("Unsaved");
        using var context = new Context(new CadToolCommandLineService(workspace));
        context.View.Attach(document.GetViewModel());
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".scr");
        try
        {
            await File.WriteAllLinesAsync(path, ["SAVE", "NEW unwanted"]);
            await context.Run($"SCRIPT \"{path}\"");
            Assert.Contains("line 1 after 0 commands", context.View.LatestOutputText);
            Assert.Single(workspace.GetDocuments());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task HistoryRecallSuppressesCompletionsUntilTheUserEdits()
    {
        using var context = new Context();
        await context.Run("LINE");
        await context.Run("POLYLINE");
        context.View.ShowPreviousCommand();
        Assert.Equal("POLYLINE", context.View.CommandText);
        Assert.True(context.View.IsNavigatingHistory);
        Assert.False(context.View.HasSuggestions);
        context.View.ShowPreviousCommand();
        Assert.Equal("LINE", context.View.CommandText);
        Assert.False(context.View.HasSuggestions);
        context.View.CommandText = "CIRCLE ";
        Assert.False(context.View.IsNavigatingHistory);
        Assert.Contains("CIRCLE 2P", context.View.Suggestions);
        Assert.Contains("RADIUS", context.View.InputHint);
    }

    [Fact]
    public async Task CancelWithExtraArgumentsDoesNotCancelPendingDrawing()
    {
        using var context = new Context();
        await context.Run("PL");
        await context.Run("1,2");
        await context.Run("CANCEL typo");
        Assert.Equal(CadCanvasToolMode.Polyline, context.Document.CadCanvasToolMode);
        Assert.Equal(new CadPointD(1, 2), context.Document.DrawingAnchor);
        Assert.Contains("Usage", context.View.LatestOutputText);
    }

    [Fact]
    public async Task ScriptStopsAtFirstFailureAndKeepsEarlierCommandsUndoable()
    {
        using var context = new Context();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + " command test.scr");
        try
        {
            await File.WriteAllLinesAsync(path, ["; a comment", "LINE", "0,0", "10,0", "ERASE unexpected", "ALL", "ERASE"]);
            await context.Run($"SCRIPT \"{path}\"");
            Assert.Contains("line 5 after 3 commands", context.View.LatestOutputText);
            Assert.Single(context.Document.CadEditor.Document.Entities.Values, entity => !entity.IsErased);
            await context.Run("UNDO");
            Assert.DoesNotContain(context.Document.CadEditor.Document.Entities.Values, entity => !entity.IsErased);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ScriptCancellationStopsBeforeFollowingLine()
    {
        using var context = new Context();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".scr");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Tools.Handler = async (command, token) =>
        {
            if (command != "WAIT_TEST") return null;
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(true, "finished");
        };
        try
        {
            await File.WriteAllLinesAsync(path, ["WAIT_TEST", "LINE"]);
            var running = context.Run($"SCRIPT \"{path}\"");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            context.View.CancelCurrentCommand();
            await running;
            Assert.Equal(CadCanvasToolMode.Select, context.Document.CadCanvasToolMode);
            Assert.False(context.View.IsCommandExecuting);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ScriptRejectsNestedExecutionAndAllowsExplicitDocumentSwitch()
    {
        using var context = new Context();
        using var other = new CadToolboxTestContext();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".scr");
        context.Tools.Handler = (command, _) =>
        {
            if (command != "NEW next") return Task.FromResult<CadToolCommandLineExecution?>(null);
            context.View.Attach(other.Document);
            return Task.FromResult<CadToolCommandLineExecution?>(new(true, "Created next"));
        };
        try
        {
            await File.WriteAllLinesAsync(path, ["NEW next", "LINE", "0,0", "10,0", $"SCRIPT \"{path}\""]);
            await context.Run($"SCRIPT \"{path}\"");
            Assert.Contains("Nested SCRIPT", context.View.LatestOutputText);
            Assert.Empty(context.Document.CadEditor.Document.Entities);
            Assert.Single(other.Document.CadEditor.Document.Entities);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EscapeAfterSwitchingTabsCancelsOriginalTaskAndSuppressesLateSuccessWithoutReentry()
    {
        using var context = new Context();
        using var other = new CadToolboxTestContext();
        var pending = new TaskCompletionSource<CadToolCommandLineExecution?>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Tools.Handler = (_, _) => pending.Task;
        var running = context.Run("UNION");
        var originalToken = Assert.Single(context.Tools.Tokens);
        Assert.True(context.View.IsCommandExecuting);
        other.Document.SetToolMode(CadCanvasToolMode.Polyline);
        Click(other.Document, new(1, 2));
        context.View.Attach(other.Document);

        context.View.CancelCurrentCommand();
        Assert.True(originalToken.IsCancellationRequested);
        Assert.Equal(new CadPointD(1, 2), other.Document.DrawingAnchor);
        Assert.Equal(CadCanvasToolMode.Polyline, other.Document.CadCanvasToolMode);
        Assert.False(context.View.ExecuteCommandCommand.CanExecute(null));
        await context.Run("LINE"); // The method itself also guards direct callers.
        Assert.Single(context.Tools.Tokens);

        pending.SetResult(new(true, "late success must not appear"));
        await running;
        Assert.False(context.View.IsCommandExecuting);
        context.View.FlushPendingEntries();
        Assert.DoesNotContain(context.View.Entries, item => item.Text.Contains("late success"));
        Assert.Contains(context.View.Entries, item => item.Kind == CadCommandLineEntryKind.Warning);
        context.Tools.Handler = (_, _) => Task.FromResult<CadToolCommandLineExecution?>(null);
        await context.Run("STATUS");
        Assert.False(context.Tools.Tokens[^1].IsCancellationRequested);
    }

    [Fact]
    public async Task TypedCancelUsesTheRunningTaskTokenInsteadOfStartingAnotherCommand()
    {
        using var context = new Context();
        var pending = new TaskCompletionSource<CadToolCommandLineExecution?>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Tools.Handler = (_, _) => pending.Task;
        var running = context.Run("long_operation");
        context.View.CommandText = "._cancel";
        context.View.SubmitCommandInput();
        Assert.True(Assert.Single(context.Tools.Tokens).IsCancellationRequested);
        Assert.Empty(context.View.CommandText);
        pending.SetException(new OperationCanceledException());
        await running;
        context.View.ShowPreviousCommand();
        Assert.Equal("long_operation", context.View.CommandText);
    }

    [Fact]
    public async Task DelayedBuiltinDispatchKeepsTheOriginalDocumentAfterTabSwitch()
    {
        using var context = new Context();
        using var other = new CadToolboxTestContext();
        var pending = new TaskCompletionSource<CadToolCommandLineExecution?>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Tools.Handler = (_, _) => pending.Task;
        var running = context.Run("LINE");
        context.View.Attach(other.Document);
        pending.SetResult(null);
        await running;
        Assert.Equal(CadCanvasToolMode.Line, context.Document.CadCanvasToolMode);
        Assert.Equal(CadCanvasToolMode.Select, other.Document.CadCanvasToolMode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedBuiltinDoesNotRunAgainstAClosedOrReplacedDocument(bool replaceEditor)
    {
        using var context = new Context();
        using var other = new CadToolboxTestContext();
        var pending = new TaskCompletionSource<CadToolCommandLineExecution?>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Tools.Handler = (_, _) => pending.Task;
        var running = context.Run("LINE");
        if (replaceEditor) context.Document.ReplaceEditor(new CadEditor(CadDocument.Create("Replacement")));
        else context.Document.Dispose();
        context.View.Attach(other.Document);
        pending.SetResult(null);
        await running;
        Assert.Equal(CadCanvasToolMode.Select, other.Document.CadCanvasToolMode);
        Assert.Equal(CadCanvasToolMode.Select, context.Document.CadCanvasToolMode);
        context.View.FlushPendingEntries();
        Assert.Contains(context.View.Entries, item => item.Kind == CadCommandLineEntryKind.Warning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyEnterCompletesPendingPolylineFromCanvasOrTerminalPoints(bool terminalPoints)
    {
        using var context = new Context();
        await context.Run("PL");
        var points = new[] { new CadPointD(0, 0), new CadPointD(20, 0), new CadPointD(20, 10) };
        foreach (var point in points)
            if (terminalPoints) await context.Run($"{point.X},{point.Y}");
            else Click(context.Document, point);
        Assert.Empty(context.Document.CadEditor.Document.Entities);

        await context.Run("");

        var polyline = Assert.IsType<CadPolyline>(Assert.Single(context.Document.CadEditor.Document.Entities.Values));
        Assert.Equal(points, polyline.Points);
        context.Document.Undo();
        Assert.True(polyline.IsErased);
    }

    [Fact]
    public async Task EmptyEnterWithInsufficientPointsKeepsPendingDrawingAndReportsFailure()
    {
        using var context = new Context();
        await context.Run("PL");
        Click(context.Document, new(4, 6));

        await context.Run("");

        Assert.Empty(context.Document.CadEditor.Document.Entities);
        Assert.Equal(new CadPointD(4, 6), context.Document.DrawingAnchor);
        Assert.Equal(CadCanvasToolMode.Polyline, context.Document.CadCanvasToolMode);
        context.View.FlushPendingEntries();
        Assert.Equal(CadCommandLineEntryKind.Error, context.View.Entries[^1].Kind);
    }

    [Fact]
    public async Task EmptyEnterCompletesGripInsteadOfRepeatingLastTerminalCommand()
    {
        using var context = new Context();
        await context.Run("STATUS");
        var vm = context.Document;
        var id = vm.CadEditor.AddLine(default, new(20, 0));
        vm.SelectEntities([id]);
        var grip = new CadHandleSceneBuilder().BuildSelectionHandles(vm.CadEditor.Document, [id])
            .OfType<CadGripHandle>().Where(h => h.Type == CadHandleType.Vertex)
            .OrderBy(h => h.Position.DistanceTo(new(20, 0))).First();
        Click(vm, grip.Position);
        Assert.True(vm.IsGripEditing);
        vm.DynamicInputFields.Single(field => field.Key == "Length").Text = "30";
        vm.DynamicInputFields.Single(field => field.Key == "Angle").Text = "90";

        await context.Run("");

        Assert.False(vm.IsGripEditing);
        Assert.True(((CadLine)vm.CadEditor.Document.GetEntity(id)).End.NearEquals(new(0, 30)));
    }

    [Fact]
    public async Task CancelCoordinatesAndFailedCommandsDoNotReplaceIdleRepeatCommand()
    {
        using var context = new Context();
        await context.Run("LINE");
        await context.Run("0,0");
        await context.Run("10");
        await context.Run("not_a_command");
        context.View.CancelCurrentCommand();
        Assert.Equal(CadCanvasToolMode.Select, context.Document.CadCanvasToolMode);

        await context.Run("");

        Assert.Equal(CadCanvasToolMode.Line, context.Document.CadCanvasToolMode);
        Assert.Null(context.Document.DrawingAnchor);
        Assert.Single(context.Document.CadEditor.Document.Entities);
    }

    [Fact]
    public async Task SuccessfulShortcutWithCommaArgumentsRemainsRepeatable()
    {
        using var context = new Context();
        var calls = new List<string>();
        context.Tools.Handler = (command, _) =>
        {
            calls.Add(command);
            return Task.FromResult<CadToolCommandLineExecution?>(new(true, "done"));
        };
        await context.Run("MOVE 10,0");
        await context.Run("");
        Assert.Equal(new[] { "MOVE 10,0", "MOVE 10,0" }, calls);
    }

    [Fact]
    public async Task EscapePropagatesThroughRealToolHostAndPreventsBooleanCommitAfterSwitchingDocuments()
    {
        using var workspace = new ToolExecutionWorkspace();
        var first = workspace.CreateDocument("First");
        var firstVm = first.GetViewModel();
        var a = firstVm.CadEditor.AddCircle(default, 10);
        var b = firstVm.CadEditor.AddCircle(default, 3);
        firstVm.SelectEntities([a, b]);
        var second = workspace.CreateDocument("Second");
        var secondVm = second.GetViewModel();
        workspace.ActivateDocument(first.DocumentId);
        var waiting = new WaitingWorkspace(workspace, first);
        using var context = new Context(new CadToolCommandLineService(waiting));
        context.View.Attach(firstVm);
        var history = firstVm.CadEditor.CreateDocumentHistorySnapshot();
        var running = context.Run("UNION");
        await waiting.Host.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(((EditorTabViewModel)first.Host).Operation.IsRunning);
        workspace.ActivateDocument(second.DocumentId);
        context.View.Attach(secondVm);
        secondVm.SetToolMode(CadCanvasToolMode.Line);
        Click(secondVm, new(5, 5));

        context.View.CancelCurrentCommand();
        Assert.True(waiting.Host.OperationToken.IsCancellationRequested);
        waiting.Host.Continue.SetResult();
        await running;

        Assert.True(firstVm.CadEditor.DocumentHistoryEquals(history));
        Assert.Equal(2, firstVm.CadEditor.Document.Entities.Count);
        Assert.All(firstVm.CadEditor.Document.Entities.Values, entity => Assert.False(entity.IsErased));
        Assert.Empty(secondVm.CadEditor.Document.Entities);
        Assert.Equal(new CadPointD(5, 5), secondVm.DrawingAnchor);
        Assert.False(((EditorTabViewModel)first.Host).Operation.IsRunning);
    }

    private static void Click(CadDocumentViewModel vm, CadPointD point)
    {
        var screen = vm.CadEditor.Viewport.WorldToScreen(point);
        vm.PointerMove(screen);
        vm.PointerDown(screen, CadCanvasPointerButton.Left, false);
        vm.PointerUp(screen, CadCanvasPointerButton.Left);
    }

    private sealed class Context : IDisposable
    {
        private readonly CadToolboxTestContext _document = new();
        public CadDocumentViewModel Document => _document.Document;
        public ControlledTools Tools { get; } = new();
        public CommandLineToolboxViewModel View { get; }
        public Context(ICadToolCommandLineService? tools = null)
        {
            Document.SetViewportSize(800, 600);
            Document.IsObjectSnapEnabled = false;
            Document.IsGridSnapEnabled = false;
            View = new(_document.Platform, _document.Platform, new CadCommandLineService(), tools ?? Tools,
                _document.GetService<IAsyncSubscriber<CadCommandActivityMessage>>(),
                _document.GetService<IAsyncSubscriber<CadInteractionActivityMessage>>());
            View.Attach(Document);
        }
        public Task Run(string command) { View.CommandText = command; return View.ExecuteCommandCommand.ExecuteAsync(null); }
        public void Dispose() { View.Dispose(); _document.Dispose(); }
    }

    private sealed class ControlledTools : ICadToolCommandLineService
    {
        public List<CancellationToken> Tokens { get; } = [];
        public Func<string, CancellationToken, Task<CadToolCommandLineExecution?>> Handler { get; set; } =
            (_, _) => Task.FromResult<CadToolCommandLineExecution?>(null);
        public Task<CadToolCommandLineExecution?> TryExecuteAsync(string commandLine, CancellationToken cancellationToken = default)
        { Tokens.Add(cancellationToken); return Handler(commandLine, cancellationToken); }
        public IReadOnlyList<string> Complete(string commandText, int maximumCount = 12) => [];
    }

    private sealed class WaitingWorkspace(ICadToolWorkspace inner, CadToolWorkspaceDocument document) : ICadToolWorkspace
    {
        public WaitingHost Host { get; } = new(document.Host);
        private CadToolWorkspaceDocument Wrap(CadToolWorkspaceDocument value) => value.DocumentId == document.DocumentId ? value with { Host = Host } : value;
        public IReadOnlyList<CadToolWorkspaceDocument> GetDocuments() => inner.GetDocuments().Select(Wrap).ToArray();
        public CadToolWorkspaceDocument? GetActiveDocument() => inner.GetActiveDocument() is { } value ? Wrap(value) : null;
        public CadToolWorkspaceDocument GetRequiredDocument(string documentId) => Wrap(inner.GetRequiredDocument(documentId));
        public CadToolWorkspaceDocument CreateDocument(string? name) => Wrap(inner.CreateDocument(name));
        public async Task<CadToolWorkspaceDocument> OpenDocumentAsync(string filePath, CancellationToken token) => Wrap(await inner.OpenDocumentAsync(filePath, token));
        public bool ActivateDocument(string documentId) => inner.ActivateDocument(documentId);
        public bool RenameDocument(string documentId, string name) => inner.RenameDocument(documentId, name);
        public Task<bool> SaveDocumentAsync(string documentId, string? filePath, CancellationToken token) => inner.SaveDocumentAsync(documentId, filePath, token);
        public Task<bool> CloseDocumentAsync(string documentId) => inner.CloseDocumentAsync(documentId);
    }

    private sealed class WaitingHost(ICadWorkspaceDocument inner) : ICadWorkspaceDocument
    {
        public ICadToolDocumentSession Session => inner.Session;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken OperationToken { get; private set; }
        public void Load(CadDocument document, string filePath) => inner.Load(document, filePath);
        public Task<T> RunAsync<T>(string message, Func<CancellationToken, Task<T>> operation, CancellationToken token = default) =>
            inner.RunAsync(message, async ct =>
            {
                OperationToken = ct;
                Started.SetResult();
                await Continue.Task;
                return await operation(ct);
            }, token);
    }
}
