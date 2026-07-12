using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Layouts;

public sealed class LayoutTabItemViewModel : ObservableObject
{
    private readonly Func<LayoutTabItemViewModel, string, bool>? _rename;
    private string _name;

    public LayoutId? LayoutId { get; }
    public bool IsModelSpace => LayoutId is null;

    public string Name
    {
        get => _name;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (normalized == _name || IsModelSpace || string.IsNullOrWhiteSpace(normalized))
                return;
            if (_rename?.Invoke(this, normalized) == true)
                SetProperty(ref _name, normalized);
        }
    }

    internal LayoutTabItemViewModel(
        LayoutId? layoutId,
        string name,
        Func<LayoutTabItemViewModel, string, bool>? rename = null)
    {
        LayoutId = layoutId;
        _name = name;
        _rename = rename;
    }
}

public sealed class LayoutViewportItemViewModel(LayoutViewportId id, string name)
{
    public LayoutViewportId Id { get; } = id;
    public string Name { get; } = name;
}

public partial class LayoutWorkspaceViewModel : ObservableObject
{
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public ObservableCollection<LayoutTabItemViewModel> Tabs { get; } = [];
    public ObservableCollection<LayoutViewportItemViewModel> Viewports { get; } = [];

    [ObservableProperty]
    public partial LayoutTabItemViewModel? SelectedTab { get; set; }

