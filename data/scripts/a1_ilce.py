"""İl/ilçe düzeyi: kaynaklar arası sayım ve isim uyuşmazlıkları."""
import sys
from collections import Counter, defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import *  # noqa

sys.stdout.reconfigure(encoding="utf-8")

iller, ilceler, mah = load_nvi()
mu = load_murat()
ep, ep_ci, ep_co, ep_di = load_epigra()

plate_by_ilname = {fold(i["adi"]): i["kimlikNo"] for i in iller}

# --- il
print("== İL")
print("NVİ il:", len(iller), "| murat il:", len({r[0] for r in mu}), "| epigra il:", len(ep_ci))
mu_il = {int(r[0]): r[1] for r in mu}
for i in iller:
    if fold(i["adi"]) != fold(mu_il[i["kimlikNo"]]):
        print("  il isim farkı NVİ/murat:", i["adi"], mu_il[i["kimlikNo"]])
for c in ep_ci.values():
    if plate_by_ilname.get(fold(c["name"])) != c["id"]:
        print("  epigra city id != plaka:", c)

# --- ilçe kümeleri: (plaka, fold(ilçe))
nvi_ilce = {(i["il_id"], fold(i["adi"])): i["adi"] for i in ilceler}
mu_ilce = {(int(r[0]), fold(r[2])): r[2] for r in mu}
ep_ilce = {}
for c in ep_co.values():
    ep_ilce[(ep_ci[c["city_id"]]["id"], fold(c["name"]))] = c["name"]

print("\n== İLÇE sayıları")
print("NVİ:", len(nvi_ilce), "murat:", len(mu_ilce), "epigra:", len(ep_ilce))

# 'Merkez' ilçeleri
il_name = {i["kimlikNo"]: i["adi"] for i in iller}
mu_merkez = sorted(k for k in mu_ilce if k[1] == "merkez")
ep_merkez = sorted(k for k in ep_ilce if k[1] == "merkez")
print("PTT 'Merkez' ilçe sayısı: murat", len(mu_merkez), "epigra", len(ep_merkez))


def resolve(k):
    """NVİ de merkez ilçeleri 'MERKEZ' adıyla tutuyor; dönüşüm gerekmiyor."""
    return k


def compare(name, src):
    res = {resolve(k): v for k, v in src.items()}
    only_src = sorted(set(res) - set(nvi_ilce))
    only_nvi = sorted(set(nvi_ilce) - set(res))
    print(f"\n-- {name} vs NVİ (plaka + fold(ilçe) anahtarı)")
    print(f"   yalnız {name}:", [(il_name[k[0]], res[k]) for k in only_src])
    print("   yalnız NVİ:", [(il_name[k[0]], nvi_ilce[k]) for k in only_nvi])
    # merkez olup NVİ'de il adı ile eşleşmeyenler
    return only_src, only_nvi


compare("murat", mu_ilce)
compare("epigra", ep_ilce)

# murat vs epigra raw
print("\n-- murat vs epigra (ham)")
print("   yalnız murat:", sorted(set(mu_ilce) - set(ep_ilce)))
print("   yalnız epigra:", sorted(set(ep_ilce) - set(mu_ilce)))

# Yazım varyantları: aynı fold ama farklı ham yazım
print("\n-- NVİ ilçe adları: rakam / boşluk / tire içerenler")
for k, v in sorted(nvi_ilce.items()):
    if any(ch.isdigit() for ch in v) or "-" in v or " " in v:
        print("  ", il_name[k[0]], v, "| murat:", mu_ilce.get(k), "| epigra:", ep_ilce.get(k))

# Merkez ilçe = il adı olan NVİ ilçeleri
nvi_merkez = [k for k in nvi_ilce if k[1] == fold(il_name[k[0]])]
print("\nNVİ'de il adıyla aynı adı taşıyan ilçe:", len(nvi_merkez))
print("NVİ'de 'MERKEZ' adlı ilçe:", sum(1 for k in nvi_ilce if k[1] == "merkez"))
for k, v in nvi_ilce.items():
    for nm, src in (("murat", mu_ilce), ("epigra", ep_ilce)):
        if k in src and tr_lower(src[k]) != tr_lower(v):
            print("   yazım farkı (case-insensitive)", nm, v, src[k])
# büyükşehir: 30 il, merkez ilçe yok
bsehir = [il_name[p] for p in il_name if (p, "merkez") not in nvi_ilce]
print("MERKEZ ilçesi olmayan il (büyükşehir):", len(bsehir))

# ilçe adı = il adı (başka il)
il_folds = {fold(v): v for v in il_name.values()}
cross = [(il_name[k[0]], v) for k, v in nvi_ilce.items() if k[1] in il_folds and k[1] != fold(il_name[k[0]])]
print("\nBaşka bir ilin adını taşıyan ilçe:", cross)

# ilçe adı birden fazla ilde
cnt = Counter(k[1] for k in nvi_ilce)
dups = {n: [il_name[k[0]] for k in nvi_ilce if k[1] == n] for n, c in cnt.items() if c > 1}
print("\nBirden fazla ilde bulunan ilçe adı:", len(dups))
for n, l in sorted(dups.items()):
    print("  ", n, l)

write_csv(DERIVED / "ilce_nvi.csv", ["plaka", "il", "ilce", "ilce_kimlikNo", "fold"],
          [(i["il_id"], i["_il"], i["adi"], i["kimlikNo"], fold(i["adi"])) for i in ilceler])
