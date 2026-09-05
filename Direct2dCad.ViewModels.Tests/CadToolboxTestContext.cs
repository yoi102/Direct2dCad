using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db;
using Direct2dCad.IO;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using Direct2dCad.ViewModels.Toolboxes;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.ViewModels.Tests;

internal sealed class CadToolboxTestContext : IDisposable
{
    private readonly ServiceProvider _provider;
    public CadDocumentViewModel Document { get; }
    public EntitySearchToolboxViewModel Search { get; }
    public EntityPropertiesToolboxViewModel Properties { get; }
    public LayersToolboxViewModel Layers { get; }
    public CadTestPlatform Platform { get; } = new();

    public CadToolboxTestContext()
    {
        var services = new ServiceCollection();
        services.AddMessagePipe();
        var platform = Platform;
        services.AddSingleton<IImageImportService>(platform);
        services.AddSingleton<IClipboardTextService>(platform);
        services.AddSingleton<IOleHostService>(platform);
        services.AddSingleton<ISnackbarService>(platform);
        _provider = services.BuildServiceProvider();
        Document = ActivatorUtilities.CreateInstance<CadDocumentViewModel>(
            _provider, new CadClipboardStore());
        Search = new EntitySearchToolboxViewModel(platform, platform,
            _provider.GetRequiredService<ISubscriber<CadDocumentInteractionStateChangedMessage>>());
        Properties = new EntityPropertiesToolboxViewModel(platform, platform,
            _provider.GetRequiredService<ISubscriber<CadDocumentInteractionStateChangedMessage>>(),
            _provider.GetRequiredService<ISubscriber<CadBlockDefinitionSelectionChangedMessage>>(),
            platform, platform);
        Layers = new LayersToolboxViewModel(platform, null!, platform, platform,
            _provider.GetRequiredService<ISubscriber<CadDocumentInteractionStateChangedMessage>>());
    }

    public void Publish() => _provider
        .GetRequiredService<IPublisher<CadDocumentInteractionStateChangedMessage>>()
        .Publish(new CadDocumentInteractionStateChangedMessage(Document));

    public T GetService<T>() where T : notnull => _provider.GetRequiredService<T>();

    public EditorTabViewModel CreateEditorTab(RecordingDialogService dialog, IFileDialogService files, ICadDocumentWriter writer) =>
        new(Document, new RecordingSettingsStore(), new RecordingWorkspaceStore(), null!, files, dialog,
            _provider.GetRequiredService<ISnackbarService>(), null!,
            _provider.GetRequiredService<ISubscriber<CadDocumentViewSettingsChangedMessage>>(),
            _provider.GetRequiredService<ISubscriber<CadSelectionFilterChangedMessage>>(),
            _provider.GetRequiredService<ISubscriber<CadDocumentInteractionStateChangedMessage>>(),
            _provider.GetRequiredService<IPublisher<EditorTabDocumentSummaryChangedMessage>>(), writer);

    public BlocksToolboxViewModel CreateBlocks(RecordingDialogService dialog) => new(Platform, Platform, dialog, Platform,
        _provider.GetRequiredService<ISubscriber<CadDocumentInteractionStateChangedMessage>>(),
        _provider.GetRequiredService<IPublisher<CadBlockDefinitionSelectionChangedMessage>>());

    public void Dispose()
    {
        Search.Dispose();
        Properties.Dispose();
        Layers.Dispose();
        Document.Dispose();
        _provider.Dispose();
    }
}

internal sealed class CadTestPlatform : IToolboxLayoutSettingsStore, IToolboxIconProvider,
    IImageImportService, IClipboardTextService, IOleHostService, ISnackbarService, ISystemFontCatalog
{
    public IReadOnlyList<string> FontFamilies => ["Arial"];
    public CadToolboxState? Load(string contentId) => null;
    public void Save(IEnumerable<KeyValuePair<string, CadToolboxState>> toolboxes) { }
    public object Explorer => string.Empty;
    public object Layers => string.Empty;
    public object Blocks => string.Empty;
    public object Terminal => string.Empty;
    public object Search => string.Empty;
    public object Filter => string.Empty;
    public object Git => string.Empty;
    public object Problems => string.Empty;
    public object Assistant => string.Empty;
    public object Messages => string.Empty;
    public CadImageImportData LoadFromFile(string filePath) => throw new NotSupportedException();
    CadImageImportData? IImageImportService.LoadFromClipboard() => null;
    public string CreatePngDataUrl(CadImageImportData image) => throw new NotSupportedException();
    string? IClipboardTextService.LoadFromClipboard() => null;
    CadOleImportData? IOleHostService.LoadFromClipboard() => null;
    public CadOleDrawData? DrawOleObject(Guid sessionId, CadOleDrawRequest request) => null;
    public void BeginEdit(Guid sessionId, EntityId entityId, byte[] oleBytes, string objectName) { }
    public void EndEditSession(Guid sessionId, EntityId entityId) { }
    public void EndEditSessions(Guid sessionId) { }
    public void ReleaseRenderSession(Guid sessionId, EntityId entityId) { }
    public void ReleaseTransientRenderSession(Guid sessionId, Guid renderId) { }
    public void ReleaseRenderSessions(Guid sessionId) { }
    public void Enqueue(object content, TimeSpan? durationOverride = null, bool promote = false,
        bool neverConsiderToBeDuplicate = false, CadMessageLevel level = CadMessageLevel.Information) { }
    public void EnqueueInAll(object content, TimeSpan? durationOverride = null, bool promote = false,
        bool neverConsiderToBeDuplicate = false, CadMessageLevel level = CadMessageLevel.Information) { }
    public void Enqueue(object identifier, object content, TimeSpan? durationOverride = null,
        bool promote = false, bool neverConsiderToBeDuplicate = false,
        CadMessageLevel level = CadMessageLevel.Information) { }
}
