using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Collections.Specialized;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using TextBox = FlaUI.Core.AutomationElements.TextBox;

namespace Direct2dCad.UiAutomation.Tests;

[Collection(CadApplicationCollection.Name)]
public sealed class MainWindowUiTests : IDisposable
{
    private readonly CadApplicationFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void ApplicationLaunchesWithAccessibleMainSurface()
    {
        fixture.EnsureApplicationIsRunning();

        Assert.Equal(
            "Direct2dCad.MainWindow",
            fixture.MainWindow.AutomationId);
        Assert.True(fixture.MainWindow.IsEnabled);
        Assert.NotNull(fixture.WaitForElement("MainRibbon"));
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void UserSettingsDialog_AppliesAndPersistsIsolatedSettings()
    {
        fixture.WaitForElement("FileRibbonTab").AsTabItem().Select();
        var settingsButton = fixture.WaitForElement("UserSettingsButton").AsButton();
        fixture.WaitUntil(
            () => settingsButton.IsEnabled && !settingsButton.IsOffscreen,
            "The user settings button did not become interactive.");
        settingsButton.Click();

        var dialog = fixture.WaitForWindow("UserSettingsDialog");
        Assert.NotNull(dialog.FindFirstDescendant(
            condition => condition.ByAutomationId("ResetUserSettingsButton")));
        var darkTheme = dialog.FindFirstDescendant(
            condition => condition.ByAutomationId("DarkThemeCheckBox"));
        Assert.NotNull(darkTheme);
        var darkThemeCheckBox = darkTheme.AsCheckBox();
        var expectedDarkTheme = darkThemeCheckBox.ToggleState != ToggleState.On;
        darkThemeCheckBox.Click();

        var apply = dialog.FindFirstDescendant(
            condition => condition.ByAutomationId("ApplyUserSettingsButton"));
        Assert.NotNull(apply);
        apply.AsButton().Click();

        var settingsPath = Path.Combine(
            fixture.SettingsDirectory,
            "user-settings.json");
        fixture.WaitUntil(
            () => File.Exists(settingsPath) &&
                  ReadDarkThemeSetting(settingsPath) == expectedDarkTheme,
            "Applying user settings did not persist the isolated JSON file.");

        var cancel = dialog.FindFirstDescendant(
            condition => condition.ByAutomationId("CancelUserSettingsButton"));
        Assert.NotNull(cancel);
        cancel.AsButton().Invoke();

        fixture.WaitUntil(
            () => !fixture.IsWindowOpen("UserSettingsDialog"),
            "The user settings dialog did not close after Cancel.");
        fixture.EnsureApplicationIsRunning();
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void NewDocument_LineDrawingAndEscapeCompleteEndToEnd()
    {
        CreateNewDocument();

        var canvas = fixture.WaitForElement("CadCanvas");
        var drawTab = fixture.WaitForElement("DrawRibbonTab").AsTabItem();
        drawTab.Select();
        var lineTool = fixture.WaitForElement("LineToolButton").AsToggleButton();
        lineTool.Click();
        var toolMode = fixture.WaitForElement("CurrentToolModeText");

        fixture.WaitUntil(
            () => string.Equals(toolMode.Name, "Line", StringComparison.Ordinal),
            "The Line tool did not become active.");

        canvas.Focus();
        var bounds = canvas.BoundingRectangle;
        Assert.True(bounds.Width > 200);
        Assert.True(bounds.Height > 150);
        var first = new Point(
            (int)(bounds.Left + bounds.Width * 0.35),
            (int)(bounds.Top + bounds.Height * 0.45));
        var second = new Point(
            (int)(bounds.Left + bounds.Width * 0.65),
            (int)(bounds.Top + bounds.Height * 0.60));
        Mouse.Click(first);
        Mouse.Click(second);

        fixture.EnsureApplicationIsRunning();
        Keyboard.Press(VirtualKeyShort.ESC);
        fixture.WaitUntil(
            () => string.Equals(toolMode.Name, "Select", StringComparison.Ordinal),
            "Escape did not return the canvas to Select mode.");

        Assert.Equal(
            ToggleState.On,
            fixture.WaitForElement("SelectToolButton").AsToggleButton().ToggleState);

        var commandInput = GetOrOpenCommandLineInput();
        var commandOutput = fixture.WaitForElement("CommandLineOutput");

        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 1");

        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 0");

        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "REDO", "Redo completed.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 1");

        ExecuteCommandAndWaitForOutput(
            commandInput,
            commandOutput,
            "RECTANGLE",
            "Rectangle mode active.");
        ExecuteCommand(commandInput, "0,0");
        ExecuteCommand(commandInput, "20,10");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 2");

        ExecuteCommandAndWaitForOutput(
            commandInput,
            commandOutput,
            "SELECTALL",
            "Selected 2 entities.");
        ExecuteCommandAndWaitForOutput(
            commandInput,
            commandOutput,
            "ERASE",
            "Deleted 2 entities.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 0");

        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 2");

        Mouse.Click(new Point(
            (int)(bounds.Left + 12),
            (int)(bounds.Top + 12)));
        Thread.Sleep(100);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Thread.Sleep(100);
        Keyboard.Press(VirtualKeyShort.DELETE);
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 0");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "UNDO", "Undo completed.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 2");

        drawTab.Select();
        fixture.WaitForElement("DocumentSettingsButton").AsButton().Invoke();
        var documentSettings = fixture.WaitForWindow("DocumentSettingsDialog");
        Assert.NotNull(documentSettings.FindFirstDescendant(
            condition => condition.ByAutomationId("ResetDocumentSettingsButton")));
        var cancelDocumentSettings = documentSettings.FindFirstDescendant(
            condition => condition.ByAutomationId("CancelDocumentSettingsButton"));
        Assert.NotNull(cancelDocumentSettings);
        cancelDocumentSettings.AsButton().Invoke();

        fixture.MainWindow.Close();
        fixture.WaitForElement("MessageDialog");
        ClickWhenEnabled("MessageDialogOkButton");
        fixture.WaitForElement("UnsavedDocumentsDialog");
        var unsavedDocuments = fixture.WaitForElement("UnsavedDocumentsList").AsListBox();
        Assert.NotEmpty(unsavedDocuments.Items);
        ClickWhenEnabled("CancelCloseDocumentsButton");
        fixture.WaitUntil(
            () => fixture.MainWindow.IsEnabled,
            "Cancelling the unsaved-document dialog did not return to the editor.");
        fixture.EnsureApplicationIsRunning();
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void MultiSelectionPropertiesAndLockedFrozenLayersStayInSync()
    {
        CreateNewDocument();
        var commandInput = GetOrOpenCommandLineInput();
        var commandOutput = fixture.WaitForElement("CommandLineOutput");

        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "LINE", "Line mode active.");
        ExecuteCommand(commandInput, "0,0");
        ExecuteCommand(commandInput, "20,10");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "RECTANGLE", "Rectangle mode active.");
        ExecuteCommand(commandInput, "30,0");
        ExecuteCommand(commandInput, "50,20");
        ExecuteCommandAndWaitForOutput(
            commandInput,
            commandOutput,
            "CANCEL",
            "Select mode active.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "SELECTALL", "Selected 2 entities.");

        var selectionCount = EnsureToolboxElement(
            "MultiEntitySelectionCount",
            "toolbox.entity-properties");
        fixture.WaitUntil(
            () => selectionCount.Name.EndsWith(": 2", StringComparison.Ordinal),
            "The multi-selection property panel did not show two selected entities.");
        Assert.NotNull(fixture.WaitForElement("MultiEntityLayerSelector").AsComboBox());

        var lockToggle = EnsureToolboxElement(
            "LayerLockToggle",
            "toolbox.layers").AsToggleButton();
        lockToggle.Click();
        fixture.WaitUntil(
            () => lockToggle.ToggleState == ToggleState.On,
            "The default layer did not become locked.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "SELECTALL", "Selected 2 entities.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "ERASE", "Nothing is selected.");

        lockToggle = fixture.WaitForElement("LayerLockToggle").AsToggleButton();
        lockToggle.Click();
        fixture.WaitUntil(
            () => lockToggle.ToggleState == ToggleState.Off,
            "The default layer did not become unlocked.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "SELECTALL", "Selected 2 entities.");

        var freezeToggle = fixture.WaitForElement("LayerFreezeToggle").AsToggleButton();
        freezeToggle.Click();
        fixture.WaitUntil(
            () => freezeToggle.ToggleState == ToggleState.On,
            "The default layer did not become frozen.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "SELECTALL", "Selected 0 entities.");
        freezeToggle = fixture.WaitForElement("LayerFreezeToggle").AsToggleButton();
        freezeToggle.Click();
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void CadImageAndOleClipboardContentEnterMovablePasteAndCanBePlaced()
    {
        CreateNewDocument();
        var commandInput = GetOrOpenCommandLineInput();
        var commandOutput = fixture.WaitForElement("CommandLineOutput");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "RECTANGLE", "Rectangle mode active.");
        ExecuteCommand(commandInput, "0,0");
        ExecuteCommand(commandInput, "20,10");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "SELECTALL", "Selected 1 entities.");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "COPY", "Copied 1 entity");
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "PASTE", "Paste preview active for 1 entity");

        var canvas = fixture.WaitForElement("CadCanvas");
        var bounds = canvas.BoundingRectangle;
        Mouse.Click(new Point(
            (int)(bounds.Left + bounds.Width * 0.55),
            (int)(bounds.Top + bounds.Height * 0.55)));
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 2");

        SetClipboardImage();
        canvas.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        Thread.Sleep(150);
        Mouse.Click(new Point(
            (int)(bounds.Left + bounds.Width * 0.65),
            (int)(bounds.Top + bounds.Height * 0.45)));
        ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 3");

        var oleSourcePath = SetClipboardOleFile();
        try
        {
            canvas.Focus();
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
            Thread.Sleep(250);
            Mouse.Click(new Point(
                (int)(bounds.Left + bounds.Width * 0.45),
                (int)(bounds.Top + bounds.Height * 0.40)));
            ExecuteCommandAndWaitForOutput(commandInput, commandOutput, "STATUS", "Entities: 4");
        }
        finally
        {
            File.Delete(oleSourcePath);
        }
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void LayoutTabsAndPaperModelSpaceSwitchTogether()
    {
        CreateNewDocument();
        var layoutTabs = fixture.WaitForElement("LayoutTabs").AsListBox();
        var initialTabCount = layoutTabs.Items.Length;
        Assert.True(initialTabCount >= 1);

        fixture.WaitForElement("AddLayoutButton").AsButton().Invoke();
        fixture.WaitUntil(
            () => layoutTabs.Items.Length == initialTabCount + 1,
            "Adding a layout did not create a second layout tab.");

        layoutTabs.Items[^1].Select();
        var paperSpace = fixture.WaitForElement("PaperSpaceButton").AsRadioButton();
        var modelSpace = fixture.WaitForElement("LayoutModelSpaceButton").AsRadioButton();
        fixture.WaitUntil(
            () => paperSpace.IsChecked == true,
            "The new layout did not enter paper space.");

        var createViewport = fixture.WaitForElement("CreateLayoutViewportButton").AsButton();
        createViewport.Invoke();
        var canvas = fixture.WaitForElement("CadCanvas");
        var bounds = canvas.BoundingRectangle;
        Mouse.Click(new Point(
            (int)(bounds.Left + bounds.Width * 0.25),
            (int)(bounds.Top + bounds.Height * 0.25)));
        Mouse.Click(new Point(
            (int)(bounds.Left + bounds.Width * 0.75),
            (int)(bounds.Top + bounds.Height * 0.75)));
        fixture.WaitUntil(
            () => modelSpace.IsEnabled,
            "Creating a layout viewport did not enable model space.");

        modelSpace.Click();
        fixture.WaitUntil(
            () => fixture.WaitForElement("LayoutModelSpaceButton")
                .AsRadioButton()
                .IsChecked == true,
            "The layout did not enter model space.");
        paperSpace = fixture.WaitForElement("PaperSpaceButton").AsRadioButton();
        paperSpace.Click();
        fixture.WaitUntil(
            () => fixture.WaitForElement("PaperSpaceButton")
                .AsRadioButton()
                .IsChecked == true,
            "The layout did not return to paper space.");

        layoutTabs.Items[0].Select();
        fixture.WaitUntil(
            () => layoutTabs.Items[0].IsSelected,
            "Selecting the model tab did not update the active layout tab.");
    }

    [Fact]
    [Trait("Category", "UiAutomation")]
    public void AiToolCommands_CreateMeasureAndManageGridPreset()
    {
        CreateNewDocument();
        var commandInput = GetOrOpenCommandLineInput();
        var commandOutput = fixture.WaitForElement("CommandLineOutput");

        ExecuteCommandAndWaitForOutput(
            commandInput,
            commandOutput,
            "TOOL add_line {\"x1\":0,\"y1\":0,\"x2\":10,\"y2\":0}",
            "created_entity_id");
        ExecuteCommandAndWaitForOutput(
            commandInput,
            commandOutput,
            "TOOL measure_geometry {\"points\":[{\"x\":0,\"y\":0},{\"x\":3,\"y\":4}]}",
            "total_distance_millimeters");
        ExecuteCommandAndWaitForOutput(
            commandInput,
            commandOutput,
            "TOOL manage_grid_presets {\"operation\":\"list\"}",
            "presets");
    }

    private static void ExecuteCommand(TextBox commandInput, string command)
    {
        commandInput.Focus();
        commandInput.Text = command;
        Keyboard.Press(VirtualKeyShort.RETURN);

        var autocompleteWait = Stopwatch.StartNew();
        while (autocompleteWait.Elapsed < TimeSpan.FromMilliseconds(500) &&
               !string.IsNullOrWhiteSpace(commandInput.Text))
        {
            Thread.Sleep(20);
        }

        if (!string.IsNullOrWhiteSpace(commandInput.Text))
            Keyboard.Press(VirtualKeyShort.RETURN);
    }

    private TextBox GetOrOpenCommandLineInput()
    {
        var existingInput = fixture.MainWindow.FindFirstDescendant(
            condition => condition.ByAutomationId("CommandLineInput"));
        if (existingInput is not null && !existingInput.IsOffscreen)
            return existingInput.AsTextBox();

        fixture.MainWindow.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.OEM_3);
        return fixture.WaitForElement("CommandLineInput").AsTextBox();
    }

