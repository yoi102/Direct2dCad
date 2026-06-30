using AvalonDock.Mvvm.CommunityToolkit;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.IO;
using Direct2dCad.ViewServices.Abstractions;

namespace Direct2dCad.ViewModels;

public partial class EditorTabViewModel : ObservableDocument, IDisposable
{
    private readonly IUserSettingsService _userSettingsService;

    private readonly CadUserSettings _userSettings;
    private readonly CadDocumentStorage _storage = new();
    private readonly IFileDialogService _fileDialogService;
    private readonly IMessageBoxService _messageBoxService;
    private bool _isSyncingViewSettings;
    private bool _isSyncingUserSettings;

    public EditorTabViewModel(CadDocumentViewModel cadDocumentViewModel,
        IUserSettingsService userSettingsService,
        IFileDialogService fileDialogService,
        IMessageBoxService messageBoxService
        )
    {
        _userSettingsService = userSettingsService;
        _userSettings = _userSettingsService.Load();
        _fileDialogService = fileDialogService;
        _messageBoxService = messageBoxService;

        CadDocumentViewModel = cadDocumentViewModel;
        _userSettingsService = userSettingsService;
        CadDocumentViewModel.ApplyUserSettings(_userSettings);
        CadDocumentViewModel.ViewSettingsChanged += OnCadDocumentViewSettingsChanged;
        CadDocumentViewModel.DrawingText = TextInput;
        ApplyDocumentViewSettingsToToolbar();
        ApplyUserSettingsToToolbar();
        this.Id = Guid.NewGuid().ToString();
        this.Title = cadDocumentViewModel.CadEditor.Document.Name;
    }

    public override bool OnClose()
    {
      return  base.OnClose();
    }
    public CadDocumentViewModel CadDocumentViewModel { get; }

