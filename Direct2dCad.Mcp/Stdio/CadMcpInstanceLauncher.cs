using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Direct2dCad.Mcp.Stdio;

/// <summary>Finds or starts the desktop instance that the stdio bridge forwards to.</summary>
internal interface ICadMcpInstanceLauncher
{
    /// <summary>True when a desktop window of this application is already open.</summary>
    bool IsInstanceRunning();

    /// <summary>Serializes launches across bridge processes, so two clients do not open two windows.</summary>
    Task<IDisposable> AcquireLaunchLockAsync(int port, CancellationToken cancellationToken);

    ICadLaunchedInstance Launch(int port, string? accessToken);
}

internal interface ICadLaunchedInstance : IDisposable
{
    int ProcessId { get; }
    bool HasExited { get; }
    int ExitCode { get; }
}

internal sealed partial class CadMcpInstanceLauncher(string executablePath) : ICadMcpInstanceLauncher
{
    private static readonly SemaphoreSlim LocalLaunchLock = new(1, 1);

    public string ExecutablePath { get; } = executablePath;

    public bool IsInstanceRunning()
    {
        var current = Environment.ProcessId;
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExecutablePath));
        try
        {
            // Other stdio bridges run the same executable without a window; only a desktop instance counts.
            return processes.Any(process =>
            {
                try
                {
                    return process.Id != current && !process.HasExited &&
                           (!OperatingSystem.IsWindows() || process.MainWindowHandle != 0);
                }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
                {
                    return false;
                }
            });
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    public async Task<IDisposable> AcquireLaunchLockAsync(int port, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            await LocalLaunchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Releaser(() => LocalLaunchLock.Release());
        }

        // A semaphore, unlike a mutex, may be released from whichever thread the await resumes on.
        var semaphore = new Semaphore(1, 1, $@"Local\Direct2dCad.Mcp.Launch.{port}");
        try
        {
            await Task.Run(() =>
            {
                while (!semaphore.WaitOne(TimeSpan.FromMilliseconds(200)))
                    cancellationToken.ThrowIfCancellationRequested();
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            semaphore.Dispose();
            throw;
        }
        return new Releaser(() =>
        {
            semaphore.Release();
            semaphore.Dispose();
        });
    }

    public ICadLaunchedInstance Launch(int port, string? accessToken)
    {
        // The token travels in the inherited environment, never on the visible command line.
        if (accessToken is not null)
            Environment.SetEnvironmentVariable(CadMcpServer.AccessTokenEnvironmentVariable, accessToken);
        var arguments = $"--mcp-port {port}";
        return OperatingSystem.IsWindows() ? LaunchDetached(arguments) : LaunchPortable(arguments);
    }

    private ICadLaunchedInstance LaunchPortable(string arguments)
    {
        var start = new ProcessStartInfo(ExecutablePath, arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(ExecutablePath) ?? Environment.CurrentDirectory,
            // Redirecting keeps the child off the bridge's protocol pipes.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {ExecutablePath}.");
        process.StandardInput.Close();
        process.OutputDataReceived += static (_, _) => { };
        process.ErrorDataReceived += static (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new LaunchedProcess(process);
    }

    /// <summary>
    /// Starts the desktop app outside the client's job object. MCP clients built on Node/libuv put
    /// their children in a kill-on-close job; without breaking away, quitting the client would also
    /// close the drawing the user is working in. No handles are inherited, so the app never holds
    /// the bridge's stdio pipes open.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private unsafe ICadLaunchedInstance LaunchDetached(string arguments)
    {
        const uint CreateNewProcessGroup = 0x00000200;
        const uint CreateUnicodeEnvironment = 0x00000400;
        const uint CreateBreakawayFromJob = 0x01000000;
        const uint CreateDefaultErrorMode = 0x04000000;
        const int ErrorAccessDenied = 5;

        var commandLine = $"\"{ExecutablePath}\" {arguments}\0".ToCharArray();
        var startup = new StartupInfo { Size = sizeof(StartupInfo) };
        var flags = CreateNewProcessGroup | CreateUnicodeEnvironment | CreateDefaultErrorMode;
        ProcessInformation information;
        fixed (char* line = commandLine)
        {
            var started = CreateProcess(ExecutablePath, line, 0, 0, false, flags | CreateBreakawayFromJob, 0,
                Path.GetDirectoryName(ExecutablePath), startup, out information);
            // A job that forbids breakaway refuses the flag; the app then shares the client's lifetime.
            if (!started && Marshal.GetLastPInvokeError() == ErrorAccessDenied)
                started = CreateProcess(ExecutablePath, line, 0, 0, false, flags, 0,
                    Path.GetDirectoryName(ExecutablePath), startup, out information);
            if (!started)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not start {ExecutablePath}.");
        }
        try
        {
            // The open process handle keeps the id from being reused until Process has its own handle.
            return new LaunchedProcess(Process.GetProcessById(information.ProcessId));
        }
        finally
        {
            CloseHandle(information.Thread);
            CloseHandle(information.Process);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public nint Reserved2;
        public nint StandardInput;
        public nint StandardOutput;
        public nint StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CreateProcess(
        string applicationName,
        char* commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        nint environment,
        string? currentDirectory,
        in StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    private sealed class LaunchedProcess(Process process) : ICadLaunchedInstance
    {
        public int ProcessId => process.Id;
        public bool HasExited => process.HasExited;
        public int ExitCode => process.ExitCode;
        public void Dispose() => process.Dispose();
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                release();
        }
    }
}
