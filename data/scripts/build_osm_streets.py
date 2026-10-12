"""Extracts named streets per ilce from OpenStreetMap (ODbL) for the optional AdresTR.Streets package.

Setup / run (venv outside git):
    python -m venv data/raw/osm/.venv && data/raw/osm/.venv/Scripts/python -m pip install osmium shapely
    curl -L -o data/raw/osm/turkey-latest.osm.pbf https://download.geofabrik.de/europe/turkey-latest.osm.pbf
    data/raw/osm/.venv/Scripts/python data/scripts/build_osm_streets.py

Pass 1 builds il (admin_level=4, ISO3166-2) and ilce (admin_level=6) polygons from boundary relations and matches the ilce
to data/staging/ilce.csv by plate + folded name. Pass 2 assigns every named highway (first/middle/last node) and every
addr:street value to an ilce. Output: data/osm/streets.csv.gz (ilce_id,tur,ad,kanit), SOURCES.json.
"""
import csv
import gzip
import hashlib
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

import osmium
import shapely.wkb
from shapely import STRtree
from shapely.geometry import Point

sys.path.insert(0, str(Path(__file__).parent))
from common import fold  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
PBF = ROOT / "data/raw/osm/turkey-latest.osm.pbf"
OUT = ROOT / "data/osm"
HIGHWAYS = {"primary", "secondary", "tertiary", "unclassified", "residential", "living_street", "pedestrian", "trunk", "service"}
TYPES = [
    ("cadde", r"(caddesi|cadde|cad|cd)"), ("sokak", r"(sokağı|sokağ|sokak|sok|sk)"), ("bulvar", r"(bulvarı|bulvar|blv|bulv)"),
    ("yol", r"(yolu|yol)"), ("meydan", r"(meydanı|meydan)"), ("cikmaz", r"(çıkmazı|çıkmaz|cikmaz)"),
]


def read_csv(name):
    with open(ROOT / "data/staging" / name, encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f))


ilce_rows = read_csv("ilce.csv")
ilce_key = {(int(r["plaka"]), fold(r["ad"], strip_suffix=False)): int(r["ilce_id"]) for r in ilce_rows}
ilce_by_plaka = defaultdict(list)
for r in ilce_rows:
    ilce_by_plaka[int(r["plaka"])].append(r)
il_names = {fold(r["ad"], strip_suffix=False): int(r["plaka"]) for r in read_csv("il.csv")}


def wkb_polygon(area, factory):
    try:
        return shapely.wkb.loads(factory.create_multipolygon(area), hex=True)
    except Exception:
        return None


# ---- pass 1: boundaries
il_polys, ilce_polys = [], []
factory = osmium.geom.WKBFactory()
processor = osmium.FileProcessor(str(PBF)).with_areas(osmium.filter.TagFilter(("boundary", "administrative")))
for obj in processor:
    if not obj.is_area() or obj.from_way():
        continue
    level = obj.tags.get("admin_level")
    if level not in ("4", "6"):
        continue
    poly = wkb_polygon(obj, factory)
    if poly is None or poly.is_empty:
        continue
    name = obj.tags.get("name:tr") or obj.tags.get("name") or ""
    if level == "4":
        iso = obj.tags.get("ISO3166-2", "")
        plaka = int(iso[3:]) if iso.startswith("TR-") and iso[3:].isdigit() else il_names.get(fold(name, strip_suffix=False))
        if plaka:
            il_polys.append((plaka, poly))
    else:
        ilce_polys.append((name, poly))
print(f"boundaries: {len(il_polys)} il, {len(ilce_polys)} ilce polygons", flush=True)

il_tree = STRtree([p for _, p in il_polys])
polys, ids, unmatched = [], [], []
for name, poly in ilce_polys:
    point = poly.representative_point()
    plaka = next((il_polys[i][0] for i in il_tree.query(point, predicate="within")), None)
    ilce_id = ilce_key.get((plaka, fold(name, strip_suffix=False))) if plaka else None
    if ilce_id is None and plaka:
        # "Merkez"-style central districts: the OSM district named like the il, or "<Il> Merkez".
        candidates = [r for r in ilce_by_plaka[plaka] if fold(r["ad"], strip_suffix=False) == "merkez"]
        keys = {k for k, v in il_names.items() if v == plaka}
        if candidates and (fold(name, strip_suffix=False) in keys or fold(name, strip_suffix=False) in {k + " merkez" for k in keys}):
            ilce_id = int(candidates[0]["ilce_id"])
    if ilce_id is None:
        unmatched.append((plaka, name))
        continue
    polys.append(poly)
    ids.append(ilce_id)
