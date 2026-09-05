using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;

namespace Direct2dCad.ViewModels.Services.Rendering;

internal readonly partial struct CadRenderInvalidationCalculator
{
    private CadRenderInvalidation CreateLayoutEntityInvalidation(CadEntityInvalidationSnapshot snapshot)
    {
        if (!snapshot.IsRenderable || snapshot.Bounds.IsEmpty)
            return CadRenderInvalidation.Empty;
        if (layoutId is not { } id || !document.TryGetLayout(id, out var layout) || layout is null)
            return CadRenderInvalidation.Full;

        var padding = ResolveEntityInvalidationPadding(snapshot);
        if (snapshot.OwnerBlockId == layout.PaperSpaceBlockId)
            return CreateWorldBoundsInvalidation(snapshot.Bounds, padding);
        // Definition changes are represented by their expanded reference changes.
        if (snapshot.OwnerBlockId != BlockId.ModelSpace)
            return CadRenderInvalidation.Empty;

        var rectangles = new List<CadScreenRect>();
        foreach (var view in layout.Viewports)
        {
            if (!view.IsVisible)
                continue;
            var paperBounds = CadLayoutViewportMapper.ModelToPaperBounds(view, snapshot.Bounds);
            // Layout line weights are in paper units, independent of the model viewport scale.
            var extent = padding / Math.Max(viewport.Zoom, double.Epsilon);
            paperBounds = paperBounds.Inflate(extent);
            if (!double.IsFinite(paperBounds.MinX) || !double.IsFinite(paperBounds.MaxX) ||
                !double.IsFinite(paperBounds.MinY) || !double.IsFinite(paperBounds.MaxY))
                return CadRenderInvalidation.Full;
            if (!paperBounds.Intersects(view.Bounds))
                continue;

            var clipped = paperBounds.Intersection(view.Bounds);
            var invalidation = CreateWorldBoundsInvalidation(clipped, paddingPixels: 2);
            if (invalidation.IsFull)
                return invalidation;
            rectangles.AddRange(invalidation.DirtyScreenRects);
        }
        return CadRenderInvalidation.FromScreenRects(rectangles);
    }
}
