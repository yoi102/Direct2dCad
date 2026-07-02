using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public sealed record TextStyleOption(StyleId? Id, string Name)
{
    public override string ToString() => Name;
}

public partial class TextPropertyViewModel : EntityPropertyViewModel
{
    private const double Epsilon = 1e-9;
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public TextPropertyViewModel(CadDocumentViewModel documentViewModel, EntityId entityId)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        EntityId = entityId;
        RefreshFromEntity();
    }

    public EntityId EntityId { get; }
    public string EntityIdText => EntityId.ToString();
    public IReadOnlyList<TextStyleOption> TextStyleOptions { get; private set; } = [];
    public double BoundsWidth => TryGetText(out var text) ? text.TextBounds.Width : 0;
    public double BoundsHeight => TryGetText(out var text) ? text.TextBounds.Height : 0;
    public string BoundsSizeText => $"{BoundsWidth:F3} x {BoundsHeight:F3}";
    public string BoundsMeasurementState => TryGetText(out var text) && text.RequiresBoundsMeasurement
        ? "Pending"
        : "Measured";

    [ObservableProperty]
    public partial string TextContent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double PositionX { get; set; }

    [ObservableProperty]
    public partial double PositionY { get; set; }

    [ObservableProperty]
    public partial double Height { get; set; }

    [ObservableProperty]
    public partial double RotationDegrees { get; set; }

    [ObservableProperty]
    public partial TextStyleOption? SelectedTextStyleOption { get; set; }

    [ObservableProperty]
    public partial CadColor StrokeColor { get; set; }

    [ObservableProperty]
    public partial bool UseByLayerLineWeight { get; set; }

    [ObservableProperty]
    public partial double LineWeight { get; set; }

    [ObservableProperty]
    public partial int ZIndex { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial bool IsInverted { get; set; }

    [ObservableProperty]
    public partial double InvertedMarginFactor { get; set; }

    public void RefreshFromEntity()
    {
        if (!TryGetText(out var text))
            return;

        _isRefreshing = true;
        try
        {
            TextContent = text.Text;
            PositionX = text.Position.X;
            PositionY = text.Position.Y;
            Height = text.Height;
            RotationDegrees = RadiansToDegrees(text.RotationRadians);
            RefreshTextStyleOptions(text.TextStyleId);
            StrokeColor = ResolveStrokeColor(text);
            UseByLayerLineWeight = text.LineWeight is null || text.LineWeight.Value.IsByLayer;
            LineWeight = ResolveLineWeight(text).Value;
            ZIndex = text.ZIndex;
            IsVisible = text.IsVisible;
            IsInverted = text.IsInverted;
            InvertedMarginFactor = text.InvertedMarginFactor;
        }
        finally
        {
            _isRefreshing = false;
        }

        RaiseBoundsPropertiesChanged();
    }

    partial void OnTextContentChanged(string value)
    {
        if (_isRefreshing || !TryGetText(out var text) || text.Text == (value ?? string.Empty))
            return;

        _documentViewModel.CadEditor.SetTextContent(EntityId, value ?? string.Empty);
    }

    partial void OnPositionXChanged(double value) => CommitGeometry();

    partial void OnPositionYChanged(double value) => CommitGeometry();

    partial void OnHeightChanged(double value) => CommitGeometry();

    partial void OnRotationDegreesChanged(double value) => CommitGeometry();

    partial void OnSelectedTextStyleOptionChanged(TextStyleOption? value)
    {
        if (_isRefreshing || !TryGetText(out var text))
            return;

        var styleId = value?.Id;
        if (Nullable.Equals(text.TextStyleId, styleId))
            return;

        _documentViewModel.CadEditor.SetTextStyle(EntityId, styleId);
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing || !TryGetText(out var text) || ResolveStrokeColor(text) == value)
            return;

        _documentViewModel.CadEditor.SetEntityColor(EntityId, value);
    }

    partial void OnUseByLayerLineWeightChanged(bool value)
    {
        if (_isRefreshing || !TryGetText(out _))
            return;

        _documentViewModel.CadEditor.SetEntityLineWeight(
            EntityId,
            value ? CadLineWeight.ByLayer : new CadLineWeight(ResolveLineWeightValue(LineWeight)));
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing || UseByLayerLineWeight || !TryGetText(out _))
            return;

        if (!IsFinitePositive(value))
        {
            RefreshFromEntity();
            return;
        }

        _documentViewModel.CadEditor.SetEntityLineWeight(EntityId, new CadLineWeight(value));
    }

    partial void OnZIndexChanged(int value)
    {
        if (_isRefreshing || !TryGetText(out var text) || text.ZIndex == value)
            return;

        _documentViewModel.CadEditor.SetEntityZIndex(EntityId, value);
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing || !TryGetText(out var text) || text.IsVisible == value)
            return;

        _documentViewModel.CadEditor.SetEntityVisibility(EntityId, value);
    }

    partial void OnIsInvertedChanged(bool value)
    {
        if (_isRefreshing || !TryGetText(out var text) || text.IsInverted == value)
            return;

        _documentViewModel.CadEditor.SetTextInverted(EntityId, value);
    }

    partial void OnInvertedMarginFactorChanged(double value)
    {
        if (_isRefreshing || !TryGetText(out var text))
            return;

        if (!IsFiniteNonNegative(value))
        {
            RefreshFromEntity();
            return;
        }

        if (Math.Abs(text.InvertedMarginFactor - value) <= Epsilon)
            return;

        _documentViewModel.CadEditor.SetTextInvertedMarginFactor(EntityId, value);
    }

    private void CommitGeometry()
    {
        if (_isRefreshing || !TryGetText(out var text))
            return;

        if (!TryCreateGeometry(out var position, out var height, out var rotationRadians))
        {
            RefreshFromEntity();
            return;
        }

        if (position.DistanceSquaredTo(text.Position) <= Epsilon &&
            Math.Abs(height - text.Height) <= Epsilon &&
            Math.Abs(rotationRadians - text.RotationRadians) <= Epsilon)
        {
            return;
        }

        _documentViewModel.CadEditor.SetTextGeometry(EntityId, position, height, rotationRadians);
    }

    private bool TryCreateGeometry(out CadPointD position, out double height, out double rotationRadians)
    {
        position = new CadPointD(PositionX, PositionY);
        height = Height;
        rotationRadians = DegreesToRadians(RotationDegrees);

        return IsFinite(PositionX) &&
               IsFinite(PositionY) &&
               IsFinitePositive(height) &&
               IsFinite(RotationDegrees);
    }

    private bool TryGetText(out CadText text)
    {
        if (_documentViewModel.CadEditor.Document.TryGetEntity(EntityId, out var entity) &&
            entity is CadText currentText &&
            !currentText.IsErased)
        {
            text = currentText;
            return true;
        }

        text = null!;
        return false;
    }

    private void RefreshTextStyleOptions(StyleId? selectedStyleId)
    {
        TextStyleOptions = BuildTextStyleOptions(_documentViewModel.CadEditor.Document);
        OnPropertyChanged(nameof(TextStyleOptions));
        SelectedTextStyleOption = FindTextStyleOption(TextStyleOptions, selectedStyleId);
    }

    private void RaiseBoundsPropertiesChanged()
    {
        OnPropertyChanged(nameof(BoundsWidth));
        OnPropertyChanged(nameof(BoundsHeight));
        OnPropertyChanged(nameof(BoundsSizeText));
        OnPropertyChanged(nameof(BoundsMeasurementState));
    }

    private CadColor ResolveStrokeColor(CadText text)
    {
        var document = _documentViewModel.CadEditor.Document;
        var layer = document.GetLayer(text.LayerId);
        var styleId = text.GraphicStyleId ?? layer.DefaultGraphicStyleId;

        if (styleId is { } graphicStyleId &&
            document.TryGetStyle(graphicStyleId, out var style) &&
            style is CadGraphicStyle graphic)
        {
            return graphic.StrokeColor;
        }

        return layer.Color;
    }

    private CadLineWeight ResolveLineWeight(CadText text)
    {
        var document = _documentViewModel.CadEditor.Document;
        var layer = document.GetLayer(text.LayerId);
        var styleId = text.GraphicStyleId ?? layer.DefaultGraphicStyleId;
        var styleWeight = styleId is { } graphicStyleId &&
                          document.TryGetStyle(graphicStyleId, out var style) &&
                          style is CadGraphicStyle graphic
            ? graphic.LineWeight
            : (CadLineWeight?)null;

        var weight = text.LineWeight is { IsByLayer: false }
            ? text.LineWeight.Value
            : styleWeight is { IsByLayer: false }
            ? styleWeight.Value
            : layer.LineWeight;

        return weight.IsByLayer || weight.Value <= 0
            ? CadLineWeight.Default
            : weight;
    }

    internal static IReadOnlyList<TextStyleOption> BuildTextStyleOptions(CadDocument document)
    {
        var options = new List<TextStyleOption>
        {
            new(null, "Default (Meiryo)")
        };

        options.AddRange(document.Styles.Values
            .OfType<CadTextStyle>()
            .OrderBy(style => style.Name, StringComparer.OrdinalIgnoreCase)
            .Select(style => new TextStyleOption(style.Id, style.Name)));

        return options;
    }

    internal static TextStyleOption? FindTextStyleOption(
        IReadOnlyList<TextStyleOption> options,
        StyleId? styleId)
    {
        return options.FirstOrDefault(option => Nullable.Equals(option.Id, styleId)) ??
               options.FirstOrDefault();
    }

    private static double ResolveLineWeightValue(double value)
    {
        return IsFinitePositive(value) ? value : CadLineWeight.Default.Value;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && IsFinite(value);
    }

    private static bool IsFiniteNonNegative(double value)
    {
        return value >= 0 && IsFinite(value);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

public partial class TransientTextPropertyViewModel : EntityPropertyViewModel
{
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public TransientTextPropertyViewModel(CadDocumentViewModel documentViewModel)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        RefreshFromDocument();
    }

    public CadDocumentViewModel DocumentViewModel => _documentViewModel;
    public IReadOnlyList<TextStyleOption> TextStyleOptions { get; private set; } = [];

    [ObservableProperty]
    public partial string TextContent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial TextStyleOption? SelectedTextStyleOption { get; set; }

    [ObservableProperty]
    public partial CadColor StrokeColor { get; set; }

    [ObservableProperty]
    public partial double LineWeight { get; set; }

    [ObservableProperty]
    public partial int ZIndex { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial bool IsInverted { get; set; }

    [ObservableProperty]
    public partial double InvertedMarginFactor { get; set; }

    public void RefreshFromDocument()
    {
        _isRefreshing = true;
        try
        {
            TextContent = _documentViewModel.DrawingText;
            RefreshTextStyleOptions(_documentViewModel.DrawingTextStyleId);
            StrokeColor = _documentViewModel.DrawingTextStrokeColor;
            LineWeight = _documentViewModel.DrawingTextLineWeight;
            ZIndex = _documentViewModel.DrawingTextZIndex;
            IsVisible = _documentViewModel.DrawingTextIsVisible;
            IsInverted = _documentViewModel.DrawingTextInverted;
            InvertedMarginFactor = _documentViewModel.DrawingTextInvertedMarginFactor;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    partial void OnTextContentChanged(string value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingText = value ?? string.Empty;
    }

    partial void OnSelectedTextStyleOptionChanged(TextStyleOption? value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingTextStyleId = value?.Id;
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingTextStrokeColor = value;
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingTextLineWeight = IsFinitePositive(value)
            ? value
            : CadLineWeight.Default.Value;
    }

    partial void OnZIndexChanged(int value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingTextZIndex = value;
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingTextIsVisible = value;
    }

    partial void OnIsInvertedChanged(bool value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingTextInverted = value;
    }

    partial void OnInvertedMarginFactorChanged(double value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingTextInvertedMarginFactor = IsFiniteNonNegative(value)
            ? value
            : CadText.DefaultInvertedMarginFactor;
    }

    private void RefreshTextStyleOptions(StyleId? selectedStyleId)
    {
        TextStyleOptions = TextPropertyViewModel.BuildTextStyleOptions(_documentViewModel.CadEditor.Document);
        OnPropertyChanged(nameof(TextStyleOptions));
        SelectedTextStyleOption = TextPropertyViewModel.FindTextStyleOption(TextStyleOptions, selectedStyleId);
    }

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsFiniteNonNegative(double value)
    {
        return value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
