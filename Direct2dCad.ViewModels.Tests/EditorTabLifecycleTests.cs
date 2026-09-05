using System.IO;
using Direct2dCad.Db.Cad;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Settings;

namespace Direct2dCad.ViewModels.Tests;

public sealed class EditorTabLifecycleTests
{
    [Fact]
    public void OverflowingOriginInputDoesNotExecuteACommand()
    {
        using var context = new CadToolboxTestContext();
        using var tab = context.CreateEditorTab(new RecordingDialogService(), new RecordingFileDialogs(), new RecordingDocumentWriter());
        tab.ViewModelCadUnit = Enums.ViewModelCadUnit.Inch;
        var history = context.Document.CadEditor.CreateDocumentHistorySnapshot();
        var previous = tab.ViewModelCadOriginX;
        tab.ViewModelCadOriginX = 1e308;
        Assert.Equal(previous, tab.ViewModelCadOriginX);
        Assert.True(context.Document.CadEditor.DocumentHistoryEquals(history));
    }

    [Theory]
    [InlineData(UnsavedDocumentDialogResult.Cancel, false)]
    [InlineData(UnsavedDocumentDialogResult.Discard, true)]
    [InlineData(UnsavedDocumentDialogResult.Save, true)]
    public async Task ClosingUnsavedDocumentHonorsDialogChoice(UnsavedDocumentDialogResult choice, bool expected)
    {
        using var context = new CadToolboxTestContext();
        var dialogs = new RecordingDialogService { CloseResult = choice };
        var writer = new RecordingDocumentWriter();
        using var tab = context.CreateEditorTab(dialogs, new RecordingFileDialogs { SavePath = "contract.d2cad" }, writer);
        Assert.True(tab.IsModified);
        Assert.Equal(expected, await tab.ConfirmCloseAsync());
        Assert.Equal(1, dialogs.CloseRequests);
        Assert.Equal(choice == UnsavedDocumentDialogResult.Save ? 1 : 0, writer.Writes);
        Assert.Equal(choice != UnsavedDocumentDialogResult.Save, tab.IsModified);
        Assert.Equal(0, dialogs.OpenProgressCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledOrFailedSaveNeverMarksDocumentClean(bool failWriter)
    {
        using var context = new CadToolboxTestContext();
        var dialogs = new RecordingDialogService { CloseResult = UnsavedDocumentDialogResult.Save };
        var files = new RecordingFileDialogs { SavePath = failWriter ? "failure.d2cad" : null };
        var writer = new RecordingDocumentWriter { Failure = new IOException("disk full") };
        using var tab = context.CreateEditorTab(dialogs, files, writer);
        Assert.False(await tab.ConfirmCloseAsync());
        Assert.True(tab.IsModified);
        Assert.Empty(tab.CurrentFilePath);
        Assert.Equal(0, dialogs.OpenProgressCount);
        Assert.Equal(failWriter ? 1 : 0, dialogs.Errors.Count);
    }

    [Fact]
    public async Task SaveEditUndoRedoAndReloadUpdateModifiedStateAndToolbar()
    {
        using var context = new CadToolboxTestContext();
        var dialogs = new RecordingDialogService();
        using var tab = context.CreateEditorTab(dialogs, new RecordingFileDialogs(), new RecordingDocumentWriter());
        Assert.True(await tab.SaveToFileForWorkspaceToolAsync("saved.d2cad", default));
        Assert.False(tab.IsModified);
        Assert.True(await tab.ConfirmCloseAsync());
        Assert.Equal(0, dialogs.CloseRequests);
        tab.ViewModelCadBackgroundColor = CadColor.Red;
        Assert.True(tab.IsModified);
        tab.UndoCommand.Execute(null);
        Assert.False(tab.IsModified);
        tab.RedoCommand.Execute(null);
        Assert.True(tab.IsModified);
        var loaded = CadDocument.Create("Loaded");
        loaded.ViewSettings.BackgroundColor = CadColor.Blue;
        loaded.ViewSettings.Origin.Position = new(25, 30);
        tab.Load(loaded, "loaded.d2cad");
        Assert.False(tab.IsModified);
        Assert.Equal("Loaded", tab.DocumentName);
        Assert.Equal(CadColor.Blue, tab.ViewModelCadBackgroundColor);
        Assert.Equal(25, tab.ViewModelCadOriginX);
        Assert.Equal(loaded.ViewSettings.Grid.MajorSpacingPresetId, tab.SelectedMajorGridSpacingPreset!.Id);
        tab.SelectedMajorGridSpacingPreset = tab.GridSpacingPresets.Single(item => item.OpensGridSettings);
        var settings = Assert.IsType<DocumentSettingsViewModel>(dialogs.DocumentSettings);
        Assert.Same(settings.GridAndSnapping, settings.SelectedSection);
        Assert.False(tab.SelectedMajorGridSpacingPreset!.OpensGridSettings);
        Assert.False(tab.IsModified);
    }
}
