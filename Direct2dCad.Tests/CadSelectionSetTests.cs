using Direct2dCad.Db;
using Direct2dCad.Editor;

namespace Direct2dCad.Tests;

public sealed class CadSelectionSetTests
{
    [Fact]
    public void Replace_DoesNotChangeVersion_WhenContentIsEquivalent()
    {
        var selection = new CadSelectionSet();
        selection.Add(new EntityId(1));
        selection.Add(new EntityId(2));
        var version = selection.Version;

        selection.Replace([new EntityId(2), new EntityId(1), new EntityId(1)]);

        Assert.Equal(version, selection.Version);
        Assert.Equal(2, selection.Count);
    }

    [Fact]
    public void Replace_MaterializesLazySource_BeforeClearingCurrentSelection()
    {
        var selection = new CadSelectionSet();
        selection.Replace([new EntityId(1), new EntityId(2), new EntityId(3)]);
        var version = selection.Version;
        var filteredCurrentSelection =
            selection.EntityIds.Where(static id => id != new EntityId(2));

        selection.Replace(filteredCurrentSelection);

        Assert.Equal(version + 1, selection.Version);
        Assert.Equal(
            new HashSet<EntityId> { new(1), new(3) },
            selection.EntityIds);
    }

    [Fact]
    public void Clear_DoesNotChangeVersion_WhenAlreadyEmpty()
    {
        var selection = new CadSelectionSet();

        selection.Clear();

        Assert.Equal(0, selection.Version);
    }
}
