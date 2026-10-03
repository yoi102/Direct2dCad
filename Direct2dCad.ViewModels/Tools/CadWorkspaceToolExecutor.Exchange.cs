using System.Text.Json;
using Direct2dCad.IO;
using Direct2dCad.IO.Dxf;
using Direct2dCad.Db.Cad.Settings;
namespace Direct2dCad.ViewModels.Tools;

internal sealed partial class CadWorkspaceToolExecutor
{
    private async Task<string> OpenDxfAsync(JsonElement args,CancellationToken token)
    {
        CadUnit? unit=args.TryGetProperty("source_unit",out var value)?Enum.Parse<CadUnit>(value.GetString()!,true):null;
        var result=await new CadDxfStorage().ImportAsync(RequiredString(args,"file_path"),unit,token);
        token.ThrowIfCancellationRequested();
        var document=_workspace.CreateDocument(result.Document.Name);document.EditorTab.Load(result.Document,string.Empty);
        _defaultDocumentId=document.DocumentId;
        return Success(new{document=DocumentDto(_workspace.GetRequiredDocument(document.DocumentId)),imported=result.Imported,unsupported=result.Unsupported});
    }
    private async Task<string> ExportDxfAsync(JsonElement args,CancellationToken token)
    {
        var document=ResolveDocument(args);var path=Path.GetFullPath(RequiredString(args,"file_path"));
        if(!string.IsNullOrEmpty(document.FilePath) && string.Equals(path,Path.GetFullPath(document.FilePath),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Export requires a separate destination.");
        var editor=document.DocumentViewModel.CadEditor;var version=editor.DocumentChangeVersion;
        bool Current()=>!document.DocumentViewModel.IsDisposed && ReferenceEquals(editor,document.DocumentViewModel.CadEditor) && version==editor.DocumentChangeVersion;
        return await document.EditorTab.Operation.RunAsync("DXF",async ct=>
        {
            var snapshot=await new CadDocumentStorage().CreateIndependentSnapshotAsync(editor.Document,new(Current,async c=>await Task.Delay(1,c)),ct);
            var storage=new CadDxfStorage();var losses=await Task.Run(()=>storage.AnalyzeExport(snapshot,ct),ct);
            if(losses.Count>0 && !OptionalBool(args,"allow_loss",false))return Success(new{exported=false,losses,document_id=document.DocumentId,document_version=version});
            var expected=await Task.Run(()=>CadFileRevision.Capture(path),ct);
            if(expected.Exists && !OptionalBool(args,"overwrite",false))throw new CadFileConflictException(expected);
            if(!Current())throw new CadSnapshotChangedException();
            await storage.ExportAsync(snapshot,path,true,expected,ct);
            return Success(new{exported=true,file_path=path,losses,document_id=document.DocumentId,document_version=version});
        },token);
    }
}