    private void CreateNewDocument()
    {
        fixture.WaitForElement("FileRibbonTab").AsTabItem().Select();
        var button = fixture.WaitForElement("NewDocumentButton").AsButton();
        fixture.WaitUntil(
            () => button.IsEnabled && !button.IsOffscreen,
            "The new-document button did not become interactive.");
        button.Invoke();
    }

    private AutomationElement EnsureToolboxElement(
        string automationId,
        string toolboxContentId)
    {
        var existing = fixture.MainWindow.FindFirstDescendant(
            condition => condition.ByAutomationId(automationId));
        if (existing is not null && !existing.IsOffscreen)
            return existing;

        var shortcut = toolboxContentId switch
        {
            "toolbox.entity-properties" => VirtualKeyShort.KEY_G,
            "toolbox.layers" => VirtualKeyShort.KEY_L,
            _ => throw new ArgumentOutOfRangeException(
                nameof(toolboxContentId),
                toolboxContentId,
                "No UI shortcut is registered for this toolbox.")
        };

        fixture.MainWindow.Focus();
        ToggleToolboxShortcut(shortcut);
        var activated = WaitForVisibleElement(automationId, TimeSpan.FromSeconds(2));
        if (activated is not null)
            return activated;

        // A visible toolbox is hidden by its toggle shortcut. Toggle once more
        // when the first press found it open but its content had not been in the
        // automation tree at the time of the initial lookup.
        ToggleToolboxShortcut(shortcut);

        return fixture.WaitForElement(automationId);
    }

