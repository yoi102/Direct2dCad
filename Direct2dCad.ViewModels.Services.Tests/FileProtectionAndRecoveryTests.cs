using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Editor;
using Direct2dCad.IO;
using Direct2dCad.ViewModels.Services.Documents;

namespace Direct2dCad.ViewModels.Services.Tests;

public class FileProtectionAndRecoveryTests
{
    [Fact]
    public async Task ExistingTargetAndExternallyChangedOriginalRequireRevisionApproval()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".d2cad");
        try
        {
            var storage = new CadDocumentStorage();
            var editor = new CadEditor(CadDocument.Create("Protection"));
            storage.Save(editor.Document, path);
            using var fresh = new CadDocumentSaveSession(editor, storage);
            var conflict = await Assert.ThrowsAsync<CadFileConflictException>(() => fresh.SaveAsync(path));
            Assert.True(await fresh.SaveAsync(path, overwriteAuthorization: conflict.CurrentRevision));
            await File.WriteAllTextAsync(path, "external edit");
            await Assert.ThrowsAsync<CadFileConflictException>(() => fresh.SaveCurrentAsync());
            Assert.Equal("external edit", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RevisionChangingAfterAuthorizationIsRejectedAtCommit()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".d2cad");
        try
        {
            var editor = new CadEditor(CadDocument.Create("Race"));
            var storage = new CadDocumentStorage();
            storage.Save(editor.Document, path);
            using var session = new CadDocumentSaveSession(editor, new MutatingWriter(storage), path);
            await Assert.ThrowsAsync<CadFileConflictException>(() => session.SaveCurrentAsync());
            Assert.Equal("external during save", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RecoveryPreservesOriginalAndSaveBaselineAndKeepsBoundedVersions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "D2cadRecoveryTest-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "original.d2cad");
            var storage = new CadDocumentStorage();
            var editor = new CadEditor(CadDocument.Create("Original"));
            storage.Save(editor.Document, source);
            var baseline = CadFileRevision.Capture(source);
            using var session = new CadDocumentSaveSession(editor, storage, source);
            var store = new CadRecoveryStore(Path.Combine(directory, "Recovery")) { RetainedVersions = 2 };
            for (var i = 0; i < 4; i++)
            {
                editor.AddLine(new(i, 0), new(i, 10));
                await store.SaveAsync(editor, source);
            }
            Assert.Equal(2, store.List().Count);
            Assert.True(baseline.Matches(CadFileRevision.Capture(source)));
            Assert.True(session.IsModified);
            var restored = await store.LoadAsync(store.List()[0]);
            Assert.Equal(4, restored.Entities.Values.Count(e => e is CadLine));
            restored.AssignIndependentIdentity();
            Assert.NotEqual(editor.Document.Id, restored.Id);
            using var recoveredSession = new CadDocumentSaveSession(new CadEditor(restored), storage);
            Assert.Equal("", recoveredSession.FilePath);
            Assert.True(recoveredSession.IsModified);
            Assert.True(await session.SaveCurrentAsync());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class MutatingWriter(CadDocumentStorage storage) : ICadDocumentWriter
    {
        public async Task SaveAsync(CadDocument document, string path, CadSnapshotCaptureOptions capture, CancellationToken token = default)
        {
            await File.WriteAllTextAsync(path, "external during save", token);
            await storage.SaveAsync(document, path, capture, token);
        }
    }
}
