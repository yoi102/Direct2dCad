using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public abstract class CadEntity : IEquatable<CadEntity>
{
    public EntityId Id { get; }
    public string Name { get; private set; }
    public LayerId LayerId { get; private set; }

    /// <summary>
    /// 该实体属于哪个 BlockDefinition。ModelSpace 本质上也是一个 BlockDefinition。
    /// </summary>
    public BlockId OwnerBlockId { get; private set; }

    public bool IsLocked { get; private set; }
    public bool IsErased { get; private set; }
    public bool IsVisible { get; private set; } = true;
    public CadLineWeight? LineWeight { get; private set; }
    public CadColorSource ColorSource { get; private set; } = CadColorSource.ByLayer;
    public bool UseLayerColor => ColorSource == CadColorSource.ByLayer;
    public bool UseLayerLineWeight { get; private set; } = true;
    public CadStrokeStyle StrokeStyle { get; private set; } = CadStrokeStyle.Default;
    public int ZIndex { get; private set; }

    public abstract CadRectD Bounds { get; }

    protected CadEntity(EntityId id, LayerId layerId, BlockId ownerBlockId, string name = "")
    {
        Id = id;
        LayerId = layerId;
        OwnerBlockId = ownerBlockId;
        Name = name ?? string.Empty;
    }

    public void Rename(string name) => Name = name ?? string.Empty;

    public void SetLocked(bool locked) => IsLocked = locked;

    public void SetVisible(bool visible) => IsVisible = visible;

    public void SetLineWeight(CadLineWeight? lineWeight)
    {
        if (lineWeight is { IsByLayer: true })
        {
            UseLayerLineWeight = true;
            return;
        }

        LineWeight = lineWeight;
        UseLayerLineWeight = lineWeight is null;
    }

    public void SetLineWeightState(CadLineWeight? lineWeight, bool useLayerLineWeight)
    {
        LineWeight = lineWeight is { IsByLayer: true } ? null : lineWeight;
        UseLayerLineWeight = useLayerLineWeight || LineWeight is null;
    }

    public void SetUseLayerColor(bool useLayerColor) =>
        ColorSource = useLayerColor ? CadColorSource.ByLayer : CadColorSource.Explicit;

    public void SetColorSource(CadColorSource colorSource)
    {
        if (!Enum.IsDefined(colorSource))
            throw new ArgumentOutOfRangeException(nameof(colorSource));

        ColorSource = colorSource;
    }

    public void SetUseLayerLineWeight(bool useLayerLineWeight) => UseLayerLineWeight = useLayerLineWeight;

    public void SetStrokeStyle(CadStrokeStyle strokeStyle) => StrokeStyle = strokeStyle;

    public void SetZIndex(int zIndex) => ZIndex = zIndex;

    public void Erase() => IsErased = true;

    public void Restore() => IsErased = false;

    internal void ChangeLayerInternal(LayerId layerId) => LayerId = layerId;

    internal void ChangeOwnerInternal(BlockId ownerBlockId) => OwnerBlockId = ownerBlockId;

    public bool Equals(CadEntity? other) => other is not null && Id.Equals(other.Id);

    public override bool Equals(object? obj) => obj is CadEntity other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();
}
