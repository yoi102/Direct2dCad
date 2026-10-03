"""Independent DXF audit. Requires ezdxf==1.4.3; never rewrites inputs."""
import argparse
import hashlib
import json
from pathlib import Path
import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode

parser = argparse.ArgumentParser()
parser.add_argument("evidence_directory", type=Path)
args = parser.parse_args()
repo = Path(__file__).resolve().parents[2]
rows = []
for name in ("calibration.dxf", "external-roundtrip.dxf"):
    path = args.evidence_directory / name
    document = ezdxf.readfile(path)
    audit = document.audit()
    assert not audit.errors and not audit.fixes, (name, audit.errors, audit.fixes)
    rows.append(dict(file=name, sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                     version=document.dxfversion, entities=len(document.modelspace()),
                     units=document.units, errors=[], fixes=[]))
calibration = ezdxf.readfile(args.evidence_directory / "calibration.dxf")
assert calibration.units == 4
assert any(abs(line.dxf.start.distance(line.dxf.end) - 100) < 1e-8 for line in calibration.modelspace().query("LINE"))
assert any(line.dxf.invisible == 1 for line in calibration.modelspace().query("LINE"))
assert "100 mm 中文" in [decode_dxf_unicode(text.dxf.text) for text in calibration.modelspace().query("TEXT")]
assert next(layer for layer in calibration.layers if decode_dxf_unicode(layer.dxf.name)=="轮廓").dxf.true_color == 0xFF0000
insert = calibration.modelspace().query("INSERT")[0]
assert insert.dxf.name == "part" and insert.dxf.xscale == 2 and insert.dxf.yscale == 1.5
assert len(calibration.blocks.get("part")) == 1

def geometry(entity):
    d = entity.dxf
    def point(p): return tuple(round(v, 7) for v in p)
    if entity.dxftype() == "LINE": return ("LINE", point(d.start), point(d.end))
    if entity.dxftype() == "ARC": return ("ARC", point(d.center), round(d.radius, 7), round(d.start_angle % 360, 7), round(d.end_angle % 360, 7))
    if entity.dxftype() == "CIRCLE": return ("CIRCLE", point(d.center), round(d.radius, 7))
    raise AssertionError("External sample changed its declared geometry subset")
source_path = repo / "docs/samples/dxf/ezdxf-closed-loop-arcs.dxf"
source = ezdxf.readfile(source_path)
roundtrip = ezdxf.readfile(args.evidence_directory / "external-roundtrip.dxf")
assert sorted(map(geometry, source.modelspace())) == sorted(map(geometry, roundtrip.modelspace()))
report = dict(validator="ezdxf", validator_version=ezdxf.__version__, rows=rows,
              calibration_checks=["100 mm line", "hidden entity", "Unicode text", "layer true color", "block and nonuniform insert"],
              external_geometry_matches=True, external_source_sha256=hashlib.sha256(source_path.read_bytes()).hexdigest())
(args.evidence_directory / "dxf-external-audit.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(report, ensure_ascii=False, indent=2))
