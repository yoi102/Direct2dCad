using Direct2dCad.Db;
using Direct2dCad.Rendering;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.ViewModels.Tests;

public sealed class RenderSessionLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructorFailureReleasesRenderSessionAndBudgetEvenWhenOleCleanupFails(bool failCleanup)
    {
        var factory = new BudgetedRenderFactory { FailInitialization = true };
        var ole = new FailingOleHost { FailEndSessions = failCleanup };
        using var provider = CreateProvider(factory, ole);

        var error = Assert.ThrowsAny<Exception>(() => CreateDocument(provider));

        if (failCleanup)
        {
            var aggregate = Assert.IsType<AggregateException>(error);
            Assert.Contains(aggregate.InnerExceptions, exception => exception.Message == "Render initialization failed.");
            Assert.Contains(aggregate.InnerExceptions, exception => exception.Message == "OLE close failed.");
        }
        else
            Assert.Equal("Render initialization failed.", error.Message);
        AssertReleased(factory);
        Assert.Equal(1, ole.EndSessionCalls);
        Assert.Equal(1, ole.ReleaseSessionCalls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void OleDisposalFailureStillReleasesRenderSessionAndBudget(bool failEnd, bool failRelease)
    {
        var factory = new BudgetedRenderFactory();
        var ole = new FailingOleHost { FailEndSessions = failEnd, FailReleaseSessions = failRelease };
        using var provider = CreateProvider(factory, ole);
        var document = CreateDocument(provider);
        document.AttachRenderResources();

        Assert.Throws<InvalidOperationException>(document.Dispose);

        Assert.True(document.IsDisposed);
        AssertReleased(factory);
        Assert.Equal(1, ole.EndSessionCalls);
        Assert.Equal(1, ole.ReleaseSessionCalls);
        document.Dispose();
        Assert.Equal(1, factory.LastSession!.DisposeCount);
        Assert.Equal(1, ole.EndSessionCalls);
        // Former editor event handlers must remain detached after a failed close.
        document.CadEditor.AddLine(default, new(10, 10));
        Assert.Equal(1, ole.EndSessionCalls);
    }

    [Fact]
    public void MultipleCleanupFailuresAreReportedAfterEveryOwnerHasReleasedItsLease()
    {
        var factory = new BudgetedRenderFactory { FailDisposal = true };
        var ole = new FailingOleHost { FailEndSessions = true };
        using var provider = CreateProvider(factory, ole);
        var document = CreateDocument(provider);

        var error = Assert.Throws<AggregateException>(document.Dispose);

        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.Contains(error.InnerExceptions, exception => exception.Message == "OLE close failed.");
        Assert.Contains(error.InnerExceptions, exception => exception.Message == "Render disposal failed.");
        AssertReleased(factory);
        document.Dispose();
    }

    private static void AssertReleased(BudgetedRenderFactory factory)
    {
        Assert.NotNull(factory.LastSession);
        Assert.True(factory.LastSession.IsDisposed);
        Assert.Equal(1, factory.LastSession.DisposeCount);
        Assert.Equal(0, factory.Budget.Statistics.DocumentCount);
    }

    private static CadDocumentViewModel CreateDocument(IServiceProvider provider) =>
        ActivatorUtilities.CreateInstance<CadDocumentViewModel>(provider, new CadClipboardStore());

    private static ServiceProvider CreateProvider(ICadRenderSessionFactory factory, IOleHostService ole)
    {
        var platform = new CadTestPlatform();
        var services = new ServiceCollection();
        services.AddMessagePipe();
        services.AddSingleton(factory);
        services.AddSingleton<IImageImportService>(platform);
        services.AddSingleton<IClipboardTextService>(platform);
        services.AddSingleton<ISnackbarService>(platform);
        services.AddSingleton(ole);
        return services.BuildServiceProvider();
    }

    private sealed class BudgetedRenderFactory : ICadRenderSessionFactory
    {
        public CadRenderResourceBudget Budget { get; } = new();
        public bool FailInitialization { get; init; }
        public bool FailDisposal { get; init; }
        public TestRenderSession? LastSession { get; private set; }

        public ICadRenderSession Create()
        {
            var lease = Budget.RegisterDocument();
            return LastSession = new TestRenderSession
            {
                OleCallbackFailure = FailInitialization ? new InvalidOperationException("Render initialization failed.") : null,
                DisposalCallback = () =>
                {
                    lease.Dispose();
                    if (FailDisposal) throw new InvalidOperationException("Render disposal failed.");
                }
            };
        }
    }

    private sealed class FailingOleHost : IOleHostService
    {
        public bool FailEndSessions { get; init; }
        public bool FailReleaseSessions { get; init; }
        public int EndSessionCalls { get; private set; }
        public int ReleaseSessionCalls { get; private set; }
        public CadOleImportData? LoadFromClipboard() => null;
        public CadOleDrawData? DrawOleObject(Guid sessionId, CadOleDrawRequest request) => null;
        public void BeginEdit(Guid sessionId, EntityId entityId, byte[] oleBytes, string objectName) { }
        public void EndEditSession(Guid sessionId, EntityId entityId) { }
        public void ReleaseRenderSession(Guid sessionId, EntityId entityId) { }
        public void ReleaseTransientRenderSession(Guid sessionId, Guid renderId) { }
        public void EndEditSessions(Guid sessionId)
        {
            EndSessionCalls++;
            if (FailEndSessions) throw new InvalidOperationException("OLE close failed.");
        }
        public void ReleaseRenderSessions(Guid sessionId)
        {
            ReleaseSessionCalls++;
            if (FailReleaseSessions) throw new InvalidOperationException("OLE release failed.");
        }
    }
}
