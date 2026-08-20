using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Lang.Strings;
using MaterialDesignThemes.Wpf;

namespace Direct2dCad.wpf.Controls;

internal sealed class CadRadialMenuPopup : IDisposable
{
    private const double Diameter = 292;
    private readonly RadialMenuVisual _visual = new();
    private readonly Popup _popup;
    private Point _canvasOrigin;

    public CadRadialMenuPopup()
    {
        _popup = new Popup
        {
            AllowsTransparency = true,
            IsHitTestVisible = false,
            Placement = PlacementMode.Relative,
            StaysOpen = true,
            Child = _visual
        };
    }

    public void Show(
        FrameworkElement placementTarget,
        Point canvasOrigin,
        IReadOnlyList<CadRadialMenuAction> actions)
    {
        _canvasOrigin = canvasOrigin;
        _visual.SetResourceOwner(placementTarget);
        _visual.SetActions(actions);
        _popup.PlacementTarget = placementTarget;
        _popup.HorizontalOffset = canvasOrigin.X - Diameter * 0.5;
        _popup.VerticalOffset = canvasOrigin.Y - Diameter * 0.5;
        _popup.IsOpen = true;
    }

    public void UpdatePointer(Point canvasPoint) =>
        _visual.UpdatePointer(ToPopupPoint(canvasPoint));

    public void SetActions(IReadOnlyList<CadRadialMenuAction> actions) =>
        _visual.SetActions(actions);

    public CadRadialMenuAction? Complete(Point canvasPoint)
    {
        UpdatePointer(canvasPoint);
        var action = _visual.SelectedAction;
        Close();
        return action;
    }

    public void Close() => _popup.IsOpen = false;

    public void Dispose()
    {
        Close();
        _popup.Child = null;
        _popup.PlacementTarget = null;
    }

    private static Point Center => new(Diameter * 0.5, Diameter * 0.5);

    private Point ToPopupPoint(Point canvasPoint) => new(
        Center.X + canvasPoint.X - _canvasOrigin.X,
        Center.Y + canvasPoint.Y - _canvasOrigin.Y);

    private sealed class RadialMenuVisual : Canvas
    {
        private const double InnerRadius = 44;
        private const double OuterRadius = 136;
        private const double IconSize = 30;
        private static readonly Brush NormalIconBrush = CreateBrush(Color.FromArgb(238, 232, 232, 236));
        private static readonly Brush SelectedIconBrush = Brushes.White;
        private CadRadialMenuAction[] _actions = new CadRadialMenuAction[CadRadialMenuSettings.SectorCount];
        private readonly ContentControl[] _sectorIcons = new ContentControl[CadRadialMenuSettings.SectorCount];
        private readonly Grid _centerContent;
        private readonly TextBlock _selectedActionText;
        private FrameworkElement? _resourceOwner;
        private int _selectedIndex = -1;

        public RadialMenuVisual()
        {
            Width = Diameter;
            Height = Diameter;
            IsHitTestVisible = false;

            for (var index = 0; index < _sectorIcons.Length; index++)
            {
                var icon = new ContentControl
                {
                    Width = IconSize,
                    Height = IconSize,
                    Foreground = NormalIconBrush,
                    IsHitTestVisible = false
                };
                _sectorIcons[index] = icon;
                Children.Add(icon);
            }

            _centerContent = new Grid
            {
                Width = 82,
                Height = 82,
                IsHitTestVisible = false
            };
            _selectedActionText = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI"),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            _centerContent.Children.Add(_selectedActionText);
            SetLeft(_centerContent, Center.X - _centerContent.Width * 0.5);
            SetTop(_centerContent, Center.Y - _centerContent.Height * 0.5);
            Children.Add(_centerContent);

            UpdateActionVisuals();
        }

        public CadRadialMenuAction? SelectedAction =>
            _selectedIndex >= 0 ? _actions[_selectedIndex] : null;

        public void SetResourceOwner(FrameworkElement resourceOwner)
        {
            if (ReferenceEquals(_resourceOwner, resourceOwner))
                return;

            _resourceOwner = resourceOwner;
            foreach (var icon in _sectorIcons)
                icon.Tag = null;
            UpdateActionVisuals();
        }

        public void SetActions(IReadOnlyList<CadRadialMenuAction> actions)
        {
            for (var index = 0; index < _actions.Length; index++)
            {
                _actions[index] = index < actions.Count && Enum.IsDefined(actions[index])
                    ? actions[index]
                    : CadRadialMenuAction.None;
            }

            _selectedIndex = -1;
            UpdateActionVisuals();
            InvalidateVisual();
        }

