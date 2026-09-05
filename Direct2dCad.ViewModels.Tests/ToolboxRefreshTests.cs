using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;

namespace Direct2dCad.ViewModels.Tests;

public sealed class ToolboxRefreshTests
{
    [Fact]
    public void SearchBatchMovePublishesOneCollectionNotification()
    {
        using var context = new CadToolboxTestContext();
        AddSelectedLines(context, 1000);
        var editor = context.Document.CadEditor;
        context.Search.Attach(context.Document);
        context.Search.SelectedResult = context.Search.Results[10];
        var selectedId = context.Search.SelectedResult.EntityId;
        var notifications = 0;
        context.Search.Results.CollectionChanged += (_, _) => notifications++;
        editor.MoveEntities(editor.Document.Entities.Keys, new CadVectorD(10, 0));
        context.Publish();
        Assert.Equal(1, notifications);
        Assert.Equal(selectedId, context.Search.SelectedResult!.EntityId);
        Assert.All(context.Search.Results, item =>
            Assert.Equal(editor.Document.GetEntity(item.EntityId).Bounds, item.Bounds));
    }

    [Fact]
    public void SearchGeometryUpdatesOnlyChangedRowAndPreservesSelectionAndFilters()
    {
        using var context = new CadToolboxTestContext();
        var editor = context.Document.CadEditor;
        var first = editor.AddCircle(CadPointD.Origin, 5);
        var second = editor.AddCircle(new CadPointD(20, 0), 5);
        context.Search.Attach(context.Document);
        var unchanged = context.Search.Results.Single(item => item.EntityId == second);
        context.Search.SelectedResult = context.Search.Results.Single(item => item.EntityId == first);
        var filters = context.Search.LayerFilters.ToArray();
        var notifications = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        context.Search.Results.CollectionChanged += (_, e) => notifications.Add(e.Action);
        editor.MoveEntities([first], new CadVectorD(10, 0));
        context.Publish();
        Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Replace], notifications);
        Assert.Same(unchanged, context.Search.Results.Single(item => item.EntityId == second));
        Assert.Equal(first, context.Search.SelectedResult!.EntityId);
        Assert.Equal(editor.Document.GetEntity(first).Bounds, context.Search.SelectedResult.Bounds);
        Assert.Same(filters[0], context.Search.LayerFilters[0]);
        editor.Undo();
        context.Publish();
        Assert.Equal(editor.Document.GetEntity(first).Bounds, context.Search.SelectedResult.Bounds);
    }

    [Fact]
    public void SearchFallsBackAfterMissedVersionsAndReevaluatesRenameFilter()
    {
        using var context = new CadToolboxTestContext();
        var editor = context.Document.CadEditor;
        var first = editor.AddCircle(CadPointD.Origin, 5);
        var second = editor.AddCircle(new CadPointD(20, 0), 5);
        context.Search.Attach(context.Document);
        editor.MoveEntities([first], new CadVectorD(10, 0));
        editor.MoveEntities([second], new CadVectorD(10, 0));
        context.Publish();
        Assert.All(context.Search.Results, item => Assert.Equal(editor.Document.GetEntity(item.EntityId).Bounds, item.Bounds));
        context.Search.SearchText = "Renamed";
        Assert.Empty(context.Search.Results);
        editor.RenameEntity(first, "Renamed");
        context.Publish();
        Assert.Equal(first, Assert.Single(context.Search.Results).EntityId);
        editor.Undo();
        context.Publish();
        Assert.Empty(context.Search.Results);
    }

    [Fact]
    public void MultiSelectionSkipsUnselectedEditsAndZIndexDoesNotRebuildFillOptions()
    {
        using var context = new CadToolboxTestContext();
        AddSelectedLines(context, 1000);
        var editor = context.Document.CadEditor;
        var other = editor.AddCircle(CadPointD.Origin, 5);
        context.Properties.Attach(context.Document);
        var panel = Assert.IsType<MultiEntityPropertyViewModel>(context.Properties.Entity);
        var notifications = new List<string?>();
        panel.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        editor.MoveEntities([other], new CadVectorD(10, 0));
        context.Publish();
        Assert.Empty(notifications);
        var layers = panel.LayerOptions;
        editor.SetEntityZIndex(editor.Selection.EntityIds, 7);
        context.Publish();
        Assert.Equal(7, panel.ZIndex);
        Assert.Same(layers, panel.LayerOptions);
        Assert.DoesNotContain(nameof(panel.FillStyleOptions), notifications);
    }

    [Fact]
    public void GeometryChangesDoNotRefreshLayerPanelButMembershipChangesDo()
    {
        using var context = new CadToolboxTestContext();
        var editor = context.Document.CadEditor;
        var id = editor.AddCircle(CadPointD.Origin, 5);
        context.Layers.Attach(context.Document);
        var notifications = 0;
        context.Layers.PropertyChanged += (_, _) => notifications++;
        editor.MoveEntities([id], new CadVectorD(10, 10));
        context.Publish();
        Assert.Equal(0, notifications);
        editor.Undo();
        context.Publish();
        Assert.Equal(0, notifications);
        editor.DeleteEntities([id]);
        context.Publish();
        Assert.True(notifications > 0);
    }

    [Fact]
    public void MultiSelectionPreservesCommonAndMixedFillValuesWithoutTemporaryArrays()
    {
        using var context = new CadToolboxTestContext();
        var editor = context.Document.CadEditor;
        var red = editor.Document.CreateSolidFillStyle("Red", CadColor.Red);
        var blue = editor.Document.CreateSolidFillStyle("Blue", CadColor.Blue);
        var first = editor.Document.AddCircle(CadPointD.Origin, 5, fillStyleId: red);
        var second = editor.Document.AddCircle(new CadPointD(20, 0), 5, fillStyleId: blue);
        context.Document.SelectEntities([first.Id, second.Id]);
        context.Properties.Attach(context.Document);
        var panel = Assert.IsType<MultiEntityPropertyViewModel>(context.Properties.Entity);
        Assert.True(panel.SupportsFill);
        Assert.True(panel.HasMixedFillColor);
        Assert.True(panel.Matches([second.Id, first.Id, second.Id]));
        editor.SetEntityFillStyle([second.Id], red);
        context.Publish();
        Assert.False(panel.HasMixedFillColor);
        Assert.False(panel.HasMixedFillStyle);
        Assert.Equal(CadColor.Red, panel.FillColor);
        editor.Undo();
        context.Publish();
        Assert.True(panel.HasMixedFillColor);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void SwitchingDocumentsWithMatchingEntityIdsDoesNotEditTheOldDocument(int count)
    {
        using var first = new CadToolboxTestContext();
        using var second = new CadToolboxTestContext();
        AddSelectedLines(first, count);
        AddSelectedLines(second, count);
        first.Properties.Attach(first.Document);
        var original = first.Properties.Entity;
        first.Properties.Attach(second.Document);
        Assert.NotSame(original, first.Properties.Entity);

        if (count == 1)
            Assert.IsType<LinePropertyViewModel>(first.Properties.Entity).EntityName = "Second document";
        else
            Assert.IsType<MultiEntityPropertyViewModel>(first.Properties.Entity).ZIndex = 12;

        Assert.All(first.Document.CadEditor.Document.Entities.Values, entity =>
        {
            Assert.NotEqual("Second document", entity.Name);
            Assert.Equal(0, entity.ZIndex);
        });
        Assert.All(second.Document.CadEditor.Document.Entities.Values, entity =>
            Assert.True(count == 1 ? entity.Name == "Second document" : entity.ZIndex == 12));
    }

    [Fact]
    public void UnrelatedMessagesDoNotRefreshMultiSelectionProperties()
    {
        using var context = new CadToolboxTestContext();
        AddSelectedLines(context, 1000);
        context.Properties.Attach(context.Document);
        var panel = Assert.IsType<MultiEntityPropertyViewModel>(context.Properties.Entity);
        var notifications = 0;
        panel.PropertyChanged += (_, _) => notifications++;
        for (var i = 0; i < 100; i++)
            context.Publish();
        context.Properties.Attach(context.Document);
        Assert.Equal(0, notifications);
        Assert.Same(panel, context.Properties.Entity);

        context.Document.CadEditor.SetEntityZIndex(context.Document.CadEditor.Selection.EntityIds, 7);
        context.Publish();
        Assert.Equal(7, panel.ZIndex);
        Assert.True(notifications > 0);
        context.Document.CadEditor.Undo();
        context.Publish();
        Assert.Equal(0, panel.ZIndex);
    }

    [Fact]
    public void LayerUpdatesReuseItemsWithoutResettingCollectionUnlessOrderChanges()
    {
        using var context = new CadToolboxTestContext();
        var editor = context.Document.CadEditor;
        context.Layers.Attach(context.Document);
        var item = Assert.Single(context.Layers.Layers);
        var collectionChanges = 0;
        context.Layers.Layers.CollectionChanged += (_, _) => collectionChanges++;
        for (var i = 0; i < 100; i++)
            context.Publish();
        editor.Execute(new RenameLayerCommand(LayerId.Default, "Renamed"));
        context.Publish();
        Assert.Same(item, Assert.Single(context.Layers.Layers));
        Assert.Equal("Renamed", item.Name);
        Assert.Equal(0, collectionChanges);
        editor.Execute(new AddLineCommand(CadPointD.Origin, new CadPointD(10, 0)));
        context.Publish();
        Assert.Equal(1, item.EntityCount);
        editor.Undo();
        context.Publish();
        Assert.Equal(0, item.EntityCount);
        Assert.Equal(0, collectionChanges);
    }

    [Fact]
    public void LayerPriorityAndUndoKeepUiOrderAndSelection()
    {
        using var context = new CadToolboxTestContext();
        var editor = context.Document.CadEditor;
        var second = editor.Document.CreateLayer("Second", CadColor.Red, CadLineWeight.Default);
        context.Layers.Attach(context.Document);
        var defaultItem = context.Layers.Layers.Single(x => x.LayerId == LayerId.Default);
        context.Layers.SelectedLayer = defaultItem;
        editor.SetLayerDrawingPriorities(new Dictionary<LayerId, int> { [LayerId.Default] = 5, [second] = 1 });
        context.Publish();
        Assert.Same(defaultItem, context.Layers.Layers[0]);
        Assert.Same(defaultItem, context.Layers.SelectedLayer);
        editor.Undo();
        context.Publish();
        Assert.Same(defaultItem, context.Layers.SelectedLayer);
        Assert.Equal(editor.Document.Layers.Values
            .OrderByDescending(x => editor.Document.DocumentSettings.LayerDrawingPriority.GetPriority(x.Id))
            .ThenByDescending(x => x.Id.Value).Select(x => x.Id), context.Layers.Layers.Select(x => x.LayerId));
    }

    private static void AddSelectedLines(CadToolboxTestContext context, int count)
    {
        var editor = context.Document.CadEditor;
        editor.ExecuteRange(Enumerable.Range(0, count).Select(i =>
            new AddLineCommand(new CadPointD(i, 0), new CadPointD(i, 10))));
        context.Document.SelectEntities(editor.Document.Entities.Keys);
    }
}
