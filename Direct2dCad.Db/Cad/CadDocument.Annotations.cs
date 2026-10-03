using Direct2dCad.Db.Data.Entities;
namespace Direct2dCad.Db.Cad;

public sealed partial class CadDocument
{
    public CadDimension AddDimension(CadDimensionDefinition definition, LayerId? layer = null, BlockId? owner = null)
    {
        var entity = new CadDimension(_ids.NewEntityId(), layer ?? LayerId.Default, owner ?? BlockId.ModelSpace, definition);
        AddEntityCore(entity); entity.RefreshAssociation(this); return entity;
    }
    public CadDimension RestoreDimension(EntityId id, LayerId layer, BlockId owner, CadDimensionDefinition definition, string name = "")
    {
        var entity = new CadDimension(id, layer, owner, definition, name);
        AddEntityCore(entity); entity.RefreshAssociation(this); return entity;
    }
}
