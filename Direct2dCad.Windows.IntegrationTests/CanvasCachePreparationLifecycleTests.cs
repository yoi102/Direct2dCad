using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Direct2D.Scene;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using Direct2dCad.wpf.Controls;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Vortice.Direct2D1;

namespace Direct2dCad.Windows.IntegrationTests;

[Collection("Native zoom pixel comparisons")]
public sealed class CanvasCachePreparationLifecycleTests
{
    [ThreadStatic]
    private static List<Window>? _hostWindows;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingViewModelCancelsOldQueuedWorkAndRetryWithoutRestoringItsSurface(bool disposeOldModel)
    {
        RunSta(() =>
        {
            using var services = CreateServices();
            using var oldModel = CreateModel(services, CadColor.Red);
            using var newModel = CreateModel(services, CadColor.Green);
            using var canvas = CreateCanvas();
            canvas.DocumentViewModel = oldModel;
            var oldHost = Assert.IsType<Direct2DImageRenderHost>(oldModel.RenderSession);
            oldHost.RebuildAll(oldModel.CadEditor.Document);
            Invoke(canvas, "FlushScheduledRender");
            Assert.True(PumpDispatcher(TimeSpan.FromMilliseconds(80)) >= 2);
            AssertCanvasSize(canvas);
            Invoke(canvas, "FlushScheduledRender");
            var oldRenderer = Field<Direct2DSceneRender>(oldHost, "_renderer");
            var oldPreparedCount = oldRenderer.PreparedEntityResourceCount;
            QueueOwnerCallbackAndArmRetry(canvas, oldHost);

            canvas.DocumentViewModel = newModel;
            var newHost = Assert.IsType<Direct2DImageRenderHost>(newModel.RenderSession);
            newHost.RebuildAll(newModel.CadEditor.Document);
            Invoke(canvas, "FlushScheduledRender");
            Assert.True(PumpDispatcher(TimeSpan.FromMilliseconds(80)) >= 2);
            AssertCanvasSize(canvas);
            Invoke(canvas, "FlushScheduledRender");
            var imageSource = Assert.IsType<D3D11ImageSource>(canvas.Source);
            var expectedSurface = Field<nint>(imageSource, "_surface9Ptr");
            Assert.NotEqual(nint.Zero, expectedSurface);
            Assert.True(newHost.HasPresentedScene);
            var expectedPixels = newHost.CapturePresentedPixels();
            Assert.True(expectedPixels[1] > 180 && expectedPixels[2] < 80,
                "The replacement scene must actually present its green background.");

            if (disposeOldModel)
                oldModel.Dispose();
            var dispatcherPasses = PumpDispatcher(TimeSpan.FromMilliseconds(80));

            Assert.True(dispatcherPasses >= 2, "The real dispatcher must run beyond the retry interval.");
            Assert.Same(newModel, canvas.DocumentViewModel);
            AssertCurrentSurfaceUnchanged(canvas, newHost, expectedSurface);
            Assert.Equal(expectedPixels, newHost.CapturePresentedPixels());
            Assert.False(Field<DispatcherTimer>(canvas, "_renderCacheRetryTimer").IsEnabled);
            Assert.Null(Field<object?>(canvas, "_renderCacheRetrySession"));
            if (!disposeOldModel)
                Assert.Equal(oldPreparedCount, oldRenderer.PreparedEntityResourceCount);
        });
    }

