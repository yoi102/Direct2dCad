using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Tests;

public sealed class SelectionPruningTests
{
    [Fact]
    public void EditsPreserveSelection_DeletedOrFrozenEntitiesArePruned()
    {
        using var context = new CadToolboxTestContext();
        context.Document.AttachRenderResources();
        var editor = context.Document.CadEditor;
        var first = editor.AddLine(CadPointD.Origin, new(10, 0));
        var second = editor.AddLine(new(20, 0), new(30, 0));
        editor.Selection.Replace([first, second]);
        var version = editor.Selection.Version;
        editor.SetLineGeometry(first, new(0, 5), new(10, 5));
        editor.SetEntityColor(first, CadColor.Red);
        Assert.Equal(version, editor.Selection.Version);
        editor.DeleteEntity(first);
        Assert.Equal(second, Assert.Single(editor.Selection.EntityIds));
        editor.SetLayerState(LayerId.Default, true, true, false);
        Assert.Equal(second, Assert.Single(editor.Selection.EntityIds));
        editor.SetLayerState(LayerId.Default, true, false, true);
        Assert.Empty(editor.Selection.EntityIds);
    }
}