    private static void ToggleToolboxShortcut(VirtualKeyShort key)
    {
        Keyboard.TypeSimultaneously(
            VirtualKeyShort.CONTROL,
            VirtualKeyShort.SHIFT,
            key);
    }

    private AutomationElement? WaitForVisibleElement(
        string automationId,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            fixture.EnsureApplicationIsRunning();
            var element = fixture.MainWindow.FindFirstDescendant(
                condition => condition.ByAutomationId(automationId));
            if (element is not null && !element.IsOffscreen)
                return element;

            Thread.Sleep(50);
        }

        return null;
    }

    private void ClickWhenEnabled(string automationId)
    {
        var button = fixture.WaitForElement(automationId).AsButton();
        fixture.WaitUntil(
            () => button.IsEnabled && !button.IsOffscreen,
            $"Button '{automationId}' did not become interactive.");
        button.Click();
    }

    private void ExecuteCommandAndWaitForOutput(
        TextBox commandInput,
        AutomationElement commandOutput,
        string command,
        string expectedText)
    {
        ClearCommandOutput(commandInput, commandOutput);
        ExecuteCommand(commandInput, command);
        fixture.WaitUntil(
            () => CountOutputMatches(commandOutput, expectedText) > 0,
            $"Command output did not contain '{expectedText}'. " +
            $"Visible output: {ReadCommandOutput(commandOutput)}");
    }

    private static string ReadCommandOutput(AutomationElement commandOutput) =>
        $"Latest result: {commandOutput.Properties.HelpText.ValueOrDefault}; visible rows: " + string.Join(
            " | ",
            commandOutput.FindAllDescendants(
                    condition => condition.ByControlType(ControlType.ListItem))
                .Select(element => element.Name)
                .Where(text => !string.IsNullOrWhiteSpace(text)));

    private void ClearCommandOutput(
        TextBox commandInput,
        AutomationElement commandOutput)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            ExecuteCommand(commandInput, "CLEAR");
            Thread.Sleep(100);
            if (CountOutputEntries(commandOutput) != 0)
                continue;

            Thread.Sleep(100);
            if (CountOutputEntries(commandOutput) == 0)
                return;
        }

        throw new TimeoutException("The CLEAR command did not empty the terminal output.");
    }

    private static int CountOutputMatches(
        AutomationElement commandOutput,
        string expectedText)
    {
        var matchCount = 0;
        try
        {
            if (commandOutput.Properties.HelpText.ValueOrDefault?.Contains(
                    expectedText,
                    StringComparison.Ordinal) == true)
            {
                return 1;
            }

            foreach (var element in commandOutput.FindAllDescendants())
            {
                try
                {
                    if (element.Name.Contains(expectedText, StringComparison.Ordinal))
                        matchCount++;
                }
                catch (COMException)
                {
                    // Virtualized terminal rows can disappear between query and property access.
                }
            }
        }
        catch (COMException)
        {
            // UIA can invalidate the descendant snapshot while a range update is applied.
        }

        return matchCount;
    }

    private static int CountOutputEntries(AutomationElement commandOutput)
    {
        try
        {
            return commandOutput.FindAllDescendants(
                condition => condition.ByControlType(ControlType.ListItem)).Length;
        }
        catch (COMException)
        {
            return int.MaxValue;
        }
    }

    private static bool ReadDarkThemeSetting(string settingsPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        return document.RootElement
            .GetProperty("General")
            .GetProperty("IsDarkTheme")
            .GetBoolean();
    }

    private static void SetClipboardImage()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var bitmap = new Bitmap(8, 6);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.LimeGreen);
                System.Windows.Forms.Clipboard.SetImage(bitmap);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw failure;
    }

    private static string SetClipboardOleFile()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"Direct2dCad-UI-OLE-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "Direct2dCad OLE clipboard UI automation");

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var paths = new StringCollection { path };
                System.Windows.Forms.Clipboard.SetFileDropList(paths);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            File.Delete(path);
            throw failure;
        }

        return path;
    }
}
