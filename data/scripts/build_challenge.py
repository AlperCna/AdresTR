"""Challenge set builder: eval/challenge/challenge.jsonl.

Run (from the repository root):

    python -I data/scripts/build_challenge.py          # validate + write
    python -I data/scripts/build_challenge.py --check  # validate only, compare with the file on disk

The cases live in `challenge_cases.py` (same folder). Each case is a list of segments
`(text, label | None)` (a bare string is shorthand for an unlabeled segment) plus gold
information. This script:

1. concatenates the segments into `text` and computes span offsets in UTF-16 code units
   (asserting there are no astral characters, so Python indices == UTF-16 offsets);
2. resolves `adm=(il, ilce, birim)` names to AdresTR gazetteer ids from `data/staging/`;
3. derives `gold.fields` from the spans and resolved ids (overridable per case);
4. validates everything (labels, tags, non-empty spans, hierarchy, name/span consistency,
   postal codes, all label keys present) and writes JSONL (UTF-8, LF).

Exit code is non-zero on any validation error.
"""
from __future__ import annotations

import csv
import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from common import fold  # noqa: E402
import challenge_cases  # noqa: E402

REPO = HERE.parents[1]
STAGING = REPO / "data" / "staging"
OUT = REPO / "eval" / "challenge" / "challenge.jsonl"

LABELS = ["il", "ilce", "mahalle", "semt", "csbm_tur", "csbm_ad", "site", "blok", "dis_kapi",
          "kat", "daire", "posta_kodu", "tarif", "diger"]
TAGS = {"semt", "historic-name", "ambiguous-name", "merkez", "numbered-street", "slash-door", "glued",
        "broken-i", "ascii", "typo", "missing-ilce", "missing-il", "reordered", "phone", "landmark",
        "site-blok", "osb", "kume-evler", "koy", "postal-code", "abbreviation", "uppercase",
        "duplicate-token", "d-k-ambiguity"}
# Tags that explain why a span's text does not fold to the official name.
NONMATCH_TAGS = {"typo", "broken-i", "glued", "abbreviation", "historic-name"}

CSBM_TUR = {
    "cadde": ["cd", "cad", "cadde", "caddesi", "cadessi", "cadesi", "caddee", "cadd", "cddesi"],
    "sokak": ["sk", "sok", "sokak", "sokagi", "sokaği", "sokağı", "sokar", "sokk", "sokah"],
    "bulvar": ["blv", "bulv", "bul", "bulvar", "bulvari", "bulvarı", "bulvr", "bulvarr"],
    "meydan": ["meydan", "meydani", "meydanı", "mey"],
    "kume_evler": ["kume evler", "kume evleri", "kumeevler", "kumeevleri", "kume evl", "k evleri"],
    "cikmaz": ["cikmaz", "cikmazi", "çıkmaz", "çıkmazı", "ckm"],
    "yol": ["yol", "yolu", "karayolu"],
}
CSBM_LOOKUP = {}
for _tur, _forms in CSBM_TUR.items():
    for _f in _forms:
        CSBM_LOOKUP[re.sub(r"\s+", " ", fold(_f, strip_suffix=False))] = _tur

KAT_WORDS = {"zemin": "0", "giris": "0", "bodrum": "-1"}


def key(s: str) -> str:
    """Gazetteer.Key approximation: case, diacritics, spaces and . - ' / ignored."""
    return fold(s, strip_suffix=False).replace(" ", "")


# ---------------------------------------------------------------- gazetteer
def load_staging():
    def rows(name):
        with open(STAGING / name, encoding="utf-8", newline="") as f:
            return list(csv.DictReader(f))

    il = {int(r["plaka"]): r for r in rows("il.csv")}
    ilce = {int(r["ilce_id"]): r for r in rows("ilce.csv")}
    birim = {int(r["birim_id"]): r for r in rows("birim.csv")}
    alias = rows("alias.csv")
    return il, ilce, birim, alias


IL, ILCE, BIRIM, ALIAS = load_staging()
IL_BY_KEY = defaultdict(list)
for _p, _r in IL.items():
    IL_BY_KEY[key(_r["ad"])].append(_p)
ILCE_BY_KEY = defaultdict(list)
for _i, _r in ILCE.items():
    ILCE_BY_KEY[key(_r["ad"])].append(_i)
BIRIM_BY_KEY = defaultdict(list)
for _b, _r in BIRIM.items():
    BIRIM_BY_KEY[key(_r["ad"])].append(_b)
ALIAS_BY_KEY = defaultdict(list)  # key(alias) -> [(hedef, id, tur)]
for _a in ALIAS:
    ALIAS_BY_KEY[key(_a["alias"])].append((_a["hedef"], int(_a["hedef_id"]), _a["tur"]))


def ilce_plaka(i: int) -> int:
    return int(ILCE[i]["plaka"])


