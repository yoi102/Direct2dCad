"""Independent read-only audit of elliptical-region fixtures; requires ezdxf 1.4.3."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import ezdxf


def point(edge, t):
    if edge["IsLine"]:
        return tuple(edge["Start"][k] + (edge["End"][k] - edge["Start"][k]) * t for k in ("X", "Y"))
    angle = edge["StartAngle"] + edge["Sweep"] * t
    x, y = edge["RadiusX"] * math.cos(angle), edge["RadiusY"] * math.sin(angle)
    c, s = math.cos(edge["Rotation"]), math.sin(edge["Rotation"])
    return edge["Center"]["X"] + x * c - y * s, edge["Center"]["Y"] + x * s + y * c


def close(a, b, tolerance=1e-7):
    return math.dist(tuple(a)[:2], tuple(b)[:2]) <= tolerance


def conic(edge):
    """Return the independently decoded CCW parameter curve C + U cos(t) + V sin(t)."""
    construction = edge.construction_tool()
    if edge.EDGE_TYPE == "EllipseEdge":
        u = tuple(edge.major_axis)
        v = (-u[1] * edge.ratio, u[0] * edge.ratio)
        start, span = construction.start_param, construction.param_span
    else:
        u, v = (edge.radius, 0), (0, edge.radius)
        start, span = math.radians(edge.start_angle), math.radians(construction.angle_span)
    return tuple(edge.center), u, v, start, span


def signed_area(edges):
    # Green's theorem on exact lines and conic arcs; no tessellation for area.
    area = 0.0
    origin = tuple(edges[0].real_start_point)
    for edge in edges:
        a = tuple(edge.real_start_point)
        b = tuple(edge.real_end_point)
        if edge.EDGE_TYPE == "LineEdge":
            area += (a[0] - origin[0]) * (b[1] - origin[1]) - (a[1] - origin[1]) * (b[0] - origin[0])
        else:
            center, u, v, _, span = conic(edge)
            area += ((center[0] - origin[0]) * (b[1] - a[1])
                     - (center[1] - origin[1]) * (b[0] - a[0])
                     + (u[0] * v[1] - u[1] * v[0]) * span * (1 if edge.ccw else -1))
    return area / 2


def contains(edges, point):
    """Exact horizontal ray intersections with independently parsed line/conic equations."""
    px, py = point
    crossings = 0
    for edge in edges:
        if edge.EDGE_TYPE == "LineEdge":
            a, b = edge.real_start_point, edge.real_end_point
            if (a.y > py) != (b.y > py) and a.x + (py - a.y) * (b.x - a.x) / (b.y - a.y) > px:
                crossings += 1
            continue
        center, u, v, start, span = conic(edge)
        amplitude = math.hypot(u[1], v[1])
        level = (py - center[1]) / amplitude
        if abs(level) >= 1:
            continue  # tangent or outside
        phase = math.atan2(u[1], v[1])
        alpha = math.asin(level)
        for root in (alpha - phase, math.pi - alpha - phase):
            for turn in range(math.floor((start - root) / math.tau), math.ceil((start + span - root) / math.tau) + 1):
                angle = root + turn * math.tau
                derivative = -u[1] * math.sin(angle) + v[1] * math.cos(angle)
                if math.isclose(angle, start, rel_tol=0, abs_tol=1e-12):
                    included = derivative > 0
                elif math.isclose(angle, start + span, rel_tol=0, abs_tol=1e-12):
                    included = derivative < 0
                else:
                    included = start < angle < start + span
                if included and center[0] + u[0] * math.cos(angle) + v[0] * math.sin(angle) > px:
                    crossings += 1
    return crossings % 2 == 1


def source_signed_area(edges):
    area = 0.0
    origin = point(edges[0], 0)
    for edge in edges:
        a, b = point(edge, 0), point(edge, 1)
        if edge["IsLine"]:
            area += (a[0] - origin[0]) * (b[1] - origin[1]) - (a[1] - origin[1]) * (b[0] - origin[0])
        else:
            c = edge["Center"]
            area += ((c["X"] - origin[0]) * (b[1] - a[1]) - (c["Y"] - origin[1]) * (b[0] - a[0])
                     + edge["RadiusX"] * edge["RadiusY"] * edge["Sweep"])
    return area / 2


def audit(directory):
    results = []
    for filename in sorted(directory.glob("*.dxf")):
        source = json.loads(filename.with_suffix(".json").read_text(encoding="utf-8-sig"))
        drawing = ezdxf.readfile(filename)
        errors = drawing.audit()
        assert not errors.errors and not errors.fixes, (filename.name, errors.errors, errors.fixes)
        model = drawing.modelspace()
        hatch, = model.query("HATCH")
        assert len(hatch.paths) == len(source["contours"])
        assert len(model.query("ELLIPSE")) > 0
        assert len(model.query("LWPOLYLINE")) == 0
        assert hatch.dxf.hatch_style == 0, (filename.name, "nested islands require normal even-odd hatch style")
        samples = 0
        loop_areas = []
        for boundary, expected in zip(hatch.paths, source["contours"]):
            assert boundary.edges
            assert all(close(edge.real_end_point, boundary.edges[(index + 1) % len(boundary.edges)].real_start_point)
                       for index, edge in enumerate(boundary.edges)), (filename.name, "open HATCH loop")
            emitted = iter(boundary.edges)
            for edge in expected:
                if edge["IsLine"]:
                    actual = next(emitted)
                    assert actual.EDGE_TYPE == "LineEdge"
                    assert close(actual.real_start_point, point(edge, 0)), (filename.name, "line start")
                    assert close(actual.real_end_point, point(edge, 1)), (filename.name, "line end")
                    samples += 2
                    continue
                # A source curve may be emitted as multiple exact arcs. Derive their
                # parameter coverage from the independent reader, not exporter rules;
                # require gap-free, ordered, full coverage of the original edge.
                covered = 0.0
                while covered < 1 - 1e-12:
                    actual = next(emitted)
                    assert actual.EDGE_TYPE == ("EllipseEdge" if edge["IsEllipse"] else "ArcEdge")
                    assert actual.ccw == (edge["Sweep"] > 0), (filename.name, "direction")
                    construction = actual.construction_tool()
                    span = construction.param_span if edge["IsEllipse"] else math.radians(construction.angle_span)
                    assert span > 1e-12, (filename.name, "empty curve", actual)
                    fraction = span / abs(edge["Sweep"])
                    end = covered + fraction
                    assert end <= 1 + 1e-12, (filename.name, "excess coverage", end)
                    assert close(actual.real_start_point, point(edge, covered)), (filename.name, "start", covered)
                    assert close(actual.real_end_point, point(edge, end)), (filename.name, "end", end)
                    # ezdxf construction curves always run counterclockwise. Reverse
                    # their traversal for a clockwise HATCH edge.
                    for t in (0, .13, .31, .5, .73, .91, 1):
                        param = 1 - t if not actual.ccw else t
                        if edge["IsEllipse"]:
                            angle = construction.start_param + span * param
                            p = next(construction.vertices([angle]))
                        else:
                            angle = math.radians(actual.start_angle) + span * param
                            p = (actual.center.x + actual.radius * math.cos(angle), actual.center.y + actual.radius * math.sin(angle))
                        wanted = point(edge, covered + fraction * t)
                        assert close(p, wanted), (filename.name, "edge", t, p, wanted)
                        samples += 1
                    covered = end
                assert abs(covered - 1) <= 1e-12, (filename.name, "missing coverage", covered)
            assert next(emitted, None) is None, (filename.name, "extra HATCH edges")
            actual_area = signed_area(boundary.edges)
            expected_area = source_signed_area(expected)
            assert math.isclose(actual_area, expected_area, rel_tol=1e-10, abs_tol=1e-7), (filename.name, "signed loop area", actual_area, expected_area)
            loop_areas.append(actual_area)
        depths = [sum(contains(other.edges, tuple(boundary.edges[0].real_start_point))
                      for other in hatch.paths if other is not boundary) for boundary in hatch.paths]
        material_area = sum(abs(area) * (-1 if depth % 2 else 1) for area, depth in zip(loop_areas, depths))
        assert math.isclose(material_area, source["Area"], rel_tol=1e-10, abs_tol=1e-7), (filename.name, "material area", material_area, source["Area"])
        # The independently parsed ELLIPSE records must also have points lying on
        # their corresponding exact source edges, allowing major-axis swapping.
        expected_ellipses = [e for c in source["contours"] for e in c if e["IsEllipse"]]
        assert len(model.query("ELLIPSE")) == len(expected_ellipses)
        for actual, edge in zip(model.query("ELLIPSE"), expected_ellipses):
            conic = actual.construction_tool()
            for t in (0, .21, .5, .84, 1):
                param = t if edge["Sweep"] > 0 else 1 - t
                p = next(conic.vertices([conic.start_param + conic.param_span * param]))
                assert close(p, point(edge, t)), (filename.name, "ELLIPSE", t, p, point(edge, t))
                samples += 1
        results.append(dict(file=filename.name, sha256=hashlib.sha256(filename.read_bytes()).hexdigest(),
                            source_sha256=hashlib.sha256(filename.with_suffix(".json").read_bytes()).hexdigest(),
                            hatch_loops=len(hatch.paths), hatch_edges=sum(len(p.edges) for p in hatch.paths),
                            loop_signed_areas=loop_areas, nesting_depths=depths,
                            material_area=material_area, source_area=source["Area"],
                            checked_points=samples, errors=0, fixes=0))
    assert results, "No fixture DXF files found."
    return dict(validator="ezdxf", version=ezdxf.__version__, results=results)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    arguments = parser.parse_args()
    print(json.dumps(audit(arguments.directory), ensure_ascii=False, indent=2))
