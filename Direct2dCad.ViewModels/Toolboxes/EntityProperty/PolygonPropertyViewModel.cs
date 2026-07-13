using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class TransientPolygonPropertyViewModel : EntityPropertyViewModel
{
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public TransientPolygonPropertyViewModel(CadDocumentViewModel documentViewModel)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        RefreshFromDocument();
    }

    public CadDocumentViewModel DocumentViewModel => _documentViewModel;
    public IReadOnlyList<FillStyleOption> FillStyleOptions { get; private set; } = [];

    [ObservableProperty]
    public partial FillStyleOption? SelectedFillStyleOption { get; set; }

    [ObservableProperty]
    public partial CadColor FillColor { get; set; }

    [ObservableProperty]
    public partial CadColor StrokeColor { get; set; }

    [ObservableProperty]
    public partial bool UseByLayerColor { get; set; }

    [ObservableProperty]
    public partial double LineWeight { get; set; }

    [ObservableProperty]
    public partial bool UseByLayerLineWeight { get; set; }

    [ObservableProperty]
    public partial int ZIndex { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    public bool FillColorControlsEnabled => CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption);
    public bool ColorControlsEnabled => !UseByLayerColor;
    public bool LineWeightControlsEnabled => !UseByLayerLineWeight;

    public void RefreshFromDocument()
    {
        _isRefreshing = true;
        try
        {
            RefreshDrawingLayerOptions(_documentViewModel);
            RefreshFillStyleOptions(_documentViewModel.DrawingDefaults.PolygonFillStyleId);
            FillColor = CirclePropertyViewModel.ResolveFillColor(_documentViewModel.CadEditor.Document, _documentViewModel.DrawingDefaults.PolygonFillStyleId);
            StrokeColor = _documentViewModel.DrawingDefaults.PolygonStrokeColor;
            UseByLayerColor = _documentViewModel.DrawingDefaults.PolygonUseLayerColor;
            LineWeight = _documentViewModel.DrawingDefaults.PolygonLineWeight;
            UseByLayerLineWeight = _documentViewModel.DrawingDefaults.PolygonUseLayerLineWeight;
            ZIndex = _documentViewModel.DrawingDefaults.PolygonZIndex;
            IsVisible = _documentViewModel.DrawingDefaults.PolygonIsVisible;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    partial void OnSelectedFillStyleOptionChanged(FillStyleOption? value)
    {
        OnPropertyChanged(nameof(FillColorControlsEnabled));

        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.PolygonFillStyleId = CirclePropertyViewModel.ResolveFillStyleId(_documentViewModel.CadEditor.Document, value, FillColor);
    }

    partial void OnFillColorChanged(CadColor value)
    {
        if (_isRefreshing || !CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption))
            return;

        _documentViewModel.DrawingDefaults.PolygonFillStyleId = CirclePropertyViewModel.ResolveFillStyleId(
            _documentViewModel.CadEditor.Document,
            SelectedFillStyleOption,
            value);
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing || UseByLayerColor)
            return;

        _documentViewModel.DrawingDefaults.PolygonStrokeColor = value;
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing || UseByLayerLineWeight)
            return;

        _documentViewModel.DrawingDefaults.PolygonLineWeight = IsFinitePositive(value)
            ? value
            : CadLineWeight.Default.Value;
    }

    partial void OnUseByLayerColorChanged(bool value)
    {
        OnPropertyChanged(nameof(ColorControlsEnabled));
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.PolygonUseLayerColor = value;
    }

    partial void OnUseByLayerLineWeightChanged(bool value)
    {
        OnPropertyChanged(nameof(LineWeightControlsEnabled));
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.PolygonUseLayerLineWeight = value;
    }

    partial void OnZIndexChanged(int value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.PolygonZIndex = value;
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.PolygonIsVisible = value;
    }

    private void RefreshFillStyleOptions(StyleId? selectedStyleId)
    {
        FillStyleOptions = CirclePropertyViewModel.BuildFillStyleOptions(_documentViewModel.CadEditor.Document);
        OnPropertyChanged(nameof(FillStyleOptions));
        SelectedFillStyleOption = CirclePropertyViewModel.FindFillStyleOption(_documentViewModel.CadEditor.Document, FillStyleOptions, selectedStyleId);
        OnPropertyChanged(nameof(FillColorControlsEnabled));
    }

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
