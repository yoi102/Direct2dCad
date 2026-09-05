using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Platform;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadOleSessionControllerTests
{
    [Fact]
    public void PersistedUpdatesAreUndoableAndViewChangesDoNotCreateHistory()
    {
        using var context = new Context();
        var original = context.Entity.CopyOleBytes();
        context.Controller.BeginEdit(context.Entity);
        context.Publish([2, 3]);
        Assert.Equal(new byte[] { 2, 3 }, context.Entity.CopyOleBytes());
        Assert.Empty(context.Host.Closed);
        context.Editor.Undo();
        Assert.Equal(original, context.Entity.CopyOleBytes());
        Assert.Contains(context.Entity.Id, context.Host.Closed);
        context.Editor.Redo();
        Assert.Equal(new byte[] { 2, 3 }, context.Entity.CopyOleBytes());

        var history = context.Editor.CreateDocumentHistorySnapshot();
        context.Publish(null, persisted: false);
        Assert.Equal([context.Entity.Id], context.Invalidated);
        Assert.True(context.Editor.DocumentHistoryEquals(history));
    }

    [Fact]
    public void DeletionClosesTheOpenServerAndReleasesRendering()
    {
        using var context = new Context();
        context.Controller.BeginEdit(context.Entity);
        context.Editor.DeleteEntities([context.Entity.Id]);
        Assert.Contains(context.Entity.Id, context.Host.Closed);
        Assert.Contains(context.Entity.Id, context.Host.Released);
    }

    [Fact]
    public void ReplacingDocumentSuppressesReentrantSaveAndDisposeUnsubscribes()
    {
        using var context = new Context();
        var original = context.Entity.CopyOleBytes();
        context.Controller.BeginEdit(context.Entity);
        context.Host.OnCloseAll = () => context.Publish([99]);
        context.Controller.ReplaceEditor(new CadEditor(CadDocument.Create("Next")));
        Assert.Equal(original, context.Entity.CopyOleBytes());
        context.Controller.Dispose();
        context.Publish([88]);
        Assert.Equal(original, context.Entity.CopyOleBytes());
        Assert.Equal(2, context.Host.AllReleased);
    }

    [Fact]
    public void LockingLayerClosesSessionAndRejectsFurtherUpdates()
    {
        using var context = new Context();
        context.Controller.BeginEdit(context.Entity);
        context.Editor.Execute(new Direct2dCad.Commands.SetLayerStateCommand(LayerId.Default, true, true, false));
        Assert.Contains(context.Entity.Id, context.Host.Closed);
        var before = context.Entity.CopyOleBytes();
        context.Publish([9]);
        Assert.Equal(before, context.Entity.CopyOleBytes());
    }

    [Fact]
    public void ReplacedDocumentRejectsLateUpdatesEvenWhenEntityIdsMatch()
    {
        using var context = new Context();
        context.Controller.BeginEdit(context.Entity);
        var oldSession = context.Host.SessionId;
        var next = new CadEditor(CadDocument.Create("Next"));
        var nextEntity = next.Document.AddOleObject(context.Entity.Bounds, [5]);
        Assert.Equal(context.Entity.Id, nextEntity.Id);
        context.Controller.ReplaceEditor(next);
        context.Controller.BeginEdit(nextEntity);
        Assert.NotEqual(oldSession, context.Host.SessionId);
        context.Publish([99], sessionId: oldSession);
        Assert.Equal(new byte[] { 5 }, nextEntity.CopyOleBytes());
        Assert.False(next.DocumentCommands.CanUndo);
    }

    private sealed class Context : IDisposable
    {
        private readonly ServiceProvider _provider;
        public CadEditor Editor { get; } = new(CadDocument.Create("OLE"));
        public Direct2dCad.Db.Data.Entities.CadOleObject Entity { get; }
        public Host Host { get; } = new();
        public List<EntityId> Invalidated { get; } = [];
        public CadOleSessionController Controller { get; }

        public Context()
        {
            var services = new ServiceCollection();
            services.AddMessagePipe();
            _provider = services.BuildServiceProvider();
            Entity = Editor.Document.AddOleObject(CadRectD.FromXYWH(0, 0, 10, 10), [1]);
            Controller = new(Editor, Host, _provider.GetRequiredService<ISubscriber<CadOleObjectUpdatedMessage>>(), Invalidated.Add);
        }

        public void Publish(byte[]? bytes, bool persisted = true, Guid? sessionId = null) =>
            _provider.GetRequiredService<IPublisher<CadOleObjectUpdatedMessage>>().Publish(new(
                sessionId ?? Host.SessionId, Entity.Id,
                bytes is null ? null : new CadOleImportData(bytes, Entity.ContentType, Entity.SourceName, 1), persisted));

        public void Dispose()
        {
            Controller.Dispose();
            _provider.Dispose();
        }
    }

    private sealed class Host : IOleHostService
    {
        public Guid SessionId { get; private set; }
        public List<EntityId> Closed { get; } = [];
        public List<EntityId> Released { get; } = [];
        public Action? OnCloseAll { get; set; }
        public int AllReleased { get; private set; }
        public CadOleImportData? LoadFromClipboard() => null;
        public CadOleDrawData? DrawOleObject(Guid sessionId, CadOleDrawRequest request) => null;
        public void BeginEdit(Guid sessionId, EntityId entityId, byte[] oleBytes, string objectName) => SessionId = sessionId;
        public void EndEditSession(Guid sessionId, EntityId entityId) => Closed.Add(entityId);
        public void EndEditSessions(Guid sessionId) => OnCloseAll?.Invoke();
        public void ReleaseRenderSession(Guid sessionId, EntityId entityId) => Released.Add(entityId);
        public void ReleaseTransientRenderSession(Guid sessionId, Guid renderId) { }
        public void ReleaseRenderSessions(Guid sessionId) => AllReleased++;
    }
}
