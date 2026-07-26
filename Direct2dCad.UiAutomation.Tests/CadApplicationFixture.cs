using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Application = FlaUI.Core.Application;

namespace Direct2dCad.UiAutomation.Tests;

public sealed class CadApplicationFixture : IDisposable
{
    private readonly string _settingsDirectory;

    public Application Application { get; }
    public UIA3Automation Automation { get; }
    public Window MainWindow { get; }
    public string SettingsDirectory => _settingsDirectory;

    public CadApplicationFixture()
    {
        var executablePath = FindApplicationExecutable();
        _settingsDirectory = Path.Combine(
            Path.GetTempPath(),
            "Direct2dCad.UiAutomation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_settingsDirectory);

        var startInfo = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = false
        };
        startInfo.Environment["DIRECT2DCAD_SETTINGS_DIRECTORY"] = _settingsDirectory;

        Application = Application.Launch(startInfo);
        Automation = new UIA3Automation();
        MainWindow = Application.GetMainWindow(
                         Automation,
                         TimeSpan.FromSeconds(30)) ??
                     throw new InvalidOperationException(
                         "Direct2dCad did not create its main window within 30 seconds.");
        MainWindow.Focus();
    }

    public AutomationElement WaitForElement(string automationId, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(15);
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < effectiveTimeout)
        {
            EnsureApplicationIsRunning();
            var element = MainWindow.FindFirstDescendant(
                condition => condition.ByAutomationId(automationId));
            if (element is not null)
                return element;

            Thread.Sleep(50);
        }

        throw new TimeoutException(
            $"Automation element '{automationId}' was not found within {effectiveTimeout}.");
    }

    public Window WaitForWindow(string automationId, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(15);
        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<Window> windows = [];
        while (stopwatch.Elapsed < effectiveTimeout)
        {
            EnsureApplicationIsRunning();
            windows = Application.GetAllTopLevelWindows(Automation);
            var window = windows.FirstOrDefault(
                             candidate => candidate.AutomationId == automationId) ??
                         FindDesktopWindow(automationId);
            if (window is not null)
                return window;

            Thread.Sleep(50);
        }

        var discoveredWindows = string.Join(
            ", ",
            windows.Select(window =>
                $"'{window.Name}' ({window.AutomationId})"));
        throw new TimeoutException(
            $"Top-level window '{automationId}' was not found within {effectiveTimeout}. " +
            $"Discovered windows: {discoveredWindows}.");
    }

    public bool IsWindowOpen(string automationId) =>
        Application.GetAllTopLevelWindows(Automation).Any(
            window => window.AutomationId == automationId) ||
        FindDesktopWindow(automationId) is not null;

    public void WaitUntil(Func<bool> condition, string failureMessage, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(10);
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < effectiveTimeout)
        {
            EnsureApplicationIsRunning();
            if (condition())
                return;

            Thread.Sleep(50);
        }

        throw new TimeoutException(failureMessage);
    }

    public void EnsureApplicationIsRunning()
    {
        if (Application.HasExited)
            throw new InvalidOperationException("Direct2dCad exited unexpectedly.");
    }

    private Window? FindDesktopWindow(string automationId)
    {
        var element = Automation.GetDesktop().FindFirstDescendant(
            condition => condition.ByAutomationId(automationId).And(
                condition.ByProcessId(Application.ProcessId)));
        return element?.AsWindow();
    }

    public void Dispose()
    {
        try
        {
            if (!Application.HasExited)
                Application.Kill();
        }
        finally
        {
            Automation.Dispose();
            Application.Dispose();
            try
            {
                Directory.Delete(_settingsDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string FindApplicationExecutable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Direct2dCad.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
            throw new FileNotFoundException("Could not locate the Direct2dCad repository root.");

#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var executablePath = Path.Combine(
            directory.FullName,
            "Direct2dCad.wpf",
            "bin",
            configuration,
            "net10.0-windows",
            "win-x64",
            "Direct2dCad.exe");
        return File.Exists(executablePath)
            ? executablePath
            : throw new FileNotFoundException(
                "Direct2dCad must be built before running UI automation tests.",
                executablePath);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CadApplicationCollection
{
    public const string Name = "Direct2dCad UI automation";
}
