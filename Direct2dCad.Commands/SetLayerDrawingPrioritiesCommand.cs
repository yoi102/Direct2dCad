using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;

namespace Direct2dCad.Commands;

public sealed class SetLayerDrawingPrioritiesCommand : ICadCommand
{
    private readonly Dictionary<LayerId, int> _priorities;
    private readonly int _defaultPriority;
    private Dictionary<LayerId, int>? _previousPriorities;
    private int? _previousDefaultPriority;

    public string Name => "Set Layer Drawing Priorities";

    public SetLayerDrawingPrioritiesCommand(
        IReadOnlyDictionary<LayerId, int> priorities,
        int defaultPriority = 0)
    {
        ArgumentNullException.ThrowIfNull(priorities);

        _priorities = new Dictionary<LayerId, int>(priorities);
        _defaultPriority = defaultPriority;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var drawingPriority = document.DocumentSettings.LayerDrawingPriority;
        _previousPriorities ??= new Dictionary<LayerId, int>(drawingPriority.Priorities);
        _previousDefaultPriority ??= drawingPriority.DefaultPriority;

        Apply(drawingPriority, _priorities, _defaultPriority);
        return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_previousPriorities is null || _previousDefaultPriority is null)
            return CadDocumentChangeSet.Empty;

        Apply(
            document.DocumentSettings.LayerDrawingPriority,
            _previousPriorities,
            _previousDefaultPriority.Value);
        return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
    }

    private static void Apply(
        LayerDrawingPriority drawingPriority,
        IReadOnlyDictionary<LayerId, int> priorities,
        int defaultPriority)
    {
        drawingPriority.Clear();
        drawingPriority.SetDefaultPriority(defaultPriority);

        foreach (var pair in priorities)
            drawingPriority.SetPriority(pair.Key, pair.Value);
    }
}
