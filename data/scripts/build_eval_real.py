"""Build the REAL evaluation set (eval/real/{dev,test}.jsonl) from openly licensed public-institution addresses.

    python -I data/scripts/build_eval_real.py              # fetch (cached) + build + validate
    python -I data/scripts/build_eval_real.py --refresh    # re-download every source file
    python -I data/scripts/build_eval_real.py --offline    # never touch the network (cache must exist)
    python -I data/scripts/build_eval_real.py --validate-only
    python -I data/scripts/build_eval_real.py --inspect 100   # print a seeded random sample for manual review

Inputs  : data/raw/eval/<source>/ (git-ignored; MANIFEST.json = url, sha256, bytes, download date, license)
          data/staging/{il,ilce,birim,alias}.csv (gazetteer ids)
Outputs : eval/real/dev.jsonl, eval/real/test.jsonl, eval/real/SOURCES.md, eval/real/LICENSE.md
Contract: eval/SCHEMA.md

Privacy: only address / administrative columns are emitted. Facility names, phone columns and the muhtar name
column are never written anywhere outside data/raw. Person-named health categories are excluded, and rows whose
address text contains a mobile phone number (possibly a person's) are dropped.

Gold policy (eval/SCHEMA.md "Two questions, two kinds of gold"; full text in SOURCES.md):
  * gold.fields = what the TEXT says: il / ilce / mahalle get the official name only if written, else null
    (absent when it cannot be decided automatically). The source columns are used to normalize what is written.
  * gold.il/ilce/birim = what the TEXT determines via the gazetteer (synthetic-generator rule); the publisher's
    true location is kept in `note` ("true location: birim …").
  * street / door / null fields only from a strict full-string parse; otherwise the keys are absent.
  * spans only when the whole text was parsed and every labeled substring matches its gold value after folding.
"""
from __future__ import annotations

import argparse
import collections
import csv
import datetime as dt
import hashlib
import io
import json
import random
import re
import sys
import time
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import common  # noqa: E402

REPO = common.ROOT.parent
RAW_EVAL = common.RAW / "eval"
STAGING = common.ROOT / "staging"
OUT = REPO / "eval" / "real"
UA = "AdresTR-data/0.1 (+https://github.com/AlperCna/AdresTR)"
CRAWL_DELAY = {"data.ibb.gov.tr": 10, "acikveri.bizizmir.com": 10}  # robots.txt Crawl-Delay
SEED = "adrestr-eval-real-v1"
DEV_EVERY = 5  # every 5th selected entity (ordered by ilçe, hash) goes to dev -> 20 %

IBB_LICENSE = "IBB-Acik-Veri"
IZM_LICENSE = "CC-BY-4.0"
IBB_LICENSE_URL = "https://data.ibb.gov.tr/license"
IZM_LICENSE_URL = "https://acikveri.bizizmir.com/tr/license"
ATTRIBUTION_DEFAULT = ("Atıf 4.0 Uluslararası (CC BY 4.0) kapsamında lisanslanan kamu sektörü bilgilerini "
                       "içerir.")

# quota = rows in dev+test (multiples of DEV_EVERY so that dev is exactly 20 %)
SOURCES = {
    "ibb-saglik": {
        "title": "İBB Açık Veri — İstanbul Sağlık Kurum ve Kuruluşları",
        "publisher": "İstanbul Büyükşehir Belediyesi",
        "dataset": "https://data.ibb.gov.tr/dataset/istanbul-saglik-kurum-ve-kuruluslari-verisi",
        "files": [("saglik-tesisleri.xlsx",
                   "https://data.ibb.gov.tr/dataset/bd3b9489-c7d5-4ff3-897c-8667f57c70bb/resource/"
                   "f2154883-68e3-41dc-b2be-a6c2eb721c9e/download/saglik-tesisleri.xlsx")],
        "license": IBB_LICENSE, "license_url": IBB_LICENSE_URL, "quota": 520, "templated": False,
    },
    "ibb-muhtarlik": {
        "title": "İBB Açık Veri — Muhtarlık Adres Bilgileri",
        "publisher": "İstanbul Büyükşehir Belediyesi",
        "dataset": "https://data.ibb.gov.tr/dataset/muhtarlik-adres-bilgileri",
        "files": [("muhtarlik.geojson",
                   "https://data.ibb.gov.tr/dataset/muhtarlik-adres-bilgileri/resource/"
                   "71f75529-7fae-4a85-b05f-664c62eda422/geojson_download")],
        "license": IBB_LICENSE, "license_url": IBB_LICENSE_URL, "quota": 180, "templated": False,
    },
    "ibb-pazar-tpl": {
        "title": "İBB Açık Veri — İstanbul İli Semt Pazarları (2025)",
        "publisher": "İstanbul Büyükşehir Belediyesi",
        "dataset": "https://data.ibb.gov.tr/dataset/istanbul-ili-semt-pazarlari",
        "files": [("semt-pazarlari-2025.xlsx",
                   "https://data.ibb.gov.tr/dataset/3cf7a2de-8013-4728-bc65-dd813af87796/resource/"
                   "78d6e0eb-9012-43c1-9eb1-cd30f743f3a8/download/istanbul-ili-balkc-olan-semt-pazarlar_2025.xlsx")],
        "license": IBB_LICENSE, "license_url": IBB_LICENSE_URL, "quota": 80, "templated": True,
    },
    "izmir-eczane": {
        "title": "İzmir BB Açık Veri — Nöbetçi Eczaneler ve Eczane Listesi",
        "publisher": "İzmir Büyükşehir Belediyesi",
        "dataset": "https://acikveri.bizizmir.com/dataset/nobetci-eczaneler-ve-eczane-listesi",
        "files": [("eczane-listesi.csv", "https://openfiles.izmir.bel.tr/111324/docs/eczane-listesi.csv")],
        "license": IZM_LICENSE, "license_url": IZM_LICENSE_URL, "quota": 560, "templated": False,
    },
    "izmir-saglik-tpl": {
        "title": "İzmir BB Açık Veri — Sağlık Kurumları (CBS web servisi)",
        "publisher": "İzmir Büyükşehir Belediyesi",
        "dataset": "https://acikveri.bizizmir.com/dataset/saglik-kurumlari",
        "files": [(f"{ep}.json", f"https://openapi.izmir.bel.tr/api/ibb/cbs/{ep}") for ep in (
            "ailesagligimerkezleri", "hastaneler", "toplumsagligimerkezleri", "agizvedissagligimerkezleri",
            "anacocuksagligimerkezleri", "dalmerkezleri", "acilyardimistasyonu", "veremsavasdispanserleri",
            "kanmerkezleri")],
        "license": IZM_LICENSE, "license_url": IZM_LICENSE_URL, "quota": 80, "templated": True,
    },
    "izmir-muhtarlik-tpl": {
        "title": "İzmir BB Açık Veri — Muhtarlıklar",
        "publisher": "İzmir Büyükşehir Belediyesi",
        "dataset": "https://acikveri.bizizmir.com/dataset/muhtarliklar",
        "files": [("izbb-muhtarliklar.csv",
                   "https://acikveri.bizizmir.com/dataset/e1616f3c-51c0-4176-ba40-d5d8647c094e/resource/"
                   "7d0b7f55-9ec2-4e35-91f8-0fd0aceefa18/download/izbb-muhtarliklar.csv")],
        "license": IZM_LICENSE, "license_url": IZM_LICENSE_URL, "quota": 80, "templated": True,
    },
}

# İBB "Alt Kategori" values kept. Everything else is excluded, in particular the categories whose facility
# name is (or usually is) a natural person: Doktor/Muayenehane, Psikologlar, Diyetisyen, Diş Hekimi, Eczane
# (named after the pharmacist), Veteriner, Gözlükçü/Optik, and the uncurated "Sağlık Diğer".
IBB_KEEP_CATEGORIES = {
    "Aile Sağlığı Merkezi", "Devlet Hastanesi", "Şehir Hastanesi", "Eğitim Araştırma Hastanesi",
    "Üniversite Hastanesi", "Özel Hastane", "Tıp Merkezi Özel", "Poliklinik Özel", "Ağız Diş Sağlığı Merkezi",
    "Özel Ağız ve Diş Sağlığı Merkezleri", "Laboratuvar Özel", "Belediye Sağlık Merkezi", "Semt Poliklinikleri",
    "Toplum Sağlığı Merkezi", "Verem Savaş Dispanseri", "Kızılay/Kan Merkezi", "Acil Yardım İstasyonu",
    "Diyaliz Merkezi", "Diyaliz Merkezi Özel", "Görüntüleme Merkezi Özel", "Fizik Tedavi ve Rehabilitasyon Merkezi",
    "Fizik Tedavi ve Rehabilitasyon Merkezi Özel", "Göz Merkezi Özel", "Kadın Doğum ve Çocuk Hastanesi",
    "Tüp Bebek Merkezi", "Evde Bakım Merkezleri", "Yaşlı Bakım Evi/Huzurevi",
    "Rehabilitasyon ve Aile Danışma Merkezi", "Özel Tanı Tedavi Merkezleri", "Sağlık Kabini Özel",
    "Psikoteknik Değerlendirme Merkezi", "İşitme Cihazı Satış ve Uygulama Merkezi",
    "Protez Ortez Yapım ve Uygulama Merkezi Özel", "Ecza Deposu", "Medikal", "Diş Laboratuvarı",
    "Belediye Hizmet Binası",
}
# Even in kept categories, drop a row whose facility name carries a personal title.
PERSON_TITLE_RX = re.compile(
    r"(?<![a-z])(dr|op|uzm|prof|doc|dt|dyt|psk|vet|ecz|fzt|hekim|hekimi|muayenehane(si)?|eczanesi)(?![a-z])")

# İzmir eczane ILCE_ID = NVİ ilçe kimlikNo (verified against the NVİ ilçe list, 2026-10-08).
IZMIR_NVI_ILCE = {
    1128: "Aliağa", 2006: "Balçova", 1178: "Bayındır", 2056: "Bayraklı", 1181: "Bergama", 1776: "Beydağ",
    1203: "Bornova", 1780: "Buca", 1251: "Çeşme", 2007: "Çiğli", 1280: "Dikili", 1334: "Foça", 2009: "Gaziemir",
    2018: "Güzelbahçe", 2057: "Karabağlar", 1432: "Karaburun", 1448: "Karşıyaka", 1461: "Kemalpaşa",
    1467: "Kınık", 1477: "Kiraz", 1819: "Konak", 1826: "Menderes", 1521: "Menemen", 2013: "Narlıdere",
    1563: "Ödemiş", 1611: "Seferihisar", 1612: "Selçuk", 1677: "Tire", 1682: "Torbalı", 1703: "Urla",
}

LABELS = ["il", "ilce", "mahalle", "semt", "csbm_tur", "csbm_ad", "site", "blok", "dis_kapi", "kat", "daire",
          "posta_kodu", "tarif", "diger"]
FIELD_ORDER = LABELS[:-1]
NAME_LABELS = {"il", "ilce", "mahalle", "semt", "csbm_ad", "site", "tarif"}
SCHEMA_TAGS = {"semt", "historic-name", "ambiguous-name", "merkez", "numbered-street", "slash-door", "glued",
               "broken-i", "ascii", "typo", "missing-ilce", "missing-il", "reordered", "phone", "landmark",
               "site-blok", "osb", "kume-evler", "koy", "postal-code", "abbreviation", "uppercase",
               "duplicate-token", "d-k-ambiguity"}
LICENSES = {IBB_LICENSE, IZM_LICENSE}

