namespace Direct2dCad.ViewModels.Tests;

public sealed class RenderSessionBoundaryTests
{
    [Fact]
    public void DocumentUsesInjectedSessionAndDisposesIt()
    {
        var context = new CadToolboxTestContext();
        var session = Assert.IsType<TestRenderSession>(context.Document.RenderSession);
        context.Document.SetRenderSize(320, 200);
        Assert.Equal(320, session.TargetWidth);
        Assert.Equal(200, session.TargetHeight);
        context.Document.AttachRenderResources();
        context.Document.RequestRender();
        Assert.True(session.RenderCount > 0);
        context.Dispose();
        Assert.True(session.IsDisposed);
    }

    [Fact]
    public void ViewModelAndServicesAssembliesDoNotReferenceNativeBackend()
    {
        foreach (var assembly in new[] { typeof(CadDocumentViewModel).Assembly,
                     typeof(Direct2dCad.ViewModels.Services.Platform.Printing.ICadPrintService).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
                reference.Name!.Contains("Rendering.Direct2D", StringComparison.Ordinal) ||
                reference.Name.StartsWith("Vortice", StringComparison.Ordinal) ||
                reference.Name.StartsWith("SharpGen", StringComparison.Ordinal));
    }
}
