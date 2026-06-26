namespace Direct2dCad.Db.Cad.Settings;

public sealed class LayerDrawingPriority
{
    private readonly Dictionary<LayerId, int> _priorities = [];

    public int DefaultPriority { get; private set; }

    public IReadOnlyDictionary<LayerId, int> Priorities => _priorities;

    public int GetPriority(LayerId layerId)
    {
        return _priorities.TryGetValue(layerId, out var priority)
            ? priority
            : DefaultPriority;
    }

    public void SetPriority(LayerId layerId, int priority)
    {
        _priorities[layerId] = priority;
    }

    public bool RemovePriority(LayerId layerId)
    {
        return _priorities.Remove(layerId);
    }

    public void SetDefaultPriority(int priority)
    {
        DefaultPriority = priority;
    }

    public void Clear()
    {
        _priorities.Clear();
    }
}
