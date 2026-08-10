using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Agent;
using Direct2dCad.Agent.Codex;
using Direct2dCad.AI;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Agents;
using Direct2dCad.ViewModels.Tools;
using Direct2dCad.ViewModels.Services.Platform;
using AvalonDock.Core;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class AiAssistantToolboxViewModel : CadToolboxViewModelBase, IDisposable
{
    private readonly IAiChatClient _chatClient;
    private readonly IAgentRunner _agentRunner;
    private readonly ICodexAgentClient _codexAgentClient;
    private readonly IAiAssistantSettingsStore _settingsStore;
    private readonly IDialogService _dialogService;
    private readonly ICadToolWorkspace _workspace;
    private readonly IImageImportService _imageImportService;
    private readonly IAiFileImportService _fileImportService;
    private readonly IFileDialogService _fileDialogService;
    private readonly AgentConversation _conversation = new();
    private CancellationTokenSource? _requestCancellation;
    private CadDocumentViewModel? _documentViewModel;

    public AiAssistantToolboxViewModel(
        IToolboxLayoutSettingsStore toolboxLayoutSettingsStore,
        IToolboxIconProvider toolboxIconProvider,
        IAiChatClient chatClient,
        IAgentRunner agentRunner,
        ICodexAgentClient codexAgentClient,
        IAiAssistantSettingsStore settingsStore,
        IDialogService dialogService,
        ICadToolWorkspace workspace,
        IImageImportService imageImportService,
        IAiFileImportService fileImportService,
        IFileDialogService fileDialogService)
        : base(toolboxLayoutSettingsStore, "toolbox.ai-assistant", DockZone.RightBottom, isOpenByDefault: false)
    {
        _chatClient = chatClient;
        _agentRunner = agentRunner;
        _codexAgentClient = codexAgentClient;
        _settingsStore = settingsStore;
        _dialogService = dialogService;
        _workspace = workspace;
        _imageImportService = imageImportService;
        _fileImportService = fileImportService;
        _fileDialogService = fileDialogService;
        Title = Resource("AiAssistant", "AI Assistant");
        Icon = toolboxIconProvider.Assistant;
        Shortcut = "Ctrl+Shift+A";
        CanClose = false;

        ApplySettings(settingsStore.Load());
        UpdateConfiguredStatus();
        Messages.Add(new AiChatItemViewModel(
            AiChatItemKind.System,
            Resource("AiAssistantWelcome", "Configure LM Studio or Codex, then ask me to inspect or edit documents in the workspace.")));
    }

    public ObservableCollection<AiChatItemViewModel> Messages { get; } = [];
    public ObservableCollection<AiImageAttachmentViewModel> Attachments { get; } = [];
    public ObservableCollection<AiImageAttachmentViewModel> ImageAttachments => Attachments;
    public ObservableCollection<string> LmStudioModels { get; } = [];
    public ObservableCollection<string> CodexModels { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string UserInput { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial AiAssistantProvider Provider { get; set; }

    [ObservableProperty]
    public partial string Endpoint { get; set; } = AiAssistantSettings.DefaultEndpoint;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string? SelectedModel { get; set; }

    [ObservableProperty]
    public partial double Temperature { get; set; } = 0.2;

    [ObservableProperty]
    public partial bool EnableCadTools { get; set; } = true;

    [ObservableProperty]
    public partial int ContextWindowTokens { get; set; } = AiAssistantSettings.DefaultContextWindowTokens;

    [ObservableProperty]
    public partial string CodexExecutablePath { get; set; } = "codex";

    [ObservableProperty]
    public partial string? SelectedCodexModel { get; set; }

    [ObservableProperty]
    public partial string CodexReasoningEffort { get; set; } = "medium";

    [ObservableProperty]
    public partial string CodexServiceTier { get; set; } =
        AiAssistantSettings.DefaultCodexServiceTier;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFileFromFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(PasteFileCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string ConnectionStatus { get; private set; } = Resource("AiDisconnected", "Not connected");

    public bool HasDocument => _documentViewModel is not null;

    public void Attach(CadDocumentViewModel? documentViewModel)
    {
        if (ReferenceEquals(_documentViewModel, documentViewModel))
            return;

        _documentViewModel = documentViewModel;
        OnPropertyChanged(nameof(HasDocument));
        if (documentViewModel is not null)
        {
            Messages.Add(new AiChatItemViewModel(
                AiChatItemKind.System,
                string.Format(Resource("AiActiveDocumentFormat", "Active document: {0}"), documentViewModel.CadEditor.Document.Name)));
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenSettings))]
    private async Task OpenSettingsAsync()
    {
        var result = await _dialogService.ShowAiAssistantSettingsDialogAsync(
            new AiAssistantSettingsDialogRequest(
                CreateSettings(),
                LmStudioModels.ToArray(),
                CodexModels.ToArray(),
                LoadModelsAsync),
            ViewServiceIdentifiers.RootDialogHost);
        if (result is null)
            return;

        ApplySettings(result.Settings);
        ReplaceModels(LmStudioModels, result.LmStudioModels);
        ReplaceModels(CodexModels, result.CodexModels);
        SaveSettings();
        await _codexAgentClient.ResetConversationAsync();
        _conversation.Clear();
        UpdateConfiguredStatus();
    }

    private bool CanOpenSettings() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanManageImages))]
    private void AddFileFromFile()
    {
        AttachFile(_fileDialogService.OpenFile());
    }

    public void AttachFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        try
        {
            AddAttachment(_fileImportService.Load(filePath));
        }
        catch (Exception exception)
        {
            AddError(exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageImages))]
    private void PasteFile()
    {
        try
        {
            var files = _fileImportService.LoadFilesFromClipboard();
            if (files.Count > 0)
            {
                foreach (var file in files)
                    AddAttachment(file);
                return;
            }

            var image = _imageImportService.LoadFromClipboard();
            if (image is null)
            {
                AddError(Resource("AiClipboardNoImage", "The clipboard does not contain an image."));
                return;
            }

            AddAttachment(new AiFileImportData(
                image.SourceName,
                image.ContentType,
                DataUrl: _imageImportService.CreatePngDataUrl(image)));
        }
        catch (Exception exception)
        {
            AddError(exception.Message);
        }
    }

    [RelayCommand]
    private void RemoveImage(AiImageAttachmentViewModel? attachment)
    {
        if (attachment is not null)
            ImageAttachments.Remove(attachment);
        SendCommand.NotifyCanExecuteChanged();
    }

    private bool CanManageImages() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var prompt = UserInput.Trim();
        var attachments = Attachments.ToArray();
        if (prompt.Length == 0 && attachments.Length == 0 ||
            (Provider == AiAssistantProvider.LmStudio && string.IsNullOrWhiteSpace(SelectedModel)))
            return;

        var requestPrompt = prompt.Length > 0
            ? prompt
            : Resource("AiImagePrompt", "Please analyze the attached files.");
        var contentParts = new List<AiChatContentPart>
        {
            AiChatContentPart.TextPart(requestPrompt)
        };
        contentParts.AddRange(attachments.Select(CreateContentPart));

        UserInput = string.Empty;
        Attachments.Clear();
        SendCommand.NotifyCanExecuteChanged();
        Messages.Add(new AiChatItemViewModel(AiChatItemKind.User, requestPrompt, attachments));
        var toolset = new CadAgentToolset(_workspace, _imageImportService);

        BeginRequest();
        var cancellationToken = _requestCancellation!.Token;
        try
        {
            ConnectionStatus = Resource("AiGenerating", "Generating...");
            if (Provider == AiAssistantProvider.Codex)
                await RunCodexAsync(requestPrompt, contentParts, toolset, cancellationToken);
            else
                await RunLmStudioAsync(requestPrompt, contentParts, toolset, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ConnectionStatus = Resource("AiCancelled", "Cancelled");
            Messages.Add(new AiChatItemViewModel(AiChatItemKind.System, ConnectionStatus));
        }
        catch (Exception exception)
        {
            ConnectionStatus = Resource("AiRequestFailed", "Request failed");
            AddError(exception.Message);
        }
        finally
        {
            EndRequest();
        }
    }

    private async Task RunLmStudioAsync(
        string prompt,
        IReadOnlyList<AiChatContentPart> contentParts,
        CadAgentToolset toolset,
        CancellationToken cancellationToken)
    {
        _conversation.AddUser(prompt, contentParts);
        var result = await _agentRunner.RunAsync(
                new AgentRunRequest(
                    Endpoint,
                    SelectedModel!,
                    toolset.CreateSystemPrompt(),
                    prompt,
                    _conversation,
                    ContextWindowTokens,
                    Temperature,
                    EnableCadTools ? toolset : null),
                ReportAgentEventAsync,
                cancellationToken);

        ContextWindowTokens = result.ContextWindowTokens;
        SaveSettings();
        CompleteRun(result.ResponseWasEmpty, result.Model ?? SelectedModel);
    }

    private async Task RunCodexAsync(
        string prompt,
        IReadOnlyList<AiChatContentPart> contentParts,
        CadAgentToolset toolset,
        CancellationToken cancellationToken)
    {
        var result = await _codexAgentClient.RunAsync(
            new CodexAgentRunRequest(
                prompt,
                toolset.CreateSystemPrompt(),
                CreateCodexOptions(CreateSettings()),
                EnableCadTools ? toolset : null,
                contentParts),
            ReportAgentEventAsync,
            cancellationToken);
        CompleteRun(
            result.ResponseWasEmpty,
            result.Model ?? SelectedCodexModel ?? "Codex");
    }

    private void CompleteRun(bool responseWasEmpty, string? model)
    {
        if (responseWasEmpty)
            AddError(Resource("AiEmptyResponse", "The model returned an empty response."));
        ConnectionStatus = string.Format(
            Resource("AiReadyModelFormat", "Ready: {0}"),
            model);
    }

    private void UpdateConfiguredStatus()
    {
        var model = Provider == AiAssistantProvider.Codex
            ? SelectedCodexModel ?? "Codex"
            : SelectedModel;
        ConnectionStatus = string.IsNullOrWhiteSpace(model)
            ? Resource("AiDisconnected", "Not connected")
            : string.Format(
                Resource("AiReadyModelFormat", "Ready: {0}"),
                model);
    }

    private bool CanSend() =>
        !IsBusy &&
        (!string.IsNullOrWhiteSpace(UserInput) || Attachments.Count > 0) &&
        (Provider == AiAssistantProvider.Codex || !string.IsNullOrWhiteSpace(SelectedModel));

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _requestCancellation?.Cancel();

    private bool CanStop() => IsBusy;

    [RelayCommand]
    private async Task ClearConversationAsync()
    {
        await _codexAgentClient.ResetConversationAsync();
        _conversation.Clear();
        Messages.Clear();
        Attachments.Clear();
        SendCommand.NotifyCanExecuteChanged();
        Messages.Add(new AiChatItemViewModel(
            AiChatItemKind.System,
            Resource("AiConversationCleared", "Conversation cleared.")));
    }

    private void ApplySettings(AiAssistantSettings settings)
    {
        settings.Normalize();
        Endpoint = settings.Endpoint;
        Provider = settings.Provider;
        SelectedModel = settings.Model;
        Temperature = settings.Temperature;
        EnableCadTools = settings.EnableCadTools;
        ContextWindowTokens = settings.ContextWindowTokens;
        CodexExecutablePath = settings.CodexExecutablePath;
        SelectedCodexModel = settings.CodexModel;
        CodexReasoningEffort = settings.CodexReasoningEffort;
        CodexServiceTier = settings.CodexServiceTier;
        if (!string.IsNullOrWhiteSpace(settings.Model))
            ReplaceModels(LmStudioModels, [settings.Model]);
        if (!string.IsNullOrWhiteSpace(settings.CodexModel))
            ReplaceModels(CodexModels, [settings.CodexModel]);
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(CreateSettings());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AddError(exception.Message);
        }
    }

    private void BeginRequest()
    {
        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        IsBusy = true;
    }

    private async Task<IReadOnlyList<string>> LoadModelsAsync(
        AiAssistantSettings settings,
        CancellationToken cancellationToken)
    {
        settings.Normalize();
        return settings.Provider == AiAssistantProvider.Codex
            ? await _codexAgentClient.GetModelsAsync(CreateCodexOptions(settings), cancellationToken)
            : await _chatClient.GetModelsAsync(settings.Endpoint, cancellationToken);
    }

    private AiAssistantSettings CreateSettings()
    {
        var settings = new AiAssistantSettings
        {
            Provider = Provider,
            Endpoint = Endpoint,
            Model = SelectedModel ?? string.Empty,
            Temperature = Temperature,
            EnableCadTools = EnableCadTools,
            ContextWindowTokens = ContextWindowTokens,
            CodexExecutablePath = CodexExecutablePath,
            CodexModel = SelectedCodexModel ?? string.Empty,
            CodexReasoningEffort = CodexReasoningEffort,
            CodexServiceTier = CodexServiceTier
        };
        settings.Normalize();
        return settings;
    }

    private static CodexAgentOptions CreateCodexOptions(AiAssistantSettings settings) =>
        new(
            settings.CodexExecutablePath,
            settings.CodexModel,
            settings.CodexReasoningEffort,
            settings.CodexServiceTier,
            Environment.CurrentDirectory,
            settings.ContextWindowTokens);

    private ValueTask ReportAgentEventAsync(AgentRunEvent agentEvent)
    {
        switch (agentEvent.Kind)
        {
            case AgentRunEventKind.AssistantMessage when !string.IsNullOrWhiteSpace(agentEvent.Content):
                Messages.Add(new AiChatItemViewModel(AiChatItemKind.Assistant, agentEvent.Content));
                break;
            case AgentRunEventKind.ToolResult when agentEvent.ToolName is not null:
                Messages.Add(new AiChatItemViewModel(
                    AiChatItemKind.Tool,
                    $"{agentEvent.ToolName}: {CreateToolResultSummary(agentEvent.Content ?? string.Empty)}"));
                break;
            case AgentRunEventKind.ContextReduced:
                if (agentEvent.ContextWindowTokens is { } contextWindowTokens)
                    ContextWindowTokens = contextWindowTokens;
                ConnectionStatus = Resource("AiReducingContext", "Reducing conversation context and retrying...");
                break;
        }

        return ValueTask.CompletedTask;
    }

    private void EndRequest()
    {
        IsBusy = false;
        _requestCancellation?.Dispose();
        _requestCancellation = null;
    }

    private void AddError(string message) => Messages.Add(new AiChatItemViewModel(AiChatItemKind.Error, message));

    private void AddAttachment(AiFileImportData file)
    {
        if (file.IsImage && file.DataUrl!.Length > 14_000_000)
        {
            AddError(Resource("AiImageTooLarge", "The image is too large to attach."));
            return;
        }

        if (!file.IsImage && string.IsNullOrWhiteSpace(file.TextContent))
        {
            AddError("The selected file is empty.");
            return;
        }

        Attachments.Add(new AiImageAttachmentViewModel(
            file.SourceName,
            file.DataUrl,
            file.TextContent,
            file.ContentType));
        SendCommand.NotifyCanExecuteChanged();
    }

    private static AiChatContentPart CreateContentPart(AiImageAttachmentViewModel attachment) =>
        attachment.IsImage
            ? AiChatContentPart.Image(attachment.DataUrl!)
            : AiChatContentPart.FileText(
                attachment.SourceName,
                attachment.ContentType,
                attachment.TextContent!);

    private static string CreateToolResultSummary(string result)
    {
        if (result.Length <= 180)
            return result;
        return string.Concat(result.AsSpan(0, 177), "...");
    }

    private static void ReplaceModels(
        ObservableCollection<string> target,
        IEnumerable<string> models)
    {
        target.Clear();
        foreach (var model in models
                     .Where(model => !string.IsNullOrWhiteSpace(model))
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            target.Add(model);
        }
    }

    private static string Resource(string key, string fallback) =>
        Strings.ResourceManager.GetString(key, Strings.Culture) ?? fallback;

    public void Dispose()
    {
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
    }
}

