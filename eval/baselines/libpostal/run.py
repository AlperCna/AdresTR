"""libpostal baseline for the AdresTR benchmark.

Sends every `text` of an eval JSONL file (eval/SCHEMA.md) to a libpostal HTTP
parse service (pelias/libpostal-service: GET /parse?address=...), maps the
libpostal labels to the AdresTR label set and writes prediction JSONL
({"id": ..., "fields": {...}}).

Stdlib only; run with `python -I`. See README.md for the mapping rationale.

    python -I eval/baselines/libpostal/run.py \
        --input eval/challenge/challenge.jsonl --split test \
        --output eval/predictions/libpostal/challenge-test.jsonl \
        --raw eval/predictions/libpostal/raw/challenge-test.jsonl

`--from-raw FILE` re-applies the mapping to previously saved raw libpostal
outputs without a running container.
"""

from __future__ import annotations

import argparse
import concurrent.futures
import csv
import json
import os
import re
import sys
import time
import unicodedata
import urllib.error
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
DEFAULT_IL_CSV = os.path.join(REPO, "data", "staging", "il.csv")
DEFAULT_URL = "http://localhost:4400/parse"

# --------------------------------------------------------------------------
# Folding helpers (Turkish-aware, length preserving on the text side)
# --------------------------------------------------------------------------

_FOLD = str.maketrans({
    "İ": "i", "I": "i", "ı": "i", "Ş": "s", "ş": "s", "Ğ": "g", "ğ": "g",
    "Ü": "u", "ü": "u", "Ö": "o", "ö": "o", "Ç": "c", "ç": "c",
    "Â": "a", "â": "a", "Î": "i", "î": "i", "Û": "u", "û": "u",
})


def fold(s: str) -> str:
    """Lower-case ASCII-ish fold. Same length as the input for Turkish text."""
    out = []
    for ch in s.translate(_FOLD):
        low = ch.lower()
        out.append(low if len(low) == 1 else ch)
    return "".join(out)


def key(s: str) -> str:
    """Comparison key: folded, combining marks and non-alphanumerics removed."""
    s = unicodedata.normalize("NFD", fold(s))
    return "".join(c for c in s if c.isalnum())


def clean(s: str) -> str:
    """Collapse whitespace and trim punctuation at both ends."""
    s = re.sub(r"\s+", " ", s).strip()
    return s.strip(" .,;:-")


# --------------------------------------------------------------------------
# Surface recovery: libpostal returns lower-cased, re-tokenised values. We map
# them back to the original substring (case/diacritics as written) when the
# value's tokens can be found in order in the input text.
# --------------------------------------------------------------------------

def surface(value: str, text: str, ftext: str) -> str:
    v = fold(value.replace("̇", ""))
    toks = [t for t in re.split(r"\s+", v) if t]
    if not toks:
        return value
    pat = r"[\W_]*".join(re.escape(t) for t in toks)
    m = re.search(r"(?<!\w)" + pat + r"(?!\w)", ftext) or re.search(pat, ftext)
    if not m:
        return clean(value.replace("̇", ""))
    return clean(text[m.start():m.end()])


# --------------------------------------------------------------------------
# Type words (common written forms; no typo variants on purpose)
# --------------------------------------------------------------------------

def _forms(*words: str) -> set[str]:
    return {key(w) for w in words}


CSBM_TYPES: list[tuple[str, set[str]]] = [
    ("kume_evler", _forms("küme evler", "küme evleri", "kümeevler", "kümeevleri")),
    ("cadde", _forms("cadde", "caddesi", "cad", "cd", "cadd")),
    ("sokak", _forms("sokak", "sokağı", "sok", "sk", "sokagi")),
    ("bulvar", _forms("bulvar", "bulvarı", "bulv", "blv", "blvr", "bul")),
    ("meydan", _forms("meydan", "meydanı", "mey", "meyd")),
    ("cikmaz", _forms("çıkmaz", "çıkmazı", "çık", "çkmz")),
    ("yol", _forms("yol", "yolu")),
]
MAHALLE_TYPES = _forms("mahalle", "mahallesi", "mah", "mh", "mhl", "köyü", "köy", "mevkii", "mevki", "mevkisi")
ILCE_TYPES = _forms("ilçe", "ilçesi")
SITE_TYPES = _forms("sitesi", "site", "sit", "apartmanı", "apartman", "apt", "ap")
BLOK_TYPES = _forms("blok", "bloğu", "blk", "bl")