def birim_ilce(b: int) -> int:
    return int(BIRIM[b]["ilce_id"])


def resolve_il(name: str) -> int:
    c = IL_BY_KEY.get(key(name), [])
    c += [h for (t, h, _) in ALIAS_BY_KEY.get(key(name), []) if t == "il"]
    c = sorted(set(c))
    if len(c) != 1:
        raise ValueError(f"il {name!r}: {len(c)} matches")
    return c[0]


def resolve_ilce(name: str, plaka: int) -> int:
    c = [i for i in ILCE_BY_KEY.get(key(name), []) if ilce_plaka(i) == plaka]
    c += [h for (t, h, _) in ALIAS_BY_KEY.get(key(name), []) if t == "ilce" and ilce_plaka(h) == plaka]
    c = sorted(set(c))
    if len(c) != 1:
        raise ValueError(f"ilce {name!r} in il {plaka}: {len(c)} matches")
    return c[0]


def resolve_birim(spec: str, ilce_id: int) -> int:
    """spec = 'Name' or 'Name|tur=koy|ust=Kepçeli|pk=12502' (ust= empty means no parent)."""
    parts = spec.split("|")
    name, quals = parts[0], dict(p.split("=", 1) for p in parts[1:])
    c = [b for b in BIRIM_BY_KEY.get(key(name), []) if birim_ilce(b) == ilce_id]
    for q, v in quals.items():
        if q == "tur":
            c = [b for b in c if BIRIM[b]["tur"] == v]
        elif q == "ust":
            c = [b for b in c if key(BIRIM[b]["ust_ad"]) == key(v)]
        elif q == "pk":
            c = [b for b in c if BIRIM[b]["posta_kodu"] == v]
        else:
            raise ValueError(f"unknown qualifier {q}")
    if len(c) != 1:
        raise ValueError(f"birim {spec!r} in ilce {ilce_id} ({ILCE[ilce_id]['ad']}): {len(c)} matches "
                         f"{[(b, BIRIM[b]['tur'], BIRIM[b]['ust_ad']) for b in c]}")
    return c[0]


# ---------------------------------------------------------------- building
class CaseError(Exception):
    pass


