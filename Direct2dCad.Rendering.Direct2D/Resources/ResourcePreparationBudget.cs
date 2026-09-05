namespace Direct2dCad.Rendering.Direct2D.Resources;

internal sealed class ResourcePreparationBudget(
    int maximumItems,
    TimeSpan maximumDuration,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly long _started = (timeProvider ?? TimeProvider.System).GetTimestamp();
    public int ProcessedItems { get; private set; }

    public bool TryStartItem()
    {
        if (ProcessedItems >= maximumItems ||
            (ProcessedItems > 0 && _clock.GetElapsedTime(_started) >= maximumDuration))
            return false;
        ProcessedItems++;
        return true;
    }
}
