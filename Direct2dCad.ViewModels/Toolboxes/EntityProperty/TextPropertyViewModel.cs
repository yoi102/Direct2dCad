using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class TextPropertyViewModel : EntityPropertyViewModel,
    IEntityHeaderPropertySectionViewModel,
    IEntitySettingsPropertySectionViewModel,
    IStrokeAppearancePropertySectionViewModel
{
    private const double Epsilon = 1e-9;
    private readonly CadDocumentViewModel _documentViewModel;
    private readonly ISystemFontCatalog _systemFontCatalog;
    private bool _isRefreshing;

    public TextPropertyViewModel(
        CadDocumentViewModel documentViewModel,
        EntityId entityId,
        ISystemFontCatalog systemFontCatalog)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        _systemFontCatalog = systemFontCatalog ?? throw new ArgumentNullException(nameof(systemFontCatalog));
        EntityId = entityId;
        RefreshFromEntity();
    }

    public EntityId EntityId { get; }
    public string EntityIdText => EntityId.ToString();
    public IReadOnlyList<string> FontFamilyOptions { get; private set; } = [];
    public double BoundsWidth => TryGetText(out var text) ? text.TextBounds.Width : 0;
    public double BoundsHeight => TryGetText(out var text) ? text.TextBounds.Height : 0;
    public string BoundsSizeText => $"{BoundsWidth:F3} x {BoundsHeight:F3}";

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
    public partial string? SelectedFontFamily { get; set; }

    [ObservableProperty]
    public partial CadColor StrokeColor { get; set; }

    [ObservableProperty]
    public partial bool UseByLayerColor { get; set; }

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

    public bool ColorControlsEnabled => !UseByLayerColor;

    public bool LineWeightControlsEnabled => !UseByLayerLineWeight;

    public void RefreshFromEntity()
    {
        if (!TryGetText(out var text))
            return;

        _isRefreshing = true;
        try
        {
            RefreshLayerOptions(_documentViewModel, text);
            TextContent = text.Text;
            PositionX = text.Position.X;
            PositionY = text.Position.Y;
            Height = text.Height;
            RotationDegrees = RadiansToDegrees(text.RotationRadians);
            RefreshFontFamilyOptions(ResolveFontFamily(_documentViewModel.CadEditor.Document, text.TextStyleId));
            StrokeColor = ResolveStrokeColor(text);
            UseByLayerColor = text.UseLayerColor;
            UseByLayerLineWeight = text.UseLayerLineWeight;
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

    partial void OnSelectedFontFamilyChanged(string? value)
    {
        if (_isRefreshing || string.IsNullOrWhiteSpace(value) || !TryGetText(out var text))
            return;

        var fontFamily = value.Trim();
        if (string.Equals(
                ResolveFontFamily(_documentViewModel.CadEditor.Document, text.TextStyleId),
                fontFamily,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var styleId = ResolveOrCreateFontStyle(_documentViewModel.CadEditor.Document, fontFamily);
        _documentViewModel.CadEditor.SetTextStyle(EntityId, styleId);
        RefreshFromEntity();
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing || UseByLayerColor || !TryGetText(out var text) || ResolveStrokeColor(text) == value)
            return;

        _documentViewModel.CadEditor.SetEntityColor(EntityId, value);
    }

    partial void OnUseByLayerColorChanged(bool value)
    {
        OnPropertyChanged(nameof(ColorControlsEnabled));

        if (_isRefreshing || !TryGetText(out _))
            return;

        _documentViewModel.CadEditor.SetEntityUseLayerColor(EntityId, value);
        RefreshFromEntity();
    }

    partial void OnUseByLayerLineWeightChanged(bool value)
    {
        OnPropertyChanged(nameof(LineWeightControlsEnabled));

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

    private void RefreshFontFamilyOptions(string selectedFontFamily)
    {
        FontFamilyOptions = BuildFontFamilyOptions(
            _documentViewModel.CadEditor.Document,
            _systemFontCatalog.FontFamilies,
            selectedFontFamily);
        OnPropertyChanged(nameof(FontFamilyOptions));
        SelectedFontFamily = FontFamilyOptions.FirstOrDefault(fontFamily =>
                                 string.Equals(fontFamily, selectedFontFamily, StringComparison.OrdinalIgnoreCase))
                             ?? selectedFontFamily;
    }

    private void RaiseBoundsPropertiesChanged()
    {
        OnPropertyChanged(nameof(BoundsWidth));
        OnPropertyChanged(nameof(BoundsHeight));
        OnPropertyChanged(nameof(BoundsSizeText));
    }

    private CadColor ResolveStrokeColor(CadText text)
    {
        return ResolveStrokeColor(_documentViewModel.CadEditor.Document, text, text.GraphicStyleId);
    }

    private CadLineWeight ResolveLineWeight(CadText text)
    {
        return ResolveEntityLineWeight(_documentViewModel.CadEditor.Document, text, text.GraphicStyleId);
    }

    internal static IReadOnlyList<string> BuildFontFamilyOptions(
        CadDocument document,
        IReadOnlyList<string> systemFontFamilies,
        string? selectedFontFamily = null)
    {
        var fontFamilies = new HashSet<string>(systemFontFamilies, StringComparer.OrdinalIgnoreCase)
        {
            "Meiryo"
        };

        foreach (var style in document.Styles.Values.OfType<CadTextStyle>())
            fontFamilies.Add(style.FontFamily);

        if (!string.IsNullOrWhiteSpace(selectedFontFamily))
            fontFamilies.Add(selectedFontFamily.Trim());

        return fontFamilies.Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal static string ResolveFontFamily(CadDocument document, StyleId? textStyleId)
    {
        return textStyleId is { } styleId &&
               document.TryGetStyle(styleId, out var style) &&
               style is CadTextStyle textStyle
            ? textStyle.FontFamily
            : "Meiryo";
    }

    internal static StyleId? ResolveOrCreateFontStyle(CadDocument document, string fontFamily)
    {
        var normalizedFontFamily = fontFamily.Trim();
        if (string.Equals(normalizedFontFamily, "Meiryo", StringComparison.OrdinalIgnoreCase))
            return null;

        var existingStyle = document.Styles.Values
            .OfType<CadTextStyle>()
            .FirstOrDefault(style =>
                string.Equals(style.FontFamily, normalizedFontFamily, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(style.TextHeight - 1.0) <= Epsilon &&
                Math.Abs(style.WidthFactor - 1.0) <= Epsilon &&
                Math.Abs(style.ObliqueAngle) <= Epsilon &&
                !style.IsBold &&
                !style.IsItalic);
        if (existingStyle is not null)
            return existingStyle.Id;

        return document.CreateTextStyle(
            CreateFontStyleName(document, normalizedFontFamily),
            normalizedFontFamily,
            textHeight: 1.0);
    }

    private static string CreateFontStyleName(CadDocument document, string fontFamily)
    {
        var baseName = $"Font - {fontFamily}";
        var existingNames = document.Styles.Values
            .Select(style => style.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existingNames.Contains(baseName))
            return baseName;

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} ({suffix})";
            if (!existingNames.Contains(candidate))
                return candidate;
        }
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

public partial class TransientTextPropertyViewModel : EntityPropertyViewModel,
    IEntitySettingsPropertySectionViewModel,
    IStrokeAppearancePropertySectionViewModel
{
    private readonly CadDocumentViewModel _documentViewModel;
    private readonly ISystemFontCatalog _systemFontCatalog;
    private bool _isRefreshing;

    public TransientTextPropertyViewModel(
        CadDocumentViewModel documentViewModel,
        ISystemFontCatalog systemFontCatalog)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        _systemFontCatalog = systemFontCatalog ?? throw new ArgumentNullException(nameof(systemFontCatalog));
        RefreshFromDocument();
    }

    public CadDocumentViewModel DocumentViewModel => _documentViewModel;
    public IReadOnlyList<string> FontFamilyOptions { get; private set; } = [];

    [ObservableProperty]
    public partial string TextContent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double RotationDegrees { get; set; }

    [ObservableProperty]
    public partial string? SelectedFontFamily { get; set; }

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

    [ObservableProperty]
    public partial bool IsInverted { get; set; }

    [ObservableProperty]
    public partial double InvertedMarginFactor { get; set; }

    public bool ColorControlsEnabled => !UseByLayerColor;
    public bool LineWeightControlsEnabled => !UseByLayerLineWeight;

    public void RefreshFromDocument()
    {
        _isRefreshing = true;
        try
        {
            RefreshDrawingLayerOptions(_documentViewModel);
            TextContent = _documentViewModel.DrawingDefaults.Text;
            RotationDegrees = _documentViewModel.DrawingDefaults.TextRotationDegrees;
            RefreshFontFamilyOptions(TextPropertyViewModel.ResolveFontFamily(
                _documentViewModel.CadEditor.Document,
                _documentViewModel.DrawingDefaults.TextStyleId));
            StrokeColor = _documentViewModel.DrawingDefaults.TextStrokeColor;
            UseByLayerColor = _documentViewModel.DrawingDefaults.TextUseLayerColor;
            LineWeight = _documentViewModel.DrawingDefaults.TextLineWeight;
            UseByLayerLineWeight = _documentViewModel.DrawingDefaults.TextUseLayerLineWeight;
            ZIndex = _documentViewModel.DrawingDefaults.TextZIndex;
            IsVisible = _documentViewModel.DrawingDefaults.TextIsVisible;
            IsInverted = _documentViewModel.DrawingDefaults.TextInverted;
            InvertedMarginFactor = _documentViewModel.DrawingDefaults.TextInvertedMarginFactor;
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

        _documentViewModel.DrawingDefaults.Text = value ?? string.Empty;
    }

    partial void OnRotationDegreesChanged(double value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.TextRotationDegrees = IsFinite(value)
            ? value
            : 0;
    }

    partial void OnSelectedFontFamilyChanged(string? value)
    {
        if (_isRefreshing || string.IsNullOrWhiteSpace(value))
            return;

        var document = _documentViewModel.CadEditor.Document;
        var fontFamily = value.Trim();
        if (string.Equals(
                TextPropertyViewModel.ResolveFontFamily(document, _documentViewModel.DrawingDefaults.TextStyleId),
                fontFamily,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _documentViewModel.DrawingDefaults.TextStyleId =
            TextPropertyViewModel.ResolveOrCreateFontStyle(document, fontFamily);
        RefreshFromDocument();
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing || UseByLayerColor)
            return;

        _documentViewModel.DrawingDefaults.TextStrokeColor = value;
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing || UseByLayerLineWeight)
            return;

        _documentViewModel.DrawingDefaults.TextLineWeight = IsFinitePositive(value)
            ? value
            : CadLineWeight.Default.Value;
    }

    partial void OnUseByLayerColorChanged(bool value)
    {
        OnPropertyChanged(nameof(ColorControlsEnabled));
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.TextUseLayerColor = value;
    }

    partial void OnUseByLayerLineWeightChanged(bool value)
    {
        OnPropertyChanged(nameof(LineWeightControlsEnabled));
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.TextUseLayerLineWeight = value;
    }

    partial void OnZIndexChanged(int value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.TextZIndex = value;
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.TextIsVisible = value;
    }

    partial void OnIsInvertedChanged(bool value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.TextInverted = value;
    }

    partial void OnInvertedMarginFactorChanged(double value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.TextInvertedMarginFactor = IsFiniteNonNegative(value)
            ? value
            : CadText.DefaultInvertedMarginFactor;
    }

    private void RefreshFontFamilyOptions(string selectedFontFamily)
    {
        FontFamilyOptions = TextPropertyViewModel.BuildFontFamilyOptions(
            _documentViewModel.CadEditor.Document,
            _systemFontCatalog.FontFamilies,
            selectedFontFamily);
        OnPropertyChanged(nameof(FontFamilyOptions));
        SelectedFontFamily = FontFamilyOptions.FirstOrDefault(fontFamily =>
                                 string.Equals(fontFamily, selectedFontFamily, StringComparison.OrdinalIgnoreCase))
                             ?? selectedFontFamily;
    }

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsFiniteNonNegative(double value)
    {
        return value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
