using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Direct2dCad.CommandLine;
using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using Direct2dCad.wpf.Controls;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class CanvasGestureInterruptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureLossAndUnloadEndPanWithoutDiscardingConfirmedDrawingPoint(bool unload)
    {
        RunSta(() =>
        {
            using var services = CreateServices();
            using var model = ActivatorUtilities.CreateInstance<CadDocumentViewModel>(services, new CadClipboardStore());
            using var canvas = new CadCanvas { DocumentViewModel = model };
            var commandLine = new CadCommandLineService();
            Assert.True(commandLine.Execute("POLYLINE", model).Success);
            Assert.True(commandLine.Execute("10,20", model).Success);
            Assert.Equal(new CadPointD(10, 20), model.DrawingAnchor);
            Assert.True(model.PointerDown(new(40, 50), CadCanvasPointerButton.Right, forcePan: false).CaptureMouse);
            Assert.True(model.IsPanning);

            // Real WPF event routing, without showing a window or stealing desktop input.
            if (unload)
                canvas.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, canvas));
            else
                canvas.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent });

            Assert.False(model.IsPanning);
            Assert.False(canvas.IsRadialMenuActive);
            Assert.Equal(new CadPointD(10, 20), model.DrawingAnchor);
            Assert.True(model.CanUndoCurrentDrawingStep);
        });
    }

    [Fact]
    public void KeyboardFocusMovingToNumericInputPreservesClickStartedGrip()
    {
        RunSta(() =>
        {
            using var services = CreateServices();
            using var model = ActivatorUtilities.CreateInstance<CadDocumentViewModel>(services, new CadClipboardStore());
            using var canvas = new CadCanvas { DocumentViewModel = model };
            model.SetViewportSize(800, 600);
            var line = model.CadEditor.Document.AddLine(new(0, 0), new(100, 0));
            model.SelectEntities([line.Id]);
            model.RequestRender();
            var gripScreen = model.CadEditor.Viewport.WorldToScreen(line.Start);
            Assert.True(model.PointerDown(gripScreen, CadCanvasPointerButton.Left, forcePan: false).Handled);
            model.PointerUp(gripScreen, CadCanvasPointerButton.Left);
            Assert.True(model.IsGripEditing);

            canvas.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, canvas, new TextBox())
                { RoutedEvent = Keyboard.LostKeyboardFocusEvent });

            Assert.True(model.IsGripEditing);
            Assert.False(canvas.IsMouseCaptured);
        });
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

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF gesture test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
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
