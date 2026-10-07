using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using System.Runtime.InteropServices;
using TextBox = FlaUI.Core.AutomationElements.TextBox;

namespace Direct2dCad.UiAutomation.Tests;

public sealed partial class MainWindowUiTests
{
    [Fact]
    [Trait("Category", "UiAutomation")]
    public void ShortcutNewFromTerminalCreatesExactlyOneDocumentAndPreservesDraft()
    {
        CreateNewDocument();
        var documents = GetShortcutDocumentList();
        var input = GetOrOpenCommandLineInput();
        int DocumentCount() => documents.FindAllDescendants(c => c.ByAutomationId("DocumentExplorerItem")).Length;
        var before = DocumentCount();
        Assert.True(before > 0);
        FocusShortcutDraft(input);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_N);
        fixture.WaitUntil(() => DocumentCount() == before + 1, "Ctrl+N did not create exactly one document.");
        Assert.Equal("unsubmitted shortcut draft", input.Text);
        CaptureScreenshot("shortcut-new-from-terminal.png");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "UiAutomation")]
    public void ShortcutFileDialogsFromTerminalPreserveDraftAndModalScope(bool saveAs)
    {
        CreateNewDocument();
        var documents = GetShortcutDocumentList();
        var input = GetOrOpenCommandLineInput();
        fixture.WaitForElement("DrawRibbonTab").AsTabItem().Select();
        ClickVisibleElement(fixture.WaitForElement("LineToolButton"));
        FocusShortcutDraft(input);
        if (saveAs)
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_S);
        else
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_O);
        Window? Dialog() => fixture.Automation.GetDesktop().FindFirstDescendant(c => c.ByClassName("#32770")
            .And(c.ByProcessId(fixture.Application.ProcessId)))?.AsWindow();
        try { fixture.WaitUntil(() => Dialog() is not null, "The file shortcut did not open its dialog."); }
        catch (TimeoutException)
        {
            CaptureScreenshot($"shortcut-file-dialog-missing-{saveAs}.png");
            throw;
        }
        var dialog = Dialog()!;
        Assert.Contains(saveAs ? "Save" : "Open", dialog.Name);
        dialog.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_N);
        Assert.NotNull(Dialog());
        Keyboard.Type(VirtualKeyShort.ESC);
        fixture.WaitUntil(() => Dialog() is null, "The file dialog did not close.");
        Assert.Equal("Line", fixture.WaitForElement("CurrentToolStatusText").Name);
        Assert.Equal("unsubmitted shortcut draft", input.Text);
        Assert.Single(documents.FindAllDescendants(c => c.ByAutomationId("DocumentExplorerItem")));
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void ShortcutTerminalToggleAliasesAndTabNavigationDoNotTrapDraft()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        using var keyboardLayout = new ShortcutKeyboardLayout(fixture.MainWindow);
        FocusShortcutDraft(input);
        Keyboard.Type(VirtualKeyShort.TAB);
        fixture.WaitUntil(() => !input.Properties.HasKeyboardFocus.ValueOrDefault,
            "Tab without suggestions trapped terminal focus.");
        input.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.TAB);
        fixture.WaitUntil(() => !input.Properties.HasKeyboardFocus.ValueOrDefault,
            "Shift+Tab trapped terminal focus.");
        foreach (var key in new[] { VirtualKeyShort.KEY_J, VirtualKeyShort.OEM_3 })
        {
            fixture.MainWindow.Focus();
            input.Focus();
            TypeTerminalToggle(key);
            fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("CommandLineInput"))
                is null or { IsOffscreen: true }, $"The terminal toggle {key} did not hide the toolbox exactly once.");
            TypeTerminalToggle(key);
            input = fixture.WaitForElement("CommandLineInput").AsTextBox();
            fixture.WaitUntil(() => !input.IsOffscreen, "The terminal toggle did not show the toolbox.");
            Assert.Equal("unsubmitted shortcut draft", input.Text);
        }
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void ShortcutCanvasRedoAndTextEditingKeepSeparateHistories()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL add_circle {\"center_x\":0,\"center_y\":0,\"radius\":10}", "created_entity_id");
        var canvas = fixture.WaitForElement("CadCanvas");
        canvas.Focus();
        fixture.WaitUntil(() => canvas.Properties.HasKeyboardFocus.ValueOrDefault, "Canvas did not receive focus.");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
        canvas.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_Z);
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        FocusShortcutDraft(input);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(VirtualKeyShort.DELETE);
        fixture.WaitUntil(() => input.Text.Length == 0, "Ctrl+A/Delete did not edit terminal text.");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        fixture.WaitUntil(() => input.Text == "unsubmitted shortcut draft", "Ctrl+Z did not restore terminal text.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void ShortcutControlTabNavigatesDocumentsFromCanvasAndTerminal()
    {
        CreateNewDocument();
        fixture.WaitForElement("CadCanvas");
        var input = GetOrOpenCommandLineInput();
        SelectDocumentTab("Welcome");
        fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByName("Untitled")) is { IsOffscreen: false },
            "The drawing tab was not ready after switching to Welcome.");
        SelectDocumentTab("Untitled");
        FocusShortcutDraft(input);
        NavigateShortcutDocument();
        fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("CadCanvas"))
            is null or { IsOffscreen: true }, "Ctrl+Tab did not navigate to Welcome from the terminal.");
        Assert.Equal("unsubmitted shortcut draft", input.Text);
        SelectDocumentTab("Untitled");
        var canvas = fixture.WaitForElement("CadCanvas"); canvas.Focus();
        NavigateShortcutDocument();
        fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("CadCanvas"))
            is null or { IsOffscreen: true }, "Ctrl+Tab was intercepted by the canvas.");
    }

    private void FocusShortcutDraft(TextBox input)
    {
        input.Text = "unsubmitted shortcut draft";
        fixture.MainWindow.SetForeground(); fixture.MainWindow.Focus(); input.Focus();
        fixture.WaitUntil(() => input.Properties.HasKeyboardFocus.ValueOrDefault &&
            GetShortcutForegroundWindow() == fixture.MainWindow.Properties.NativeWindowHandle.Value,
            "Terminal draft did not receive foreground keyboard focus.");
    }

    private static void TypeTerminalToggle(VirtualKeyShort key)
    {
        using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
        {
            // OEM scan-code mappings depend on the desktop keyboard layout.
            // Deliver the legacy virtual key itself to verify the WPF alias.
            if (key == VirtualKeyShort.OEM_3) Keyboard.TypeVirtualKeyCode((ushort)key);
            else Keyboard.Type(key);
        }
    }

    private void NavigateShortcutDocument()
    {
        using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
        {
            Keyboard.Type(VirtualKeyShort.TAB);
            var list = fixture.WaitForElement("PART_DocumentListBox", includePopups: true);
            fixture.WaitUntil(() => list.FindAllDescendants().Any(e => e.Properties.HasKeyboardFocus.ValueOrDefault),
                "The document navigator did not focus its selected document before Control was released.");
        }
    }

    private sealed class ShortcutKeyboardLayout : IDisposable
    {
        private readonly IntPtr _window;
        private readonly IntPtr _original;
        public ShortcutKeyboardLayout(Window window)
        {
            _window = window.Properties.NativeWindowHandle.Value;
            _original = GetKeyboardLayout(GetWindowThreadProcessId(_window, out _));
            var us = LoadKeyboardLayout("00000409", 0);
            Assert.NotEqual(IntPtr.Zero, us);
            SendMessage(_window, 0x50, IntPtr.Zero, us);
        }
        public void Dispose() => SendMessage(_window, 0x50, IntPtr.Zero, _original);
        [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint threadId);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadKeyboardLayout(string layout, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void ShortcutAiShiftEnterAndGlobalNewPreservePrompt()
    {
        CreateNewDocument();
        var documents = GetShortcutDocumentList();
        fixture.MainWindow.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_A);
        var prompt = fixture.WaitForElement("AiPromptInput").AsTextBox();
        fixture.WaitUntil(() => !prompt.IsOffscreen, "AI prompt did not become visible.");
        prompt.Text = "Prompt draft"; prompt.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.END);
        Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.RETURN);
        fixture.WaitUntil(() => prompt.Text.Contains('\n'), "Shift+Enter did not insert an AI prompt newline.");
        var draft = prompt.Text;
        fixture.MainWindow.SetForeground();
        prompt.Focus();
        fixture.WaitUntil(() => prompt.Properties.HasKeyboardFocus.ValueOrDefault &&
            GetShortcutForegroundWindow() == fixture.MainWindow.Properties.NativeWindowHandle.Value,
            "AI prompt did not receive foreground keyboard focus before Ctrl+N.");
        Assert.Single(documents.FindAllDescendants(c => c.ByAutomationId("DocumentExplorerItem")));
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_N);
        try
        {
            fixture.WaitUntil(() => documents.FindAllDescendants(c => c.ByAutomationId("DocumentExplorerItem")).Length == 2,
                "Ctrl+N from AI input did not create exactly one document.");
        }
        catch (TimeoutException)
        {
            CaptureScreenshot("shortcut-ai-new-missing.png");
            throw;
        }
        Assert.Equal(draft, prompt.Text);
        CaptureScreenshot("shortcut-ai-new-and-newline.png");
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void ShortcutSaveRoutesFromAnActualFloatingDocumentWindow()
    {
        CreateNewDocument();
        fixture.WaitForElement("CadCanvas");
        var tab = fixture.MainWindow.FindAllDescendants(c => c.ByControlType(ControlType.TabItem))
            .Single(e => e.FindFirstDescendant(c => c.ByName("Untitled")) is not null);
        ClickVisibleElement(tab, MouseButton.Right);
        AutomationElement? floatItem = null;
        fixture.WaitUntil(() => (floatItem = fixture.Automation.GetDesktop().FindFirstDescendant(c => c.ByName("Float")
            .And(c.ByControlType(ControlType.MenuItem)).And(c.ByProcessId(fixture.Application.ProcessId)))) is not null,
            "The document context menu did not expose Float.");
        floatItem!.AsMenuItem().Invoke();
        fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("CadCanvas")) is null,
            "The document did not leave the main window after Float.");
        AutomationElement? floating = null;
        fixture.WaitUntil(() => (floating = FindShortcutFloatingWindow()) is not null,
            "The document did not create a visible native floating window.");
        floating!.Focus();
        var bounds = floating.BoundingRectangle;
        Mouse.Click(new System.Drawing.Point((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2)));
        var handle = floating.Properties.NativeWindowHandle.Value;
        fixture.WaitUntil(() =>
        {
            var info = new ShortcutGuiThreadInfo { Size = (uint)Marshal.SizeOf<ShortcutGuiThreadInfo>() };
            return GetShortcutForegroundWindow() == handle &&
                GetShortcutGuiThreadInfo(GetShortcutWindowThreadProcessId(handle, out _), ref info) && IsShortcutChild(handle, info.Focus);
        }, "The floating document's child content host did not receive native keyboard focus.");
        if (Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            floating.CaptureToFile(Path.Combine(directory, "shortcut-floating-document.png"));
        }
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        AutomationElement? Dialog() => fixture.Automation.GetDesktop().FindFirstDescendant(c => c.ByClassName("#32770")
            .And(c.ByProcessId(fixture.Application.ProcessId)));
        fixture.WaitUntil(() => Dialog() is not null, "Ctrl+S from the floating document did not open Save.");
        Assert.Contains("Save", Dialog()!.Name);
        Dialog()!.Focus(); Keyboard.Type(VirtualKeyShort.ESC);
        fixture.WaitUntil(() => Dialog() is null, "The floating document Save dialog did not close.");

        // Escape from a main-window panel must find and focus the active canvas
        // even when that canvas now lives in a different native window.
        fixture.MainWindow.Focus();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "LINE", "Line mode active.");
        FocusShortcutDraft(input);
        Keyboard.Type(VirtualKeyShort.ESC);
        fixture.WaitUntil(() => fixture.WaitForElement("CurrentToolStatusText").Name == "Select", "Escape did not cancel the floating drawing.");
        fixture.WaitUntil(() => GetShortcutForegroundWindow() == handle, "Escape did not return focus to the floating document.");
        fixture.WaitUntil(() =>
        {
            var info = new ShortcutGuiThreadInfo { Size = (uint)Marshal.SizeOf<ShortcutGuiThreadInfo>() };
            return GetShortcutGuiThreadInfo(GetShortcutWindowThreadProcessId(handle, out _), ref info) && IsShortcutChild(handle, info.Focus);
        }, "Escape did not focus the floating document's child content host.");
        Assert.Equal("unsubmitted shortcut draft", input.Text);
    }

    private AutomationElement? FindShortcutFloatingWindow()
    {
        var handles = new List<IntPtr>();
        EnumWindows((window, _) =>
        {
            GetShortcutWindowThreadProcessId(window, out var process);
            if (process == fixture.Application.ProcessId && window != fixture.MainWindow.Properties.NativeWindowHandle.Value)
            {
                handles.Add(window);
            }
            return true;
        }, IntPtr.Zero);
        foreach (var handle in handles)
        {
            var root = fixture.Automation.FromHandle(handle);
            if (root.Properties.ClassName.ValueOrDefault == "Window" && IsShortcutWindowVisible(handle) &&
                root.BoundingRectangle.Width > 400) return root;
        }
        return null;
    }

    private delegate bool ShortcutEnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(ShortcutEnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint GetShortcutWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern IntPtr GetShortcutForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")] private static extern bool IsShortcutWindowVisible(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "IsChild")] private static extern bool IsShortcutChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll", EntryPoint = "GetGUIThreadInfo")]
    private static extern bool GetShortcutGuiThreadInfo(uint thread, ref ShortcutGuiThreadInfo info);
    [StructLayout(LayoutKind.Sequential)]
    private struct ShortcutGuiThreadInfo
    {
        public uint Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }

    private AutomationElement GetShortcutDocumentList()
    {
        var list = fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("DocumentExplorerList"));
        if (list is null || list.IsOffscreen)
        {
            fixture.MainWindow.Focus();
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_E);
            list = fixture.WaitForElement("DocumentExplorerList");
        }
        fixture.WaitUntil(() => !list.IsOffscreen, "The document explorer did not become visible.");
        return list;
    }
}
