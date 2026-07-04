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
    public partial double LineWeight { get; set; }

    [ObservableProperty]
    public partial int ZIndex { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    public bool FillColorControlsEnabled => CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption);

    public void RefreshFromDocument()
    {
        _isRefreshing = true;
        try
        {
            RefreshFillStyleOptions(_documentViewModel.DrawingPolygonFillStyleId);
            FillColor = CirclePropertyViewModel.ResolveFillColor(_documentViewModel.CadEditor.Document, _documentViewModel.DrawingPolygonFillStyleId);
            StrokeColor = _documentViewModel.DrawingPolygonStrokeColor;
            LineWeight = _documentViewModel.DrawingPolygonLineWeight;
            ZIndex = _documentViewModel.DrawingPolygonZIndex;
            IsVisible = _documentViewModel.DrawingPolygonIsVisible;
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

        _documentViewModel.DrawingPolygonFillStyleId = CirclePropertyViewModel.ResolveFillStyleId(_documentViewModel.CadEditor.Document, value, FillColor);
    }

    partial void OnFillColorChanged(CadColor value)
    {
        if (_isRefreshing || !CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption))
            return;

        _documentViewModel.DrawingPolygonFillStyleId = CirclePropertyViewModel.ResolveFillStyleId(
            _documentViewModel.CadEditor.Document,
            SelectedFillStyleOption,
            value);
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingPolygonStrokeColor = value;
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingPolygonLineWeight = IsFinitePositive(value)
            ? value
            : CadLineWeight.Default.Value;
    }

    partial void OnZIndexChanged(int value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingPolygonZIndex = value;
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingPolygonIsVisible = value;
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
