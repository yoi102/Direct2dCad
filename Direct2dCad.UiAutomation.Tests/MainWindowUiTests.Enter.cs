using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using TextBox = FlaUI.Core.AutomationElements.TextBox;

namespace Direct2dCad.UiAutomation.Tests;

public sealed partial class MainWindowUiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "UiAutomation")]
    public void ShortcutEnterCompletesPendingPolylineInMainAndFloatingCanvas(bool floating)
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        PrepareEnterPolyline(input, output);
        var floatingHandle = floating ? FocusEnterFloatingCanvas() : IntPtr.Zero;
        if (!floating)
            FocusEnterMainControl(fixture.WaitForElement("CadCanvas"));

        Keyboard.Type(VirtualKeyShort.RETURN);
        // SendInput queues the key. Keep focus in this HWND until the resulting
        // drawing state is observable, before focusing Terminal for inspection.
        try
        {
            fixture.WaitUntil(() => fixture.WaitForElement("CurrentDrawingStepText").Name.Contains("Specify the first point"),
                "Canvas Enter did not finish the pending polyline before focus moved to Terminal.");
        }
        catch (TimeoutException)
        {
            SaveEnterConfirmationFailure(floatingHandle);
            throw;
        }
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL list_entities {\"type\":\"Polyline\"}", "Polyline");
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
        CaptureScreenshot($"enter-{(floating ? "floating" : "main")}-canvas.png");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "UiAutomation")]
    public void ShortcutEnterNativeRepeatedKeyDownDoesNotConfirmTheNextDrawing(bool floating)
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        PrepareEnterPolyline(input, output);
        var floatingHandle = floating ? FocusEnterFloatingCanvas() : IntPtr.Zero;
        if (!floating)
            FocusEnterMainControl(fixture.WaitForElement("CadCanvas"));

        // SendInput repeats the native keydown without a keyup in between.
        // This is a deterministic synthetic repeat sequence, not a physical hold.
        try
        {
            Keyboard.Press(VirtualKeyShort.RETURN);
            fixture.WaitUntil(() => fixture.WaitForElement("CurrentDrawingStepText").Name.Contains("Specify the first point"),
                "The first Enter keydown did not complete the pending polyline.");
            Keyboard.Press(VirtualKeyShort.RETURN);
            Keyboard.Press(VirtualKeyShort.RETURN);
        }
        finally
        {
            Keyboard.Release(VirtualKeyShort.RETURN);
        }

        // A visible keyboard action queued after those keydowns is the barrier:
        // inspecting an unchanged prompt immediately could precede dispatch.
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_J);
        fixture.WaitUntil(() => fixture.MainWindow.FindFirstDescendant(c => c.ByAutomationId("CommandLineInput"))
            is null or { IsOffscreen: true }, "The post-repeat terminal toggle did not finish processing.");
        fixture.WaitUntil(() => fixture.WaitForElement("CurrentDrawingStepText").Name.Contains("Specify the first point"),
            "Repeated Enter confirmed the next empty drawing instead of preserving the first-point prompt.");
        Assert.DoesNotContain("more point", fixture.WaitForElement("CurrentDrawingStepText").Name);
        CaptureScreenshot($"enter-native-repeat-{(floating ? "floating" : "main")}.png");

        input = GetOrOpenCommandLineInput();
        output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "UiAutomation")]
    public void ShortcutEnterFromPassivePanelCompletesDrawingAndFocusesItsCanvas(bool floating)
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        PrepareEnterPolyline(input, output);
        var floatingHandle = floating ? FocusEnterFloatingCanvas() : IntPtr.Zero;
        EnsureToolboxElement("EntityPropertiesToolbox", "toolbox.entity-properties");
        var scroll = fixture.WaitForElement("EntityPropertiesScrollViewer");
        FocusEnterMainControl(scroll);
        Keyboard.Type(VirtualKeyShort.RETURN);
        if (floating)
            fixture.WaitUntil(() => HasEnterFloatingCanvasFocus(floatingHandle),
                "Passive-panel Enter did not return focus to the floating canvas's native content host.");
        else
            fixture.WaitUntil(() => fixture.WaitForElement("CadCanvas").Properties.HasKeyboardFocus.ValueOrDefault,
                "Passive-panel Enter did not return focus to the canvas.");
        CaptureScreenshot($"enter-passive-panel-{(floating ? "floating" : "main")}.png");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        ExecuteCommandAndWaitForOutput(input, output, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void ShortcutEnterValidatesDynamicInputAndCommitsOnlyTheCurrentPoint()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "PL", "Polyline mode active.");
        ShowDynamicInputOnCanvas();
        var x = fixture.WaitForElement("DynamicInputX").AsTextBox();
        FocusEnterMainControl(x);
        x.Text = "invalid coordinate";
        Keyboard.Type(VirtualKeyShort.RETURN);
        fixture.WaitUntil(() => !string.IsNullOrWhiteSpace(fixture.WaitForElement("DynamicInputError").Name),
            "Invalid coordinate Enter did not show a dynamic-input error.");
        Assert.Equal("invalid coordinate", x.Text);
        Assert.True(x.Properties.HasKeyboardFocus.ValueOrDefault);
        CaptureScreenshot("enter-dynamic-input-invalid.png");

        EnterDynamicCoordinates("0", "0");
        fixture.WaitUntil(() => fixture.WaitForElement("CadCanvas").Properties.HasKeyboardFocus.ValueOrDefault,
            "Corrected coordinate Enter did not return focus to the canvas.");
        Keyboard.Type(VirtualKeyShort.RETURN);
        fixture.WaitUntil(() => fixture.WaitForElement("CurrentDrawingStepText").Name.Contains("Specify 1 more point"),
            "Dynamic-input Enter did not commit exactly one pending polyline point.");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
        ExecuteCommandAndWaitForOutput(input, output, "20,0", "Point accepted:");
        FocusEnterMainControl(fixture.WaitForElement("CadCanvas"));
        Keyboard.Type(VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        CaptureScreenshot("enter-dynamic-input-one-point.png");
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void ShortcutEnterCommitsSingleLinePropertyAndReturnsToCanvas()
    {
        PrepareEscapePropertySelection();
        var field = ScrollPropertyIntoView("EntityNameInput").AsTextBox();
        FocusEnterMainControl(field);
        field.Text = "Name confirmed with Enter";
        Assert.True(field.Properties.HasKeyboardFocus.ValueOrDefault);
        Keyboard.Type(VirtualKeyShort.RETURN);
        fixture.WaitUntil(() => fixture.WaitForElement("CadCanvas").Properties.HasKeyboardFocus.ValueOrDefault,
            "Property Enter did not return focus to the canvas.");
        Assert.Equal("Name confirmed with Enter", field.Text);
        Assert.False(field.IsOffscreen);

        // Recreate the selection view to verify the binding source was committed.
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL clear_selection {}", "selected_entity_ids");
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 1 entities.");
        Assert.Equal("Name confirmed with Enter", ScrollPropertyIntoView("EntityNameInput").AsTextBox().Text);
        CaptureScreenshot("enter-property-committed.png");
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void ShortcutEnterKeepsInvalidPropertyFocusedAndAllowsCorrection()
    {
        PrepareEscapePropertySelection();
        var field = ScrollPropertyIntoView("EntityRotationInput").AsTextBox();
        FocusEnterMainControl(field);
        field.Text = "invalid angle";
        Assert.True(field.Properties.HasKeyboardFocus.ValueOrDefault);
        Keyboard.Type(VirtualKeyShort.RETURN);
        Assert.Equal("invalid angle", field.Text);
        Assert.True(field.Properties.HasKeyboardFocus.ValueOrDefault);
        Assert.False(fixture.WaitForElement("EntityNameInput").IsOffscreen);
        CaptureScreenshot("enter-property-invalid.png");

        field.Text = "30";
        Keyboard.Type(VirtualKeyShort.RETURN);
        fixture.WaitUntil(() => fixture.WaitForElement("CadCanvas").Properties.HasKeyboardFocus.ValueOrDefault,
            "Corrected property Enter did not return focus to the canvas.");
        Assert.Equal(30, double.Parse(field.Text, System.Globalization.CultureInfo.InvariantCulture), precision: 8);
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(input, output, "TOOL clear_selection {}", "selected_entity_ids");
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 1 entities.");
        Assert.Equal(30, double.Parse(ScrollPropertyIntoView("EntityRotationInput").AsTextBox().Text,
            System.Globalization.CultureInfo.InvariantCulture), precision: 8);
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void ShortcutEnterInDrawingPropertyDoesNotCompletePendingPolyline()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        PrepareEnterPolyline(input, output);
        var properties = EnsureToolboxElement("EntityPropertiesToolbox", "toolbox.entity-properties");
        var scroll = fixture.WaitForElement("EntityPropertiesScrollViewer");
        if (scroll.Patterns.Scroll.Pattern.VerticallyScrollable.Value)
            scroll.Patterns.Scroll.Pattern.SetScrollPercent(-1, 0);
        // DrawingLayerPropertySection's first edit is the name of new entities.
        // It is distinct from the selected-entity name field tested above.
        var field = properties.FindAllDescendants(c => c.ByControlType(ControlType.Edit))
            .First(e => !e.IsOffscreen && e.IsEnabled).AsTextBox();
        FocusEnterMainControl(field);
        field.Text = "Pending Enter polyline";
        Assert.True(field.Properties.HasKeyboardFocus.ValueOrDefault);
        Keyboard.Type(VirtualKeyShort.RETURN);
        fixture.WaitUntil(() => fixture.WaitForElement("CadCanvas").Properties.HasKeyboardFocus.ValueOrDefault,
            "Drawing property Enter did not return focus to the canvas.");
        Assert.Equal("Pending Enter polyline", field.Text);
        Assert.Contains("Polyline", fixture.WaitForElement("CurrentToolStatusText").Name);
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");

        FocusEnterMainControl(fixture.WaitForElement("CadCanvas"));
        Keyboard.Type(VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        ExecuteCommandAndWaitForOutput(input, output, "CANCEL", "Select mode active.");
        ExecuteCommandAndWaitForOutput(input, output, "SELECTALL", "Selected 1 entities.");
        Assert.Equal("Pending Enter polyline", ScrollPropertyIntoView("EntityNameInput").AsTextBox().Text);
        CaptureScreenshot("enter-drawing-property-preserves-points.png");
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void ShortcutEnterAcceptsTerminalCompletionWithoutSubmittingOrFinishingDrawing()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        PrepareEnterPolyline(input, output);
        input.Text = "CIR";
        FocusEnterMainControl(input);
        Keyboard.Type(VirtualKeyShort.DOWN);
        Keyboard.Type(VirtualKeyShort.RETURN);
        fixture.WaitUntil(() => input.Text == "CIRCLE ", "Enter did not accept the selected terminal completion.");
        Assert.True(input.Properties.HasKeyboardFocus.ValueOrDefault);
        Assert.Contains("Polyline", fixture.WaitForElement("CurrentToolStatusText").Name);
        CaptureScreenshot("enter-terminal-completion.png");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
        FocusEnterMainControl(fixture.WaitForElement("CadCanvas"));
        Keyboard.Type(VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void ShortcutEnterDisabledAiSendCannotFinishDrawingAndShiftEnterKeepsNewline()
    {
        CreateNewDocument();
        var input = GetOrOpenCommandLineInput();
        var output = fixture.WaitForElement("CommandLineOutput");
        PrepareEnterPolyline(input, output);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_A);
        var prompt = fixture.WaitForElement("AiPromptInput").AsTextBox();
        fixture.WaitUntil(() => !prompt.IsOffscreen, "The AI prompt did not become visible.");

        // Empty/whitespace prompts cannot send with either provider. The test
        // therefore exercises real keys without issuing a model request.
        prompt.Text = "";
        FocusEnterMainControl(prompt);
        Keyboard.Type(VirtualKeyShort.RETURN);
        Assert.Equal("", prompt.Text);
        Assert.True(prompt.Properties.HasKeyboardFocus.ValueOrDefault);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.RETURN);
        Assert.Equal("", prompt.Text);
        Assert.True(prompt.Properties.HasKeyboardFocus.ValueOrDefault);
        Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.RETURN);
        fixture.WaitUntil(() => prompt.Text.Contains('\n'), "Shift+Enter did not insert an AI prompt newline.");
        var whitespaceDraft = prompt.Text;
        Keyboard.Type(VirtualKeyShort.RETURN);
        Assert.Equal(whitespaceDraft, prompt.Text);
        Assert.True(prompt.Properties.HasKeyboardFocus.ValueOrDefault);
        Assert.Contains("Polyline", fixture.WaitForElement("CurrentToolStatusText").Name);
        CaptureScreenshot("enter-ai-disabled-send.png");
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 0");
        FocusEnterMainControl(fixture.WaitForElement("CadCanvas"));
        Keyboard.Type(VirtualKeyShort.RETURN);
        ExecuteCommandAndWaitForOutput(input, output, "STATUS", "Entities: 1");
        Assert.Equal(whitespaceDraft, prompt.Text);
    }

    private void PrepareEnterPolyline(TextBox input, AutomationElement output)
    {
        ExecuteCommandAndWaitForOutput(input, output, "PL", "Polyline mode active.");
        ExecuteCommandAndWaitForOutput(input, output, "0,0", "Point accepted:");
        ExecuteCommandAndWaitForOutput(input, output, "20,0", "Point accepted:");
        ExecuteCommandAndWaitForOutput(input, output, "20,20", "Point accepted:");
    }

    private void SaveEnterConfirmationFailure(IntPtr floatingHandle)
    {
        if (Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY") is not { Length: > 0 } directory)
            return;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "enter-confirmation-failure.txt");
        var diagnostics = new System.Text.StringBuilder();
        void Record(Action action)
        {
            try { action(); }
            catch (Exception exception) { diagnostics.AppendLine($"Diagnostic error: {exception}"); }
        }
        diagnostics.AppendLine($"Confirmation failure with original focus preserved: {DateTime.UtcNow:O}");
        Record(() => diagnostics.AppendLine($"Drawing step: {fixture.WaitForElement("CurrentDrawingStepText").Name}"));
        Record(() => diagnostics.AppendLine($"Tool: {fixture.WaitForElement("CurrentToolStatusText").Name}"));
        Record(() =>
        {
            var foreground = GetShortcutForegroundWindow();
            var info = new ShortcutGuiThreadInfo { Size = (uint)Marshal.SizeOf<ShortcutGuiThreadInfo>() };
            var gotInfo = GetShortcutGuiThreadInfo(GetShortcutWindowThreadProcessId(foreground, out _), ref info);
            diagnostics.AppendLine($"Native foreground={foreground}, floating={floatingHandle}, gotInfo={gotInfo}, " +
                $"active={info.Active}, focus={info.Focus}, capture={info.Capture}, flags={info.Flags}");
            var actual = fixture.Automation.FocusedElement();
            diagnostics.AppendLine($"UIA focus id={actual?.Properties.AutomationId.ValueOrDefault}, " +
                $"class={actual?.Properties.ClassName.ValueOrDefault}, name={actual?.Name}, bounds={actual?.BoundingRectangle}");
        });
        Record(() => CaptureScreenshot("enter-confirmation-failed-main.png"));
        if (floatingHandle != IntPtr.Zero)
            Record(() => fixture.Automation.FromHandle(floatingHandle)
                .CaptureToFile(Path.Combine(directory, "enter-confirmation-failed-floating.png")));
        Record(() => File.WriteAllText(path, diagnostics.ToString()));
    }

    private void FocusEnterMainControl(AutomationElement control)
    {
        fixture.MainWindow.SetForeground();
        fixture.MainWindow.Focus();
        control.Focus();
        fixture.WaitUntil(() => control.Properties.HasKeyboardFocus.ValueOrDefault &&
            GetShortcutForegroundWindow() == fixture.MainWindow.Properties.NativeWindowHandle.Value,
            "The Enter target did not receive foreground keyboard focus.");
    }

    private IntPtr FocusEnterFloatingCanvas()
    {
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
        var floatingWindow = floating!;
        floatingWindow.AsWindow().SetForeground();
        floatingWindow.Focus();
        var bounds = floatingWindow.BoundingRectangle;
        // The pending polyline's length field overlaps the window centre. Click
        // the upper-left canvas interior, away from its dimensions and tab bars,
        // to enter the child HWND and add a vertex without editing a numeric field.
        Mouse.Click(new System.Drawing.Point((int)(bounds.Left + bounds.Width * .2), (int)(bounds.Top + bounds.Height * .3)));
        var handle = floatingWindow.Properties.NativeWindowHandle.Value;
        try
        {
            fixture.WaitUntil(() => HasEnterFloatingCanvasFocus(handle),
                "The floating document's child content host did not receive native keyboard focus.");
        }
        catch (TimeoutException exception)
        {
            var actual = fixture.Automation.FocusedElement();
            CaptureScreenshot("enter-floating-focus-failed.png");
            throw new TimeoutException($"{exception.Message} Actual UIA focus: " +
                $"id={actual?.Properties.AutomationId.ValueOrDefault}, class={actual?.Properties.ClassName.ValueOrDefault}, " +
                $"name={actual?.Name}, bounds={actual?.BoundingRectangle}.", exception);
        }
        if (Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_SCREENSHOT_DIRECTORY") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            floatingWindow.CaptureToFile(Path.Combine(directory, "enter-floating-before-confirm.png"));
            var actual = fixture.Automation.FocusedElement();
            File.WriteAllText(Path.Combine(directory, "enter-floating-focus.txt"),
                $"Foreground={GetShortcutForegroundWindow()}, floating={handle}; " +
                $"UIA id={actual?.Properties.AutomationId.ValueOrDefault}, " +
                $"class={actual?.Properties.ClassName.ValueOrDefault}, bounds={actual?.BoundingRectangle}");
        }
        return handle;
    }

    private bool HasEnterFloatingCanvasFocus(IntPtr handle)
    {
        var info = new ShortcutGuiThreadInfo { Size = (uint)Marshal.SizeOf<ShortcutGuiThreadInfo>() };
        return GetShortcutForegroundWindow() == handle &&
            GetShortcutGuiThreadInfo(GetShortcutWindowThreadProcessId(handle, out _), ref info) &&
            IsShortcutChild(handle, info.Focus);
    }
}
