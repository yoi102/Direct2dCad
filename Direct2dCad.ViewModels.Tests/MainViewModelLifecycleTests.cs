using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Settings;
using Direct2dCad.ViewModels.Settings.UserSettings;

namespace Direct2dCad.ViewModels.Tests;

public sealed class MainViewModelLifecycleTests
{
    [Fact]
    public void NewAndTemplateDocumentsUseTheInjectedFactoryWithoutGlobalServiceLocation()
    {
        using var context = new MainWindowTestContext();
        context.ViewModel.NewCommand.Execute(null);
        var first = Assert.IsType<EditorTabViewModel>(context.ViewModel.CurrentEditorTabViewModel);
        context.ViewModel.NewTemplateCommand.Execute("A3-100");
        var template = Assert.IsType<EditorTabViewModel>(context.ViewModel.CurrentEditorTabViewModel);
        Assert.NotSame(first, template);
        Assert.Equal(2, context.Layout.Documents.Count());
        Assert.Equal("A3-100", template.DocumentName);
        Assert.Equal(100, template.CadDocumentViewModel.DimensionAnnotationScale);
        Assert.False(first.CadDocumentViewModel.IsDisposed);
    }

    [Fact]
    public void WelcomeToolboxAndEditorActivationKeepCorrectPrintAndDocumentContext()
    {
        using var context = new MainWindowTestContext();
        var vm = context.ViewModel;
        Assert.False(vm.IsPrintAvailable);
        Assert.Null(context.ActiveEditor.Current);
        var (tab, _) = context.AddDocument("Paper");
        Assert.True(vm.IsPrintAvailable);
        tab.LayoutWorkspace.SelectedTab = tab.LayoutWorkspace.Tabs[1];
        Assert.True(vm.IsPrintAvailable);
        vm.ActiveDockContent = vm.EntityProperties;
        Assert.True(vm.IsPrintAvailable);
        Assert.Same(tab, context.ActiveEditor.Current);
        Assert.True(vm.Layers.HasDocument);
        vm.TabControlSelectedIndex = 3;
        vm.ActiveDockContent = new object();
        Assert.False(vm.IsPrintAvailable);
        Assert.Null(vm.CurrentEditorTabViewModel);
        Assert.Equal(0, vm.TabControlSelectedIndex);
        Assert.Null(context.ActiveEditor.Current);
        Assert.Null(vm.EntityProperties.DocumentViewModel);
        Assert.Null(vm.EntityProperties.Entity);
        Assert.False(vm.Layers.HasDocument);
        Assert.False(vm.Blocks.HasDocument);
        Assert.False(vm.EntitySearch.HasDocument);
        Assert.False(vm.SelectionFilter.HasDocument);
        vm.ActiveDockContent = vm.EntityProperties;
        Assert.Null(vm.CurrentEditorTabViewModel);
        vm.ActiveDockContent = tab;
        Assert.True(vm.IsPrintAvailable);
        Assert.Same(tab, vm.CurrentEditorTabViewModel);
        Assert.Same(tab.CadDocumentViewModel, vm.EntityProperties.DocumentViewModel);
        Assert.True(vm.Layers.HasDocument);
        Assert.True(vm.Blocks.HasDocument);
        Assert.True(vm.EntitySearch.HasDocument);
        Assert.True(vm.SelectionFilter.HasDocument);

        var (other, _) = context.AddDocument("Model");
        Assert.True(vm.IsPrintAvailable);
        Assert.Same(other, context.ActiveEditor.Current);
        tab.LayoutWorkspace.SelectedTab = tab.LayoutWorkspace.Tabs[0];
        tab.LayoutWorkspace.SelectedTab = tab.LayoutWorkspace.Tabs[1];
        Assert.True(vm.IsPrintAvailable);
        vm.DocumentClosedCommand.Execute(other);
        Assert.Null(context.ActiveEditor.Current);
        Assert.False(vm.Layers.HasDocument);
        Assert.False(vm.Blocks.HasDocument);
        Assert.Equal(0, vm.TabControlSelectedIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToolboxHideAndReopenPreserveEditorContextAcrossTransientNullActivation(bool paperSpace)
    {
        using var context = new MainWindowTestContext();
        var vm = context.ViewModel;
        var (tab, _) = context.AddDocument("Terminal context", saved: true);
        if (paperSpace) tab.LayoutWorkspace.SelectedTab = tab.LayoutWorkspace.Tabs[1];
        vm.TabControlSelectedIndex = 3;
        vm.CommandLine.CommandText = "LINE draft";
        vm.AiAssistant.UserInput = "Unsent AI draft";
        var terminalContextChanges = 0;
        var aiContextChanges = 0;
        vm.CommandLine.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.CommandLine.HasDocument)) terminalContextChanges++;
        };
        vm.AiAssistant.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.AiAssistant.HasDocument)) aiContextChanges++;
        };

        AssertDocumentContext(context, tab);
        vm.ActiveDockContent = vm.CommandLine;
        AssertDocumentContext(context, tab);
        // AvalonDock temporarily reports null when hiding, floating, or reopening an anchorable.
        vm.ActiveDockContent = null;
        AssertDocumentContext(context, tab);
        vm.ActiveDockContent = vm.CommandLine;
        AssertDocumentContext(context, tab);
        vm.ActiveDockContent = null;
        vm.ActiveDockContent = vm.AiAssistant;
        AssertDocumentContext(context, tab);

        Assert.Equal(3, vm.TabControlSelectedIndex);
        Assert.Equal("LINE draft", vm.CommandLine.CommandText);
        Assert.Equal("Unsent AI draft", vm.AiAssistant.UserInput);
        Assert.Equal(0, terminalContextChanges);
        Assert.Equal(0, aiContextChanges);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WelcomePageThenNullAndToolboxActivationDoNotRestoreDocumentContext(bool previouslyHadDocument)
    {
        using var context = new MainWindowTestContext();
        var vm = context.ViewModel;
        if (previouslyHadDocument) context.AddDocument("Still open behind welcome", saved: true);

        vm.ActiveDockContent = new object(); // A non-CAD static document, such as the welcome page.
        AssertDocumentContext(context, null);
        vm.ActiveDockContent = null;
        AssertDocumentContext(context, null);
        vm.ActiveDockContent = vm.CommandLine;
        AssertDocumentContext(context, null);
        vm.ActiveDockContent = null;
        vm.ActiveDockContent = vm.AiAssistant;
        AssertDocumentContext(context, null);
        Assert.Equal(0, vm.TabControlSelectedIndex);
    }

    [Fact]
    public void ClosingDocumentWhileToolboxActivePreventsTransientNullFromRevivingDisposedContext()
    {
        using var context = new MainWindowTestContext();
        var vm = context.ViewModel;
        var (tab, _) = context.AddDocument("Closed document", saved: true);
        vm.ActiveDockContent = vm.CommandLine;
        AssertDocumentContext(context, tab);

        vm.DocumentClosedCommand.Execute(tab);
        Assert.True(tab.CadDocumentViewModel.IsDisposed);
        AssertDocumentContext(context, null);
        foreach (var toolbox in new object[] { vm.CommandLine, vm.AiAssistant, vm.EntityProperties, vm.Layers })
        {
            vm.ActiveDockContent = null;
            AssertDocumentContext(context, null);
            vm.ActiveDockContent = toolbox;
            AssertDocumentContext(context, null);
            Assert.True(tab.CadDocumentViewModel.IsDisposed);
        }
        Assert.Equal(0, vm.TabControlSelectedIndex);
    }

    private static void AssertDocumentContext(MainWindowTestContext context, EditorTabViewModel? expected)
    {
        var vm = context.ViewModel;
        Assert.Same(expected, vm.CurrentEditorTabViewModel);
        Assert.Same(expected, context.ActiveEditor.Current);
        Assert.Same(expected?.CadDocumentViewModel, vm.EntityProperties.DocumentViewModel);
        Assert.Equal(expected is not null, vm.IsPrintAvailable);
        Assert.Equal(expected is not null, vm.CommandLine.HasDocument);
        Assert.Equal(expected is not null, vm.AiAssistant.HasDocument);
        Assert.Equal(expected is not null, vm.Layers.HasDocument);
        Assert.Equal(expected is not null, vm.Blocks.HasDocument);
        Assert.Equal(expected is not null, vm.EntitySearch.HasDocument);
        Assert.Equal(expected is not null, vm.SelectionFilter.HasDocument);
        if (expected is null) Assert.Null(vm.EntityProperties.Entity);
        else Assert.False(expected.CadDocumentViewModel.IsDisposed);
    }

    [Theory]
    [InlineData(UnsavedDocumentDialogResult.Cancel, false, 0)]
    [InlineData(UnsavedDocumentDialogResult.Discard, true, 0)]
    [InlineData(UnsavedDocumentDialogResult.Save, true, 1)]
    public async Task CloseApplicationHandlesAllModifiedDocuments(UnsavedDocumentDialogResult choice, bool expected, int writes)
    {
        using var context = new MainWindowTestContext();
        context.Dialogs.CloseResult = choice;
        context.Files.SavePath = Path.GetFullPath("new-document.d2cad");
        var (one, firstWriter) = context.AddDocument("First", saved: true);
        var (two, secondWriter) = context.AddDocument("Second");
        context.AddDocument("Clean", saved: true);
        one.CadDocumentViewModel.CadEditor.AddLine(new(0, 0), new(10, 10));
        Assert.Equal(expected, await context.ViewModel.ConfirmCloseApplicationAsync());
        Assert.Equal(1, context.Dialogs.CloseRequests);
        Assert.Equal(2, context.Dialogs.UnsavedDocuments.Count);
        Assert.Equal(writes, firstWriter.Writes);
        Assert.Equal(writes, secondWriter.Writes);
        Assert.Equal(writes == 0, one.IsModified);
        Assert.Equal(writes == 0, two.IsModified);
        Assert.Equal(0, context.Dialogs.OpenProgressCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloseApplicationStopsOnCancelledOrFailedSave(bool failure)
    {
        using var context = new MainWindowTestContext();
        context.Dialogs.CloseResult = UnsavedDocumentDialogResult.Save;
        var (first, writer) = context.AddDocument("First");
        var (second, secondWriter) = context.AddDocument("Second");
        if (failure)
        {
            context.Files.SavePath = Path.GetFullPath("failed.d2cad");
            writer.Failure = new IOException("Disk full");
        }
        Assert.False(await context.ViewModel.ConfirmCloseApplicationAsync());
        Assert.True(first.IsModified);
        Assert.True(second.IsModified);
        Assert.Equal(0, secondWriter.Writes);
        Assert.Equal(0, context.Dialogs.OpenProgressCount);
        Assert.Equal(failure ? 1 : 0, context.Dialogs.Errors.Count);
    }

    [Fact]
    public async Task CleanWorkspaceClosesWithoutConfirmation()
    {
        using var context = new MainWindowTestContext();
        Assert.True(await context.ViewModel.ConfirmCloseApplicationAsync());
        context.AddDocument("Saved", saved: true);
        Assert.True(await context.ViewModel.ConfirmCloseApplicationAsync());
        Assert.Equal(0, context.Dialogs.CloseRequests);
    }

    [Fact]
    public async Task OpenExistingDocumentActivatesItWithoutLoadingOrCreatingAnother()
    {
        using var context = new MainWindowTestContext();
        var (one, _) = context.AddDocument("Existing", saved: true);
        context.AddDocument("Other", saved: true);
        context.Files.OpenPath = one.CurrentFilePath!.ToUpperInvariant();
        await context.ViewModel.OpenFileCommand.ExecuteAsync(null);
        Assert.Same(one, context.ActiveEditor.Current);
        Assert.Same(one, context.Layout.ActiveDockable);
        Assert.Equal(2, context.Layout.Documents.Count());
        Assert.Empty(context.Dialogs.Errors);
        context.Files.OpenPath = Path.GetFullPath("missing-" + Guid.NewGuid() + ".d2cad");
        await context.ViewModel.OpenFileCommand.ExecuteAsync(null);
        Assert.Single(context.Dialogs.Errors);
        Assert.Equal(0, context.Dialogs.OpenProgressCount);
        Assert.Same(one, context.ActiveEditor.Current);
    }

    [Fact]
    public void SettingsDialogsThemeCultureAndTopmostCommandsAreWired()
    {
        using var context = new MainWindowTestContext();
        var vm = context.ViewModel;
        vm.OpenDocumentSettingsDialogCommand.Execute(null);
        Assert.Null(context.Dialogs.DocumentSettings);
        var (tab, _) = context.AddDocument("Settings");
        vm.OpenDocumentSettingsDialogCommand.Execute(null);
        Assert.IsType<DocumentSettingsViewModel>(context.Dialogs.DocumentSettings);
        vm.OpenUserSettingsDialogCommand.Execute(null);
        var user = Assert.IsType<UserSettingsViewModel>(context.Dialogs.UserSettings);
        user.Rendering.ShowFramesPerSecond = false;
        Assert.True(user.TryApply());
        Assert.False(tab.CadDocumentViewModel.ShowFramesPerSecond);
        vm.ChangeCultureCommand.Execute("1041");
        Assert.Equal(1041, context.Appearance.CultureLcid);
        vm.ChangeCultureCommand.Execute("invalid");
        Assert.Equal(1041, context.Appearance.CultureLcid);
        vm.IsDarkTheme = !vm.IsDarkTheme;
        Assert.Equal(vm.IsDarkTheme, context.Appearance.IsDarkTheme);
        Assert.NotEmpty(context.Settings.Saved);
        vm.ChangeTopmostCommand.Execute(null);
        Assert.True(vm.Topmost);
        context.Settings.Failure = new IOException("Read-only settings");
        vm.IsDarkTheme = !vm.IsDarkTheme;
        Assert.Single(context.Dialogs.Errors);
    }
}
