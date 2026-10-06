using AvalonDock.Core;
using AvalonDock.Mvvm;
using Direct2dCad.CommandLine;
using Direct2dCad.Db.Cad;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Documents;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.ViewModels.Tools;
using MessagePipe;

namespace Direct2dCad.ViewModels.Tests;

internal sealed class MainWindowTestContext : IDisposable
{
    public CadToolboxTestContext Context { get; } = new();
    public RecordingDialogService Dialogs { get; } = new();
    public RecordingFileDialogs Files { get; } = new();
    public RecordingSettingsStore Settings { get; } = new();
    public RecordingAppearanceServices Appearance { get; } = new();
    public IActiveEditorContext ActiveEditor { get; } = new ActiveEditorContext();
    public IDockLayoutService Layout { get; }
    public MainViewModel ViewModel { get; }
    private readonly List<(CadToolboxTestContext Context, EditorTabViewModel Tab)> _tabs = [];
    private readonly IToolbox[] _toolboxes;
    private readonly ToolExecutionWorkspace _workspace = new();
    private readonly CadRecoveryStore _recoveryStore;
    private readonly bool _ownsRecoveryStore;

    public MainWindowTestContext(CadRecoveryStore? recoveryStore = null, IFileLocationService? fileLocationService = null)
    {
        _ownsRecoveryStore = recoveryStore is null;
        _recoveryStore = recoveryStore ?? new CadRecoveryStore(Path.Combine(Path.GetTempPath(), "CadMainRecovery-" + Guid.NewGuid()), trackSession: true);
        var p = Context.Platform;
        _toolboxes =
        [
            new DocumentExplorerToolboxViewModel(p, p, Context.GetService<ISubscriber<EditorTabDocumentSummaryChangedMessage>>()),
            Context.Layers, Context.Properties, Context.Search, Context.CreateBlocks(Dialogs),
            new DrawingRecoveryToolboxViewModel(p, p),
            new SelectionFilterToolboxViewModel(p, p, Context.GetService<ISubscriber<CadSelectionFilterChangedMessage>>()),
            new CommandLineToolboxViewModel(p, p, new CadCommandLineService(), new CadToolCommandLineService(_workspace),
                Context.GetService<IAsyncSubscriber<CadCommandActivityMessage>>(),
                Context.GetService<IAsyncSubscriber<CadInteractionActivityMessage>>()),
            new MessageToolboxViewModel(p, p, new CadMessageLog()),
            AiAssistantToolboxViewModelTests.CreateDisconnectedViewModel()
        ];
        Layout = new DockLayoutService(_toolboxes);
        ViewModel = new MainViewModel(Layout, new SideToggleManager(Layout), Appearance, Appearance,
            Files, p, Dialogs, Settings, p, ActiveEditor, new TestEditorTabFactory(this), _recoveryStore, fileLocationService);
    }

    public (EditorTabViewModel Tab, RecordingDocumentWriter Writer) AddDocument(string name, bool saved = false)
    {
        var context = new CadToolboxTestContext();
        var writer = new RecordingDocumentWriter();
        var tab = context.CreateEditorTab(Dialogs, Files, writer);
        if (saved)
            tab.Load(CadDocument.Create(name), Path.GetFullPath(name + ".d2cad"));
        else
            tab.TryRenameDocument(name);
        Layout.OpenOrActivateDocument<EditorTabViewModel>(_ => false, () => tab);
        _tabs.Add((context, tab));
        ViewModel.ActiveDockContent = tab;
        return (tab, writer);
    }

    public void Dispose()
    {
        ViewModel.Dispose();
        foreach (var (context, tab) in _tabs)
        {
            tab.Dispose();
            context.Dispose();
        }
        foreach (var toolbox in _toolboxes.OfType<IDisposable>())
            toolbox.Dispose();
        Context.Dispose();
        _workspace.Dispose();
        if (_ownsRecoveryStore)
        {
            _recoveryStore.Dispose();
            if (Directory.Exists(_recoveryStore.DirectoryPath)) Directory.Delete(_recoveryStore.DirectoryPath, true);
        }
    }

    private sealed class TestEditorTabFactory(MainWindowTestContext owner) : IEditorTabFactory
    {
        public EditorTabViewModel Create(Action<EditorTabViewModel>? initialize = null)
        {
            var context = new CadToolboxTestContext();
            var tab = context.CreateEditorTab(owner.Dialogs, owner.Files, new RecordingDocumentWriter());
            try
            {
                initialize?.Invoke(tab);
                owner._tabs.Add((context, tab));
                return tab;
            }
            catch
            {
                tab.Dispose();
                context.Dispose();
                throw;
            }
        }
    }
}

internal sealed class RecordingAppearanceServices : IApplicationThemeService, IApplicationCultureService
{
    public bool IsDarkTheme { get; private set; }
    public CadColor PrimaryColor { get; private set; }
    public CadColor SecondaryColor { get; private set; }
    public int CultureLcid { get; private set; }
    public void ToggleThemeLightDark() => IsDarkTheme = !IsDarkTheme;
    public void ApplyThemeLightDark(bool isDarkTheme) => IsDarkTheme = isDarkTheme;
    public void ApplyThemeColors(CadColor primaryColor, CadColor secondaryColor) =>
        (PrimaryColor, SecondaryColor) = (primaryColor, secondaryColor);
    public void ApplyTheme(bool isDarkTheme, CadColor primaryColor, CadColor secondaryColor)
    {
        ApplyThemeLightDark(isDarkTheme);
        ApplyThemeColors(primaryColor, secondaryColor);
    }
    public void ChangeCulture(string language) => ChangeCulture(int.Parse(language));
    public void ChangeCulture(int lcid) => CultureLcid = lcid;
    public int GetCurrentCultureLCID() => CultureLcid;
}
