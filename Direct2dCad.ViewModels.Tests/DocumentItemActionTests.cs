using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Tests;

public sealed class DocumentItemActionTests
{
    [Fact]
    public async Task ClosingSpecificInactiveItemHonorsCancelAndKeepsActiveDrawing()
    {
        using var context = new MainWindowTestContext();
        var (first, _) = context.AddDocument("First", saved: true);
        first.CadDocumentViewModel.CadEditor.AddLine(default, new(10, 10));
        var (second, _) = context.AddDocument("Second", saved: true);
        Assert.All(context.ViewModel.DocumentExplorer.Documents, item => Assert.Same(context.ViewModel, item.Workspace));
        context.Dialogs.CloseResult = UnsavedDocumentDialogResult.Cancel;
        await context.ViewModel.CloseEditorDocumentCommand.ExecuteAsync(first);
        Assert.Equal(2, context.Layout.Documents.Count());
        Assert.Same(second, context.ViewModel.CurrentEditorTabViewModel);
        context.Dialogs.CloseResult = UnsavedDocumentDialogResult.Discard;
        await context.ViewModel.CloseEditorDocumentCommand.ExecuteAsync(first);
        Assert.Same(second, Assert.Single(context.Layout.Documents));
        Assert.Same(second, context.ViewModel.CurrentEditorTabViewModel);
        Assert.False(context.ViewModel.CloseEditorDocumentCommand.CanExecute(first));
        Assert.Equal("Second", Assert.Single(context.ViewModel.DocumentExplorer.Documents).DocumentName);
        await context.ViewModel.CloseEditorDocumentCommand.ExecuteAsync(second);
        Assert.Empty(context.Layout.Documents);
        Assert.Null(context.ViewModel.CurrentEditorTabViewModel);
    }

    [Fact]
    public void ActivationAndFolderCommandsUseTheRequestedDrawingAndSavedPath()
    {
        var locations = new RecordingFileLocationService();
        using var context = new MainWindowTestContext(fileLocationService: locations);
        var (first, _) = context.AddDocument("First", saved: true);
        context.AddDocument("Second");
        context.ViewModel.ActivateEditorDocumentCommand.Execute(first);
        Assert.Same(first, context.ViewModel.CurrentEditorTabViewModel);
        Assert.Same(first, context.Layout.ActiveDockable);
        Assert.False(context.ViewModel.OpenFileFolderCommand.CanExecute(""));
        context.ViewModel.OpenFileFolderCommand.Execute("");
        Assert.Empty(locations.Opened);
        Assert.True(context.ViewModel.OpenFileFolderCommand.CanExecute(first.CurrentFilePath));
        context.ViewModel.OpenFileFolderCommand.Execute(first.CurrentFilePath);
        Assert.Equal(first.CurrentFilePath, Assert.Single(locations.Opened));
    }

    private sealed class RecordingFileLocationService : IFileLocationService
    {
        public List<string> Opened { get; } = [];
        public bool CanOpenContainingFolder(string? filePath) => !string.IsNullOrWhiteSpace(filePath);
        public void OpenContainingFolder(string filePath) => Opened.Add(filePath);
    }
}
