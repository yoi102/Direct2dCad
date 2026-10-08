using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Vortice.Mathematics;

namespace Direct2dCad.Windows.IntegrationTests;

[Collection("Native zoom pixel comparisons")]
public sealed class RenderHostCacheLifecycleRegressionTests
{
    [Fact]
    public void SameSizeReplacementDetachesOldSourceAndBindsExistingSurface()
    {
        using var host = new Direct2DImageRenderHost();
        var original = new Source();
        var replacement = new Source();
        host.AttachImageSource(original);
        var surface = original.Surface;
        Assert.NotEqual(nint.Zero, surface);
        host.AttachImageSource(replacement);
        Assert.Equal(nint.Zero, original.Surface);
        Assert.Equal(surface, replacement.Surface);
        Assert.Equal(new[] { surface, nint.Zero }, original.Assignments);
        Assert.Equal(new[] { surface }, replacement.Assignments);
        host.AttachImageSource(replacement);
        Assert.Single(replacement.Assignments);
    }

    [Fact]
    public void DetachFailureKeepsPreviousOwnerDrawableAndReplacementCanBeRetried()
    {
        using var target = new ImageSourceDirect2DResource();
        var original = new Source();
        var replacement = new Source();
        target.SetTarget(original);
        var surface = original.Surface;
        original.FailDetach = true;
        Assert.Throws<InvalidOperationException>(() => target.SetTarget(replacement));
        Assert.Equal(surface, original.Surface);
        Assert.Empty(replacement.Assignments);
        target.DrawFrame(context => context.Clear(new Color4(.2f, .3f, .4f, 1)));
        Assert.Equal(1, original.Presents);
        original.FailDetach = false;
        target.SetTarget(replacement);
        Assert.Equal(nint.Zero, original.Surface);
        Assert.Equal(surface, replacement.Surface);
        target.DrawFrame(context => context.Clear(new Color4(.4f, .3f, .2f, 1)));
        Assert.Equal(1, replacement.Presents);
    }

    [Fact]
    public void ResizeDetachFailurePreservesSizeAndSurfaceUntilSafeRetry()
    {
        using var target = new ImageSourceDirect2DResource();
        var source = new Source();
        target.SetTarget(source);
        var surface = source.Surface;
        source.FailDetach = true;
        Assert.Throws<InvalidOperationException>(() => target.SetSize(321, 241));
        Assert.Equal(320, target.Width);
        Assert.Equal(240, target.Height);
        Assert.Equal(surface, source.Surface);
        target.DrawFrame(context => context.Clear(new Color4(.2f, .3f, .4f, 1)));
        source.FailDetach = false;
        target.SetSize(321, 241);
        Assert.Equal(321, target.Width);
        Assert.Equal(241, target.Height);
        Assert.Equal(nint.Zero, source.Assignments[^2]);
        Assert.NotEqual(nint.Zero, source.Assignments[^1]);
    }

    [Fact]
    public void FailedReplacementAttachmentCanRetryAtTheSameSize()
    {
        using var target = new ImageSourceDirect2DResource();
        var original = new Source();
        var replacement = new Source { FailNextAttach = true };
        target.SetTarget(original);
        var surface = original.Surface;
        Assert.Throws<InvalidOperationException>(() => target.SetTarget(replacement));
        Assert.Equal(nint.Zero, original.Surface);
        target.SetTarget(replacement);
        Assert.Equal(surface, replacement.Surface);
        target.DrawFrame(context => context.Clear(new Color4(.2f, .3f, .4f, 1)));
        Assert.Equal(1, replacement.Presents);
    }

    [Fact]
    public void HostAttachmentFailureAdoptsTheTargetOwnerAndSizeRetryPresentsCorrectPixels()
    {
        var document = CadDocument.Create("Host attachment retry");
        var line = document.AddLine(new(-35, 0), new(35, 0));
        line.SetLineWeight(new CadLineWeight(2));
        using var host = CreateHost(document);
        host.Render(CadRenderInvalidation.Full);
        var expected = host.CaptureBackBufferPixels();
        var replacement = new Source { FailNextAttach = true };
        Assert.Throws<InvalidOperationException>(() => host.AttachImageSource(replacement));
        Assert.False(host.HasPresentedScene);
        host.SetSize(320, 240);
        Assert.Equal(1, replacement.SizeCalls);
        Assert.NotEqual(nint.Zero, replacement.Surface);
        var pending = true;
        for (var step = 0; step < 500 && pending; step++)
        { pending = host.PrepareRenderCacheStep(); if (pending) Thread.Sleep(1); }
        Assert.False(pending);
        host.Render(CadRenderInvalidation.Full);
        Assert.Equal(expected, host.CaptureBackBufferPixels());
        Assert.Equal(1, replacement.Presents);
    }

