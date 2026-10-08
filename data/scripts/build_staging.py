"""AdresTR staging CSV üretici (Yol B: yalnız PTT türevi MIT veri + Wikidata CC0).

Kullanım:
    python -I data/scripts/fetch_wikidata.py data/raw/wikidata   # (gerekirse) Wikidata CSV'leri
    python -I data/scripts/build_staging.py                      # data/staging/*.csv + SOURCES.json

GİRDİLER (yalnız bunlar; melihozkara/NVİ kopyası KULLANILMAZ):
    data/raw/muratgozel/src/data/neighbourhoods.json        (MIT, PTT türevi; birincil satır kaynağı)
    data/raw/epigra/src/Database/Seeders/Geozone*Seeder.php  (MIT, PTT 2022-08; semt sütunu + çapraz kontrol)
    data/raw/wikidata/wd_{il,ilce,mahalle,koy,belde,parent_up}.csv  (CC0)

ÇIKTILAR (data/staging/, UTF-8, LF, RFC4180, başlık satırı, id'ye göre sıralı):
    il.csv, ilce.csv, birim.csv, alias.csv, SOURCES.json, retired_ids.csv (yalnız id emekliye ayrılırsa)

ID KARARLILIĞI:
    * il: plaka.
    * ilce_id = plaka*100 + seq. Doğal anahtar (plaka, fold(ad)).
    * birim_id = ilce_id*10000 + seq. Doğal anahtar (ilce_id, tur, fold(ad), fold(ust_ad));
      aynı dörtlüye birden çok satır düşerse posta_kodu ile ayrıştırılır.
    * İlk çalıştırma: seq, ebeveyn içinde doğal anahtara göre sıralanarak 1..n verilir.
    * Sonraki çalıştırmalar: data/staging/*.csv okunur; aynı doğal anahtar aynı id'yi alır.
      Yeni varlıklar ebeveyndeki max(seq) (mevcut + emekli) + 1'den devam eder.
      Kaybolan varlıkların id'leri retired_ids.csv'ye yazılır ve ASLA yeniden kullanılmaz.

TUR EŞLEMESİ (PTT 'Mahalle' sütunundan; a2_mahalle.parse_ptt + ek ayrıştırma):
    "X Mah"                          -> mahalle      ("… Osb Mah" / "Organize Sanayi" -> osb)
    "X Mah (B Beldesi)"              -> mahalle, ust_ad = B   (belde mahallesi; 'belde' turu üretilmez,
                                                               çünkü PTT'de beldenin kendisi satır değildir)
    "X Mah (Y Mah)"                  -> mahalle, ust_ad = Y
    "X Köyü" / "X Köy" / "X (B Bucağı) Köyü" -> koy (bucak varsa ust_ad = "B Bucağı")
    "X Mah (K Köyü)", "X Mah (Y Mah) (K Köyü)"  -> köy alt birimi, ust_ad = "Y / K" (içten dışa):
         ad "... Mezrası"/"Mezra"            -> mezra   (sonek soyulur)
         ad "... Yayla Evleri/Yaylası/Yaylaları" -> yayla (sonek soyulur)
         ad "... Küme Evleri/Kümesi", "Küme N", "N.Küme" -> kume_evler (sonek soyulur; "Küme N" korunur)
         ad "... Mevkii/Mevki"               -> mevki   (sonek soyulur)
         ad içinde OSB / Organize Sanayi     -> osb     (OSB adın parçası olarak KALIR)
         ad içinde Site/Sitesi/Siteler       -> site    (adın parçası olarak KALIR)
         diğer                               -> mevki   (NVİ tip 4 'köy mahallesi/mevki' karşılığı)
    "K Köyü Yayla Evleri" vb. parantezsiz bileşikler -> yayla/mevki, ust_ad = K
    ayrıştırılamayan                  -> diger
    Sonek soyulunca ad boş kalırsa (ör. "Yayla Evleri Mah (X Köyü)") sonek ad olarak korunur
    ve doğrulamada 'kalan sonek' olarak sayılır.
"""
from __future__ import annotations

import csv
import hashlib
import io
import json
import re
import sys
from collections import Counter, defaultdict
from datetime import date
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import RAW, ROOT, fold, fold_compact, load_epigra, load_murat, load_wd, qid, tr_lower  # noqa: E402
from a2_mahalle import parse_ptt  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")

STAGING = ROOT / "staging"
DATA_VERSION = "2026.10"
TURLER = ["mahalle", "koy", "belde", "mezra", "mevki", "yayla", "kume_evler", "osb", "site", "diger"]

# ------------------------------------------------------------------ elle (el) alias tabloları
IL_ALIAS = [  # (plaka, alias) -> tur=yazim, kaynak=el
    (3, "Afyon"), (3, "Afyon Karahisar"),
    (27, "Antep"), (27, "G.Antep"),
    (30, "Hakkâri"),
    (33, "İçel"),
    (46, "Maraş"), (46, "K.Maraş"), (46, "Kahraman Maraş"),
    (63, "Urfa"), (63, "Ş.Urfa"),
]
ILCE_ALIAS = [  # (plaka, folded hedef ilçe adı, alias, tur)
    (34, "eyupsultan", "Eyüp", "tarihsel"),          # 2017'de yeniden adlandırıldı
    (6, "kahramankazan", "Kazan", "tarihsel"),       # 2016'da yeniden adlandırıldı
    (25, "aziziye", "Ilıca", "tarihsel"),            # ilçenin eski adı / merkezi
    (55, "19 mayis", "Ondokuzmayıs", "yazim"),
    (55, "19 mayis", "Ondokuz Mayıs", "yazim"),
]

# ------------------------------------------------------------------ görüntü adı onarımı
_ACRONYM = {"osb": "OSB", "toki": "TOKİ", "ptt": "PTT", "dsi": "DSİ", "tcdd": "TCDD", "sgk": "SGK",
            "ssk": "SSK", "kss": "KSS", "tso": "TSO", "tem": "TEM", "trt": "TRT"}
_ROMAN = {"ıı": "II", "ııı": "III", "ıv": "IV", "vı": "VI", "vıı": "VII", "vııı": "VIII", "ıx": "IX"}


