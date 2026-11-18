using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.ViewModels.Services.Snapping;

namespace Direct2dCad.ViewModels.Services.Tests;

public sealed class ObjectSnapMemoTests
{
    private sealed class Fixture
    {
        public Fixture()
        {
            Document = CadDocument.Create("snap memo");
            Document.AddLine(default, new(20, 0));
            Editor = new CadEditor(Document);
            Editor.Viewport.SetView(20, default);
            Query = (block, bounds) => { Queries++; return Editor.SpatialIndex.Query(block, bounds); };
        }

        public CadDocument Document { get; }
        public CadEditor Editor { get; }
        public Func<BlockId, CadRectD, IReadOnlyList<EntityId>> Query { get; }
        public int Queries { get; private set; }
        public CadObjectSnapController Snap { get; } = new();

        public CadPointD Resolve(CadPointD pointer, long version, out bool reused, CadDocument? document = null) =>
            Snap.Resolve(document ?? Document, version, Editor.ActiveOwnerBlockId, Editor.Viewport, Query, pointer, null, out reused);
    }

    [Fact]
    public void SameInputsReuseThePreviousResolve()
    {
        var f = new Fixture();
        var first = f.Resolve(new(.03, .02), 1, out var firstReused);
        var queries = f.Queries;
        var second = f.Resolve(new(.03, .02), 1, out var secondReused);

        Assert.False(firstReused);
        Assert.True(secondReused);
        Assert.Equal(first, second);
        Assert.Equal(CadPointD.Origin, second);
        Assert.Equal(queries, f.Queries);
    }

    [Fact]
    public void ChangedInputsResolveAgain()
    {
        var f = new Fixture();
        f.Resolve(new(.03, .02), 1, out _);

        f.Resolve(new(.03, .02), 2, out var afterVersion);
        Assert.False(afterVersion);

        f.Resolve(new(19.97, .02), 2, out var afterMove);
        Assert.False(afterMove);

        f.Editor.Viewport.SetView(40, default);
        f.Resolve(new(19.97, .02), 2, out var afterZoom);
        Assert.False(afterZoom);

        f.Snap.Clear();
        f.Resolve(new(19.97, .02), 2, out var afterClear);
        Assert.False(afterClear);

        f.Resolve(new(19.97, .02), 2, out var otherDocument, CadDocument.Create("snap memo"));
        Assert.False(otherDocument);
    }
}