public enum AiChatItemKind
{
    System,
    User,
    Assistant,
    Tool,
    Error
}

public sealed class AiChatItemViewModel(
    AiChatItemKind kind,
    string content,
    IReadOnlyList<AiImageAttachmentViewModel>? attachments = null)
{
    public AiChatItemKind Kind { get; } = kind;
    public string Content { get; } = content;
    public IReadOnlyList<AiImageAttachmentViewModel> Attachments { get; } = attachments?.ToArray() ?? [];
    public IReadOnlyList<AiImageAttachmentViewModel> Images =>
        Attachments.Where(attachment => attachment.IsImage).ToArray();
    public string Role => Kind switch
    {
        AiChatItemKind.User => Resource("AiYou", "You"),
        AiChatItemKind.Assistant => Resource("AiAssistantShort", "AI"),
        AiChatItemKind.Tool => Resource("AiCadTool", "CAD tool"),
        AiChatItemKind.Error => Resource("Error", "Error"),
        _ => Resource("Status", "Status")
    };

    private static string Resource(string key, string fallback) =>
        Strings.ResourceManager.GetString(key, Strings.Culture) ?? fallback;
}

public sealed record AiImageAttachmentViewModel(
    string SourceName,
    string? DataUrl = null,
    string? TextContent = null,
    string ContentType = "text/plain")
{
    public bool IsImage => !string.IsNullOrWhiteSpace(DataUrl);
}
