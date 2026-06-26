using Direct2dCad.Db.Cad;
using Direct2dCad.HitTesting;
using Direct2dCad.Indexing;
using Direct2dCad.Rendering;

namespace Direct2dCad.Editor.Commands;

public sealed class CadEditorCommandContext
{
    public CadDocument Document { get; }
    public CadViewport Viewport { get; }
    public CadSelectionSet Selection { get; }
    public ICadSpatialIndex SpatialIndex { get; }
    public CadHitTestService HitTesting { get; }

    public CadEditorCommandContext(
        CadDocument document,
        CadViewport viewport,
        CadSelectionSet selection,
        ICadSpatialIndex spatialIndex)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        Selection = selection ?? throw new ArgumentNullException(nameof(selection));
        SpatialIndex = spatialIndex ?? throw new ArgumentNullException(nameof(spatialIndex));
        HitTesting = new CadHitTestService(Document);
    }
}
