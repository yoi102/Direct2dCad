using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.AI;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.AI;
using Direct2dCad.ViewModels.Services.Platform;
using AvalonDock.Core;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class AiAssistantToolboxViewModel : CadToolboxViewModelBase, IDisposable
{
    private const int MaximumToolRounds = 12;

    private readonly IAiChatClient _chatClient;
    private readonly IAiAssistantSettingsStore _settingsStore;
    private readonly List<AiChatMessage> _conversation = [];
    private CancellationTokenSource? _requestCancellation;
    private CadDocumentViewModel? _documentViewModel;
    private bool _isApplyingSettings;

    public AiAssistantToolboxViewModel(
        IToolboxLayoutSettingsStore toolboxLayoutSettingsStore,
        IToolboxIconProvider toolboxIconProvider,
        IAiChatClient chatClient,
        IAiAssistantSettingsStore settingsStore)
        : base(toolboxLayoutSettingsStore, "toolbox.ai-assistant", DockZone.RightBottom, isOpenByDefault: false)
    {
        _chatClient = chatClient;
        _settingsStore = settingsStore;
        Title = Resource("AiAssistant", "AI Assistant");
        Icon = toolboxIconProvider.Assistant;
        Shortcut = "Ctrl+Shift+A";
        CanClose = false;

        ApplySettings(settingsStore.Load());
        Messages.Add(new AiChatItemViewModel(
            AiChatItemKind.System,
            Resource("AiAssistantWelcome", "Connect to LM Studio, select a model, and ask me to inspect or edit the active drawing.")));
    }

    public ObservableCollection<AiChatItemViewModel> Messages { get; } = [];
    public ObservableCollection<string> Models { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string UserInput { get; set; } = string.Empty;

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
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshModelsCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
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

    partial void OnEndpointChanged(string value) => SaveSettings();
    partial void OnSelectedModelChanged(string? value) => SaveSettings();
    partial void OnTemperatureChanged(double value) => SaveSettings();
    partial void OnEnableCadToolsChanged(bool value) => SaveSettings();

    [RelayCommand(CanExecute = nameof(CanRefreshModels))]
    private async Task RefreshModelsAsync()
    {
        BeginRequest();
        var cancellationToken = _requestCancellation!.Token;
        try
        {
            ConnectionStatus = Resource("AiConnecting", "Connecting...");
            var models = await _chatClient.GetModelsAsync(Endpoint, cancellationToken);
            var previousModel = SelectedModel;
            Models.Clear();
            foreach (var model in models)
                Models.Add(model);

            SelectedModel = previousModel is not null && Models.Contains(previousModel)
                ? previousModel
                : Models.FirstOrDefault();
            ConnectionStatus = Models.Count == 0
                ? Resource("AiNoModels", "Connected, but no model is loaded")
                : string.Format(Resource("AiConnectedModelCountFormat", "Connected: {0} model(s)"), Models.Count);
        }
        catch (OperationCanceledException)
        {
            ConnectionStatus = Resource("AiCancelled", "Cancelled");
        }
        catch (Exception exception)
        {
            ConnectionStatus = Resource("AiConnectionFailed", "Connection failed");
            AddError(exception.Message);
        }
        finally
        {
            EndRequest();
        }
    }

    private bool CanRefreshModels() => !IsBusy && !string.IsNullOrWhiteSpace(Endpoint);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var prompt = UserInput.Trim();
        if (prompt.Length == 0 || string.IsNullOrWhiteSpace(SelectedModel))
            return;

        UserInput = string.Empty;
        Messages.Add(new AiChatItemViewModel(AiChatItemKind.User, prompt));
        _conversation.Add(AiChatMessage.User(prompt));
        var batchId = Guid.NewGuid();
        var executor = _documentViewModel is null
            ? null
            : new CadAiToolExecutor(_documentViewModel, batchId);

        BeginRequest();
        var cancellationToken = _requestCancellation!.Token;
        try
        {
            ConnectionStatus = Resource("AiGenerating", "Generating...");
            for (var round = 0; round < MaximumToolRounds; round++)
            {
                var requestMessages = new List<AiChatMessage>(_conversation.Count + 1)
                {
                    AiChatMessage.System(executor?.CreateSystemPrompt() ?? CreateChatOnlySystemPrompt())
                };
                requestMessages.AddRange(_conversation);

                var completion = await _chatClient.CompleteAsync(
                    new AiChatRequest(
                        Endpoint,
                        SelectedModel,
                        requestMessages,
                        EnableCadTools && executor is not null ? CadAiToolExecutor.ToolDefinitions : [],
                        Temperature),
                    cancellationToken);

                _conversation.Add(AiChatMessage.Assistant(completion.Content, completion.ToolCalls));
                if (!string.IsNullOrWhiteSpace(completion.Content))
                    Messages.Add(new AiChatItemViewModel(AiChatItemKind.Assistant, completion.Content.Trim()));

                if (completion.ToolCalls.Count == 0)
                {
                    if (string.IsNullOrWhiteSpace(completion.Content))
                        AddError(Resource("AiEmptyResponse", "The model returned an empty response."));
                    ConnectionStatus = string.Format(
                        Resource("AiReadyModelFormat", "Ready: {0}"),
                        completion.Model ?? SelectedModel);
                    return;
                }

                if (executor is null || !EnableCadTools)
                    throw new InvalidOperationException("The model requested CAD tools, but CAD tools are unavailable.");

                foreach (var toolCall in completion.ToolCalls)
                {
                    var result = executor.Execute(toolCall);
                    _conversation.Add(AiChatMessage.Tool(toolCall.Id, result));
                    Messages.Add(new AiChatItemViewModel(
                        AiChatItemKind.Tool,
                        $"{toolCall.Name}: {CreateToolResultSummary(result)}"));
                }
            }

            throw new InvalidOperationException("The model exceeded the maximum number of CAD tool rounds.");
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

    private bool CanSend() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(UserInput) &&
        !string.IsNullOrWhiteSpace(SelectedModel);

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _requestCancellation?.Cancel();

    private bool CanStop() => IsBusy;

    [RelayCommand]
    private void ClearConversation()
    {
        _conversation.Clear();
        Messages.Clear();
        Messages.Add(new AiChatItemViewModel(
            AiChatItemKind.System,
            Resource("AiConversationCleared", "Conversation cleared.")));
    }

    private void ApplySettings(AiAssistantSettings settings)
    {
        settings.Normalize();
        _isApplyingSettings = true;
        try
        {
            Endpoint = settings.Endpoint;
            SelectedModel = settings.Model;
            Temperature = settings.Temperature;
            EnableCadTools = settings.EnableCadTools;
            if (!string.IsNullOrWhiteSpace(settings.Model))
                Models.Add(settings.Model);
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void SaveSettings()
    {
        if (_isApplyingSettings)
            return;

        try
        {
            _settingsStore.Save(new AiAssistantSettings
            {
                Endpoint = Endpoint,
                Model = SelectedModel ?? string.Empty,
                Temperature = Temperature,
                EnableCadTools = EnableCadTools
            });
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

    private void EndRequest()
    {
        IsBusy = false;
        _requestCancellation?.Dispose();
        _requestCancellation = null;
    }

    private void AddError(string message) => Messages.Add(new AiChatItemViewModel(AiChatItemKind.Error, message));

    private static string CreateChatOnlySystemPrompt() =>
        "You are the Direct2dCad assistant. No CAD document is active, so explain that drawing tools require an open document when the user requests edits. Keep replies concise.";

    private static string CreateToolResultSummary(string result)
    {
        if (result.Length <= 180)
            return result;
        return string.Concat(result.AsSpan(0, 177), "...");
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

public sealed class AiChatItemViewModel(AiChatItemKind kind, string content)
{
    public AiChatItemKind Kind { get; } = kind;
    public string Content { get; } = content;
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