    [Fact]
    public void DisposingCanvasAndNativeSessionCancelsQueuedWorkAndRetryOnTheActualDispatcher()
    {
        RunSta(() =>
        {
            using var services = CreateServices();
            using var model = CreateModel(services, CadColor.Red);
            using var canvas = CreateCanvas();
            canvas.DocumentViewModel = model;
            var host = Assert.IsType<Direct2DImageRenderHost>(model.RenderSession);
            host.RebuildAll(model.CadEditor.Document);
            Invoke(canvas, "FlushScheduledRender");
            Assert.True(PumpDispatcher(TimeSpan.FromMilliseconds(80)) >= 2);
            AssertCanvasSize(canvas);
            Invoke(canvas, "FlushScheduledRender");
            QueueOwnerCallbackAndArmRetry(canvas, host);
            var timer = Field<DispatcherTimer>(canvas, "_renderCacheRetryTimer");

            canvas.Dispose();
            model.Dispose();
            var dispatcherPasses = PumpDispatcher(TimeSpan.FromMilliseconds(80));

            Assert.True(dispatcherPasses >= 2);
            Assert.False(timer.IsEnabled);
            Assert.Null(Field<object?>(canvas, "_renderCacheRetrySession"));
            Assert.False(Field<bool>(canvas, "_renderCacheBuildScheduled"));
            Assert.Throws<ObjectDisposedException>(() => host.PrepareRenderCacheStep());
        });
    }

    [Fact]
    public void LiveModelReattachesToANewCanvasAfterThePreviousCanvasIsDisposed()
    {
        RunSta(() =>
        {
            using var services = CreateServices();
            using var model = CreateModel(services, CadColor.Green);
            using var firstCanvas = CreateCanvas();
            firstCanvas.DocumentViewModel = model;
            var host = Assert.IsType<Direct2DImageRenderHost>(model.RenderSession);
            host.RebuildAll(model.CadEditor.Document);
            Invoke(firstCanvas, "FlushScheduledRender");
            Assert.True(PumpDispatcher(TimeSpan.FromMilliseconds(80)) >= 2);
            AssertCanvasSize(firstCanvas);
            Invoke(firstCanvas, "FlushScheduledRender");
            var expectedPixels = host.CapturePresentedPixels();
            QueueOwnerCallbackAndArmRetry(firstCanvas, host);
            firstCanvas.Dispose();

            using var secondCanvas = CreateCanvas();
            secondCanvas.DocumentViewModel = model;
            host.RebuildAll(model.CadEditor.Document);
            Invoke(secondCanvas, "FlushScheduledRender");
            Assert.True(PumpDispatcher(TimeSpan.FromMilliseconds(80)) >= 2);
            AssertCanvasSize(secondCanvas);
            Invoke(secondCanvas, "FlushScheduledRender");
            var currentSource = Assert.IsType<D3D11ImageSource>(secondCanvas.Source);
            var expectedSurface = Field<nint>(currentSource, "_surface9Ptr");
            Assert.NotEqual(nint.Zero, expectedSurface);
            Assert.Equal(expectedPixels, host.CapturePresentedPixels());

            Assert.True(PumpDispatcher(TimeSpan.FromMilliseconds(80)) >= 2);
            AssertCurrentSurfaceUnchanged(secondCanvas, host, expectedSurface);
            Assert.Equal(expectedPixels, host.CapturePresentedPixels());
            Assert.False(Field<DispatcherTimer>(firstCanvas, "_renderCacheRetryTimer").IsEnabled);
            Assert.Null(Field<object?>(firstCanvas, "_renderCacheRetrySession"));
        });
    }

    [Fact]
    public void SkippingAnAlreadyPrioritizedCaptureIdCountsAsOwnerProgress()
    {
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.MultiThreaded);
        using var preparation = new Direct2DGeometryPreparationService(factory);
        var document = CadDocument.Create("Capture prefix progress");
        var prioritized = document.AddPolyline([new(0, 0), new(10, 0)]);
        var remaining = document.AddPolyline([new(1000, 0), new(1010, 0)]);
        preparation.Schedule(document);
        preparation.Prioritize([prioritized.Id]);
        // Capture and enqueue only the prioritized ID. The open snapshot channel
        // keeps the worker pending after that result is consumed.
        preparation.CaptureStep(new ResourcePreparationBudget(2, TimeSpan.FromSeconds(1)));
        using var first = TakeWithoutCapturing(preparation);
        Assert.Equal(prioritized.Id, first.EntityId);
        preparation.MarkApplied(first.EntityId);

