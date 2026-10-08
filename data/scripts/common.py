"""Ortak yardımcılar: kaynak yükleyiciler ve Türkçe katlama (folding).

Bu modül `python -I` ile çalışan scriptler tarafından
sys.path'e açıkça eklenerek içe aktarılır.
"""
from __future__ import annotations

import csv
import glob
import json
import os
import re
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]  # data/
RAW = ROOT / "raw"
DERIVED = RAW / "_derived"

# ---------------------------------------------------------------- folding
_TR_LOWER = str.maketrans({"I": "ı", "İ": "i"})
_ASCII = str.maketrans({"ç": "c", "ğ": "g", "ı": "i", "ö": "o", "ş": "s", "ü": "u",
                        "â": "a", "î": "i", "û": "u", "é": "e"})


def tr_lower(s: str) -> str:
    """Türkçe kurallarla küçük harf (I→ı, İ→i); U+0307 temizlenir."""
    return s.translate(_TR_LOWER).lower().replace("̇", "")


def tr_title(s: str) -> str:
    out = []
    for w in tr_lower(s).split(" "):
        if not w:
            out.append(w)
            continue
        f = w[0]
        f = "İ" if f == "i" else ("I" if f == "ı" else f.upper())
        out.append(f + w[1:])
    return " ".join(out)


# Sonek temizleme: "mah", "mah.", "mahallesi", "mh", "köyü", "(köyü)", "beldesi" ...
_SUFFIX_RE = re.compile(
    r"(?:\s|^)(?:mahallesi|mah\.?|mh\.?|koyu|\(koyu\)|\(koy\)|beldesi)\s*$"
)


def fold(s: str | None, strip_suffix: bool = True) -> str:
    """Türkçe ASCII katlama + küçük harf + sonek temizleme + boşluk/noktalama normalizasyonu."""
    if not s:
        return ""
    s = tr_lower(s).translate(_ASCII)
    s = s.replace("’", "'").replace("`", "'")
    s = re.sub(r"\s+", " ", s).strip()
    if strip_suffix:  # tek geçiş: "Yeni Mahalle Mah" -> "yeni mahalle" (ikinci kez soyulmaz)
        s = _SUFFIX_RE.sub("", s).strip()
    s = re.sub(r"[.\-'/]", " ", s)
    s = re.sub(r"\s+", " ", s).strip()
    return s


def fold_compact(s: str | None) -> str:
    """fold + tüm boşlukları kaldır (\"Altı Eylül\" == \"Altıeylül\")."""
    return fold(s).replace(" ", "")


# ---------------------------------------------------------------- loaders
def load_nvi():
    """melihozkara (NVİ 2026-10-06). Döner: iller, ilceler, mahalleler (dict listeleri)."""
    base = RAW / "melihozkara"
    iller = [json.loads(l) for l in open(base / "iller.jsonl", encoding="utf-8")]
    ilceler, mahalleler = [], []
    for f in sorted(glob.glob(str(base / "il-*" / "ilceler.jsonl"))):
        ilceler += [json.loads(l) for l in open(f, encoding="utf-8")]
    for f in sorted(glob.glob(str(base / "il-*" / "mahalleler.jsonl"))):
        mahalleler += [json.loads(l) for l in open(f, encoding="utf-8")]
    il_by_id = {i["kimlikNo"]: i for i in iller}
    ilce_by_id = {i["kimlikNo"]: i for i in ilceler}
    for m in mahalleler:
        m["_il"] = il_by_id[m["il_id"]]["adi"]
        m["_ilce"] = ilce_by_id[m["ilce_id"]]["adi"]
        # tip 0 (köy) kayıtlarında 'adi' null; isim koyAdi'nda
        m["_name"] = m["adi"] if m["adi"] is not None else m["koyAdi"]
    for i in ilceler:
        i["_il"] = il_by_id[i["il_id"]]["adi"]
    return iller, ilceler, mahalleler


def load_murat():
    """muratgozel/turkey-neighbourhoods (PTT 2024-03). Satır: (plaka, il, ilçe, mahalle, pk)."""
    p = RAW / "muratgozel" / "src" / "data" / "neighbourhoods.json"
    return [tuple(r) for r in json.load(open(p, encoding="utf-8"))]


def _php_records(path: Path):
    text = open(path, encoding="utf-8").read()
    recs = []
    for block in re.finditer(r"(?:array\(|\[)\s*((?:'[a-z_]+'\s*=>\s*(?:'(?:[^'\\]|\\.)*'|\d+)\s*,?\s*)+)(?:\)|\])", text):
        rec = {}
        for k, v in re.findall(r"'([a-z_]+)'\s*=>\s*('(?:[^'\\]|\\.)*'|\d+)", block.group(1)):
            rec[k] = v[1:-1].replace("\\'", "'") if v.startswith("'") else int(v)
        recs.append(rec)
    return recs


def load_epigra():
    """epigra/tr-geozones (PTT 2022-08). Satır: (il, ilçe, semt_bucak_belde, mahalle, pk)."""
    s = RAW / "epigra" / "src" / "Database" / "Seeders"
    cities = {r["id"]: r for r in _php_records(s / "GeozoneCitiesTableSeeder.php")}
    counties = {r["id"]: r for r in _php_records(s / "GeozoneCountiesTableSeeder.php")}
    districts = {r["id"]: r for r in _php_records(s / "GeozoneDistrictsTableSeeder.php")}
    out = []
    for n in _php_records(s / "GeozoneNeighbourhoodsTableSeeder.php"):
        d = districts[n["district_id"]]
        c = counties[d["county_id"]]
        out.append((cities[c["city_id"]]["name"], c["name"], d["name"], n["name"], n["post_code"]))
    return out, cities, counties, districts


def load_wd(name: str):
    p = RAW / "wikidata" / f"{name}.csv"
    with open(p, encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f))


def qid(uri: str) -> str:
    return uri.rsplit("/", 1)[-1] if uri else ""


def write_csv(path: Path, header, rows):
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(header)
        w.writerows(rows)
