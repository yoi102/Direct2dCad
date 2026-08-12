using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class OleObjectPropertyViewModel : EntityPropertyViewModel,
    IEntityHeaderPropertySectionViewModel,
    IEntitySettingsPropertySectionViewModel
{
    private const double Epsilon = 1e-9;
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;
    private bool _isUpdatingGeometryProperties;

    public OleObjectPropertyViewModel(CadDocumentViewModel documentViewModel, EntityId entityId)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        EntityId = entityId;
        RefreshFromEntity();
    }

    public string Title => "OLE Object";
    public EntityId EntityId { get; }
    public string EntityIdText => EntityId.ToString();
    public bool SupportsRotation => false;

    [ObservableProperty] public partial string SourceName { get; private set; } = string.Empty;
    [ObservableProperty] public partial string ContentType { get; private set; } = string.Empty;
    [ObservableProperty] public partial double Left { get; set; }
    [ObservableProperty] public partial double Bottom { get; set; }
    [ObservableProperty] public partial double Right { get; set; }
    [ObservableProperty] public partial double Top { get; set; }
    [ObservableProperty] public partial double CenterX { get; set; }
    [ObservableProperty] public partial double CenterY { get; set; }
    [ObservableProperty] public partial double Width { get; set; }
    [ObservableProperty] public partial double Height { get; set; }
    [ObservableProperty] public partial int ZIndex { get; set; }
    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial double Opacity { get; set; }

    public void RefreshFromEntity()
    {
        if (!TryGetOleObject(out var oleObject))
            return;

        _isRefreshing = true;
        try
        {
            RefreshLayerOptions(_documentViewModel, oleObject);
            SourceName = oleObject.SourceName;
            ContentType = oleObject.ContentType;
            RefreshGeometryProperties(oleObject.Bounds);
            ZIndex = oleObject.ZIndex;
            IsVisible = oleObject.IsVisible;
            Opacity = oleObject.Opacity;
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
        if (_isRefreshing || !TryGetOleObject(out var oleObject) || oleObject.ZIndex == value)
            return;

        _documentViewModel.CadEditor.SetEntityZIndex(EntityId, value);
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing || !TryGetOleObject(out var oleObject) || oleObject.IsVisible == value)
            return;

        _documentViewModel.CadEditor.SetEntityVisibility(EntityId, value);
    }

    partial void OnOpacityChanged(double value)
    {
        if (_isRefreshing || !TryGetOleObject(out var oleObject))
            return;

        if (!IsFinite(value))
        {
            RefreshFromEntity();
            return;
        }

        var opacity = Math.Clamp(value, 0.0, 1.0);
        if (Math.Abs(oleObject.Opacity - opacity) <= Epsilon)
            return;

        _documentViewModel.CadEditor.SetEntityOpacity(EntityId, opacity);
    }

    private void CommitEdgeGeometryChange()
    {
        if (_isRefreshing || _isUpdatingGeometryProperties || !TryGetOleObject(out _))
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
        if (_isRefreshing || _isUpdatingGeometryProperties || !TryGetOleObject(out _))
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
        if (!TryGetOleObject(out var oleObject) || oleObject.Bounds.NearEquals(bounds, Epsilon))
            return;

        _documentViewModel.CadEditor.SetOleObjectBounds(EntityId, bounds);
    }

    private bool TryCreateBoundsFromEdges(out CadRectD bounds)
    {
        bounds = CadRectD.FromLTRB(
            ToModelLength(Left),
            ToModelLength(Bottom),
            ToModelLength(Right),
            ToModelLength(Top));
        return IsFinite(Left) &&
               IsFinite(Bottom) &&
               IsFinite(Right) &&
               IsFinite(Top) &&
               bounds.Width > Epsilon &&
               bounds.Height > Epsilon;
    }

    private bool TryCreateBoundsFromCenterSize(out CadRectD bounds)
    {
        bounds = CadRectD.FromCenter(
            ToModelPoint(new CadPointD(CenterX, CenterY)),
            ToModelLength(Width),
            ToModelLength(Height));
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
        Left = ToDisplayLength(bounds.Left);
        Bottom = ToDisplayLength(bounds.Bottom);
        Right = ToDisplayLength(bounds.Right);
        Top = ToDisplayLength(bounds.Top);
    }

    private void UpdateCenterSizeProperties(CadRectD bounds)
    {
        var displayCenter = ToDisplayPoint(bounds.Center);
        CenterX = displayCenter.X;
        CenterY = displayCenter.Y;
        Width = ToDisplayLength(bounds.Width);
        Height = ToDisplayLength(bounds.Height);
    }

    private bool TryGetOleObject(out CadOleObject oleObject)
    {
        if (_documentViewModel.CadEditor.Document.TryGetEntity(EntityId, out var entity) &&
            entity is CadOleObject currentOleObject &&
            !currentOleObject.IsErased)
        {
            oleObject = currentOleObject;
            return true;
        }

        oleObject = null!;
        return false;
    }

    private static bool IsFinitePositive(double value) => value > 0 && IsFinite(value);

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
