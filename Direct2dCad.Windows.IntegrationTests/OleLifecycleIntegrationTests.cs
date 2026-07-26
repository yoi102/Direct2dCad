using Direct2dCad.Ole.Windows;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class OleLifecycleIntegrationTests
{
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task OleObject_CreatePersistReloadAdviseDrawAndDisposeCompletesLifecycle()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"Direct2dCad-Ole-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(path, "Direct2dCad OLE lifecycle integration");

            await RunOnStaThreadAsync(() =>
            {
                using var initialization = OleInitializationScope.Enter();
                byte[] persistedBytes;
                using (var source = DrawableOleObjectFactory.CreateFromFile(path))
                {
                    var firstDraw = source.Draw(320, 180);
                    Assert.Equal(320 * 180 * 4, firstDraw.Length);
                    persistedBytes = source.GetBackingStorageBytes();
                    Assert.NotEmpty(persistedBytes);
                }

                var viewChangeCount = 0;
                var closeCount = 0;
                var reloaded = DrawableOleObjectFactory.CreateFromBytes(persistedBytes);
                reloaded.HostViewChanged += (_, _) => viewChangeCount++;
                reloaded.HostClosed += (_, _) => closeCount++;
                var adviseSink = new OleAdviseSinkBridge(reloaded);

                adviseSink.OnViewChange(1, -1);
                adviseSink.OnClose();
                var region = reloaded.Draw(640, 360, 120, 80, 200, 140);

                Assert.Equal(1, viewChangeCount);
                Assert.Equal(1, closeCount);
                Assert.Equal(200 * 140 * 4, region.Length);

                reloaded.Dispose();
                Assert.Throws<ObjectDisposedException>(() => reloaded.Draw(32, 32));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Task RunOnStaThreadAsync(Action action)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
