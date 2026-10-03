using Direct2dCad.Editor;
using Direct2dCad.IO;

namespace Direct2dCad.ViewModels.Services.Documents;

/// <summary>Serializes saves and owns the saved-state baseline for one editor tab.</summary>
public sealed class CadDocumentSaveSession : IDisposable
{
    private readonly ICadDocumentWriter _writer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource _lifetime = new();
    private CadEditor _editor;
    private object? _savedHistory;
    private long _directVersion;
    private long _savedDirectVersion;
    private bool _disposed;
    private CadFileRevision? _fileRevision;

    public string FilePath { get; private set; }
    public bool IsModified => string.IsNullOrWhiteSpace(FilePath) ||
        !_editor.DocumentHistoryEquals(_savedHistory) || _directVersion != _savedDirectVersion;

    public CadDocumentSaveSession(CadEditor editor, ICadDocumentWriter writer, string filePath = "")
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(filePath);
        _editor = editor;
        _writer = writer;
        FilePath = filePath;
        _savedHistory = editor.CreateDocumentHistorySnapshot();
        _fileRevision = string.IsNullOrWhiteSpace(filePath) ? null :
            CadDocumentOrigin.Get(editor.Document) ?? CadFileRevision.Capture(filePath);
    }

    public void MarkDirectChange() => _directVersion++;

    public void Reset(CadEditor editor, string filePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(filePath);
        _lifetime.Cancel();
        _lifetime.Dispose();
        _lifetime = new();
        _editor = editor;
        FilePath = filePath;
        _savedHistory = editor.CreateDocumentHistorySnapshot();
        _savedDirectVersion = _directVersion;
        _fileRevision = string.IsNullOrWhiteSpace(filePath) ? null :
            CadDocumentOrigin.Get(editor.Document) ?? CadFileRevision.Capture(filePath);
    }

    public Task<bool> SaveAsync(string filePath, Action? committed = null, Func<IDisposable>? beginSave = null,
        CancellationToken cancellationToken = default, CadFileRevision? overwriteAuthorization = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return SaveCoreAsync(filePath, committed, beginSave, cancellationToken, overwriteAuthorization);
    }

    public Task<bool> SaveCurrentAsync(Action? committed = null, Func<IDisposable>? beginSave = null,
        CancellationToken cancellationToken = default, CadFileRevision? overwriteAuthorization = null) =>
        SaveCoreAsync(null, committed, beginSave, cancellationToken, overwriteAuthorization);

    public Task<bool> SaveCompatibilityCopyAsync(string filePath, CadFileRevision expectedDestination,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(FilePath) && string.Equals(Path.GetFullPath(filePath),
            Path.GetFullPath(FilePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A compatible copy must use a different path from the original drawing.");
        return SaveCoreAsync(filePath, null, null, cancellationToken, expectedDestination, isCompatibilityCopy: true);
    }

    private async Task<bool> SaveCoreAsync(string? filePath, Action? committed, Func<IDisposable>? beginSave,
        CancellationToken cancellationToken = default, CadFileRevision? overwriteAuthorization = null, bool isCompatibilityCopy = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_editor.Document.IsReadOnly && !isCompatibilityCopy)
            throw new InvalidOperationException(_editor.Document.CompatibilityNotice);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        await _gate.WaitAsync(token);
        try
        {
            // A queued ordinary save follows an earlier Save As only after it commits.
            filePath ??= FilePath;
            if (string.IsNullOrWhiteSpace(filePath))
                throw new InvalidOperationException("A file path is required before saving the current document.");
            var current = File.Exists(filePath)
                ? await Task.Run(() => CadFileRevision.Capture(filePath), token)
                : CadFileRevision.Capture(filePath);
            var samePath = !string.IsNullOrWhiteSpace(FilePath) &&
                string.Equals(Path.GetFullPath(FilePath), current.FullPath, StringComparison.OrdinalIgnoreCase);
            var expected = overwriteAuthorization ?? (samePath ? _fileRevision : null);
            if (expected is null ? current.Exists : !expected.Matches(current))
                throw new CadFileConflictException(current);
            expected ??= current;
            using var activity = beginSave?.Invoke();
            for (var attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                var editor = _editor;
                var history = editor.CreateDocumentHistorySnapshot();
                var version = editor.DocumentChangeVersion;
                var directVersion = _directVersion;
                var capture = new CadSnapshotCaptureOptions(
                    () => ReferenceEquals(editor, _editor) && editor.DocumentChangeVersion == version && _directVersion == directVersion,
                    async ct => await Task.Delay(1, ct))
                { ExpectedDestination = expected, AllowCompatibilityCopy = isCompatibilityCopy, UpdateOrigin = !isCompatibilityCopy };
                try { await _writer.SaveAsync(editor.Document, filePath, capture, token); }
                catch (CadSnapshotChangedException) when (attempt < 2) { continue; }

                if (token.IsCancellationRequested || !ReferenceEquals(editor, _editor) || _disposed)
                    return false;
                if (isCompatibilityCopy) return true;
                FilePath = filePath;
                _fileRevision = CadDocumentOrigin.Get(editor.Document) ?? CadFileRevision.Capture(filePath);
                _savedHistory = history;
                _savedDirectVersion = directVersion;
                committed?.Invoke();
                return true;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task WaitForIdleAsync()
    {
        await _gate.WaitAsync();
        _gate.Release();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
