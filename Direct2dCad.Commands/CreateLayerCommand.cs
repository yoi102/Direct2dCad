using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands;

public sealed class CreateLayerCommand : ICadCommand
{
    private readonly string _name;
    private readonly CadColor _color;
    private readonly CadLineWeight _lineWeight;
    private readonly StyleId? _defaultGraphicStyleId;
    private readonly int? _drawingPriority;
    private LayerId? _layerId;
    private int? _resolvedDrawingPriority;

    public string Name => "Create Layer";
    public LayerId? LayerId => _layerId;

    public CreateLayerCommand(
        string name,
        CadColor color,
        CadLineWeight lineWeight,
        StyleId? defaultGraphicStyleId = null,
        int? drawingPriority = null)
    {
        _name = name;
        _color = color;
        _lineWeight = lineWeight;
        _defaultGraphicStyleId = defaultGraphicStyleId;
        _drawingPriority = drawingPriority;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_layerId is not null && document.TryGetLayer(_layerId.Value, out _))
        {
            if (_resolvedDrawingPriority is { } resolvedPriority)
            {
                document.DocumentSettings.LayerDrawingPriority.SetPriority(
                    _layerId.Value,
                    resolvedPriority);
            }
            else if (_drawingPriority is { } existingPriority)
            {
                document.DocumentSettings.LayerDrawingPriority.SetPriority(
                    _layerId.Value,
                    existingPriority);
            }

            return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
        }

        if (_layerId is { } existingLayerId)
        {
            document.RestoreLayer(
                existingLayerId,
                _name,
                _color,
                _lineWeight,
                isVisible: true,
                isLocked: false,
                isFrozen: false,
                _defaultGraphicStyleId);
        }
        else
        {
            _layerId = document.CreateLayer(_name, _color, _lineWeight, _defaultGraphicStyleId);
        }

        var priority = _resolvedDrawingPriority ??
                       _drawingPriority ??
                       ResolveLowestPriority(document, _layerId.Value);
        document.DocumentSettings.LayerDrawingPriority.SetPriority(_layerId.Value, priority);
        _resolvedDrawingPriority = priority;

        return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_layerId is not null)
        {
            document.DocumentSettings.LayerDrawingPriority.RemovePriority(_layerId.Value);
            document.RemoveLayer(_layerId.Value);
        }

        return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
    }

    private static int ResolveLowestPriority(CadDocument document, LayerId newLayerId)
    {
        var lowest = document.Layers.Keys
            .Where(layerId => !layerId.Equals(newLayerId))
            .Select(document.DocumentSettings.LayerDrawingPriority.GetPriority)
            .DefaultIfEmpty(document.DocumentSettings.LayerDrawingPriority.DefaultPriority)
            .Min();
        return lowest == int.MinValue ? int.MinValue : lowest - 1;
    }
}
