using Direct2dCad.Db.Cad;

namespace Direct2dCad.Rendering;

public interface ICadRenderer
{
    void Render(CadDocument document, CadViewport viewport, CadRenderOptions? options = null);
}