matched = len(set(ids))
print(f"ilce matched: {matched}/{len(ilce_rows)}; unmatched polygons: {len(unmatched)} e.g. {unmatched[:12]}", flush=True)
tree = STRtree(polys)


def ilce_of(lon, lat):
    p = Point(lon, lat)
    for i in tree.query(p, predicate="intersects"):
        return ids[i]
    return None


def split_type(name):
    name = re.sub(r"\s+", " ", name).strip()
    for tur, pattern in TYPES:
        m = re.match(r"^(.+?)\s+" + pattern + r"\.?$", name, re.IGNORECASE)
        if m:
            return tur, m.group(1).strip()
    m = re.match(r"^(\d+(?:/\d+)?)\.?\s*(?:sokak|sk|sok)\.?$", name, re.IGNORECASE)
    return ("", name)


# ---- pass 2: streets
streets = defaultdict(lambda: [0, ""])  # (ilce, tur, folded) -> [count, display]


def add(ilce_id, raw):
    if ilce_id is None or not raw or len(raw) > 80:
        return
    tur, ad = split_type(raw)
    ad = re.sub(r"^(\d+)\.\s*$", r"\1", ad)
    key = (ilce_id, tur, fold(ad, strip_suffix=False))
    if len(key[2]) < 2:
        return
    entry = streets[key]
    entry[0] += 1
    entry[1] = entry[1] or ad


count = 0
processor = (osmium.FileProcessor(str(PBF), osmium.osm.NODE | osmium.osm.WAY)
             .with_locations()
             .with_filter(osmium.filter.KeyFilter("highway", "addr:street")))
for obj in processor:
    tags = obj.tags
    street = tags.get("addr:street")
    if obj.is_node():
        if street and obj.location.valid():
            add(ilce_of(obj.location.lon, obj.location.lat), street)
        continue
    nodes = [n.location for n in obj.nodes if n.location.valid()]
    if not nodes:
        continue
    points = {(l.lon, l.lat) for l in (nodes[0], nodes[len(nodes) // 2], nodes[-1])}
    ilceler = {ilce_of(lon, lat) for lon, lat in points}
    if street:
        for i in ilceler:
            add(i, street)
    name = tags.get("name:tr") or tags.get("name")
    if name and tags.get("highway") in HIGHWAYS:
        for i in ilceler:
            add(i, name)
    count += 1
    if count % 500000 == 0:
        print(f"{count} ways, {len(streets)} streets", flush=True)

OUT.mkdir(parents=True, exist_ok=True)
rows = sorted(((i, t, e[1], e[0]) for (i, t, _), e in streets.items()), key=lambda r: (r[0], r[1], fold(r[2], strip_suffix=False)))
with gzip.GzipFile(OUT / "streets.csv.gz", "wb", mtime=0) as gz:
    text = "ilce_id,tur,ad,kanit\n" + "".join(
        f'{i},{t},"{ad.replace(chr(34), chr(34) * 2)}",{n}\n' if ("," in ad or '"' in ad) else f"{i},{t},{ad},{n}\n" for i, t, ad, n in rows)
    gz.write(text.encode("utf-8"))
sha = hashlib.sha256(PBF.read_bytes()).hexdigest()
(OUT / "SOURCES.json").write_text(json.dumps({
    "source": "https://download.geofabrik.de/europe/turkey-latest.osm.pbf", "sha256": sha, "license": "ODbL-1.0",
    "attribution": "© OpenStreetMap contributors", "ilce_matched": matched, "ilce_total": len(ilce_rows),
    "streets": len(rows), "unmatched_polygons": len(unmatched),
}, indent=2) + "\n", encoding="utf-8")
print("wrote", OUT / "streets.csv.gz", len(rows), "streets;", (OUT / "streets.csv.gz").stat().st_size // 1024, "KiB")
