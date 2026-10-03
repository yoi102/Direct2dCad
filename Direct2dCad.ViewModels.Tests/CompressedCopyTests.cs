using Direct2dCad.IO;
namespace Direct2dCad.ViewModels.Tests;
public sealed class CompressedCopyTests
{
    [Fact]public async Task CompressedCopyPreservesSourceOriginAndUndoPayloadsAndRefusesSourcePath()
    {
        using var c=new CadToolboxTestContext();var editor=c.Document.CadEditor;var id=editor.AddLine(default,new(100,0));
        var source=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".d2cad");var copy=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".d2cad");
        var dialogs=new RecordingDialogService();var files=new RecordingFileDialogs{SavePath=copy};using var tab=c.CreateEditorTab(dialogs,files,new RecordingDocumentWriter());
        try
        {
            new CadDocumentStorage().Save(editor.Document,source);var revision=CadFileRevision.Capture(source);var origin=CadDocumentOrigin.Get(editor.Document);
            tab.Load(editor.Document,source);editor=c.Document.CadEditor;editor.DeleteEntities([id]);var state=editor.CreateDocumentHistorySnapshot();
            await tab.SaveCompressedCopyCommand.ExecuteAsync(null);
            Assert.Empty(dialogs.Errors);Assert.True(File.Exists(copy));Assert.Equal(source,tab.CurrentFilePath);Assert.True(revision.Matches(CadFileRevision.Capture(source)));Assert.Equal(origin,CadDocumentOrigin.Get(editor.Document));Assert.True(editor.DocumentHistoryEquals(state));
            Assert.True(new CadDocumentStorage().Load(copy).GetEntity(id).IsErased);editor.Undo();Assert.False(editor.Document.GetEntity(id).IsErased);
            files.SavePath=source;await tab.SaveCompressedCopyCommand.ExecuteAsync(null);Assert.Single(dialogs.Errors);Assert.True(revision.Matches(CadFileRevision.Capture(source)));
        }
        finally{File.Delete(source);File.Delete(copy);}
    }
}
