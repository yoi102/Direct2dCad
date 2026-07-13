using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class ImagePropertyViewModel : EntityPropertyViewModel,
    IEntityHeaderPropertySectionViewModel,
    IEntitySettingsPropertySectionViewModel
{
    private const double Epsilon = 1e-9;
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;
    private bool _isUpdatingGeometryProperties;

    public ImagePropertyViewModel(CadDocumentViewModel documentViewModel, EntityId entityId)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        EntityId = entityId;
        RefreshFromEntity();
    }

    public EntityId EntityId { get; }
    public string Title => "Image";
    public string EntityIdText => EntityId.ToString();
    public bool SupportsRotation => true;

    [ObservableProperty]
    public partial string SourceName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ContentType { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int PixelWidth { get; private set; }

    [ObservableProperty]
    public partial int PixelHeight { get; private set; }

    [ObservableProperty]
    public partial double Left { get; set; }

    [ObservableProperty]
    public partial double Bottom { get; set; }

    [ObservableProperty]
    public partial double Right { get; set; }

    [ObservableProperty]
    public partial double Top { get; set; }

    [ObservableProperty]
    public partial double CenterX { get; set; }

    [ObservableProperty]
    public partial double CenterY { get; set; }

    [ObservableProperty]
    public partial double Width { get; set; }

    [ObservableProperty]
    public partial double Height { get; set; }

    [ObservableProperty]
    public partial int ZIndex { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial double Opacity { get; set; }

    [ObservableProperty]
    public partial double RotationDegrees { get; set; }

    public void RefreshFromEntity()
    {
        if (!TryGetImage(out var image))
            return;

        _isRefreshing = true;
        try
        {
            RefreshLayerOptions(_documentViewModel, image);
            SourceName = image.SourceName;
            ContentType = image.ContentType;
            PixelWidth = image.PixelWidth;
            PixelHeight = image.PixelHeight;
            RefreshGeometryProperties(image.FrameBounds);
            ZIndex = image.ZIndex;
            IsVisible = image.IsVisible;
            Opacity = image.Opacity;
            RotationDegrees = RadiansToDegrees(image.RotationRadians);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    partial void OnLeftChanged(double value) => CommitEdgeGeometryChange();
    partial void OnBottomChanged(double value) => CommitEdgeGeometryChange();
    partial void OnRightChanged(double value) => CommitEdgeGeometryChange();
    partial void OnTopChanged(double value) => CommitEdgeGeometryChange();
    partial void OnCenterXChanged(double value) => CommitCenterSizeGeometryChange();
    partial void OnCenterYChanged(double value) => CommitCenterSizeGeometryChange();
    partial void OnWidthChanged(double value) => CommitCenterSizeGeometryChange();
    partial void OnHeightChanged(double value) => CommitCenterSizeGeometryChange();

    partial void OnZIndexChanged(int value)
    {
        if (_isRefreshing || !TryGetImage(out var image) || image.ZIndex == value)
            return;

        _documentViewModel.CadEditor.SetEntityZIndex(EntityId, value);
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing || !TryGetImage(out var image) || image.IsVisible == value)
            return;

        _documentViewModel.CadEditor.SetEntityVisibility(EntityId, value);
    }

    partial void OnOpacityChanged(double value)
    {
        if (_isRefreshing || !TryGetImage(out var image))
            return;

        if (!IsFinite(value))
        {
            RefreshFromEntity();
            return;
        }

        var opacity = Math.Clamp(value, 0.0, 1.0);
        if (Math.Abs(image.Opacity - opacity) <= Epsilon)
            return;

        _documentViewModel.CadEditor.SetEntityOpacity(EntityId, opacity);
    }

    partial void OnRotationDegreesChanged(double value)
    {
        if (_isRefreshing || !TryGetImage(out var image))
            return;

        if (!IsFinite(value))
        {
            RefreshFromEntity();
            return;
        }

        var rotationRadians = DegreesToRadians(value);
        if (Math.Abs(image.RotationRadians - rotationRadians) <= Epsilon)
            return;

        _documentViewModel.CadEditor.SetImageRotation(EntityId, rotationRadians);
    }

    private void CommitEdgeGeometryChange()
    {
        if (_isRefreshing || _isUpdatingGeometryProperties || !TryGetImage(out _))
            return;

        if (!TryCreateBoundsFromEdges(out var bounds))
        {
            RefreshFromEntity();
            return;
        }

        _isUpdatingGeometryProperties = true;
        try
        {
            UpdateCenterSizeProperties(bounds);
        }
        finally
        {
            _isUpdatingGeometryProperties = false;
        }

        CommitGeometry(bounds);
    }

    private void CommitCenterSizeGeometryChange()
    {
        if (_isRefreshing || _isUpdatingGeometryProperties || !TryGetImage(out _))
            return;

        if (!TryCreateBoundsFromCenterSize(out var bounds))
        {
            RefreshFromEntity();
            return;
        }

        _isUpdatingGeometryProperties = true;
        try
        {
            UpdateEdgeProperties(bounds);
        }
        finally
        {
            _isUpdatingGeometryProperties = false;
        }

        CommitGeometry(bounds);
    }

    private void CommitGeometry(CadRectD bounds)
    {
        if (!TryGetImage(out var image) || image.FrameBounds.NearEquals(bounds, Epsilon))
            return;

        _documentViewModel.CadEditor.SetImageBounds(EntityId, bounds);
    }

    private bool TryCreateBoundsFromEdges(out CadRectD bounds)
    {
        bounds = new CadRectD(Left, Bottom, Right, Top);
        return IsFinite(Left) &&
               IsFinite(Bottom) &&
               IsFinite(Right) &&
               IsFinite(Top) &&
               bounds.Width > Epsilon &&
               bounds.Height > Epsilon;
    }

    private bool TryCreateBoundsFromCenterSize(out CadRectD bounds)
    {
        bounds = CadRectD.FromCenter(new CadPointD(CenterX, CenterY), Width, Height);
        return IsFinite(CenterX) &&
               IsFinite(CenterY) &&
               IsFinitePositive(Width) &&
               IsFinitePositive(Height);
    }

    private void RefreshGeometryProperties(CadRectD bounds)
    {
        UpdateEdgeProperties(bounds);
        UpdateCenterSizeProperties(bounds);
    }

    private void UpdateEdgeProperties(CadRectD bounds)
    {
        Left = bounds.Left;
        Bottom = bounds.Bottom;
        Right = bounds.Right;
        Top = bounds.Top;
    }

    private void UpdateCenterSizeProperties(CadRectD bounds)
    {
        CenterX = bounds.Center.X;
        CenterY = bounds.Center.Y;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    private bool TryGetImage(out CadImage image)
    {
        if (_documentViewModel.CadEditor.Document.TryGetEntity(EntityId, out var entity) &&
            entity is CadImage currentImage &&
            !currentImage.IsErased)
        {
            image = currentImage;
            return true;
        }

        image = null!;
        return false;
    }

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && IsFinite(value);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
}
