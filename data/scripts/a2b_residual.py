"""a2 sonrası: eşleşmeyen NVİ/PTT kayıtları arasında bulanık eşleşme + PTT 2022/2024 posta kodu farkı."""
import csv
import difflib
import sys
from collections import Counter, defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import *  # noqa
from a2_mahalle import nvi_key, parse_ptt, TUR  # noqa

sys.stdout.reconfigure(encoding="utf-8")

iller, ilceler, nvi = load_nvi()
flags = {int(r["kimlikNo"]): r["ptt_eslesme"] for r in csv.DictReader(open(DERIVED / "mahalle_nvi_vs_ptt.csv", encoding="utf-8"))}
ptt = list(csv.DictReader(open(DERIVED / "ptt_parsed_murat.csv", encoding="utf-8")))

miss_nvi = defaultdict(list)
for m in nvi:
    if flags[m["kimlikNo"]] == "yok":
        kind, name, parent = nvi_key(m)
        miss_nvi[(m["il_id"], fold(m["_ilce"]))].append((fold(name), m))
miss_ptt = defaultdict(list)
for p in ptt:
    if p["nvi_eslesme"] == "yok":
        miss_ptt[(int(p["plaka"]), p["ilce_fold"])].append((p["name_fold"], p))

fuzzy_pairs = []
for key, lst in miss_nvi.items():
    cands = miss_ptt.get(key, [])
    names = [c[0] for c in cands]
    for n, m in lst:
        best = difflib.get_close_matches(n, names, n=1, cutoff=0.8)
        if best:
            fuzzy_pairs.append((m, best[0], difflib.SequenceMatcher(None, n, best[0]).ratio()))

n_nvi_miss = sum(len(v) for v in miss_nvi.values())
n_ptt_miss = sum(len(v) for v in miss_ptt.values())
print("Eşleşmeyen NVİ:", n_nvi_miss, "| eşleşmeyen PTT:", n_ptt_miss)
print("Aynı ilçede eşleşmeyen PTT adıyla bulanık eşleşen (ratio>=0.8) NVİ:", len(fuzzy_pairs))
print("  mahalleTur kırılımı:", Counter(TUR[m["mahalleTur"]] for m, _, _ in fuzzy_pairs))
print("  örnekler:")
for m, b, r in fuzzy_pairs[:40]:
    print(f"    {m['_il']}/{m['_ilce']}: NVİ '{m['adi'] or m['koyAdi']}' ~ PTT '{b}' ({r:.2f})")

# eşleşmeyen NVİ'nin il dağılımı
c = Counter(m["_il"] for v in miss_nvi.values() for _, m in v)
print("\nEşleşmeyen NVİ, en çok il:", c.most_common(10))
c2 = Counter(int(p["plaka"]) for v in miss_ptt.values() for _, p in v)
print("Eşleşmeyen PTT, en çok plaka:", c2.most_common(10))

# Hatay (deprem sonrası) / Kahramanmaraş kontrolü
for il in ("HATAY", "KAHRAMANMARAŞ"):
    tot = sum(1 for m in nvi if m["_il"] == il and m["mahalleTur"] == 1)
    miss = sum(1 for v in miss_nvi.values() for _, m in v if m["_il"] == il and m["mahalleTur"] == 1)
    print(f"{il}: tip1 {tot}, PTT'de yok {miss}")

# PTT 2022 vs 2024 posta kodu farkı
mu = load_murat()
ep, *_ = load_epigra()
km = {(int(r[0]), fold(r[2]), r[3]): r[4] for r in mu}
il_plate = {fold(i["adi"]): i["kimlikNo"] for i in iller}
ke = {(il_plate[fold(r[0])], fold(r[1]), r[3]): r[4] for r in ep}
common_keys = set(km) & set(ke)
print("\nPTT 2022↔2024 ortak satır:", len(common_keys), "| posta kodu farklı:", sum(1 for k in common_keys if km[k] != ke[k]))
print("yalnız 2024:", [k for k in km if k not in ke][:5], "| yalnız 2022:", [k for k in ke if k not in km][:5])
