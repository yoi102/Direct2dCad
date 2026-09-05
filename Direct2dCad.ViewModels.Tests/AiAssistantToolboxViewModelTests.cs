using Direct2dCad.AI.Contracts;
using Direct2dCad.Agent;
using Direct2dCad.Agent.Codex;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.ViewModels.Tools;

namespace Direct2dCad.ViewModels.Tests;

public sealed class AiAssistantToolboxViewModelTests
{
    [Theory]
    [InlineData(AiChatItemKind.System)]
    [InlineData(AiChatItemKind.User)]
    [InlineData(AiChatItemKind.Assistant)]
    [InlineData(AiChatItemKind.Tool)]
    [InlineData(AiChatItemKind.Error)]
    public void ChatItem_MapsEveryKindToDisplayRole(AiChatItemKind kind)
    {
        var image = new AiImageAttachmentViewModel("preview.png", "data:image/png;base64,AA==");
        var text = new AiImageAttachmentViewModel(
            "notes.txt",
            TextContent: "notes",
            ContentType: "text/plain");
        var item = new AiChatItemViewModel(kind, "content", [image, text]);

        Assert.False(string.IsNullOrWhiteSpace(item.Role));
        Assert.Equal(2, item.Attachments.Count);
        Assert.Single(item.Images);
        Assert.Same(image, item.Images[0]);
    }

