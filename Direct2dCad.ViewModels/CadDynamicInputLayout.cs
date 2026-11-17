using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels;

/// <summary>A dynamic-input box to place: its field key and measured width in canvas pixels.</summary>
public readonly record struct CadDynamicInputBoxSpec(string Key, double Width);

/// <summary>One straight guide stroke in canvas pixels. Angular guides are flattened arcs.</summary>
public readonly record struct CadDynamicInputGuide(CadPointD Start, CadPointD End);

public sealed record CadDynamicInputLayoutRequest(
    double SurfaceWidth,
    double SurfaceHeight,
    double BoxHeight,
    CadPointD Pointer,
    IReadOnlyList<CadDynamicInputBoxSpec> Boxes,
    CadPointD? Anchor,
    CadPointD Point,
    CadPointD XCorner,
    CadPointD YCorner,
    IReadOnlyList<CadDynamicInputMeasurement> Measurements,
    bool DrawAnchorLine = false,
    bool CursorBadgeVisible = false,
    CadPointD CursorBadgePosition = default,
    double CursorBadgeSize = 28);

/// <param name="Positions">Top-left corner of every box, keyed by field.</param>
/// <param name="InlineBadgePosition">Where the cursor tool badge joins the cursor-side group, if it does.</param>
public sealed record CadDynamicInputLayoutResult(
    IReadOnlyDictionary<string, CadPointD> Positions,
    IReadOnlyList<CadDynamicInputGuide> Guides,
    CadPointD? InlineBadgePosition);

/// <summary>
/// Shared placement of dynamic-input boxes and their construction guides for every desktop client.
/// Lengths use aligned dimensions whose side is chosen to stay clear of the pointer, the tool badge,
/// the anchor and boxes already placed; angles use an arc from their reference direction.
/// </summary>
public static class CadDynamicInputLayout
{
    private const double Margin = 4, Gap = 6, CursorOffset = 18, InlineBadgeWidth = 34;
    private const double LinearOffset = 26, StackStep = 24, ExtensionOvershoot = 6;
    private const double PointerClearance = 14, AnchorClearance = 10, MinimumAngularLeg = 6;
    private const double MinimumArcRadius = 30, MaximumArcRadius = 90, ArcStepDegrees = 4, RayOvershoot = 8;
    private const double LineCrossingPenalty = 400; // a box hiding the geometry being measured costs more than a small overlap
    private const int Levels = 3;

    public static CadDynamicInputLayoutResult Compute(CadDynamicInputLayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var layout = new Builder(request);
        return layout.Build();
    }

    private sealed class Builder(CadDynamicInputLayoutRequest request)
    {
        private readonly Dictionary<string, double> _widths = request.Boxes.ToDictionary(box => box.Key, box => box.Width);
        private readonly Dictionary<string, CadPointD> _positions = [];
        private readonly Dictionary<string, Box> _placed = [];
        private readonly List<Box> _occupied = [];
        private readonly List<CadDynamicInputGuide> _guides = [];
        private readonly List<(CadPointD Start, CadPointD End)> _legs = [];
        private double BoxHeight => request.BoxHeight;

        public CadDynamicInputLayoutResult Build()
        {
            var measurements = request.Measurements.Where(m => _widths.ContainsKey(m.Key) &&
                (!m.IsAngular || m.Start.DistanceTo(m.End) >= MinimumAngularLeg)).ToArray();
            var measured = measurements.Select(m => m.Key).ToHashSet();
            // The measured geometry itself (segments and angle legs) should stay visible.
            foreach (var m in measurements)
            {
                _legs.Add((m.Start, m.End));
                if (m.AngleReference is { } reference) _legs.Add((m.Start, reference));
            }
            var pointer = request.Pointer;
            _occupied.Add(Box.Around(pointer, PointerClearance));
            if (request.Anchor is { } anchorPoint) _occupied.Add(Box.Around(anchorPoint, AnchorClearance));

            // Point coordinates stay by the cursor; geometric parameters sit on their construction guides.
            var cursorKeys = request.Boxes.Select(b => b.Key)
                .Where(key => !measured.Contains(key) && key is not ("Width" or "Height")).ToArray();
            var inlineBadge = cursorKeys.Length > 0 && request.CursorBadgeVisible;
            var groupWidth = cursorKeys.Sum(key => _widths[key]) + Math.Max(0, cursorKeys.Length - 1) * Gap +
                (inlineBadge ? InlineBadgeWidth : 0);
            var desiredX = pointer.X + CursorOffset;
            var desiredY = pointer.Y + CursorOffset;
            if (desiredX + groupWidth > request.SurfaceWidth - Margin) desiredX = pointer.X - groupWidth - CursorOffset;
            if (desiredY + BoxHeight > request.SurfaceHeight - Margin) desiredY = pointer.Y - BoxHeight - Gap;
            var groupX = Clamp(desiredX, Margin, request.SurfaceWidth - groupWidth - Margin);
            var groupY = Clamp(desiredY, Margin + 2, request.SurfaceHeight - BoxHeight - Margin - 2);
            CadPointD? badge = null;
            if (inlineBadge) badge = new CadPointD(groupX, groupY - 2);
            else if (request.CursorBadgeVisible)
                _occupied.Add(new Box(request.CursorBadgePosition.X, request.CursorBadgePosition.Y, request.CursorBadgeSize, request.CursorBadgeSize));
            if (cursorKeys.Length > 0) _occupied.Add(new Box(groupX, groupY, groupWidth, BoxHeight));
            if (inlineBadge) groupX += InlineBadgeWidth;
            foreach (var box in request.Boxes) _positions[box.Key] = new CadPointD(groupX, groupY);
            foreach (var key in cursorKeys)
            {
                Commit(key, new Box(groupX, groupY, _widths[key], BoxHeight), occupy: false);
                groupX += _widths[key] + Gap;
            }

            if (request.Anchor is { } anchor)
            {
                if (_widths.ContainsKey("Width") && _widths.ContainsKey("Height") && !measured.Contains("Width"))
                    PlaceRectangle(anchor);
                else if (request.DrawAnchorLine && _widths.ContainsKey("Length"))
                    Line(anchor, request.Point);
            }
            foreach (var measurement in measurements)
            {
                if (measurement.IsAngular) PlaceAngular(measurement);
                else PlaceLinear(measurement.Key, measurement.Start, measurement.End, ScreenUpNormal(measurement.End - measurement.Start),
                    LinearOffset + measurement.StackIndex * StackStep);
            }
            SeparateRemainingOverlaps();
            return new CadDynamicInputLayoutResult(_positions, _guides, badge);
        }

