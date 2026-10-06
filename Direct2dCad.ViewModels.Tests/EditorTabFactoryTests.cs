using System.Runtime.CompilerServices;
using AvalonDock.Core;
using AvalonDock.Mvvm;
using Direct2dCad.IO;
using Direct2dCad.Rendering;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using Direct2dCad.ViewModels.Services.Platform.Printing;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.ViewModels.Tests;

public sealed class EditorTabFactoryTests
{
    [Fact]
    public void ClosedDocumentsAreCollectedWhileRootContainerAndAnotherDocumentRemainAlive()
    {
        using var provider = CreateProvider();
        var factory = provider.GetRequiredService<IEditorTabFactory>();
        using var openTab = factory.Create();
        var closedDocuments = Enumerable.Range(0, 8).Select(_ => CreateAndClose(factory)).ToArray();

        ForceCollection();

        Assert.All(closedDocuments, document => Assert.False(document.IsAlive));
        Assert.False(openTab.CadDocumentViewModel.IsDisposed);
        openTab.CadDocumentViewModel.CadEditor.AddLine(default, new(10, 20));
        Assert.Single(openTab.CadDocumentViewModel.CadEditor.Document.Entities);
        GC.KeepAlive(provider);
    }

    [Fact]
    public void RootResolutionIsRejectedAndEachFactoryCallOwnsAnIndependentDocument()
    {
        using var provider = CreateProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<EditorTabViewModel>());
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<CadDocumentViewModel>());
        var factory = provider.GetRequiredService<IEditorTabFactory>();
        using var first = factory.Create();
        using var second = factory.Create();
        Assert.NotSame(first.CadDocumentViewModel, second.CadDocumentViewModel);
        Assert.NotSame(first.CadDocumentViewModel.CadEditor, second.CadDocumentViewModel.CadEditor);
        first.Dispose();
        first.Dispose();
        Assert.True(first.CadDocumentViewModel.IsDisposed);
        Assert.False(second.CadDocumentViewModel.IsDisposed);
    }

    [Fact]
    public void InitializationFailureDisposesAndReleasesTheCreatedDocument()
    {
        using var provider = CreateProvider();
        var document = CreateWithFailedInitialization(provider.GetRequiredService<IEditorTabFactory>());
        ForceCollection();
        Assert.False(document.IsAlive);
        GC.KeepAlive(provider);
    }

    [Fact]
    public void ResolutionFailureDisposesAlreadyCreatedScopedDependencies()
    {
        var services = CreateServices();
        var probe = new DisposalProbe();
        services.AddScoped(_ => probe);
        services.AddScoped<EditorTabViewModel>(provider =>
        {
            _ = provider.GetRequiredService<DisposalProbe>();
            throw new InvalidOperationException("Resolution failed.");
        });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var factory = provider.GetRequiredService<IEditorTabFactory>();

        Assert.Throws<InvalidOperationException>(() => factory.Create());
        Assert.Equal(1, probe.DisposeCount);
    }

    [Fact]
    public void ShellDisposalDisposesAllStillOpenDocuments()
    {
        using var context = new MainWindowTestContext();
        var (first, _) = context.AddDocument("First");
        var (second, _) = context.AddDocument("Second");
        context.ViewModel.Dispose();
        Assert.True(first.CadDocumentViewModel.IsDisposed);
        Assert.True(second.CadDocumentViewModel.IsDisposed);
        Assert.Null(context.ActiveEditor.Current);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndClose(IEditorTabFactory factory)
    {
        var tab = factory.Create();
        var document = tab.CadDocumentViewModel.CadEditor.Document;
        tab.CadDocumentViewModel.CadEditor.AddLine(default, new(10, 10));
        var reference = new WeakReference(document);
        tab.Dispose();
        Assert.True(tab.CadDocumentViewModel.IsDisposed);
        return reference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateWithFailedInitialization(IEditorTabFactory factory)
    {
        EditorTabViewModel? created = null;
        Assert.Throws<InvalidDataException>(() => factory.Create(tab =>
        {
            created = tab;
            throw new InvalidDataException("The drawing could not be initialized.");
        }));
        Assert.NotNull(created);
        Assert.True(created.CadDocumentViewModel.IsDisposed);
        return new WeakReference(created.CadDocumentViewModel.CadEditor.Document);
    }

    private static void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static ServiceProvider CreateProvider() =>
        CreateServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    [Fact]
    public async Task WorkspaceCloseOwnsDisposalWithoutAWindowClosedEvent()
    {
        using var provider = CreateProvider();
        var workspace = provider.GetRequiredService<ICadToolWorkspace>();
        var dialog = (RecordingDialogService)provider.GetRequiredService<IDialogService>();
        var document = workspace.CreateDocument("Workspace lifetime");
        document.Session.CadEditor.AddLine(default, new(10, 0));
        Assert.False(await workspace.CloseDocumentAsync(document.DocumentId));
        Assert.False(document.Session.IsDisposed);

        dialog.CloseResult = UnsavedDocumentDialogResult.Discard;
        Assert.True(await workspace.CloseDocumentAsync(document.DocumentId));
        Assert.True(document.Session.IsDisposed);
        Assert.Empty(workspace.GetDocuments());
        Assert.Null(provider.GetRequiredService<Direct2dCad.ViewModels.Tools.IActiveEditorContext>().Current);
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddViewModels();
        services.AddSingleton<ICadRenderSessionFactory, TestRenderSessionFactory>();
        services.AddMessagePipe();
        var platform = new CadTestPlatform();
        services.AddSingleton<IImageImportService>(platform);
        services.AddSingleton<IClipboardTextService>(platform);
        services.AddSingleton<IOleHostService>(platform);
        services.AddSingleton<ISnackbarService>(platform);
        services.AddSingleton<IUserSettingsStore, RecordingSettingsStore>();
        services.AddSingleton<IWorkspaceSettingsStore, RecordingWorkspaceStore>();
        services.AddSingleton<IDockLayoutService>(new DockLayoutService([]));
        services.AddSingleton<IFileDialogService, RecordingFileDialogs>();
        services.AddSingleton<IDialogService, RecordingDialogService>();
        services.AddSingleton<ICadPrintService, NullPrintService>();
        services.AddSingleton<ICadDocumentWriter, RecordingDocumentWriter>();
        return services;
    }

    private sealed class DisposalProbe : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    private sealed class NullPrintService : ICadPrintService
    {
        public Task<bool> PrintAsync(CadPrintRequest request, Action? onPrintStarted = null,
            Action<bool>? onBusyChanged = null, Action? onPrintCompleted = null,
            Action<CadPrintCompletion>? onPrintFinished = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