    [Fact]
    public async Task FileAndClipboardImagesAreAttachedAndSentToCodex()
    {
        var fileDialog = new FakeFileDialogService { ImagePath = "sample.png" };
        var imageImport = new FakeImageImportService();
        var codex = new FakeCodexAgentClient();
        using var viewModel = CreateViewModel(fileDialog, imageImport, codex);

        viewModel.AddFileFromFileCommand.Execute(null);
        viewModel.PasteFileCommand.Execute(null);

        Assert.Equal(2, viewModel.ImageAttachments.Count);
        Assert.True(viewModel.SendCommand.CanExecute(null));

        viewModel.UserInput = "Describe the images";
        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.ImageAttachments);
        Assert.NotNull(codex.LastRequest);
        Assert.Equal(
            3,
            codex.LastRequest!.ContentParts!.Count);
        Assert.Equal(
            2,
            Assert.Single(viewModel.Messages, message => message.Kind == AiChatItemKind.User).Images.Count);
    }

    [Fact]
    public async Task ImageOnlyMessageUsesDefaultPrompt()
    {
        var fileDialog = new FakeFileDialogService { ImagePath = "sample.png" };
        var imageImport = new FakeImageImportService();
        var codex = new FakeCodexAgentClient();
        using var viewModel = CreateViewModel(fileDialog, imageImport, codex);

        viewModel.AddFileFromFileCommand.Execute(null);
        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.Equal(Strings.AiImagePrompt, codex.LastRequest!.Prompt);
        Assert.Contains(
            viewModel.Messages,
            message => message.Kind == AiChatItemKind.User &&
                       message.Content == Strings.AiImagePrompt);
    }

    [Fact]
    public async Task ClipboardWithoutImageShowsErrorAndAttachmentsCanBeRemovedAndCleared()
    {
        var fileDialog = new FakeFileDialogService { ImagePath = "sample.png" };
        var imageImport = new FakeImageImportService { ClipboardImage = null };
        var codex = new FakeCodexAgentClient();
        using var viewModel = CreateViewModel(fileDialog, imageImport, codex);

        viewModel.PasteFileCommand.Execute(null);
        Assert.Empty(viewModel.ImageAttachments);
        Assert.Contains(
            viewModel.Messages,
            message => message.Kind == AiChatItemKind.Error &&
                       message.Content == Strings.AiClipboardNoImage);

        viewModel.AddFileFromFileCommand.Execute(null);
        var attachment = Assert.Single(viewModel.ImageAttachments);
        viewModel.RemoveImageCommand.Execute(attachment);
        Assert.Empty(viewModel.ImageAttachments);

        viewModel.Messages.Add(new AiChatItemViewModel(AiChatItemKind.User, "temporary"));
        await viewModel.ClearConversationCommand.ExecuteAsync(null);

        var cleared = Assert.Single(viewModel.Messages);
        Assert.Equal(AiChatItemKind.System, cleared.Kind);
        Assert.Equal(Strings.AiConversationCleared, cleared.Content);
    }

    [Fact]
    public void ImageFileCanBeAttachedWithoutOpeningTheFilePicker()
    {
        var imageImport = new FakeImageImportService();
        using var viewModel = CreateViewModel(
            new FakeFileDialogService { ImagePath = null },
            imageImport,
            new FakeCodexAgentClient());

        viewModel.AttachFile("pasted-image.png");

        var attachment = Assert.Single(viewModel.ImageAttachments);
        Assert.Equal("sample.png", attachment.SourceName);
    }

    [Fact]
    public async Task TextFileIsSentAsTextContentPart()
    {
        using var viewModel = CreateViewModel(
            new FakeFileDialogService { ImagePath = "notes.txt" },
            new FakeImageImportService(),
            new FakeCodexAgentClient(),
            new FakeAiFileImportService(
                new AiFileImportData("notes.txt", "text/plain", TextContent: "important notes")));

        viewModel.AddFileFromFileCommand.Execute(null);
        await viewModel.SendCommand.ExecuteAsync(null);

        var filePart = Assert.Single(
            viewModel.Messages.Single(message => message.Kind == AiChatItemKind.User).Attachments,
            attachment => attachment.SourceName == "notes.txt");
        Assert.Equal("important notes", filePart.TextContent);
    }

    [Fact]
    public void ClipboardFilesCanBeAttached()
    {
        var fileImport = new FakeAiFileImportService(
            new AiFileImportData("notes.txt", "text/plain", TextContent: "important notes"));
        fileImport.ClipboardFiles = [
            new AiFileImportData("notes.txt", "text/plain", TextContent: "important notes")
        ];
        using var viewModel = CreateViewModel(
            new FakeFileDialogService(),
            new FakeImageImportService { ClipboardImage = null },
            new FakeCodexAgentClient(),
            fileImport);

        viewModel.PasteFileCommand.Execute(null);

        var attachment = Assert.Single(viewModel.Attachments);
        Assert.Equal("notes.txt", attachment.SourceName);
        Assert.Equal("important notes", attachment.TextContent);
    }

    [Fact]
    public async Task LmStudioMessageUsesAgentRunnerAndReportsEvents()
    {
        var runner = new FakeAgentRunner
        {
            Result = new AgentRunResult("local-model", 4096, false),
            Events = [
                new AgentRunEvent(AgentRunEventKind.AssistantMessage, "answer"),
                new AgentRunEvent(AgentRunEventKind.ToolResult, "{\"success\":true}", "list_layers"),
                new AgentRunEvent(AgentRunEventKind.ContextReduced, ContextWindowTokens: 4096)
            ]
        };
        using var viewModel = CreateViewModel(
            new FakeFileDialogService(),
            new FakeImageImportService(),
            new FakeCodexAgentClient(),
            agentRunner: runner);
        viewModel.Provider = AiAssistantProvider.LmStudio;
        viewModel.SelectedModel = "local-model";
        viewModel.UserInput = "inspect";

        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.NotNull(runner.LastRequest);
        Assert.Equal("inspect", runner.LastRequest!.UserPrompt);
        Assert.Equal(4096, viewModel.ContextWindowTokens);
        Assert.Contains(viewModel.Messages, message =>
            message.Kind == AiChatItemKind.Assistant && message.Content == "answer");
        Assert.Contains(viewModel.Messages, message =>
            message.Kind == AiChatItemKind.Tool && message.Content.Contains("list_layers"));
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task SendAsync_ReportsAgentFailureAndRestoresReadyState()
    {
        var runner = new FakeAgentRunner
        {
            Exception = new InvalidOperationException("model failed")
        };
        using var viewModel = CreateViewModel(
            new FakeFileDialogService(),
            new FakeImageImportService(),
            new FakeCodexAgentClient(),
            agentRunner: runner);
        viewModel.Provider = AiAssistantProvider.LmStudio;
        viewModel.SelectedModel = "local-model";
        viewModel.UserInput = "inspect";

        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
        Assert.Equal(Strings.AiRequestFailed, viewModel.ConnectionStatus);
        Assert.Contains(viewModel.Messages, message =>
            message.Kind == AiChatItemKind.Error && message.Content == "model failed");
    }

    [Fact]
    public async Task StopCommand_CancelsActiveLmStudioRequest()
    {
        var runner = new FakeAgentRunner
        {
            WaitForCancellation = true,
            Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        using var viewModel = CreateViewModel(
            new FakeFileDialogService(),
            new FakeImageImportService(),
            new FakeCodexAgentClient(),
            agentRunner: runner);
        viewModel.Provider = AiAssistantProvider.LmStudio;
        viewModel.SelectedModel = "local-model";
        viewModel.UserInput = "inspect";

        var send = viewModel.SendCommand.ExecuteAsync(null);
        await runner.Started.Task;
        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.ClearConversationCommand.CanExecute(null));
        Assert.False(viewModel.RemoveImageCommand.CanExecute(null));

        viewModel.StopCommand.Execute(null);
        await send;

        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.ClearConversationCommand.CanExecute(null));
        Assert.Equal(Strings.AiCancelled, viewModel.ConnectionStatus);
        Assert.Contains(viewModel.Messages, message =>
            message.Kind == AiChatItemKind.System && message.Content == Strings.AiCancelled);
    }

    [Fact]
    public async Task SendAsync_ReportsEmptyResponse()
    {
        var runner = new FakeAgentRunner
        {
            Result = new AgentRunResult("local-model", 8192, true)
        };
        using var viewModel = CreateViewModel(
            new FakeFileDialogService(),
            new FakeImageImportService(),
            new FakeCodexAgentClient(),
            agentRunner: runner);
        viewModel.Provider = AiAssistantProvider.LmStudio;
        viewModel.SelectedModel = "local-model";
        viewModel.UserInput = "inspect";

        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.Contains(viewModel.Messages, message =>
            message.Kind == AiChatItemKind.Error && message.Content == Strings.AiEmptyResponse);
    }

    [Fact]
    public async Task OpenSettingsAsync_AppliesResultSavesAndResetsCodexConversation()
    {
        var settingsStore = new FakeSettingsStore();
        var codex = new FakeCodexAgentClient();
        var dialog = new FakeDialogService
        {
            AiSettingsResult = new AiAssistantSettingsDialogResult(
                new AiAssistantSettings
                {
                    Provider = AiAssistantProvider.LmStudio,
                    Endpoint = "http://localhost:4321/v1",
                    Model = "new-model"
                },
                ["new-model"],
                ["codex-model"])
        };
        using var viewModel = CreateViewModel(
            new FakeFileDialogService(),
            new FakeImageImportService(),
            codex,
            settingsStore: settingsStore,
            dialog: dialog);

        await viewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(AiAssistantProvider.LmStudio, viewModel.Provider);
        Assert.Equal("new-model", viewModel.SelectedModel);
        Assert.Equal(["new-model"], viewModel.LmStudioModels);
        Assert.Equal(1, settingsStore.SaveCount);
        Assert.Equal(1, codex.ResetCount);
        Assert.Equal(
            string.Format(Strings.AiReadyModelFormat, "new-model"),
            viewModel.ConnectionStatus);
    }

    [Fact]
    public async Task OpenSettingsAsync_CancelLeavesCurrentStateUntouched()
    {
        var settingsStore = new FakeSettingsStore();
        var codex = new FakeCodexAgentClient();
        using var viewModel = CreateViewModel(
            new FakeFileDialogService(),
            new FakeImageImportService(),
            codex,
            settingsStore: settingsStore,
            dialog: new FakeDialogService());
        var originalStatus = viewModel.ConnectionStatus;

        await viewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(originalStatus, viewModel.ConnectionStatus);
        Assert.Equal(0, settingsStore.SaveCount);
        Assert.Equal(0, codex.ResetCount);
    }

    [Fact]
    public void AttachFile_ReportsImportAndValidationErrors()
    {
        var importError = new FakeAiFileImportService(exception: new InvalidDataException("unsupported"));
        using var errorViewModel = CreateViewModel(
            new FakeFileDialogService(),
            new FakeImageImportService(),
            new FakeCodexAgentClient(),
            importError);
        errorViewModel.AttachFile("broken.bin");

        Assert.Contains(errorViewModel.Messages, message =>
            message.Kind == AiChatItemKind.Error && message.Content == "unsupported");

        using var emptyViewModel = CreateViewModel(
            new FakeFileDialogService(),
            new FakeImageImportService(),
            new FakeCodexAgentClient(),
            new FakeAiFileImportService(
                new AiFileImportData("empty.txt", "text/plain", TextContent: "")));
        emptyViewModel.AttachFile("empty.txt");

        Assert.Contains(emptyViewModel.Messages, message =>
            message.Kind == AiChatItemKind.Error && message.Content.Contains("empty"));
    }

    [Fact]
    public void AttachFile_RejectsOversizedImagePayload()
    {
        using var viewModel = CreateViewModel(
            new FakeFileDialogService(),
            new FakeImageImportService(),
            new FakeCodexAgentClient(),
            new FakeAiFileImportService(new AiFileImportData(
                "large.png",
                "image/png",
                DataUrl: new string('a', 14_000_001))));

        viewModel.AttachFile("large.png");

        Assert.Empty(viewModel.Attachments);
        Assert.Contains(viewModel.Messages, message =>
            message.Kind == AiChatItemKind.Error && message.Content == Strings.AiImageTooLarge);
    }

    [Fact]
    public void SendCommand_RequiresModelForLmStudio()
    {
        using var viewModel = CreateViewModel(
            new FakeFileDialogService(),
            new FakeImageImportService(),
            new FakeCodexAgentClient());
        viewModel.Provider = AiAssistantProvider.LmStudio;
        viewModel.UserInput = "inspect";

        Assert.False(viewModel.SendCommand.CanExecute(null));

        viewModel.SelectedModel = "local-model";
        Assert.True(viewModel.SendCommand.CanExecute(null));
    }

    internal static AiAssistantToolboxViewModel CreateDisconnectedViewModel() =>
        CreateViewModel(new(), new(), new());

    private static AiAssistantToolboxViewModel CreateViewModel(
        FakeFileDialogService fileDialog,
        FakeImageImportService imageImport,
        FakeCodexAgentClient codex,
        FakeAiFileImportService? fileImport = null,
        FakeAgentRunner? agentRunner = null,
        FakeSettingsStore? settingsStore = null,
        FakeDialogService? dialog = null) =>
        new(
            new InMemoryToolboxLayoutSettingsStore(),
            new TestToolboxIconProvider(),
            new FakeAiChatClient(),
            agentRunner ?? new FakeAgentRunner(),
            codex,
            settingsStore ?? new FakeSettingsStore(),
            dialog ?? new FakeDialogService(),
            new FakeToolWorkspace(),
            imageImport,
            fileImport ?? new FakeAiFileImportService(),
            fileDialog);

    private sealed class InMemoryToolboxLayoutSettingsStore : IToolboxLayoutSettingsStore
    {
        public CadToolboxState? Load(string contentId) => null;

        public void Save(IEnumerable<KeyValuePair<string, CadToolboxState>> toolboxes)
        {
        }
    }

    private sealed class TestToolboxIconProvider : IToolboxIconProvider
    {
        public object Explorer => string.Empty;
        public object Layers => string.Empty;
        public object Blocks => string.Empty;
        public object Terminal => string.Empty;
        public object Search => string.Empty;
        public object Filter => string.Empty;
        public object Git => string.Empty;
        public object Problems => string.Empty;
        public object Assistant => string.Empty;
        public object Messages => string.Empty;
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public string? ImagePath { get; init; }

        public string? SaveAsD2cad(string fileName) => null;
        public string? OpenD2cadFile() => null;
        public string? OpenFile() => ImagePath;
        public string? OpenImageFile() => ImagePath;
    }

    private sealed class FakeAiFileImportService : IAiFileImportService
    {
        private readonly AiFileImportData _data;
        private readonly Exception? _exception;

        public FakeAiFileImportService(
            AiFileImportData? data = null,
            Exception? exception = null)
        {
            _data = data ?? new AiFileImportData(
                "sample.png",
                "image/png",
                DataUrl: "data:image/png;base64,AA==");
            _exception = exception;
        }

        public IReadOnlyList<AiFileImportData> ClipboardFiles { get; set; } = [];

        public AiFileImportData Load(string filePath)
        {
            if (_exception is not null)
                throw _exception;
            return _data;
        }

        public IReadOnlyList<AiFileImportData> LoadFilesFromClipboard() => ClipboardFiles;
    }

    private sealed class FakeImageImportService : IImageImportService
    {
        public CadImageImportData? ClipboardImage { get; init; } =
            new(1, 1, 4, [0, 0, 0, 255], "image/png", "Clipboard Image");

        public CadImageImportData LoadFromFile(string filePath) =>
            new(1, 1, 4, [0, 0, 0, 255], "image/png", "sample.png");

        public CadImageImportData? LoadFromClipboard() => ClipboardImage;

        public string CreatePngDataUrl(CadImageImportData image) =>
            $"data:image/png;base64,{Convert.ToBase64String([1, 2, 3])}";
    }

    private sealed class FakeSettingsStore : IAiAssistantSettingsStore
    {
        public int SaveCount { get; private set; }

        public AiAssistantSettings Load() => new()
        {
            Provider = AiAssistantProvider.Codex,
            CodexModel = "codex-test"
        };

        public void Save(AiAssistantSettings settings)
        {
            SaveCount++;
        }
    }

    private sealed class FakeAiChatClient : IAiChatClient
    {
        public Task<IReadOnlyList<string>> GetModelsAsync(
            string endpoint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<AiChatCompletion> CompleteAsync(
            AiChatRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AiChatCompletion("ok", [], request.Model));
    }

    private sealed class FakeAgentRunner : IAgentRunner
    {
        public AgentRunResult Result { get; set; } = new("test-model", 8192, false);
        public Exception? Exception { get; set; }
        public IReadOnlyList<AgentRunEvent> Events { get; set; } = [];
        public AgentRunRequest? LastRequest { get; private set; }
        public bool WaitForCancellation { get; set; }
        public TaskCompletionSource<bool>? Started { get; set; }

        public Task<AgentRunResult> RunAsync(
            AgentRunRequest request,
            Func<AgentRunEvent, ValueTask>? reportEvent = null,
            CancellationToken cancellationToken = default) =>
            RunCoreAsync(request, reportEvent, cancellationToken);

        private async Task<AgentRunResult> RunCoreAsync(
            AgentRunRequest request,
            Func<AgentRunEvent, ValueTask>? reportEvent,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            Started?.TrySetResult(true);
            if (Exception is not null)
                throw Exception;
            if (WaitForCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            foreach (var agentEvent in Events)
                if (reportEvent is not null)
                    await reportEvent(agentEvent);
            cancellationToken.ThrowIfCancellationRequested();
            return Result;
        }
    }

    private sealed class FakeCodexAgentClient : ICodexAgentClient
    {
        public CodexAgentRunRequest? LastRequest { get; private set; }
        public int ResetCount { get; private set; }

        public Task<IReadOnlyList<string>> GetModelsAsync(
            CodexAgentOptions options,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<CodexAgentRunResult> RunAsync(
            CodexAgentRunRequest request,
            Func<AgentRunEvent, ValueTask>? reportEvent = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new CodexAgentRunResult(request.Options.Model, false));
        }

        public Task ResetConversationAsync(CancellationToken cancellationToken = default)
        {
            ResetCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeToolWorkspace : ICadToolWorkspace
    {
        public IReadOnlyList<CadToolWorkspaceDocument> GetDocuments() => [];
        public CadToolWorkspaceDocument? GetActiveDocument() => null;
        public CadToolWorkspaceDocument GetRequiredDocument(string documentId) => throw new NotSupportedException();
        public CadToolWorkspaceDocument CreateDocument(string? name) => throw new NotSupportedException();
        public Task<CadToolWorkspaceDocument> OpenDocumentAsync(string filePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public bool ActivateDocument(string documentId) => false;
        public bool RenameDocument(string documentId, string name) => false;
        public Task<bool> SaveDocumentAsync(string documentId, string? filePath, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> CloseDocumentAsync(string documentId) => Task.FromResult(false);
    }

    private sealed class FakeDialogService : IDialogService
    {
        public AiAssistantSettingsDialogResult? AiSettingsResult { get; init; }

        public void Close(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) { }
        public Task ShowOrReplaceMessageDialogAsync(string message, string header = "", string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Task.CompletedTask;
        public Task<bool> ShowOrReplaceMessageDialogWithCancelAsync(string message, string header = "", string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Task.FromResult(false);
        public IDisposable ShowProgressBarDialog(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => new NoopDisposable();
        public Task<bool> ShowExitConfirmation(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Task.FromResult(false);
        public Task<UnsavedDocumentDialogResult> ShowUnsavedDocumentDialogAsync(string documentName, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Task.FromResult(UnsavedDocumentDialogResult.Cancel);
        public Task<UnsavedDocumentDialogResult> ShowUnsavedDocumentsDialogAsync(IReadOnlyList<UnsavedDocumentInfo> documents, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Task.FromResult(UnsavedDocumentDialogResult.Cancel);
        public Task<GridSpacingPresetDialogResult?> ShowGridSpacingPresetDialogAsync(GridSpacingPresetDialogRequest request, string dialogIdentifier = ViewServiceIdentifiers.DocumentSettingsDialogHost) => Task.FromResult<GridSpacingPresetDialogResult?>(null);
        public Task<CreateBlockDialogResult?> ShowCreateBlockDialogAsync(CreateBlockDialogRequest request, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Task.FromResult<CreateBlockDialogResult?>(null);
        public Task<AiAssistantSettingsDialogResult?> ShowAiAssistantSettingsDialogAsync(AiAssistantSettingsDialogRequest request, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Task.FromResult(AiSettingsResult);
        public void ShowDocumentSettingsDialog(IDocumentSettingsDialogViewModel viewModel) { }
        public void ShowUserSettingsDialog(IUserSettingsDialogViewModel viewModel) { }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
