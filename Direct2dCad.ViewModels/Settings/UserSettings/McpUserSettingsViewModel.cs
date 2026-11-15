using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Application.Platform;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Settings.UserSettings;

public partial class McpUserSettingsViewModel : UserSettingsSectionViewModel, IDisposable
{
    private readonly ICadMcpService? _service;
    private readonly IClipboardTextService? _clipboard;

    public McpUserSettingsViewModel(CadMcpUserSettings settings, ICadMcpService? service,
        IClipboardTextService? clipboard) : base("MCP")
    {
        _service = service;
        _clipboard = clipboard;
        IsEnabled = settings.IsEnabled;
        PortText = settings.Port.ToString(CultureInfo.InvariantCulture);
        if (_service is not null)
            _service.StatusChanged += OnStatusChanged;
        RefreshStatus();
    }

    [ObservableProperty] public partial bool IsEnabled { get; set; }
    [ObservableProperty] public partial string PortText { get; set; }
    [ObservableProperty] public partial string StatusText { get; private set; } = string.Empty;
    [ObservableProperty] public partial string ErrorMessage { get; private set; } = string.Empty;
    [ObservableProperty] public partial string CopyFeedback { get; private set; } = string.Empty;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(CopyEndpointCommand))]
    public partial string Endpoint { get; private set; } = string.Empty;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(CopyEndpointCommand))]
    public partial bool IsRunning { get; private set; }

    private void OnStatusChanged(object? sender, EventArgs e) => RefreshStatus();

    private void RefreshStatus()
    {
        var status = _service?.Status ?? new CadMcpStatus(CadMcpState.Stopped);
        StatusText = Localized(status.State switch
        {
            CadMcpState.Starting => "McpStarting",
            CadMcpState.Running => "McpRunning",
            CadMcpState.Stopping => "McpStopping",
            CadMcpState.Failed => "McpFailed",
            _ => "McpStopped"
        });
        IsRunning = status.State == CadMcpState.Running;
        Endpoint = IsRunning ? status.Endpoint?.AbsoluteUri ?? string.Empty : string.Empty;
        ErrorMessage = status.Error ?? string.Empty;
        CopyFeedback = string.Empty;
    }

    private bool CanCopyEndpoint() => IsRunning && Endpoint.Length > 0 && _clipboard is not null;

    [RelayCommand(CanExecute = nameof(CanCopyEndpoint))]
    private async Task CopyEndpointAsync()
    {
        try
        {
            await _clipboard!.SetTextAsync(Endpoint);
            CopyFeedback = Localized("McpAddressCopied");
        }
        catch (Exception exception)
        {
            CopyFeedback = exception.Message;
        }
    }

    internal override bool TryApplyTo(CadUserSettings settings)
    {
        if (!int.TryParse(PortText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
            return false;

        settings.Mcp.IsEnabled = IsEnabled;
        settings.Mcp.Port = port;
        return true;
    }

    internal override void ResetToDefaults()
    {
        var defaults = new CadMcpUserSettings();
        IsEnabled = defaults.IsEnabled;
        PortText = defaults.Port.ToString(CultureInfo.InvariantCulture);
        CopyFeedback = string.Empty;
    }

    public void Dispose()
    {
        if (_service is not null)
            _service.StatusChanged -= OnStatusChanged;
    }
}
