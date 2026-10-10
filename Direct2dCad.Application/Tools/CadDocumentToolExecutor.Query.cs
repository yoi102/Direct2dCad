using System.Text.Json;
using System.Text.Json.Nodes;
using Direct2dCad.IO;

using Direct2dCad.Lang;

namespace Direct2dCad.Application.Tools;

public sealed partial class CadDocumentToolExecutor
{
    private Direct2dCad.Db.Cad.CadDocument? _querySnapshot;
    private Direct2dCad.Editor.CadEditor? _queryEditor;
    private long _queryVersion=-1;
    internal async Task<string> QueryAsync(string name,JsonElement args,CancellationToken token)
    {
        var editor=session.CadEditor;var version=editor.DocumentChangeVersion;
        if(args.TryGetProperty("expected_document_version",out var expected) && expected.GetInt64()!=version)
            throw new CadSnapshotChangedException();
        var selection=editor.Selection.EntityIds.ToHashSet();var owner=editor.ActiveOwnerBlockId;
        bool Current()=>!session.IsDisposed && ReferenceEquals(editor,session.CadEditor) && version==editor.DocumentChangeVersion && owner==editor.ActiveOwnerBlockId && selection.SetEquals(editor.Selection.EntityIds);
        var options=CadEntityQueryProtocol.Parse(args,name=="list_entities",MaximumListedEntities);
        var snapshot=ReferenceEquals(editor,_queryEditor) && _queryVersion==version && _querySnapshot is not null?_querySnapshot:
            await new CadDocumentStorage().CreateIndependentSnapshotAsync(editor.Document,
                new CadSnapshotCaptureOptions(Current,async ct=>await Task.Delay(1,ct)),token);
        var result=await Task.Run(()=>name=="list_entities" ? CadEntityQuery.CreatePage(snapshot,owner,selection,options,token) : CadEntityQuery.CreateStatistics(snapshot,owner,selection,options,token),token);
        token.ThrowIfCancellationRequested();
        if(!Current()) throw new CadSnapshotChangedException();
        _querySnapshot=snapshot;_queryEditor=editor;_queryVersion=version;
        var node=Direct2dCad.AI.Contracts.CadJson.SerializeToNode(result)!.AsObject();node["document_version"]=version;
        return Success(node);
    }
}
