"""Proposes semt -> mahalle aliases from Turkish Wikipedia semt articles (CC BY-SA text; only facts are extracted).

For each big city the category "<Il> semtleri" is read; for every article the intro must (a) name exactly one ilce of
that il and (b) name at least one official mahalle of that ilce ("X Mahallesi"). The semt itself must not be an official
mahalle name of that ilce. Output: data/curated/semt_alias.proposed.csv (review, then merge into semt_alias.csv).

Run: python -I data/scripts/build_semt_from_wikipedia.py
"""
import csv
import json
import re
import sys
import time
import urllib.parse
import urllib.request
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import fold  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
UA = "AdresTR-data/0.1 (+https://github.com/AlperCna/AdresTR)"
API = "https://tr.wikipedia.org/w/api.php"
CITIES = ["İstanbul", "Ankara", "İzmir", "Bursa", "Antalya", "Adana", "Konya", "Gaziantep", "Kocaeli", "Mersin",
          "Eskişehir", "Kayseri", "Samsun", "Trabzon", "Diyarbakır", "Denizli", "Muğla", "Tekirdağ", "Balıkesir", "Manisa"]


def api(**params):
    params.update(format="json", formatversion="2")
    url = API + "?" + urllib.parse.urlencode(params)
    for attempt in range(3):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": UA}), timeout=30) as r:
                return json.load(r)
        except Exception:
            time.sleep(1 + attempt)
    return {}


def members(title):
    out, cont = [], {}
    while True:
        d = api(action="query", list="categorymembers", cmtitle=title, cmlimit="500", **cont)
        out += d.get("query", {}).get("categorymembers", [])
        if "continue" not in d:
            return out
        cont = {"cmcontinue": d["continue"]["cmcontinue"]}


def rows(name):
    with open(ROOT / "data/staging" / name, encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f))


il_by_plaka = {int(r["plaka"]): r["ad"] for r in rows("il.csv")}
ilce_by_il = defaultdict(list)
for r in rows("ilce.csv"):
    ilce_by_il[il_by_plaka[int(r["plaka"])]].append(r)
units_by_ilce = defaultdict(list)
for r in rows("birim.csv"):
    if r["tur"] in ("mahalle", "osb"):
        units_by_ilce[r["ilce_id"]].append(r)
existing = {(a["hedef_id"], fold(a["alias"])) for a in rows("alias.csv") if a["hedef"] == "birim"}
curated = {(r["il"], r["ilce"], fold(r["semt"])) for r in csv.DictReader(open(ROOT / "data/curated/semt_alias.csv", encoding="utf-8"))}

proposed = []
for il in CITIES:
    pages = {}
    for suffix in ("'in", "'ın", "'un", "'ün", "'nin", "'nın", "'nun", "'nün"):
        for m in members(f"Kategori:{il}{suffix} semtleri"):
            if m["ns"] == 0:
                pages[m["title"]] = m["pageid"]
            elif m["ns"] == 14 and "semt" in m["title"].lower():
                for sub in members(m["title"]):
                    if sub["ns"] == 0:
                        pages[sub["title"]] = sub["pageid"]
    titles = list(pages)
    for i in range(0, len(titles), 20):
        d = api(action="query", prop="extracts", exintro="1", explaintext="1", exlimit="20", titles="|".join(titles[i:i + 20]))
        for page in d.get("query", {}).get("pages", []):
            text = page.get("extract") or ""
            title = re.sub(r"\s*\(.*?\)", "", page["title"]).strip()
            if not text:
                continue
            first = text.split("\n")[0]
            ilceler = [r for r in ilce_by_il[il] if re.search(r"(?<!\w)" + re.escape(r["ad"]) + r"(?!\w)", first) and fold(r["ad"]) != fold(title)]
            if len({r["ilce_id"] for r in ilceler}) != 1:
                continue
            ilce = ilceler[0]
            official = {fold(u["ad"]): u for u in units_by_ilce[ilce["ilce_id"]]}
            if fold(title) in official:
                continue
            # Evidence: a sentence of the full article that names the semt and an official mahalle of the ilce together.
            full = api(action="query", prop="extracts", explaintext="1", titles=page["title"])
            body = (full.get("query", {}).get("pages", [{}])[0].get("extract") or first)
            found, evidence = [], ""
            for sent in re.split(r"(?<=[.!?])\s+", body.replace("
", " ")):
                if fold(title.split(",")[0]) not in fold(sent) or "Mahalle" not in sent:
                    continue
                for m in re.finditer(r"([A-ZÇĞİÖŞÜ][\w’'\.\- ]{1,40}?)\s+Mahalle", sent):
                    name = m.group(1).split(",")[-1].split(" ve ")[-1].strip()
                    u = official.get(fold(name))
                    if u and u["ad"] not in found:
                        found.append(u["ad"])
                        evidence = evidence or sent
            if not found or (ilce["ilce_id"], fold(title)) in existing:
                continue
            if (il, ilce["ad"], fold(title)) in curated:
                continue
            sentence = evidence[:240]
            proposed.append({
                "il": il, "ilce": ilce["ad"], "semt": title, "mahalleler": "|".join(found[:3]),
                "kaynak": "https://tr.wikipedia.org/wiki/" + urllib.parse.quote(page["title"].replace(" ", "_")),
                "not": sentence.replace("\n", " "),
            })
    print(il, len(titles), "pages ->", sum(1 for p in proposed if p["il"] == il), "proposals", flush=True)

out = ROOT / "data/curated/semt_alias.proposed.csv"
with open(out, "w", encoding="utf-8", newline="") as f:
    w = csv.DictWriter(f, ["il", "ilce", "semt", "mahalleler", "kaynak", "not"], lineterminator="\n")
    w.writeheader()
    w.writerows(sorted(proposed, key=lambda r: (r["il"], r["ilce"], r["semt"])))
print("wrote", out, len(proposed))
