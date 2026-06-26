using Direct2dCad.Db.Cad;

namespace Direct2dCad.Rendering;

public abstract class CadRender : ICadRenderer
{
    public abstract void Render(CadDocument document, CadViewport viewport, CadRenderOptions? options = null);
}
