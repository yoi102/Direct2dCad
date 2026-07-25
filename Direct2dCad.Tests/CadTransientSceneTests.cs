using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.Tests;

public sealed class CadTransientSceneTests
{
    [Fact]
    public void EmptyReplaceAndClear_DoNotChangeVersion()
    {
        var scene = new CadTransientScene();

        scene.Replace([]);
        scene.Clear();

        Assert.True(scene.IsEmpty);
        Assert.Equal(0, scene.Version);
    }

    [Fact]
    public void Replace_FiltersNullItemsAndChangesVersion()
    {
        var scene = new CadTransientScene();
        var style = new CadTransientStyle(CadColor.Green, 1);
        var line = new CadTransientLine(CadPointD.Origin, new CadPointD(10, 0), style);
        CadTransientItem?[] items = [null, line, null];

        scene.Replace(items!);

        Assert.Equal(1, scene.Version);
        Assert.Same(line, Assert.Single(scene.Items));
    }

    [Fact]
    public void Clear_ChangesVersionOnlyWhenSceneContainsItems()
    {
        var scene = new CadTransientScene();
        var style = new CadTransientStyle(CadColor.Green, 1);
        scene.Replace([new CadTransientLine(CadPointD.Origin, new CadPointD(10, 0), style)]);
        var populatedVersion = scene.Version;

        scene.Clear();
        scene.Clear();

        Assert.True(scene.IsEmpty);
        Assert.Equal(populatedVersion + 1, scene.Version);
    }
}
