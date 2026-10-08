"""Posta kodu kuralları, isim belirsizliği (ambiguity), isim çakışmaları ve biçim tuhaflıkları."""
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path
from statistics import median

sys.path.insert(0, str(Path(__file__).parent))
from common import *  # noqa

sys.stdout.reconfigure(encoding="utf-8")

iller, ilceler, nvi = load_nvi()
mu = load_murat()

# ------------------------------------------------------------ posta kodu
print("== POSTA KODU (murat / PTT)")
pks = Counter(r[4] for r in mu)
print("satır:", len(mu), "| distinct pk:", len(pks))
print("pk 5 hane & rakam:", sum(1 for p in pks if re.fullmatch(r"\d{5}", p)))
print("ilk 2 hane == plaka:", sum(1 for r in mu if r[4][:2] == r[0]), "/", len(mu))
key = Counter((r[0], r[2], r[3]) for r in mu)
print("aynı (il, ilçe, mahalle) için birden fazla satır:", sum(1 for c in key.values() if c > 1))
multi_pk = defaultdict(set)
for r in mu:
    multi_pk[(r[0], r[2], r[3])].add(r[4])
print("birden fazla pk alan (il, ilçe, mahalle):", sum(1 for v in multi_pk.values() if len(v) > 1))
ilce_by_pk = defaultdict(set)
for r in mu:
    ilce_by_pk[r[4]].add((r[0], r[2]))
print("birden fazla ilçeye yayılan pk:", sum(1 for v in ilce_by_pk.values() if len(v) > 1),
      [(p, v) for p, v in ilce_by_pk.items() if len(v) > 1][:5])
pk_by_ilce = defaultdict(set)
for r in mu:
    pk_by_ilce[(r[0], r[2])].add(r[4])
per = sorted(len(v) for v in pk_by_ilce.values())
print("ilçe başına pk: min", per[0], "medyan", median(per), "maks", per[-1], "| tek pk'lı ilçe:", sum(1 for x in per if x == 1))
mper = sorted(pks.values())
print("pk başına mahalle: medyan", median(mper), "maks", mper[-1], "| p90", mper[int(len(mper) * .9)])
# 3. hane deseni: merkez/kırsal
print("pk son 3 hane dağılımı (ilk 10):", Counter(p[2:] for p in pks).most_common(10))
# pk -> mahalle belirsizliği: bir pk + mahalle adı tekil mi?
pk_name = Counter((r[4], fold(r[3])) for r in mu)
print("(pk, katlanmış mahalle adı) tekrarı:", sum(1 for c in pk_name.values() if c > 1),
      [k for k, c in pk_name.items() if c > 1][:8])

# ------------------------------------------------------------ belirsizlik (NVİ)
print("\n== İSİM BELİRSİZLİĞİ (NVİ)")


def ambiguity(rows, label):
    names = Counter(fold(n) for n, _, _ in rows)
    print(f"-- {label}: {len(rows)} kayıt, {len(names)} distinct katlanmış ad")
    print("   en sık 30:", [(n, c) for n, c in names.most_common(30)])
    dist = Counter(names.values())
    buckets = [(1, 1), (2, 2), (3, 5), (6, 10), (11, 50), (51, 100), (101, 10**9)]
    out = []
    for lo, hi in buckets:
        nn = sum(v for k, v in dist.items() if lo <= k <= hi)
        rr = sum(k * v for k, v in dist.items() if lo <= k <= hi)
        out.append((f"{lo}-{hi if hi < 10**9 else '∞'}", nn, rr))
    print("   ad başına tekrar kovası (ad sayısı, kayıt sayısı):", out)
    uniq_tr = sum(1 for n, _, _ in rows if names[fold(n)] == 1)
    il_c = Counter((fold(n), il) for n, il, _ in rows)
    ilce_c = Counter((fold(n), il, ilce) for n, il, ilce in rows)
    uniq_il = sum(1 for n, il, _ in rows if il_c[(fold(n), il)] == 1)
    uniq_ilce = sum(1 for n, il, ilce in rows if ilce_c[(fold(n), il, ilce)] == 1)
    print(f"   Türkiye genelinde tekil ad taşıyan kayıt: {uniq_tr} (%{100 * uniq_tr / len(rows):.1f})")
    print(f"   il içinde tekil: {uniq_il} (%{100 * uniq_il / len(rows):.1f})")
    print(f"   ilçe içinde tekil: {uniq_ilce} (%{100 * uniq_ilce / len(rows):.1f}) -> ilçe içi çakışan kayıt {len(rows) - uniq_ilce}")
    return names


