using System.Diagnostics;
using System.Text;

namespace Direct2dCad.Agent.Codex;

internal interface ICodexAppServerTransport : IDisposable
{
    Task<string?> ReadLineAsync(CancellationToken cancellationToken);
    Task WriteLineAsync(string line, CancellationToken cancellationToken);
    string GetErrorSummary();
}

internal interface ICodexAppServerTransportFactory
{
    ICodexAppServerTransport Start(CodexAgentOptions options);
}

internal sealed class CodexAppServerTransportFactory : ICodexAppServerTransportFactory
{
    public ICodexAppServerTransport Start(CodexAgentOptions options) =>
        new ProcessCodexAppServerTransport(options);
}

internal sealed class ProcessCodexAppServerTransport : ICodexAppServerTransport
{
    private const int MaximumErrorLines = 20;

    private readonly Process _process;
    private readonly Task _errorPump;
    private readonly Queue<string> _errorLines = [];
    private readonly object _errorGate = new();

    public ProcessCodexAppServerTransport(CodexAgentOptions options)
    {
        var executable = ResolveExecutable(options.ExecutablePath);
        var startInfo = CreateStartInfo(executable, NormalizeServiceTier(options.ServiceTier));
        _process = Process.Start(startInfo) ??
                   throw new InvalidOperationException("Unable to start the Codex app-server process.");
        _errorPump = PumpErrorsAsync(_process.StandardError);
    }

    public Task<string?> ReadLineAsync(CancellationToken cancellationToken) =>
        _process.StandardOutput.ReadLineAsync(cancellationToken).AsTask();

    public Task WriteLineAsync(string line, CancellationToken cancellationToken) =>
        _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);

    public string GetErrorSummary()
    {
        lock (_errorGate)
            return string.Join(Environment.NewLine, _errorLines);
    }

    public void Dispose()
    {
        try
        {
            _process.StandardInput.Close();
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
            _errorPump.Wait(TimeSpan.FromSeconds(1));
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _process.Dispose();
        }
    }

    private async Task PumpErrorsAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                lock (_errorGate)
                {
                    _errorLines.Enqueue(line);
                    while (_errorLines.Count > MaximumErrorLines)
                        _errorLines.Dequeue();
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static ProcessStartInfo CreateStartInfo(string executable, string serviceTier)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (OperatingSystem.IsWindows() &&
            Path.GetExtension(executable) is ".cmd" or ".bat")
        {
            startInfo.FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(
                $"\"\"{executable}\" -c service_tier=\\\"{serviceTier}\\\" app-server --listen stdio://\"");
            return startInfo;
        }

        startInfo.FileName = executable;
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add($"service_tier=\"{serviceTier}\"");
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--listen");
        startInfo.ArgumentList.Add("stdio://");
        return startInfo;
    }

    private static string ResolveExecutable(string configuredPath)
    {
        var value = string.IsNullOrWhiteSpace(configuredPath) ? "codex" : configuredPath.Trim();
        if (Path.IsPathFullyQualified(value))
        {
            if (!File.Exists(value))
                throw new FileNotFoundException($"Codex executable was not found: {value}", value);
            return value;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", string.Empty }
            : new[] { string.Empty };
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory.Trim(), value + extension);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException(
            $"Codex executable '{value}' was not found. Install Codex CLI or set its executable path.");
    }

    private static string NormalizeServiceTier(string value) =>
        string.Equals(value, "fast", StringComparison.OrdinalIgnoreCase) ? "fast" : "flex";
}
