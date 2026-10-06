"""Read-only independent audit of --region-dxf-evidence fixtures. Requires ezdxf==1.4.3."""
import argparse
import hashlib
import json
import math
from pathlib import Path

import ezdxf
from ezdxf import path as dxf_path
from ezdxf.path import nesting
from ezdxf.math import bulge_to_arc
from ezdxf.math.triangulation import mapbox_earcut_2d

parser = argparse.ArgumentParser()
parser.add_argument("evidence_directory", type=Path)
directory = parser.parse_args().evidence_directory
source = json.loads((directory / "region-source.json").read_text(encoding="utf-8"))


def near(a, b, tolerance=1e-7):
    assert abs(a - b) <= tolerance, (a, b)


def loops(vertices, factor):
    edges = []
    for i, (x, y, bulge) in enumerate(vertices):
        end = vertices[(i + 1) % len(vertices)]
        a, b = (x * factor, y * factor), (end[0] * factor, end[1] * factor)
        if bulge == 0:
            edges.append(dict(line=True, start=a, end=b, center=(0, 0), radius=0, sweep=0))
        else:
            assert math.isfinite(bulge) and abs(bulge) <= 1 + 1e-12
            center, _, _, radius = bulge_to_arc(a, b, bulge)
            edges.append(dict(line=False, start=a, end=b, center=tuple(center), radius=radius, sweep=4 * math.atan(bulge)))
    return edges


def point(edge, t):
    a, b, c = edge["start"], edge["end"], edge["center"]
    if edge["line"]:
        return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
    angle = math.atan2(a[1] - c[1], a[0] - c[0]) + edge["sweep"] * t
    return (c[0] + edge["radius"] * math.cos(angle), c[1] + edge["radius"] * math.sin(angle))


def length(edges):
    return sum(math.dist(e["start"], e["end"]) if e["line"] else abs(e["sweep"]) * e["radius"] for e in edges)


def area(edges):
    origin = edges[0]["start"]
    result = 0
    for e in edges:
        a, b, c = [(p[0] - origin[0], p[1] - origin[1]) for p in (e["start"], e["end"], e["center"])]
        result += a[0] * b[1] - a[1] * b[0] if e["line"] else c[0] * (b[1] - a[1]) - c[1] * (b[0] - a[0]) + e["radius"] ** 2 * e["sweep"]
    return result / 2


def distance(edge, p):
    a, b, c = edge["start"], edge["end"], edge["center"]
    if edge["line"]:
        dx, dy = b[0] - a[0], b[1] - a[1]
        t = max(0, min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / (dx * dx + dy * dy)))
        return math.dist(p, point(edge, t))
    start = math.atan2(a[1] - c[1], a[0] - c[0])
    angle = math.atan2(p[1] - c[1], p[0] - c[0])
    travel = ((angle - start) * (1 if edge["sweep"] > 0 else -1)) % math.tau
    if travel <= abs(edge["sweep"]) + 1e-12:
        return abs(math.dist(p, c) - edge["radius"])
    return min(math.dist(p, a), math.dist(p, b))


def equivalent(expected, actual):
    near(length(expected), length(actual))
    near(area(expected), area(actual))
    # Both directions catch missing/extra pieces even when total length/area happen to match.
    for left, right in ((expected, actual), (actual, expected)):
        for edge in left:
            for i in range(17):
                p = point(edge, i / 16)
                assert min(distance(e, p) for e in right) < 1e-7, p
    near(sum(abs(e["sweep"]) for e in expected if not e["line"]), sum(abs(e["sweep"]) for e in actual if not e["line"]))


def analytic_path(edges):
    # Build polygon samples directly from parsed bulges. The general ezdxf path converter
    # approximates circular arcs by cubic Beziers and may retain nearly coincident closing
    # vertices, which are unsuitable for measuring analytic area through triangulation.
    vertices = [edges[0]["start"]]
    for edge in edges:
        count = 1 if edge["line"] else max(1, math.ceil(abs(edge["sweep"]) / (2 * math.acos(1 - min(.001 / edge["radius"], 1)))))
        vertices.extend(point(edge, i / count) for i in range(1, count + 1))
    assert math.dist(vertices[0], vertices[-1]) < 1e-7
    return dxf_path.from_vertices(vertices[:-1], close=True)


