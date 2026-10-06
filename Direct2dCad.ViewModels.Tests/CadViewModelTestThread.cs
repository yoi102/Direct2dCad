using System.Collections.Concurrent;

namespace Direct2dCad.ViewModels.Tests;

/// <summary>
/// Runs asynchronous interaction tests with the same serialized continuation contract as the WPF
/// dispatcher. View models and their mutable overlay scenes are UI-thread-owned, not thread safe.
/// </summary>
internal static class CadViewModelTestThread
{
    public static Task RunAsync(Func<Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var context = new SerialContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                context.Post(async _ =>
                {
                    try
                    {
                        await test();
                        completion.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                    finally
                    {
                        context.Complete();
                    }
                }, null);
                context.Run();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }
        }) { IsBackground = true, Name = "CAD view model test thread" };
        thread.Start();
        return completion.Task;
    }

    private sealed class SerialContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));

        public void Run()
        {
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                callback(state);
        }

        public void Complete() => _queue.CompleteAdding();
        public void Dispose() => _queue.Dispose();
    }
}
