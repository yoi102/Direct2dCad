using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Direct2dCad.Application.Platform;
using Direct2dCad.Mcp;
using ModelContextProtocol.Client;

namespace Direct2dCad.Application.Tests;

public sealed partial class HeadlessToolExecutionTests
{
    [Fact]
    public async Task McpServiceStartsChangesPortStopsAndRetriesAnOccupiedPort()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var workspace = new HeadlessWorkspace();
        using var ui = new McpUiContext();
        await using var service = new CadMcpService(ui, workspace);
        var first = AvailableMcpPort();
        await service.ConfigureAsync(true, first);
        Assert.Equal(CadMcpState.Running, service.Status.State);
        await using (var client = await ConnectMcpAsync(service.Status.Endpoint!, "2025-11-25", timeout.Token))
        {
            await service.ConfigureAsync(true, first);
            Assert.NotEmpty(await client.ListToolsAsync(cancellationToken: timeout.Token));
        }
        using var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        var second = ((IPEndPoint)occupied.LocalEndpoint).Port;
        await service.ConfigureAsync(true, second);
        Assert.Equal(CadMcpState.Failed, service.Status.State);
        Assert.NotEmpty(service.Status.Error!);
        Assert.Null(service.Status.Endpoint);
        occupied.Stop();
        await service.ConfigureAsync(true, second);
        Assert.Equal(second, service.Status.Endpoint!.Port);
        Assert.Equal(CadMcpState.Running, service.Status.State);
        var third = AvailableMcpPort();
        await Task.WhenAll(service.ConfigureAsync(false, second), service.ConfigureAsync(true, third));
        Assert.Equal(third, service.Status.Endpoint!.Port);
        await service.ConfigureAsync(false, third);
        Assert.Equal(CadMcpState.Stopped, service.Status.State);
        Assert.Null(service.Status.Endpoint);
        using var released = new TcpListener(IPAddress.Loopback, third);
        released.Start();
    }

    [Theory]
    [InlineData("2025-11-25", false)]
    [InlineData("2026-07-28", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", true)]
    public async Task McpSettingsToolFinishesItsResponseBeforeReconfiguringItself(string version, bool changePort)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var workspace = new HeadlessWorkspace();
        using var ui = new McpUiContext();
        await using var service = new CadMcpService(ui, workspace);
        await service.ConfigureAsync(true, AvailableMcpPort());
        var oldEndpoint = service.Status.Endpoint!;
        var newPort = AvailableMcpPort();
        Task? reconfiguration = null;
        workspace.SettingsHandler = (patch, _) =>
        {
            reconfiguration = service.ConfigureAsync(changePort, newPort);
            return patch.Clone();
        };
        await using var client = await ConnectMcpAsync(oldEndpoint, version, timeout.Token);
        var result = await client.CallToolAsync("set_application_settings", new Dictionary<string, object?>
        {
            ["settings"] = new { mcp = new { is_enabled = changePort, port = newPort } }
        }, cancellationToken: timeout.Token);
        Assert.False(result.IsError);
        Assert.NotNull(reconfiguration);
        await reconfiguration.WaitAsync(timeout.Token);
        Assert.Equal(changePort ? CadMcpState.Running : CadMcpState.Stopped, service.Status.State);
        if (changePort)
        {
            Assert.Equal(newPort, service.Status.Endpoint!.Port);
            await using var reconnected = await ConnectMcpAsync(service.Status.Endpoint, version, timeout.Token);
            Assert.NotEmpty(await reconnected.ListToolsAsync(cancellationToken: timeout.Token));
        }
        using var released = new TcpListener(IPAddress.Loopback, oldEndpoint.Port);
        released.Start();
    }

    [Fact]
    public async Task McpShutdownCancelsActiveCallsAndQueuedConfigurationWithoutLateListeners()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var workspace = new HeadlessWorkspace();
        workspace.CreateDocument("shutdown");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.CaptureHandler = async (_, _, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        };
        using var ui = new McpUiContext();
        await using var service = new CadMcpService(ui, workspace);
        await service.ConfigureAsync(true, AvailableMcpPort());
        await using var client = await ConnectMcpAsync(service.Status.Endpoint!, "2026-07-28", timeout.Token);
        var capture = client.CallToolAsync("capture_view", cancellationToken: timeout.Token).AsTask();
        await entered.Task.WaitAsync(timeout.Token);
        var nextPort = AvailableMcpPort();
        var change = service.ConfigureAsync(true, nextPort);
        var queued = service.ConfigureAsync(true, AvailableMcpPort());
        await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8), timeout.Token);
        await Task.WhenAll(change, queued);
        await AssertMcpCallInterruptedAsync(capture);
        await service.ConfigureAsync(true, nextPort);
        Assert.Equal(CadMcpState.Stopped, service.Status.State);
        using var released = new TcpListener(IPAddress.Loopback, nextPort);
        released.Start();
    }

    private static int AvailableMcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