# a door number: digits, optional letter glued to it, optional "/x" or "-x" part where x is a
# number (+letter) or a single letter ("17/A", "22/1", "1-3"); "29/Konya" keeps only "29"
_L = "A-Za-zÇĞİÖŞÜçğıöşü"
_DOOR = rf"\d+(?:[{_L}](?![\w:]))?(?:\s*[/\-]\s*(?:\d+(?:[{_L}](?![\w:]))?|[{_L}](?![\w:])))?"
DOOR_AFTER_NO = re.compile(r"(?<!\w)(?:no|numara)\s*[:.]?\s*(" + _DOOR + ")", re.I)
DOOR_LEADING = re.compile(r"^\s*(" + _DOOR + r")(?![\w:])")
UNIT_AFTER_PREFIX = re.compile(r"(?<!\w)(?:daire|dai̇re|d|iç\s*kapı|ic\s*kapi)\s*[:.]?\s*(\d+[A-Za-z]?)", re.I)
LEVEL_AFTER_PREFIX = re.compile(r"(?<!\w)(?:kat|k)\s*[:.]?\s*(-?\d+)", re.I)
LEVEL_BEFORE_SUFFIX = re.compile(r"(-?\d+)\s*\.?\s*(?:kat|k)\b", re.I)
FIRST_NUMBER = re.compile(r"(?<!\w)(-?\d+)(?!\w)")


def words(s: str) -> list[str]:
    return [w for w in re.split(r"\s+", s.strip()) if w]


def split_road(value: str) -> tuple[str, str | None]:
    """'Moda Cad.' -> ('Moda', 'cadde').

    Splits at the LAST street type word (one or two words) that is not the first word; whatever
    libpostal appended after it ('... Blv. Güneş', '... Cad. No') is dropped. No type word -> whole value.
    """
    ws = words(value)
    for i in range(len(ws) - 1, 0, -1):
        for n in (2, 1):
            if i + n > len(ws):
                continue
            k = key("".join(ws[i:i + n]))
            for tur, forms in CSBM_TYPES:
                if k in forms:
                    return clean(" ".join(ws[:i])), tur
    return clean(value), None


def ends_with_type(value: str, forms: set[str]) -> bool:
    ws = words(value)
    return len(ws) > 1 and key(ws[-1]) in forms


def strip_trailing(value: str, forms: set[str]) -> str:
    ws = words(value)
    if len(ws) > 1 and key(ws[-1]) in forms:
        return clean(" ".join(ws[:-1]))
    return clean(value)


def strip_leading(value: str, forms: set[str]) -> str:
    ws = words(value)
    if len(ws) > 1 and key(ws[0]) in forms:
        return clean(" ".join(ws[1:]))
    return clean(value)


def door(value: str) -> str | None:
    m = DOOR_AFTER_NO.search(value) or DOOR_LEADING.search(value)
    return re.sub(r"\s+", "", m.group(1)) if m else None


def flat(value: str) -> str | None:
    m = UNIT_AFTER_PREFIX.search(value) or FIRST_NUMBER.search(value)
    return m.group(1) if m else None


def floor(value: str) -> str | None:
    k = key(value)
    if k.startswith("zemin"):
        return "0"
    if k.startswith("bodrum"):
        return "-1"
    m = LEVEL_AFTER_PREFIX.search(value) or LEVEL_BEFORE_SUFFIX.search(value) or FIRST_NUMBER.search(value)
    return m.group(1) if m else None


# --------------------------------------------------------------------------
# Mapping
# --------------------------------------------------------------------------

ADMIN_LABELS = ("state", "state_district", "city", "city_district")
# libpostal resources/boundaries/osm/tr.yaml: admin_level 4 (il) -> state,
# 6 (ilce) -> state_district (city_district inside Istanbul/Ankara),
# 8 -> city (Istanbul itself is relabelled city), 10 -> suburb.
ILCE_PRIORITY = ("state_district", "city_district", "city", "state")