def _cap(ch: str) -> str:
    return {"i": "İ", "ı": "I"}.get(ch, ch.upper())


def tr_display(s: str) -> str:
    """PTT/murat metnini Türkçe başlık biçimine onarır.

    - Kelime başı, '(' '/' '.' sonrası ve tire sonrası büyük harf
      (Farsça izafet '-i/-ı/-ü/-u' küçük kalır: 'Kuva-i Milliye').
    - Kesme işaretinden sonra küçük ('Tan'ın').
    - Sıra sayısı: '100.yıl' -> '100. Yıl'.
    - Kısaltmalar (OSB, TOKİ, PTT ...) ve Roma rakamları (II, III ...) büyük.
    """
    s = re.sub(r"\s+", " ", tr_lower(s)).strip()
    s = re.sub(r"(\d)\.\s*(?=[^\d\s.])", r"\1. ", s)          # 100.yıl -> 100. yıl
    s = re.sub(r"(\w{2,})osb\b", r"\1 osb", s)                 # 'acıdereosb' -> 'acıdere osb'
    s = re.sub(r"(?<![\wçğıöşü])ı\.(?=\S)", "I. ", s)          # 'ı.organize' -> 'I. organize'
    out = []
    prev = " "
    for i, ch in enumerate(s):
        if ch.isalpha():
            start = prev in " (/." or prev.isdigit() and False
            if prev == "-":
                # izafet: tek harf i/ı/ü/u ve ardından boşluk/son -> küçük kalsın
                nxt = s[i + 1] if i + 1 < len(s) else " "
                start = not (ch in "iıüu" and not nxt.isalpha())
            if prev == " " or prev == "(" or prev == "/" or prev == ".":
                start = True
            out.append(_cap(ch) if start else ch)
        else:
            out.append(ch)
        prev = ch
    s = "".join(out)

    def fix_token(m):
        w = m.group(0)
        lw = tr_lower(w)
        if lw in _ACRONYM:
            return _ACRONYM[lw]
        if lw in _ROMAN:
            return _ROMAN[lw]
        return w

    s = re.sub(r"[A-Za-zÇĞİÖŞÜçğıöşüÂÎÛâîû]+", fix_token, s)
    s = s.replace("P.t.t", "P.T.T").replace("P.T.t", "P.T.T")
    return s


# ------------------------------------------------------------------ PTT birim ayrıştırma
_MAH_TAIL = re.compile(r"(?i)\s*\b(?:mahallesi|mah\.?|mh\.?)\s*$")
_SUBTYPES = [
    ("mezra", re.compile(r"(?i)\s*\b(?:mezras[ıi]|mezra)$")),
    ("yayla", re.compile(r"(?i)\s*\b(?:yayla evleri|yaylalar[ıi]|yaylas[ıi])$")),
    ("kume_evler", re.compile(r"(?i)\s*\b(?:küme evleri|kümesi)$")),
    ("mevki", re.compile(r"(?i)\s*\b(?:mevkii|mevkisi|mevki)$")),
]
_OSB = re.compile(r"(?i)\bosb\b|osb$|organize sanayi")
_SITE = re.compile(r"(?i)\bsite(?:si|ler)?\b")
_KUME_NAME = re.compile(r"(?i)^(?:küme\s*\d+|\d+\.?\s*küme)$")
_SUFFIX_RESIDUAL = re.compile(
    r"(?i)(?:\bmah\.?|\bmh\.?|\bmahallesi|\bköyü|\bbeldesi|\bmezras[ıi]|\bmevkii|\byaylas[ıi]|\byayla evleri|\bküme evleri|\bkümesi)$")


def strip_mah(name: str) -> str:
    name = re.sub(r"(?i)\bmh\.(?=\S)", " ", name)              # 'Mh.mezrası' -> ' mezrası'
    m = re.match(r"(?i)^(.+?)mah\.$", name)                     # 'Yenimah.' -> 'Yeni'
    if m and " " not in name:
        name = m.group(1)
    prev = None
    while prev != name:                                         # 'Ahmed-i Hani Mahallesi Mah'
        prev = name
        name = _MAH_TAIL.sub("", name).strip()
    return re.sub(r"\s+", " ", name).strip()


def subtype(name: str):
    """Ad sonundaki tür sonekini bul -> (tur|None, soyulmuş ad)."""
    for tur, rx in _SUBTYPES:
        if rx.search(name):
            stripped = strip_mah(rx.sub("", name).strip())
            return tur, (stripped if stripped else name)
    return None, name


def parse_unit(raw: str):
    """-> (tur, ad_ham, [üst adlar içten dışa])"""
    kind, name, parent, sub = parse_ptt(raw)
    name = strip_mah(name or "")
    ust = []
    if sub:
        ust.append(strip_mah(sub))
    if kind == "koy":
        if sub:  # 'Gözecik (merkez Bucağı) Köyü'
            return "koy", name, [sub.strip()]
        return "koy", name, []
    if kind == "mahalle":
        return ("osb" if _OSB.search(name) else "mahalle"), name, []
    if kind == "belde_mah":
        return ("osb" if _OSB.search(name) else "mahalle"), name, ust + [parent]
    if kind == "mah_alt":
        return ("osb" if _OSB.search(name) else "mahalle"), name, ust + [strip_mah(parent)]
    if kind == "koy_alt":
        ust = ust + [parent]
        t, nm = subtype(name)
        if t:
            return t, nm, ust
        if _KUME_NAME.match(name) or re.match(r"(?i)^küme\d", name):
            return "kume_evler", name, ust
        if _OSB.search(name):
            return "osb", name, ust
        if _SITE.search(name):
            return "site", name, ust
        return "mevki", name, ust
    # kind == 'other'
    if parent and re.search(r"(?i)köyü$", name):               # 'Gözecik Köyü' + (Merkez Bucağı)
        return "koy", re.sub(r"(?i)\s*köyü$", "", name), [parent]
    m = re.match(r"(?i)^(.+?)\s+köyü?\s+(.+)$", name)          # 'Dereköy Köyü Yayla Evleri'
    if m:
        k, rest = m.group(1), m.group(2)
        t, nm = subtype(rest)
        if not t and re.fullmatch(r"(?i)yayla(?:s[ıi]|lar[ıi])?|yayla evleri", rest):
            t, nm = "yayla", "Yayla"
        return (t or "mevki"), nm, [k]
    m = re.match(r"(?i)^(.+?)\s+köy$", name)                    # 'Kıran Köy', 'Karacalar Eski Köy'
    if m:
        return "koy", m.group(1), []
    return "diger", name, ([parent] if parent else [])


