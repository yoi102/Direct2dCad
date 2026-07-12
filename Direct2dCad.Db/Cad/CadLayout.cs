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
        PaperWidth = GuardPositive(width, nameof(width));
        PaperHeight = GuardPositive(height, nameof(height));
        MarginLeft = GuardNonNegative(marginLeft, nameof(marginLeft));
        MarginTop = GuardNonNegative(marginTop, nameof(marginTop));
        MarginRight = GuardNonNegative(marginRight, nameof(marginRight));
        MarginBottom = GuardNonNegative(marginBottom, nameof(marginBottom));
        if (MarginLeft + MarginRight >= PaperWidth || MarginTop + MarginBottom >= PaperHeight)
            throw new ArgumentException("Paper margins must leave a positive printable area.");
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
    public bool IsLocked { get; private set; } = true;

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
        if (bounds.IsEmpty)
            throw new ArgumentException("Layout viewport bounds cannot be empty.", nameof(bounds));
        if (scale <= 0 || !double.IsFinite(scale))
            throw new ArgumentOutOfRangeException(nameof(scale));
        Bounds = bounds;
        ModelCenter = modelCenter;
        Scale = scale;
        RotationRadians = rotationRadians;
    }

    public void SetVisible(bool visible) => IsVisible = visible;
    public void SetLocked(bool locked) => IsLocked = locked;
}
