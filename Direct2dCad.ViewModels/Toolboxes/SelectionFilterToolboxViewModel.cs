using System.Collections.ObjectModel;
using AvalonDock.Core;
using AvalonDock.Mvvm.CommunityToolkit;
using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class SelectionFilterToolboxViewModel : ObservableToolboxBase
{
    private static readonly (Type EntityType, string ResourceKey, string FallbackName)[] SupportedTypes =
    [
        (typeof(CadLine), "Line", "Line"),
        (typeof(CadCircle), "Circle", "Circle"),
        (typeof(CadArc), "Arc", "Arc"),
        (typeof(CadEllipse), "Ellipse", "Ellipse"),
        (typeof(CadEllipseArc), "EllipseArc", "Ellipse Arc"),
        (typeof(CadRectangle), "Rectangle", "Rectangle"),
        (typeof(CadPolyline), "Polyline", "Polyline"),
        (typeof(CadSpline), "Spline", "Spline"),
        (typeof(CadText), "Text", "Text"),
        (typeof(CadShapeText), "ShapeText", "Shape Text"),
        (typeof(CadImage), "Image", "Image"),
        (typeof(CadOleObject), "OleObject", "OLE Object"),
        (typeof(CadBlockReference), "BlockReference", "Block Reference")
    ];

    private CadDocumentViewModel? _documentViewModel;
    private bool _isSynchronizing;

    public SelectionFilterToolboxViewModel(IToolboxIconProvider toolboxIconProvider)
    {
        Title = GetLocalizedText("SelectionFilter", "Selection Filter");
        Zone = DockZone.RightTop;
        Icon = toolboxIconProvider.Filter;
        Shortcut = "Ctrl+Shift+F";
        IsOpenByDefault = false;
        ContentId = Id = Guid.NewGuid().ToString();
        CanClose = false;

        foreach (var (entityType, resourceKey, fallbackName) in SupportedTypes)
        {
            Types.Add(new SelectionFilterTypeItemViewModel(
                entityType,
                GetLocalizedText(resourceKey, fallbackName),
                OnTypeEnabledChanged));
        }
    }

    [ObservableProperty]
    public partial string ContentId { get; private set; }

    [ObservableProperty]
    public partial bool? AreAllTypesEnabled { get; set; } = true;

    public ObservableCollection<SelectionFilterTypeItemViewModel> Types { get; } = [];

    public void Attach(CadDocumentViewModel? documentViewModel)
    {
        _documentViewModel = documentViewModel;
        _isSynchronizing = true;
        try
        {
            foreach (var item in Types)
            {
                item.IsEnabled = documentViewModel is null ||
                                 documentViewModel.IsEntityTypeSelectionEnabled(item.EntityType);
            }
        }
        finally
        {
            _isSynchronizing = false;
        }

        RefreshHeaderState();
    }

    partial void OnAreAllTypesEnabledChanged(bool? value)
    {
        if (_isSynchronizing || value is not { } enabled)
            return;

        _isSynchronizing = true;
        try
        {
            foreach (var item in Types)
                item.IsEnabled = enabled;
        }
        finally
        {
            _isSynchronizing = false;
        }

        if (_documentViewModel is not null)
        {
            foreach (var item in Types)
                _documentViewModel.SetEntityTypeSelectionEnabled(item.EntityType, enabled);
        }

        RefreshHeaderState();
    }

    private void OnTypeEnabledChanged(SelectionFilterTypeItemViewModel item)
    {
        if (_isSynchronizing)
            return;

        _documentViewModel?.SetEntityTypeSelectionEnabled(item.EntityType, item.IsEnabled);
        RefreshHeaderState();
    }

    private void RefreshHeaderState()
    {
        var enabledCount = Types.Count(item => item.IsEnabled);
        var headerState = enabledCount switch
        {
            0 => false,
            _ when enabledCount == Types.Count => true,
            _ => (bool?)null
        };

        _isSynchronizing = true;
        try
        {
            AreAllTypesEnabled = headerState;
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private static string GetLocalizedText(string resourceKey, string fallback)
    {
        return Direct2dCad.Lang.Strings.Strings.ResourceManager.GetString(
                   resourceKey,
                   System.Globalization.CultureInfo.CurrentUICulture) ?? fallback;
    }
}

public partial class SelectionFilterTypeItemViewModel : ObservableObject
{
    private readonly Action<SelectionFilterTypeItemViewModel> _changed;

    public SelectionFilterTypeItemViewModel(
        Type entityType,
        string displayName,
        Action<SelectionFilterTypeItemViewModel> changed)
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    public Type EntityType { get; }

    public string DisplayName { get; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    partial void OnIsEnabledChanged(bool value)
    {
        _changed(this);
    }
}