def build_case(idx: int, case: dict, warnings: list[str]) -> dict:
    cid = f"chal-{idx:04d}"
    segs = case["seg"]
    text, spans = "", []
    for s in segs:
        if isinstance(s, str):
            piece, label = s, None
        else:
            piece, label = s
        if label is not None:
            if label not in LABELS:
                raise CaseError(f"{cid}: unknown label {label!r}")
            if not piece or piece != piece.strip():
                raise CaseError(f"{cid}: span {piece!r} empty or has outer whitespace")
            spans.append({"start": len(text), "end": len(text) + len(piece), "label": label})
        text += piece
    if any(ord(ch) > 0xFFFF for ch in text):
        raise CaseError(f"{cid}: astral character in text (UTF-16 offsets would differ)")
    if "\n" in text or "\r" in text or text != text.strip() or not text:
        raise CaseError(f"{cid}: text empty, multi-line or has outer whitespace: {text!r}")
    if not spans:
        raise CaseError(f"{cid}: no spans")

    by_label = defaultdict(list)
    for sp in spans:
        by_label[sp["label"]].append(text[sp["start"]:sp["end"]])

    # --- ids
    il_name, ilce_name, birim_spec = case["adm"]
    il_id = resolve_il(il_name) if il_name else None
    ilce_id = resolve_ilce(ilce_name, il_id) if ilce_name else None
    birim_id = resolve_birim(birim_spec, ilce_id) if birim_spec else None
    if ilce_id is not None and il_id is None:
        raise CaseError(f"{cid}: ilce without il")
    if birim_id is not None and ilce_id is None:
        raise CaseError(f"{cid}: birim without ilce")
    if ilce_id is not None and ilce_plaka(ilce_id) != il_id:
        raise CaseError(f"{cid}: ilce {ilce_id} not in il {il_id}")
    if birim_id is not None and birim_ilce(birim_id) != ilce_id:
        raise CaseError(f"{cid}: birim {birim_id} not in ilce {ilce_id}")

    tags = case["tags"]
    if not tags or any(t not in TAGS for t in tags) or len(set(tags)) != len(tags):
        raise CaseError(f"{cid}: bad tags {tags}")
    nonmatch_ok = bool(NONMATCH_TAGS & set(tags)) or case.get("nonmatch_ok", False)
    overrides = case.get("fields", {})
    for k in overrides:
        if k not in LABELS:
            raise CaseError(f"{cid}: unknown field override {k}")

    # --- fields
    fields = {}
    for lab in LABELS:
        texts = by_label.get(lab, [])
        if lab in overrides:
            fields[lab] = overrides[lab]
            if fields[lab] is not None and not texts and lab not in ("csbm_tur",):
                raise CaseError(f"{cid}: field {lab} overridden but no span")
            continue
        if not texts:
            fields[lab] = None
            continue
        if lab == "il":
            if il_id is None:
                raise CaseError(f"{cid}: il span without il id (override needed)")
            fields[lab] = IL[il_id]["ad"]
        elif lab == "ilce":
            if ilce_id is not None:
                fields[lab] = ILCE[ilce_id]["ad"]
            else:
                names = {ILCE[i]["ad"] for t in texts for i in ILCE_BY_KEY.get(key(t), [])}
                if len(names) != 1:
                    raise CaseError(f"{cid}: ilce span {texts} without id: candidates {names}")
                fields[lab] = names.pop()
        elif lab == "mahalle":
            if birim_id is not None:
                fields[lab] = BIRIM[birim_id]["ad"]
            else:
                cands = [b for t in texts for b in BIRIM_BY_KEY.get(key(t), [])]
                if ilce_id is not None:
                    cands = [b for b in cands if birim_ilce(b) == ilce_id]
                elif il_id is not None:
                    cands = [b for b in cands if ilce_plaka(birim_ilce(b)) == il_id]
                names = Counter(BIRIM[b]["ad"] for b in cands).most_common()
                if not names or (len(names) > 1 and names[0][1] == names[1][1]):
                    raise CaseError(f"{cid}: mahalle span {texts} without id: candidates {names}")
                fields[lab] = names[0][0]  # most frequent official spelling
        else:
            if len(set(texts)) != 1:
                raise CaseError(f"{cid}: {len(texts)} different {lab} spans {texts}: override needed")
            t = texts[0]
            if lab == "csbm_tur":
                k = re.sub(r"\s+", " ", fold(t.rstrip("."), strip_suffix=False))
                if k not in CSBM_LOOKUP:
                    raise CaseError(f"{cid}: unknown csbm_tur form {t!r}")
                fields[lab] = CSBM_LOOKUP[k]
            elif lab in ("dis_kapi", "blok"):
                fields[lab] = re.sub(r"\s+", "", t)
            elif lab == "kat":
                fields[lab] = KAT_WORDS.get(key(t), t)
                if not re.fullmatch(r"-?\d+", fields[lab]):
                    raise CaseError(f"{cid}: kat {t!r} not numeric")
            elif lab == "posta_kodu":
                if not re.fullmatch(r"\d{5}", t):
                    raise CaseError(f"{cid}: posta_kodu {t!r}")
                fields[lab] = t
            else:  # semt, csbm_ad, site, daire, tarif, diger
                fields[lab] = re.sub(r"\s+", " ", t).strip()

    # --- consistency checks
    if fields["csbm_tur"] is not None and fields["csbm_tur"] not in CSBM_TUR:
        raise CaseError(f"{cid}: csbm_tur {fields['csbm_tur']}")
    for lab, ref in (("il", IL[il_id]["ad"] if il_id else None),
                     ("ilce", ILCE[ilce_id]["ad"] if ilce_id else None),
                     ("mahalle", BIRIM[birim_id]["ad"] if birim_id else None)):
        for t in by_label.get(lab, []):
            if ref is None:
                continue
            ok = key(t) == key(ref)
            if not ok:  # alias match (semt aliases excluded: those must be labeled semt)
                tid = {"il": il_id, "ilce": ilce_id, "mahalle": birim_id}[lab]
                hed = {"il": "il", "ilce": "ilce", "mahalle": "birim"}[lab]
                ok = any(h == hed and i == tid and tur != "semt" for (h, i, tur) in ALIAS_BY_KEY.get(key(t), []))
            if not ok and not nonmatch_ok:
                raise CaseError(f"{cid}: {lab} span {t!r} does not fold to {ref!r} (add typo/glued/... tag)")
            if not ok and "typo" in tags and lab == "ilce" and key(t) in ILCE_BY_KEY:
                warnings.append(f"{cid}: typo'd ilce {t!r} is itself a real ilce name")
    # null birim but resolvable mahalle span in known ilce -> suspicious
    if birim_id is None and ilce_id is not None and not case.get("allow_null", False):
        for t in by_label.get("mahalle", []):
            c = [b for b in BIRIM_BY_KEY.get(key(t), []) if birim_ilce(b) == ilce_id]
            if len(c) == 1:
                raise CaseError(f"{cid}: mahalle {t!r} resolves uniquely in ilce but birim is null")
    if ilce_id is None and il_id is not None and not case.get("allow_null", False):
        for t in by_label.get("ilce", []):
            c = [i for i in ILCE_BY_KEY.get(key(t), []) if ilce_plaka(i) == il_id]
            if c:
                raise CaseError(f"{cid}: ilce {t!r} resolves in il but ilce is null")
        for t in by_label.get("mahalle", []):
            c = [b for b in BIRIM_BY_KEY.get(key(t), []) if ilce_plaka(birim_ilce(b)) == il_id]
            if len(c) == 1:
                raise CaseError(f"{cid}: mahalle {t!r} unique in il but ilce is null")
    # postal code
    pk = fields["posta_kodu"]
    if pk is not None and il_id is not None and not case.get("pk_conflict", False):
        if int(pk[:2]) != il_id:
            raise CaseError(f"{cid}: posta_kodu {pk} not in il {il_id}")
        if birim_id is not None and BIRIM[birim_id]["posta_kodu"] and BIRIM[birim_id]["posta_kodu"] != pk:
            raise CaseError(f"{cid}: posta_kodu {pk} != birim pk {BIRIM[birim_id]['posta_kodu']}")
    if case.get("pk_conflict") and (pk is None or il_id is None or int(pk[:2]) == il_id):
        raise CaseError(f"{cid}: pk_conflict flag without a conflicting code")
    # semt checks
    for t in by_label.get("semt", []):
        targets = [i for (h, i, tur) in ALIAS_BY_KEY.get(key(t), []) if h == "birim" and tur == "semt"]
        if ilce_id is not None:
            targets = [b for b in targets if birim_ilce(b) == ilce_id]
        if not targets:
            warnings.append(f"{cid}: semt {t!r} not in alias.csv (birim must come from elsewhere)")
            if birim_id is not None and not by_label.get("mahalle"):
                raise CaseError(f"{cid}: undocumented semt {t!r} but birim set")
        elif len(targets) == 1 and not by_label.get("mahalle") and birim_id != targets[0]:
            raise CaseError(f"{cid}: semt {t!r} maps uniquely to {targets[0]} but birim={birim_id}")
        elif len(targets) > 1 and not by_label.get("mahalle") and birim_id is not None \
                and pk is None:
            raise CaseError(f"{cid}: semt {t!r} maps to {len(targets)} units but birim set")
    # structural tags must agree with the spans
    for tag, lab in (("missing-il", "il"), ("missing-ilce", "ilce")):
        if (tag in tags) == bool(by_label.get(lab)):
            raise CaseError(f"{cid}: tag {tag} inconsistent with {lab} spans")
    for tag, lab in (("postal-code", "posta_kodu"), ("landmark", "tarif")):
        if (tag in tags) != bool(by_label.get(lab)):
            raise CaseError(f"{cid}: tag {tag} inconsistent with {lab} spans")
    if "phone" in tags and not by_label.get("diger"):
        raise CaseError(f"{cid}: phone tag without diger span")
    if (by_label.get("site") or by_label.get("blok")) and "site-blok" not in tags:
        raise CaseError(f"{cid}: site/blok span without site-blok tag")
    if "uppercase" in tags:
        letters = [ch for ch in text if ch.isalpha()]
        if sum(ch.isupper() for ch in letters) < 0.5 * len(letters):
            raise CaseError(f"{cid}: uppercase tag but mostly lower-case text")
    if not case.get("note"):
        raise CaseError(f"{cid}: note missing")

    split = "dev" if idx % 3 == 0 else "test"
    row = {
        "id": cid,
        "text": text,
        "source": "challenge",
        "license": "CC0-1.0",
        "split": split,
        "spans": spans,
        "gold": {"il": il_id, "ilce": ilce_id, "birim": birim_id, "fields": fields},
        "tags": tags,
        "note": case["note"],
    }
    return row


