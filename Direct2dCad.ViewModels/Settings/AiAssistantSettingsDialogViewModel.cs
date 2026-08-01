using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.AI;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Settings;

public partial class AiAssistantSettingsDialogViewModel : ObservableObject
{
    private readonly Func<AiAssistantSettings, CancellationToken, Task<IReadOnlyList<string>>> _loadModelsAsync;

    public AiAssistantSettingsDialogViewModel(AiAssistantSettingsDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _loadModelsAsync = request.LoadModelsAsync;
        var settings = request.Settings.Clone();
        Provider = settings.Provider;
        Endpoint = settings.Endpoint;
        SelectedModel = settings.Model;
        Temperature = settings.Temperature;
        EnableCadTools = settings.EnableCadTools;
        ContextWindowTokens = settings.ContextWindowTokens;
        CodexExecutablePath = settings.CodexExecutablePath;
        SelectedCodexModel = settings.CodexModel;
        CodexReasoningEffort = settings.CodexReasoningEffort;
        CodexServiceTier = settings.CodexServiceTier;
        ReplaceModels(LmStudioModels, request.LmStudioModels);
        ReplaceModels(CodexModels, request.CodexModels);
    }

    public IReadOnlyList<AiAssistantProvider> ProviderOptions { get; } =
        Enum.GetValues<AiAssistantProvider>();

    public IReadOnlyList<string> ReasoningEffortOptions { get; } =
        ["none", "minimal", "low", "medium", "high", "xhigh"];

    public IReadOnlyList<string> ServiceTierOptions { get; } =
        [AiAssistantSettings.DefaultCodexServiceTier, AiAssistantSettings.FastCodexServiceTier];

    public ObservableCollection<string> LmStudioModels { get; } = [];
    public ObservableCollection<string> CodexModels { get; } = [];

    public bool IsLmStudio => Provider == AiAssistantProvider.LmStudio;
    public bool IsCodex => Provider == AiAssistantProvider.Codex;
    public bool HasConnectionStatus => !string.IsNullOrWhiteSpace(ConnectionStatus);

    public bool IsValid =>
        !IsBusy &&
        (IsCodex
            ? !string.IsNullOrWhiteSpace(CodexExecutablePath)
            : Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) &&
              endpoint.Scheme is "http" or "https" &&
              !string.IsNullOrWhiteSpace(SelectedModel));

    [ObservableProperty]
    public partial AiAssistantProvider Provider { get; set; }

    [ObservableProperty]
    public partial string Endpoint { get; set; } = AiAssistantSettings.DefaultEndpoint;

    [ObservableProperty]
    public partial string? SelectedModel { get; set; }

    [ObservableProperty]
    public partial double Temperature { get; set; } = 0.2;

    [ObservableProperty]
    public partial bool EnableCadTools { get; set; } = true;

    [ObservableProperty]
    public partial int ContextWindowTokens { get; set; } =
        AiAssistantSettings.DefaultContextWindowTokens;

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
    [NotifyCanExecuteChangedFor(nameof(RefreshModelsCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string ConnectionStatus { get; private set; } = string.Empty;

    [RelayCommand(CanExecute = nameof(CanRefreshModels))]
    private async Task RefreshModelsAsync()
    {
        IsBusy = true;
        ConnectionStatus = Localize("AiConnecting", "Connecting...");
        try
        {
            var models = await _loadModelsAsync(CreateSettings(), CancellationToken.None);
            var target = IsCodex ? CodexModels : LmStudioModels;
            ReplaceModels(target, models);
            if (IsCodex)
            {
                if (!string.IsNullOrWhiteSpace(SelectedCodexModel) &&
                    !CodexModels.Contains(SelectedCodexModel))
                {
                    SelectedCodexModel = null;
                }

                SelectedCodexModel ??= CodexModels.FirstOrDefault();
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(SelectedModel) &&
                    !LmStudioModels.Contains(SelectedModel))
                {
                    SelectedModel = null;
                }

                SelectedModel ??= LmStudioModels.FirstOrDefault();
            }

            ConnectionStatus = models.Count == 0
                ? Localize("AiNoModels", "Connected, but no model is loaded")
                : string.Format(
                    Localize("AiConnectedModelCountFormat", "Connected: {0} model(s)"),
                    models.Count);
        }
        catch (Exception exception)
        {
            ConnectionStatus = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRefreshModels() =>
        !IsBusy &&
        (IsCodex
            ? !string.IsNullOrWhiteSpace(CodexExecutablePath)
            : Uri.TryCreate(Endpoint, UriKind.Absolute, out _));

    public AiAssistantSettingsDialogResult CreateResult() =>
        new(CreateSettings(), LmStudioModels.ToArray(), CodexModels.ToArray());

    partial void OnProviderChanged(AiAssistantProvider value)
    {
        OnPropertyChanged(nameof(IsLmStudio));
        OnPropertyChanged(nameof(IsCodex));
        NotifyValidationChanged();
        RefreshModelsCommand.NotifyCanExecuteChanged();
    }

    partial void OnEndpointChanged(string value)
    {
        NotifyValidationChanged();
        RefreshModelsCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedModelChanged(string? value) => NotifyValidationChanged();

    partial void OnCodexExecutablePathChanged(string value)
    {
        NotifyValidationChanged();
        RefreshModelsCommand.NotifyCanExecuteChanged();
    }

    partial void OnConnectionStatusChanged(string value) =>
        OnPropertyChanged(nameof(HasConnectionStatus));

    partial void OnIsBusyChanged(bool value) => NotifyValidationChanged();

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

    private void NotifyValidationChanged() => OnPropertyChanged(nameof(IsValid));

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

    private static string Localize(string key, string fallback) =>
        Strings.ResourceManager.GetString(key, Strings.Culture) ?? fallback;
}