mah1 = [(m["adi"], m["il_id"], m["ilce_id"]) for m in nvi if m["mahalleTur"] == 1]
names1 = ambiguity(mah1, "tip1 mahalle")
mk = [(m["adi"], m["il_id"], m["ilce_id"]) for m in nvi if m["mahalleTur"] == 1] + \
     list({(m["koyAdi"], m["il_id"], m["ilce_id"]) for m in nvi if m["koyAdi"] != "MERKEZ" and m["koyKurumBelediyeTur"] != 4})
ambiguity(mk, "mahalle + köy (yerleşim adı)")
allu = [(m["_name"], m["il_id"], m["ilce_id"]) for m in nvi]
ambiguity(allu, "tüm 73.398 NVİ birimi")

# ilçe içi çakışma örnekleri (aynı ilçede aynı katlanmış ad, tip1)
c = Counter((fold(n), il, ilce) for n, il, ilce in mah1)
ex = [(k, v) for k, v in c.items() if v > 1]
ilce_name = {i["kimlikNo"]: i["adi"] for i in ilceler}
print("   tip1 ilçe içi çakışma örnekleri:", [(ilce_name[k[2]], k[0], v) for k, v in ex[:15]])
# katlama kaynaklı çakışma: ham ad farklı, katlanmış ad aynı
raw_by_fold = defaultdict(set)
for m in nvi:
    if m["mahalleTur"] == 1:
        raw_by_fold[(fold(m["adi"]), m["ilce_id"])].add(m["adi"])
fc = [(ilce_name[k[1]], v) for k, v in raw_by_fold.items() if len(v) > 1]
print("   aynı ilçede yalnız katlama sonrası çakışan (farklı ham yazım):", len(fc), fc[:15])
# boşluk katlaması ile çakışma
comp = defaultdict(set)
for m in nvi:
    if m["mahalleTur"] == 1:
        comp[(fold_compact(m["adi"]), m["ilce_id"])].add(m["adi"])
cc = [(ilce_name[k[1]], v) for k, v in comp.items() if len(v) > 1 and len({fold(x) for x in v}) > 1]
print("   aynı ilçede yalnız boşluk kaldırınca çakışan:", len(cc), cc[:15])

# ------------------------------------------------------------ çakışmalar
print("\n== İSİM ÇAKIŞMALARI")
il_f = {fold(i["adi"]) for i in iller}
ilce_f = defaultdict(set)
for i in ilceler:
    ilce_f[fold(i["adi"])].add(i["il_id"])
m_eq_ilce_any = [m for m in nvi if m["mahalleTur"] == 1 and fold(m["adi"]) in ilce_f]
m_eq_ilce_same_il = [m for m in m_eq_ilce_any if m["il_id"] in ilce_f[fold(m["adi"])]]
m_eq_own_ilce = [m for m in m_eq_ilce_any if fold(m["adi"]) == fold(m["_ilce"])]
print("tip1 mahalle adı == herhangi bir ilçe adı:", len(m_eq_ilce_any), "| aynı ildeki bir ilçe adı:", len(m_eq_ilce_same_il),
      "| kendi ilçesinin adı:", len(m_eq_own_ilce))
print("   'merkez' adlı tip1 mahalle:", sum(1 for m in nvi if m["mahalleTur"] == 1 and fold(m["adi"]) == "merkez"))
print("   aynı ildeki başka ilçe adını taşıyan örnekler:",
      [(m["_il"], m["_ilce"], m["adi"]) for m in m_eq_ilce_same_il if fold(m["adi"]) != fold(m["_ilce"]) and fold(m["adi"]) != "merkez"][:20])
