namespace Direct2dCad.Application.Platform;

public enum CadMcpState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed
}

public sealed record CadMcpStatus(CadMcpState State, Uri? Endpoint = null, string? Error = null);

public interface ICadMcpService
{
    CadMcpStatus Status { get; }
    event EventHandler? StatusChanged;
    Task ConfigureAsync(bool enabled, int port);
}
