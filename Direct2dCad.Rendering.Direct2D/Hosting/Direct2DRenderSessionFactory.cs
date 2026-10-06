namespace Direct2dCad.Rendering.Direct2D.Hosting;

public sealed class Direct2DRenderSessionFactory(CadRenderResourceBudget resourceBudget) : ICadRenderSessionFactory
{
    public ICadRenderSession Create() => new Direct2DImageRenderHost(null, resourceBudget);
}
