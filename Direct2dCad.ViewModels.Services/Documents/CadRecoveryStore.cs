using System.Text.Json;
using Direct2dCad.Editor;
using Direct2dCad.IO;

namespace Direct2dCad.ViewModels.Services.Documents;

public sealed record CadRecoveryEntry(string DocumentId, string Name, string SourcePath, DateTimeOffset SavedAt,
    string SnapshotName, long DocumentVersion)
{
    public string? SessionId { get; init; }
    public string DisplayName => $"{Name} · {SavedAt.ToLocalTime():g}";
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime LocalSavedAt => SavedAt.LocalDateTime;
}

/// <summary>Independent recovery files never become an ordinary save destination.</summary>
public sealed class CadRecoveryStore : IDisposable
{
    public string DirectoryPath { get; }
    public int RetainedVersions { get; init; } = 3;
    public long MaximumBytes { get; init; } = 256L * 1024 * 1024;
    private readonly CadDocumentStorage _storage = new();
    private readonly SemaphoreSlim _writes=new(1,1);
    private readonly string? _sessionId;
    private FileStream? _sessionLease;
    private bool _sessionCompleted;

    public CadRecoveryStore(string? directoryPath = null, bool trackSession = false)
    {
        if (directoryPath is null)
        {
            var requestedDirectory = Environment.GetEnvironmentVariable("DIRECT2DCAD_RECOVERY_DIRECTORY");
            directoryPath = string.IsNullOrWhiteSpace(requestedDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Direct2dCad", "Recovery")
                : requestedDirectory;
        }

        DirectoryPath = Path.GetFullPath(directoryPath);
        _sessionId = trackSession ? Guid.NewGuid().ToString("N") : null;
    }

    private string Resolve(string name)
    {
        if(string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Invalid recovery file name.");
        if (Path.GetFileName(name) != name) throw new InvalidDataException("Invalid recovery file name.");
        var path = Path.GetFullPath(Path.Combine(DirectoryPath, name));
        if (!path.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Recovery path is outside its storage directory.");
        return path;
    }

    public IReadOnlyList<CadRecoveryEntry> List()
    {
        if (!Directory.Exists(DirectoryPath)) return [];
        CleanupAbandonedFiles();
        var entries = new List<CadRecoveryEntry>();
        foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.recovery.json"))
        {
            try
            {
                if (new FileInfo(file).Length > 64 * 1024) continue;
                var entry = JsonSerializer.Deserialize<CadRecoveryEntry>(File.ReadAllText(file));
                if (entry is not null && File.Exists(Resolve(entry.SnapshotName))) entries.Add(entry);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return entries.OrderByDescending(e => e.SavedAt).ToArray();
    }

    /// <summary>Live sessions hold an exclusive lease; only abandoned snapshots are offered for recovery.</summary>
    public IReadOnlyList<CadRecoveryEntry> ListRecoverable()
    {
        var active = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool IsRecoverable(CadRecoveryEntry entry)
        {
            if (entry.SessionId is not { } session) return true;
            if (!active.TryGetValue(session, out var running))
                active[session] = running = IsSessionActive(session);
            return !running;
        }
        return List().Where(IsRecoverable).ToArray();
    }

    private bool IsSessionActive(string sessionId)
    {
        if (!Guid.TryParseExact(sessionId, "N", out _)) return true;
        try
        {
            using var lease = new FileStream(Resolve(sessionId + ".session"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (FileNotFoundException) { return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    public async Task<CadRecoveryEntry> SaveAsync(CadEditor editor, string sourcePath, CancellationToken token = default)
    {
        if(RetainedVersions<1 || MaximumBytes<1) throw new ArgumentOutOfRangeException(nameof(RetainedVersions));
        await _writes.WaitAsync(token);
        try{return await SaveCoreAsync(editor,sourcePath,token);}
        finally{_writes.Release();}
    }
    private async Task<CadRecoveryEntry> SaveCoreAsync(CadEditor editor,string sourcePath,CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_sessionCompleted, this);
        if (editor.Document.IsReadOnly) throw new InvalidOperationException(editor.Document.CompatibilityNotice);
        Directory.CreateDirectory(DirectoryPath);
        if (_sessionId is not null && _sessionLease is null)
            _sessionLease = new FileStream(Resolve(_sessionId + ".session"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        var version = editor.DocumentChangeVersion;
        var id = editor.Document.Id.ToString();
        var entry = new CadRecoveryEntry(id, editor.Document.Name, sourcePath, DateTimeOffset.UtcNow,
            $"{id}-{Guid.NewGuid():N}.d2cad", version) { SessionId = _sessionId };
        var target = Resolve(entry.SnapshotName);
        var metadata = Resolve(entry.SnapshotName + ".recovery.json");
        var temporaryMetadata = metadata + ".tmp";
        try
        {
            await _storage.SaveAsync(editor.Document, target,
                new CadSnapshotCaptureOptions(() => editor.DocumentChangeVersion == version,
                    async ct => await Task.Delay(1, ct)) { UpdateOrigin = false,
                    ExpectedDestination = CadFileRevision.Capture(target) }, token);
            token.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(temporaryMetadata, JsonSerializer.Serialize(entry), token);
            File.Move(temporaryMetadata, metadata);
        }
        catch
        {
            File.Delete(target);
            throw;
        }
        finally { File.Delete(temporaryMetadata); }
        Prune(id, entry.SnapshotName);
        return entry;
    }

    public Task<Direct2dCad.Db.Cad.CadDocument> LoadAsync(CadRecoveryEntry entry, CancellationToken token = default) =>
        _storage.LoadAsync(Resolve(entry.SnapshotName), token);

    public void Remove(CadRecoveryEntry entry)
    {
        File.Delete(Resolve(entry.SnapshotName + ".recovery.json"));
        File.Delete(Resolve(entry.SnapshotName));
    }
    public void RemoveDocument(string documentId)
    {
        foreach(var entry in List().Where(e=>e.DocumentId==documentId)) Remove(entry);
    }

    public async Task RemoveDocumentAsync(string documentId)
    {
        await _writes.WaitAsync();
        try
        {
            foreach (var entry in List().Where(e => e.DocumentId == documentId &&
                (_sessionId is null ? e.SessionId is null || !IsSessionActive(e.SessionId) : e.SessionId == _sessionId)))
                Remove(entry);
        }
        finally { _writes.Release(); }
    }

    public async Task RemoveRecoverableAsync(string? documentId = null)
    {
        await _writes.WaitAsync();
        try
        {
            foreach (var entry in ListRecoverable().Where(e => documentId is null || e.DocumentId == documentId))
                Remove(entry);
        }
        finally { _writes.Release(); }
    }

    public async Task CompleteSessionAsync()
    {
        await _writes.WaitAsync();
        try
        {
            _sessionCompleted = true;
            if (_sessionId is null) return;
            foreach (var entry in List().Where(e => e.SessionId == _sessionId)) Remove(entry);
            Dispose();
            File.Delete(Resolve(_sessionId + ".session"));
        }
        finally { _writes.Release(); }
    }

    // Releasing the lease alone leaves snapshots recoverable, just as process termination does.
    public void Dispose()
    {
        _sessionCompleted = true;
        _sessionLease?.Dispose();
        _sessionLease = null;
    }
    private void CleanupAbandonedFiles()
    {
        var referenced=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var metadata in Directory.EnumerateFiles(DirectoryPath,"*.recovery.json"))
        {
            try
            {
                if(new FileInfo(metadata).Length>64*1024) throw new InvalidDataException("Recovery metadata is too large.");
                var entry=JsonSerializer.Deserialize<CadRecoveryEntry>(File.ReadAllText(metadata));
                if(entry is null || !File.Exists(Resolve(entry.SnapshotName))) throw new InvalidDataException("Recovery snapshot is missing.");
                referenced.Add(entry.SnapshotName);
            }
            catch(Exception ex) when(ex is IOException or JsonException or UnauthorizedAccessException)
            {
                if(DateTime.UtcNow-File.GetLastWriteTimeUtc(metadata)>=TimeSpan.FromHours(1))
                    try{File.Delete(metadata);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
        foreach(var file in Directory.EnumerateFiles(DirectoryPath))
        {
            var name=Path.GetFileName(file);
            if(!(name.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase) || name.EndsWith(".d2cad",StringComparison.OrdinalIgnoreCase) && !referenced.Contains(name))) continue;
            if(DateTime.UtcNow-File.GetLastWriteTimeUtc(file)<TimeSpan.FromHours(1)) continue;
            try { File.Delete(Resolve(name)); } catch(IOException) { } catch(UnauthorizedAccessException) { }
        }
    }

    private void Prune(string documentId, string newest)
    {
        if (new FileInfo(Resolve(newest)).Length > MaximumBytes)
        {
            Remove(List().Single(e => e.SnapshotName == newest));
            throw new IOException("The recovery snapshot exceeds its storage budget.");
        }
        var entries = List();
        long Size()=>Directory.EnumerateFiles(DirectoryPath).Sum(p=>new FileInfo(p).Length);
        foreach (var entry in entries.Where(e => e.DocumentId == documentId && e.SessionId == _sessionId).Skip(RetainedVersions))
        {
            Remove(entry);
        }
        foreach (var entry in List().Reverse())
        {
            if (Size() <= MaximumBytes) break;
            if (entry.SnapshotName == newest) continue;
            if (entry.SessionId is not null && entry.SessionId != _sessionId && IsSessionActive(entry.SessionId)) continue;
            Remove(entry);
        }
        if(Size()>MaximumBytes)
        {
            Remove(List().Single(e=>e.SnapshotName==newest));
            throw new IOException("Recovery storage exceeds its budget, including unfinished files; retry after cleanup.");
        }
    }
}