    [ObservableProperty]
    public partial string CurrentFilePath { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string TextInput { get; set; } = "Text";

    [ObservableProperty]
    public partial ViewModelCadGridType ViewModelCadGridType { get; set; } = ViewModelCadGridType.Lines;

    [ObservableProperty]
    public partial ViewModelCadSnapMarkerType ViewModelCadSnapMarkerType { get; set; } = ViewModelCadSnapMarkerType.Cross;

    [ObservableProperty]
    public partial ViewModelCadOriginDisplayType ViewModelCadOriginDisplayType { get; set; } = ViewModelCadOriginDisplayType.AxesAndMarker;

    [ObservableProperty]
    public partial ViewModelCadOriginMarkerType ViewModelCadOriginMarkerType { get; set; } = ViewModelCadOriginMarkerType.Circle;

    [ObservableProperty]
    public partial ViewModelCadOriginLinePattern ViewModelCadOriginLinePattern { get; set; } = ViewModelCadOriginLinePattern.Solid;

    [ObservableProperty]
    public partial double ViewModelCadOriginSize { get; set; } = 18.0;

    [ObservableProperty]
    public partial double ViewModelCadOriginStrokeWidth { get; set; } = 0.62;

    [ObservableProperty]
    public partial string ViewModelCadOriginColorText { get; set; } = "#FF50BEFF";

    [ObservableProperty]
    public partial double ViewModelCadOriginX { get; set; }

    [ObservableProperty]
    public partial double ViewModelCadOriginY { get; set; }

    [ObservableProperty]
    public partial bool IsAntialiasingEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsTextAntialiasingEnabled { get; set; } = true;

    [ObservableProperty]
    public partial string SelectedEntityColorText { get; set; } = "#F0FFD65C";

    [ObservableProperty]
    public partial string SelectionWindowStrokeColorText { get; set; } = "#E640C4FF";

    [ObservableProperty]
    public partial string SelectionWindowFillColorText { get; set; } = "#2040C4FF";

    [ObservableProperty]
    public partial string SelectionCrossingStrokeColorText { get; set; } = "#E65CDC80";

    [ObservableProperty]
    public partial string SelectionCrossingFillColorText { get; set; } = "#245CDC80";

    partial void OnTextInputChanged(string value)
    {
        CadDocumentViewModel.DrawingText = value;
        CadDocumentViewModel.RequestRender();
    }

    partial void OnViewModelCadGridTypeChanged(ViewModelCadGridType value)
    {
        if (_isSyncingViewSettings)
            return;

        var gridType = (CadGridType)value;

        CadDocumentViewModel.CadEditor.Document.ViewSettings.Grid.Type = gridType;
        CadDocumentViewModel.RequestRender();
    }

    partial void OnViewModelCadSnapMarkerTypeChanged(ViewModelCadSnapMarkerType value)
    {
        if (_isSyncingViewSettings)
            return;

        var markerType = (CadSnapMarkerType)value;

        CadDocumentViewModel.CadEditor.Document.ViewSettings.Grid.SnapMarkerType = markerType;
        CadDocumentViewModel.RequestRender();
    }

    partial void OnViewModelCadOriginDisplayTypeChanged(ViewModelCadOriginDisplayType value)
    {
        if (_isSyncingViewSettings)
            return;

        ApplyOriginSettingsFromToolbar();
    }

    partial void OnViewModelCadOriginMarkerTypeChanged(ViewModelCadOriginMarkerType value)
    {
        if (_isSyncingViewSettings)
            return;

        ApplyOriginSettingsFromToolbar();
    }

    partial void OnViewModelCadOriginLinePatternChanged(ViewModelCadOriginLinePattern value)
    {
        if (_isSyncingViewSettings)
            return;

        ApplyOriginSettingsFromToolbar();
    }

    partial void OnViewModelCadOriginSizeChanged(double value)
    {
        if (_isSyncingViewSettings)
            return;

        ApplyOriginSettingsFromToolbar();
    }

    partial void OnViewModelCadOriginStrokeWidthChanged(double value)
    {
        if (_isSyncingViewSettings)
            return;

        ApplyOriginSettingsFromToolbar();
    }

    partial void OnViewModelCadOriginColorTextChanged(string value)
    {
        if (_isSyncingViewSettings)
            return;

        ApplyOriginSettingsFromToolbar();
    }

    partial void OnViewModelCadOriginXChanged(double value)
    {
        if (_isSyncingViewSettings)
            return;

        ApplyOriginPositionFromToolbar();
    }

    partial void OnViewModelCadOriginYChanged(double value)
    {
        if (_isSyncingViewSettings)
            return;

        ApplyOriginPositionFromToolbar();
    }

    partial void OnIsAntialiasingEnabledChanged(bool value)
    {
        if (_isSyncingUserSettings)
            return;

        ApplyUserSettingsFromToolbar();
    }

    partial void OnIsTextAntialiasingEnabledChanged(bool value)
    {
        if (_isSyncingUserSettings)
            return;

        ApplyUserSettingsFromToolbar();
    }

    partial void OnSelectedEntityColorTextChanged(string value)
    {
        if (_isSyncingUserSettings)
            return;

        ApplyUserSettingsFromToolbar();
    }

    partial void OnSelectionWindowStrokeColorTextChanged(string value)
    {
        if (_isSyncingUserSettings)
            return;

        ApplyUserSettingsFromToolbar();
    }

    partial void OnSelectionWindowFillColorTextChanged(string value)
    {
        if (_isSyncingUserSettings)
            return;

        ApplyUserSettingsFromToolbar();
    }

    partial void OnSelectionCrossingStrokeColorTextChanged(string value)
    {
        if (_isSyncingUserSettings)
            return;

        ApplyUserSettingsFromToolbar();
    }

    partial void OnSelectionCrossingFillColorTextChanged(string value)
    {
        if (_isSyncingUserSettings)
            return;

        ApplyUserSettingsFromToolbar();
    }

    [RelayCommand]
    private void SaveFile()
    {
        if (string.IsNullOrWhiteSpace(CurrentFilePath))
        {
            SaveAsFile();
            return;
        }

        SaveTo(CurrentFilePath);
    }

    [RelayCommand]
    private void SaveAsFile()
    {
        var fileName = string.IsNullOrWhiteSpace(CadDocumentViewModel.CadEditor.Document.Name)
                  ? "Untitled.d2cad"
                  : $"{CadDocumentViewModel.CadEditor.Document.Name}.d2cad";
        var selectedFileName = _fileDialogService.SaveFile(fileName);
        if (selectedFileName is null)
            return;

        if (SaveTo(selectedFileName))
            CurrentFilePath = selectedFileName;
    }

    private bool SaveTo(string filePath)
    {
        try
        {
            _storage.Save(CadDocumentViewModel.CadEditor.Document, filePath);
            return true;
        }
        catch (Exception ex)
        {
            _messageBoxService.ShowMessage(ex.Message, "Save failed");
            return false;
        }
    }

    [RelayCommand]
    private void Undo()
    {
        CadDocumentViewModel.Undo();
        ApplyDocumentViewSettingsToToolbar();
    }

    [RelayCommand]
    private void Redo()
    {
        CadDocumentViewModel.Redo();
        ApplyDocumentViewSettingsToToolbar();
    }

    [RelayCommand]
    private void SetCadCanvasToolMode(string mode_string)
    {
        if (!Enum.TryParse<CadCanvasToolMode>(mode_string, out var mode))
            return;

        CadDocumentViewModel.SetToolMode(mode);
    }

    [RelayCommand]
    public void FitToWindow()
    {
        CadDocumentViewModel.FitToWindow();
    }

    private void ApplyUserSettingsToToolbar()
    {
        _isSyncingUserSettings = true;
        try
        {
            IsAntialiasingEnabled = _userSettings.Rendering.IsAntialiasingEnabled;
            IsTextAntialiasingEnabled = _userSettings.Rendering.IsTextAntialiasingEnabled;
            SelectedEntityColorText = FormatColor(_userSettings.Interaction.SelectedEntityStrokeColor);
            SelectionWindowStrokeColorText = FormatColor(_userSettings.Interaction.SelectionWindowStrokeColor);
            SelectionWindowFillColorText = FormatColor(_userSettings.Interaction.SelectionWindowFillColor);
            SelectionCrossingStrokeColorText = FormatColor(_userSettings.Interaction.SelectionCrossingStrokeColor);
            SelectionCrossingFillColorText = FormatColor(_userSettings.Interaction.SelectionCrossingFillColor);
        }
        finally
        {
            _isSyncingUserSettings = false;
        }
    }

    private void ApplyUserSettingsFromToolbar()
    {
        if (!TryParseColor(SelectedEntityColorText, out var selectedEntityColor) ||
            !TryParseColor(SelectionWindowStrokeColorText, out var selectionWindowStrokeColor) ||
            !TryParseColor(SelectionWindowFillColorText, out var selectionWindowFillColor) ||
            !TryParseColor(SelectionCrossingStrokeColorText, out var selectionCrossingStrokeColor) ||
            !TryParseColor(SelectionCrossingFillColorText, out var selectionCrossingFillColor))
        {
            return;
        }

        _userSettings.Rendering.IsAntialiasingEnabled = IsAntialiasingEnabled;
        _userSettings.Rendering.IsTextAntialiasingEnabled = IsTextAntialiasingEnabled;
        _userSettings.Interaction.SelectedEntityStrokeColor = selectedEntityColor;
        _userSettings.Interaction.SelectionWindowStrokeColor = selectionWindowStrokeColor;
        _userSettings.Interaction.SelectionWindowFillColor = selectionWindowFillColor;
        _userSettings.Interaction.SelectionCrossingStrokeColor = selectionCrossingStrokeColor;
        _userSettings.Interaction.SelectionCrossingFillColor = selectionCrossingFillColor;
        _userSettings.Normalize();

        CadDocumentViewModel.ApplyUserSettings(_userSettings);
        SaveUserSettings();
    }

    private void SaveUserSettings()
    {
        try
        {
            _userSettingsService.Save(_userSettings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _messageBoxService.ShowMessage(ex.Message, "User settings save failed");
        }
    }

    private void ApplyDocumentViewSettingsToToolbar()
    {
        _isSyncingViewSettings = true;
        try
        {
            ViewModelCadGridType = (ViewModelCadGridType)CadDocumentViewModel.CadEditor.Document.ViewSettings.Grid.Type;
            ViewModelCadSnapMarkerType = (ViewModelCadSnapMarkerType)CadDocumentViewModel.CadEditor.Document.ViewSettings.Grid.SnapMarkerType;
            ViewModelCadOriginDisplayType = (ViewModelCadOriginDisplayType)CadDocumentViewModel.CadEditor.Document.ViewSettings.Origin.DisplayType;
            ViewModelCadOriginMarkerType = (ViewModelCadOriginMarkerType)CadDocumentViewModel.CadEditor.Document.ViewSettings.Origin.MarkerType;
            ViewModelCadOriginLinePattern = (ViewModelCadOriginLinePattern)CadDocumentViewModel.CadEditor.Document.ViewSettings.Origin.LinePattern;
            ViewModelCadOriginSize = CadDocumentViewModel.CadEditor.Document.ViewSettings.Origin.Size;
            ViewModelCadOriginStrokeWidth = CadDocumentViewModel.CadEditor.Document.ViewSettings.Origin.StrokeWidth;
            ViewModelCadOriginColorText = FormatColor(CadDocumentViewModel.CadEditor.Document.ViewSettings.Origin.Color);
            ViewModelCadOriginX = CadDocumentViewModel.CadEditor.Document.ViewSettings.Origin.Position.X;
            ViewModelCadOriginY = CadDocumentViewModel.CadEditor.Document.ViewSettings.Origin.Position.Y;
        }
        finally
        {
            _isSyncingViewSettings = false;
        }
    }

    private void ApplyOriginSettingsFromToolbar()
    {
        if (!TryParseColor(ViewModelCadOriginColorText, out var color))
            return;

        if (!IsPositiveFinite(ViewModelCadOriginSize) ||
            !IsPositiveFinite(ViewModelCadOriginStrokeWidth))
        {
            return;
        }

        CadDocumentViewModel.CadEditor.SetOriginSettings(
            (CadOriginDisplayType)ViewModelCadOriginDisplayType,
            (CadOriginMarkerType)ViewModelCadOriginMarkerType,
            (CadOriginLinePattern)ViewModelCadOriginLinePattern,
            color,
            ViewModelCadOriginSize,
            ViewModelCadOriginStrokeWidth);
    }

    private void ApplyOriginPositionFromToolbar()
    {
        if (!IsFinite(ViewModelCadOriginX) || !IsFinite(ViewModelCadOriginY))
            return;

        CadDocumentViewModel.CadEditor.SetOriginPosition(
            new CadPointD(ViewModelCadOriginX, ViewModelCadOriginY));
    }

    private static string FormatColor(CadColor color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static bool TryParseColor(string? text, out CadColor color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var hex = text.Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];

        if (hex.Length == 6)
            hex = "FF" + hex;

        if (hex.Length != 8)
            return false;

        try
        {
            color = CadColor.FromArgb(
                Convert.ToByte(hex[0..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16),
                Convert.ToByte(hex[6..8], 16));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsPositiveFinite(double value)
    {
        return value > 0 && IsFinite(value);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public void Dispose()
    {
        SaveUserSettings();
        CadDocumentViewModel.ViewSettingsChanged -= OnCadDocumentViewSettingsChanged;
        CadDocumentViewModel.Dispose();
    }

    private void OnCadDocumentViewSettingsChanged(object? sender, EventArgs e)
    {
        ApplyDocumentViewSettingsToToolbar();
    }

    internal void Load(string fileName)
    {
        var document = _storage.Load(fileName);
        CadDocumentViewModel.ReplaceEditor(new CadEditor(document));
        CurrentFilePath = fileName;
    }
}
