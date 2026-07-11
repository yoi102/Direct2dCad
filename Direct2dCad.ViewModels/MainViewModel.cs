using AvalonDock.Core;
using AvalonDock.Mvvm;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO;
using Direct2dCad.ViewModels.Services.ViewServices;
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
    private readonly ICultureSettingService _cultureSettingService;
    private readonly IThemeSettingService _themeSettingService;
    private readonly CadDocumentStorage _storage = new();

    public MainViewModel(IDockLayoutService dockLayoutService, SideToggleManager sideToggleManager,
        ICultureSettingService cultureSettingService,
        IThemeSettingService themeSettingService,
        IFileDialogService fileDialogService,
        IImageImportService imageImportService,
        IDialogService dialogService,
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
        _snackbarService = snackbarService;
        FolderExplorer = _dockLayoutService.GetAnchorable<FolderExplorerToolboxViewModel>() ?? throw new ArgumentNullException(nameof(FolderExplorerToolboxViewModel));
        FolderExplorer.Attach(_dockLayoutService);
        Layers = _dockLayoutService.GetAnchorable<LayerToolboxViewModel>() ?? throw new ArgumentNullException(nameof(LayerToolboxViewModel));
        EntityProperties = _dockLayoutService.GetAnchorable<EntityPropertiesToolboxViewModel>() ?? throw new ArgumentNullException(nameof(EntityPropertiesToolboxViewModel));
        Search = _dockLayoutService.GetAnchorable<SearchViewModel>() ?? throw new ArgumentNullException(nameof(SearchViewModel));

        IsDarkTheme = themeSettingService.IsDarkTheme;
        CurrentCultureLCID = cultureSettingService.GetCurrentCultureLCID();
    }

    /// <summary>The MVVM layout tree — bind to DockLayout on the DockingManager.</summary>
    public IRootDock DockLayout => _dockLayoutService.Layout;

    /// <summary>Exposes the layout service for binding to ToggleDockingManager.LayoutService.</summary>
    public IDockLayoutService LayoutService => _dockLayoutService;

    /// <summary>Provides typed access to the folder explorer VM via the layout service.</summary>
    public FolderExplorerToolboxViewModel FolderExplorer { get; }

    public LayerToolboxViewModel Layers { get; }

    public EntityPropertiesToolboxViewModel EntityProperties { get; }
    public SearchViewModel Search { get; }

    [ObservableProperty]
    public partial EditorTabViewModel? CurrentEditorTabViewModel { get; private set; }

    partial void OnCurrentEditorTabViewModelChanged(EditorTabViewModel? value)
    {
        FolderExplorer.SetActiveDocument(value);
        Layers.Attach(value?.CadDocumentViewModel);
        EntityProperties.Attach(value?.CadDocumentViewModel);
        Search.Attach(value?.CadDocumentViewModel);
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
        FolderExplorer.RefreshDocuments();
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
                FolderExplorer.RefreshDocuments();
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
            FolderExplorer.RefreshDocuments();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowOrReplaceMessageDialogAsync(ex.Message, "Open failed");
        }
    }

    [RelayCommand]
    private void OpenDocumentSettingsDialog()
    {

        //需要输入CurrentEditorTabViewModel 的设置相关内容输入进去。   里面有ok  apply  cancel 进行设置的确认与取消
        _dialogService.OpenDocumentSettingsDialog();



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
        FolderExplorer.RefreshDocuments();
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
            FolderExplorer.RefreshDocuments();
        }
        else
        {
            //TabControlSelectedIndex = 0;
        }
    }

    [RelayCommand]
    private void ActiveContentChanged()
    {
        if (_dockLayoutService.ActiveDockable is EditorTabViewModel editorTabView)
        {
            CurrentEditorTabViewModel = editorTabView;
        }

        //CurrentEditorTabViewModel = _dockLayoutService.ActiveDockable as EditorTabViewModel;
        //TabControlSelectedIndex = CurrentEditorTabViewModel != null ? 1 : 0;
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
    }

    [RelayCommand]
    private void ChangeCulture(string lcidString)
    {
        if (!int.TryParse(lcidString, out var lcid))
            return;
        CurrentCultureLCID = lcid;
        _cultureSettingService.ChangeCulture(lcid);
    }

    [RelayCommand]
    private void ChangeTopmost()
    {
        Topmost = !Topmost;
    }

    #endregion TitleBar
}
