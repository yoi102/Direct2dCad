namespace Direct2dCad.ViewModels.Rendering;

internal sealed class CadDeferredRenderScheduler(int delayMilliseconds)
{
    private int _version;

    public void Schedule(Action render, Func<bool> isCanceled)
    {
        var context = SynchronizationContext.Current;
        if (context is null)
            return;

        var version = Interlocked.Increment(ref _version);
        _ = Task.Delay(delayMilliseconds).ContinueWith(
            task =>
            {
                if (!task.IsCompletedSuccessfully)
                    return;

                context.Post(
                    _ =>
                    {
                        if (isCanceled() || version != Volatile.Read(ref _version))
                            return;

                        render();
                    },
                    null);
            },
            TaskScheduler.Default);
    }
}
