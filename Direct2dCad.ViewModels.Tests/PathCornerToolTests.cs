using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.ViewModels.Tools;

namespace Direct2dCad.ViewModels.Tests;

public sealed class PathCornerToolTests
{
    [Theory] [InlineData("fillet",false)] [InlineData("chamfer",false)] [InlineData("fillet",true)] [InlineData("chamfer",true)]
    public async Task ToolCanModifyOnePathAndNativeStoragePreservesTheResult(string operation,bool whole)
    {
        using var workspace=new ToolExecutionWorkspace(); var vm=workspace.CreateDocument("path").GetViewModel();
        var entity=vm.CadEditor.Document.AddPolyline([new(0,0),new(40,0),new(40,40),new(0,40)],true);
        var args=JsonSerializer.Serialize(new {operation,entity_ids=new[]{entity.Id.Value},x=20,y=0,x2=40,y2=20,distance=3,second_distance=5,all_corners=whole});
        var response=await new CadWorkspaceToolExecutor(workspace).ExecuteAsync(new AiToolCall("corner","edit_curves",args),CancellationToken.None);
        using var json=JsonDocument.Parse(response); Assert.True(json.RootElement.GetProperty("success").GetBoolean(),response);
        var result=Assert.IsAssignableFrom<Curve>(Assert.Single(vm.CadEditor.Document.Entities.Values,e=>!e.IsErased));
        Assert.True(result.IsClosed); if(operation=="fillet") Assert.IsType<CadCompositePath>(result); else Assert.IsType<CadPolyline>(result);
        var file=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".d2cad");
        try
        {
            var storage=new Direct2dCad.IO.CadDocumentStorage(); storage.Save(vm.CadEditor.Document,file);
            var loaded=Assert.Single(storage.Load(file).Entities.Values,e=>!e.IsErased);
            Assert.Equal(result.GetType(),loaded.GetType()); Assert.Equal(result.Bounds,loaded.Bounds);
        }
        finally { File.Delete(file); }
        vm.Undo(); Assert.False(entity.IsErased); vm.Redo(); Assert.Single(vm.CadEditor.Document.Entities.Values,e=>!e.IsErased);
    }
}
