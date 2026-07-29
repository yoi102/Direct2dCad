using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;

namespace Direct2dCad.Editor.Tests;

public sealed class CadEditorConcurrencyTests
{
    [Fact]
    public async Task DocumentCommand_HoldsWriteAccessThroughResourcePropagation()
    {
        var document = CadDocument.Create("Concurrency");
        var editor = new CadEditor(document);
        var resources = new BlockingGeometryResourceManager();
        editor.RegisterGeometryResourceManager(
            resources,
            rebuildExistingResources: false);

        var commandTask = Task.Run(() =>
            editor.AddLine(CadPointD.Origin, new CadPointD(10, 0)));

        Assert.True(resources.UpdateEntered.Wait(TimeSpan.FromSeconds(2)));
        var readerAcquired = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var readerTask = Task.Run(() =>
        {
            using var access = document.AcquireReadAccess();
            readerAcquired.SetResult();
        });

        await Task.Delay(50);
        Assert.False(readerAcquired.Task.IsCompleted);

        resources.AllowUpdate.Set();
        await commandTask.WaitAsync(TimeSpan.FromSeconds(2));
        await readerTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(readerAcquired.Task.IsCompletedSuccessfully);
        Assert.Single(document.Entities);
    }

    private sealed class BlockingGeometryResourceManager :
        ICadGeometryResourceManager
    {
        public ManualResetEventSlim UpdateEntered { get; } = new();
        public ManualResetEventSlim AllowUpdate { get; } = new();

        public void RebuildAll(CadDocument document)
        {
        }

        public void ApplyChanges(
            CadDocument document,
            CadDocumentChangeSet changes)
        {
            UpdateEntered.Set();
            if (!AllowUpdate.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Test resource update was not released.");
        }

        public void RebuildEntity(CadDocument document, EntityId entityId)
        {
        }

        public void RemoveEntity(EntityId entityId)
        {
        }
    }
}
