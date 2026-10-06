using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.CommandLine;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Tools;

namespace Direct2dCad.ViewModels.Tests;

public sealed class InputReliabilityTests
{
    [Theory]
    [InlineData(CadUnit.Millimeter, "1.25,2.75", 1.25, 2.75)]
    [InlineData(CadUnit.Inch, "1,2", 25.4, 50.8)]
    [InlineData(CadUnit.Millimeter, "@1.25,2.75", 1.25, 2.75)]
    [InlineData(CadUnit.Millimeter, "@1.25<0", 1.25, 0)]
    public void ExplicitCoordinatesSurviveSubmissionHistoryAndStorage(CadUnit unit, string input, double x, double y)
    {
        using var context = new CadToolboxTestContext();
        var model = context.Document;
        model.SetDocumentUnit(unit);
        var commands = new CadCommandLineService();
        foreach (var command in new[] { "LINE", "0,0", input })
            Assert.True(commands.Execute(command, model).Success);
        var line = Assert.IsType<CadLine>(Assert.Single(model.CadEditor.Document.Entities.Values));
        Assert.Equal(x, line.End.X, 9);
        Assert.Equal(y, line.End.Y, 9);
        model.Undo();
        Assert.True(line.IsErased);
        model.Redo();
        Assert.Equal(new CadPointD(x, y), line.End);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".d2cad");
        try
        {
            var storage = new Direct2dCad.IO.CadDocumentStorage();
            storage.Save(model.CadEditor.Document, path);
            var loaded = Assert.IsType<CadLine>(Assert.Single(storage.Load(path).Entities.Values));
            Assert.Equal(line.End, loaded.End);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("measure_geometry", "nearest_point")]
    [InlineData("measure_geometry", "project_point")]
    public async Task PublicMeasurementAcceptsZeroDistanceAtOrigin(string tool, string? operation)
    {
        using var workspace = new ToolExecutionWorkspace();
        var model = workspace.CreateDocument("Origin").GetViewModel();
        var line = model.CadEditor.Document.AddLine(new(-10, 0), new(10, 0));
        var input = JsonSerializer.Serialize(new { operation, entity_ids = new[] { line.Id.Value }, point = new { x = 0, y = 0 } });
        var output = await new CadWorkspaceToolExecutor(workspace).ExecuteAsync(new AiToolCall("origin", tool, input), CancellationToken.None);
        using var json = JsonDocument.Parse(output);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean(), output);
        Assert.Equal(0, json.RootElement.GetProperty("result").GetProperty("distance_millimeters").GetDouble());
    }

    [Fact]
    public void DisablingEveryCatalogTypeExcludesCompositePathsFromSelectAll()
    {
        using var context = new CadToolboxTestContext();
        foreach (var kind in Enum.GetValues<TestEntityKind>())
            CadEntityTestCases.Add(context.Document.CadEditor.Document, kind);
        context.Document.ApplyDisabledSelectionEntityTypeKeys(CadSelectionEntityTypeCatalog.All.Select(x => x.Key));
        context.Document.SelectAllEntities();
        Assert.Empty(context.Document.CadEditor.Selection.EntityIds);
        context.Document.ApplyDisabledSelectionEntityTypeKeys([]);
        context.Document.SelectAllEntities();
        Assert.Contains(context.Document.CadEditor.Selection.EntityIds,
            id => context.Document.CadEditor.Document.GetEntity(id) is CadCompositePath);
    }
}
