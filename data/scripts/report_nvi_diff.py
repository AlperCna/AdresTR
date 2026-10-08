"""Staging (PTT+Wikidata) ile melihozkara NVİ kopyasını karşılaştırır -> docs/research/staging-vs-nvi.md

PAKETLENEN VERİNİN PARÇASI DEĞİLDİR. NVİ kopyası yalnız bu rapor için okunur;
staging CSV'lerine hiçbir değer geri yazılmaz.

Kullanım: python -I data/scripts/report_nvi_diff.py
"""
import csv
import random
import re
import sys
from collections import Counter, defaultdict
from datetime import date
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import ROOT, fold, fold_compact, load_nvi  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
STAGING = ROOT / "staging"
OUT = ROOT.parent / "docs" / "research" / "staging-vs-nvi.md"
TUR_NVI = {0: "0-köy", 1: "1-mahalle", 3: "3-mezra", 4: "4-mevki", 5: "5-yayla evleri", 6: "6-mevki(?)"}


def rd(name):
    with open(STAGING / name, encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f))


_TYPE_TAIL = re.compile(r"\s*(?:yayla evleri|kume evleri|mezrasi|mezra|yaylalari|yaylasi|kumesi|mevkii|mevki)$")


def c(s):
    """Boşluksuz katlanmış ad; tür sonekleri (mezrası, yaylası, mevkii...) her iki tarafta da yok sayılır,
    çünkü NVİ bunları çoğu zaman adın içinde tutuyor ('ALAYHAN MEZRASI' mevki)."""
    f = fold(s or "")
    g = _TYPE_TAIL.sub("", f).strip()
    return (g or f).replace(" ", "")


def nokoy(n):
    return n[:-3] if n.endswith("koy") and len(n) > 5 else n


