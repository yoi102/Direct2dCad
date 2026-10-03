using CommunityToolkit.Mvvm.Input;
using Direct2dCad.IO;
using Direct2dCad.IO.Dxf;
using Direct2dCad.Lang;
using Direct2dCad.ViewModels.Services.Platform;
namespace Direct2dCad.ViewModels;
public partial class EditorTabViewModel
{
    [RelayCommand]private async Task SaveCompressedCopyAsync()
    {
        if(IsCompatibilityReadOnly)return;
        var path=_fileDialogService.ChooseCompatibleCopyPath(DocumentName+"-compressed");if(path is null)return;
        try
        {
            if(!string.IsNullOrEmpty(CurrentFilePath) && string.Equals(Path.GetFullPath(path),Path.GetFullPath(CurrentFilePath),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(CadUiText.Get("ExportSeparatePath"));
            var expected=await Task.Run(()=>CadFileRevision.Capture(path));
            while(expected.Exists)
            {
                var choice=await _dialogService.ShowFileConflictDialogAsync(path);
                if(choice==CadFileConflictChoice.Cancel)return;
                if(choice==CadFileConflictChoice.Overwrite)break;
                path=_fileDialogService.ChooseCompatibleCopyPath(DocumentName+"-compressed");if(path is null)return;
                if(!string.IsNullOrEmpty(CurrentFilePath) && string.Equals(Path.GetFullPath(path),Path.GetFullPath(CurrentFilePath),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(CadUiText.Get("ExportSeparatePath"));
                expected=await Task.Run(()=>CadFileRevision.Capture(path));
            }
            var editor=CadDocumentViewModel.CadEditor;var version=editor.DocumentChangeVersion;
            await Operation.RunAsync(CadUiText.Get("SaveCompressedCopy"),ct=>new CadDocumentStorage().SaveAsync(editor.Document,path,
                new(()=>!_disposed && ReferenceEquals(editor,CadDocumentViewModel.CadEditor) && version==editor.DocumentChangeVersion,async c=>await Task.Delay(1,c))
                {ExpectedDestination=expected,UpdateOrigin=false},ct));
        }
        catch(OperationCanceledException){}
        catch(Exception ex){await _dialogService.ShowOrReplaceMessageDialogAsync(ex.Message,CadUiText.Get("SaveCompressedCopy"));}
    }
    [RelayCommand]private async Task ExportDxfAsync()
    {
        var path=_fileDialogService.ExportDxfFile(DocumentName);if(path is null)return;
        try
        {
            if(string.Equals(Path.GetFullPath(path),string.IsNullOrEmpty(CurrentFilePath)?"":Path.GetFullPath(CurrentFilePath),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(CadUiText.Get("ExportSeparatePath"));
            var editor=CadDocumentViewModel.CadEditor;var version=editor.DocumentChangeVersion;
            var snapshot=await Operation.RunAsync(CadUiText.Get("ExportDxf"),ct=>new CadDocumentStorage().CreateIndependentSnapshotAsync(editor.Document,
                new(()=>!_disposed && ReferenceEquals(editor,CadDocumentViewModel.CadEditor) && version==editor.DocumentChangeVersion,async c=>await Task.Delay(1,c)),ct));
            var storage=new CadDxfStorage();var losses=await Operation.RunAsync(CadUiText.Get("ExportDxf"),ct=>Task.Run(()=>storage.AnalyzeExport(snapshot,ct),ct));
            if(losses.Count>0 && !await _dialogService.ShowOrReplaceMessageDialogWithCancelAsync(CadUiText.Get("DxfLossDescription")+Environment.NewLine+string.Join(Environment.NewLine,losses.Select(p=>$"{p.Key}: {p.Value}")),CadUiText.Get("ExportDxf")))return;
            var expected=await Task.Run(()=>CadFileRevision.Capture(path));
            while(expected.Exists)
            {
                var choice=await _dialogService.ShowFileConflictDialogAsync(path);
                if(choice==CadFileConflictChoice.Cancel)return;
                if(choice==CadFileConflictChoice.Overwrite)break;
                path=_fileDialogService.ExportDxfFile(DocumentName);if(path is null)return;
                if(!string.IsNullOrEmpty(CurrentFilePath) && string.Equals(Path.GetFullPath(path),Path.GetFullPath(CurrentFilePath),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(CadUiText.Get("ExportSeparatePath"));
                expected=await Task.Run(()=>CadFileRevision.Capture(path));
            }
            if(_disposed)return;
            await Operation.RunAsync(CadUiText.Get("ExportDxf"),ct=>storage.ExportAsync(snapshot,path,true,expected,ct));
        }
        catch(OperationCanceledException){}
        catch(Exception ex){await _dialogService.ShowOrReplaceMessageDialogAsync(ex.Message,CadUiText.Get("ExportDxf"));}
    }
}
