using Direct2dCad.Rendering.Direct2D.Entities;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Transient;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Transient;

internal sealed class Direct2DTransientPathCache(Direct2DResourceCache resources, Direct2DGeometryFactory geometryFactory) : IDisposable
{
    private readonly Dictionary<CadTransientCompositePath, ID2D1PathGeometry> _paths = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<CadTransientCompositePath> _active = new(ReferenceEqualityComparer.Instance);
    private readonly List<CadTransientCompositePath> _stale = [];
    private CadTransientScene? _scene;
    private long _version = -1;

    public ID2D1PathGeometry? Get(CadTransientCompositePath path) => _paths.GetValueOrDefault(path);

    public void Prepare(CadTransientScene? scene)
    {
        if (ReferenceEquals(_scene, scene) && _version == (scene?.Version ?? -1))
            return;
        if (resources.Factory is not { } factory)
            return;
        _active.Clear();
        if (scene is not null) Collect(scene.Items);
        _stale.Clear();
        foreach (var path in _paths.Keys)
            if (!_active.Contains(path)) _stale.Add(path);
        foreach (var path in _stale)
        {
            _paths[path].Dispose();
            _paths.Remove(path);
        }
        foreach (var path in _active)
            if (!_paths.ContainsKey(path))
                _paths.Add(path, geometryFactory.CreateCompositePath(factory, path.StartPoint, path.Segments, path.Closed));
        _scene = scene;
        _version = scene?.Version ?? -1;
        _active.Clear();
        _stale.Clear();
    }

    private void Collect(IReadOnlyList<CadTransientItem> items)
    {
        foreach (var item in items)
        {
            if (item is CadTransientCompositePath path) _active.Add(path);
            else if (item is CadTransientGroup group) Collect(group.Items);
        }
    }

    public void Clear()
    {
        foreach (var path in _paths.Values) path.Dispose();
        _paths.Clear();
        _active.Clear();
        _stale.Clear();
        _scene = null;
        _version = -1;
    }

    public void Dispose() => Clear();
}
