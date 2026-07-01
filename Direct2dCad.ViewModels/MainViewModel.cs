using AvalonDock.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.IO;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.ViewServices.Abstractions;

namespace Direct2dCad.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CadDocumentStorage _storage = new();
    private readonly IFileDialogService _fileDialogService;
    private readonly IMessageBoxService _messageBoxService;
    private readonly IDockLayoutService _dockLayoutService;
    private readonly ICultureSettingService _cultureSettingService;
    private readonly IThemeSettingService _themeSettingService;

    public MainViewModel(IDockLayoutService dockLayoutService,
        ICultureSettingService cultureSettingService,
        IThemeSettingService themeSettingService,
        IFileDialogService fileDialogService,
        IMessageBoxService messageBoxService
        )
    {
        _dockLayoutService = dockLayoutService;
        _cultureSettingService = cultureSettingService;
        _themeSettingService = themeSettingService;

        _fileDialogService = fileDialogService;
        _messageBoxService = messageBoxService;

        FolderExplorer = _dockLayoutService.GetAnchorable<FolderExplorerViewModel>() ?? throw new ArgumentNullException(nameof(FolderExplorerViewModel));
        EntityProperties = _dockLayoutService.GetAnchorable<EntityPropertiesViewModel>() ?? throw new ArgumentNullException(nameof(EntityPropertiesViewModel));

        _dockLayoutService.OpenOrActivateDocument(
                   e => false,
                   () =>
                   {
                       var tab = Ioc.Default.GetRequiredService<EditorTabViewModel>();
                       CurrentEditorTabViewModel = tab;
                       return tab;
                   });
        IsDarkTheme = themeSettingService.IsDarkTheme;
    }

    /// <summary>The MVVM layout tree — bind to DockLayout on the DockingManager.</summary>
    public IRootDock DockLayout => _dockLayoutService.Layout;

    /// <summary>Exposes the layout service for binding to ToggleDockingManager.LayoutService.</summary>
    public IDockLayoutService LayoutService => _dockLayoutService;

    /// <summary>Provides typed access to the folder explorer VM via the layout service.</summary>
    public FolderExplorerViewModel FolderExplorer { get; }

    public EntityPropertiesViewModel EntityProperties { get; }
    [ObservableProperty]
    public partial EditorTabViewModel? CurrentEditorTabViewModel { get; private set; }

    [RelayCommand]
    private void OpenFile()
    {
        var fileName = _fileDialogService.OpenFile();
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
                return tab;
            });
        }
        catch (Exception ex)
        {
            _messageBoxService.ShowMessage(ex.Message, "Open failed");
        }
    }
    [RelayCommand]
    private void DocumentClosed(object content)
    {
        if (content is EditorTabViewModel editorTabViewModel)
        {
            editorTabViewModel.Dispose();
        }
    }
    [RelayCommand]
    private void ActiveContentChanged()
    {
        CurrentEditorTabViewModel = _dockLayoutService.ActiveDockable as EditorTabViewModel;
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
