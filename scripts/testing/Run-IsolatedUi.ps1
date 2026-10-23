param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [Parameter(Mandatory)] [string] $ResultsDirectory,
    [string] $Filter,
    [ValidateRange(30, 7200)] [int] $TimeoutSeconds = 3600
)

# Run already-built UI tests on a separate input desktop. This temporarily
# switches away from the current desktop and restores it even if tests fail.
# Hotkeys registered by other desktop apps do not intercept test input there.
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$project = Join-Path $repositoryRoot 'Direct2dCad.UiAutomation.Tests/Direct2dCad.UiAutomation.Tests.csproj'
function Quote-PsLiteral([string] $value) { return "'" + $value.Replace("'", "''") + "'" }
$childCommand = @"
Set-Location $(Quote-PsLiteral $repositoryRoot)
`$env:DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY = $(Quote-PsLiteral (Join-Path $ResultsDirectory 'screenshots'))
dotnet test $(Quote-PsLiteral $project) -c $Configuration --no-build --nologo -v minimal --logger 'trx;LogFileName=ui.trx' --results-directory $(Quote-PsLiteral $ResultsDirectory) $(if ($Filter) { '--filter ' + (Quote-PsLiteral $Filter) }) *> $(Quote-PsLiteral (Join-Path $ResultsDirectory 'run.log'))
exit `$LASTEXITCODE
"@
$encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childCommand))

if (-not ('Direct2dCadIsolatedDesktop' -as [type])) {
    Add-Type @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class Direct2dCadIsolatedDesktop
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo
    {
        public int Size;
        public string Reserved, Desktop, Title;
        public int X, Y, Width, Height, XChars, YChars, Fill, Flags;
        public short ShowWindow, ReservedSize;
        public IntPtr ReservedPointer, StdInput, StdOutput, StdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public uint ProcessId, ThreadId;
    }
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr attributes);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SwitchDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string application, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string directory,
        ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr handle, out uint code);

    public static int Run(string executable, string arguments, string directory, int timeoutSeconds)
    {
        var original = OpenInputDesktop(0, false, 0x100);
        if (original == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr desktop = IntPtr.Zero;
        IntPtr child = IntPtr.Zero;
        int childId = 0;
        try
        {
            var name = "Direct2dCad-Tests-" + Guid.NewGuid().ToString("N");
            desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x1ff, IntPtr.Zero);
            if (desktop == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = "winsta0\\" + name,
                Flags = 1, ShowWindow = 0 };
            var command = new StringBuilder("\"" + executable + "\" " + arguments);
            if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, false, 0x08000000,
                IntPtr.Zero, directory, ref startup, out var created))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            child = created.Process;
            childId = (int)created.ProcessId;
            CloseHandle(created.Thread);
            if (!SwitchDesktop(desktop)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var wait = WaitForSingleObject(child, checked((uint)timeoutSeconds * 1000));
            if (wait == 258)
                throw new TimeoutException("The isolated UI test run exceeded its deadline.");
            if (wait != 0 || !GetExitCodeProcess(child, out var exitCode))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return (int)exitCode;
        }
        finally
        {
            try
            {
                if (child != IntPtr.Zero && GetExitCodeProcess(child, out var code) && code == 259)
                {
                    using (var process = Process.GetProcessById(childId))
                    {
                        process.Kill(true);
                        process.WaitForExit(10000);
                    }
                }
            }
            finally
            {
                var restored = SwitchDesktop(original);
                var restoreError = Marshal.GetLastWin32Error();
                if (child != IntPtr.Zero) CloseHandle(child);
                if (desktop != IntPtr.Zero) CloseDesktop(desktop);
                CloseDesktop(original);
                if (!restored) throw new Win32Exception(restoreError, "Could not restore the original input desktop.");
            }
        }
    }
}
'@
}

$executable = (Get-Process -Id $PID).Path
$exitCode = [Direct2dCadIsolatedDesktop]::Run($executable,
    "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand", $repositoryRoot, $TimeoutSeconds)
Get-Content (Join-Path $ResultsDirectory 'run.log') -Tail 60
if ($exitCode -ne 0) { throw "Isolated UI tests failed with exit code $exitCode. Results: $ResultsDirectory" }