def filled_triangles(paths):
    # ezdxf.path.triangulate flattens all descendants into holes, including islands
    # inside holes. Use its explicit nesting tree and triangulate each material level.
    def material(polygon):
        exterior, *holes = polygon
        yield from mapbox_earcut_2d(exterior.flattening(.001), [h[0].flattening(.001) for h in holes])
        for hole in holes:
            for island in hole[1:]:
                yield from material(island)
    for polygon in nesting.make_polygon_structure(paths):
        yield from material(polygon)


rows = []
for fixture in source["fixtures"]:
    filename = directory / fixture["file"]
    document = ezdxf.readfile(filename)
    audit = document.audit()
    assert not audit.errors and not audit.fixes, (filename, audit.errors, audit.fixes)
    assert document.dxfversion == "AC1018" and document.units == fixture["unit_code"]
    factor = 25.4 if document.units == 1 else 1
    layout = document.blocks.get(fixture["block"]) if fixture["block"] else document.modelspace()
    boundaries = list(layout.query("LWPOLYLINE"))
    assert len(boundaries) == fixture["contour_count"]
    actual_contours = []
    for expected, polyline in zip(fixture["contours"], boundaries):
        assert polyline.closed
        actual = loops(list(polyline.get_points("xyb")), factor)
        equivalent(expected, actual)
        actual_contours.append(actual)
    near(fixture["length_mm"], sum(length(c) for c in actual_contours))
    hatches = list(layout.query("HATCH"))
    hatch_area = None
    if fixture["solid_fill"]:
        assert len(hatches) == 1
        hatch = hatches[0]
        assert hatch.dxf.solid_fill == 1 and hatch.dxf.pattern_name == "SOLID" and hatch.dxf.hatch_style == 0
        assert hatch.dxf.associative == 0 and hatch.dxf.true_color == 0x00FF00
        assert len(hatch.paths) == fixture["contour_count"]
        hatch_contours = []
        for boundary, expected in zip(hatch.paths, actual_contours):
            assert boundary.is_closed
            parsed = loops(boundary.vertices, factor)
            equivalent(expected, parsed)
            hatch_contours.append(parsed)
        # Independent renderer nesting/triangulation catches holes filled over and lost interior islands.
        triangles = list(filled_triangles(map(analytic_path, hatch_contours)))
        hatch_area = sum(abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) / 2 for a, b, c in triangles)
        assert abs(hatch_area - fixture["area_mm2"]) < fixture["area_mm2"] * 5e-4 + 1e-3, (filename, hatch_area, fixture["area_mm2"])
        assert layout[0].dxftype() == "HATCH"
        if fixture["file"] == "nested-solid-inch.dxf":
            assert hatch.dxf.transparency == 0x0200007F
            for p, expected_inside in (((80, -35), True), ((85, -35), False), ((95, -35), True), ((105, -35), False)):
                def contains_triangle(t):
                    def cross(a, b): return (b.x - a.x) * (p[1] - a.y) - (b.y - a.y) * (p[0] - a.x)
                    signs = [cross(t[i], t[(i + 1) % 3]) for i in range(3)]
                    return min(signs) >= -1e-12 or max(signs) <= 1e-12
                assert any(contains_triangle(t) for t in triangles) == expected_inside, p
    else:
        assert not hatches
    if fixture["block"]:
        insert = document.modelspace().query("INSERT")[0]
        assert insert.dxf.name == fixture["block"] and insert.dxf.xscale == 2 and insert.dxf.yscale == 1.5
        for boundary in boundaries:
            assert boundary.dxf.invisible == 1 and boundary.dxf.true_color == 0xFF0000
            assert boundary.dxf.lineweight == 50 and boundary.dxf.linetype == "DASHED" and boundary.dxf.layer == "region"
            assert boundary.dxf.owner == layout.block_record_handle
    rows.append(dict(file=fixture["file"], sha256=hashlib.sha256(filename.read_bytes()).hexdigest(),
                     errors=[], fixes=[], contours=len(boundaries), exact_boundary_geometry_matches=True,
                     solid_hatch=bool(hatches), hatch_triangulated_area_mm2=hatch_area))
report = dict(validator="ezdxf", validator_version=ezdxf.__version__, fixtures=rows,
              checks=["real Boolean command export", "exact signed arc and line boundaries", "closed contour count", "holes and nested islands in HATCH triangulation", "outline has no fill", "inch units", "block ownership and stroke appearance"],
              limitations="Independent parser and triangulation, not acceptance in an external CAD application. Triangulation samples parsed analytic arcs with 0.001 mm chord deviation; exact boundary comparisons use analytic formulas.")
(directory / "region-dxf-external-audit.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(report, ensure_ascii=False, indent=2))
