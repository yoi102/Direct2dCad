using AvalonDock.Core;
using AvalonDock.Mvvm;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.ViewModels.Services;
using Direct2dCad.ViewModels.Toolboxes;

namespace Direct2dCad.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialogService;
    private readonly IDialogService _dialogService;
    private readonly ISnackbarService _snackbarService;
    private readonly IDockLayoutService _dockLayoutService;
    private readonly SideToggleManager _sideToggleManager;
    private readonly ICultureSettingService _cultureSettingService;
    private readonly IThemeSettingService _themeSettingService;

    public MainViewModel(IDockLayoutService dockLayoutService, SideToggleManager sideToggleManager,
        ICultureSettingService cultureSettingService,
        IThemeSettingService themeSettingService,
        IFileDialogService fileDialogService,
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
        _dialogService = dialogService;
        _snackbarService = snackbarService;
        FolderExplorer = _dockLayoutService.GetAnchorable<FolderExplorerToolboxViewModel>() ?? throw new ArgumentNullException(nameof(FolderExplorerToolboxViewModel));
        EntityProperties = _dockLayoutService.GetAnchorable<EntityPropertiesToolboxViewModel>() ?? throw new ArgumentNullException(nameof(EntityPropertiesToolboxViewModel));

        IsDarkTheme = themeSettingService.IsDarkTheme;
        CurrentCultureLCID = cultureSettingService.GetCurrentCultureLCID();
    }

    /// <summary>The MVVM layout tree — bind to DockLayout on the DockingManager.</summary>
    public IRootDock DockLayout => _dockLayoutService.Layout;

    /// <summary>Exposes the layout service for binding to ToggleDockingManager.LayoutService.</summary>
    public IDockLayoutService LayoutService => _dockLayoutService;

    /// <summary>Provides typed access to the folder explorer VM via the layout service.</summary>
    public FolderExplorerToolboxViewModel FolderExplorer { get; }

    public EntityPropertiesToolboxViewModel EntityProperties { get; }

    [ObservableProperty]
    public partial EditorTabViewModel? CurrentEditorTabViewModel { get; private set; }

    partial void OnCurrentEditorTabViewModelChanged(EditorTabViewModel? value)
    {
        EntityProperties.Attach(value?.CadDocumentViewModel);
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
        _dockLayoutService.OpenOrActivateDocument(
           e => false,
           () =>
           {
               var tab = Ioc.Default.GetRequiredService<EditorTabViewModel>();
               CurrentEditorTabViewModel = tab;
               _snackbarService.Enqueue("New document created.");
               return tab;
           });
    }

    [RelayCommand]
    private void OpenFile()
    {
        var fileName = _fileDialogService.OpenD2cadFile();
        if (fileName is null)
            return;

        try
        {
            _dockLayoutService.OpenOrActivateDocument(
            e => e.CurrentFilePath == fileName,
            () =>
            {
                var tab = Ioc.Default.GetRequiredService<EditorTabViewModel>();
                tab.Load(fileName);
                CurrentEditorTabViewModel = tab;
                _snackbarService.Enqueue("File opened successfully.");
                return tab;
            });
        }
        catch (Exception ex)
        {
            _ = _dialogService.ShowOrReplaceMessageDialogAsync(ex.Message, "Open failed");
        }
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
