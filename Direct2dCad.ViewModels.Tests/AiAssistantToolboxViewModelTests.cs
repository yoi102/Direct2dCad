using Direct2dCad.AI;
using Direct2dCad.Agent;
using Direct2dCad.Agent.Codex;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.ViewModels.Tools;

namespace Direct2dCad.ViewModels.Tests;

public sealed class AiAssistantToolboxViewModelTests
{
    [Fact]
    public async Task FileAndClipboardImagesAreAttachedAndSentToCodex()
    {
        var fileDialog = new FakeFileDialogService { ImagePath = "sample.png" };
        var imageImport = new FakeImageImportService();
        var codex = new FakeCodexAgentClient();
        using var viewModel = CreateViewModel(fileDialog, imageImport, codex);

        viewModel.AddImageFromFileCommand.Execute(null);
        viewModel.PasteImageCommand.Execute(null);

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

        viewModel.AddImageFromFileCommand.Execute(null);
        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.Equal("Please analyze the attached image.", codex.LastRequest!.Prompt);
        Assert.Contains(
            viewModel.Messages,
            message => message.Kind == AiChatItemKind.User &&
                       message.Content == "Please analyze the attached image.");
    }

    [Fact]
    public async Task ClipboardWithoutImageShowsErrorAndAttachmentsCanBeRemovedAndCleared()
    {
        var fileDialog = new FakeFileDialogService { ImagePath = "sample.png" };
        var imageImport = new FakeImageImportService { ClipboardImage = null };
        var codex = new FakeCodexAgentClient();
        using var viewModel = CreateViewModel(fileDialog, imageImport, codex);

        viewModel.PasteImageCommand.Execute(null);
        Assert.Empty(viewModel.ImageAttachments);
        Assert.Contains(
            viewModel.Messages,
            message => message.Kind == AiChatItemKind.Error &&
                       message.Content.Contains("clipboard", StringComparison.OrdinalIgnoreCase));

        viewModel.AddImageFromFileCommand.Execute(null);
        var attachment = Assert.Single(viewModel.ImageAttachments);
        viewModel.RemoveImageCommand.Execute(attachment);
        Assert.Empty(viewModel.ImageAttachments);

        viewModel.Messages.Add(new AiChatItemViewModel(AiChatItemKind.User, "temporary"));
        await viewModel.ClearConversationCommand.ExecuteAsync(null);

        var cleared = Assert.Single(viewModel.Messages);
        Assert.Equal(AiChatItemKind.System, cleared.Kind);
        Assert.Equal("Conversation cleared.", cleared.Content);
    }

    private static AiAssistantToolboxViewModel CreateViewModel(
        FakeFileDialogService fileDialog,
        FakeImageImportService imageImport,
        FakeCodexAgentClient codex) =>
        new(
            new InMemoryToolboxLayoutSettingsStore(),
            new TestToolboxIconProvider(),
            new FakeAiChatClient(),
            new FakeAgentRunner(),
            codex,
            new FakeSettingsStore(),
            new FakeDialogService(),
            new FakeToolWorkspace(),
            imageImport,
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
        public string? OpenImageFile() => ImagePath;
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
        public AiAssistantSettings Load() => new()
        {
            Provider = AiAssistantProvider.Codex,
            CodexModel = "codex-test"
        };

        public void Save(AiAssistantSettings settings)
        {
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
        public Task<AgentRunResult> RunAsync(
            AgentRunRequest request,
            Func<AgentRunEvent, ValueTask>? reportEvent = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentRunResult(request.Model, 8192, false));
    }

    private sealed class FakeCodexAgentClient : ICodexAgentClient
    {
        public CodexAgentRunRequest? LastRequest { get; private set; }

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

        public Task ResetConversationAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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
        public void Close(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) { }
        public Task ShowOrReplaceMessageDialogAsync(string message, string header = "", string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Task.CompletedTask;
        public Task<bool> ShowOrReplaceMessageDialogWithCancelAsync(string message, string header = "", string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Task.FromResult(false);
        public IDisposable ShowProgressBarDialog(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => new NoopDisposable();
        public Task<bool> ShowExitConfirmation(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Task.FromResult(false);
        public Task<UnsavedDocumentDialogResult> ShowUnsavedDocumentDialogAsync(string documentName, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Task.FromResult(UnsavedDocumentDialogResult.Cancel);
        public Task<UnsavedDocumentDialogResult> ShowUnsavedDocumentsDialogAsync(IReadOnlyList<UnsavedDocumentInfo> documents, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Task.FromResult(UnsavedDocumentDialogResult.Cancel);
        public Task<GridSpacingPresetDialogResult?> ShowGridSpacingPresetDialogAsync(GridSpacingPresetDialogRequest request, string dialogIdentifier = ViewServiceIdentifiers.DocumentSettingsDialogHost) => Task.FromResult<GridSpacingPresetDialogResult?>(null);
        public Task<CreateBlockDialogResult?> ShowCreateBlockDialogAsync(CreateBlockDialogRequest request, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Task.FromResult<CreateBlockDialogResult?>(null);
        public Task<AiAssistantSettingsDialogResult?> ShowAiAssistantSettingsDialogAsync(AiAssistantSettingsDialogRequest request, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Task.FromResult<AiAssistantSettingsDialogResult?>(null);
        public void ShowDocumentSettingsDialog(IDocumentSettingsDialogViewModel viewModel) { }
        public void ShowUserSettingsDialog(IUserSettingsDialogViewModel viewModel) { }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
