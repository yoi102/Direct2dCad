using Direct2dCad.Db.Cad;

namespace Direct2dCad.IO;

public interface ICadDocumentWriter
{
    Task SaveAsync(CadDocument document, string filePath, CadSnapshotCaptureOptions capture,
        CancellationToken cancellationToken = default);
}

/// <summary>Callbacks run on the document owner thread, never on the serializer worker.</summary>
public sealed record CadSnapshotCaptureOptions(
    Func<bool> IsCurrent,
    Func<CancellationToken, ValueTask> YieldAsync)
{
    public TimeSpan MaximumSliceDuration { get; init; } = TimeSpan.FromMilliseconds(4);
}

public sealed class CadSnapshotChangedException() : InvalidOperationException(
    "The document changed while preparing its save snapshot. Please retry saving.");
