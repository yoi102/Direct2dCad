using System.Text.Json;
using Direct2dCad.Application.Platform;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Settings.UserSettings;

namespace Direct2dCad.ViewModels.Tests;

public sealed class McpUserSettingsTests
{
    [Fact]
    public void OldSettingsDefaultToDisabledAndCopiesOwnTheirMcpPreferences()
    {
        var settings = JsonSerializer.Deserialize<CadUserSettings>("{\"Version\":2}")!;
        settings.Normalize();
        Assert.False(settings.Mcp.IsEnabled);
        Assert.Equal(5277, settings.Mcp.Port);
        settings.Mcp.IsEnabled = true;
        settings.Mcp.Port = 7777;
        var copy = settings.Clone();
        settings.Mcp.Port = 8888;
        Assert.True(copy.Mcp.IsEnabled);
        Assert.Equal(7777, copy.Mcp.Port);
        var restored = JsonSerializer.Deserialize<CadUserSettings>(JsonSerializer.Serialize(copy))!;
        Assert.True(restored.Mcp.IsEnabled);
        Assert.Equal(7777, restored.Mcp.Port);
        restored.Mcp = null!;
        restored.Normalize();
        Assert.False(restored.Mcp.IsEnabled);
        restored.Mcp.Port = -1;
        restored.Normalize();
        Assert.Equal(5277, restored.Mcp.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("5277.5")]
    [InlineData("abc")]
    [InlineData(" 5277")]
    public void InvalidPortPreventsSavingAndSelectsMcp(string port)
    {
        var store = new RecordingSettingsStore();
        using var vm = new UserSettingsViewModel(CadUserSettings.CreateDefault(), store, _ => Assert.Fail());
        vm.Mcp.PortText = port;
        Assert.False(vm.TryApply());
        Assert.Same(vm.Mcp, vm.SelectedSection);
        Assert.NotEmpty(vm.ValidationError!);
        Assert.Empty(store.Saved);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("65535", 65535)]
    [InlineData("5277", 5277)]
    public void McpEditsAndResetOnlyTakeEffectWhenApplied(string text, int port)
    {
        var settings = CadUserSettings.CreateDefault();
        var store = new RecordingSettingsStore();
        using var vm = new UserSettingsViewModel(settings, store, settings.CopyFrom);
        vm.Mcp.IsEnabled = true;
        vm.Mcp.PortText = text;
        Assert.False(settings.Mcp.IsEnabled);
        Assert.Empty(store.Saved);
        Assert.True(vm.TryApply());
        Assert.True(settings.Mcp.IsEnabled);
        Assert.Equal(port, settings.Mcp.Port);
        vm.ResetToDefaults();
        Assert.True(settings.Mcp.IsEnabled);
        Assert.True(vm.TryApply());
        Assert.False(settings.Mcp.IsEnabled);
        Assert.Equal(5277, settings.Mcp.Port);
    }

    [Fact]
    public async Task RuntimeStatusAndCopyUseTheActualListenerAndUnsubscribeOnClose()
    {
        var service = new RecordingMcpService();
        var clipboard = new RecordingClipboard();
        using var vm = new UserSettingsViewModel(CadUserSettings.CreateDefault(), new RecordingSettingsStore(),
            _ => { }, mcpService: service, clipboard: clipboard);
        Assert.False(vm.Mcp.CopyEndpointCommand.CanExecute(null));
        Assert.Equal(1, service.SubscriberCount);
        var endpoint = new Uri("http://127.0.0.1:5278/mcp");
        service.SetStatus(new(CadMcpState.Running, endpoint));
        vm.Mcp.PortText = "9999";
        Assert.True(vm.Mcp.CopyEndpointCommand.CanExecute(null));
        await vm.Mcp.CopyEndpointCommand.ExecuteAsync(null);
        Assert.Equal(endpoint.AbsoluteUri, clipboard.Text);
        Assert.NotEmpty(vm.Mcp.CopyFeedback);
        clipboard.Failure = new IOException("clipboard busy");
        await vm.Mcp.CopyEndpointCommand.ExecuteAsync(null);
        Assert.Equal("clipboard busy", vm.Mcp.CopyFeedback);
        service.SetStatus(new(CadMcpState.Failed, Error: "port occupied"));
        Assert.Equal("port occupied", vm.Mcp.ErrorMessage);
        Assert.Empty(vm.Mcp.Endpoint);
        Assert.False(vm.Mcp.CopyEndpointCommand.CanExecute(null));
        vm.Dispose();
        Assert.Equal(0, service.SubscriberCount);
        service.SetStatus(new(CadMcpState.Running, endpoint));
        Assert.Equal("port occupied", vm.Mcp.ErrorMessage);
    }

    [Fact]
    public void ApplicationSettingsExposeActualMcpStatusAndStartupFailures()
    {
        var service = new RecordingMcpService();
        using var context = new MainWindowTestContext(mcpService: service);
        var stopped = context.ViewModel.GetApplicationSettingsForTools().GetProperty("mcp_service");
        Assert.True(stopped.GetProperty("available").GetBoolean());
        Assert.Equal("Stopped", stopped.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, stopped.GetProperty("endpoint").ValueKind);
        Assert.Equal(JsonValueKind.Null, stopped.GetProperty("error").ValueKind);

        service.SetStatus(new(CadMcpState.Running, new Uri("http://127.0.0.1:5278/mcp")));
        var running = context.ViewModel.GetApplicationSettingsForTools();
        Assert.Equal(5277, running.GetProperty("settings").GetProperty("mcp").GetProperty("port").GetInt32());
        Assert.Equal("Running", running.GetProperty("mcp_service").GetProperty("state").GetString());
        Assert.Equal("http://127.0.0.1:5278/mcp", running.GetProperty("mcp_service").GetProperty("endpoint").GetString());

        service.SetStatus(new(CadMcpState.Failed, Error: "port occupied"));
        var failed = context.ViewModel.GetApplicationSettingsForTools().GetProperty("mcp_service");
        Assert.Equal("Failed", failed.GetProperty("state").GetString());
        Assert.Equal("port occupied", failed.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, failed.GetProperty("endpoint").ValueKind);
    }

    [Fact]
    public void ApplicationSettingsReportMcpAvailabilityForHostsWithoutTheService()
    {
        using var context = new MainWindowTestContext();
        var status = context.ViewModel.GetApplicationSettingsForTools().GetProperty("mcp_service");
        Assert.False(status.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("state").ValueKind);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("endpoint").ValueKind);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("error").ValueKind);
    }

    [Fact]
    public async Task CliOverridesStayInMemoryUntilSettingsAreSavedAndAiUsesTheSamePipeline()
    {
        var service = new RecordingMcpService();
        using var context = new MainWindowTestContext(mcpService: service);
        var savedBeforeStartup = context.Settings.Saved.Count;
        await context.ViewModel.InitializeMcpAsync(5279);
        Assert.Equal((true, 5279), Assert.Single(service.Configurations));
        Assert.Equal(savedBeforeStartup, context.Settings.Saved.Count);
        Assert.All(context.Settings.Saved, saved => Assert.False(saved.Mcp.IsEnabled));
        var settings = context.ViewModel.GetApplicationSettingsForTools().GetProperty("settings").GetProperty("mcp");
        Assert.True(settings.GetProperty("is_enabled").GetBoolean());
        Assert.Equal(5279, settings.GetProperty("port").GetInt32());
        using var patch = JsonDocument.Parse("{\"mcp\":{\"is_enabled\":false,\"port\":5280}}");
        context.ViewModel.SetApplicationSettingsForTools(patch.RootElement, false);
        Assert.Equal((false, 5280), service.Configurations.Last());
        Assert.Equal(savedBeforeStartup + 1, context.Settings.Saved.Count);
        Assert.Equal(5280, context.Settings.Saved.Last().Mcp.Port);
        var descriptor = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddViewModels()
            .Single(item => item.ServiceType == typeof(MainViewModel));
        Assert.Equal(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public async Task AccessTokenIsGeneratedCopiedAppliedAndReset()
    {
        var settings = CadUserSettings.CreateDefault();
        var clipboard = new RecordingClipboard();
        using var vm = new UserSettingsViewModel(settings, new RecordingSettingsStore(), settings.CopyFrom,
            clipboard: clipboard);
        Assert.Empty(vm.Mcp.AccessToken);
        Assert.False(vm.Mcp.CopyAccessTokenCommand.CanExecute(null));

        vm.Mcp.GenerateAccessTokenCommand.Execute(null);
        var token = vm.Mcp.AccessToken;
        Assert.True(CadMcpAccessToken.IsValid(token));
        Assert.True(vm.Mcp.CopyAccessTokenCommand.CanExecute(null));
        await vm.Mcp.CopyAccessTokenCommand.ExecuteAsync(null);
        Assert.Equal(token, clipboard.Text);
        Assert.Empty(settings.Mcp.AccessToken);

        Assert.True(vm.TryApply());
        Assert.Equal(token, settings.Mcp.AccessToken);
        var copy = settings.Clone();
        Assert.Equal(token, copy.Mcp.AccessToken);
        Assert.Equal(token, JsonSerializer.Deserialize<CadUserSettings>(JsonSerializer.Serialize(copy))!.Mcp.AccessToken);

        vm.ResetToDefaults();
        Assert.True(vm.TryApply());
        Assert.Empty(settings.Mcp.AccessToken);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("tab\tinside")]
    [InlineData("caf\u00e9")]
    public void AccessTokenThatCannotTravelInAHeaderPreventsSaving(string token)
    {
        var store = new RecordingSettingsStore();
        using var vm = new UserSettingsViewModel(CadUserSettings.CreateDefault(), store, _ => Assert.Fail());
        vm.Mcp.AccessToken = token;
        Assert.False(vm.TryApply());
        Assert.Same(vm.Mcp, vm.SelectedSection);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task AccessTokenReachesTheServiceButNeverTheAgentTools()
    {
        var service = new RecordingMcpService();
        using var context = new MainWindowTestContext(mcpService: service);
        await context.ViewModel.InitializeMcpAsync(5279, "cli-token");
        Assert.Equal("cli-token", service.AccessTokens.Last());

        var visible = context.ViewModel.GetApplicationSettingsForTools();
        Assert.False(visible.GetProperty("settings").GetProperty("mcp").TryGetProperty("access_token", out _));
        Assert.True(visible.GetProperty("mcp_service").GetProperty("requires_access_token").GetBoolean());
        Assert.DoesNotContain("cli-token", visible.GetRawText(), StringComparison.Ordinal);

        // A tool reset or patch keeps the token: the agent cannot switch authentication off.
        using var reset = JsonDocument.Parse("{}");
        context.ViewModel.SetApplicationSettingsForTools(reset.RootElement, true);
        Assert.Equal("cli-token", service.AccessTokens.Last());
        using var patch = JsonDocument.Parse("{\"mcp\":{\"access_token\":\"\"}}");
        context.ViewModel.SetApplicationSettingsForTools(patch.RootElement, false);
        Assert.Equal("cli-token", service.AccessTokens.Last());
    }

    private sealed class RecordingClipboard : IClipboardTextService
    {
        public string? Text { get; private set; }
        public Exception? Failure { get; set; }
        public string? LoadFromClipboard() => Text;
        public Task SetTextAsync(string text)
        {
            if (Failure is not null) throw Failure;
            Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingMcpService : ICadMcpService
    {
        private EventHandler? _changed;
        public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;
        public CadMcpStatus Status { get; private set; } = new(CadMcpState.Stopped);
        public event EventHandler? StatusChanged { add => _changed += value; remove => _changed -= value; }
        public List<(bool Enabled, int Port)> Configurations { get; } = [];
        public List<string?> AccessTokens { get; } = [];
        public Task ConfigureAsync(bool enabled, int port, string? accessToken = null)
        {
            Configurations.Add((enabled, port));
            AccessTokens.Add(accessToken);
            return Task.CompletedTask;
        }
        public void SetStatus(CadMcpStatus status)
        {
            Status = status;
            _changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