        private void PlaceRectangle(CadPointD anchor)
        {
            var point = request.Point;
            var x = request.XCorner;
            var y = request.YCorner;
            var center = anchor + (point - anchor) * .5;
            var widthStart = anchor.Y + x.Y > y.Y + point.Y ? anchor : y;
            var widthEnd = widthStart == anchor ? x : point;
            var heightStart = anchor.X + y.X > x.X + point.X ? anchor : x;
            var heightEnd = heightStart == anchor ? y : point;
            CadVectorD Outward(CadPointD from, CadPointD to)
            {
                var edge = to - from;
                var normal = edge.Length > 1e-6 ? edge.Normalize().Perpendicular() : CadVectorD.UnitY;
                return normal.Dot(from + (to - from) * .5 - center) < 0 ? -normal : normal;
            }
            PlaceLinear("Width", widthStart, widthEnd, Outward(widthStart, widthEnd), LinearOffset + 2);
            PlaceLinear("Height", heightStart, heightEnd, Outward(heightStart, heightEnd), LinearOffset + 2);
        }

        private void PlaceLinear(string key, CadPointD start, CadPointD end, CadVectorD preferredNormal, double baseOffset)
        {
            var width = _widths[key];
            var middle = start + (end - start) * .5;
            (double Score, CadVectorD Normal, double Offset, Box Box)? best = null;
            for (var level = 0; level < Levels; level++)
            {
                foreach (var (normal, sidePenalty) in new[] { (preferredNormal, 0d), (-preferredNormal, .1) })
                {
                    var offset = baseOffset + level * StackStep;
                    var box = Box.Centered(middle + normal * offset, width, BoxHeight);
                    var score = Penalty(box) + level * .25 + sidePenalty;
                    if (best is null || score < best.Value.Score) best = (score, normal, offset, box);
                }
                if (best!.Value.Score < 1) break; // a clear spot on this level
            }
            var chosen = best!.Value;
            Line(start, start + chosen.Normal * (chosen.Offset + ExtensionOvershoot));
            Line(end, end + chosen.Normal * (chosen.Offset + ExtensionOvershoot));
            Line(start + chosen.Normal * chosen.Offset, end + chosen.Normal * chosen.Offset);
            Commit(key, chosen.Box);
        }