print("   kendi ilçesinin adını taşıyan örnekler:", [(m["_il"], m["_ilce"], m["adi"]) for m in m_eq_own_ilce][:15])
koy_eq_ilce = {(m["_il"], m["_ilce"], m["koyAdi"]) for m in nvi if m["koyAdi"] != "MERKEZ" and fold(m["koyAdi"]) in ilce_f}
print("köy/belde adı == bir ilçe adı:", len(koy_eq_ilce))
m_eq_il = [m for m in nvi if m["mahalleTur"] == 1 and fold(m["adi"]) in il_f]
print("tip1 mahalle adı == bir il adı:", len(m_eq_il), Counter(fold(m["adi"]) for m in m_eq_il).most_common(10))
koy_eq_il = {(m["_il"], m["koyAdi"]) for m in nvi if m["koyAdi"] != "MERKEZ" and fold(m["koyAdi"]) in il_f}
print("köy/belde adı == bir il adı:", len(koy_eq_il), list(koy_eq_il)[:10])
print("ilçe adı == bir il adı:", [(i["_il"], i["adi"]) for i in ilceler if fold(i["adi"]) in il_f])
# mahalle adı içinde ilçe/il adı geçen (öneki): "Kadıköy" gibi
print("ilçe adı == başka ildeki ilçe adı (25 ad, yukarıda a1)")

# ------------------------------------------------------------ biçim tuhaflıkları
print("\n== BİÇİM TUHAFLIKLARI")


def quirks(names, label):
    q = Counter()
    ex = defaultdict(list)
    tests = {
        "rakam": r"\d",
        "başta rakam (19 MAYIS)": r"^\d",
        "Roma rakamı token (I/II/III/IV/V)": r"(^|[\s\-.])(I{1,3}|IV|V|VI{0,3})($|[\s\-.])",
        "nokta": r"\.",
        "tire": r"-",
        "kesme işareti": r"['’`]",
        "parantez": r"[()]",
        "çift boşluk": r"  ",
        "baş/son boşluk": r"^\s|\s$",
        "şapkalı harf (âîû)": r"[âîûÂÎÛ]",
        "U+0307 birleşik nokta": "̇",
        "'/' eğik çizgi": r"/",
        "OSB": r"(?i)\bosb",
        "KÜME / küme evler": r"(?i)k[üu]me",
        "SİTE/SİTESİ": r"(?i)\bsite",
    }
    for n in names:
        for k, pat in tests.items():
            if re.search(pat, n):
                q[k] += 1
                if len(ex[k]) < 6:
                    ex[k].append(n)
    print(f"-- {label} ({len(names)} ad)")
    for k in tests:
        if q[k]:
            print(f"   {k}: {q[k]}  örn. {ex[k]}")


quirks([m["_name"] for m in nvi], "NVİ adi/koyAdi")
quirks([m["bilesenAdi"] for m in nvi], "NVİ bilesenAdi")
quirks([r[3] for r in mu], "PTT (murat) mahalle sütunu")
up = sum(1 for m in nvi if m["_name"] == m["_name"].upper())
print("NVİ tamamen büyük harf:", up, "/", len(nvi))
# PTT titleCase hatası: parantez içi küçük harfle başlıyor
lowparen = sum(1 for r in mu if re.search(r"\([a-zçğıöşü]", r[3]))
print("PTT(murat) parantez içi küçük harfle başlayan (titleCase hatası):", lowparen)
# 'Mah' sonrası çift boşluk
print("PTT ' Mah' öncesi çift boşluk:", sum(1 for r in mu if "  Mah" in r[3]))
# İ / I katlama tuzağı: NVİ'de 'I' ve 'İ' karışık kelime
print("ilçe adlarında rakam:", [i["adi"] for i in ilceler if re.search(r"\d", i["adi"])])