def main(argv: list[str]) -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    check_only = "--check" in argv
    warnings: list[str] = []
    rows, errors = [], []
    seen_text = {}
    for i, case in enumerate(challenge_cases.CASES, start=1):
        try:
            row = build_case(i, case, warnings)
        except (CaseError, ValueError) as e:
            errors.append(f"case #{i}: {e}")
            continue
        if row["text"] in seen_text:
            errors.append(f"{row['id']}: duplicate text of {seen_text[row['text']]}")
        seen_text[row["text"]] = row["id"]
        rows.append(row)
    for w in warnings:
        print("WARN", w)
    if errors:
        for e in errors:
            print("ERROR", e)
        print(f"{len(errors)} error(s)")
        return 1

    data = "".join(json.dumps(r, ensure_ascii=False) + "\n" for r in rows)
    tag_counts = Counter(t for r in rows for t in r["tags"])
    split_counts = Counter(r["split"] for r in rows)
    print(f"{len(rows)} rows; splits {dict(split_counts)}")
    for t in sorted(TAGS):
        print(f"  {t:16s} {tag_counts.get(t, 0)}")
    if check_only:
        same = OUT.exists() and OUT.read_text(encoding="utf-8") == data
        print("up to date" if same else "OUT OF DATE")
        return 0 if same else 1
    OUT.parent.mkdir(parents=True, exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(data)
    print(f"wrote {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
