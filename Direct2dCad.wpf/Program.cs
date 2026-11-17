using System.Runtime.CompilerServices;
using Direct2dCad.Mcp.Stdio;

namespace Direct2dCad;

/// <summary>
/// Process entry point. <c>--mcp-stdio</c> runs the windowless MCP stdio bridge for clients that
/// launch servers as processes; every other command line starts the WPF application as before.
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (CadMcpStdioBridge.IsRequested(args))
        {
            Environment.ExitCode = CadMcpStdioBridge.Run(args);
            return;
        }
        RunApplication();
    }

    // Kept out of Main so the bridge path never loads the WPF assemblies that App derives from.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunApplication() => App.Main();
}