        preparation.CaptureStep(new ResourcePreparationBudget(1, TimeSpan.FromSeconds(1)));
        Assert.True(preparation.IsPending);
        Assert.False(preparation.IsWaitingForResults,
            "Advancing past a prioritized ID must keep owner capture running without a worker retry delay.");

        preparation.CaptureStep(new ResourcePreparationBudget(2, TimeSpan.FromSeconds(1)));
        using var second = TakeWithoutCapturing(preparation);
        Assert.Equal(remaining.Id, second.EntityId);
        Assert.Equal(10, second.Geometry!.ComputeLength(), 3);
        preparation.MarkApplied(second.EntityId);
    }

    private static Direct2DPreparedGeometry TakeWithoutCapturing(Direct2DGeometryPreparationService preparation)
    {
        Direct2DPreparedGeometry? result = null;
        Assert.True(SpinWait.SpinUntil(() => preparation.TryTakeNext(out result), TimeSpan.FromSeconds(5)),
            "The captured geometry must reach the owner.");
        return Assert.IsType<Direct2DPreparedGeometry>(result);
    }

    private static CadDocumentViewModel CreateModel(ServiceProvider services, CadColor background)
    {
        var model = ActivatorUtilities.CreateInstance<CadDocumentViewModel>(services, new CadClipboardStore());
        model.CadEditor.Document.ViewSettings.BackgroundColor = background;
        model.CadEditor.Document.ViewSettings.Grid.Type = CadGridType.None;
        model.CadEditor.Document.ViewSettings.Origin.DisplayType = CadOriginDisplayType.None;
        model.CadEditor.Document.AddPolyline([new(-10, 0), new(10, 0)]);
        model.CadEditor.Viewport.SetSize(64, 64);
        model.CadEditor.Viewport.SetView(1, new(32, 32));
        return model;
    }

    private static CadCanvas CreateCanvas()
    {
        var canvas = new CadCanvas { Width = 64, Height = 64 };
        // Give WPF a real presentation source and client arrange slot. The window
        // stays offscreen and never activates or appears in the taskbar.
        var parent = new Grid { Width = 64, Height = 64 };
        parent.Children.Add(canvas);
        var window = new Window
        {
            Width = 64, Height = 64,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowActivated = false, ShowInTaskbar = false,
            Left = -10000, Top = -10000,
            Content = parent
        };
        Assert.NotNull(_hostWindows);
        _hostWindows.Add(window);
        window.Show();
        Assert.True(PumpDispatcher(TimeSpan.FromMilliseconds(80)) >= 2);
        // Image.ArrangeOverride returns an empty size before its D3DImage has any
        // pixels. Size assertions follow native attachment and its measure pass.
        return canvas;
    }

    private static void AssertCanvasSize(CadCanvas canvas)
    {
        Assert.IsType<Grid>(canvas.Parent);
        Assert.Equal(64, canvas.ActualWidth);
        Assert.Equal(64, canvas.ActualHeight);
    }

    private static void AssertCurrentSurfaceUnchanged(CadCanvas canvas, Direct2DImageRenderHost host, nint expected)
    {
        var actual = Field<nint>(Assert.IsType<D3D11ImageSource>(canvas.Source), "_surface9Ptr");
        var target = Field<ImageSourceDirect2DResource>(host, "_target");
        var activeSurface = Field<Vortice.Direct3D9.IDirect3DSurface9>(target, "_sharedSurface9").NativePointer;
        Assert.Equal(activeSurface, actual);
        Assert.True(expected == actual,
            $"Expected surface {expected}, actual {actual}; target {host.TargetWidth} x {host.TargetHeight}, " +
            $"canvas {canvas.ActualWidth} x {canvas.ActualHeight}.");
    }

    private static void QueueOwnerCallbackAndArmRetry(CadCanvas canvas, ICadRenderSession session)
    {
        // Exercise both cancellation races deterministically; only scheduling is
        // injected. The queued callback and timer run on WPF's real dispatcher.
        Invoke(canvas, "OnRenderCacheBuildRequested", session, EventArgs.Empty);
        Assert.True(Field<bool>(canvas, "_renderCacheBuildScheduled"));
        typeof(CadCanvas).GetField("_renderCacheRetrySession", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(canvas, session);
        var timer = Field<DispatcherTimer>(canvas, "_renderCacheRetryTimer");
        timer.Interval = TimeSpan.FromMilliseconds(1);
        timer.Start();
        Assert.True(timer.IsEnabled);
    }

    private static int PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var passes = 0;
        var progress = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(2)
        };
        var end = new DispatcherTimer(DispatcherPriority.Send) { Interval = duration };
        progress.Tick += (_, _) => passes++;
        end.Tick += (_, _) => frame.Continue = false;
        progress.Start();
        end.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { progress.Stop(); end.Stop(); }
        return passes;
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void Invoke(object owner, string method, params object?[] args)
    {
        try
        {
            owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            _hostWindows = [];
            var asynchronousFailures = new List<Exception>();
            dispatcher.UnhandledException += (_, e) =>
            {
                asynchronousFailures.Add(e.Exception);
                e.Handled = true;
            };
            try
            {
                action();
                Assert.Empty(asynchronousFailures);
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                foreach (var window in _hostWindows)
                {
                    try { window.Close(); }
                    catch (Exception exception) { failure ??= exception; }
                }
                _hostWindows = null;
                dispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF cache lifecycle test timed out.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddMessagePipe();
        services.AddSingleton<ICadRenderSessionFactory>(new Direct2DRenderSessionFactory(new CadRenderResourceBudget()));
        var platform = new PlatformStub();
        services.AddSingleton<IImageImportService>(platform);
        services.AddSingleton<IClipboardTextService>(platform);
        services.AddSingleton<IOleHostService>(platform);
        services.AddSingleton<ISnackbarService>(platform);
        return services.BuildServiceProvider();
    }

    private sealed class PlatformStub : IImageImportService, IClipboardTextService, IOleHostService, ISnackbarService
    {
        public CadImageImportData LoadFromFile(string filePath) => throw new NotSupportedException();
        CadImageImportData? IImageImportService.LoadFromClipboard() => null;
        public string CreatePngDataUrl(CadImageImportData image) => throw new NotSupportedException();
        string? IClipboardTextService.LoadFromClipboard() => null;
        CadOleImportData? IOleHostService.LoadFromClipboard() => null;
        public CadOleDrawData? DrawOleObject(Guid sessionId, CadOleDrawRequest request) => null;
        public void BeginEdit(Guid sessionId, EntityId entityId, byte[] oleBytes, string objectName) { }
        public void EndEditSession(Guid sessionId, EntityId entityId) { }
        public void EndEditSessions(Guid sessionId) { }
        public void ReleaseRenderSession(Guid sessionId, EntityId entityId) { }
        public void ReleaseTransientRenderSession(Guid sessionId, Guid renderId) { }
        public void ReleaseRenderSessions(Guid sessionId) { }
        public void Enqueue(object content, TimeSpan? durationOverride = null, bool promote = false,
            bool neverConsiderToBeDuplicate = false, CadMessageLevel level = CadMessageLevel.Information) { }
        public void EnqueueInAll(object content, TimeSpan? durationOverride = null, bool promote = false,
            bool neverConsiderToBeDuplicate = false, CadMessageLevel level = CadMessageLevel.Information) { }
        public void Enqueue(object identifier, object content, TimeSpan? durationOverride = null, bool promote = false,
            bool neverConsiderToBeDuplicate = false, CadMessageLevel level = CadMessageLevel.Information) { }
    }
}