    [ObservableProperty]
    public partial LayoutViewportItemViewModel? SelectedViewport { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingsVisibility))]
    public partial bool IsSettingsOpen { get; set; }

    public bool SettingsVisibility => IsSettingsOpen && HasActiveLayout;
    public bool HasActiveLayout => SelectedTab?.LayoutId is not null;
    public bool CanDeleteLayout => HasActiveLayout && Document.Layouts.Count > 1;
    public bool CanDeleteViewport => SelectedViewport is not null;

    [ObservableProperty] public partial double PaperWidth { get; set; }
    [ObservableProperty] public partial double PaperHeight { get; set; }
    [ObservableProperty] public partial double MarginLeft { get; set; }
    [ObservableProperty] public partial double MarginTop { get; set; }
    [ObservableProperty] public partial double MarginRight { get; set; }
    [ObservableProperty] public partial double MarginBottom { get; set; }
    [ObservableProperty] public partial CadColor PaperColor { get; set; } = CadColor.White;

    [ObservableProperty] public partial double ViewportLeft { get; set; }
    [ObservableProperty] public partial double ViewportBottom { get; set; }
    [ObservableProperty] public partial double ViewportWidth { get; set; }
    [ObservableProperty] public partial double ViewportHeight { get; set; }
    [ObservableProperty] public partial double ModelCenterX { get; set; }
    [ObservableProperty] public partial double ModelCenterY { get; set; }
    [ObservableProperty] public partial double ViewportScale { get; set; } = 1;
    [ObservableProperty] public partial double ViewportRotationDegrees { get; set; }
    [ObservableProperty] public partial bool IsViewportVisible { get; set; } = true;
    [ObservableProperty] public partial bool IsViewportLocked { get; set; } = true;
    [ObservableProperty] public partial string ValidationError { get; private set; } = string.Empty;

    private CadDocument Document => _documentViewModel.CadEditor.Document;

    public LayoutWorkspaceViewModel(CadDocumentViewModel documentViewModel)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        RefreshDocumentStructure();
    }

    partial void OnSelectedTabChanged(LayoutTabItemViewModel? value)
    {
        if (_isRefreshing || value is null)
            return;

        if (value.LayoutId is { } layoutId)
            _documentViewModel.ActivateLayout(layoutId);
        else
            _documentViewModel.ActivateModelSpace();

        LoadLayoutSettings();
        NotifyCapabilitiesChanged();
    }

    partial void OnSelectedViewportChanged(LayoutViewportItemViewModel? value)
    {
        if (!_isRefreshing)
            LoadSelectedViewport();
        OnPropertyChanged(nameof(CanDeleteViewport));
        RemoveViewportCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSettingsOpenChanged(bool value) => OnPropertyChanged(nameof(SettingsVisibility));

    public void RefreshDocumentStructure()
    {
        var activeLayoutId = _documentViewModel.ActiveLayoutId;
        if (activeLayoutId is { } missingId && !Document.TryGetLayout(missingId, out _))
        {
            _documentViewModel.ActivateModelSpace();
            activeLayoutId = null;
        }
        else if (activeLayoutId is { } existingId &&
                 _documentViewModel.ActiveLayoutViewportId is { } activeViewportId)
        {
            var activeLayout = Document.GetLayout(existingId);
            if (!activeLayout.Viewports.Any(item => item.Id == activeViewportId && item.IsVisible))
                _documentViewModel.ExitLayoutViewport();
        }

        _isRefreshing = true;
        try
        {
            Tabs.Clear();
            Tabs.Add(new LayoutTabItemViewModel(null, "Model"));
            foreach (var layout in Document.Layouts.Values)
                Tabs.Add(new LayoutTabItemViewModel(layout.Id, layout.Name, TryRenameLayout));

            SelectedTab = Tabs.First(item => Nullable.Equals(item.LayoutId, activeLayoutId));
        }
        finally
        {
            _isRefreshing = false;
        }

        LoadLayoutSettings();
        NotifyCapabilitiesChanged();
    }

    [RelayCommand]
    private void AddLayout()
    {
        var layoutId = _documentViewModel.CadEditor.CreateLayout(CreateUniqueLayoutName());
        RefreshDocumentStructure();
        SelectedTab = Tabs.First(item => item.LayoutId == layoutId);
        IsSettingsOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteLayout))]
    private void DeleteLayout()
    {
        if (SelectedTab?.LayoutId is not { } layoutId)
            return;

        _documentViewModel.CadEditor.DeleteLayout(layoutId);
        _documentViewModel.ActivateModelSpace();
        RefreshDocumentStructure();
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsOpen = HasActiveLayout && !IsSettingsOpen;
    }

    [RelayCommand]
    private void SwapPaperOrientation()
    {
        (PaperWidth, PaperHeight) = (PaperHeight, PaperWidth);
        (MarginLeft, MarginBottom, MarginRight, MarginTop) =
            (MarginBottom, MarginRight, MarginTop, MarginLeft);
    }

    [RelayCommand]
    private void ApplyLayoutSettings()
    {
        if (SelectedTab?.LayoutId is not { } layoutId)
            return;

        try
        {
            var commands = new List<ICadCommand>
            {
                new SetLayoutPaperCommand(layoutId, new CadLayoutPaperSnapshot(
                    PaperWidth, PaperHeight, MarginLeft, MarginTop, MarginRight, MarginBottom)),
                new SetLayoutPaperColorCommand(layoutId, PaperColor)
            };

            if (SelectedViewport is { } selectedViewport)
                commands.Add(new SetLayoutViewportCommand(
                    layoutId,
                    selectedViewport.Id,
                    CreateViewportSnapshot()));

            _documentViewModel.CadEditor.ExecuteRange(commands, "Set Layout Properties");
            if (!IsViewportVisible &&
                SelectedViewport is { } hiddenViewport &&
                _documentViewModel.ActiveLayoutViewportId == hiddenViewport.Id)
            {
                _documentViewModel.ExitLayoutViewport();
            }
            ValidationError = string.Empty;
            _documentViewModel.FitToWindow();
            RefreshDocumentStructure();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ValidationError = ex.Message;
        }
    }

    [RelayCommand]
    private void AddViewport()
    {
        if (SelectedTab?.LayoutId is not { } layoutId)
            return;

        var layout = Document.GetLayout(layoutId);
        var bounds = layout.PrintableBounds;
        var insetX = bounds.Width * 0.08;
        var insetY = bounds.Height * 0.08;
        bounds = bounds.Inflate(-insetX, -insetY);
        var viewportId = _documentViewModel.CadEditor.AddLayoutViewport(
            layoutId,
            bounds,
            CadPointD.Origin,
            1);
        RefreshDocumentStructure();
        SelectedViewport = Viewports.First(item => item.Id == viewportId);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteViewport))]
    private void RemoveViewport()
    {
        if (SelectedTab?.LayoutId is not { } layoutId || SelectedViewport is not { } viewport)
            return;

        if (_documentViewModel.ActiveLayoutViewportId == viewport.Id)
            _documentViewModel.ExitLayoutViewport();
        _documentViewModel.CadEditor.RemoveLayoutViewport(layoutId, viewport.Id);
        RefreshDocumentStructure();
    }

    private bool TryRenameLayout(LayoutTabItemViewModel item, string name)
    {
        if (item.LayoutId is not { } layoutId)
            return false;

        try
        {
            _documentViewModel.CadEditor.RenameLayout(layoutId, name);
            ValidationError = string.Empty;
            return true;
        }
        catch (ArgumentException ex)
        {
            ValidationError = ex.Message;
            return false;
        }
    }

    private void LoadLayoutSettings()
    {
        _isRefreshing = true;
        try
        {
            Viewports.Clear();
            if (SelectedTab?.LayoutId is not { } layoutId)
            {
                SelectedViewport = null;
                return;
            }

            var layout = Document.GetLayout(layoutId);
            PaperWidth = layout.PaperWidth;
            PaperHeight = layout.PaperHeight;
            MarginLeft = layout.MarginLeft;
            MarginTop = layout.MarginTop;
            MarginRight = layout.MarginRight;
            MarginBottom = layout.MarginBottom;
            PaperColor = layout.PaperColor;

            for (var index = 0; index < layout.Viewports.Count; index++)
            {
                var viewport = layout.Viewports[index];
                Viewports.Add(new LayoutViewportItemViewModel(viewport.Id, $"Viewport {index + 1}"));
            }

            SelectedViewport = Viewports.FirstOrDefault();
        }
        finally
        {
            _isRefreshing = false;
        }

        LoadSelectedViewport();
    }

    private void LoadSelectedViewport()
    {
        if (SelectedTab?.LayoutId is not { } layoutId || SelectedViewport is not { } selected)
            return;

        var viewport = Document.GetLayout(layoutId).GetViewport(selected.Id);
        ViewportLeft = viewport.Bounds.Left;
        ViewportBottom = viewport.Bounds.Bottom;
        ViewportWidth = viewport.Bounds.Width;
        ViewportHeight = viewport.Bounds.Height;
        ModelCenterX = viewport.ModelCenter.X;
        ModelCenterY = viewport.ModelCenter.Y;
        ViewportScale = viewport.Scale;
        ViewportRotationDegrees = viewport.RotationRadians * 180 / Math.PI;
        IsViewportVisible = viewport.IsVisible;
        IsViewportLocked = viewport.IsLocked;
    }

    private CadLayoutViewportSnapshot CreateViewportSnapshot()
    {
        var bounds = CadRectD.FromXYWH(ViewportLeft, ViewportBottom, ViewportWidth, ViewportHeight);
        return new CadLayoutViewportSnapshot(
            bounds,
            new CadPointD(ModelCenterX, ModelCenterY),
            ViewportScale,
            ViewportRotationDegrees * Math.PI / 180,
            IsViewportVisible,
            IsViewportLocked);
    }

    private string CreateUniqueLayoutName()
    {
        var names = Document.Layouts.Values
            .Select(layout => layout.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; ; index++)
        {
            var candidate = $"Layout{index}";
            if (!names.Contains(candidate))
                return candidate;
        }
    }

    private void NotifyCapabilitiesChanged()
    {
        OnPropertyChanged(nameof(HasActiveLayout));
        OnPropertyChanged(nameof(CanDeleteLayout));
        OnPropertyChanged(nameof(CanDeleteViewport));
        OnPropertyChanged(nameof(SettingsVisibility));
        DeleteLayoutCommand.NotifyCanExecuteChanged();
        RemoveViewportCommand.NotifyCanExecuteChanged();
    }
}