# street type words (folded, no trailing dot) -> csbm_tur enum
TYPE_MAP = {
    "cad": "cadde", "cd": "cadde", "cadde": "cadde", "caddesi": "cadde",
    "sk": "sokak", "sok": "sokak", "sokak": "sokak", "sokagi": "sokak",
    "blv": "bulvar", "bulv": "bulvar", "bulvar": "bulvar", "bulvari": "bulvar",
    "meydani": "meydan", "meyd": "meydan", "cikmazi": "cikmaz", "yolu": "yol",
}
TYPE_ALT = "|".join(sorted(TYPE_MAP, key=len, reverse=True))


# ============================================================== text helpers
_CF = {"I": "i", "İ": "i", "ı": "i", "Ç": "c", "ç": "c", "Ğ": "g", "ğ": "g", "Ö": "o", "ö": "o", "Ş": "s",
       "ş": "s", "Ü": "u", "ü": "u", "Â": "a", "â": "a", "Î": "i", "î": "i", "Û": "u", "û": "u", "É": "e",
       "é": "e", "’": "'", "‘": "'", "`": "'"}
TR_LETTERS = set("çğıöşüÇĞİÖŞÜ")
TR_NON_I = set("çğöşüÇĞÖŞÜ")


def cfold(s: str) -> str:
    """Length-preserving Turkish fold (lower case, ASCII) so regex positions map 1:1 onto the original text."""
    out = []
    for ch in s:
        c = _CF.get(ch)
        if c is None:
            low = ch.lower()
            c = low if len(low) == 1 else ch
        out.append(c)
    return "".join(out)


def key(s: str | None) -> str:
    """Mirror of AdresTR Gazetteer.Key: fold, drop spaces and . - ' /."""
    return re.sub(r"[\s.\-'/]+", "", cfold(s or ""))


def compact(s: str) -> str:
    return re.sub(r"\s+", "", cfold(s))


def tr_upper(s: str) -> str:
    return s.replace("i", "İ").replace("ı", "I").upper()


def u16(text: str, i: int) -> int:
    return i + sum(1 for c in text[:i] if ord(c) > 0xFFFF)


def from_u16(text: str, j: int) -> int:
    n = 0
    for i, c in enumerate(text):
        if n >= j:
            return i
        n += 2 if ord(c) > 0xFFFF else 1
    return len(text)


def h(*parts) -> str:
    return hashlib.sha1("|".join([SEED, *map(str, parts)]).encode("utf-8")).hexdigest()


def edit1(a: str, b: str) -> bool:
    """True if Levenshtein(a, b) == 1."""
    if a == b or abs(len(a) - len(b)) > 1:
        return False
    if len(a) > len(b):
        a, b = b, a
    i = 0
    while i < len(a) and a[i] == b[i]:
        i += 1
    return a[i:] == b[i + 1:] or (len(a) == len(b) and a[i + 1:] == b[i + 1:])


# ============================================================== fetch
def sha256(p: Path) -> str:
    hh = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            hh.update(chunk)
    return hh.hexdigest()


_last_hit: dict[str, float] = {}


def _download(url: str, dest: Path) -> None:
    host = urllib.parse.urlsplit(url).hostname or ""
    wait = _last_hit.get(host, 0) + CRAWL_DELAY.get(host, 1) - time.time()
    if wait > 0:
        time.sleep(wait)
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=120) as r:
        data = r.read()
    _last_hit[host] = time.time()
    tmp = dest.with_suffix(dest.suffix + ".part")
    tmp.write_bytes(data)
    tmp.replace(dest)


