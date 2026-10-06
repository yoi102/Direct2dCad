using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Commands;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Tools;

namespace Direct2dCad.ViewModels.Tests;

public sealed class MilestoneToolContractTests
{
    [Fact]public async Task DxfToolsShareLossAndOverwriteProtectionAndImportOnlyAfterSuccess()
    {
        using var w=new ToolExecutionWorkspace();var vm=w.CreateDocument("exchange").GetViewModel();
        vm.CadEditor.AddLine(default,new(100,0));var e=new CadWorkspaceToolExecutor(w);var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".dxf");
        try
        {
            await ToolExecutionWorkspace.Execute(e,"export_dxf",new{file_path=path});
            await ToolExecutionWorkspace.Execute(e,"export_dxf",new{file_path=path},success:false);
            var result=await ToolExecutionWorkspace.Execute(e,"open_dxf",new{file_path=path});Assert.Equal(1,result.GetProperty("result").GetProperty("imported").GetInt32());Assert.Equal(2,w.GetDocuments().Count);
            var id=w.GetActiveDocument()!.DocumentId;var target=w.GetActiveDocument()!.GetViewModel();
            target.CadEditor.Document.AddDimension(new(CadDimensionKind.Aligned,[new(default),new(new(100,0))],new(50,10),new()));
            var losses=await ToolExecutionWorkspace.Execute(e,"export_dxf",new{file_path=path,document_id=id,overwrite=true});Assert.False(losses.GetProperty("result").GetProperty("exported").GetBoolean());
        }
        finally{File.Delete(path);}
    }
    [Fact]public async Task DimensionToolUsesSourceAssociationAndPaginationRejectsVersionChanges()
    {
        using var w=new ToolExecutionWorkspace();var vm=w.CreateDocument("annotations").GetViewModel();var line=vm.CadEditor.AddLine(default,new(100,0));var e=new CadWorkspaceToolExecutor(w);
        var created=await ToolExecutionWorkspace.Execute(e,"add_dimension",new{kind="Aligned",source_entity_id=line.Value,x=50,y=20,style="Fine"});
        var dimension=Assert.Single(vm.CadEditor.Document.Entities.Values.OfType<CadDimension>());Assert.Equal(100,dimension.Measurement);Assert.Equal(.13,dimension.Definition.Style.LineWeight);
        var first=await ToolExecutionWorkspace.Execute(e,"list_entities",new{limit=1});var version=first.GetProperty("result").GetProperty("document_version").GetInt64();
        await ToolExecutionWorkspace.Execute(e,"list_entities",new{limit=1,offset=1,expected_document_version=version});
        vm.CadEditor.SetLineGeometry(line,default,new(125,0));Assert.Equal(125,dimension.Measurement);
        await ToolExecutionWorkspace.Execute(e,"list_entities",new{limit=1,offset=1,expected_document_version=version},success:false);
    }
    [Fact]public async Task SnappingSettingsSurviveUnrelatedToolChangesAndUndo()
    {
        using var w=new ToolExecutionWorkspace();var vm=w.CreateDocument("snap").GetViewModel();var e=new CadWorkspaceToolExecutor(w);
        await ToolExecutionWorkspace.Execute(e,"set_view_settings",new{grid_snap_enabled=true,object_snap_enabled=false,polar_enabled=true,polar_increment_degrees=30,object_snap_modes=new[]{"Endpoint","Tangent"}});
        var expected=vm.CadEditor.Document.ViewSettings.Snap;
        e=new CadWorkspaceToolExecutor(w);
        await ToolExecutionWorkspace.Execute(e,"set_view_settings",new{background_color="#AABBCC"});Assert.Equal(expected,vm.CadEditor.Document.ViewSettings.Snap);vm.Undo();Assert.Equal(expected,vm.CadEditor.Document.ViewSettings.Snap);
        var result=await ToolExecutionWorkspace.Execute(e,"get_view_settings",new{});Assert.Equal(30,result.GetProperty("result").GetProperty("snapping").GetProperty("polar_increment_degrees").GetDouble());
        await ToolExecutionWorkspace.Execute(e,"set_view_settings",new{snap_screen_tolerance=100},success:false);Assert.Equal(expected,vm.CadEditor.Document.ViewSettings.Snap);
    }
    [Fact]public async Task PointerAndToolUseTheSameCurvePlanAndIdentityRule()
    {
        using var w=new ToolExecutionWorkspace();var vm=w.CreateDocument("edit").GetViewModel();var id=vm.CadEditor.AddLine(default,new(20,0));var e=new CadWorkspaceToolExecutor(w);
        var expected=CadCurveEditing.Offset(vm.CadEditor.Document.GetEntity(id),2,new(10,10)).Creations[0].Shape.Segments[0];
        var output=await ToolExecutionWorkspace.Execute(e,"edit_curves",new{operation="offset",entity_ids=new[]{id.Value},distance=2,x=10,y=10});
        var resultId=new Direct2dCad.Db.EntityId(output.GetProperty("result").GetProperty("result_entity_ids")[0].GetInt64());var result=Assert.IsType<CadLine>(vm.CadEditor.Document.GetEntity(resultId));Assert.Equal(expected.Start,result.Start);Assert.Equal(expected.End,result.End);vm.Undo();Assert.True(result.IsErased);vm.Redo();Assert.False(result.IsErased);
    }
    [Fact]public async Task CircularProjectionAndTangencyAreAnalytic()
    {
        using var w=new ToolExecutionWorkspace();var vm=w.CreateDocument("measure").GetViewModel();var circle=vm.CadEditor.AddCircle(default,10);var line=vm.CadEditor.AddLine(new(-20,10),new(20,10));var e=new CadWorkspaceToolExecutor(w);
        var projection=await ToolExecutionWorkspace.Execute(e,"measure_geometry",new{operation="nearest_point",entity_ids=new[]{circle.Value},point=new{x=0,y=20}});var result=projection.GetProperty("result");Assert.False(result.GetProperty("approximate").GetBoolean());Assert.Equal(10,result.GetProperty("distance_millimeters").GetDouble(),8);
        var crossings=await ToolExecutionWorkspace.Execute(e,"measure_geometry",new{operation="intersections",entity_ids=new[]{circle.Value,line.Value}});Assert.Single(crossings.GetProperty("result").GetProperty("points").EnumerateArray());
    }
    [Fact]public async Task QueryRejectsMutationsAndClosedDocumentsDuringCapture()
    {
        using var w=new ToolExecutionWorkspace();var doc=w.CreateDocument("query");var vm=doc.GetViewModel();
        for(var i=0;i<3000;i++)vm.CadEditor.Document.AddLine(new(i,0),new(i,10));var e=new CadWorkspaceToolExecutor(w);
        var query=e.ExecuteAsync(new AiToolCall("query","list_entities","{}"),default);vm.CadEditor.AddLine(default,new(10,20));
        using(var result=JsonDocument.Parse(await query))Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        var closed=e.ExecuteAsync(new AiToolCall("closed","list_entities","{}"),default);await w.CloseDocumentAsync(doc.DocumentId);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await closed);
    }
    [Fact]public async Task QueryCancellationDoesNotPublishAPartialPage()
    {
        using var w=new ToolExecutionWorkspace();var vm=w.CreateDocument("cancel query").GetViewModel();for(var i=0;i<3000;i++)vm.CadEditor.Document.AddLine(new(i,0),new(i,10));var e=new CadWorkspaceToolExecutor(w);using var cancellation=new CancellationTokenSource();
        var query=e.ExecuteAsync(new AiToolCall("cancel","list_entities","{}"),cancellation.Token);cancellation.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await query);Assert.False(vm.IsDisposed);
    }
}
