using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Editor.Tests;

public sealed class CadSelectionAvailabilityCacheTests
{
    [Fact]
    public void GeometryAppearanceAndViewportChanges_ReuseAvailability()
    {
        var document = CadDocument.Create("Selection cache");
        var line = document.AddLine(CadPointD.Origin, new(10, 0));
        var editor = new CadEditor(document);
        editor.Selection.Add(line.Id);
        var cache = new CadSelectionAvailabilityCache();
        Assert.Equal(new(true, true, true), cache.Get(editor));
        var version = cache.Version;

        editor.SetLineGeometry(line.Id, new(1, 1), new(20, 20));
        editor.SetEntityLineWeight(line.Id, new CadLineWeight(2));
        editor.Viewport.SetView(20, new(100, 100));
        cache.Get(editor);
        Assert.Equal(version, cache.Version);
        editor.Undo();
        cache.Get(editor);
        Assert.Equal(version, cache.Version);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void LayerAccessAndUndo_InvalidateAvailability(bool locked, bool frozen)
    {
        var document = CadDocument.Create("Access cache");
        var line = document.AddLine(CadPointD.Origin, new(10, 0));
        var editor = new CadEditor(document);
        editor.Selection.Add(line.Id);
        var cache = new CadSelectionAvailabilityCache();
        Assert.True(cache.Get(editor).CanCreateBlock);
        var version = cache.Version;

        editor.SetLayerState(line.LayerId, true, locked, frozen);
        Assert.Equal(new(true, false, false), cache.Get(editor));
        Assert.True(cache.Version > version);
        editor.Undo();
        Assert.Equal(new(true, true, true), cache.Get(editor));
        editor.DeleteEntity(line.Id);
        Assert.False(cache.Get(editor).CanDelete);
        editor.Undo();
        Assert.True(cache.Get(editor).CanDelete);
    }

    [Fact]
    public void SelectionOwnerAndEditorReplacement_InvalidateAvailability()
    {
        var document = CadDocument.Create("Owner cache");
        var line = document.AddLine(CadPointD.Origin, new(10, 0));
        var editor = new CadEditor(document);
        var cache = new CadSelectionAvailabilityCache();
        Assert.Equal(new(false, false, false), cache.Get(editor));
        editor.Selection.Add(line.Id);
        Assert.True(cache.Get(editor).CanCreateBlock);
        editor.ActiveOwnerBlockId = document.GetLayout(LayoutId.Default).PaperSpaceBlockId;
        Assert.Equal(new(true, true, false), cache.Get(editor));
        editor.Selection.Clear();
        Assert.False(cache.Get(editor).HasSelection);
        var replacement = new CadEditor(document);
        replacement.Selection.Add(line.Id);
        Assert.True(cache.Get(replacement).CanCreateBlock);
    }
}
