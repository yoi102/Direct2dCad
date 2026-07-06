namespace Direct2dCad.Db.Cad;

public sealed class CadLayer : IEquatable<CadLayer>
{
    public LayerId Id { get; }
    public string Name { get; private set; }
    public bool IsVisible { get; private set; } = true;
    public bool IsLocked { get; private set; }
    public bool IsFrozen { get; private set; }
    public CadColor Color { get; private set; }
    public CadLineWeight LineWeight { get; private set; }

    /// <summary>
    /// 图层默认图形样式。为空时使用 Layer 自身的 Color / LineWeight / Continuous LineType。
    /// </summary>
    public StyleId? DefaultGraphicStyleId { get; private set; }

    internal CadLayer(
        LayerId id,
        string name,
        CadColor color,
        CadLineWeight lineWeight,
        StyleId? defaultGraphicStyleId = null)
    {
        Id = id;
        Name = GuardName(name);
        Color = color;
        LineWeight = lineWeight;
        DefaultGraphicStyleId = defaultGraphicStyleId;
    }

    internal void Rename(string name) => Name = GuardName(name);
    public void SetVisible(bool visible) => IsVisible = visible;
    public void SetLocked(bool locked) => IsLocked = locked;
    public void SetFrozen(bool frozen) => IsFrozen = frozen;
    public void SetColor(CadColor color) => Color = color;
    public void SetLineWeight(CadLineWeight lineWeight) => LineWeight = lineWeight;

    internal void SetDefaultGraphicStyleInternal(StyleId? styleId) => DefaultGraphicStyleId = styleId;

    public bool Equals(CadLayer? other) => other is not null && Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is CadLayer other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();

    private static string GuardName(string name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Name cannot be empty.", nameof(name))
            : name.Trim();
    }
}