def load_provinces(path: str) -> set[str]:
    with open(path, encoding="utf-8", newline="") as f:
        return {key(row["ad"]) for row in csv.DictReader(f)}


def map_components(comps: list[tuple[str, str]], text: str, provinces: set[str],
                   repair: bool = True) -> dict[str, str]:
    ftext = fold(text)
    occ: dict[str, list[str]] = {}
    for label, value in comps:
        if value.strip():
            occ.setdefault(label, []).append(surface(value, text, ftext))

    def first(label: str, fn=lambda v: v):
        """First occurrence of `label` for which fn() gives a value."""
        for v in occ.get(label, []):
            r = fn(v)
            if r:
                return r
        return None

    fields: dict[str, str] = {}

    # ---- il / ilce -------------------------------------------------------
    # admin labels: LAST occurrence (Turkish order is small -> large, the hierarchy comes last)
    adm = {lbl: occ[lbl][-1] for lbl in ADMIN_LABELS if lbl in occ}
    il_lbl = next((lbl for lbl in ADMIN_LABELS if lbl in adm and key(adm[lbl]) in provinces), None)
    if il_lbl is None and "state" in adm:
        il_lbl = "state"
    il = adm.get(il_lbl) if il_lbl else None

    ilce = None
    for lbl in ILCE_PRIORITY:
        if lbl == il_lbl or lbl not in adm:
            continue
        v = adm[lbl]
        if (il and key(v) == key(il)) or key(v) in provinces:
            continue
        ilce = v
        break

    if repair and il is None:
        # one admin value holding both, e.g. city = "kadıköy istanbul"
        for lbl in ADMIN_LABELS:
            v = adm.get(lbl)
            parts = [p for p in re.split(r"[\s/,\-]+", v or "") if p]
            if len(parts) < 2:
                continue
            if key(parts[-1]) in provinces:
                il, rest = parts[-1], " ".join(parts[:-1])
            elif key(parts[0]) in provinces:
                il, rest = parts[0], " ".join(parts[1:])
            else:
                continue
            if ilce is None or ilce == v:
                ilce = clean(rest)
            break

    # ---- house: site, or a mislabelled mahalle / ilce -------------------
    mahalle = first("suburb")
    site = None
    for h in occ.get("house", []):
        if repair and ends_with_type(h, MAHALLE_TYPES):
            mahalle = mahalle or h
        elif repair and ends_with_type(h, ILCE_TYPES):
            ilce = ilce or strip_trailing(h, ILCE_TYPES)
        elif site is None:
            site = h

    # ---- road: csbm, or "X Mah. Y Cad." --------------------------------
    road = None
    for r in occ.get("road", []):
        if repair:
            ws = words(r)
            idx = [i for i, w in enumerate(ws) if key(w) in MAHALLE_TYPES and i > 0]
            if idx:
                i = idx[-1]
                mahalle = mahalle or " ".join(ws[:i + 1])
                r = " ".join(ws[i + 1:])
        if r.strip() and len(key(r)) > 0:
            ad, tur = split_road(r)
            if ad or tur:
                road = (ad, tur)
                break

    if il:
        fields["il"] = clean(il)
    if ilce:
        fields["ilce"] = clean(ilce)
    if mahalle:
        m = strip_trailing(mahalle, MAHALLE_TYPES)
        if m:
            fields["mahalle"] = m
    if road:
        ad, tur = road
        if ad:
            fields["csbm_ad"] = ad
        if tur:
            fields["csbm_tur"] = tur

    # ---- numbers -------------------------------------------------------
    v = first("house_number", door)
    if v:
        fields["dis_kapi"] = v

    blok = None
    for lbl in ("entrance", "staircase"):
        blok = blok or first(lbl)
    daire = None
    for u in occ.get("unit", []):
        if any(key(w) in BLOK_TYPES for w in words(u)):  # "b blok" labelled unit
            blok = blok or u
        elif daire is None:
            daire = flat(u)
    if daire:
        fields["daire"] = daire
    if blok:
        b = strip_leading(strip_trailing(blok, BLOK_TYPES), BLOK_TYPES)
        if b:
            fields["blok"] = b

    v = first("level", floor)
    if v:
        fields["kat"] = v

    v = first("postcode", lambda s: (re.search(r"(?<!\d)\d{5}(?!\d)", s) or [None])[0])
    if v:
        fields["posta_kodu"] = v

    if site:
        s = strip_trailing(site, SITE_TYPES)
        if s:
            fields["site"] = s

    return fields


