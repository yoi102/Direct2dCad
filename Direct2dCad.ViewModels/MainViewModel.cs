using AvalonDock.Core;
using AvalonDock.Mvvm;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Settings;
using Direct2dCad.ViewModels.Settings.UserSettings;
using Direct2dCad.ViewModels.Toolboxes;

namespace Direct2dCad.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialogService;
    private readonly IImageImportService _imageImportService;
    private readonly IDialogService _dialogService;
    private readonly ISnackbarService _snackbarService;
    private readonly IDockLayoutService _dockLayoutService;
    private readonly SideToggleManager _sideToggleManager;
    private readonly IApplicationCultureService _cultureSettingService;
    private readonly IApplicationThemeService _themeSettingService;
    private readonly IUserSettingsStore _userSettingsStore;
    private readonly CadUserSettings _userSettings;
    private readonly CadDocumentStorage _storage = new();

    public MainViewModel(IDockLayoutService dockLayoutService, SideToggleManager sideToggleManager,
        IApplicationCultureService cultureSettingService,
        IApplicationThemeService themeSettingService,
        IFileDialogService fileDialogService,
        IImageImportService imageImportService,
        IDialogService dialogService,
        IUserSettingsStore userSettingsStore,
        ISnackbarService snackbarService
        )
    {
        dockLayoutService.AnchorableStateChanged += OnAnchorableStateChanged;
        _dockLayoutService = dockLayoutService;
        _sideToggleManager = sideToggleManager;
        _cultureSettingService = cultureSettingService;
        _themeSettingService = themeSettingService;

        _fileDialogService = fileDialogService;
        _imageImportService = imageImportService;
        _dialogService = dialogService;
        _userSettingsStore = userSettingsStore;
        _userSettings = userSettingsStore.Load();
        _snackbarService = snackbarService;
        DocumentExplorer = _dockLayoutService.GetAnchorable<DocumentExplorerToolboxViewModel>() ?? throw new ArgumentNullException(nameof(DocumentExplorerToolboxViewModel));
        DocumentExplorer.Attach(_dockLayoutService);
        Layers = _dockLayoutService.GetAnchorable<LayersToolboxViewModel>() ?? throw new ArgumentNullException(nameof(LayersToolboxViewModel));
        Blocks = _dockLayoutService.GetAnchorable<BlocksToolboxViewModel>() ?? throw new ArgumentNullException(nameof(BlocksToolboxViewModel));
        EntityProperties = _dockLayoutService.GetAnchorable<EntityPropertiesToolboxViewModel>() ?? throw new ArgumentNullException(nameof(EntityPropertiesToolboxViewModel));
        EntitySearch = _dockLayoutService.GetAnchorable<EntitySearchToolboxViewModel>() ?? throw new ArgumentNullException(nameof(EntitySearchToolboxViewModel));
        SelectionFilter = _dockLayoutService.GetAnchorable<SelectionFilterToolboxViewModel>() ?? throw new ArgumentNullException(nameof(SelectionFilterToolboxViewModel));
        CommandLine = _dockLayoutService.GetAnchorable<CommandLineToolboxViewModel>() ?? throw new ArgumentNullException(nameof(CommandLineToolboxViewModel));

        IsDarkTheme = _userSettings.General.IsDarkTheme;
        _themeSettingService.ApplyTheme(
            _userSettings.General.IsDarkTheme,
            _userSettings.General.PrimaryColor,
            _userSettings.General.SecondaryColor);
        CurrentCultureLCID = _userSettings.General.CultureLcid;
        cultureSettingService.ChangeCulture(CurrentCultureLCID);
    }

    /// <summary>The MVVM layout tree — bind to DockLayout on the DockingManager.</summary>
    public IRootDock DockLayout => _dockLayoutService.Layout;

    /// <summary>Exposes the layout service for binding to ToggleDockingManager.LayoutService.</summary>
    public IDockLayoutService LayoutService => _dockLayoutService;

    /// <summary>Provides typed access to the open-document explorer via the layout service.</summary>
    public DocumentExplorerToolboxViewModel DocumentExplorer { get; }

    public LayersToolboxViewModel Layers { get; }
    public BlocksToolboxViewModel Blocks { get; }

    public EntityPropertiesToolboxViewModel EntityProperties { get; }
    public EntitySearchToolboxViewModel EntitySearch { get; }
    public SelectionFilterToolboxViewModel SelectionFilter { get; }
    public CommandLineToolboxViewModel CommandLine { get; }

    [ObservableProperty]
    public partial EditorTabViewModel? CurrentEditorTabViewModel { get; private set; }

    partial void OnCurrentEditorTabViewModelChanged(EditorTabViewModel? value)
    {
        DocumentExplorer.SetActiveDocument(value);
        Layers.Attach(value?.CadDocumentViewModel);
        EntityProperties.Attach(value?.CadDocumentViewModel);
        Blocks.Attach(value?.CadDocumentViewModel);
        EntitySearch.Attach(value?.CadDocumentViewModel);
        SelectionFilter.Attach(value?.CadDocumentViewModel);
        CommandLine.Attach(value?.CadDocumentViewModel);
    }

    [ObservableProperty]
    public partial bool IsPrimarySideBarOpen { get; set; }

    [ObservableProperty]
    public partial bool IsBottomPanelOpen { get; set; }

    [ObservableProperty]
    public partial bool IsSecondarySideBarOpen { get; set; }

    [ObservableProperty]
    public partial int TabControlSelectedIndex { get; set; } = 0;

    private void OnAnchorableStateChanged(object? sender, EventArgs e)
    {
        IsPrimarySideBarOpen = _dockLayoutService.IsSideOpen(ToolboxSide.Left);
        IsBottomPanelOpen = _dockLayoutService.IsSideOpen(ToolboxSide.Bottom);
        IsSecondarySideBarOpen = _dockLayoutService.IsSideOpen(ToolboxSide.Right);
    }

    [RelayCommand]
    private void TogglePrimarySideBar() => _sideToggleManager.Toggle(ToolboxSide.Left);

    [RelayCommand]
    private void ToggleBottomPanel() => _sideToggleManager.Toggle(ToolboxSide.Bottom);

    [RelayCommand]
    private void ToggleSecondarySideBar() => _sideToggleManager.Toggle(ToolboxSide.Right);

    [RelayCommand]
    private void New()
    {
        var tab = _dockLayoutService.OpenOrActivateDocument(
           e => false,
           () =>
           {
               var newTab = Ioc.Default.GetRequiredService<EditorTabViewModel>();
               _snackbarService.Enqueue("New document created.");
               return newTab;
           });

        CurrentEditorTabViewModel = tab;
        DocumentExplorer.RefreshDocuments();
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var fileName = _fileDialogService.OpenD2cadFile();
        if (fileName is null)
            return;

        try
        {
            var existingTab = _dockLayoutService.Documents
                .OfType<EditorTabViewModel>()
                .FirstOrDefault(x => string.Equals(
                    x.CurrentFilePath,
                    fileName,
                    StringComparison.OrdinalIgnoreCase));
            if (existingTab is not null)
            {
                _dockLayoutService.ActiveDockable = existingTab;
                CurrentEditorTabViewModel = existingTab;
                DocumentExplorer.RefreshDocuments();
                return;
            }

            Direct2dCad.Db.Cad.CadDocument document;
            using (_dialogService.ShowProgressBarDialog())
                document = await _storage.LoadAsync(fileName);

            var tab = _dockLayoutService.OpenOrActivateDocument(
            e => e.CurrentFilePath == fileName,
            () =>
            {
                var newTab = Ioc.Default.GetRequiredService<EditorTabViewModel>();
                newTab.Load(document, fileName);
                _snackbarService.Enqueue("File opened successfully.");
                return newTab;
            });

            CurrentEditorTabViewModel = tab;
            DocumentExplorer.RefreshDocuments();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowOrReplaceMessageDialogAsync(ex.Message, "Open failed");
        }
    }

    [RelayCommand]
    private void OpenDocumentSettingsDialog()
    {
        if (CurrentEditorTabViewModel is null)
            return;

        _dialogService.ShowDocumentSettingsDialog(
            new DocumentSettingsViewModel(CurrentEditorTabViewModel, _dialogService));
    }

    [RelayCommand]
    private void OpenUserSettingsDialog()
    {
        _dialogService.ShowUserSettingsDialog(
            new UserSettingsViewModel(_userSettings, _userSettingsStore, ApplyUserSettings));
    }

    private void ApplyUserSettings(CadUserSettings settings)
    {
        _userSettings.CopyFrom(settings);
        IsDarkTheme = _userSettings.General.IsDarkTheme;
        _themeSettingService.ApplyTheme(
            _userSettings.General.IsDarkTheme,
            _userSettings.General.PrimaryColor,
            _userSettings.General.SecondaryColor);

        if (CurrentCultureLCID != _userSettings.General.CultureLcid)
        {
            CurrentCultureLCID = _userSettings.General.CultureLcid;
            _cultureSettingService.ChangeCulture(CurrentCultureLCID);
        }

        foreach (var editorTab in _dockLayoutService.Documents.OfType<EditorTabViewModel>())
            editorTab.ApplyUserSettings(_userSettings);
    }

    [RelayCommand]
    private void InsertImageFromFile()
    {
        var fileName = _fileDialogService.OpenImageFile();
        if (fileName is null)
            return;

        try
        {
            InsertImage(_imageImportService.LoadFromFile(fileName));
            _snackbarService.Enqueue("Image inserted.");
        }
        catch (Exception ex)
        {
            _ = _dialogService.ShowOrReplaceMessageDialogAsync(ex.Message, "Insert image failed");
        }
    }

    [RelayCommand]
    private void PasteImageFromClipboard()
    {
        try
        {
            var image = _imageImportService.LoadFromClipboard();
            if (image is null)
            {
                _snackbarService.Enqueue("Clipboard does not contain an image.");
                return;
            }

            InsertImage(image);
            _snackbarService.Enqueue("Image pasted.");
        }
        catch (Exception ex)
        {
            _ = _dialogService.ShowOrReplaceMessageDialogAsync(ex.Message, "Paste image failed");
        }
    }

    private void InsertImage(CadImageImportData image)
    {
        var documentViewModel = CurrentEditorTabViewModel?.CadDocumentViewModel;
        if (documentViewModel is null)
        {
            _snackbarService.Enqueue("Open or create a document before inserting an image.");
            return;
        }

        var bounds = CreateImageBounds(documentViewModel, image.PixelWidth, image.PixelHeight);
        var entityId = documentViewModel.CadEditor.AddImage(
            bounds,
            image.PixelWidth,
            image.PixelHeight,
            image.Stride,
            image.Pixels,
            documentViewModel.DrawingLayerId,
            image.ContentType,
            image.SourceName,
            image.SourceName);

        documentViewModel.SelectEntities([entityId]);
        DocumentExplorer.RefreshDocuments();
    }

    private static CadRectD CreateImageBounds(
        CadDocumentViewModel documentViewModel,
        int pixelWidth,
        int pixelHeight)
    {
        var viewportBounds = documentViewModel.CadEditor.Viewport.VisibleWorldBounds;
        if (viewportBounds.IsEmpty)
        {
            var fallbackWidth = Math.Max(pixelWidth, 1);
            var fallbackHeight = Math.Max(pixelHeight, 1);
            return CadRectD.FromCenter(CadPointD.Origin, fallbackWidth, fallbackHeight);
        }

        var maxPixelSide = Math.Max(pixelWidth, pixelHeight);
        if (maxPixelSide <= 0)
            maxPixelSide = 1;

        var maxWorldSide = Math.Max(
            Math.Min(viewportBounds.Width, viewportBounds.Height) * 0.35,
            1.0);
        var scale = maxWorldSide / maxPixelSide;
        var width = Math.Max(pixelWidth * scale, 1.0);
        var height = Math.Max(pixelHeight * scale, 1.0);

        return CadRectD.FromCenter(viewportBounds.Center, width, height);
    }

    public async Task<bool> ConfirmCloseApplicationAsync()
    {
        var modifiedDocuments = _dockLayoutService.Documents
            .OfType<EditorTabViewModel>()
            .Where(document => document.IsModified)
            .ToArray();
        if (modifiedDocuments.Length == 0)
            return true;

        var result = await _dialogService.ShowUnsavedDocumentsDialogAsync(
            modifiedDocuments
                .Select(document => new UnsavedDocumentInfo(
                    document.DocumentName,
                    document.CurrentFilePath))
                .ToArray());
        if (result == UnsavedDocumentDialogResult.Cancel)
            return false;
        if (result == UnsavedDocumentDialogResult.Discard)
            return true;

        foreach (var document in modifiedDocuments)
        {
            if (!await document.SaveForCloseAsync())
                return false;
        }

        return true;
    }

    [RelayCommand]
    private void DocumentClosed(object content)
    {
        if (content is EditorTabViewModel editorTabViewModel)
        {
            if (CurrentEditorTabViewModel == editorTabViewModel)
            {
                CurrentEditorTabViewModel = null;
            }
            editorTabViewModel.Dispose();
            DocumentExplorer.RefreshDocuments();
        }


        if (CurrentEditorTabViewModel is null)
        {
            TabControlSelectedIndex = 0;
        }
    }

    [RelayCommand]
    private void ActiveContentChanged()
    {
        if (_dockLayoutService.ActiveDockable is EditorTabViewModel editorTabView)
        {
            CurrentEditorTabViewModel = editorTabView;
        }


    }

    #region TitleBar

    [ObservableProperty]
    public partial bool Topmost { get; set; }

    [ObservableProperty]
    public partial int CurrentCultureLCID { get; set; }

    [ObservableProperty]
    public partial bool IsDarkTheme { get; set; }

    partial void OnIsDarkThemeChanged(bool value)
    {
        _themeSettingService.ApplyThemeLightDark(value);
        if (_userSettings is null)
            return;

        _userSettings.General.IsDarkTheme = value;
        SaveUserSettings();
    }

    [RelayCommand]
    private void ChangeCulture(string lcidString)
    {
        if (!int.TryParse(lcidString, out var lcid))
            return;
        CurrentCultureLCID = lcid;
        _cultureSettingService.ChangeCulture(lcid);
        _userSettings.General.CultureLcid = lcid;
        SaveUserSettings();
    }

    [RelayCommand]
    private void ChangeTopmost()
    {
        Topmost = !Topmost;
    }

    private void SaveUserSettings()
    {
        try
        {
            _userSettingsStore.Save(_userSettings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = _dialogService.ShowOrReplaceMessageDialogAsync(ex.Message, "User settings save failed");
        }
    }

    #endregion TitleBar
}
