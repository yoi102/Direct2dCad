using Direct2dCad.Db;

namespace Direct2dCad.Rendering;

public sealed class CadRenderOptions
{
    public bool DrawGrid { get; init; } = true;
    public bool DrawOrigin { get; init; } = true;
    public bool KeepStrokeWidthScreenConstant { get; init; } = true;
    public double MinimumScreenStrokeWidth { get; init; } = 0.5;
    public IReadOnlySet<EntityId> HiddenEntityIds { get; init; } = new HashSet<EntityId>();
}
