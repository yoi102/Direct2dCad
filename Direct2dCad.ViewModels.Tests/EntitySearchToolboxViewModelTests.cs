using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor.Commands;
using Direct2dCad.ViewModels.Toolboxes;

namespace Direct2dCad.ViewModels.Tests;

public sealed class EntitySearchToolboxViewModelTests
{
    [Fact]
    public void InteractionMessagesAndRepeatedAttachDoNotRebuildUnchangedResults()
    {
        using var fixture = new CadToolboxTestContext();
        var editor = fixture.Document.CadEditor;
        editor.Execute(new AddLineCommand(CadPointD.Origin, new CadPointD(10, 0)));
        fixture.Search.Attach(fixture.Document);
        var original = Assert.Single(fixture.Search.Results);
        var notifications = 0;
        fixture.Search.Results.CollectionChanged += (_, _) => notifications++;

        editor.Execute(new PanViewportCommand(new CadVectorD(20, 10)));
        for (var i = 0; i < 100; i++)
            fixture.Publish();
        fixture.Search.Attach(fixture.Document);
        fixture.Search.SelectedResult = original;

        Assert.Same(original, Assert.Single(fixture.Search.Results));
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void DetachedEditorChangesAndUndoRedoRefreshOncePerVersion()
    {
        using var fixture = new CadToolboxTestContext();
        fixture.Search.Attach(fixture.Document);
        var notifications = 0;
        fixture.Search.Results.CollectionChanged += (_, _) => notifications++;
        var editor = fixture.Document.CadEditor;

        editor.Execute(new AddLineCommand(CadPointD.Origin, new CadPointD(10, 0)));
        fixture.Publish();
        fixture.Publish();
        Assert.Single(fixture.Search.Results);
        Assert.Equal(1, notifications);

        editor.Undo();
        fixture.Publish();
        Assert.Empty(fixture.Search.Results);
        editor.Redo();
        fixture.Search.Attach(fixture.Document);
        Assert.Single(fixture.Search.Results);
        Assert.Equal(3, notifications);
    }

    [Fact]
    public void OwnerChangesRefreshCurrentSpaceButNotEntireDocument()
    {
        using var fixture = new CadToolboxTestContext();
        var editor = fixture.Document.CadEditor;
        editor.Execute(new AddLineCommand(CadPointD.Origin, new CadPointD(10, 0)));
        fixture.Search.Attach(fixture.Document);
        editor.ActiveOwnerBlockId = BlockId.PaperSpace;
        fixture.Publish();
        Assert.Empty(fixture.Search.Results);

        fixture.Search.SearchScope = CadEntitySearchScope.EntireDocument;
        var original = Assert.Single(fixture.Search.Results);
        editor.ActiveOwnerBlockId = BlockId.ModelSpace;
        fixture.Publish();
        Assert.Same(original, Assert.Single(fixture.Search.Results));
    }

    [Fact]
    public void MetadataAndLayerRenamesRefreshDisplayedValuesAndPreserveFilters()
    {
        using var fixture = new CadToolboxTestContext();
        var editor = fixture.Document.CadEditor;
        var add = new AddLineCommand(CadPointD.Origin, new CadPointD(10, 0));
        editor.Execute(add);
        fixture.Search.Attach(fixture.Document);
        fixture.Search.SelectedLayerFilter = fixture.Search.LayerFilters.Single(x => x.LayerId == LayerId.Default);
        fixture.Search.SelectedTypeFilter = fixture.Search.TypeFilters.Single(x => x.TypeKey == "Line");

        editor.Execute(new RenameEntityCommand(add.CreatedEntityId!.Value, "Renamed"));
        editor.Execute(new RenameLayerCommand(LayerId.Default, "New layer name"));
        fixture.Publish();

        var result = Assert.Single(fixture.Search.Results);
        Assert.Equal("Renamed", result.Name);
        Assert.Equal("New layer name", result.LayerName);
        Assert.Equal(LayerId.Default, fixture.Search.SelectedLayerFilter?.LayerId);
        Assert.Equal("Line", fixture.Search.SelectedTypeFilter?.TypeKey);
    }

    [Fact]
    public void SwitchingDocumentsWithEqualVersionsStillRefreshes()
    {
        using var first = new CadToolboxTestContext();
        using var second = new CadToolboxTestContext();
        first.Document.CadEditor.Execute(new AddLineCommand(CadPointD.Origin, new CadPointD(10, 0)));
        second.Document.CadEditor.Execute(new AddCircleCommand(CadPointD.Origin, 5));
        Assert.Equal(first.Document.CadEditor.DocumentChangeVersion, second.Document.CadEditor.DocumentChangeVersion);
        first.Search.Attach(first.Document);
        Assert.Equal("Line", Assert.Single(first.Search.Results).EntityType);
        first.Search.Attach(second.Document);
        Assert.Equal("Circle", Assert.Single(first.Search.Results).EntityType);
    }

    [Fact]
    public void LeavingLayoutDuringPanEndsGestureAndRefreshesSearchScope()
    {
        using var fixture = new CadToolboxTestContext();
        var document = fixture.Document;
        var editor = document.CadEditor;
        editor.Execute(new AddLineCommand(CadPointD.Origin, new CadPointD(10, 0)));
        var viewport = editor.Document.GetLayout(LayoutId.Default).Viewports[0];
        document.ActivateLayout(LayoutId.Default);
        fixture.Search.Attach(document);
        Assert.Empty(fixture.Search.Results);
        document.ActivateLayoutViewport(viewport.Id);
        Assert.Single(fixture.Search.Results);
        var originalCenter = viewport.ModelCenter;
        document.PointerDown(new CadPointD(50, 50), CadCanvasPointerButton.Right, forcePan: false);
        document.PointerMove(new CadPointD(70, 60));
        Assert.True(document.IsPanning);
        var movedCenter = viewport.ModelCenter;
        Assert.NotEqual(originalCenter, movedCenter);

        document.ActivateModelSpace();

        Assert.False(document.IsPanning);
        Assert.Single(fixture.Search.Results);
        Assert.Equal(movedCenter, viewport.ModelCenter);
        document.PointerMove(new CadPointD(90, 80));
        Assert.Equal(movedCenter, viewport.ModelCenter);
        editor.Undo();
        Assert.Equal(originalCenter, viewport.ModelCenter);
    }

    [Fact]
    public void ManualRefreshAndSearchStillRebuildAndDetachClearsResults()
    {
        using var fixture = new CadToolboxTestContext();
        var editor = fixture.Document.CadEditor;
        editor.Execute(new AddLineCommand(CadPointD.Origin, new CadPointD(10, 0)));
        fixture.Search.Attach(fixture.Document);
        var original = Assert.Single(fixture.Search.Results);
        fixture.Search.RefreshCommand.Execute(null);
        Assert.NotSame(original, Assert.Single(fixture.Search.Results));
        fixture.Search.SearchText = "No matching entity";
        Assert.Empty(fixture.Search.Results);
        fixture.Search.SearchText = "Line";
        Assert.Single(fixture.Search.Results);
        fixture.Search.Attach(null);
        fixture.Publish();
        Assert.Empty(fixture.Search.Results);
        Assert.False(fixture.Search.HasDocument);
    }

}
