using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Commands;

internal sealed class DimensionTransformState
{
    private Dictionary<EntityId, CadDimensionDefinition>? _before;
    public void Capture(CadDocument document, IEnumerable<EntityId> ids) => _before ??= ids
        .Select(document.GetEntity).OfType<CadDimension>().ToDictionary(d => d.Id, d => d.Definition.Copy());
    public void Restore(CadDocument document)
    {
        if (_before is null) return;
        foreach (var (id, definition) in _before) ((CadDimension)document.GetEntity(id)).SetDefinition(definition);
    }
}