def main():
    il = rd("il.csv")
    ilce = rd("ilce.csv")
    birim = rd("birim.csv")
    iller, ilceler, nvi = load_nvi()

    ilce_key = {r["ilce_id"]: (int(r["plaka"]), fold(r["ad"])) for r in ilce}
    nvi_ilce = {(i["il_id"], fold(i["adi"])) for i in ilceler}
    st_ilce = set(ilce_key.values())

    # --- birim anahtarları: (plaka, ilçe, sınıf, ad, üst)
    def st_cls(t):
        return "mahalle" if t in ("mahalle", "osb") else ("koy" if t == "koy" else "alt")

    S = []
    for r in birim:
        pl, ic = ilce_key[r["ilce_id"]]
        cls = st_cls(r["tur"])
        par = c(r["ust_ad"].split(" / ")[-1]) if r["ust_ad"] and cls != "koy" else ""
        S.append(dict(r=r, k=(pl, ic, cls, c(r["ad"]), par)))
    N = []
    seen_koy = set()
    for m in nvi:
        t = m["mahalleTur"]
        if m["koyAdi"] != "MERKEZ" and m["koyKurumBelediyeTur"] != 4:
            kk = (m["ilce_id"], m["koyKayitNo"])
            if kk not in seen_koy:  # köy kaydı koyKayitNo'dan (tip 0 olmasa da)
                seen_koy.add(kk)
                N.append(dict(m=m, label="köy (koyKayitNo)", k=(m["il_id"], fold(m["_ilce"]), "koy", c(m["koyAdi"]), "")))
        if t == 0:
            continue
        if t == 1:
            par = c(m["koyAdi"]) if m["koyKurumBelediyeTur"] == 4 or (m["koyAdi"] != "MERKEZ") else ""
            N.append(dict(m=m, label=TUR_NVI[t], k=(m["il_id"], fold(m["_ilce"]), "mahalle", c(m["adi"]), par)))
        else:
            N.append(dict(m=m, label=TUR_NVI[t], k=(m["il_id"], fold(m["_ilce"]), "alt", c(m["adi"]), c(m["koyAdi"]))))

    def match(A, B):
        full = Counter(b["k"] for b in B)
        name = Counter(b["k"][:4] for b in B)
        nk = Counter(b["k"][:3] + (nokoy(b["k"][3]),) for b in B)
        out = []
        for a in A:
            if a["k"] in full:
                out.append("tam")
            elif a["k"][:4] in name:
                out.append("ad")
            elif a["k"][:3] + (nokoy(a["k"][3]),) in nk:
                out.append("koy-eki")
            else:
                out.append("yok")
        return out

    rn = match(N, S)
    rs = match(S, N)
    tab_n = defaultdict(Counter)
    for a, x in zip(N, rn):
        tab_n[a["label"]][x] += 1
    tab_s = defaultdict(Counter)
    for a, x in zip(S, rs):
        tab_s[a["r"]["tur"]][x] += 1

    # --- nvi_id doğrulaması (Wikidata'dan gelen kimlikler NVİ'de var mı, ad tutuyor mu)
    by_kimlik = {m["kimlikNo"]: m for m in nvi}
    by_koy = {}
    for m in nvi:
        if m["koyAdi"] != "MERKEZ":
            by_koy.setdefault(m["koyKayitNo"], m)
    idv = Counter()
    id_bad = []
    for r in birim:
        if not r["nvi_id"]:
            continue
        i = int(r["nvi_id"])
        m = by_kimlik.get(i) if r["tur"] in ("mahalle", "osb") else by_koy.get(i)
        if not m:
            idv["NVİ'de yok"] += 1
            continue
        nm = m["adi"] if r["tur"] in ("mahalle", "osb") else m["koyAdi"]
        pl, ic = ilce_key[r["ilce_id"]]
        same_ilce = (m["il_id"], fold(m["_ilce"])) == (pl, ic)
        if not same_ilce:
            idv["farklı ilçe"] += 1
            id_bad.append((r["birim_id"], r["ad"], m["_il"], m["_ilce"], nm))
        elif nokoy(c(nm)) == nokoy(c(r["ad"])):
            idv["ilçe + ad tutuyor"] += 1
        else:
            idv["ilçe tutuyor, ad farklı"] += 1
            if len(id_bad) < 400:
                id_bad.append((r["birim_id"], r["ad"], m["_il"], m["_ilce"], nm))

    # --- örnekler
    random.seed(11)
    miss_n = [a for a, x in zip(N, rn) if x == "yok"]
    miss_s = [a for a, x in zip(S, rs) if x == "yok"]
    il_miss_n = Counter(a["m"]["_il"] for a in miss_n).most_common(10)
    il_name = {int(r["plaka"]): r["ad"] for r in il}
    il_miss_s = Counter(il_name[a["k"][0]] for a in miss_s).most_common(10)

    L = []
    w = L.append
    w("# Staging (PTT + Wikidata) ile NVİ kopyasının karşılaştırması")
    w("")
    w(f"Oluşturma: {date.today().isoformat()}. Üreten: `data/scripts/report_nvi_diff.py`.")
    w("")
    w("> Bu rapor **paketlenen verinin parçası değildir**. NVİ kopyası (melihozkara, 2026-10-06, lisanssız) yalnızca karşılaştırma için okunur. "
      "Staging dosyalarına buradan hiçbir değer geri yazılmaz.")
    w("")
    w("## Sayılar")
    w("")
    w("| Seviye | Staging | NVİ |")
    w("|---|---|---|")
    w(f"| İl | {len(il)} | {len(iller)} |")
    w(f"| İlçe | {len(ilce)} | {len(ilceler)} |")
    w(f"| Birim satırı | {len(birim)} | {len(nvi)} |")
    w(f"| Köy (distinct) | {sum(1 for r in birim if r['tur'] == 'koy')} | {len(seen_koy)} |")
    w(f"| Mahalle (staging mahalle+osb / NVİ tip 1) | {sum(1 for r in birim if r['tur'] in ('mahalle', 'osb'))} | "
      f"{sum(1 for m in nvi if m['mahalleTur'] == 1)} |")
    w("")
    w(f"İlçe kümesi (plaka + katlanmış ad): yalnız staging'de {len(st_ilce - nvi_ilce)}, yalnız NVİ'de {len(nvi_ilce - st_ilce)}.")
    w("")
    w("Staging tür dağılımı: " + ", ".join(f"{k} {v}" for k, v in Counter(r["tur"] for r in birim).most_common()))
    w("")
    w("## NVİ'den staging'e (NVİ kaydı staging'de var mı?)")
    w("")
    w("Eşleşme kademeleri:")
    w("- **tam:** ad + üst köy/belde tutuyor.")
    w("- **ad:** üst birim yok sayılıyor.")
    w("- **köy-eki:** `XKÖY` = `X Köyü`.")
    w("")
    w("Anahtar: plaka, katlanmış ilçe, sınıf (mahalle/köy/alt birim) ve boşluksuz katlanmış ad.")
    w("")
    w("| NVİ türü | n | tam | ad | köy-eki | **yok** | yok % |")
    w("|---|---|---|---|---|---|---|")
    for t in sorted(tab_n):
        cc = tab_n[t]
        n = sum(cc.values())
        w(f"| {t} | {n} | {cc['tam']} | {cc['ad']} | {cc['koy-eki']} | **{cc['yok']}** | {100 * cc['yok'] / n:.1f} |")
    tot = Counter(rn)
    w(f"| **toplam** | {len(N)} | {tot['tam']} | {tot['ad']} | {tot['koy-eki']} | **{tot['yok']}** | {100 * tot['yok'] / len(N):.1f} |")
    w("")
    w("## Staging'den NVİ'ye (staging birimi NVİ'de var mı?)")
    w("")
    w("| staging tur | n | tam | ad | köy-eki | **yok** | yok % |")
    w("|---|---|---|---|---|---|---|")
    for t in sorted(tab_s):
        cc = tab_s[t]
        n = sum(cc.values())
        w(f"| {t} | {n} | {cc['tam']} | {cc['ad']} | {cc['koy-eki']} | **{cc['yok']}** | {100 * cc['yok'] / n:.1f} |")
    tot = Counter(rs)
    w(f"| **toplam** | {len(S)} | {tot['tam']} | {tot['ad']} | {tot['koy-eki']} | **{tot['yok']}** | {100 * tot['yok'] / len(S):.1f} |")
    w("")
    w("**Eksiklerin dağılımı (il bazında, ilk 10):**")
    w("")
    w("- NVİ'de var, staging'de yok: " + ", ".join(f"{a} {b}" for a, b in il_miss_n))
    w("- Staging'de var, NVİ'de yok: " + ", ".join(f"{a} {b}" for a, b in il_miss_s))
    w("")
    w("## Wikidata'dan gelen `nvi_id` doğrulaması")
    w("")
    w("Staging'deki `nvi_id` değerleri yalnız Wikidata'dan geliyor:")
    w("- mahalle/osb için P12883 → NVİ `kimlikNo`")
    w("- köy için P13588 → NVİ `koyKayitNo`")
    w("")
    w("| Sonuç | Sayı |")
    w("|---|---|")
    for k, v in idv.most_common():
        w(f"| {k} | {v} |")
    w("")
    if id_bad:
        w("Örnek uyuşmazlıklar (birim_id, staging adı, NVİ il/ilçe, NVİ adı). Bunların çoğu ad değişikliği ya da yazım varyantı; "
          "\"farklı ilçe\" satırları birleştirme hatası adayı:")
        w("")
        for x in id_bad[:25]:
            w(f"- {x[0]} `{x[1]}` → {x[2]}/{x[3]} `{x[4]}`")
        w("")
    w("## Örnekler")
    w("")
    w("### NVİ'de olup staging'de olmayan (rastgele 25)")
    w("")
    for a in random.sample(miss_n, min(25, len(miss_n))):
        m = a["m"]
        w(f"- {m['_il']}/{m['_ilce']}: {m['bilesenAdi'].strip()} (tip {m['mahalleTur']})")
    w("")
    w("### Staging'de olup NVİ'de olmayan (rastgele 25)")
    w("")
    for a in random.sample(miss_s, min(25, len(miss_s))):
        r = a["r"]
        w(f"- {il_name[a['k'][0]]}/{a['k'][1]}: {r['ad']}" + (f" ({r['ust_ad']})" if r["ust_ad"] else "") + f" [{r['tur']}, {r['posta_kodu']}]")
    w("")
    OUT.write_text("\n".join(L) + "\n", encoding="utf-8", newline="\n")
    print("yazıldı:", OUT)
    print("\n".join(L[:60]))


if __name__ == "__main__":
    main()
