using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadBlockReference : CadEntity
{
    private CadRectD _resolvedBounds;

    public BlockId DefinitionBlockId { get; private set; }
    public CadPointD Position { get; private set; }
    public double RotationRadians { get; private set; }
    public double ScaleX { get; private set; }
    public double ScaleY { get; private set; }

    public override CadRectD Bounds => _resolvedBounds.IsEmpty
        ? CadRectD.FromLTRB(Position.X, Position.Y, Position.X, Position.Y)
        : _resolvedBounds;
    public StyleId? GraphicStyleId { get; private set; }

    internal CadBlockReference(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        BlockId definitionBlockId,
        CadPointD position,
        double rotationRadians = 0,
        double scaleX = 1.0,
        double scaleY = 1.0,
        string name = "")
        : base(id, layerId, ownerBlockId, name)
    {
        DefinitionBlockId = definitionBlockId;
        Position = position;
        RotationRadians = rotationRadians;
        ScaleX = GuardPositive(scaleX, nameof(scaleX));
        ScaleY = GuardPositive(scaleY, nameof(scaleY));
        _resolvedBounds = CadRectD.Empty;
    }

    internal void SetDefinitionBlockInternal(BlockId definitionBlockId) => DefinitionBlockId = definitionBlockId;

    public void SetPosition(CadPointD position) => Position = position;

    public void SetRotation(double rotationRadians) => RotationRadians = rotationRadians;

    public void SetScale(double scaleX, double scaleY)
    {
        ScaleX = GuardPositive(scaleX, nameof(scaleX));
        ScaleY = GuardPositive(scaleY, nameof(scaleY));
    }

    internal bool SetResolvedBounds(CadRectD bounds)
    {
        if (_resolvedBounds.NearEquals(bounds))
            return false;

        _resolvedBounds = bounds;
        return true;
    }

    private static double GuardPositive(double value, string paramName)
    {
        return value <= 0 || double.IsNaN(value) || double.IsInfinity(value)
            ? throw new ArgumentOutOfRangeException(paramName)
            : value;
    }

    public void SetGraphicStyleInternal(StyleId? styleId) => GraphicStyleId = styleId;
}
