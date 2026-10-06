using AvalonDock.Mvvm;
using AvalonDock.Core;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.IO;
using Direct2dCad.IO.Dxf;
using Direct2dCad.Lang;
namespace Direct2dCad.ViewModels;
public partial class MainViewModel
{
    [RelayCommand]private async Task ImportDxfAsync()
    {
        var path=_fileDialogService.OpenDxfFile();if(path is null)return;
        try
        {
            var focus=_dockLayoutService.ActiveDockable;var storage=new CadDxfStorage();CadDxfResult result;
            var revision=await Task.Run(()=>
            {
                if(new FileInfo(path).Length>storage.Limits.MaximumBytes)throw new InvalidDataException("DXF exceeds the file budget.");
                return CadFileRevision.Capture(path);
            });
            try {result=await OpenOperation.RunAsync(CadUiText.Get("ImportDxf"),ct=>storage.ImportAsync(path,token:ct));}
            catch(CadDxfUnitRequiredException)
            {
                var unit=await _dialogService.ChooseDxfSourceUnitAsync();if(unit is null)return;
                await Task.Run(revision.Verify);
                result=await OpenOperation.RunAsync(CadUiText.Get("ImportDxf"),ct=>storage.ImportAsync(path,unit,ct));
            }
            var changedFocus=!ReferenceEquals(focus,_dockLayoutService.ActiveDockable);var finalFocus=_dockLayoutService.ActiveDockable;
            var tab=_dockLayoutService.OpenOrActivateDocument(e=>false,()=>_editorTabFactory.Create(t=>t.Load(result.Document,string.Empty)));
            if(changedFocus)_dockLayoutService.ActiveDockable=finalFocus;else CurrentEditorTabViewModel=tab;
            DocumentExplorer.RefreshDocuments();
            _snackbarService.Enqueue(string.Format(CadUiText.Get("DxfImported"),result.Imported,result.Unsupported.Values.Sum()));
            if(result.Unsupported.Count>0)_snackbarService.Enqueue(string.Join("; ",result.Unsupported.Select(p=>$"{p.Key}: {p.Value}")));
        }
        catch(OperationCanceledException){}
        catch(Exception ex){await _dialogService.ShowOrReplaceMessageDialogAsync(ex.Message,CadUiText.Get("ImportDxf"));}
    }
}