        public void UpdatePointer(Point pointer)
        {
            var offset = pointer - Center;
            var distance = offset.Length;
            var selectedIndex = distance < InnerRadius
                ? -1
                : GetSectorIndex(offset);
            if (_selectedIndex == selectedIndex)
                return;

            _selectedIndex = selectedIndex;
            UpdateActionVisuals();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            var sectorPen = CreatePen(Color.FromArgb(210, 102, 102, 102), 1);
            var normalBrush = CreateBrush(Color.FromArgb(232, 31, 31, 34));
            var selectedBrush = CreateBrush(Color.FromArgb(242, 103, 58, 183));
            var selectedPen = CreatePen(Color.FromArgb(255, 224, 196, 255), 1.5);

            for (var index = 0; index < _actions.Length; index++)
            {
                var selected = index == _selectedIndex;
                drawingContext.DrawGeometry(
                    selected ? selectedBrush : normalBrush,
                    selected ? selectedPen : sectorPen,
                    CreateSector(index));
            }

            drawingContext.DrawEllipse(
                CreateBrush(Color.FromArgb(246, 43, 43, 47)),
                CreatePen(Color.FromArgb(220, 165, 165, 170), 1),
                Center,
                InnerRadius,
                InnerRadius);
            drawingContext.DrawEllipse(
                null,
                CreatePen(Color.FromArgb(170, 245, 245, 245), 1),
                Center,
                OuterRadius,
                OuterRadius);
        }

        private static Geometry CreateSector(int index)
        {
            var sectorAngle = Math.PI * 2 / CadRadialMenuSettings.SectorCount;
            var centerAngle = -Math.PI * 0.5 + index * sectorAngle;
            var startAngle = centerAngle - sectorAngle * 0.5;
            var endAngle = centerAngle + sectorAngle * 0.5;
            var startInner = PointOnCircle(InnerRadius, startAngle);
            var startOuter = PointOnCircle(OuterRadius, startAngle);
            var endOuter = PointOnCircle(OuterRadius, endAngle);
            var endInner = PointOnCircle(InnerRadius, endAngle);

            var geometry = new StreamGeometry();
            using var context = geometry.Open();
            context.BeginFigure(startInner, isFilled: true, isClosed: true);
            context.LineTo(startOuter, isStroked: true, isSmoothJoin: false);
            context.ArcTo(
                endOuter,
                new Size(OuterRadius, OuterRadius),
                0,
                isLargeArc: false,
                SweepDirection.Clockwise,
                isStroked: true,
                isSmoothJoin: false);
            context.LineTo(endInner, isStroked: true, isSmoothJoin: false);
            context.ArcTo(
                startInner,
                new Size(InnerRadius, InnerRadius),
                0,
                isLargeArc: false,
                SweepDirection.Counterclockwise,
                isStroked: true,
                isSmoothJoin: false);
            geometry.Freeze();
            return geometry;
        }

        private void UpdateActionVisuals()
        {
            for (var index = 0; index < _sectorIcons.Length; index++)
            {
                var angle = -Math.PI * 0.5 + index * Math.PI * 2 / CadRadialMenuSettings.SectorCount;
                var iconCenter = PointOnCircle((InnerRadius + OuterRadius) * 0.5, angle);
                var icon = _sectorIcons[index];
                icon.Foreground = index == _selectedIndex ? SelectedIconBrush : NormalIconBrush;
                if (!Equals(icon.Tag, _actions[index]))
                    ApplyIcon(icon, _actions[index]);
                SetLeft(icon, iconCenter.X - IconSize * 0.5);
                SetTop(icon, iconCenter.Y - IconSize * 0.5);
            }

            _selectedActionText.Text = _selectedIndex < 0
                ? string.Empty
                : GetDisplayName(_actions[_selectedIndex]);
        }

        private static string GetDisplayName(CadRadialMenuAction action)
        {
            var resourceKey = CadRadialMenuActionCatalog.GetResourceKey(action);
            return Strings.ResourceManager.GetString(resourceKey, CultureInfo.CurrentUICulture) ?? action.ToString();
        }

        private void ApplyIcon(ContentControl host, CadRadialMenuAction action)
        {
            host.Tag = action;
            var templateKey = CadRadialMenuActionIconCatalog.GetTemplateKey(action);
            if (templateKey is not null &&
                _resourceOwner?.TryFindResource(templateKey) is DataTemplate template)
            {
                host.Content = null;
                host.ContentTemplate = template;
                return;
            }

            host.ContentTemplate = null;
            host.Content = new PackIcon
            {
                Kind = CadRadialMenuActionIconCatalog.GetFallbackKind(action),
                Width = IconSize,
                Height = IconSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
        }

        private static int GetSectorIndex(Vector offset)
        {
            var sectorAngle = Math.PI * 2 / CadRadialMenuSettings.SectorCount;
            var angle = Math.Atan2(offset.Y, offset.X) + Math.PI * 0.5 + sectorAngle * 0.5;
            if (angle < 0)
                angle += Math.PI * 2;
            return (int)Math.Floor(angle / sectorAngle) % CadRadialMenuSettings.SectorCount;
        }

        private static Point PointOnCircle(double radius, double angle) => new(
            Center.X + Math.Cos(angle) * radius,
            Center.Y + Math.Sin(angle) * radius);

        private static SolidColorBrush CreateBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Pen CreatePen(Color color, double thickness)
        {
            var pen = new Pen(CreateBrush(color), thickness)
            {
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();
            return pen;
        }
    }
}