        private void PlaceAngular(CadDynamicInputMeasurement measurement)
        {
            var vertex = measurement.Start;
            var width = _widths[measurement.Key];
            var reference = measurement.AngleReference!.Value - vertex;
            var startAngle = reference.Length > 1e-9 ? Math.Atan2(reference.Y, reference.X) : 0;
            var sweep = measurement.AngleSweepDegrees * Math.PI / 180;
            var leg = vertex.DistanceTo(measurement.End);
            var baseRadius = Math.Max(12, Math.Min(leg, Math.Clamp(leg * .5, MinimumArcRadius, MaximumArcRadius)));
            var middle = Direction(startAngle + sweep / 2);
            var support = Math.Abs(middle.X) * width / 2 + Math.Abs(middle.Y) * BoxHeight / 2;
            (double Score, double Radius, Box Box)? best = null;
            void Consider(double radius, double distance, double extra)
            {
                var box = Box.Centered(vertex + middle * distance, width, BoxHeight);
                var score = Penalty(box) + extra;
                if (best is null || score < best.Value.Score) best = (score, radius, box);
            }
            for (var level = 0; level < Levels && (best is null || best.Value.Score >= 1); level++)
            {
                var radius = baseRadius + level * StackStep;
                Consider(radius, radius + support + 3, level * .25);
                if (level == 0 && radius - support - 3 > 8) Consider(radius, radius - support - 3, .15);
            }
            var chosen = best!.Value;
            Line(vertex, vertex + Direction(startAngle) * (chosen.Radius + RayOvershoot));
            Line(vertex, vertex + Direction(startAngle + sweep) * (chosen.Radius + RayOvershoot));
            var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(measurement.AngleSweepDegrees) / ArcStepDegrees));
            var previous = vertex + Direction(startAngle) * chosen.Radius;
            for (var i = 1; i <= steps; i++)
            {
                var next = vertex + Direction(startAngle + sweep * i / steps) * chosen.Radius;
                Line(previous, next);
                previous = next;
            }
            Commit(measurement.Key, chosen.Box);
        }

        private void SeparateRemainingOverlaps()
        {
            // Last resort, as before: stack a box below (or above) whatever it still overlaps.
            var settled = new List<Box>();
            foreach (var spec in request.Boxes)
            {
                if (!_placed.TryGetValue(spec.Key, out var box)) box = new Box(_positions[spec.Key].X, _positions[spec.Key].Y, spec.Width, BoxHeight);
                for (var attempt = 0; attempt < request.Boxes.Count * 2 && settled.Any(other => other.Intersects(box)); attempt++)
                {
                    var other = settled.First(other => other.Intersects(box));
                    var y = other.Bottom + Gap <= request.SurfaceHeight - BoxHeight - Margin ? other.Bottom + Gap : Math.Max(Margin, other.Y - BoxHeight - Gap);
                    box = box with { Y = y };
                }
                _positions[spec.Key] = new CadPointD(box.X, box.Y);
                settled.Add(box);
            }
        }

        private void Commit(string key, Box box, bool occupy = true)
        {
            box = box with
            {
                X = Clamp(box.X, Margin, request.SurfaceWidth - box.Width - Margin),
                Y = Clamp(box.Y, Margin, request.SurfaceHeight - box.Height - Margin)
            };
            _placed[key] = box;
            _positions[key] = new CadPointD(box.X, box.Y);
            if (occupy) _occupied.Add(box);
        }

        private double Penalty(Box box)
        {
            var overlap = _occupied.Sum(other => box.OverlapArea(other));
            var inside = box.OverlapArea(new Box(0, 0, request.SurfaceWidth, request.SurfaceHeight));
            var crossings = _legs.Count(leg => box.Crosses(leg.Start, leg.End));
            return overlap + (box.Width * box.Height - inside) * 2 + crossings * LineCrossingPenalty;
        }

        private void Line(CadPointD from, CadPointD to) => _guides.Add(new CadDynamicInputGuide(from, to));
    }

    private static CadVectorD ScreenUpNormal(CadVectorD direction)
    {
        var normal = direction.Length > 1e-6 ? direction.Normalize().Perpendicular() : CadVectorD.UnitY;
        return normal.Y > 0 ? -normal : normal;
    }

    private static CadVectorD Direction(double radians) => new(Math.Cos(radians), Math.Sin(radians));

    private static double Clamp(double value, double minimum, double maximum) => Math.Clamp(value, minimum, Math.Max(minimum, maximum));

    private readonly record struct Box(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;
        public static Box Centered(CadPointD center, double width, double height) => new(center.X - width / 2, center.Y - height / 2, width, height);
        public static Box Around(CadPointD point, double radius) => new(point.X - radius, point.Y - radius, radius * 2, radius * 2);
        public bool Intersects(Box other) => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
        public bool Contains(CadPointD point) => point.X > X && point.X < Right && point.Y > Y && point.Y < Bottom;

        /// <summary>Whether the segment passes through the box interior (Liang–Barsky clipping).</summary>
        public bool Crosses(CadPointD start, CadPointD end)
        {
            if (Contains(start) || Contains(end)) return true;
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            double enter = 0, exit = 1;
            foreach (var (p, q) in new[] { (-dx, start.X - X), (dx, Right - start.X), (-dy, start.Y - Y), (dy, Bottom - start.Y) })
            {
                if (Math.Abs(p) < 1e-12) { if (q < 0) return false; continue; }
                var r = q / p;
                if (p < 0) enter = Math.Max(enter, r); else exit = Math.Min(exit, r);
                if (enter > exit) return false;
            }
            return exit - enter > 1e-9;
        }

        public double OverlapArea(Box other) =>
            Math.Max(0, Math.Min(Right, other.Right) - Math.Max(X, other.X)) * Math.Max(0, Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y));
    }
}
