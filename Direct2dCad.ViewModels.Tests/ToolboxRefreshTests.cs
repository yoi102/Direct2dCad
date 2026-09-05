using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;

namespace Direct2dCad.ViewModels.Tests;

public sealed class ToolboxRefreshTests
{
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
