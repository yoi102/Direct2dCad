using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Interactions;

namespace Direct2dCad.ViewModels.Tests;

public sealed class ClipboardWorkflowContractTests
{
    [Theory]
    [MemberData(nameof(CadEntityTestCases.All), MemberType = typeof(CadEntityTestCases))]
    public void CrossDocumentPreviewPlacementAndRepeatedPastePreserveEntityState(TestEntityKind kind)
    {
        var source = CadDocument.Create("Source");
        var entity = CadEntityTestCases.Add(source, kind);
        entity.Rename("Original");
        entity.SetZIndex(42);
        entity.SetLineWeight(new CadLineWeight(0.7));
        var originalBounds = entity.Bounds;
        var sourceEditor = new CadEditor(source);
        sourceEditor.Selection.Replace([entity.Id]);
        var store = new CadClipboardStore();
        var controller = new CadPasteInteractionController(store);
        var snapshot = controller.Copy(new CadClipboardInteractionService(sourceEditor));
        Assert.NotNull(snapshot);
        Assert.True(controller.HasUserCopySnapshot);
        // The snapshot must remain valid after the source entity changes or is erased.
        entity.Rename("Changed after copy");
        entity.Erase();

        var target = CadDocument.Create("Target");
        var layer = target.CreateLayer("Destination", CadColor.Blue, new CadLineWeight(0.3));
        var editor = new CadEditor(target);
        var service = new CadClipboardInteractionService(editor);
        var history = editor.CreateDocumentHistorySnapshot();
        Assert.True(controller.BeginPreview(service));
        var position = new CadPointD(100, 200);
        var delta = position - snapshot.BasePoint;
        var items = new List<CadTransientItem>();
        controller.AddPreview(service, items, position, layer);
        var preview = Assert.IsType<CadTransientGroup>(Assert.Single(items));
        Assert.NotEmpty(preview.Items);
        Assert.True(preview.Transform.TransformPoint(snapshot.BasePoint).NearEquals(position));
        Assert.Empty(target.Entities);
        Assert.True(editor.DocumentHistoryEquals(history));
        controller.Clear(clearClipboard: false);
        Assert.False(controller.IsPreviewActive);
        Assert.Same(snapshot, store.Snapshot);
        Assert.True(controller.BeginPreview(service));

        var id = Assert.Single(controller.Commit(service, position, layer));
        var pasted = target.GetEntity(id);
        Assert.Equal(entity.GetType(), pasted.GetType());
        Assert.Equal("Original", pasted.Name);
        Assert.Equal(layer, pasted.LayerId);
        Assert.Equal(BlockId.ModelSpace, pasted.OwnerBlockId);
        Assert.Equal(42, pasted.ZIndex);
        Assert.Equal(new CadLineWeight(0.7), pasted.LineWeight);
        Assert.False(pasted.UseLayerLineWeight);
        Assert.False(controller.IsPreviewActive);
        AssertBounds(originalBounds.Translate(delta), pasted.Bounds);
        var blockCount = target.Blocks.Count;
        editor.Undo();
        Assert.True(pasted.IsErased);
        Assert.True(editor.DocumentHistoryEquals(history));
        editor.Redo();
        Assert.Same(pasted, target.GetEntity(id));
        Assert.False(pasted.IsErased);
        AssertBounds(originalBounds.Translate(delta), pasted.Bounds);

        Assert.True(controller.BeginPreview(service));
        var secondId = Assert.Single(controller.Commit(service, new(200, 300), layer));
        Assert.NotEqual(id, secondId);
        Assert.Equal(blockCount, target.Blocks.Count);
        if (pasted is CadBlockReference reference)
            Assert.Equal(reference.DefinitionBlockId, Assert.IsType<CadBlockReference>(target.GetEntity(secondId)).DefinitionBlockId);
        editor.Undo();
        Assert.False(pasted.IsErased);
        Assert.True(target.GetEntity(secondId).IsErased);
        controller.Clear(clearClipboard: true);
        Assert.Null(store.Snapshot);
    }

    private static void AssertBounds(CadRectD expected, CadRectD actual)
    {
        Assert.Equal(expected.MinX, actual.MinX, 6);
        Assert.Equal(expected.MinY, actual.MinY, 6);
        Assert.Equal(expected.MaxX, actual.MaxX, 6);
        Assert.Equal(expected.MaxY, actual.MaxY, 6);
    }
}
