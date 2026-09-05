using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Direct2D.Scene;

// Array-backed reduction tree: shrinking an extremal entity must shrink its ancestors too.
internal sealed class EntityBoundsTree
{
    private readonly CadRectD[] _nodes;
    private readonly int _leafStart;

    public EntityBoundsTree(IReadOnlyList<Direct2DEntityRenderPacket> entries)
    {
        _leafStart = 1;
        while (_leafStart < entries.Count)
            _leafStart *= 2;
        _nodes = new CadRectD[_leafStart * 2];
        Array.Fill(_nodes, CadRectD.Empty);
        for (var index = 0; index < entries.Count; index++)
            _nodes[_leafStart + index] = GetBounds(entries[index]);
        for (var index = _leafStart - 1; index > 0; index--)
            _nodes[index] = _nodes[index * 2].Union(_nodes[index * 2 + 1]);
    }

    public CadRectD Bounds => _nodes[1];

    public void Update(int index, Direct2DEntityRenderPacket entry)
    {
        var node = _leafStart + index;
        var bounds = GetBounds(entry);
        if (_nodes[node].Equals(bounds))
            return;
        _nodes[node] = bounds;
        while ((node /= 2) > 0)
        {
            bounds = _nodes[node * 2].Union(_nodes[node * 2 + 1]);
            if (_nodes[node].Equals(bounds))
                break;
            _nodes[node] = bounds;
        }
    }

    private static CadRectD GetBounds(Direct2DEntityRenderPacket entry) =>
        entry.IsRenderable ? entry.Bounds : CadRectD.Empty;
}