    [Fact]
    public void DetachedHostDisposalCannotClearAnotherHostsNewSurface()
    {
        var source = new Source();
        using var oldHost = new Direct2DImageRenderHost();
        using var newHost = new Direct2DImageRenderHost();
        oldHost.AttachImageSource(source);
        oldHost.DetachImageSource();
        Assert.Equal(nint.Zero, source.Surface);
        newHost.AttachImageSource(source);
        var surface = source.Surface;
        var assignmentCount = source.Assignments.Count;
        oldHost.Dispose();
        Assert.NotEqual(nint.Zero, surface);
        Assert.Equal(surface, source.Surface);
        Assert.Equal(assignmentCount, source.Assignments.Count);
    }

    [Fact]
    public void ReplacementSelectionWithTheSameVersionInvalidatesCachedBasePixels()
    {
        var document = CadDocument.Create("Selection scene replacement");
        var line = document.AddLine(new(-35, 0), new(35, 0));
        using var host = CreateHost(document);
        var first = new CadHandleScene();
        first.Replace([new CadSelectionEntityReference(line.Id, line.Bounds, new(0, 0), CadHandleStyle.SelectionOutline)]);
        var second = new CadHandleScene();
        second.Replace([new CadSelectionEntityReference(line.Id, line.Bounds, new(0, 15), CadHandleStyle.SelectionOutline)]);
        Assert.Equal(first.SelectionVersion, second.SelectionVersion);
        host.SetHandleScene(first);
        host.Render(CadRenderInvalidation.Full);
        var oldPixels = host.CaptureBackBufferPixels();
        Assert.True(host.BeginViewportInteraction());
        host.SetHandleScene(second);
        Assert.False(host.IsViewportInteractionActive);
        Assert.False(host.RenderViewportInteractionPreview());
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: false);
        var actual = host.CaptureBackBufferPixels();
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        Assert.Equal(host.CaptureBackBufferPixels(), actual);
        Assert.False(oldPixels.AsSpan().SequenceEqual(actual));
    }

    [Fact]
    public void ReplacementMoveSceneWithTheSameVersionInvalidatesCachedBasePixels()
    {
        var document = CadDocument.Create("Move scene replacement");
        var line = document.AddLine(new(-35, 0), new(35, 0));
        line.SetLineWeight(new CadLineWeight(2));
        using var host = CreateHost(document);
        host.SetRenderOptions(new() { DrawGrid = false, DrawOrigin = false, DrawGripHandles = false,
            HiddenEntityIds = new HashSet<Direct2dCad.Db.EntityId> { line.Id }, IsAntialiasingEnabled = false });
        var item = new CadTransientEntityReference(line.Id, default, new CadTransientStyle(CadColor.Red, 2), UseSourceAppearance: true);
        var first = new CadTransientScene();
        first.Replace([new CadTransientGroup([item], CadMatrixD.CreateTranslation(0, 0))]);
        var second = new CadTransientScene();
        second.Replace([new CadTransientGroup([item], CadMatrixD.CreateTranslation(0, 15))]);
        Assert.Equal(first.Version, second.Version);
        host.SetTransientScene(first);
        host.Render(CadRenderInvalidation.Full);
        var oldPixels = host.CaptureBackBufferPixels();
        Assert.True(host.BeginViewportInteraction());
        host.SetTransientScene(second);
        Assert.False(host.IsViewportInteractionActive);
        Assert.False(host.RenderViewportInteractionPreview());
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: false);
        var actual = host.CaptureBackBufferPixels();
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        Assert.Equal(host.CaptureBackBufferPixels(), actual);
        Assert.False(oldPixels.AsSpan().SequenceEqual(actual));
    }

    private static Direct2DImageRenderHost CreateHost(CadDocument document)
    {
        var viewport = new CadViewport();
        viewport.SetSize(320, 240); viewport.SetView(2, new(160, 120));
        var host = new Direct2DImageRenderHost();
        host.AttachImageSource(new Source());
        host.SetScene(document, viewport, prepareResourcesInBackground: false);
        host.SetRenderOptions(new() { DrawGrid = false, DrawOrigin = false, DrawGripHandles = false });
        return host;
    }

    private sealed class Source : ID3D11ImageSource
    {
        public int SurfaceWidth => 320;
        public int SurfaceHeight => 240;
        public nint Surface;
        public bool FailDetach;
        public bool FailNextAttach;
        public int Presents;
        public int SizeCalls;
        public List<nint> Assignments = [];
        public void SetSize(int width, int height) { SizeCalls++; }
        public void SetSurface(nint surface)
        {
            if (surface == 0 && FailDetach) throw new InvalidOperationException("Injected detach failure");
            if (surface != 0 && FailNextAttach)
            { FailNextAttach = false; throw new InvalidOperationException("Injected attach failure"); }
            Surface = surface; Assignments.Add(surface);
        }
        public void Present(Action action, IReadOnlyList<IntRect>? dirtyRects = null) { action(); Presents++; }
        public void Invalidate() { }
        public void Invalidate(IntRect rect) { }
        public void Invalidate(IReadOnlyList<IntRect> rects) { }
    }
}
