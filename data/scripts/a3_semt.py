"""PTT 'semt_bucak_belde' sütunu (epigra 'districts') ne içeriyor? + posta kodu ilişkisi."""
import sys
from collections import Counter, defaultdict
from pathlib import Path
from statistics import median

sys.path.insert(0, str(Path(__file__).parent))
from common import *  # noqa

sys.stdout.reconfigure(encoding="utf-8")

ep, ci, co, di = load_epigra()
iller, ilceler, nvi = load_nvi()

semt_by_ilce = defaultdict(set)
mah_by_semt = defaultdict(list)
pk_by_semt = defaultdict(set)
semt_by_pk = defaultdict(set)
for il, ilce, semt, mah, pk in ep:
    semt_by_ilce[(il, ilce)].add(semt)
    mah_by_semt[(il, ilce, semt)].append(mah)
    pk_by_semt[(il, ilce, semt)].add(pk)
    semt_by_pk[pk].add((il, ilce, semt))

n_semt = len(mah_by_semt)
print("Distinct (il, ilçe, semt):", n_semt, "| distinct posta kodu:", len(semt_by_pk))
per = sorted(len(v) for v in semt_by_ilce.values())
print("İlçe başına semt: min", per[0], "medyan", median(per), "maks", per[-1], "| tek semtli ilçe:", sum(1 for x in per if x == 1))
print("Semt başına posta kodu:", Counter(len(v) for v in pk_by_semt.values()))
print("Posta kodu başına semt:", Counter(len(v) for v in semt_by_pk.values()))
print("Semt başına mahalle: medyan", median(len(v) for v in mah_by_semt.values()), "maks", max(len(v) for v in mah_by_semt.values()))

# semt adı = ilçe adı?
same = sum(1 for (il, ilce, s) in mah_by_semt if fold(s) == fold(ilce))
merkez = sum(1 for (il, ilce, s) in mah_by_semt if fold(s) == "merkez")
print("Semt adı == ilçe adı:", same, "| semt adı 'Merkez':", merkez)
# semt adı bir NVİ belde adı mı?
belde = {(fold(m["_il"]), fold(m["_ilce"]), fold(m["koyAdi"])) for m in nvi if m["koyKurumBelediyeTur"] == 4}
koy = {(fold(m["_il"]), fold(m["_ilce"]), fold(m["koyAdi"])) for m in nvi if m["koyAdi"] != "MERKEZ"}
is_belde = sum(1 for (il, ilce, s) in mah_by_semt if (fold(il), fold(ilce), fold(s)) in belde)
is_koy = sum(1 for (il, ilce, s) in mah_by_semt if (fold(il), fold(ilce), fold(s)) in koy)
nvi_mah = {(fold(m["_il"]), fold(m["_ilce"]), fold(m["adi"])) for m in nvi if m["mahalleTur"] == 1 and m["adi"]}
is_mah = sum(1 for (il, ilce, s) in mah_by_semt if (fold(il), fold(ilce), fold(s)) in nvi_mah)
print("Semt adı NVİ belde adıyla aynı:", is_belde, "| bir köy adıyla aynı:", is_koy, "| bir mahalle adıyla aynı:", is_mah)
suffix = Counter(s.split(" ")[-1] for (_, _, s) in mah_by_semt)
print("Semt adlarının son kelimesi (ilk 15):", suffix.most_common(15))

# Örnek ilçeler
for il, ilce in (("İstanbul", "Kadıköy"), ("İstanbul", "Beşiktaş"), ("Ankara", "Çankaya"), ("İzmir", "Konak"), ("İzmir", "Bornova"), ("İstanbul", "Şişli")):
    ss = sorted(semt_by_ilce.get((il, ilce), []))
    print(f"\n{il}/{ilce}: {len(ss)} semt")
    for s in ss:
        print(f"   [{s}] pk={sorted(pk_by_semt[(il, ilce, s)])} -> {mah_by_semt[(il, ilce, s)][:12]}{' ...' if len(mah_by_semt[(il, ilce, s)]) > 12 else ''}")

# Aranan semtler herhangi bir yerde semt sütununda geçiyor mu?
targets = ["moda", "levent", "bostanci", "kizilay", "alsancak", "bornova merkez", "kocamustafapasa", "bahariye", "nisantasi", "etiler"]
allsemt = defaultdict(list)
for (il, ilce, s) in mah_by_semt:
    allsemt[fold(s)].append((il, ilce, s))
allmah = defaultdict(list)
for il, ilce, s, m, pk in ep:
    allmah[fold(m)].append((il, ilce, s, m))
print()
for t in targets:
    print(f"'{t}': semt sütununda {allsemt.get(t, [])} | mahalle olarak {[(x[1], x[3]) for x in allmah.get(t, [])][:6]}")

# Sınıflandırma
cls = Counter()
alias_rows = []
for (il, ilce, s), mahs in mah_by_semt.items():
    fs = fold(s)
    k = (fold(il), fold(ilce), fs)
    if fs == fold(ilce):
        c = "ilçe adı (merkez posta bölgesi)"
    elif fs.endswith("merkezkoyler") or fs.endswith("koyler"):
        c = "…Merkezköyler/Köyler (kırsal toplu)"
    elif k in belde:
        c = "belde adı"
    elif fs in {fold(m) for m in mahs}:
        c = "kendi mahallelerinden birinin adı"
    elif k in koy:
        c = "köy adı"
    else:
        c = "bağımsız semt adı (gerçek alias adayı)"
    cls[c] += 1
    if c in ("bağımsız semt adı (gerçek alias adayı)", "kendi mahallelerinden birinin adı"):
        alias_rows.append((il, ilce, s, c, len(mahs), "|".join(mahs)))
print("\nSemt sınıflandırması:", cls.most_common())
write_csv(DERIVED / "ptt_semt_alias_adaylari.csv", ["il", "ilce", "semt", "sinif", "mahalle_sayisi", "mahalleler"], alias_rows)
print("Örnek bağımsız semt:", [(r[1], r[2], r[4]) for r in alias_rows if r[3].startswith("bağımsız")][:40])
