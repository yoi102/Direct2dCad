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
    private readonly Dictionary<string, LiveSettingBatch> _liveSettingBatches = [];
    private bool _isRefreshing;
    private bool _isApplyingLiveSetting;
    private static readonly TimeSpan LiveSettingBatchTimeout = TimeSpan.FromSeconds(1);

    public ObservableCollection<LayoutTabItemViewModel> Tabs { get; } = [];
    public ObservableCollection<LayoutViewportItemViewModel> Viewports { get; } = [];
    public ObservableCollection<LayoutViewportItemViewModel> CurrentViewportOptions { get; } = [];

    [ObservableProperty]
    public partial LayoutTabItemViewModel? SelectedTab { get; set; }

    [ObservableProperty]
    public partial LayoutViewportItemViewModel? SelectedViewport { get; set; }

    [ObservableProperty]
    public partial LayoutViewportItemViewModel? CurrentViewport { get; set; }

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
    [ObservableProperty] public partial bool IsViewportLocked { get; set; }
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
        {
            if (value is not null)
                _documentViewModel.SetPreferredLayoutViewport(value.Id);
            LoadSelectedViewport();
        }
        OnPropertyChanged(nameof(CanDeleteViewport));
        RemoveViewportCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentViewportChanged(LayoutViewportItemViewModel? value)
    {
        if (_isRefreshing ||
            value is null ||
            _documentViewModel.ActiveLayoutViewportId == value.Id)
        {
            return;
        }

        _documentViewModel.ActivateLayoutViewport(value.Id);
    }

    partial void OnIsSettingsOpenChanged(bool value) => OnPropertyChanged(nameof(SettingsVisibility));

    partial void OnPaperWidthChanged(double value) => ApplyPaperSettings(nameof(PaperWidth));
    partial void OnPaperHeightChanged(double value) => ApplyPaperSettings(nameof(PaperHeight));
    partial void OnMarginLeftChanged(double value) => ApplyPaperSettings(nameof(MarginLeft));
    partial void OnMarginTopChanged(double value) => ApplyPaperSettings(nameof(MarginTop));
    partial void OnMarginRightChanged(double value) => ApplyPaperSettings(nameof(MarginRight));
    partial void OnMarginBottomChanged(double value) => ApplyPaperSettings(nameof(MarginBottom));
    partial void OnPaperColorChanged(CadColor value) => ApplyPaperColor(nameof(PaperColor));

    partial void OnViewportLeftChanged(double value) => ApplyViewportSettings(nameof(ViewportLeft));
    partial void OnViewportBottomChanged(double value) => ApplyViewportSettings(nameof(ViewportBottom));
    partial void OnViewportWidthChanged(double value) => ApplyViewportSettings(nameof(ViewportWidth));
    partial void OnViewportHeightChanged(double value) => ApplyViewportSettings(nameof(ViewportHeight));
    partial void OnModelCenterXChanged(double value) => ApplyViewportSettings(nameof(ModelCenterX));
    partial void OnModelCenterYChanged(double value) => ApplyViewportSettings(nameof(ModelCenterY));
    partial void OnViewportScaleChanged(double value) => ApplyViewportSettings(nameof(ViewportScale));
    partial void OnViewportRotationDegreesChanged(double value) => ApplyViewportSettings(nameof(ViewportRotationDegrees));
    partial void OnIsViewportVisibleChanged(bool value) => ApplyViewportSettings(nameof(IsViewportVisible));
    partial void OnIsViewportLockedChanged(bool value) => ApplyViewportSettings(nameof(IsViewportLocked));

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

    public void HandleDocumentStructureChanged()
    {
        if (!_isApplyingLiveSetting)
            RefreshDocumentStructure();
    }

    public void HandleLayoutSettingsChanged()
    {
        if (_isApplyingLiveSetting || SelectedTab?.LayoutId is not { } layoutId)
            return;

        if (!Document.TryGetLayout(layoutId, out var layout) || layout is null)
        {
            RefreshDocumentStructure();
            return;
        }

        LoadPaperSettings(layout);
        if (SelectedViewport is { } selected && layout.Viewports.Any(item => item.Id == selected.Id))
            LoadSelectedViewport();
        RefreshCurrentViewportOptions(layout);
        ValidationError = string.Empty;
    }

    [RelayCommand]
    private void AddLayout()
    {
        var layoutId = _documentViewModel.CadEditor.CreateLayout(CreateUniqueLayoutName());
        SelectedTab = Tabs.First(item => item.LayoutId == layoutId);
        IsSettingsOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteLayout))]
    private void DeleteLayout()
    {
        if (SelectedTab?.LayoutId is not { } layoutId)
            return;

        _documentViewModel.CadEditor.DeleteLayout(layoutId);
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsOpen = HasActiveLayout && !IsSettingsOpen;
    }

    [RelayCommand]
    private void SwapPaperOrientation()
    {
        _isRefreshing = true;
        try
        {
            (PaperWidth, PaperHeight) = (PaperHeight, PaperWidth);
            (MarginLeft, MarginBottom, MarginRight, MarginTop) =
                (MarginBottom, MarginRight, MarginTop, MarginLeft);
        }
        finally
        {
            _isRefreshing = false;
        }

        ApplyPaperSettings(nameof(SwapPaperOrientation));
    }

    [RelayCommand]
    private void AddViewport()
    {
        if (SelectedTab?.LayoutId is null)
            return;
        IsSettingsOpen = false;
        _documentViewModel.BeginLayoutViewportCreation();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteViewport))]
    private void RemoveViewport()
    {
        if (SelectedTab?.LayoutId is not { } layoutId || SelectedViewport is not { } viewport)
            return;

        if (_documentViewModel.ActiveLayoutViewportId == viewport.Id)
            _documentViewModel.ExitLayoutViewport();
        _documentViewModel.CadEditor.RemoveLayoutViewport(layoutId, viewport.Id);
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
            CurrentViewportOptions.Clear();
            if (SelectedTab?.LayoutId is not { } layoutId)
            {
                SelectedViewport = null;
                CurrentViewport = null;
                return;
            }

            var layout = Document.GetLayout(layoutId);
            LoadPaperSettings(layout);

            for (var index = 0; index < layout.Viewports.Count; index++)
            {
                var viewport = layout.Viewports[index];
                Viewports.Add(new LayoutViewportItemViewModel(viewport.Id, $"Viewport {index + 1}"));
            }

            var preferredViewportId = _documentViewModel.ActiveLayoutViewportId ??
                                      _documentViewModel.GetPreferredLayoutViewportId(layoutId);
            SelectedViewport = preferredViewportId is { } viewportId
                ? Viewports.FirstOrDefault(item => item.Id == viewportId) ?? Viewports.FirstOrDefault()
                : Viewports.FirstOrDefault();
            RefreshCurrentViewportOptions(layout);
        }
        finally
        {
            _isRefreshing = false;
        }

        LoadSelectedViewport();
    }

    private void RefreshCurrentViewportOptions(CadLayout layout)
    {
        var wasRefreshing = _isRefreshing;
        _isRefreshing = true;
        try
        {
            CurrentViewportOptions.Clear();
            foreach (var viewport in layout.Viewports.Where(item => item.IsVisible))
            {
                var item = Viewports.FirstOrDefault(candidate => candidate.Id == viewport.Id);
                if (item is not null)
                    CurrentViewportOptions.Add(item);
            }

            CurrentViewport = _documentViewModel.ActiveLayoutViewportId is { } activeViewportId
                ? CurrentViewportOptions.FirstOrDefault(item => item.Id == activeViewportId)
                : null;
        }
        finally
        {
            _isRefreshing = wasRefreshing;
        }
    }

    private void LoadPaperSettings(CadLayout layout)
    {
        var wasRefreshing = _isRefreshing;
        _isRefreshing = true;
        try
        {
            PaperWidth = layout.PaperWidth;
            PaperHeight = layout.PaperHeight;
            MarginLeft = layout.MarginLeft;
            MarginTop = layout.MarginTop;
            MarginRight = layout.MarginRight;
            MarginBottom = layout.MarginBottom;
            PaperColor = layout.PaperColor;
        }
        finally
        {
            _isRefreshing = wasRefreshing;
        }
    }

    private void LoadSelectedViewport()
    {
        if (SelectedTab?.LayoutId is not { } layoutId || SelectedViewport is not { } selected)
            return;

        var viewport = Document.GetLayout(layoutId).GetViewport(selected.Id);
        var wasRefreshing = _isRefreshing;
        _isRefreshing = true;
        try
        {
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
        finally
        {
            _isRefreshing = wasRefreshing;
        }
    }

    private void ApplyPaperSettings(string propertyName)
    {
        if (_isRefreshing || SelectedTab?.LayoutId is not { } layoutId)
            return;

        var target = new CadLayoutPaperSnapshot(
            PaperWidth,
            PaperHeight,
            MarginLeft,
            MarginTop,
            MarginRight,
            MarginBottom);
        if (target == CadLayoutPaperSnapshot.From(Document.GetLayout(layoutId)))
            return;

        if (!ExecuteLiveSetting(
                new SetLayoutPaperCommand(layoutId, target),
                $"paper:{layoutId.Value}:{propertyName}"))
            LoadPaperSettings(Document.GetLayout(layoutId));
    }

    private void ApplyPaperColor(string propertyName)
    {
        if (_isRefreshing || SelectedTab?.LayoutId is not { } layoutId)
            return;

        if (Document.GetLayout(layoutId).PaperColor == PaperColor)
            return;

        ExecuteLiveSetting(
            new SetLayoutPaperColorCommand(layoutId, PaperColor),
            $"paper:{layoutId.Value}:{propertyName}");
    }

    private void ApplyViewportSettings(string propertyName)
    {
        if (_isRefreshing ||
            SelectedTab?.LayoutId is not { } layoutId ||
            SelectedViewport is not { } selectedViewport)
        {
            return;
        }

        var target = CreateViewportSnapshot();
        var layout = Document.GetLayout(layoutId);
        var viewport = layout.GetViewport(selectedViewport.Id);
        if (target == CadLayoutViewportSnapshot.From(viewport))
            return;

        if (!ExecuteLiveSetting(
                new SetLayoutViewportCommand(layoutId, selectedViewport.Id, target),
                $"viewport:{layoutId.Value}:{selectedViewport.Id.Value}:{propertyName}"))
        {
            LoadSelectedViewport();
            return;
        }

        RefreshCurrentViewportOptions(layout);
        if (!IsViewportVisible && _documentViewModel.ActiveLayoutViewportId == selectedViewport.Id)
            _documentViewModel.ExitLayoutViewport();
    }

    private bool ExecuteLiveSetting(ICadCommand command, string batchKey)
    {
        _isApplyingLiveSetting = true;
        try
        {
            var now = DateTime.UtcNow;
            var batchId = _liveSettingBatches.TryGetValue(batchKey, out var batch) &&
                          now - batch.LastUpdated <= LiveSettingBatchTimeout
                ? batch.Id
                : Guid.NewGuid();
            _documentViewModel.CadEditor.ExecuteInBatch(command, batchId);
            _liveSettingBatches[batchKey] = new LiveSettingBatch(batchId, now);
            ValidationError = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ValidationError = ex.Message;
            return false;
        }
        finally
        {
            _isApplyingLiveSetting = false;
        }
    }

    private CadLayoutViewportSnapshot CreateViewportSnapshot()
    {
        var bounds = new CadRectD(
            ViewportLeft,
            ViewportBottom,
            ViewportLeft + ViewportWidth,
            ViewportBottom + ViewportHeight);
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

    private readonly record struct LiveSettingBatch(Guid Id, DateTime LastUpdated);
}
