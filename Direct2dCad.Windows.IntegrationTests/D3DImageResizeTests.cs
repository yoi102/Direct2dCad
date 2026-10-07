using System.Windows;
using System.Windows.Controls;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.wpf.Controls;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class D3DImageResizeTests
{
    [Fact]
    public void DirtyRegionsUseBoundSurfaceDuringResizeAndReplacementStillPresents() => WpfTestDispatcher.Run(() =>
    {
        using var source = new D3D11ImageSource();
        using var host = new Direct2DImageRenderHost();
        host.AttachImageSource(source);
        host.SetSize(1, 1);
        var window = new Window { Content = new Image { Source = source }, Width = 300, Height = 200, ShowInTaskbar = false };
        window.Show();
        window.UpdateLayout();
        Assert.True(source.IsFrontBufferAvailable);
        Assert.Equal(1, source.PixelWidth);
        Assert.Equal(1, source.PixelHeight);

        // Deterministically expose the resize gap, without depending on callback timing.
        source.SetSize(524, 200);
        source.Invalidate();
        source.Invalidate(new Int32Rect(0, 0, 524, 200));
        source.Invalidate(new IntRect(0, 0, 524, 200));
        source.Invalidate(new[] { new IntRect(0, 0, 524, 200), new IntRect(300, 100, 30, 30) });
        var presents = 0;
        source.Present(() => presents++);
        source.Present(() => presents++, [new(0, 0, 524, 200)]);
        Assert.Equal(2, presents);
        Assert.Equal(1, source.PixelWidth);

        host.SetSize(524, 200);
        Assert.Equal(524, source.PixelWidth);
        Assert.Equal(200, source.PixelHeight);
        source.Invalidate();
        source.Present(() => presents++, [new(500, 100, 24, 100)]);
        Assert.Equal(3, presents);
        window.Close();
    });
}
