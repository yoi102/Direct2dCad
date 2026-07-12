using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class CadLayoutViewportCreationState
{
    public CadPointD? FirstCorner { get; private set; }
    public LayoutViewportId? CreatedViewportId { get; private set; }
    public Guid BatchId { get; private set; }

    public bool IsActive => BatchId != Guid.Empty;
    public bool IsDefiningBounds => IsActive && FirstCorner is not null && CreatedViewportId is null;
    public bool IsAdjustingView => IsActive && CreatedViewportId is not null;

    public void Begin()
    {
        FirstCorner = null;
        CreatedViewportId = null;
        BatchId = Guid.NewGuid();
    }

    public void SetFirstCorner(CadPointD point) => FirstCorner = point;

    public CadRectD CreateBounds(CadPointD secondCorner) => FirstCorner is { } first
        ? CadRectD.FromLTRB(first.X, first.Y, secondCorner.X, secondCorner.Y)
        : CadRectD.Empty;

    public void BeginAdjusting(LayoutViewportId viewportId)
    {
        CreatedViewportId = viewportId;
        FirstCorner = null;
    }

    public void Clear()
    {
        FirstCorner = null;
        CreatedViewportId = null;
        BatchId = Guid.Empty;
    }
}