# --------------------------------------------------------------------------
# HTTP
# --------------------------------------------------------------------------

def normalise_response(data) -> list[tuple[str, str]]:
    """Accept [{"label","value"}] (pelias / wof server) or [[value,label]] (pypostal)."""
    out = []
    for item in data or []:
        if isinstance(item, dict):
            out.append((item["label"], item["value"]))
        else:
            value, label = item
            out.append((label, value))
    return out


def parse_remote(url: str, text: str, timeout: float, retries: int = 3) -> list[tuple[str, str]]:
    full = url + "?" + urllib.parse.urlencode({"address": text})
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(full, timeout=timeout) as r:
                return normalise_response(json.loads(r.read().decode("utf-8")))
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            if attempt == retries - 1:
                raise
            time.sleep(1 + attempt)
    return []


# --------------------------------------------------------------------------
# Main
# --------------------------------------------------------------------------

def read_jsonl(path: str):
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                yield json.loads(line)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--input", required=True, help="eval JSONL (eval/SCHEMA.md)")
    ap.add_argument("--output", required=True, help="prediction JSONL to write")
    ap.add_argument("--url", default=os.environ.get("LIBPOSTAL_URL", DEFAULT_URL))
    ap.add_argument("--raw", help="also write raw libpostal output per row to this JSONL")
    ap.add_argument("--from-raw", help="read raw libpostal output (from --raw) instead of calling the service")
    ap.add_argument("--split", choices=("dev", "test"), help="only rows whose `split` field has this value")
    ap.add_argument("--il-csv", default=DEFAULT_IL_CSV, help="81 province names (data/staging/il.csv)")
    ap.add_argument("--no-repair", action="store_true",
                    help="disable the two type-word/province repairs (see README)")
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--timeout", type=float, default=30.0)
    args = ap.parse_args(argv)

    provinces = load_provinces(args.il_csv)
    if len(provinces) != 81:
        print(f"warning: {len(provinces)} province names loaded from {args.il_csv}", file=sys.stderr)

    rows = [r for r in read_jsonl(args.input) if args.split is None or r.get("split") == args.split]

    if args.from_raw:
        raw_by_id = {r["id"]: normalise_response(r["libpostal"]) for r in read_jsonl(args.from_raw)}
        parsed = [raw_by_id.get(r["id"], []) for r in rows]
    else:
        try:
            parse_remote(args.url, "Moda Cad. No:1 Kadıköy İstanbul", args.timeout, retries=1)
        except (urllib.error.URLError, TimeoutError, ConnectionError) as e:
            print(f"error: libpostal service not reachable at {args.url} ({e}); "
                  "start the container (see README.md)", file=sys.stderr)
            return 2
        with concurrent.futures.ThreadPoolExecutor(max_workers=max(1, args.workers)) as ex:
            parsed = list(ex.map(lambda r: parse_remote(args.url, r["text"], args.timeout), rows))

    for path in (args.output, args.raw):
        if path:
            os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)

    with open(args.output, "w", encoding="utf-8", newline="\n") as out:
        for row, comps in zip(rows, parsed):
            fields = map_components(comps, row["text"], provinces, repair=not args.no_repair)
            out.write(json.dumps({"id": row["id"], "fields": fields}, ensure_ascii=False) + "\n")

    if args.raw and not args.from_raw:
        with open(args.raw, "w", encoding="utf-8", newline="\n") as out:
            for row, comps in zip(rows, parsed):
                rec = {"id": row["id"], "text": row["text"],
                       "libpostal": [{"label": l, "value": v} for l, v in comps]}
                out.write(json.dumps(rec, ensure_ascii=False) + "\n")

    print(f"{len(rows)} rows -> {args.output}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