def koy_parent_of(raw: str):
    """PTT satırı bir köyün alt birimiyse o köyün ham adını döndür, değilse None."""
    kind, name, parent, sub = parse_ptt(raw)
    if kind == "koy_alt":
        return parent
    if kind == "other" and not (parent and re.search(r"(?i)köyü$", name or "")):
        m = re.match(r"(?i)^(.+?)\s+köyü?\s+(.+)$", strip_mah(name or ""))
        if m:
            return m.group(1)
    return None


# ------------------------------------------------------------------ yardımcılar
def nk(*parts) -> tuple:
    return tuple(parts)


def kfold(s: str) -> str:
    return fold(s or "", strip_suffix=False)


def fmt_coord(x):
    if x is None:
        return ""
    return f"{x:.6f}".rstrip("0").rstrip(".")


def parse_point(p: str):
    m = re.match(r"Point\(([-\d.eE]+) ([-\d.eE]+)\)", p or "")
    return (float(m.group(2)), float(m.group(1))) if m else None  # (enlem, boylam)


def coord_of(points: set, tol: float = 0.02):
    pts = [parse_point(p) for p in points]
    pts = [p for p in pts if p]
    if not pts:
        return None, None
    if len(pts) == 1:
        return pts[0]
    lats = [p[0] for p in pts]
    lons = [p[1] for p in pts]
    if max(lats) - min(lats) <= tol and max(lons) - min(lons) <= tol:
        return sum(lats) / len(lats), sum(lons) / len(lons)
    return None, None  # çelişkili çoklu koordinat -> boş


def group_wd(rows, multi):
    out = {}
    for r in rows:
        it = qid(r["item"])
        o = out.setdefault(it, {"item": it, "trLabel": r.get("trLabel", "")})
        for k in multi:
            v = r.get(k)
            if v:
                o.setdefault(k, set()).add(qid(v) if v.startswith("http") else v)
    return out


def clean_label(s: str) -> str:
    s = re.sub(r"\s*\(.*?\)\s*$", "", s or "")
    s = s.split(",")[0].strip()
    return re.sub(r"(?i)\s+(ilçesi|ilçe|mahallesi|köyü)$", "", s).strip()


def write_csv(path: Path, header, rows):
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, lineterminator="\n", quoting=csv.QUOTE_MINIMAL)
        w.writerow(header)
        w.writerows(rows)


def read_csv(path: Path):
    if not path.exists():
        return []
    with open(path, encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f))


def sha256(p: Path) -> str:
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


# ------------------------------------------------------------------ kürasyonlu tablolar
CURATED_SEMT = ROOT / "curated" / "semt_alias.csv"


def merge_curated_semt(alias: set, il_rows, ilce_rows, units) -> dict:
    """data/curated/semt_alias.csv -> alias (hedef=birim, tur=semt, kaynak=el).

    il / ilçe / mahalle katlanmış adla çözülür; mahalle yalnız tur ∈ {mahalle, osb} birimleri
    arasında aranır. Herhangi bir başvuru tekil çözülemezse derleme açık bir hatayla durur.
    Aynı (hedef, hedef_id, katlanmış alias) zaten varsa satır atlanır.
    """
    if not CURATED_SEMT.exists():
        return {"dosya": "yok", "eklenen": 0, "atlanan_ayni": 0}
    with open(CURATED_SEMT, encoding="utf-8-sig", newline="") as f:
        rows = list(csv.DictReader(f))
    il_by = defaultdict(list)
    for r in il_rows:
        il_by[fold_compact(r[1])].append(r[0])
    ilce_by = defaultdict(list)
    for r in ilce_rows:
        ilce_by[(r[1], fold_compact(r[2]))].append(r[0])
    mah_by = defaultdict(list)
    for u in units:
        if u["tur"] in ("mahalle", "osb"):
            mah_by[(u["ilce_id"], fold_compact(u["ad"]))].append(u["birim_id"])
    existing = {(h, i, fold_compact(a)) for h, i, a, _, _ in alias}
    errors, added, skipped = [], 0, 0
    for n, r in enumerate(rows, start=2):
        where = f"{CURATED_SEMT.name}:{n}"
        il, ilce, semt = (r.get("il") or "").strip(), (r.get("ilce") or "").strip(), (r.get("semt") or "").strip()
        if not semt:
            errors.append(f"{where}: boş semt")
            continue
        pl = il_by.get(fold_compact(il), [])
        if len(pl) != 1:
            errors.append(f"{where}: il '{il}' tekil çözülemedi ({len(pl)} aday)")
            continue
        ic = ilce_by.get((pl[0], fold_compact(ilce)), [])
        if len(ic) != 1:
            errors.append(f"{where}: ilçe '{il}/{ilce}' tekil çözülemedi ({len(ic)} aday)")
            continue
        semt_disp = re.sub(r"\s+", " ", semt)
        for mah in [m.strip() for m in (r.get("mahalleler") or "").split("|") if m.strip()] or [None]:
            if mah is None:
                errors.append(f"{where}: 'mahalleler' boş")
                break
            b = mah_by.get((ic[0], fold_compact(mah)), [])
            if len(b) != 1:
                errors.append(f"{where}: mahalle '{il}/{ilce}/{mah}' tekil çözülemedi ({len(b)} aday)")
                continue
            key = ("birim", b[0], fold_compact(semt_disp))
            if key in existing:
                skipped += 1
                continue
            existing.add(key)
            alias.add(("birim", b[0], semt_disp, "semt", "el"))
            added += 1
    if errors:
        raise SystemExit("KÜRASYON HATASI (data/curated/semt_alias.csv):\n  " + "\n  ".join(errors))
    return {"dosya": "curated/semt_alias.csv", "satir": len(rows), "eklenen": added, "atlanan_ayni": skipped,
            "sha256": sha256(CURATED_SEMT)}


