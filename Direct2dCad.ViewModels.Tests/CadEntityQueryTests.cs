using System.Text.Json;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Tools;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadEntityQueryTests
{
    [Fact]
    public void Statistics_ReturnCompleteCurrentSpaceAndDocumentTypeInventories()
    {
        var document = CreateMixedDocument();

        var result = CadEntityQuery.CreateStatistics(
            document,
            BlockId.ModelSpace,
            new HashSet<EntityId>(),
            new CadEntityQueryOptions(
                CadEntityQuery.CurrentSpaceScope,
                null,
                null,
                SelectedOnly: false));
        var json = JsonSerializer.SerializeToElement(result);

        var currentSpace = json.GetProperty("current_space");
        var documentSummary = json.GetProperty("document");
        Assert.Equal(63, currentSpace.GetProperty("entity_count").GetInt32());
        Assert.Equal(64, documentSummary.GetProperty("entity_count").GetInt32());
        Assert.Equal(60, TypeCount(currentSpace, "Circle"));
        Assert.Equal(2, TypeCount(currentSpace, "Line"));
        Assert.Equal(1, TypeCount(currentSpace, "Text"));
        Assert.Equal(2, documentSummary.GetProperty("owner_space_counts").GetArrayLength());
    }

    [Fact]
    public void FilteredPage_StillReportsUnfilteredScopeCountsAndPagination()
    {
        var document = CreateMixedDocument();
        var result = CadEntityQuery.CreatePage(
            document,
            BlockId.ModelSpace,
            new HashSet<EntityId>(),
            new CadEntityQueryOptions(
                CadEntityQuery.CurrentSpaceScope,
                "Circle",
                null,
                SelectedOnly: false,
                Offset: 0,
                Limit: 50));
        var json = JsonSerializer.SerializeToElement(result);

        Assert.Equal(63, json.GetProperty("scope_entity_count").GetInt32());
        Assert.Equal(60, json.GetProperty("total_matches").GetInt32());
        Assert.Equal(50, json.GetProperty("returned_count").GetInt32());
        Assert.True(json.GetProperty("has_more").GetBoolean());
        Assert.Equal(3, json.GetProperty("scope_type_counts").GetArrayLength());
        Assert.Equal("Circle", json.GetProperty("matching_type_counts")[0].GetProperty("type").GetString());
        Assert.Equal("Circle", json.GetProperty("entities")[0].GetProperty("type").GetString());
    }

    [Fact]
    public void QueryToolSchemas_DistinguishStatisticsFromPagedDetails()
    {
        var tools = CadDocumentToolExecutor.ToolDefinitions.ToDictionary(tool => tool.Name);
        var statistics = tools["get_entity_statistics"].Parameters.GetProperty("properties");
        var list = tools["list_entities"].Parameters.GetProperty("properties");

        Assert.True(statistics.TryGetProperty("scope", out _));
        Assert.True(statistics.TryGetProperty("selected_only", out _));
        Assert.False(statistics.TryGetProperty("limit", out _));
        Assert.True(list.TryGetProperty("scope", out _));
        Assert.True(list.TryGetProperty("types", out _));
        Assert.True(list.TryGetProperty("name_contains", out _));
        Assert.True(list.TryGetProperty("fill_kind", out _));
        Assert.True(list.TryGetProperty("min_radius", out _));
        Assert.True(list.TryGetProperty("bounds", out _));
        Assert.True(list.TryGetProperty("sort_by", out _));
        Assert.True(list.TryGetProperty("offset", out _));
        Assert.True(list.TryGetProperty("limit", out _));
    }

    [Fact]
    public void StructuredFilters_CombineTypeNameStateFillGeometryAndSpatialFeatures()
    {
        var document = CadDocument.Create("Features");
        var fillStyleId = document.CreateSolidFillStyle("Signal Fill", CadColor.Red);
        var target = document.AddCircle(
            new CadPointD(10, 10),
            4,
            fillStyleId: fillStyleId,
            name: "Target Circle");
        document.AddCircle(new CadPointD(30, 30), 1, name: "Small Circle");
        document.AddLine(CadPointD.Origin, new CadPointD(20, 0), name: "Target Line");

        var result = CadEntityQuery.CreatePage(
            document,
            BlockId.ModelSpace,
            new HashSet<EntityId> { target.Id },
            new CadEntityQueryOptions(
                CadEntityQuery.CurrentSpaceScope,
                null,
                null,
                SelectedOnly: true,
                Types: ["Circle", "Arc"],
                NameContains: "target",
                IsVisible: true,
                IsClosed: true,
                HasFill: true,
                FillKind: "solid",
                MinRadius: 3,
                MaxRadius: 5,
                Bounds: CadRectD.FromLTRB(5, 5, 15, 15),
                SpatialRelation: "contained"));
        var json = JsonSerializer.SerializeToElement(result);

        Assert.Equal(1, json.GetProperty("total_matches").GetInt32());
        var entity = Assert.Single(json.GetProperty("entities").EnumerateArray());
        Assert.Equal(target.Id.Value, entity.GetProperty("id").GetInt64());
        Assert.Equal("solid", entity.GetProperty("fill_kind").GetString());
        Assert.Equal(4, entity.GetProperty("characteristics").GetProperty("radius").GetDouble());
        Assert.True(entity.GetProperty("characteristics").GetProperty("closed").GetBoolean());
    }

    [Fact]
    public void StructuredFilters_QueryTextContentDimensionsAndSortOrder()
    {
        var document = CadDocument.Create("Text");
        document.AddText("Door A-100", new CadPointD(0, 0), 2, name: "Note 2");
        document.AddText("Door A-200", new CadPointD(10, 0), 4, name: "Note 1");
        document.AddText("Window B-100", new CadPointD(20, 0), 3, name: "Other");

        var result = CadEntityQuery.CreatePage(
            document,
            BlockId.ModelSpace,
            new HashSet<EntityId>(),
            new CadEntityQueryOptions(
                CadEntityQuery.CurrentSpaceScope,
                "Text",
                null,
                SelectedOnly: false,
                TextContains: "door",
                MinHeight: 2,
                SortBy: "name",
                SortDescending: false));
        var json = JsonSerializer.SerializeToElement(result);
        var entities = json.GetProperty("entities").EnumerateArray().ToArray();

        Assert.Equal(2, json.GetProperty("total_matches").GetInt32());
        Assert.Equal("Note 1", entities[0].GetProperty("Name").GetString());
        Assert.Equal("Door A-200", entities[0].GetProperty("characteristics").GetProperty("text").GetString());
        Assert.Equal("Note 2", entities[1].GetProperty("Name").GetString());
    }

    [Fact]
    public void QueryProtocol_ParsesCombinedFiltersWithoutLosingPagination()
    {
        using var arguments = JsonDocument.Parse(
            """
            {
              "scope": "document",
              "types": ["Circle", "Arc"],
              "name_contains": "bearing",
              "fill_kind": "hatch",
              "min_radius": 2.5,
              "bounds": { "min_x": -10, "min_y": -5, "max_x": 20, "max_y": 15 },
              "spatial_relation": "intersects",
              "sort_by": "length",
              "sort_direction": "descending",
              "offset": 25,
              "limit": 75
            }
            """);

        var options = CadEntityQueryProtocol.Parse(arguments.RootElement, paged: true, maximumListedEntities: 200);

        Assert.Equal(CadEntityQuery.DocumentScope, options.Scope);
        Assert.Equal(["Circle", "Arc"], options.Types);
        Assert.Equal("bearing", options.NameContains);
        Assert.Equal("hatch", options.FillKind);
        Assert.Equal(2.5, options.MinRadius);
        Assert.Equal("intersects", options.SpatialRelation);
        Assert.Equal("length", options.SortBy);
        Assert.True(options.SortDescending);
        Assert.Equal(25, options.Offset);
        Assert.Equal(75, options.Limit);
    }

    private static CadDocument CreateMixedDocument()
    {
        var document = CadDocument.Create("Mixed");
        for (var index = 0; index < 60; index++)
            document.AddCircle(new CadPointD(index * 2, 0), 0.5);

        document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        document.AddLine(new CadPointD(0, 1), new CadPointD(10, 1));
        document.AddText("Label", new CadPointD(0, 2), 1);

        var blockId = document.CreateBlockDefinition("Detail", CadPointD.Origin);
        var blockLine = document.AddLine(new CadPointD(0, 3), new CadPointD(10, 3));
        document.MoveEntityToBlock(blockLine.Id, blockId);
        return document;
    }

    private static int TypeCount(JsonElement summary, string type) =>
        summary.GetProperty("type_counts")
            .EnumerateArray()
            .Single(item => item.GetProperty("type").GetString() == type)
            .GetProperty("count")
            .GetInt32();
}
