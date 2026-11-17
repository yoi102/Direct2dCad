using System.Globalization;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Direct2dCad.Mcp.Stdio;

public sealed record CadMcpStdioOptions(int Port, string? AccessToken, bool Launch, TimeSpan LaunchTimeout)
{
    public static readonly TimeSpan DefaultLaunchTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Parses the bridge's command line; the token falls back to DIRECT2DCAD_MCP_TOKEN.</summary>
    public static CadMcpStdioOptions Parse(IReadOnlyList<string> arguments, Func<string, string?>? readEnvironment = null)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case CadMcpStdioBridge.Argument or "--mcp" or "--no-launch":
                    break;
                case "--mcp-port" or "--mcp-token":
                    index++; // Values are validated by the shared parsers below.
                    break;
                default:
                    throw new ArgumentException($"Unknown argument for {CadMcpStdioBridge.Argument}: {arguments[index]}");
            }
        }
        return new(
            CadMcpServer.GetRequestedPort(arguments) ?? CadMcpServer.DefaultPort,
            CadMcpServer.GetRequestedAccessToken(arguments, readEnvironment),
            !arguments.Contains("--no-launch"),
            DefaultLaunchTimeout);
    }
}

/// <summary>
/// MCP over stdio for clients that launch their servers as processes. Running the desktop
/// executable with --mcp-stdio opens no window: it serves the tool catalog on stdin/stdout and
/// forwards each tool call to the desktop instance's loopback HTTP endpoint, starting that instance
/// on the first call when nothing is listening. The drawing the user has open is the one the
/// client edits, and authentication, origin checks and UI-thread dispatch stay in one place.
/// </summary>
public static class CadMcpStdioBridge
{
    public const string Argument = "--mcp-stdio";

    public const string Usage =
        "Usage: <Direct2dCad executable> --mcp-stdio [--mcp-port <port>] [--mcp-token <token>] [--no-launch]\n" +
        "  Serves MCP on stdin/stdout and forwards tool calls to Direct2dCad at http://127.0.0.1:<port>/mcp.\n" +
        "  --mcp-port   Port of the desktop MCP server (default 5277).\n" +
        "  --mcp-token  Access token; prefer the DIRECT2DCAD_MCP_TOKEN environment variable.\n" +
        "  --no-launch  Report an error instead of starting Direct2dCad when nothing is listening.";

    public static bool IsRequested(IReadOnlyList<string> arguments) => arguments.Contains(Argument);

    /// <summary>Runs the bridge on the process's standard streams and returns the exit code.</summary>
    public static int Run(IReadOnlyList<string> arguments)
    {
        var error = Console.Error;
        CadMcpStdioOptions options;
        try
        {
            options = CadMcpStdioOptions.Parse(arguments);
        }
        catch (ArgumentException exception)
        {
            error.WriteLine(exception.Message);
            error.WriteLine(Usage);
            return 2;
        }

        var input = Console.OpenStandardInput();
        var output = Console.OpenStandardOutput();
        // stdout carries only protocol messages; anything else written to Console goes to stderr.
        Console.SetOut(error);
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, args) =>
        {
            args.Cancel = true;
            stop.Cancel();
        };
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is unknown.");
        try
        {
            RunAsync(input, output, options, new CadMcpInstanceLauncher(executable), Log, stop.Token)
                .GetAwaiter().GetResult();
            return 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Log($"Stopped: {exception}");
            return 1;
        }

        void Log(string message)
        {
            lock (error)
                error.WriteLine($"[Direct2dCad MCP {DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] {message}");
        }
    }

    /// <summary>Serves one client until the input ends or the token is cancelled.</summary>
    internal static async Task RunAsync(
        Stream input,
        Stream output,
        CadMcpStdioOptions options,
        ICadMcpInstanceLauncher launcher,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        await using var upstream = new CadMcpStdioUpstream(options, launcher, log);
        // The catalog is served locally, so a client can list tools before the app is running.
        var tools = CadMcpToolCatalog.CreateTools();
        var serverOptions = new McpServerOptions
        {
            ServerInfo = new() { Name = CadMcpToolCatalog.ServerName, Version = CadMcpToolCatalog.ServerVersion },
            ServerInstructions = CadMcpToolCatalog.ServerInstructions,
            Capabilities = new() { Tools = new() },
            Handlers = new()
            {
                ListToolsHandler = (request, _) => ValueTask.FromResult(CadMcpToolCatalog.List(tools, request.Params)),
                CallToolHandler = async (request, token) =>
                {
                    var call = request.Params ?? throw new McpProtocolException(
                        "tools/call requires params.", McpErrorCode.InvalidParams);
                    // An unknown name is refused here, before it can start the desktop app.
                    CadMcpToolCatalog.ThrowIfUnknown(call.Name);
                    return await upstream.CallToolAsync(call, token).ConfigureAwait(false);
                }
            }
        };
        await using var transport = new StreamServerTransport(
            new DuplicatePropertyRejectingStream(input, log), output, CadMcpToolCatalog.ServerName);
        await using var server = McpServer.Create(transport, serverOptions);
        log($"Serving MCP on stdio; tool calls go to {upstream.Endpoint}.");
        await server.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
