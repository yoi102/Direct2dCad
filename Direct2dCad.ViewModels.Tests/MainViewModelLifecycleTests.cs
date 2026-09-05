using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Settings;
using Direct2dCad.ViewModels.Settings.UserSettings;

namespace Direct2dCad.ViewModels.Tests;

public sealed class MainViewModelLifecycleTests
{
    [Fact]
    public void WelcomeToolboxAndEditorActivationKeepCorrectPrintAndDocumentContext()
    {
        using var context = new MainWindowTestContext();
        var vm = context.ViewModel;
        Assert.False(vm.IsPrintAvailable);
        Assert.Null(context.ActiveEditor.Current);
        var (tab, _) = context.AddDocument("Paper");
        Assert.False(vm.IsPrintAvailable);
        tab.LayoutWorkspace.SelectedTab = tab.LayoutWorkspace.Tabs[1];
        Assert.True(vm.IsPrintAvailable);
        vm.ActiveDockContent = vm.EntityProperties;
        Assert.True(vm.IsPrintAvailable);
        Assert.Same(tab, context.ActiveEditor.Current);
        Assert.True(vm.Layers.HasDocument);
        vm.ActiveDockContent = new object();
        Assert.False(vm.IsPrintAvailable);
        vm.ActiveDockContent = tab;
        Assert.True(vm.IsPrintAvailable);

        var (other, _) = context.AddDocument("Model");
        Assert.False(vm.IsPrintAvailable);
        Assert.Same(other, context.ActiveEditor.Current);
        tab.LayoutWorkspace.SelectedTab = tab.LayoutWorkspace.Tabs[0];
        tab.LayoutWorkspace.SelectedTab = tab.LayoutWorkspace.Tabs[1];
        Assert.False(vm.IsPrintAvailable);
        vm.DocumentClosedCommand.Execute(other);
        Assert.Null(context.ActiveEditor.Current);
        Assert.False(vm.Layers.HasDocument);
        Assert.False(vm.Blocks.HasDocument);
        Assert.Equal(0, vm.TabControlSelectedIndex);
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
