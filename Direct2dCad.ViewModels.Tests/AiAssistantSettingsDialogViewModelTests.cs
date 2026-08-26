using Direct2dCad.AI.Contracts;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Settings;

namespace Direct2dCad.ViewModels.Tests;

public sealed class AiAssistantSettingsDialogViewModelTests
{
    [Fact]
    public void Constructor_NormalizesSettingsAndSortsDistinctModels()
    {
        var viewModel = CreateViewModel(
            new AiAssistantSettings
            {
                Endpoint = " http://localhost:1234/v1/ ",
                Model = " local-model "
            },
            ["z-model", "", "a-model", "a-model"],
            ["codex-b", "codex-a", "codex-b"]);

        Assert.Equal("http://localhost:1234/v1", viewModel.Endpoint);
        Assert.Equal("local-model", viewModel.SelectedModel);
        Assert.Equal(["a-model", "z-model"], viewModel.LmStudioModels);
        Assert.Equal(["codex-a", "codex-b"], viewModel.CodexModels);
        Assert.True(viewModel.IsLmStudio);
        Assert.False(viewModel.IsCodex);
        Assert.True(viewModel.IsValid);
    }

    [Fact]
    public void IsValid_TracksProviderSpecificInputs()
    {
        var viewModel = CreateViewModel();

        viewModel.Endpoint = "not-an-http-url";
        Assert.False(viewModel.IsValid);

        viewModel.Endpoint = "https://example.test/v1";
        viewModel.SelectedModel = null;
        Assert.False(viewModel.IsValid);

        viewModel.Provider = AiAssistantProvider.Codex;
        Assert.True(viewModel.IsCodex);
        Assert.False(viewModel.IsLmStudio);
        Assert.True(viewModel.IsValid);

        viewModel.CodexExecutablePath = "";
        Assert.False(viewModel.IsValid);
        Assert.False(viewModel.RefreshModelsCommand.CanExecute(null));

        viewModel.CodexExecutablePath = "codex";
        Assert.True(viewModel.IsValid);
        Assert.True(viewModel.RefreshModelsCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshModelsAsync_LmStudioReplacesModelsAndSelectsFirstAvailable()
    {
        AiAssistantSettings? loadedSettings = null;
        var viewModel = CreateViewModel(loadModelsAsync: (settings, _) =>
        {
            loadedSettings = settings;
            return Task.FromResult<IReadOnlyList<string>>(["z-model", "new-model", "new-model"]);
        });
        viewModel.SelectedModel = "old-model";

        await viewModel.RefreshModelsCommand.ExecuteAsync(null);

        Assert.Equal(["new-model", "z-model"], viewModel.LmStudioModels);
        Assert.Equal("new-model", viewModel.SelectedModel);
        Assert.Equal("old-model", loadedSettings!.Model);
        Assert.Equal(
            string.Format(Strings.AiConnectedModelCountFormat, 2),
            viewModel.ConnectionStatus);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task RefreshModelsAsync_CodexClearsUnavailableSelection()
    {
        var viewModel = CreateViewModel(
            new AiAssistantSettings
            {
                Provider = AiAssistantProvider.Codex,
                CodexModel = "missing-model"
            },
            loadModelsAsync: (_, _) =>
                Task.FromResult<IReadOnlyList<string>>(["codex-b", "codex-a"]));

        await viewModel.RefreshModelsCommand.ExecuteAsync(null);

        Assert.Equal(["codex-a", "codex-b"], viewModel.CodexModels);
        Assert.Equal("codex-a", viewModel.SelectedCodexModel);
        Assert.Equal(
            string.Format(Strings.AiConnectedModelCountFormat, 2),
            viewModel.ConnectionStatus);
    }

    [Fact]
    public async Task RefreshModelsAsync_ReportsEmptyResultAndRestoresBusyState()
    {
        var viewModel = CreateViewModel(
            loadModelsAsync: (_, _) =>
                Task.FromResult<IReadOnlyList<string>>([]));

        await viewModel.RefreshModelsCommand.ExecuteAsync(null);

        Assert.Equal(Strings.AiNoModels, viewModel.ConnectionStatus);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.RefreshModelsCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshModelsAsync_ReportsProviderFailureAndRestoresBusyState()
    {
        var viewModel = CreateViewModel(
            loadModelsAsync: (_, _) =>
                Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("connection failed")));

        await viewModel.RefreshModelsCommand.ExecuteAsync(null);

        Assert.Equal("connection failed", viewModel.ConnectionStatus);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.RefreshModelsCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshModelsAsync_DisablesRefreshWhileRequestIsRunning()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = CreateViewModel(loadModelsAsync: async (_, _) =>
        {
            started.SetResult(true);
            await release.Task;
            return (IReadOnlyList<string>)["model"];
        });

        var refresh = viewModel.RefreshModelsCommand.ExecuteAsync(null);
        await started.Task;

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.RefreshModelsCommand.CanExecute(null));

        release.SetResult(true);
        await refresh;

        Assert.False(viewModel.IsBusy);
        Assert.Equal("model", viewModel.SelectedModel);
    }

    [Fact]
    public void CreateResult_ReturnsNormalizedSettingsAndCurrentModelLists()
    {
        var viewModel = CreateViewModel();
        viewModel.Temperature = 99;
        viewModel.ContextWindowTokens = 1;
        viewModel.LmStudioModels.Add("model-a");
        viewModel.CodexModels.Add("codex-a");

        var result = viewModel.CreateResult();

        Assert.Equal(2, result.Settings.Temperature);
        Assert.Equal(AiAssistantSettings.MinimumContextWindowTokens, result.Settings.ContextWindowTokens);
        Assert.Equal(["model-a"], result.LmStudioModels);
        Assert.Equal(["codex-a"], result.CodexModels);
    }

    private static AiAssistantSettingsDialogViewModel CreateViewModel(
        AiAssistantSettings? settings = null,
        IReadOnlyList<string>? lmStudioModels = null,
        IReadOnlyList<string>? codexModels = null,
        Func<AiAssistantSettings, CancellationToken, Task<IReadOnlyList<string>>>? loadModelsAsync = null) =>
        new(new AiAssistantSettingsDialogRequest(
            settings ?? new AiAssistantSettings { Model = "model" },
            lmStudioModels ?? [],
            codexModels ?? [],
            loadModelsAsync ?? ((_, _) => Task.FromResult<IReadOnlyList<string>>([]))));
}
