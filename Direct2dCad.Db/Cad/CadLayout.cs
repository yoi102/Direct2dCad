using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Cad;

public sealed class CadLayout
{
    private readonly List<CadLayoutViewport> _viewports = [];

    public LayoutId Id { get; }
    public string Name { get; private set; }
    public BlockId PaperSpaceBlockId { get; }
    public double PaperWidth { get; private set; }
    public double PaperHeight { get; private set; }
    public double MarginLeft { get; private set; }
    public double MarginTop { get; private set; }
    public double MarginRight { get; private set; }
    public double MarginBottom { get; private set; }
    public CadColor PaperColor { get; private set; } = CadColor.White;
    public IReadOnlyList<CadLayoutViewport> Viewports => _viewports;

    internal CadLayout(
        LayoutId id,
        string name,
        BlockId paperSpaceBlockId,
        double paperWidth,
        double paperHeight)
    {
        Id = id;
        Name = GuardName(name);
        PaperSpaceBlockId = paperSpaceBlockId;
        SetPaper(paperWidth, paperHeight, 10, 10, 10, 10);
    }

    public void Rename(string name) => Name = GuardName(name);

    public void SetPaper(
        double width,
        double height,
        double marginLeft,
        double marginTop,
        double marginRight,
        double marginBottom)
    {
        var validatedWidth = GuardPositive(width, nameof(width));
        var validatedHeight = GuardPositive(height, nameof(height));
        var validatedMarginLeft = GuardNonNegative(marginLeft, nameof(marginLeft));
        var validatedMarginTop = GuardNonNegative(marginTop, nameof(marginTop));
        var validatedMarginRight = GuardNonNegative(marginRight, nameof(marginRight));
        var validatedMarginBottom = GuardNonNegative(marginBottom, nameof(marginBottom));
        if (validatedMarginLeft + validatedMarginRight >= validatedWidth ||
            validatedMarginTop + validatedMarginBottom >= validatedHeight)
        {
            throw new ArgumentException("Paper margins must leave a positive printable area.");
        }

        PaperWidth = validatedWidth;
        PaperHeight = validatedHeight;
        MarginLeft = validatedMarginLeft;
        MarginTop = validatedMarginTop;
        MarginRight = validatedMarginRight;
        MarginBottom = validatedMarginBottom;
    }

    public void SetPaperColor(CadColor color) => PaperColor = color;

    internal void AddViewport(CadLayoutViewport viewport)
    {
        if (_viewports.Any(item => item.Id.Equals(viewport.Id)))
            throw new InvalidOperationException($"Layout viewport already exists: {viewport.Id}");
        _viewports.Add(viewport);
    }

    internal bool RemoveViewport(LayoutViewportId viewportId) =>
        _viewports.RemoveAll(item => item.Id.Equals(viewportId)) > 0;

    public CadLayoutViewport GetViewport(LayoutViewportId viewportId) =>
        _viewports.FirstOrDefault(item => item.Id.Equals(viewportId)) ??
        throw new KeyNotFoundException($"Layout viewport not found: {viewportId}");

    public CadRectD PaperBounds => new(0, 0, PaperWidth, PaperHeight);

    public CadRectD PrintableBounds => CadRectD.FromLTRB(
        MarginLeft,
        MarginBottom,
        PaperWidth - MarginRight,
        PaperHeight - MarginTop);

    private static string GuardName(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Layout name cannot be empty.", nameof(name))
            : name.Trim();

    private static double GuardPositive(double value, string paramName) =>
        value > 0 && double.IsFinite(value)
            ? value
            : throw new ArgumentOutOfRangeException(paramName);

    private static double GuardNonNegative(double value, string paramName) =>
        value >= 0 && double.IsFinite(value)
            ? value
            : throw new ArgumentOutOfRangeException(paramName);
}

public sealed class CadLayoutViewport
{
    public LayoutViewportId Id { get; }
    public CadRectD Bounds { get; private set; }
    public CadPointD ModelCenter { get; private set; }
    public double Scale { get; private set; }
    public double RotationRadians { get; private set; }
    public bool IsVisible { get; private set; } = true;
    public bool IsLocked { get; private set; }

    internal CadLayoutViewport(
        LayoutViewportId id,
        CadRectD bounds,
        CadPointD modelCenter,
        double scale,
        double rotationRadians = 0)
    {
        Id = id;
        SetView(bounds, modelCenter, scale, rotationRadians);
    }

    public void SetView(CadRectD bounds, CadPointD modelCenter, double scale, double rotationRadians)
    {
        if (bounds.IsEmpty ||
            bounds.Width <= 0 ||
            bounds.Height <= 0 ||
            !double.IsFinite(bounds.MinX) ||
            !double.IsFinite(bounds.MinY) ||
            !double.IsFinite(bounds.MaxX) ||
            !double.IsFinite(bounds.MaxY))
        {
            throw new ArgumentException("Layout viewport bounds must have a finite positive size.", nameof(bounds));
        }
        if (!double.IsFinite(modelCenter.X) || !double.IsFinite(modelCenter.Y))
            throw new ArgumentOutOfRangeException(nameof(modelCenter));
        if (scale <= 0 || !double.IsFinite(scale))
            throw new ArgumentOutOfRangeException(nameof(scale));
        if (!double.IsFinite(rotationRadians))
            throw new ArgumentOutOfRangeException(nameof(rotationRadians));
        Bounds = bounds;
        ModelCenter = modelCenter;
        Scale = scale;
        RotationRadians = rotationRadians;
    }

    public void SetVisible(bool visible) => IsVisible = visible;
    public void SetLocked(bool locked) => IsLocked = locked;
}
