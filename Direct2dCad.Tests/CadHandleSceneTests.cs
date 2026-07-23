using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Handles;

namespace Direct2dCad.Tests;

public sealed class CadHandleSceneTests
{
    [Fact]
    public void Replace_KeepsPublishedSelectionList_WhenReferencesAreUnchanged()
    {
        var scene = new CadHandleScene();
        var reference = CreateReference(new EntityId(1));
        scene.Replace([reference]);
        var version = scene.SelectionVersion;
        var publishedReferences = scene.SelectionReferences;

        scene.Replace([reference]);

        Assert.Equal(version, scene.SelectionVersion);
        Assert.Same(publishedReferences, scene.SelectionReferences);
    }

    [Fact]
    public void Replace_ChangesVersion_WhenReferenceOffsetChanges()
    {
        var scene = new CadHandleScene();
        var reference = CreateReference(new EntityId(1));
        scene.Replace([reference]);
        var version = scene.SelectionVersion;

        scene.Replace([reference with { Offset = new CadVectorD(4, -2) }]);

        Assert.Equal(version + 1, scene.SelectionVersion);
        Assert.True(scene.HasTranslatedSelectionReferences);
        Assert.Equal(CadRectD.FromLTRB(4, -2, 14, 8), scene.SelectionWorldBounds);
    }

    [Fact]
    public void Clear_ChangesSelectionVersionOnlyOnce()
    {
        var scene = new CadHandleScene();
        scene.Replace([CreateReference(new EntityId(1))]);
        var version = scene.SelectionVersion;

        scene.Clear();
        scene.Clear();

        Assert.Equal(version + 1, scene.SelectionVersion);
        Assert.Empty(scene.SelectionReferences);
    }

    private static CadSelectionEntityReference CreateReference(EntityId entityId)
    {
        return new CadSelectionEntityReference(
            entityId,
            CadRectD.FromLTRB(0, 0, 10, 10),
            CadVectorD.Zero,
            CadHandleStyle.SelectionOutline);
    }
}