def fetch(refresh: bool, offline: bool) -> dict:
    """Download every source file (if missing or --refresh) and keep data/raw/eval/<source>/MANIFEST.json."""
    manifests = {}
    for skey, src in SOURCES.items():
        d = RAW_EVAL / skey
        d.mkdir(parents=True, exist_ok=True)
        mpath = d / "MANIFEST.json"
        man = json.loads(mpath.read_text(encoding="utf-8")) if mpath.exists() else {"files": {}}
        man.update({"source": skey, "dataset": src["dataset"], "license": src["license"],
                    "license_url": src["license_url"]})
        for name, url in src["files"]:
            p = d / name
            entry = man["files"].get(name)
            if refresh or not p.exists() or entry is None:
                if offline:
                    raise SystemExit(f"--offline but {p} (or its manifest entry) is missing")
                print(f"  downloading {skey}/{name}", file=sys.stderr)
                _download(url, p)
                entry = {"downloaded": dt.date.today().isoformat()}
            entry["url"] = url
            entry["sha256"] = sha256(p)
            entry["bytes"] = p.stat().st_size
            man["files"][name] = entry
        mpath.write_text(json.dumps(man, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        manifests[skey] = man
    return manifests


# ============================================================== readers
_X_ESC = re.compile(r"_x([0-9A-Fa-f]{4})_")
_NS = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
_RNS = "{http://schemas.openxmlformats.org/officeDocument/2006/relationships}"


def read_xlsx(path: Path, sheet: int = 0) -> list[list[str | None]]:
    """Minimal stdlib .xlsx reader (shared/inline strings + numbers; OOXML _xHHHH_ escapes decoded)."""
    z = zipfile.ZipFile(path)
    ss = []
    if "xl/sharedStrings.xml" in z.namelist():
        for si in ET.fromstring(z.read("xl/sharedStrings.xml")).findall(f"{_NS}si"):
            ss.append("".join(t.text or "" for t in si.iter(f"{_NS}t")))
    wb = ET.fromstring(z.read("xl/workbook.xml"))
    rels = ET.fromstring(z.read("xl/_rels/workbook.xml.rels"))
    rid = wb.find(f"{_NS}sheets")[sheet].get(f"{_RNS}id")
    target = next(r.get("Target") for r in rels if r.get("Id") == rid).lstrip("/")
    if not target.startswith("xl/"):
        target = "xl/" + target
    rows = []
    for row in ET.fromstring(z.read(target)).iter(f"{_NS}row"):
        vals = {}
        for c in row.findall(f"{_NS}c"):
            t, v = c.get("t"), c.find(f"{_NS}v")
            if t == "s":
                val = ss[int(v.text)]
            elif t == "inlineStr":
                val = "".join(x.text or "" for x in c.iter(f"{_NS}t"))
            else:
                val = v.text if v is not None else None
            if isinstance(val, str):
                val = _X_ESC.sub(lambda m: chr(int(m.group(1), 16)), val)
            col = 0
            for ch in re.match(r"[A-Z]+", c.get("r")).group(0):
                col = col * 26 + ord(ch) - 64
            vals[col - 1] = val
        if vals:
            rows.append([vals.get(i) for i in range(max(vals) + 1)])
    return rows


def read_table(rows: list[list]) -> list[dict]:
    header = [str(x).strip() if x is not None else "" for x in rows[0]]
    return [{header[i]: (r[i] if i < len(r) else None) for i in range(len(header))} for r in rows[1:]]


# ============================================================== gazetteer
class Gaz:
    def __init__(self):
        def rd(name):
            with open(STAGING / name, encoding="utf-8", newline="") as f:
                return list(csv.DictReader(f))
        self.il = {int(r["plaka"]): r for r in rd("il.csv")}
        self.ilce = {int(r["ilce_id"]): r for r in rd("ilce.csv")}
        self.birim = {int(r["birim_id"]): r for r in rd("birim.csv")}
        self.ilce_by_key = collections.defaultdict(list)
        for i, r in self.ilce.items():
            self.ilce_by_key[(int(r["plaka"]), key(r["ad"]))].append(i)
        self.birim_by_ilce = collections.defaultdict(lambda: collections.defaultdict(list))
        self.name_count = collections.Counter()
        self.semt_by_ilce = collections.defaultdict(set)
        for b, r in self.birim.items():
            self.birim_by_ilce[int(r["ilce_id"])][key(r["ad"])].append(b)
            self.name_count[key(r["ad"])] += 1
            if r["semt"]:
                self.semt_by_ilce[int(r["ilce_id"])].add(key(r["semt"]))
        self.units_by_key = collections.defaultdict(list)  # national: key(official name) -> [birim]
        for bid, r in self.birim.items():
            self.units_by_key[key(r["ad"])].append(bid)
        self.ilce_by_name_key = collections.defaultdict(list)  # national: key(ilçe name) -> [ilce_id]
        for i, r in self.ilce.items():
            self.ilce_by_name_key[key(r["ad"])].append(i)
        self.alias_units = collections.defaultdict(set)  # national: key(semt/historic alias) -> {birim}
        self.alias = collections.defaultdict(lambda: collections.defaultdict(list))  # ilce -> key -> [(b, tur)]
        for r in rd("alias.csv"):
            if r["hedef"] == "birim":
                b = int(r["hedef_id"])
                ilce = int(self.birim[b]["ilce_id"])
                self.alias[ilce][key(r["alias"])].append((b, r["tur"]))
                self.alias_units[key(r["alias"])].add(b)
                if r["tur"] == "semt":
                    self.semt_by_ilce[ilce].add(key(r["alias"]))

    def ilce_id(self, plaka: int, name: str) -> int | None:
        ids = self.ilce_by_key.get((plaka, key(name)), [])
        return ids[0] if len(ids) == 1 else None

    def resolve_birim(self, ilce_id: int, name: str, fuzzy: bool = False):
        """Return (birim_id | None, how, candidates). how ∈ exact, alias-<tur>, abbrev, fuzzy, ambiguous, none."""
        k = key(re.sub(r"(?i)\s+(mahallesi|mah\.?|mh\.?)\s*$", "", name.strip()))
        units = self.birim_by_ilce[ilce_id]
        if not k:
            return None, "none", []
        hits = units.get(k, [])
        if len(hits) == 1:
            return hits[0], "exact", hits
        if len(hits) > 1:
            return None, "ambiguous", hits
        al = {b for b, _ in self.alias[ilce_id].get(k, [])}
        if len(al) == 1:
            b = al.pop()
            turs = {t for bb, t in self.alias[ilce_id][k] if bb == b}
            return b, "alias-" + sorted(turs)[0], [b]
        if len(al) > 1:
            return None, "ambiguous", sorted(al)
        # "K.Bakkalköy" -> Küçükbakkalköy, "B." -> Büyük
        m = re.match(r"^([kb])\.?\s*(\S.*)$", cfold(name.strip()))
        if m:
            k2 = key(("kucuk" if m.group(1) == "k" else "buyuk") + m.group(2))
            hits = units.get(k2, [])
            if len(hits) == 1:
                return hits[0], "abbrev", hits
        if fuzzy and len(k) >= 6:
            near = [b for kk, bs in units.items() if edit1(k, kk) for b in bs]
            if len(near) == 1:
                return near[0], "fuzzy", near
        return None, "none", []


# ============================================================== parsing
DOOR_RX = (r"\d+(?:\s?[a-z](?![a-z])(?!\s*blok))?(?:\s*[/-]\s*[0-9]+[a-z]?(?![a-z0-9]))*"
           r"(?:\s*[/-]\s*[a-z](?![a-z0-9]))?")
STREET_RX = re.compile(rf"^(?P<ad>\S.*?)\s*(?<![a-z0-9])(?P<tur>{TYPE_ALT})(?![a-z])\.?$")
BAD_AD_RX = re.compile(r"(?<![a-z])(mahallesi|mah|mh|no|nolu|tel)(?![a-z])|[(),;:]|[a-z]-\d")
STRUCT_RX = re.compile(rf"(?<![a-z])({TYPE_ALT}|no|mah|mh|mahallesi)(?![a-z])")
MAH_HEAD_RX = re.compile(r"^\s*(?P<mah>\S.*?)\s*(?<![a-z])(?:mahallesi|mah|mh)(?![a-z])\.?")


def door_segments(t: str, s: int, e: int) -> list:
    """Split the door text t[s:e] per eval/SCHEMA.md: `17/5` -> dis_kapi 17 + daire 5, `3 /2C` -> 3 + 2C,
    `15/1-B` -> 15 + 1-B; a letter-only part after the slash stays in the door (`17/A`), ranges stay (`1 -3`)."""
    raw = t[s:e]
    k = raw.find("/")
    if k >= 0:
        right = raw[k + 1:].strip()
        if right and right[0].isdigit():
            left = raw[:k]
            ls, le = s + len(left) - len(left.lstrip()), s + len(left.rstrip())
            r0 = s + k + 1
            rs = r0 + (len(raw[k + 1:]) - len(raw[k + 1:].lstrip()))
            re_ = r0 + len(raw[k + 1:].rstrip())
            return [(ls, le, "dis_kapi", re.sub(r"\s+", "", t[ls:le])),
                    (rs, re_, "daire", re.sub(r"\s+", "", t[rs:re_]))]
    return [(s, e, "dis_kapi", re.sub(r"\s+", "", raw))]


def parse_street_door(t: str, f: str, a: int, b: int):
    """Parse original[a:b] (folded f) as `<street> <type>[ No:<door>]` or `No:<door>` or ''. Returns segment
    list or None. Segment = (start, end, label, value)."""
    seg = f[a:b]
    if not seg.strip():
        return []
    m = re.search(rf"(?<![a-z])no\s*[:.]?\s*(?P<no>{DOOR_RX})\s*$", seg)
    door = None
    street_end = b
    if m:
        ds, de = a + m.start("no"), a + m.end("no")
        door = door_segments(t, ds, de)
        street_end = a + m.start()
    elif re.search(r"(?<![a-z])no(?![a-z])", seg):
        return None
    s_seg = f[a:street_end]
    lead = len(s_seg) - len(s_seg.lstrip())
    s_seg = s_seg.strip()
    out = []
    if s_seg:
        sm = STREET_RX.match(s_seg)
        if not sm or BAD_AD_RX.search(sm.group("ad")):
            return None
        o = a + lead
        ad_s, ad_e = o + sm.start("ad"), o + sm.end("ad")
        tur_s, tur_e = o + sm.start("tur"), o + sm.end("tur")
        if t[tur_e:tur_e + 1] == ".":
            tur_e += 1
        out.append((ad_s, ad_e, "csbm_ad", re.sub(r"\s+", " ", t[ad_s:ad_e]).strip()))
        out.append((tur_s, tur_e, "csbm_tur", TYPE_MAP[sm.group("tur")]))
    if door:
        out += door
    return out


ECZ_RX = re.compile(
    r"^\s*(?:(?P<mah>\S.*?)\s*(?<![a-z])(?:mahallesi|mah|mh)(?![a-z])\.?\s*)?"
    rf"(?P<ad>\S.*?)\s*(?<![a-z0-9])(?P<tur>{TYPE_ALT})(?![a-z])\.?\s*"
    rf"(?:(?<![a-z])no(?![a-z])\s*[:.]?\s*(?P<no>{DOOR_RX}))?"
    r"(?P<tail>.*?)\s*$")


def find_unique(f: str, value: str, lo: int = 0, hi: int | None = None):
    """Unique position of `value` in folded text f[lo:hi] by Key (separators . - ' / and spaces ignored)."""
    k = key(value)
    if not k:
        return None
    pat = r"[\s.\-'/]*".join(re.escape(c) for c in k)
    hits = [(m.start(), m.end()) for m in re.finditer(rf"(?<![a-z0-9])(?:{pat})(?![a-z0-9])", f[lo:hi])]
    if len(hits) != 1:
        return None
    return lo + hits[0][0], lo + hits[0][1]


# ============================================================== tagging
PHONE_RX = re.compile(r"(?<!\d)(?:\+?90[\s-]?)?\(?0?\s?([2-5]\d{2})\)?[\s.-]?\d{3}[\s.-]?\d{2}[\s.-]?\d{2}(?!\d)"
                      r"|(?i:tel\s*[:.]?\s*\d{7})")
LANDMARK_RX = re.compile(r"(?<![a-z])(karsisi|karsi|krs|yani|yaninda|arkasi|ustu|bitisigi|girisi|icinde|ici|duragi|"
                         r"civari|onu|sirasi|altinda|alti)(?![a-z])")
ABBR_RX = re.compile(r"(?<![a-z])(mah|mh|cad|cd|sk|sok|blv|bulv|bul|apt|krs|sit|hast|ecz|sb)(?![a-z])")
GLUED_RX = re.compile(r"(?<![a-z])(mah|mh|cad|cd|sk|sok|blv|bulv|no)\.(?=[a-z0-9])|(?<![a-z])no\d|"
                      r"[a-z]{3}(mahallesi|sitesi|caddesi|sokak)(?![a-z])")
SITE_RX = re.compile(r"(?<![a-z])(sitesi|sit|blok|blk|apt|apartmani|is merkezi|ishani|plaza)(?![a-z])")
NUMSTREET_RX = re.compile(r"(?<![\d/])\d+(?:/\d+)?\.?\s*(?:sk|sok|sokak|sokagi|cad|cd|cadde|caddesi)(?![a-z])")
MOBILE_AREA = re.compile(r"^5\d\d$")


def tag_row(row: dict, f: str, segs: list | None, ctx: dict) -> set:
    t = row["text"]
    fields = row["gold"]["fields"]
    tags = set(ctx.get("tags", ()))
    letters = [c for c in t if c.isalpha()]
    if len(letters) >= 4 and all(c == tr_upper(c) for c in letters):
        tags.add("uppercase")
    if ABBR_RX.search(f):
        tags.add("abbreviation")
    if GLUED_RX.search(f):
        tags.add("glued")
    if SITE_RX.search(f):
        tags.add("site-blok")
    if NUMSTREET_RX.search(f) or (fields.get("csbm_ad") and re.match(r"\d", fields["csbm_ad"])):
        tags.add("numbered-street")
    if (fields.get("dis_kapi") and "/" in fields["dis_kapi"]) or re.search(r"(?<![a-z])no\s*[:.]?\s*\d+\s*/", f):
        tags.add("slash-door")
    if LANDMARK_RX.search(f):
        tags.add("landmark")
    if PHONE_RX.search(t):
        tags.add("phone")
    if len(re.findall(r"(?<![a-z])no(?![a-z])", f)) >= 2 or re.search(r"(?<![a-z])(\w{3,})\s+\1(?![a-z])", f):
        tags.add("duplicate-token")
    if re.search(rf"(?<![a-z])({TYPE_ALT})(?![a-z]).*(?<![a-z])(mah|mh|mahallesi)(?![a-z])", f):
        tags.add("reordered")
    mah = fields.get("mahalle")
    if (mah and "merkez" in key(mah)) or re.search(r"(?<![a-z])merkez(?![a-z])", f):
        tags.add("merkez")
    if mah and ctx.get("name_count", 0) > 1:
        tags.add("ambiguous-name")
    tur = ctx.get("birim_tur")
    if tur == "koy":
        tags.add("koy")
    elif tur == "osb":
        tags.add("osb")
    elif tur == "kume_evler":
        tags.add("kume-evler")
    if fields.get("posta_kodu"):
        tags.add("postal-code")
    # ascii / broken-i on aligned admin names
    for (s, e, lab, val) in (segs or []):
        if lab in ("il", "ilce", "mahalle") and fields.get(lab):
            written, gold = t[s:e], fields[lab]
            if key(written) != key(gold) or tr_upper(written) == tr_upper(gold):
                continue
            wf = re.sub(r"[\s.\-'/]", "", tr_upper(written))
            gf = re.sub(r"[\s.\-'/]", "", tr_upper(gold))
            if len(wf) == len(gf):
                for wc, gc in zip(wf, gf):
                    if wc != gc:
                        tags.add("broken-i" if {wc, gc} <= {"I", "İ"} else "ascii")
    # whole-text check; ı/İ are excluded because upper-case "YALI" is the correct spelling of "Yalı"
    if not TR_NON_I & set(t):
        for lab in ("ilce", "mahalle"):
            v = fields.get(lab)
            if v and TR_NON_I & set(v) and find_unique(f, v):
                tags.add("ascii")
    return tags


# ============================================================== row builders
def new_row(source: str, text: str, plaka: int, ilce_id: int | None, gaz: Gaz) -> dict:
    fields = {"il": gaz.il[plaka]["ad"]}
    gold = {"il": plaka}
    if ilce_id:
        fields["ilce"] = gaz.ilce[ilce_id]["ad"]
        gold["ilce"] = ilce_id
    gold["fields"] = fields
    return {"source": source, "license": SOURCES[source]["license"], "text": text, "gold": gold}


def set_mahalle(row: dict, ilce_id: int, name: str, gaz: Gaz, ctx: dict, fuzzy: bool = False,
                require: bool = False) -> bool:
    """Resolve a mahalle name within the ilçe and fill fields.mahalle / gold.birim / tags."""
    b, how, cands = gaz.resolve_birim(ilce_id, name, fuzzy=fuzzy)
    fields, notes, tags = row["gold"]["fields"], ctx.setdefault("notes", []), ctx.setdefault("tags", set())
    if b is not None:
        fields["mahalle"] = gaz.birim[b]["ad"]
        row["gold"]["birim"] = b
        ctx["birim_tur"] = gaz.birim[b]["tur"]
        ctx["name_count"] = gaz.name_count[key(gaz.birim[b]["ad"])]
        if how == "alias-tarihsel":
            tags.add("historic-name")
            notes.append(f"mahalle written with its former name; official: {gaz.birim[b]['ad']}")
        elif how == "alias-semt":
            tags.add("semt")
            notes.append(f"source mahalle value is a semt alias of {gaz.birim[b]['ad']}")
        elif how == "abbrev":
            tags.add("abbreviation")
        elif how in ("fuzzy", "alias-yazim"):
            tags.add("typo")
            notes.append(f"mahalle spelled differently from the official name {gaz.birim[b]['ad']}")
        return True
    if require:
        return False
    if how == "ambiguous":
        tags.add("ambiguous-name")
        notes.append("mahalle name matches several units in the ilçe; gold.birim not annotated")
        fields["mahalle"] = common.tr_title(re.sub(r"\s+", " ", name.strip()))
        ctx["name_count"] = 2
        return True
    return False


def finish_full(row: dict, segs: list, slash_daire_unknown: bool = False) -> None:
    """Whole text parsed: components that do not occur are annotated as null."""
    fields = row["gold"]["fields"]
    for lab in ("semt", "csbm_tur", "csbm_ad", "site", "blok", "dis_kapi", "kat", "daire", "posta_kodu", "tarif"):
        if lab not in fields:
            fields[lab] = None


def attach_spans(row: dict, segs: list) -> bool:
    """Validate every segment against its gold value; set row['spans'] only if all agree."""
    t, fields = row["text"], row["gold"]["fields"]
    seen = set()
    for (s, e, lab, val) in segs:
        g = fields.get(lab)
        if g is None or not (0 <= s < e <= len(t)):
            return False
        w = t[s:e]
        if lab in NAME_LABELS:
            ok = key(w) == key(g) and w == w.strip()
        elif lab == "csbm_tur":
            ok = TYPE_MAP.get(cfold(w).rstrip(".")) == g
        elif lab in ("dis_kapi", "blok", "daire"):
            ok = compact(w) == compact(g) and w == w.strip()
        else:
            ok = w.strip() == g
        if not ok or lab in seen:
            return False
        seen.add(lab)
    # street/door values only ever come from the parse, so they must be spanned (il/ilçe may be unwritten;
    # the parsers add a segment for every admin name they consumed)
    for lab in ("csbm_ad", "csbm_tur", "dis_kapi", "daire"):
        if fields.get(lab) is not None and lab not in seen:
            return False
    ordered = sorted(segs)
    for a, b in zip(ordered, ordered[1:]):
        if a[1] > b[0]:
            return False
    row["spans"] = [{"start": u16(t, s), "end": u16(t, e), "label": lab} for (s, e, lab, _) in ordered]
    return True


def tail_admin(t: str, f: str, a: int, ilce_name: str | None, il_name: str):
    """Parse f[a:] as `[ilçe][ -/][il]`. Returns (segments, ok)."""
    rest = f[a:]
    if not rest.strip():
        return [], True
    segs = []
    pos = a
    m_il = re.search(rf"[\s/\-,]*(?<![a-z]){re.escape(cfold(il_name))}(?![a-z])\s*$", rest)
    end = len(f)
    if m_il:
        il_s = a + m_il.start() + len(m_il.group(0)) - len(m_il.group(0).lstrip(" /-,"))
        il_e = a + m_il.start() + len(m_il.group(0).rstrip())
        segs.append((il_s, il_e, "il", il_name))
        end = a + m_il.start()
    mid = f[pos:end]
    if mid.strip(" /-,"):
        if not ilce_name:
            return segs, False
        k = mid.strip(" /-,")
        if key(k) != key(ilce_name):
            return segs, False
        s = pos + mid.index(k)
        segs.append((s, s + len(k), "ilce", ilce_name))
    return segs, True


# --- İBB sağlık / muhtarlık -----------------------------------------------------------------------------
def build_ibb_text_rows(source: str, recs: list[dict], gaz: Gaz, stats: collections.Counter) -> list[dict]:
    rows = []
    for r in recs:
        text, ilce_col, mah_col, name = r["text"], r["ilce"], r["mahalle"], r["name"]
        if not text or not text.strip() or text.strip() == "<Null>" or re.search(r"[\x00-\x1f]", text):
            stats["drop: empty or control characters"] += 1
            continue
        ilce_id = gaz.ilce_id(34, ilce_col or "")
        if not ilce_id:
            stats["drop: ilçe not in gazetteer"] += 1
            continue
        tail = f"{mah_col}/{ilce_col}"
        if not text.rstrip().endswith(tail):
            stats["drop: ADRES tail does not equal Mahalle/İlçe columns"] += 1
            continue
        if PHONE_RX.search(text) and any(MOBILE_AREA.match(m.group(1) or "") for m in PHONE_RX.finditer(text)):
            stats["drop: mobile phone in text"] += 1
            continue
        row = new_row(source, text, 34, ilce_id, gaz)
        ctx: dict = {"tags": {"missing-il"}}
        if not set_mahalle(row, ilce_id, mah_col, gaz, ctx, fuzzy=True):
            row["gold"]["fields"]["mahalle"] = common.tr_title(mah_col)
            ctx["notes"].append("mahalle not found in the gazetteer; gold.birim not annotated")
            stats["kept: mahalle unresolved"] += 1
        f = cfold(text)
        tail_s = len(text.rstrip()) - len(tail)
        segs_tail = [(tail_s, tail_s + len(mah_col), "mahalle", mah_col),
                     (tail_s + len(mah_col) + 1, tail_s + len(tail), "ilce", ilce_col)]
        sd = parse_street_door(text, f, 0, tail_s)
        segs = None
        if sd is not None:
            segs = sd + segs_tail
            for (s, e, lab, val) in sd:
                row["gold"]["fields"][lab] = val
            finish_full(row, segs)
            stats["parsed: full"] += 1
        else:
            stats["parsed: admin only"] += 1
        if segs is None or not attach_spans(row, segs):
            row.pop("spans", None)
        ist = re.search(r"(?<![a-z])istanbul(?![a-z])", f)
        ctx["w"] = {"il": "no" if not ist or sd is not None else "unknown", "ilce": "yes", "mahalle": "yes"}
        ctx["w_mah"] = mah_col
        ctx["w_type"] = None  # İBB tail "MAHALLE/İLÇE" has no type word
        row["_entity"] = [("name", source, key(name), ilce_id)]
        row["_ctx"] = ctx
        row["_segs"] = segs if "spans" in row else segs_tail
        rows.append(row)
    return rows


def load_ibb_saglik(gaz: Gaz, stats) -> list[dict]:
    recs = read_table(read_xlsx(RAW_EVAL / "ibb-saglik" / "saglik-tesisleri.xlsx"))
    stats["raw rows"] = len(recs)
    keep = []
    for r in recs:
        cat = (r.get("Alt Kategori") or "").strip()
        if cat not in IBB_KEEP_CATEGORIES:
            stats[f"drop: category excluded"] += 1
            continue
        if PERSON_TITLE_RX.search(cfold(r.get("Sağlık Tesisi Adı") or "")):
            stats["drop: personal title in facility name"] += 1
            continue
        keep.append({"text": r.get("ADRES"), "ilce": (r.get("İlçe Adı") or "").strip(),
                     "mahalle": (r.get("Mahalle Adı") or "").strip(), "name": r.get("Sağlık Tesisi Adı") or ""})
    return build_ibb_text_rows("ibb-saglik", keep, gaz, stats)


def load_ibb_muhtarlik(gaz: Gaz, stats) -> list[dict]:
    g = json.loads((RAW_EVAL / "ibb-muhtarlik" / "muhtarlik.geojson").read_text(encoding="utf-8"))
    recs = [f["properties"] for f in g["features"]]
    stats["raw rows"] = len(recs)
    keep = [{"text": p.get("Adres"), "ilce": (p.get("İlçe Adı") or "").strip(),
             "mahalle": (p.get("Mahalle Adı") or "").strip(), "name": p.get("Muhtarlık Adı") or ""} for p in recs]
    return build_ibb_text_rows("ibb-muhtarlik", keep, gaz, stats)


# --- İBB semt pazarları (templated) ----------------------------------------------------------------------
PAZAR_STREET_RX = re.compile(r"^(?P<ad>[^,;()]+?)\s+(?P<tur>cd|cad|sk|sok|blv|bulv|bulvari|caddesi|sokak)\.?$")


def load_ibb_pazar(gaz: Gaz, stats) -> list[dict]:
    recs = read_table(read_xlsx(RAW_EVAL / "ibb-pazar-tpl" / "semt-pazarlari-2025.xlsx"))
    stats["raw rows"] = len(recs)
    rows = []
    for r in recs:
        ilce_col = (r.get("İlçe") or "").strip()
        ilce_id = gaz.ilce_id(34, ilce_col)
        mah_raw = re.sub(r"\s+", " ", (r.get("Mahalle") or "").strip())
        if not ilce_id or not mah_raw:
            stats["drop: ilçe/mahalle missing"] += 1
            continue
        parts = [re.sub(r"\s+", " ", str(r.get(c) or "").strip()) for c in ("Cadde", "Sokak")]
        pieces = [mah_raw] + [p for p in parts if p]
        text = " ".join(pieces) + f" {ilce_col}/İstanbul"
        row = new_row("ibb-pazar-tpl", text, 34, ilce_id, gaz)
        ctx: dict = {"tags": set(), "notes": ["text templated from the Mahalle/Cadde/Sokak/İlçe columns"]}
        mah_name = re.sub(r"(?i)\s+(mh|mah)\.?$", "", mah_raw)
        if not set_mahalle(row, ilce_id, mah_name, gaz, ctx, fuzzy=True, require=True):
            stats["drop: mahalle not resolved"] += 1
            continue
        f = cfold(text)
        fields = row["gold"]["fields"]
        segs = []
        mah_end = len(mah_name)
        segs.append((0, mah_end, "mahalle", mah_name))
        streets = [p for p in parts if p]
        pos = len(mah_raw) + 1
        full = True
        if len(streets) == 1:
            sm = PAZAR_STREET_RX.match(cfold(streets[0]))
            if sm:
                fields["csbm_ad"] = re.sub(r"\s+", " ", streets[0][:sm.end("ad")]).strip()
                fields["csbm_tur"] = TYPE_MAP[sm.group("tur").rstrip(".")] if sm.group("tur") in TYPE_MAP else (
                    "cadde" if sm.group("tur").startswith("c") else "sokak")
                segs.append((pos + sm.start("ad"), pos + sm.end("ad"), "csbm_ad", fields["csbm_ad"]))
                te = pos + sm.end("tur") + (1 if text[pos + sm.end("tur"):pos + sm.end("tur") + 1] == "." else 0)
                segs.append((pos + sm.start("tur"), te, "csbm_tur", fields["csbm_tur"]))
            else:
                full = False
        elif len(streets) == 2:
            full = False
            ctx["notes"].append("two street columns (cadde and sokak); csbm not annotated")
        else:
            fields["csbm_ad"] = None
            fields["csbm_tur"] = None
        il_s = len(text) - len("İstanbul")
        segs.append((il_s - 1 - len(ilce_col), il_s - 1, "ilce", ilce_col))
        segs.append((il_s, len(text), "il", "İstanbul"))
        if full:
            finish_full(row, segs, False)
            if fields.get("dis_kapi") is None:
                fields["dis_kapi"] = None
        else:
            for lab in ("dis_kapi", "kat", "daire", "blok", "site", "posta_kodu"):
                fields[lab] = None  # templated: we know these are not in the text
        if not full or not attach_spans(row, segs):
            row.pop("spans", None)
        ctx["w"] = {"il": "yes", "ilce": "yes", "mahalle": "yes"}
        ctx["w_mah"] = mah_name
        ctx["w_type"] = "mahalle"  # "X mh"
        row["_entity"] = [("name", "ibb-pazar", ilce_id, key(r.get("Pazar Adı") or ""))]
        row["_ctx"] = ctx
        row["_segs"] = segs
        rows.append(row)
    return rows


# --- İzmir eczane -------------------------------------------------------------------------------------------
def load_izmir_eczane(gaz: Gaz, stats) -> list[dict]:
    raw = (RAW_EVAL / "izmir-eczane" / "eczane-listesi.csv").read_bytes().decode("utf-8-sig")
    recs = list(csv.DictReader(io.StringIO(raw, newline=""), delimiter=";"))
    stats["raw rows"] = len(recs)
    izmir_ilce = {i for i, r in gaz.ilce.items() if int(r["plaka"]) == 35}
    rows = []
    for r in recs:
        text = r.get("ADRES") or ""
        if not text.strip() or re.search(r"[\x00-\x1f]", text):
            stats["drop: empty or control characters"] += 1
            continue
        if any(MOBILE_AREA.match(m.group(1) or "") for m in PHONE_RX.finditer(text)):
            stats["drop: mobile phone in text (possible personal data)"] += 1
            continue
        f = cfold(text)
        try:
            nvi = int(r.get("ILCE_ID") or -1)
        except ValueError:
            nvi = -1
        ilce_id = gaz.ilce_id(35, IZMIR_NVI_ILCE[nvi]) if nvi in IZMIR_NVI_ILCE else None
        ctx: dict = {"tags": set(), "notes": []}
        m = ECZ_RX.match(f)
        mah_name = None
        mah_span = None
        if m and m.group("mah"):
            mah_name = text[m.start("mah"):m.end("mah")]
            mah_span = (m.start("mah"), m.end("mah"))
        else:
            mh = MAH_HEAD_RX.match(f)
            if mh:
                mah_name = text[mh.start("mah"):mh.end("mah")]
                mah_span = (mh.start("mah"), mh.end("mah"))
        # ilçe written at the end of the text?
        written_ilce = None
        for i in izmir_ilce:
            nm = gaz.ilce[i]["ad"]
            if re.search(rf"(?<![a-z]){re.escape(cfold(nm))}(?:[\s/\-,]*izmir)?\s*$", f):
                written_ilce = i
        if ilce_id and written_ilce and written_ilce != ilce_id:
            stats["drop: ILCE_ID conflicts with ilçe written in text"] += 1
            continue
        if not ilce_id and written_ilce:
            ilce_id = written_ilce
            ctx["notes"].append("ilçe taken from the text (ILCE_ID missing)")
        if mah_name and not ilce_id:
            hits = {i for i in izmir_ilce if gaz.resolve_birim(i, mah_name)[0] is not None}
            if len(hits) == 1:
                ilce_id = hits.pop()
                ctx["notes"].append("ilçe inferred from a mahalle name that is unique within İzmir (ILCE_ID missing)")
        row = new_row("izmir-eczane", text, 35, ilce_id, gaz)
        fields = row["gold"]["fields"]
        mah_ok = False
        if mah_name and ilce_id:
            mah_ok = set_mahalle(row, ilce_id, mah_name, gaz, ctx, fuzzy=True)
            if not mah_ok:
                other = {i for i in izmir_ilce - {ilce_id} if gaz.resolve_birim(i, mah_name)[0] is not None}
                if len(other) == 1:
                    stats["drop: mahalle belongs to another ilçe than ILCE_ID"] += 1
                    continue
                ctx["notes"].append("mahalle written in text not found in the ilçe; not annotated")
        segs = None
        text_full = False  # the whole text was parsed (independent of whether the true ilçe is known)
        if m and (not m.group("mah") or mah_ok or not STRUCT_RX.search(f[m.start("mah"):m.end("mah")])):
            sd_ad = (m.start("ad"), m.end("ad"))
            ad_txt = f[sd_ad[0]:sd_ad[1]]
            if not BAD_AD_RX.search(ad_txt):
                tur_s, tur_e = m.start("tur"), m.end("tur")
                if text[tur_e:tur_e + 1] == ".":
                    tur_e += 1
                fields["csbm_ad"] = re.sub(r"\s+", " ", text[sd_ad[0]:sd_ad[1]]).strip()
                fields["csbm_tur"] = TYPE_MAP[m.group("tur")]
                segs = [(sd_ad[0], sd_ad[1], "csbm_ad", fields["csbm_ad"]), (tur_s, tur_e, "csbm_tur",
                                                                               fields["csbm_tur"])]
                if m.group("mah"):
                    segs.append((m.start("mah"), m.end("mah"), "mahalle", fields.get("mahalle")))
                tail_s = m.start("tail")
                tail = f[tail_s:]
                door_ok = True
                if STRUCT_RX.search(tail):  # "X CAD. 222 SOK. NO:..": which street is meant is ambiguous
                    for lab in ("csbm_ad", "csbm_tur", "dis_kapi"):
                        fields.pop(lab, None)
                    segs, door_ok = [], False
                    stats["parsed: ambiguous (second street/door in tail)"] += 1
                elif m.group("no"):
                    # "NO:528/1 A", "NO:144/AKINIK", "NO:1/Z01": the door continues -> not annotated
                    if re.match(r"\s*[a-z](?![a-z])|[/\-a-z0-9]", tail):
                        door_ok = False
                    else:
                        for dseg in door_segments(text, m.start("no"), m.end("no")):
                            fields[dseg[2]] = dseg[3]
                            segs.append(dseg)
                tsegs, full = tail_admin(text, f, tail_s, fields.get("ilce"), "İzmir")
                text_full = full and door_ok
                if full and door_ok and fields.get("ilce") is not None:
                    segs += tsegs
                    if not m.group("no"):
                        fields["dis_kapi"] = None
                    finish_full(row, segs)
                    stats["parsed: full"] += 1
                else:
                    segs = None
                    if "csbm_ad" in fields:
                        stats["parsed: street/door only"] += 1
                    if ilce_id:
                        words = [w for w in re.split(r"[\s/\-,()]+", tail) if w]
                        grams = {key(" ".join(words[i:i + n])) for n in (1, 2) for i in range(len(words))}
                        gm = key(fields.get("mahalle"))
                        if any(g and g != gm and g in gaz.semt_by_ilce[ilce_id] for g in grams):
                            ctx["tags"].add("semt")
            else:
                stats["parsed: admin only"] += 1
        else:
            stats["parsed: admin only"] += 1
        # il / ilçe written?
        il_written = re.search(r"(?<![a-z])izmir\s*$", f) is not None
        if not il_written:
            ctx["tags"].add("missing-il")
        if fields.get("ilce"):
            if written_ilce == ilce_id:
                pass
            elif not re.search(rf"(?<![a-z]){re.escape(cfold(fields['ilce']))}(?![a-z])", f):
                ctx["tags"].add("missing-ilce")
        else:
            ctx["notes"].append("ilçe unknown (ILCE_ID missing and not written); gold.ilce not annotated")
        if segs is None or not attach_spans(row, segs):
            row.pop("spans", None)
        # ilçe/il words inside the written mahalle or street name do not count as a written ilçe/il
        fm = f
        for sp in [mah_span] + ([(m.start("ad"), m.end("ad"))] if m and "csbm_ad" in fields else []):
            if sp:
                fm = fm[:sp[0]] + " " * (sp[1] - sp[0]) + fm[sp[1]:]
        # substring test (no word boundary): glued writings such as "NO:144/AKINIK" make the state unknown
        any_ilce_word = any(cfold(gaz.ilce[i]['ad']) in fm for i in izmir_ilce)
        any_il_word = "izmir" in fm
        ctx["w"] = {
            "mahalle": "yes" if mah_name else ("no" if text_full else "unknown"),
            "ilce": "yes" if written_ilce else ("no" if text_full or not any_ilce_word else "unknown"),
            "il": "yes" if il_written else ("no" if text_full or not any_il_word else "unknown"),
        }
        ctx["w_mah"] = mah_name
        ctx["w_type"] = "mahalle" if mah_name else None  # written as "X MAH./MH./MAHALLESİ"
        row["_entity"] = [("name", "izmir-eczane", key(r.get("ADI") or ""), ilce_id),
                          ("id", "izmir-eczane", r.get("ECZANE_ID"))]
        row["_ctx"] = ctx
        row["_segs"] = segs or []
        rows.append(row)
    return rows


# --- İzmir CBS (sağlık, muhtarlık): templated ------------------------------------------------------------
def izmir_structured_rows(source: str, recs: list[dict], gaz: Gaz, stats, entity_prefix: str) -> list[dict]:
    rows = []
    for r in recs:
        ilce_col = re.sub(r"\s+", " ", (r.get("ILCE") or "").strip())
        mah_col = re.sub(r"\s+", " ", (r.get("MAHALLE") or "").strip())
        yol = re.sub(r"\s+", " ", (r.get("YOL") or "").strip())
        kapi = re.sub(r"\s+", "", (r.get("KAPINO") or "").strip())
        ilce_id = gaz.ilce_id(35, ilce_col)
        if not ilce_id or not mah_col:
            stats["drop: ilçe/mahalle missing or not in gazetteer"] += 1
            continue
        if not yol or yol in ("0", "-"):
            stats["drop: no street (YOL)"] += 1
            continue
        if kapi in ("0", "-"):
            kapi = ""
        text = f"{mah_col} MAH. {yol}" + (f" NO:{kapi}" if kapi else "") + f" {ilce_col}/İZMİR"
        row = new_row(source, text, 35, ilce_id, gaz)
        ctx: dict = {"tags": set(), "notes": ["text templated from the MAHALLE/YOL/KAPINO/ILCE fields; the "
                                              "street type is not published, so csbm_tur is not annotated"]}
        if not set_mahalle(row, ilce_id, mah_col, gaz, ctx, fuzzy=True, require=True):
            stats["drop: mahalle not resolved"] += 1
            continue
        fields = row["gold"]["fields"]
        fields["csbm_ad"] = yol
        fields["dis_kapi"] = None
        for lab in ("semt", "site", "blok", "kat", "daire", "posta_kodu", "tarif"):
            fields[lab] = None
        p = len(mah_col)
        segs = [(0, p, "mahalle", mah_col)]
        p += len(" MAH. ")
        segs.append((p, p + len(yol), "csbm_ad", yol))
        p += len(yol)
        if kapi:
            p += len(" NO:")
            for dseg in door_segments(text, p, p + len(kapi)):
                fields[dseg[2]] = dseg[3]
                segs.append(dseg)
            p += len(kapi)
        p += 1
        segs.append((p, p + len(ilce_col), "ilce", ilce_col))
        segs.append((len(text) - len("İZMİR"), len(text), "il", "İzmir"))
        # csbm_tur is not annotated (absent), so the completeness check must not require it
        if not attach_spans(row, segs):
            row.pop("spans", None)
        ctx["w"] = {"il": "yes", "ilce": "yes", "mahalle": "yes"}
        ctx["w_mah"] = mah_col
        ctx["w_type"] = "mahalle"  # template "{MAHALLE} MAH."
        row["_entity"] = [("name", entity_prefix, key(r.get("ADI") or ""), ilce_id)]
        row["_ctx"] = ctx
        row["_segs"] = segs
        rows.append(row)
        stats["parsed: full (templated)"] += 1
    return rows


def load_izmir_saglik(gaz: Gaz, stats) -> list[dict]:
    recs = []
    for name, _ in SOURCES["izmir-saglik-tpl"]["files"]:
        d = json.loads((RAW_EVAL / "izmir-saglik-tpl" / name).read_text(encoding="utf-8"))
        for r in d.get("onemliyer", []):
            r = {k: r.get(k) for k in ("ILCE", "MAHALLE", "YOL", "KAPINO", "ADI")}  # ACIKLAMA never kept
            recs.append(r)
    stats["raw rows"] = len(recs)
    return izmir_structured_rows("izmir-saglik-tpl", recs, gaz, stats, "izmir-saglik")


def load_izmir_muhtarlik(gaz: Gaz, stats) -> list[dict]:
    raw = (RAW_EVAL / "izmir-muhtarlik-tpl" / "izbb-muhtarliklar.csv").read_bytes().decode("utf-8-sig")
    sample = raw[:2000]
    delim = ";" if sample.count(";") > sample.count(",") else ","
    recs = []
    for r in csv.DictReader(io.StringIO(raw, newline=""), delimiter=delim):
        # ACIKLAMA holds the muhtar's name: dropped here and never used.
        recs.append({k: (r.get(k) or "") for k in ("ILCE", "MAHALLE", "YOL", "KAPINO", "ADI")})
    stats["raw rows"] = len(recs)
    return izmir_structured_rows("izmir-muhtarlik-tpl", recs, gaz, stats, "izmir-muhtarlik")


LOADERS = {
    "ibb-saglik": load_ibb_saglik,
    "ibb-muhtarlik": load_ibb_muhtarlik,
    "ibb-pazar-tpl": load_ibb_pazar,
    "izmir-eczane": load_izmir_eczane,
    "izmir-saglik-tpl": load_izmir_saglik,
    "izmir-muhtarlik-tpl": load_izmir_muhtarlik,
}


# ============================================================== sampling
def components(rows: list[dict]) -> list[list[dict]]:
    """Union rows that share a facility identity or the same folded address text (across all sources)."""
    parent = list(range(len(rows)))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    first: dict = {}
    for i, r in enumerate(rows):
        for k in r["_entity"] + [("text", key(r["text"]))]:
            if k in first:
                a, b = find(i), find(first[k])
                if a != b:
                    parent[a] = b
            else:
                first[k] = i
    groups = collections.defaultdict(list)
    for i, r in enumerate(rows):
        groups[find(i)].append(r)
    return list(groups.values())


def select(rows_by_source: dict[str, list[dict]]):
    allrows = [r for rs in rows_by_source.values() for r in rs]
    comps = components(allrows)
    by_source = collections.defaultdict(list)
    for c in comps:
        rep = min(c, key=lambda r: h(r["source"], r["text"]))
        rep["_group_size"] = len(c)
        by_source[rep["source"]].append(rep)
    selected = []
    for skey, reps in by_source.items():
        quota = min(SOURCES[skey]["quota"], len(reps) - len(reps) % DEV_EVERY)
        strata = collections.defaultdict(list)
        for r in reps:
            strata[r["gold"].get("ilce") or 0].append(r)
        for s in strata.values():
            s.sort(key=lambda r: h(r["source"], r["text"]))
        alloc = {k: min(2, len(v)) for k, v in strata.items()}
        rest = quota - sum(alloc.values())
        if rest < 0:
            raise SystemExit(f"{skey}: quota {quota} below the per-ilçe floor")
        cap = {k: len(v) - alloc[k] for k, v in strata.items()}
        tot = sum(cap.values()) or 1
        share = {k: rest * cap[k] / tot for k in strata}
        for k in strata:
            alloc[k] += int(share[k])
        left = quota - sum(alloc.values())
        for k in sorted(strata, key=lambda k: (-(share[k] - int(share[k])), k)):
            if left <= 0:
                break
            if alloc[k] < len(strata[k]):
                alloc[k] += 1
                left -= 1
        chosen = []
        for k in sorted(strata):
            chosen += strata[k][:alloc[k]]
        chosen.sort(key=lambda r: (r["gold"].get("ilce") or 0, h(r["source"], r["text"])))
        for i, r in enumerate(chosen):
            r["split"] = "dev" if i % DEV_EVERY == 2 else "test"
        selected += chosen
    return selected, {s: len(v) for s, v in by_source.items()}



# ============================================================== text semantics (SCHEMA: what the text says/determines)
def _unique(xs):
    xs = set(xs)
    return xs.pop() if len(xs) == 1 else None


SETTLEMENT_TURS = {"mahalle", "koy", "osb"}
TYPE_WORD_TURS = {"mahalle": {"mahalle", "osb"}, "koy": {"koy"}, "mevki": {"mevki"}}


def type_filter(cands: set, written_type: str | None, gaz: Gaz) -> set:
    """eval/SCHEMA.md: a written type word restricts the unit type (`Mah.` -> mahalle/OSB, `Köyü` -> köy,
    `Mevkii` -> mevkii); without one, settlements (mahalle, köy, OSB) win over mevkii/mezra/yayla/küme evler/site
    units of the same name, which only count when no settlement matches."""
    if written_type:
        return {b for b in cands if gaz.birim[b]["tur"] in TYPE_WORD_TURS[written_type]}
    settled = {b for b in cands if gaz.birim[b]["tur"] in SETTLEMENT_TURS}
    return settled or cands


def retarget(row: dict, ctx: dict, gaz: Gaz) -> None:
    """Turn the publisher's true location (built by the loaders) into text-based gold.

    fields.il/ilce/mahalle: official name if the text writes it, null if it certainly does not, absent if that
    cannot be decided. gold.il/ilce/birim: what the written mahalle/ilçe/il determine via the gazetteer (same rule
    as the synthetic generator); null = undetermined; absent when a non-unique result could still be narrowed by a
    component whose presence in the text is unknown. The publisher's true location is kept in `note`.
    """
    g, fields = row["gold"], row["gold"]["fields"]
    w = ctx["w"]
    true_il, true_ilce, true_birim = g.get("il"), g.get("ilce"), g.get("birim")
    notes = ctx.setdefault("notes", [])
    if true_birim is not None:
        notes.append(f"true location: birim {true_birim}")
    elif true_ilce is not None:
        notes.append(f"true location: ilce {true_ilce}")
    else:
        notes.append(f"true location: il {true_il}")

    # --- fields: only what the text writes
    for lab in ("il", "ilce", "mahalle"):
        if w[lab] == "no":
            fields[lab] = None
        elif w[lab] == "unknown":
            fields.pop(lab, None)

    # --- ids: what the text determines
    il_w = true_il if w["il"] == "yes" else None
    ilce_key = key(fields["ilce"]) if w["ilce"] == "yes" and fields.get("ilce") else None

    def plaka_of_ilce(d):
        return int(gaz.ilce[d]["plaka"])

    def ilce_of(b):
        return int(gaz.birim[b]["ilce_id"])

    def districts_from_ilce():
        if ilce_key is None:
            return set()
        return {d for d in gaz.ilce_by_name_key.get(ilce_key, ()) if il_w is None or plaka_of_ilce(d) == il_w}

    out: dict = {}
    written_mah = ctx.get("w_mah")
    if w["mahalle"] == "yes" and written_mah:
        keys = {key(written_mah)}
        if fields.get("mahalle"):
            keys.add(key(fields["mahalle"]))
        cands = {b for k in keys for b in gaz.units_by_key.get(k, ())}
        cands |= {b for k in keys for b in gaz.alias_units.get(k, ())}
        cands = type_filter(cands, ctx.get("w_type"), gaz)
        if not fields.get("mahalle"):
            exact = [gaz.birim[b]["ad"] for b in
                     type_filter(set(gaz.units_by_key.get(key(written_mah), ())), ctx.get("w_type"), gaz)]
            if exact:
                fields["mahalle"] = collections.Counter(exact).most_common(1)[0][0]
                old = "mahalle written in text not found in the ilçe; not annotated"
                if old in notes:
                    notes[notes.index(old)] = ("written mahalle exists in the gazetteer but not in the "
                                               "publisher's ilçe")
        if il_w is not None:
            cands = {b for b in cands if plaka_of_ilce(ilce_of(b)) == il_w}
        if ilce_key is not None:
            cands = {b for b in cands if key(gaz.ilce[ilce_of(b)]["ad"]) == ilce_key}
        if cands:
            districts = {ilce_of(b) for b in cands}
            out["birim"] = _unique(cands)
        else:  # the written mahalle is unknown to the gazetteer (or contradicts il/ilçe): use il/ilçe only
            districts = districts_from_ilce()
            out["birim"] = None
        out["ilce"] = _unique(districts)
        out["il"] = il_w if il_w is not None else _unique(plaka_of_ilce(d) for d in districts)
    else:
        districts = districts_from_ilce()
        out["birim"] = None if w["mahalle"] == "no" else "absent"
        out["ilce"] = _unique(districts)
        out["il"] = il_w if il_w is not None else _unique(plaka_of_ilce(d) for d in districts)
    if any(v == "unknown" for v in w.values()):
        for k in ("birim", "ilce", "il"):
            if out.get(k) is None:
                out.pop(k, None)
    if out.get("birim") == "absent":
        out.pop("birim")
    fields_obj = g.pop("fields")
    for k in ("il", "ilce", "birim"):
        g.pop(k, None)
        if k in out:
            g[k] = out[k]
    g["fields"] = fields_obj

# ============================================================== output
def finalize(row: dict, gaz: Gaz) -> dict:
    f = cfold(row["text"])
    ctx = row["_ctx"]
    retarget(row, ctx, gaz)
    tags = tag_row(row, f, row.get("_segs"), ctx)
    fields = row["gold"]["fields"]
    gold = {k: row["gold"][k] for k in ("il", "ilce", "birim") if k in row["gold"]}
    gold["fields"] = {k: fields[k] for k in FIELD_ORDER if k in fields}
    out = {
        "id": f"real-{row['source']}-{h(row['source'], row['text'])[:10]}",
        "text": row["text"],
        "source": row["source"],
        "license": row["license"],
        "split": row["split"],
    }
    if row.get("spans"):
        out["spans"] = row["spans"]
    out["gold"] = gold
    out["tags"] = sorted(t for t in tags if t in SCHEMA_TAGS)
    notes = list(dict.fromkeys(ctx.get("notes", [])))
    if notes:
        out["note"] = "; ".join(notes)
    return out


def write_jsonl(path: Path, rows: list[dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        for r in rows:
            fh.write(json.dumps(r, ensure_ascii=False, separators=(",", ":")) + "\n")


# ============================================================== validation
def validate(paths: list[Path], gaz: Gaz | None = None) -> list[str]:
    gaz = gaz or Gaz()
    errors = []
    ids = set()
    texts = collections.defaultdict(set)
    n = 0
    for p in paths:
        split_expected = p.stem
        for ln, line in enumerate(open(p, encoding="utf-8"), 1):
            n += 1
            where = f"{p.name}:{ln}"
            try:
                r = json.loads(line)
            except json.JSONDecodeError as e:
                errors.append(f"{where}: invalid JSON ({e})")
                continue
            for k in ("id", "text", "source", "license", "split", "gold"):
                if k not in r:
                    errors.append(f"{where}: missing {k}")
            if errors and errors[-1].startswith(where):
                continue
            if r["id"] in ids:
                errors.append(f"{where}: duplicate id {r['id']}")
            ids.add(r["id"])
            if not r["id"].startswith(f"real-{r['source']}-"):
                errors.append(f"{where}: id prefix")
            if r["split"] != split_expected:
                errors.append(f"{where}: split {r['split']} in {p.name}")
            if r["license"] not in LICENSES or r["license"] != SOURCES.get(r["source"], {}).get("license"):
                errors.append(f"{where}: license {r['license']!r}")
            t = r["text"]
            if not isinstance(t, str) or not t.strip():
                errors.append(f"{where}: empty text")
                continue
            texts[key(t)].add(r["split"])
            if any(MOBILE_AREA.match(m.group(1) or "") for m in PHONE_RX.finditer(t)):
                errors.append(f"{where}: mobile phone number in text")
            g = r["gold"]
            fields = g.get("fields")
            if not isinstance(fields, dict):
                errors.append(f"{where}: gold.fields missing")
                continue
            for k in fields:
                if k not in LABELS:
                    errors.append(f"{where}: unknown field {k}")
            if g.get("il") is not None and g["il"] not in gaz.il:
                errors.append(f"{where}: il {g['il']} not in staging")
            if g.get("ilce") is not None:
                if g["ilce"] not in gaz.ilce:
                    errors.append(f"{where}: ilce {g['ilce']} not in staging")
                elif int(gaz.ilce[g["ilce"]]["plaka"]) != g.get("il"):
                    errors.append(f"{where}: ilce/il mismatch")
                elif fields.get("ilce") is not None and key(fields["ilce"]) != key(gaz.ilce[g["ilce"]]["ad"]):
                    errors.append(f"{where}: fields.ilce != staging name")
            if g.get("birim") is not None:
                b = gaz.birim.get(g["birim"])
                if not b:
                    errors.append(f"{where}: birim {g['birim']} not in staging")
                elif int(b["ilce_id"]) != g.get("ilce"):
                    errors.append(f"{where}: birim/ilce mismatch")
                elif not fields.get("mahalle"):
                    errors.append(f"{where}: gold.birim set but no written mahalle")
                elif key(fields["mahalle"]) != key(b["ad"]):
                    errors.append(f"{where}: fields.mahalle != staging name")
            if g.get("il") is not None and fields.get("il") is not None and \
                    key(fields["il"]) != key(gaz.il[g["il"]]["ad"]):
                errors.append(f"{where}: fields.il != staging name")
            # text semantics: an il/ilçe value must be written in the text (exact folded match)
            for lab in ("il", "ilce"):
                if fields.get(lab) is not None and not find_unique(cfold(t), fields[lab]) and \
                        key(fields[lab]) not in key(t):
                    errors.append(f"{where}: fields.{lab}={fields[lab]!r} is not written in the text")
            if g.get("birim") is None and g.get("ilce") is None and g.get("il") is None and \
                    all(fields.get(k) is None for k in ("il", "ilce", "mahalle")) and "true location" not in \
                    r.get("note", ""):
                errors.append(f"{where}: no location at all and no true-location note")
            if fields.get("csbm_tur") not in (None, *set(TYPE_MAP.values()), "kume_evler"):
                errors.append(f"{where}: csbm_tur {fields.get('csbm_tur')!r}")
            for tg in r.get("tags", []):
                if tg not in SCHEMA_TAGS:
                    errors.append(f"{where}: unknown tag {tg}")
            spans = r.get("spans")
            if spans is not None:
                if not spans:
                    errors.append(f"{where}: empty spans array")
                n16 = u16(t, len(t))
                prev_end = 0
                for sp in sorted(spans, key=lambda s: s["start"]):
                    s16, e16, lab = sp["start"], sp["end"], sp["label"]
                    if not (0 <= s16 < e16 <= n16) or lab not in LABELS:
                        errors.append(f"{where}: span out of range/label {sp}")
                        continue
                    if s16 < prev_end:
                        errors.append(f"{where}: overlapping spans")
                    prev_end = e16
                    w = t[from_u16(t, s16):from_u16(t, e16)]
                    gv = fields.get(lab)
                    if gv is None:
                        errors.append(f"{where}: span {lab} without a gold value")
                    elif lab in NAME_LABELS and key(w) != key(gv):
                        errors.append(f"{where}: span {lab} {w!r} != {gv!r} after folding")
                    elif lab == "csbm_tur" and TYPE_MAP.get(cfold(w).rstrip(".")) != gv:
                        errors.append(f"{where}: csbm_tur span {w!r} != {gv!r}")
                    elif lab in ("dis_kapi", "blok", "daire") and compact(w) != compact(gv):
                        errors.append(f"{where}: span {lab} {w!r} != {gv!r}")
    for k, splits in texts.items():
        if len(splits) > 1:
            errors.append(f"same address text in dev and test: {k}")
    if not errors:
        print(f"validation OK: {n} rows, {len(ids)} unique ids", file=sys.stderr)
    return errors


# ============================================================== reports
def counts_table(rows: list[dict]) -> str:
    srcs = list(SOURCES)
    lines = ["| source | dev | test | total | with spans | birim determined (non-null) | street annotated |",
             "|---|---:|---:|---:|---:|---:|---:|"]
    tot = collections.Counter()
    for s in srcs:
        rs = [r for r in rows if r["source"] == s]
        c = collections.Counter(r["split"] for r in rs)
        sp = sum(1 for r in rs if "spans" in r)
        bi = sum(1 for r in rs if r["gold"].get("birim") is not None)
        st = sum(1 for r in rs if "csbm_ad" in r["gold"]["fields"])
        lines.append(f"| `{s}` | {c['dev']} | {c['test']} | {len(rs)} | {sp} | {bi} | {st} |")
        tot.update({"dev": c["dev"], "test": c["test"], "all": len(rs), "sp": sp, "bi": bi, "st": st})
    lines.append(f"| **total** | **{tot['dev']}** | **{tot['test']}** | **{tot['all']}** | {tot['sp']} | "
                 f"{tot['bi']} | {tot['st']} |")
    return "\n".join(lines)


def tag_table(rows: list[dict]) -> str:
    srcs = list(SOURCES)
    c = {s: collections.Counter() for s in srcs}
    sc = {sp: collections.Counter() for sp in ("dev", "test")}
    for r in rows:
        c[r["source"]].update(r.get("tags", []))
        sc[r["split"]].update(r.get("tags", []))
    tags = sorted({t for cc in c.values() for t in cc})
    head = "| tag | dev | test | " + " | ".join(f"`{s}`" for s in srcs) + " |"
    lines = [head, "|---|" + "---:|" * (len(srcs) + 2)]
    for t in tags:
        lines.append(f"| `{t}` | {sc['dev'][t]} | {sc['test'][t]} | " + " | ".join(str(c[s][t]) for s in srcs)
                     + " |")
    return "\n".join(lines)


def field_table(rows: list[dict]) -> str:
    lines = ["| field | annotated (value) | annotated (null) | not annotated |", "|---|---:|---:|---:|"]
    for lab in FIELD_ORDER:
        v = sum(1 for r in rows if r["gold"]["fields"].get(lab) is not None)
        nn = sum(1 for r in rows if lab in r["gold"]["fields"] and r["gold"]["fields"][lab] is None)
        lines.append(f"| `{lab}` | {v} | {nn} | {len(rows) - v - nn} |")
    return "\n".join(lines)


# Manual review (2026-10-08). Kept here so SOURCES.md stays fully generated; update after each re-review.
REVIEW_MD = """## Manual review and known label-quality issues

Two independent seeded samples of 100 output rows each were checked by hand against the raw text
(`--inspect 100`, then `--inspect 100 --inspect-seed review2`). A row counts as wrong if any annotated
`gold.fields` value or id is wrong (tags are reported separately).

| Sample | Rows | Rows with a wrong field/id | Estimated row accuracy |
|---|---:|---:|---:|
| 1 (before parser fixes) | 100 | 5 (all `izmir-eczane`) | 95 % |
| 2 (after fixes, final build) | 100 | 0 | 100 % (95 % CI ≈ 96–100 %) |

Errors found in sample 1 and fixed in the parser: a door followed by glued text (`NO:144/AKINIK` → `144`),
an alphanumeric door suffix (`NO:1/Z01` → `1/Z`), cadde+sokak chains (`ISMAIL SIVRI CAD. 222 SOK.` labeled
as the cadde) and a semt glued to a numbered street (`ESKIIZMIR-3820 SOK.`). Such rows now leave the street
and/or door unannotated. Expected overall field accuracy: ≥ 98 % (written il/ilçe/mahalle are normalized with
the source columns; residual risk is in source errors, e.g. a wrong `ILCE_ID`, which only affects the
normalization of a written name and the `true location` note).

The review was done when il/ilçe/mahalle still held the publisher's true location. The switch to text
semantics (see the gold-label policy) changed only `il`/`ilce`/`mahalle` and the ids, mechanically; it was
re-checked on a further 60-row sample (`--inspect-seed semantics3`): one glued ilçe (`NO:144/AKINIK/IZMIR`) was
labeled `null`, and partially parsed rows now leave such fields absent instead.

Known issues and conventions to keep in mind:

- **Source errors.** Rows whose text contradicts the publisher's columns are dropped; a wrong column value
  with no contradicting text can still affect the `true location` note and the choice between near-identical
  spellings, not the text-determined ids.
- **`Yolu` as street type.** `Alemdağ Yan Yolu` → `csbm_ad=Alemdağ Yan`, `csbm_tur=yol`; but
  `Baraj Yolu Cad.` → `csbm_ad=Baraj Yolu`, `csbm_tur=cadde` (the last type word decides).
- **Doors (dış kapı / iç kapı).** `No:5 /1` → `dis_kapi=5`, `daire=1`; `No:3 /2C` → `3` + `2C`;
  `No:120/1-B` → `120` + `1-B`; a letter-only part stays in the door (`No:17/A` → `17/A`, `No:58 F` → `58F`);
  ranges stay (`No:90 -92A` → `90-92A`). Some publishers may use `/` differently; this is the schema convention.
- **Numbered streets keep the dot as written** (`892. Sk.` → `csbm_ad=892.`); `Gazetteer.Key` ignores it.
- **Tags are heuristic.** `ambiguous-name` follows the synthetic-set meaning (the mahalle name exists more than
  once nationally) and is therefore frequent; `landmark` can fire on names such as `İSTASYON ALTI`; `semt` is
  only detected in the trailing text of İzmir eczane rows; `abbreviation` covers type-word abbreviations.
- **`izmir-eczane` ids.** Most pharmacy texts write neither ilçe nor il, and common mahalle names
  (Atatürk, Cumhuriyet, …) exist in many provinces, so most eczane rows have `gold.birim/ilce/il = null`
  (the text does not determine them); undecidable rows have them absent. `mahalle` is annotated only when
  written as `X MAH./MH.`.
- **Templated sources** (`-tpl`) are easy by construction (fixed order, upper case for İzmir) and must be
  reported separately from the human-written sources.
"""


def write_reports(rows, manifests, stats, cand_counts):
    snapshot = max(e["downloaded"] for m in manifests.values() for e in m["files"].values())
    src_lines = []
    for s, src in SOURCES.items():
        man = manifests[s]
        st = stats[s]
        files = "\n".join(
            f"  - `{n}` — <{e['url']}> — sha256 `{e['sha256']}` — {e['bytes']} bytes — downloaded {e['downloaded']}"
            for n, e in man["files"].items())
        filt = "\n".join(f"  - {k}: {v}" for k, v in sorted(st.items()))
        src_lines.append(
            f"### `{s}` — {src['title']}\n\n"
            f"- Dataset page: <{src['dataset']}>\n"
            f"- Publisher: {src['publisher']}\n"
            f"- License: `{src['license']}` (<{src['license_url']}>)\n"
            f"- Text: {'**templated** from structured columns (not human-written)' if src['templated'] else 'the published free-text address field, verbatim'}\n"
            f"- Files:\n{files}\n"
            f"- Processing counts:\n{filt}\n"
            f"  - distinct entities after filtering: {cand_counts.get(s, 0)}\n")
    review = REVIEW_MD
    md = f"""# Real evaluation set — sources, filters and counts

Generated by `data/scripts/build_eval_real.py` (seed `{SEED}`). Do not edit by hand; re-run the script.
Format: [`eval/SCHEMA.md`](../SCHEMA.md). Licenses and attribution: [`LICENSE.md`](LICENSE.md).

Addresses of **public institutions and businesses only** (health facilities, muhtarlık offices, open-air
markets, pharmacies). No personal addresses. Facility names, phone columns and the muhtar name column are never
emitted; person-named health categories are excluded (see filters).

## Counts

{counts_table(rows)}

Split: dev ≈ 20 %, test ≈ 80 %, stratified by source and ilçe (every {DEV_EVERY}th entity in (ilçe, hash) order goes
to dev). The split is **by entity**: rows that share a facility identity (same name in the same ilçe, same
source record id) or the same folded address text are merged into one entity first, and one row per entity is
kept, so no facility or address appears in both splits. Test rows must never be used for tuning.

## Sources

{chr(10).join(src_lines)}
Sources with `-tpl` in their name have **templated** text built from structured columns. Report them separately
from the human-written sources (`ibb-saglik`, `ibb-muhtarlik`, `izmir-eczane`).

## Gold-label policy

- **`gold.fields` = what the text says** (same rule as the synthetic and challenge sets, `eval/SCHEMA.md`).
  `il` / `ilce` / `mahalle` get the official staging name **only if the text writes them** (any spelling,
  abbreviation or typo), otherwise `null`. The publisher's columns (İBB `İlçe Adı`/`Mahalle Adı`, İzmir
  `ILCE`/`MAHALLE`, İzmir eczane `ILCE_ID` = NVİ ilçe kimlikNo) are used only to normalize what is written:
  folded name within the ilçe, `alias.csv` for former names (`historic-name`), one-edit spelling differences only
  when unique within the ilçe (`typo`). İBB addresses end in `MAHALLE/İLÇE` and never write the il, so their
  `il` field is `null`. İzmir eczane: a mahalle counts as written only as `X MAH./MH.`; an ilçe/il only when
  it ends the text. When a partially parsed eczane text might still contain an ilçe/il/mahalle that the parser
  could not isolate (e.g. `…NO:144/AKINIK/IZMIR`, a bare semt/mahalle in the trailing text), that field is left
  **absent** rather than `null`.
- **`gold.il/ilce/birim` = what the text determines** via the gazetteer, with the synthetic generator's rule:
  start from the units whose official name (or semt/historic alias) matches the written mahalle, keep only the
  unit type the text names (`X MAH./MH.` → mahalle or OSB; İBB tails `MAHALLE/İLÇE` have no type word, so
  settlements — mahalle, köy, OSB — win over mevkii/mezra/yayla/küme evler/site units of the same name), keep
  those in the written il and ilçe; `birim` = the single remaining unit, `ilce` = the single remaining district, `il` = the
  written il or the single remaining province, else `null`. Without a written mahalle `birim` is `null` and
  il/ilçe come from what is written (a nationally unique ilçe name determines its il). If a needed component's
  presence is undecidable (see above), a non-unique result is left absent instead of `null`.
- **True location.** The publisher's location is kept in `note` as `true location: birim <id>` (or
  `ilce <id>` / `il <plaka>` when the mahalle is unknown), so a geocoding-style score can still be computed.
  `missing-il` / `missing-ilce` tags mark rows whose text does not write il/ilçe. `ambiguous-name` is set, as in
  the synthetic set, whenever the mahalle name occurs more than once nationally.
- **Street / door** (`csbm_tur`, `csbm_ad`, `dis_kapi`) only from a strict full-string parse
  (`<street> <type> No:<door>` followed by the known mahalle/ilçe tail). Otherwise these keys are absent
  (= not annotated). Templated İzmir rows have `csbm_ad` and `dis_kapi` from the `YOL`/`KAPINO` columns; the
  street type is not published, so `csbm_tur` is absent. Semt pazarı rows with both a cadde and a sokak column
  leave the street unannotated.
- **Null fields**: when the whole text was parsed, components that do not occur (`semt`, `site`, `blok`, `kat`,
  `daire`, `posta_kodu`, `tarif`, missing street/door) are annotated as `null`. Doors follow the schema's
  dış kapı / iç kapı split: `No:5 /1` → `dis_kapi=5`, `daire=1` (both spanned separately); `No:17/A` →
  `dis_kapi=17/A`, `daire=null`. Templated İzmir rows split `KAPINO` the same way.
- **posta_kodu** only if written in the text (none were found).
- **spans** only when the whole text was parsed and every labeled substring equals its gold value after
  folding (`Gazetteer.Key`); otherwise the row has no `spans` (partial spans are never written).
- **tags** are detected automatically (regexes on the folded text plus gold/text comparison).

## Filters applied

- İBB sağlık: only institutional categories are kept ({len(IBB_KEEP_CATEGORIES)} `Alt Kategori` values; excluded:
  Doktor/Muayenehane, Psikologlar, Diyetisyen, Diş Hekimi, Eczane, Veteriner, Gözlükçü/Optik, Sağlık Diğer).
  Rows whose facility name carries a personal title (Dr., Op., Uzm., Prof., Doç., Dt., Dyt., Psk., Vet., Ecz.,
  Fzt., hekim, muayenehane, eczanesi) are dropped as well. Rows with `<Null>`/empty address, or whose address
  tail differs from the `Mahalle/İlçe` columns, are dropped.
- İBB muhtarlık: the dataset has no muhtar name; all rows with a consistent tail are eligible.
- İzmir eczane: only `ADRES`, `ILCE_ID` (and internally `ADI`/`ECZANE_ID` to group duplicates; never emitted)
  are used. Rows whose address contains a **mobile** number (`05xx…`, possibly the pharmacist's own phone) are
  dropped; landline business numbers are kept verbatim and tagged `phone`. Rows whose `ILCE_ID` contradicts the
  ilçe written at the end of the text, or whose `X MAH.` belongs only to another ilçe, are dropped.
- İzmir muhtarlık: the `ACIKLAMA` column (muhtar's name) is discarded at load time.
- İzmir sağlık: public-institution endpoints only (ASM, hastane, TSM, ADSM, AÇSAP, dal merkezi, 112 istasyonu,
  verem savaş dispanseri, kan merkezi); tıp merkezi, poliklinik, laboratuvar and veterinerlik endpoints are not
  used. `ACIKLAMA` is discarded.

## Field coverage (all rows)

{field_table(rows)}

## Tags per split and source

{tag_table(rows)}

{review}
## Reproduce

```bash
python -I data/scripts/build_eval_real.py            # uses the cached raw files listed above
python -I data/scripts/build_eval_real.py --refresh  # re-download (sources change without versioning)
python -I data/scripts/build_eval_real.py --validate-only
```

Raw files are cached in `data/raw/eval/<source>/` (git-ignored) with a `MANIFEST.json` (URL, sha256, size,
download date, license). The output is deterministic for identical inputs. Raw snapshot: {snapshot}.
"""
    (OUT / "SOURCES.md").write_text(md, encoding="utf-8", newline="\n")

    lic = f"""# Real evaluation set — licenses

Every row carries a `license` field. The rows are derived from public-sector open data; the AdresTR **code**
license (MIT) does not apply to them.

| `license` value | Sources | License | Attribution |
|---|---|---|---|
| `{IBB_LICENSE}` | `ibb-saglik`, `ibb-muhtarlik`, `ibb-pazar-tpl` | İstanbul Büyükşehir Belediyesi Açık Veri Lisansı 1.0 (<{IBB_LICENSE_URL}>) — free use including commercial use, copying, adaptation; compatible with CC BY 4.0 and ODC-By | required |
| `{IZM_LICENSE}` | `izmir-eczane`, `izmir-saglik-tpl`, `izmir-muhtarlik-tpl` | Creative Commons Attribution 4.0 International, as declared by İzmir Büyükşehir Belediyesi for its open-data portal (<{IZM_LICENSE_URL}>) | required |

## Required attribution

Redistributions of `eval/real/` (for example a Hugging Face dataset) must keep this notice:

> Contains public sector information from İstanbul Büyükşehir Belediyesi (İBB Açık Veri Portalı,
> https://data.ibb.gov.tr) licensed under the İBB Açık Veri Lisansı, and from İzmir Büyükşehir Belediyesi
> (İzmir Açık Veri Portalı, https://acikveri.bizizmir.com) licensed under CC BY 4.0.
> {ATTRIBUTION_DEFAULT}
> Modified: rows were filtered, sampled and annotated by the AdresTR project; templated sources (`-tpl`) were
> rendered from structured columns.

Dataset pages and download dates are listed in [`SOURCES.md`](SOURCES.md).

## Notes per source

- **İBB (all three datasets).** The İBB license does not cover personal data contained in the information
  ("Bilgilerdeki kişisel veriler"). For that reason person-named health categories and names with personal titles
  are excluded, and no facility names are published. The license grants no right to suggest endorsement by İBB.
- **İzmir eczane.** Pharmacy names usually contain the pharmacist's name, so `ADI` and `TELEFON` are never
  emitted; rows whose address text contains a mobile number are excluded. Landline numbers that the publisher put
  inside the address field are business contact data and are kept verbatim (tag `phone`).
- **İzmir muhtarlık.** The `ACIKLAMA` field contains the muhtar's name and is never used.
- **İzmir sağlık (CBS web service).** Same CC BY 4.0 license as the dataset page that lists the endpoints.
- No warranty: both publishers provide the data "as is"; errors in the source data may remain in the gold labels
  (see the manual review in `SOURCES.md`).

`license` values follow the SPDX-like ids of `eval/SCHEMA.md` (`IBB-Acik-Veri` is not an SPDX id).
"""
    (OUT / "LICENSE.md").write_text(lic, encoding="utf-8", newline="\n")


# ============================================================== main
def inspect(n: int, seed: str = "") -> None:
    rows = []
    for sp in ("dev", "test"):
        rows += [json.loads(line) for line in open(OUT / f"{sp}.jsonl", encoding="utf-8")]
    rnd = random.Random(SEED + "-inspect" + seed)
    for r in rnd.sample(rows, min(n, len(rows))):
        f = r["gold"]["fields"]
        fl = " ".join(f"{k}={v!r}" for k, v in f.items() if v is not None)
        nulls = ",".join(k for k, v in f.items() if v is None)
        sp = " ".join(f"[{s['label']}:{r['text'][s['start']:s['end']]}]" for s in r.get("spans", []))
        print(f"{r['id']}  ({r['source']}/{r['split']})\n  TEXT : {r['text']}\n  GOLD : {fl}"
              f"  ids={r['gold'].get('il')},{r['gold'].get('ilce')},{r['gold'].get('birim')}"
              f"\n  NULL : {nulls}\n  SPANS: {sp}\n  TAGS : {','.join(r['tags'])}"
              + (f"\n  NOTE : {r['note']}" if r.get("note") else ""))


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--refresh", action="store_true", help="re-download every source file")
    ap.add_argument("--offline", action="store_true", help="never download; cached files must exist")
    ap.add_argument("--validate-only", action="store_true")
    ap.add_argument("--inspect", type=int, default=0, help="print N random output rows for manual review")
    ap.add_argument("--inspect-seed", default="", help="extra seed for --inspect (draw an independent sample)")
    a = ap.parse_args()
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    paths = [OUT / "dev.jsonl", OUT / "test.jsonl"]
    if a.inspect:
        inspect(a.inspect, a.inspect_seed)
        return
    if a.validate_only:
        errs = validate(paths)
        for e in errs[:200]:
            print(e, file=sys.stderr)
        raise SystemExit(1 if errs else 0)

    manifests = fetch(a.refresh, a.offline)
    gaz = Gaz()
    stats: dict[str, collections.Counter] = {}
    rows_by_source = {}
    for s, loader in LOADERS.items():
        stats[s] = collections.Counter()
        rows_by_source[s] = loader(gaz, stats[s])
        stats[s]["eligible rows"] = len(rows_by_source[s])
        print(f"  {s}: {len(rows_by_source[s])} eligible rows", file=sys.stderr)
    selected, cand_counts = select(rows_by_source)
    out = [finalize(r, gaz) for r in selected]
    out.sort(key=lambda r: (list(SOURCES).index(r["source"]), r["id"]))
    write_jsonl(paths[0], [r for r in out if r["split"] == "dev"])
    write_jsonl(paths[1], [r for r in out if r["split"] == "test"])
    write_reports(out, manifests, stats, cand_counts)
    errs = validate(paths, gaz)
    for e in errs[:200]:
        print(e, file=sys.stderr)
    if errs:
        raise SystemExit(f"{len(errs)} validation errors")
    c = collections.Counter((r["source"], r["split"]) for r in out)
    for (s, sp), v in sorted(c.items()):
        print(f"  {s:22s} {sp:5s} {v}", file=sys.stderr)


if __name__ == "__main__":
    main()