# ------------------------------------------------------------------ id ataması
class IdAllocator:
    """Ebeveyn içinde seq ataması; mevcut id'leri ve emekli id'leri korur."""

    def __init__(self, scale: int, existing: dict, retired: set):
        self.scale = scale
        self.existing = existing          # natural key -> id
        self.retired = retired            # emekli id'ler
        self.used = set()

    def assign(self, parent_id: int, keys_sorted: list) -> dict:
        """keys_sorted: bu ebeveynin doğal anahtarları (deterministik sırada)."""
        out = {}
        taken = {i for i in list(self.existing.values()) + list(self.retired) if i // self.scale == parent_id}
        nxt = max([i % self.scale for i in taken], default=0) + 1
        for k in keys_sorted:
            if k in self.existing:
                out[k] = self.existing[k]
            else:
                if nxt >= self.scale:
                    raise SystemExit(f"seq taştı: ebeveyn {parent_id}")
                out[k] = parent_id * self.scale + nxt
                nxt += 1
        self.used.update(out.values())
        return out


# ------------------------------------------------------------------ ana akış
def main():
    stats = {}
    today = date.today().isoformat()

    # ---------------- PTT (murat) + epigra semt
    mu = load_murat()
    ep, ep_ci, ep_co, ep_di = load_epigra()
    il_name_by_plate = {}
    for r in mu:
        il_name_by_plate[int(r[0])] = r[1]
    plate_by_ilfold = {kfold(v): k for k, v in il_name_by_plate.items()}

    # semt: (plaka, fold ilçe, pk) -> semt (epigra). PK<->semt 1:1 (a3 analizinde doğrulandı).
    semt_by_pk = defaultdict(set)
    for il, ilce, semt, mah, pk in ep:
        semt_by_pk[(plate_by_ilfold[kfold(il)], kfold(ilce), pk)].add(semt)
    # çapraz kontrol murat <-> epigra
    mu_keys = {(int(r[0]), kfold(r[2]), tr_lower(r[3]), r[4]) for r in mu}
    ep_keys = {(plate_by_ilfold[kfold(r[0])], kfold(r[1]), tr_lower(r[3]), r[4]) for r in ep}
    stats["capraz_kontrol_epigra"] = {
        "murat_satir": len(mu), "epigra_satir": len(ep),
        "ortak_satir_(il,ilce,ad,pk)": len(mu_keys & ep_keys),
        "yalniz_murat": len(mu_keys - ep_keys), "yalniz_epigra": len(ep_keys - mu_keys),
        "semtsiz_pk": sum(1 for r in mu if (int(r[0]), kfold(r[2]), r[4]) not in semt_by_pk),
        "birden_fazla_semtli_pk": sum(1 for v in semt_by_pk.values() if len(v) > 1),
    }

    # ---------------- Wikidata
    wil = group_wd(load_wd("wd_il"), ("plaka", "provId", "iso", "coord"))
    wilce = group_wd(load_wd("wd_ilce"), ("parent", "distId", "coord"))
    wmah = group_wd(load_wd("wd_mahalle"), ("parent", "coord", "nbhId", "villId", "yerelnet"))
    wkoy = group_wd(load_wd("wd_koy"), ("parent", "coord", "nbhId", "villId", "yerelnet"))
    wbelde = group_wd(load_wd("wd_belde"), ("parent",))
    up = defaultdict(set)
    town_label = {}
    for r in load_wd("wd_parent_up"):
        p, u, u2 = qid(r["parent"]), qid(r.get("up", "")), qid(r.get("up2", ""))
        if r.get("parentLabel"):
            town_label[p] = r["parentLabel"]
        if u:
            up[p].add(u)
        if u and u2:
            up[u].add(u2)
    for q, o in list(wbelde.items()) + list(wilce.items()):
        up[q] |= o.get("parent", set())

    # il QID: P14358 (asla P395)
    il_q = {}
    plate_by_q = {}
    for q, o in wil.items():
        ids = o.get("provId") or {x.split("-")[1] for x in o.get("iso", set()) if "-" in x}
        if len(ids) == 1:
            pl = int(next(iter(ids)))
            il_q.setdefault(pl, []).append(q)
            plate_by_q[q] = pl

    # ---------------- il.csv
    il_rows = []
    for pl in sorted(il_name_by_plate):
        qs = il_q.get(pl, [])
        q = qs[0] if len(qs) == 1 else ""
        lat, lon = coord_of(wil[q].get("coord", set())) if q else (None, None)
        il_rows.append([pl, tr_display(il_name_by_plate[pl]), q, fmt_coord(lat), fmt_coord(lon)])

    # ---------------- ilçe
    ilce_names = {}  # (plaka, fold) -> display
    for r in mu:
        ilce_names[(int(r[0]), kfold(r[2]))] = tr_display(r[2])
    existing_ilce = {(int(r["plaka"]), kfold(r["ad"])): int(r["ilce_id"]) for r in read_csv(STAGING / "ilce.csv")}
    retired_rows = read_csv(STAGING / "retired_ids.csv")
    retired = {(r["hedef"], int(r["id"])) for r in retired_rows}
    alloc_ilce = IdAllocator(100, existing_ilce, {i for h, i in retired if h == "ilce"})
    ilce_id = {}
    by_plate = defaultdict(list)
    for k in ilce_names:
        by_plate[k[0]].append(k)
    for pl in sorted(by_plate):
        ilce_id.update(alloc_ilce.assign(pl, sorted(by_plate[pl], key=lambda k: k[1])))

    # Wikidata ilçe öğesi -> ilce_id (yalnız Wikidata + el alias; NVİ yok)
    alias_ilce = {(pl, kfold(a)): (pl, tgt) for pl, tgt, a, _ in ILCE_ALIAS}
    ilce_fold_count = Counter(k[1] for k in ilce_names)
    q_to_ilce = {}

    def label_to_ilce(label, parents, allow_no_plate):
        """(il, temizlenmiş etiket) -> tekil PTT ilçesi; değilse None."""
        lab = kfold(clean_label(label))
        if not lab:
            return None
        plates = {plate_by_q[p] for p in parents if p in plate_by_q}
        cands = set()
        for pl in plates:
            for cand in (lab, "merkez" if lab == kfold(il_name_by_plate[pl]) or lab == "merkez" or lab.startswith("merkez ") else None):
                if cand and (pl, cand) in ilce_names:
                    cands.add((pl, cand))
            if (pl, lab) in alias_ilce:
                cands.add(alias_ilce[(pl, lab)])
        if allow_no_plate and not plates and ilce_fold_count.get(lab) == 1 and lab != "merkez":
            cands = {k for k in ilce_names if k[1] == lab}
        return ilce_id[next(iter(cands))] if len(cands) == 1 else None

    for q, o in wilce.items():
        iid = label_to_ilce(o["trLabel"], o.get("parent", ()), True)
        if iid:
            q_to_ilce[q] = iid
    ilce_items = set(q_to_ilce)
    # Kasaba/belediye öğeleri (köylerin P131 hedefi): kendi etiketi + kendi P131'i (il) ile ilçeye
    n_town = 0
    for q, lab in town_label.items():
        if q in q_to_ilce or q in plate_by_q or q in wilce:
            continue
        iid = label_to_ilce(lab, up.get(q, ()), True)
        if iid:
            q_to_ilce[q] = iid
            n_town += 1
    stats["kasaba_ogesi_ilceye_baglanan"] = n_town
    # ilçe satırı için tekil Wikidata öğesi seç
    cand_by_ilce = defaultdict(list)
    for q, iid in q_to_ilce.items():
        if q in ilce_items:
            cand_by_ilce[iid].append(q)
    ilce_q = {}
    for iid, qs in cand_by_ilce.items():
        if len(qs) > 1:  # tie-break yalnız Wikidata verisiyle: P14366 taşıyan / etikette 'ilçe' geçen
            qs2 = [q for q in qs if wilce[q].get("distId")] or \
                  [q for q in qs if re.search(r"(?i)ilçe", wilce[q]["trLabel"])]
            qs = qs2
        if len(qs) == 1:
            ilce_q[iid] = qs[0]
    stats["ilce_wikidata_coklu_aday_bos"] = sum(1 for iid in cand_by_ilce if iid not in ilce_q)

    ilce_rows = []
    for k, iid in ilce_id.items():
        q = ilce_q.get(iid, "")
        lat, lon = coord_of(wilce[q].get("coord", set()), tol=0.1) if q else (None, None)
        ilce_rows.append([iid, k[0], ilce_names[k], q, fmt_coord(lat), fmt_coord(lon)])
    ilce_rows.sort()

    # ---------------- birim
    units = []
    for plaka, il, ilce, mah, pk in mu:
        tur, ad_raw, ust_raw = parse_unit(mah)
        ad = tr_display(ad_raw)
        ust = " / ".join(tr_display(u) for u in ust_raw if u and u.strip())
        semts = semt_by_pk.get((int(plaka), kfold(ilce), pk), set())
        semt = tr_display(next(iter(semts))) if len(semts) == 1 else ""
        iid = ilce_id[(int(plaka), kfold(ilce))]
        units.append(dict(ilce_id=iid, tur=tur, ad=ad, ust_ad=ust, pk=pk, semt=semt, raw=mah, plaka=int(plaka),
                          koy_parent=koy_parent_of(mah)))

    # Sentetik köy satırları: PTT'de yalnız alt birimlerinin üstü olarak geçen köyler
    # (kendi "X Köyü" satırı yok). ad = köy adı; ust_ad boş (PTT köyün üstünü vermez);
    # posta_kodu / semt yalnız tüm alt birimlerde tek bir değer varsa, aksi halde boş.
    koy_have = {(u["ilce_id"], fold_compact(u["ad"])) for u in units if u["tur"] == "koy"}
    parents = {}
    for u in units:
        if not u["koy_parent"]:
            continue
        disp = tr_display(u["koy_parent"])
        key = (u["ilce_id"], fold_compact(disp))
        if key in koy_have:
            continue
        p = parents.setdefault(key, dict(ad=disp, pks=set(), semts=set(), plaka=u["plaka"]))
        p["pks"].add(u["pk"])
        p["semts"].add(u["semt"])
    n_synth_pk = 0
    for (iid, _), p in sorted(parents.items()):
        pk = next(iter(p["pks"])) if len(p["pks"]) == 1 else ""
        semts = {s for s in p["semts"] if s}
        semt = next(iter(semts)) if len(semts) == 1 and len(p["semts"]) == 1 else ""
        n_synth_pk += bool(pk)
        units.append(dict(ilce_id=iid, tur="koy", ad=p["ad"], ust_ad="", pk=pk, semt=semt,
                          raw=f"[sentetik] {p['ad']} Köyü", plaka=p["plaka"], koy_parent=None, sentetik=True))
    stats["sentetik_koy"] = {"satir": len(parents), "posta_kodu_dolu": n_synth_pk,
                             "posta_kodu_bos_(coklu_kod)": len(parents) - n_synth_pk}

    # doğal anahtar + tekrar ayıklama
    def k4(u):
        return (u["ilce_id"], u["tur"], kfold(u["ad"]), kfold(u["ust_ad"]))

    groups = defaultdict(list)
    for u in units:
        groups[k4(u)].append(u)
    dedup = []
    dup_dropped = []
    for k, lst in groups.items():
        by_pk = defaultdict(list)
        for u in lst:
            by_pk[u["pk"]].append(u)
        for pk, l2 in by_pk.items():
            dedup.append(l2[0])
            dup_dropped += [(x["raw"], pk) for x in l2[1:]]
    units = dedup
    stats["birim_tekrar_ayiklanan"] = dup_dropped
    groups = defaultdict(list)
    for u in units:
        groups[k4(u)].append(u)

    def natkey(u):
        k = k4(u)
        return k + ((u["pk"],) if len(groups[k]) > 1 else ())

    existing_birim = {}
    for r in read_csv(STAGING / "birim.csv"):
        k = (int(r["ilce_id"]), r["tur"], kfold(r["ad"]), kfold(r["ust_ad"]))
        existing_birim.setdefault(k, []).append((r["posta_kodu"], int(r["birim_id"])))
    ex_map = {}
    for k, lst in existing_birim.items():
        if len(lst) == 1 and len(groups.get(k, [])) <= 1:
            ex_map[k] = lst[0][1]
        else:
            for pk, bid in lst:
                ex_map[k + (pk,)] = bid
    alloc_b = IdAllocator(10000, ex_map, {i for h, i in retired if h == "birim"})
    by_ilce = defaultdict(list)
    for u in units:
        by_ilce[u["ilce_id"]].append(natkey(u))
    bid_of = {}
    for iid in sorted(by_ilce):
        keys = sorted(by_ilce[iid], key=lambda k: (TURLER.index(k[1]), k[2], k[3], k[4] if len(k) > 4 else ""))
        bid_of.update(alloc_b.assign(iid, keys))
    for u in units:
        u["birim_id"] = bid_of[natkey(u)]

    # ---------------- Wikidata birim eşleşmesi (yalnız Wikidata)
    def resolve_ilce(q, depth=3, seen=None):
        seen = seen or set()
        if q in seen:
            return set()
        seen.add(q)
        if q in q_to_ilce:
            return {q_to_ilce[q]}
        if q in plate_by_q or depth == 0:
            return set()
        res = set()
        for p in up.get(q, ()):
            res |= resolve_ilce(p, depth - 1, seen)
        return res

    belde_label = {q: kfold(clean_label(o["trLabel"])) for q, o in wbelde.items()}

    def wd_index(items, cls):
        idx_p, idx_n = defaultdict(list), defaultdict(list)
        unresolved = 0
        for q, o in items.items():
            ilces = set()
            for p in o.get("parent", ()):
                ilces |= resolve_ilce(p)
            if len(ilces) != 1:
                unresolved += 1
                continue
            iid = next(iter(ilces))
            name = fold_compact(clean_label(o["trLabel"]))
            if not name:
                continue
            bparent = {belde_label[p] for p in o.get("parent", ()) if p in belde_label}
            par = fold_compact(next(iter(bparent))) if len(bparent) == 1 else ""
            idx_p[(iid, cls, name, par)].append(q)
            idx_n[(iid, cls, name)].append(q)
        return idx_p, idx_n, unresolved

    def nokoy(n):
        return n[:-3] if n.endswith("koy") and len(n) > 5 else n

    def b_keys(u, cls):
        par = fold_compact(u["ust_ad"].split(" / ")[-1]) if u["ust_ad"] else ""
        return (u["ilce_id"], cls, fold_compact(u["ad"]), par), (u["ilce_id"], cls, fold_compact(u["ad"]))

    wd_match = {}
    wd_unresolved = {}
    for cls, items, turs in (("mahalle", wmah, {"mahalle", "osb"}), ("koy", wkoy, {"koy"})):
        idx_p, idx_n, unres = wd_index(items, cls)
        wd_unresolved[cls] = unres
        cand = [u for u in units if u["tur"] in turs]
        bp, bn = defaultdict(list), defaultdict(list)
        for u in cand:
            kp, kn = b_keys(u, cls)
            bp[kp].append(u)
            bn[kn].append(u)
        used_q = set()
        # 1) ad + üst (belde) ; 2) yalnız ad ; 3) köy-eki (yalnız köy)
        for u in cand:
            kp, kn = b_keys(u, cls)
            q = None
            if len(bp[kp]) == 1 and len(idx_p.get(kp, [])) == 1:
                q = idx_p[kp][0]
            elif len(bn[kn]) == 1 and len(idx_n.get(kn, [])) == 1:
                q = idx_n[kn][0]
            if q:
                wd_match.setdefault(q, []).append((u["birim_id"], cls))
        if cls == "koy":
            bk, ik = defaultdict(list), defaultdict(list)
            for u in cand:
                bk[(u["ilce_id"], nokoy(fold_compact(u["ad"])))].append(u)
            for (iid, c, name), qs in idx_n.items():
                ik[(iid, nokoy(name))].extend(qs)
            matched_b = {b for v in wd_match.values() for b, c in v if c == cls}
            for k, lst in bk.items():
                u = lst[0]
                if len(lst) == 1 and u["birim_id"] not in matched_b and len(set(ik.get(k, []))) == 1:
                    q = ik[k][0]
                    if q not in wd_match:
                        wd_match.setdefault(q, []).append((u["birim_id"], cls))
    # bir Wikidata öğesi birden çok birime ya da bir birim birden çok öğeye gidiyorsa -> boş bırak
    b2q = defaultdict(set)
    for q, lst in wd_match.items():
        if len({b for b, _ in lst}) == 1:
            b2q[lst[0][0]].add((q, lst[0][1]))
    birim_wd = {b: next(iter(s)) for b, s in b2q.items() if len(s) == 1}

    for u in units:
        u.update(wikidata="", nvi_id="", enlem="", boylam="")
        if u["birim_id"] in birim_wd:
            q, cls = birim_wd[u["birim_id"]]
            o = (wmah if cls == "mahalle" else wkoy)[q]
            ids = o.get("nbhId" if cls == "mahalle" else "villId", set())
            ids = {i for i in ids if i.isdigit()}
            lat, lon = coord_of(o.get("coord", set()))
            u.update(wikidata=q, nvi_id=next(iter(ids)) if len(ids) == 1 else "",
                     enlem=fmt_coord(lat), boylam=fmt_coord(lon))
            u["_yerelnet"] = bool(o.get("yerelnet")) and cls == "mahalle"

    birim_rows = sorted([[u["birim_id"], u["ilce_id"], u["tur"], u["ad"], u["ust_ad"], u["pk"], u["semt"],
                          u["wikidata"], u["nvi_id"], u["enlem"], u["boylam"]] for u in units])

    # ---------------- alias
    alias = set()
    for pl, a in IL_ALIAS:
        alias.add(("il", pl, a, "yazim", "el"))
    for pl, tgt, a, tur in ILCE_ALIAS:
        alias.add(("ilce", ilce_id[(pl, tgt)], a, tur, "el"))
    # PTT semt: yalnız 'bağımsız semt adı' sınıfı (PTT verisinden sınıflandırma; NVİ yok)
    by_pk = defaultdict(list)
    for u in units:
        if u["pk"]:  # sentetik köylerde posta kodu boş olabilir
            by_pk[(u["ilce_id"], u["pk"])].append(u)
    belde_by_ilce = defaultdict(set)
    koy_by_ilce = defaultdict(set)
    for u in units:
        if u["tur"] in ("mahalle", "osb") and u["ust_ad"]:
            belde_by_ilce[u["ilce_id"]].add(fold_compact(u["ust_ad"].split(" / ")[-1]))
        if u["tur"] == "koy":
            koy_by_ilce[u["ilce_id"]].add(fold_compact(u["ad"]))
        if u["ust_ad"] and u["tur"] not in ("mahalle", "osb"):
            koy_by_ilce[u["ilce_id"]].add(fold_compact(u["ust_ad"].split(" / ")[-1]))
    ilce_disp = {r[0]: r[2] for r in ilce_rows}
    semt_cls = Counter()
    for (iid, pk), lst in by_pk.items():
        s = lst[0]["semt"]
        if not s:
            continue
        fs = fold_compact(s)
        if fs == fold_compact(ilce_disp[iid]):
            c = "ilce"
        elif fs.endswith("koyler"):
            c = "koyler"
        elif fs in belde_by_ilce[iid]:
            c = "belde"
        elif fs in {fold_compact(u["ad"]) for u in lst}:
            c = "kendi_birimi"
        elif fs in koy_by_ilce[iid]:
            c = "koy"
        else:
            c = "gercek_alias"
            for u in lst:
                alias.add(("birim", u["birim_id"], s, "semt", "ptt-semt"))
        semt_cls[c] += 1
    stats["semt_siniflandirma"] = dict(semt_cls)
    # Wikidata 6360: P2123 (YerelNet köy kimliği) taşıyan, birime tekil bağlanmış mahalle öğeleri
    n6360 = 0
    for u in units:
        if u.get("_yerelnet") and u["tur"] == "mahalle":
            alias.add(("birim", u["birim_id"], f"{u['ad']} Köyü", "tarihsel", "wikidata-6360"))
            n6360 += 1
    # Elle bakılan semt alias'ları: data/curated/semt_alias.csv (il,ilce,semt,mahalleler,kaynak,not)
    curated = merge_curated_semt(alias, il_rows, ilce_rows, units)
    stats["kuratorlu_semt"] = curated
    alias_rows = sorted(alias,key=lambda r: (["il", "ilce", "birim"].index(r[0]), r[1], r[2], r[3], r[4]))

    # ---------------- emekli id'ler
    new_ilce_ids = {r[0] for r in ilce_rows}
    new_birim_ids = {r[0] for r in birim_rows}
    ret = [(r["hedef"], int(r["id"]), r["dogal_anahtar"], r["tarih"]) for r in retired_rows]
    for k, i in existing_ilce.items():
        if i not in new_ilce_ids:
            ret.append(("ilce", i, json.dumps(k, ensure_ascii=False), today))
    for k, i in ex_map.items():
        if i not in new_birim_ids:
            ret.append(("birim", i, json.dumps(k, ensure_ascii=False), today))

    # ---------------- doğrulama
    problems = []
    if len(il_rows) != 81:
        problems.append(f"il sayısı {len(il_rows)} != 81")
    if len(ilce_rows) != 973:
        problems.append(f"ilçe sayısı {len(ilce_rows)} != 973")
    for name, rows in (("il", il_rows), ("ilce", ilce_rows), ("birim", birim_rows)):
        ids = [r[0] for r in rows]
        if len(ids) != len(set(ids)):
            problems.append(f"{name}: tekil olmayan id")
    ilce_set = set(new_ilce_ids)
    orphan = [r for r in birim_rows if r[1] not in ilce_set]
    if orphan:
        problems.append(f"birim: ilce_id'si olmayan {len(orphan)}")
    pk_empty = [r[0] for r in birim_rows if not r[5]]
    pk_bad = [(r[0], r[5]) for r in birim_rows if r[5] and (not re.fullmatch(r"\d{5}", r[5]) or int(r[5][:2]) != r[1] // 100)]
    synth_ids = {u["birim_id"] for u in units if u.get("sentetik")}
    if any(i not in synth_ids for i in pk_empty):
        problems.append("sentetik olmayan birimde boş posta_kodu")
    empty_ad = [r for r in birim_rows if not r[3]] + [r for r in ilce_rows if not r[2]] + [r for r in il_rows if not r[1]]
    space_bad = []
    for rows, cols in ((il_rows, (1,)), (ilce_rows, (2,)), (birim_rows, (3, 4, 6)), (alias_rows, (2,))):
        for r in rows:
            for c in cols:
                v = r[c]
                if v and (v != v.strip() or "  " in v):
                    space_bad.append(v)
    residual = [(r[0], r[2], r[3]) for r in birim_rows if _SUFFIX_RESIDUAL.search(r[3])]
    bad_tur = [r for r in birim_rows if r[2] not in TURLER]
    if empty_ad:
        problems.append(f"boş ad {len(empty_ad)}")
    if space_bad:
        problems.append(f"boşluk sorunu {len(space_bad)}: {space_bad[:5]}")
    if bad_tur:
        problems.append(f"geçersiz tur {len(bad_tur)}")
    if len(alloc_ilce.used) != len(new_ilce_ids) or set(bid_of.values()) != new_birim_ids:
        problems.append("id eşleme tutarsızlığı")

    cov = {}
    for t in TURLER:
        rs = [r for r in birim_rows if r[2] == t]
        if rs:
            cov[t] = {"n": len(rs), "wikidata": sum(1 for r in rs if r[7]), "koordinat": sum(1 for r in rs if r[9]),
                      "nvi_id": sum(1 for r in rs if r[8])}
    cov["_il"] = {"n": 81, "wikidata": sum(1 for r in il_rows if r[2]), "koordinat": sum(1 for r in il_rows if r[3])}
    cov["_ilce"] = {"n": len(ilce_rows), "wikidata": sum(1 for r in ilce_rows if r[3]),
                    "koordinat": sum(1 for r in ilce_rows if r[4])}

    # ---------------- yaz
    STAGING.mkdir(parents=True, exist_ok=True)
    write_csv(STAGING / "il.csv", ["plaka", "ad", "wikidata", "enlem", "boylam"], il_rows)
    write_csv(STAGING / "ilce.csv", ["ilce_id", "plaka", "ad", "wikidata", "enlem", "boylam"], ilce_rows)
    write_csv(STAGING / "birim.csv", ["birim_id", "ilce_id", "tur", "ad", "ust_ad", "posta_kodu", "semt",
                                      "wikidata", "nvi_id", "enlem", "boylam"], birim_rows)
    write_csv(STAGING / "alias.csv", ["hedef", "hedef_id", "alias", "tur", "kaynak"], alias_rows)
    with open(STAGING / "VERSION", "w", encoding="utf-8", newline="\n") as f:
        f.write(DATA_VERSION + "\n")
    if ret:
        write_csv(STAGING / "retired_ids.csv", ["hedef", "id", "dogal_anahtar", "tarih"], sorted(set(ret)))

    murat_sha = "99aca4907c85f95f8dbb9b6e2b0512325cc8be0e"
    epigra_sha = "c5566de9a88c515de93a158addb0adca2cf360a0"
    inputs = [("muratgozel/src/data/neighbourhoods.json",
               f"https://raw.githubusercontent.com/muratgozel/turkey-neighbourhoods/{murat_sha}/src/data/neighbourhoods.json",
               "MIT", "2024-03-31 (npm turkey-neighbourhoods@4.0.3; içerik PTT pk_list ~2022-08)")]
    for f in ("GeozoneCitiesTableSeeder", "GeozoneCountiesTableSeeder", "GeozoneDistrictsTableSeeder",
              "GeozoneNeighbourhoodsTableSeeder"):
        inputs.append((f"epigra/src/Database/Seeders/{f}.php",
                       f"https://raw.githubusercontent.com/epigra/tr-geozones/{epigra_sha}/src/Database/Seeders/{f}.php",
                       "MIT", "2022-08-10 (PTT)"))
    for f in ("wd_il", "wd_ilce", "wd_mahalle", "wd_koy", "wd_belde", "wd_parent_up"):
        p = RAW / "wikidata" / f"{f}.csv"
        snap = date.fromtimestamp(p.stat().st_mtime).isoformat()
        inputs.append((f"wikidata/{f}.csv",
                       f"https://query.wikidata.org/sparql (sorgu: data/scripts/fetch_wikidata.py QUERIES['{f}'])",
                       "CC0-1.0", snap))
    sources = {
        "olusturma_tarihi": today,
        "uretici": "data/scripts/build_staging.py",
        "veri_surumu": DATA_VERSION,
        "not": "Yol B: yalnız PTT türevi (MIT) + Wikidata (CC0). melihozkara/NVİ kopyası girdi DEĞİLDİR.",
        "girdiler": [{"path": p, "sha256": sha256(RAW / p), "url": u, "lisans": lic, "anlik_goruntu": s}
                     for p, u, lic, s in inputs],
        "kuratorlu_girdiler": ([{"path": "data/curated/semt_alias.csv", "sha256": sha256(CURATED_SEMT),
                                 "lisans": "CC0-1.0", "kaynak": "el (data/curated/README.md)"}]
                               if CURATED_SEMT.exists() else []),
        "ciktilar": {"il.csv": len(il_rows), "ilce.csv": len(ilce_rows), "birim.csv": len(birim_rows),
                     "alias.csv": len(alias_rows), "retired_ids.csv": len(set(ret))},
        "birim_tur_dagilimi": dict(Counter(r[2] for r in birim_rows)),
        "alias_dagilimi": {f"{h}/{t}/{k}": c for (h, t, k), c in
                           sorted(Counter((r[0], r[3], r[4]) for r in alias_rows).items())},
        "kapsam": cov,
        "dogrulama": {
            "sorunlar": problems,
            "posta_kodu_plaka_istisnalari": pk_bad[:50],
            "posta_kodu_plaka_istisna_sayisi": len(pk_bad),
            "posta_kodu_bos_(yalniz_sentetik_koy)": len(pk_empty),
            "sentetik_koy": stats["sentetik_koy"],
            "kalan_tur_soneki_sayisi": len(residual),
            "kalan_tur_soneki_ornek": residual[:30],
            "ilce_wikidata_coklu_aday_bos": stats["ilce_wikidata_coklu_aday_bos"],
            "kasaba_ogesi_ilceye_baglanan": stats["kasaba_ogesi_ilceye_baglanan"],
            "wikidata_ilceye_cozulemeyen_oge": wd_unresolved,
            "ptt_tekrar_ayiklanan": stats["birim_tekrar_ayiklanan"],
            "capraz_kontrol_epigra": stats["capraz_kontrol_epigra"],
            "semt_siniflandirma": stats["semt_siniflandirma"],
            "alias_6360": n6360,
            "kuratorlu_semt": stats["kuratorlu_semt"],
        },
    }
    with open(STAGING / "SOURCES.json", "w", encoding="utf-8", newline="\n") as f:
        json.dump(sources, f, ensure_ascii=False, indent=2)
        f.write("\n")

    print(json.dumps({k: sources[k] for k in ("ciktilar", "birim_tur_dagilimi", "alias_dagilimi", "kapsam")},
                     ensure_ascii=False, indent=1))
    print(json.dumps(sources["dogrulama"], ensure_ascii=False, indent=1)[:6000])
    if problems:
        print("DOĞRULAMA HATASI:", problems)
        sys.exit(1)
    print("OK")


if __name__ == "__main__":
    main()
