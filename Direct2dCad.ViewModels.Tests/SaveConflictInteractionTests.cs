using Direct2dCad.IO;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Tests;

public sealed class SaveConflictInteractionTests
{
    [Theory][InlineData(CadFileConflictChoice.Cancel)][InlineData(CadFileConflictChoice.Overwrite)][InlineData(CadFileConflictChoice.SaveAs)]
    public async Task ExternalChangeOffersOneConcreteChoice(CadFileConflictChoice choice)
    {
        var directory=Path.Combine(Path.GetTempPath(),"SaveConflict-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try
        {
            using var c=new CadToolboxTestContext();var storage=new CadDocumentStorage();var original=Path.Combine(directory,"original.d2cad");var copy=Path.Combine(directory,"copy.d2cad");
            storage.Save(c.Document.CadEditor.Document,original);var dialog=new RecordingDialogService{FileConflictChoice=choice};var files=new RecordingFileDialogs{SavePath=copy};using var tab=c.CreateEditorTab(dialog,files,storage);
            tab.Load(c.Document.CadEditor.Document,original);c.Document.CadEditor.AddLine(default,new(10,0));File.WriteAllText(original,"external change");
            await tab.SaveFileCommand.ExecuteAsync(null);Assert.Equal(1,dialog.FileConflictRequests);Assert.Empty(dialog.Errors);
            if(choice==CadFileConflictChoice.Overwrite){Assert.Single(storage.Load(original).Entities);Assert.False(tab.IsModified);}
            else{Assert.Equal("external change",File.ReadAllText(original));if(choice==CadFileConflictChoice.SaveAs){Assert.Single(storage.Load(copy).Entities);Assert.Equal(copy,tab.CurrentFilePath);Assert.False(tab.IsModified);}else{Assert.False(File.Exists(copy));Assert.True(tab.IsModified);}}
        }
        finally{Directory.Delete(directory,true);}
    }
}
