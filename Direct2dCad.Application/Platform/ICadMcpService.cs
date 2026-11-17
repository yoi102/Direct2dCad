namespace Direct2dCad.Application.Platform;

public enum CadMcpState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed
}

public static class CadMcpAccessToken
{
    public const int MaximumLength = 512;

    /// <summary>A token travels in an HTTP header, so it must be visible ASCII without spaces.</summary>
    public static bool IsValid(string? token) =>
        !string.IsNullOrEmpty(token) && token.Length <= MaximumLength &&
        token.All(character => character is > ' ' and <= '~');

    /// <summary>Creates a 256-bit random token that is safe to paste into a header or a config file.</summary>
    public static string Generate() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record CadMcpStatus(CadMcpState State, Uri? Endpoint = null, string? Error = null);

public interface ICadMcpService
{
    CadMcpStatus Status { get; }
    event EventHandler? StatusChanged;
    /// <param name="accessToken">When not empty, requests must carry <c>Authorization: Bearer &lt;token&gt;</c>.</param>
    Task ConfigureAsync(bool enabled, int port, string? accessToken = null);
}
