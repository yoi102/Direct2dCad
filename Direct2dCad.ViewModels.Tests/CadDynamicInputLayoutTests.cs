using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadDynamicInputLayoutTests
{
    private const double BoxHeight = 24;

    [Fact]
    public void AngleIsDrawnAsAnArcFromItsReferenceWithTheBoxOnItsBisector()
    {
        var vertex = new CadPointD(300, 300);
        var angle = new CadDynamicInputMeasurement("Angle", vertex, new(300, 150), AngleReference: new(450, 300), AngleSweepDegrees: -90);
        var layout = Compute(new(600, 200), [("Angle", 60)], [angle]);

        var arcPoints = layout.Guides.SelectMany(g => new[] { g.Start, g.End })
            .Where(p => p.DistanceTo(vertex) > 1).Select(p => p.DistanceTo(vertex)).ToArray();
        var radius = arcPoints.Min();
        Assert.InRange(radius, 30, 90);
        // Two rays (reference and end) plus a flattened arc of equal radius.
        Assert.True(layout.Guides.Count > 4);
        Assert.All(layout.Guides.Where(g => g.Start.DistanceTo(vertex) > 1),
            g => Assert.Equal(radius, g.Start.DistanceTo(vertex), 6));
        var box = BoxOf(layout, "Angle", 60);
        var center = new CadPointD(box.X + 30, box.Y + BoxHeight / 2);
        var bisector = Math.Atan2(center.Y - vertex.Y, center.X - vertex.X) * 180 / Math.PI;
        Assert.Equal(-45, bisector, 0);
        Assert.False(box.Contains(vertex));
    }

    [Fact]
    public void LengthDimensionStaysAboveByDefault()
    {
        var length = new CadDynamicInputMeasurement("Length", new(100, 300), new(300, 300));
        var layout = Compute(new(600, 600), [("Length", 60)], [length]);
        Assert.True(BoxOf(layout, "Length", 60).Bottom < 300);
    }

    [Fact]
    public void LengthDimensionMovesToTheOtherSideWhenThePointerSitsWhereTheBoxWouldGo()
    {
        var length = new CadDynamicInputMeasurement("Length", new(100, 300), new(300, 300));
        var layout = Compute(new(200, 274), [("Length", 60)], [length]);
        var box = BoxOf(layout, "Length", 60);
        Assert.True(box.Y > 300, $"Box stayed on the blocked side: {box}");
        Assert.False(box.Contains(new(200, 274)));
    }

    [Fact]
    public void EllipseSemiaxesAndStartAngleNeverOverlapEachOtherOrThePointer()
    {
        var center = new CadPointD(400, 300);
        var pointer = new CadPointD(470, 230);
        CadDynamicInputMeasurement[] measurements =
        [
            new("AxisX", center, new(520, 300)),
            new("AxisY", center, new(400, 220)),
            new("StartAngle", center, pointer, AngleReference: new(520, 300), AngleSweepDegrees: -45)
        ];
        var layout = Compute(pointer, [("AxisX", 60), ("AxisY", 60), ("StartAngle", 70)], measurements, anchor: center);
        var boxes = new[] { BoxOf(layout, "AxisX", 60), BoxOf(layout, "AxisY", 60), BoxOf(layout, "StartAngle", 70) };
        for (var i = 0; i < boxes.Length; i++)
        {
            Assert.False(boxes[i].Contains(pointer), $"Box {i} covers the pointer.");
            for (var j = i + 1; j < boxes.Length; j++)
                Assert.False(boxes[i].Intersects(boxes[j]), $"Boxes {i} and {j} overlap: {boxes[i]} / {boxes[j]}");
        }
    }

    [Fact]
    public void BoxesDoNotHideTheLineTheyMeasure()
    {
        // The line-grip case: a 30° segment from the anchor, pointer to its lower right.
        var vertex = new CadPointD(793, 390);
        var end = new CadPointD(948, 300);
        var pointer = new CadPointD(952, 350);
        CadDynamicInputMeasurement[] measurements =
        [
            new("Length", vertex, end),
            new("Angle", vertex, end, AngleReference: new(972, 390), AngleSweepDegrees: -30)
        ];
        var layout = Compute(pointer, [("Length", 56), ("Angle", 56)], measurements, anchor: vertex);
        foreach (var key in new[] { "Length", "Angle" })
            Assert.False(BoxOf(layout, key, 56).Crosses(vertex, end), $"{key} hides the measured segment.");
        Assert.False(BoxOf(layout, "Length", 56).Intersects(BoxOf(layout, "Angle", 56)));
    }

    [Fact]
    public void BoxesStayInsideTheCanvasNearItsEdges()
    {
        var surface = (Width: 400.0, Height: 300.0);
        var corner = new CadPointD(395, 295);
        CadDynamicInputMeasurement[] measurements =
        [
            new("Length", new(300, 295), corner),
            new("Angle", new(300, 295), corner, AngleReference: new(395, 295), AngleSweepDegrees: 0)
        ];
        var layout = Compute(corner, [("Length", 80), ("Angle", 80)], measurements, surface.Width, surface.Height);
        foreach (var (key, width) in new[] { ("Length", 80d), ("Angle", 80d) })
        {
            var box = BoxOf(layout, key, width);
            Assert.InRange(box.X, 0, surface.Width - width);
            Assert.InRange(box.Y, 0, surface.Height - BoxHeight);
        }
    }

    [Fact]
    public void CoordinatesFollowTheCursorAndShareTheBadgeRow()
    {
        var layout = CadDynamicInputLayout.Compute(new CadDynamicInputLayoutRequest(
            800, 600, BoxHeight, new CadPointD(100, 100), [new("X", 56), new("Y", 56)],
            null, new(100, 100), new(100, 100), new(100, 100), [], CursorBadgeVisible: true));
        Assert.Equal(new CadPointD(118, 116), layout.InlineBadgePosition);
        Assert.Equal(new CadPointD(152, 118), layout.Positions["X"]);
        Assert.Equal(new CadPointD(214, 118), layout.Positions["Y"]);
        Assert.Empty(layout.Guides);
    }

    [Fact]
    public void DegenerateAngleFallsBackToTheCursorGroup()
    {
        var vertex = new CadPointD(200, 200);
        var angle = new CadDynamicInputMeasurement("Angle", vertex, new(201, 200), AngleReference: new(300, 200), AngleSweepDegrees: 0);
        var layout = Compute(new(201, 200), [("Angle", 60)], [angle]);
        Assert.Equal(new CadPointD(219, 218), layout.Positions["Angle"]);
    }

    private static CadDynamicInputLayoutResult Compute(CadPointD pointer, (string Key, double Width)[] boxes,
        CadDynamicInputMeasurement[] measurements, double width = 800, double height = 600, CadPointD? anchor = null) =>
        CadDynamicInputLayout.Compute(new CadDynamicInputLayoutRequest(width, height, BoxHeight, pointer,
            boxes.Select(b => new CadDynamicInputBoxSpec(b.Key, b.Width)).ToArray(), anchor, pointer, pointer, pointer, measurements));

    private static Rect BoxOf(CadDynamicInputLayoutResult layout, string key, double width)
    {
        var position = layout.Positions[key];
        return new Rect(position.X, position.Y, width, BoxHeight);
    }

    private readonly record struct Rect(double X, double Y, double Width, double Height)
    {
        public double Bottom => Y + Height;
        public bool Contains(CadPointD point) => point.X >= X && point.X <= X + Width && point.Y >= Y && point.Y <= Bottom;
        public bool Intersects(Rect other) => X < other.X + other.Width && other.X < X + Width && Y < other.Bottom && other.Y < Bottom;
        public bool Crosses(CadPointD start, CadPointD end)
        {
            // Sample the segment densely; adequate for a test oracle independent of the implementation.
            for (var i = 0; i <= 400; i++)
            {
                var t = i / 400.0;
                var p = new CadPointD(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
                if (p.X > X && p.X < X + Width && p.Y > Y && p.Y < Bottom) return true;
            }
            return false;
        }
    }
}
