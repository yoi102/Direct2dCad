using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Db.Cad;

public sealed partial class CadDocument
{
    public CadRegion AddRegion(IEnumerable<CadRegionContour> contours, LayerId? layerId = null,
        StyleId? graphicStyleId = null, StyleId? fillStyleId = null, string name = "")
    {
        ValidateGraphicStyle(graphicStyleId, allowNull: true);
        var entity = new CadRegion(_ids.NewEntityId(), layerId ?? LayerId.Default, BlockId.ModelSpace, contours, name);
        entity.SetGraphicStyleInternal(graphicStyleId);
        entity.SetFillStyleInternal(fillStyleId);
        entity.SetUseLayerColor(graphicStyleId is null);
        entity.SetUseLayerLineWeight(graphicStyleId is null);
        AddEntityCore(entity);
        return entity;
    }
}
