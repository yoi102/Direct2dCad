using System.Diagnostics;

namespace Direct2dCad.Rendering.Direct2D.Hosting;

internal sealed class Direct2DFrameRateTracker
{
    private const double SampleWindowSeconds = 0.5;
    private const int SampleResetMilliseconds = 750;
    private readonly Queue<FrameSample> _samples = [];
    private long _lastFrameTimestamp;
    private double _renderDurationSecondsTotal;

    public double FramesPerSecond { get; private set; }

    public double LastFrameRenderTimeMilliseconds { get; private set; }

    public double AverageFrameRenderTimeMilliseconds { get; private set; }

    public double LastFullFrameRenderTimeMilliseconds { get; private set; }

    public void Record(long frameStartTimestamp, bool isFullFrame)
    {
        var frameEndTimestamp = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(
            frameStartTimestamp,
            frameEndTimestamp).TotalSeconds;
        if (!double.IsFinite(elapsed) || elapsed <= 0)
            return;

        LastFrameRenderTimeMilliseconds = elapsed * 1000.0;
        if (isFullFrame)
            LastFullFrameRenderTimeMilliseconds = LastFrameRenderTimeMilliseconds;

        if (_lastFrameTimestamp != 0 &&
            Stopwatch.GetElapsedTime(
                _lastFrameTimestamp,
                frameEndTimestamp).TotalMilliseconds > SampleResetMilliseconds)
        {
            _samples.Clear();
            _renderDurationSecondsTotal = 0;
        }

        _lastFrameTimestamp = frameEndTimestamp;
        _samples.Enqueue(new FrameSample(frameEndTimestamp, elapsed));
        _renderDurationSecondsTotal += elapsed;
        while (_samples.Count > 1 &&
               Stopwatch.GetElapsedTime(
                   _samples.Peek().CompletionTimestamp,
                   frameEndTimestamp).TotalSeconds > SampleWindowSeconds)
        {
            _renderDurationSecondsTotal -= _samples.Dequeue().RenderDurationSeconds;
        }

        _renderDurationSecondsTotal = Math.Max(0, _renderDurationSecondsTotal);
        var averageRenderDuration = _samples.Count > 0
            ? _renderDurationSecondsTotal / _samples.Count
            : 0;
        AverageFrameRenderTimeMilliseconds = averageRenderDuration * 1000.0;
        FramesPerSecond = averageRenderDuration > 0
            ? 1.0 / averageRenderDuration
            : 0;
    }

    public void Reset()
    {
        _samples.Clear();
        _lastFrameTimestamp = 0;
        _renderDurationSecondsTotal = 0;
        FramesPerSecond = 0;
        LastFrameRenderTimeMilliseconds = 0;
        AverageFrameRenderTimeMilliseconds = 0;
        LastFullFrameRenderTimeMilliseconds = 0;
    }

    private readonly record struct FrameSample(
        long CompletionTimestamp,
        double RenderDurationSeconds);
}
