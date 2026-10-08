"""Mahalle düzeyi: PTT türevli listeler (murat 2024-03, epigra 2022-08) ile NVİ (2026-10-06) örtüşmesi."""
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import *  # noqa

sys.stdout.reconfigure(encoding="utf-8")

_HEAD_SUFFIX = re.compile(r"\s*(?:mahallesi|mahalle|mah\.?|mh\.?)\s*$", re.I)


def parse_ptt(n: str):
    """PTT 'Mahalle' sütununu yapıya çevirir -> (kind, name, parent, sub)."""
    n = re.sub(r"\s+", " ", n).strip()
    parens = re.findall(r"\(([^()]*)\)", n)
    head = re.sub(r"\s*\([^()]*\)\s*", " ", n).strip()
    low = tr_lower(head)
    if not parens:
        if low.endswith(" köyü"):
            return "koy", head[: -len(" köyü")], None, None
        if re.search(r" mah\.?$| mh\.?$", low):
            return "mahalle", _HEAD_SUFFIX.sub("", head), None, None
        return "other", head, None, None
    last = parens[-1].strip()
    llow = tr_lower(last)
    sub = parens[-2].strip() if len(parens) >= 2 else None
    name = _HEAD_SUFFIX.sub("", head)
    if llow.endswith(" köyü"):
        if low.endswith(" köyü"):  # 'Gözecik (Merkez Bucağı) Köyü'
            return "koy", head[: -len(" köyü")], None, last
        return "koy_alt", name, last[: -len(" köyü")], sub
    if llow.endswith(" beldesi"):
        return "belde_mah", name, last[: -len(" beldesi")], sub
    if re.search(r" mah\.?$| mh\.?$", llow):
        return "mah_alt", name, _HEAD_SUFFIX.sub("", last), sub
    return "other", name, last, sub


def nvi_key(m):
    t, kur = m["mahalleTur"], m["koyKurumBelediyeTur"]
    if t == 0:
        return "koy", m["koyAdi"], None
    if t == 1 and m["koyAdi"] == "MERKEZ":
        return "mahalle", m["adi"], None
    if t == 1 and kur == 4:
        return "belde_mah", m["adi"], m["koyAdi"]
    if m["koyAdi"] == "MERKEZ":
        return "mahalle_other", m["adi"], None
    return "koy_alt", m["adi"], m["koyAdi"]


def _nokoy(n: str) -> str:
    """'ciftlikkoy' / 'ciftlik koy' -> 'ciftlik' (NVİ köy adları 'KÖY' ekini içerebiliyor)."""
    c = n.replace(" ", "")
    return c[:-3] if c.endswith("koy") and len(c) > 5 else c


TUR = {0: "0-köy", 1: "1-mahalle", 3: "3-mezra", 4: "4-mevki", 5: "5-yayla evleri", 6: "6-mevki(?)"}


def main():
    iller, ilceler, nvi = load_nvi()
    il_plate = {fold(i["adi"]): i["kimlikNo"] for i in iller}
    mu = load_murat()
    ep, *_ = load_epigra()

    # --- NVİ kayıtları
    nvi_recs = []
    for m in nvi:
        kind, name, parent = nvi_key(m)
        nvi_recs.append(dict(src=m, plate=m["il_id"], ilce=fold(m["_ilce"]), kind=kind,
                             name=fold(name), parent=fold(parent) if parent else "",
                             tur=TUR[m["mahalleTur"]]))

    def ptt_recs(rows, with_plate):
        out = []
        for r in rows:
            if with_plate:
                plate, il, ilce, mah, pk = r
                plate = int(plate)
                semt = None
            else:
                il, ilce, semt, mah, pk = r
                plate = il_plate[fold(il)]
            kind, name, parent, sub = parse_ptt(mah)
            out.append(dict(raw=mah, plate=plate, ilce=fold(ilce), kind=kind, name=fold(name),
                            parent=fold(parent) if parent else "", sub=fold(sub) if sub else "",
                            pk=pk, semt=semt))
        return out

    MU = ptt_recs(mu, True)
    EP = ptt_recs(ep, False)

    print("== Satır sayıları")
    print("NVİ:", len(nvi_recs), Counter(r["tur"] for r in nvi_recs))
    print("NVİ kind:", Counter(r["kind"] for r in nvi_recs))
    print("murat:", len(MU), Counter(r["kind"] for r in MU))
    print("epigra:", len(EP), Counter(r["kind"] for r in EP))

    # köy sayısı
    def koy_set_ptt(R):
        s = {(r["plate"], r["ilce"], r["name"]) for r in R if r["kind"] == "koy"}
        s |= {(r["plate"], r["ilce"], r["parent"]) for r in R if r["kind"] == "koy_alt"}
        return s

    nvi_koy = {(r["plate"], r["ilce"], fold(r["src"]["koyAdi"])) for r in nvi_recs
               if r["src"]["koyAdi"] != "MERKEZ" and r["src"]["koyKurumBelediyeTur"] != 4}
    nvi_belde = {(r["plate"], r["ilce"], fold(r["src"]["koyAdi"])) for r in nvi_recs
                 if r["src"]["koyKurumBelediyeTur"] == 4}
    print("\nDistinct köy: NVİ", len(nvi_koy), "| murat", len(koy_set_ptt(MU)), "| epigra", len(koy_set_ptt(EP)))
    print("Distinct köy kimliği (koyKayitNo, belde hariç):",
          len({(m['ilce_id'], m['koyKayitNo']) for m in nvi if m['koyAdi'] != 'MERKEZ' and m['koyKurumBelediyeTur'] != 4}))
    print("Distinct belde: NVİ", len(nvi_belde), "| murat",
          len({(r['plate'], r['ilce'], r['parent']) for r in MU if r['kind'] == 'belde_mah'}))
    print("NVİ 'mahalle' (tip1, MERKEZ+belde):", sum(1 for r in nvi_recs if r['kind'] in ('mahalle', 'belde_mah')))
    print("murat mahalle+belde_mah:", sum(1 for r in MU if r['kind'] in ('mahalle', 'belde_mah')))
    print("NVİ köy+köy_alt-only köyler: köy kaydı tip0 olmayan köy:", len(nvi_koy - {(r['plate'], r['ilce'], r['name']) for r in nvi_recs if r['kind'] == 'koy'}))

    # --- eşleştirme
    def match(A, B, label):
        """A'daki her kaydı B'de arar. Kademeler: tam (isim+üst) -> isim (üst yok say) -> kompakt."""
        idx_full = defaultdict(list)
        idx_name = defaultdict(list)
        idx_comp = defaultdict(list)
        idx_koy = defaultdict(list)
        for i, b in enumerate(B):
            idx_koy[(b["plate"], b["ilce"], _nokoy(b["name"]))].append(i)
            idx_full[(b["plate"], b["ilce"], b["name"], b["parent"])].append(i)
            idx_name[(b["plate"], b["ilce"], b["name"])].append(i)
            idx_comp[(b["plate"], b["ilce"], b["name"].replace(" ", ""))].append(i)
        res = []
        for a in A:
            k1 = (a["plate"], a["ilce"], a["name"], a["parent"])
            if idx_full.get(k1):
                res.append("tam")
            elif idx_name.get(k1[:3]):
                res.append("isim")
            elif idx_comp.get((a["plate"], a["ilce"], a["name"].replace(" ", ""))):
                res.append("kompakt")
            elif idx_koy.get((a["plate"], a["ilce"], _nokoy(a["name"]))):
                res.append("koy-eki")
            else:
                res.append("yok")
        return res

    print("\n== NVİ -> murat(PTT 2024) eşleşmesi, mahalleTur kırılımı")
    r_nm = match(nvi_recs, MU, "nvi->murat")
    tab = defaultdict(Counter)
    for rec, r in zip(nvi_recs, r_nm):
        tab[rec["tur"]][r] += 1
    for t in sorted(tab):
        tot = sum(tab[t].values())
        print(f"  {t:16s} n={tot:6d}", dict(tab[t]), f"eşleşmeyen %{100 * tab[t]['yok'] / tot:.1f}")
    tot = Counter(r_nm)
    print("  TOPLAM", dict(tot))

    print("\n== murat(PTT 2024) -> NVİ eşleşmesi, PTT tür kırılımı")
    r_mn = match(MU, nvi_recs, "murat->nvi")
    tab2 = defaultdict(Counter)
    for rec, r in zip(MU, r_mn):
        tab2[rec["kind"]][r] += 1
    for t in sorted(tab2):
        tot2 = sum(tab2[t].values())
        print(f"  {t:12s} n={tot2:6d}", dict(tab2[t]), f"eşleşmeyen %{100 * tab2[t]['yok'] / tot2:.1f}")
    print("  TOPLAM", dict(Counter(r_mn)))

    # Yalnız-mahalle (tip1) karşılaştırması: mahalle ↔ (mahalle+belde_mah)
    A = [r for r in nvi_recs if r["src"]["mahalleTur"] == 1]
    B = [r for r in MU if r["kind"] in ("mahalle", "belde_mah", "mah_alt")]
    ra = match(A, B, "")
    rb = match(B, A, "")
    print("\n== Sadece mahalle (NVİ tip1 vs PTT Mah/belde): NVİ", len(A), dict(Counter(ra)), "| PTT", len(B), dict(Counter(rb)))

    # örnek eşleşmeyenler
    import random
    random.seed(7)
    miss_nvi = [rec for rec, r in zip(nvi_recs, r_nm) if r == "yok"]
    miss_mu = [rec for rec, r in zip(MU, r_mn) if r == "yok"]
    print("\nÖrnek NVİ'de olup PTT'de olmayan (tip1):",
          [(r["src"]["_il"], r["src"]["_ilce"], r["src"]["bilesenAdi"]) for r in random.sample([x for x in miss_nvi if x['tur'] == '1-mahalle'], 15)])
    print("\nÖrnek PTT'de olup NVİ'de olmayan (mahalle):",
          [(r["plate"], r["ilce"], r["raw"]) for r in random.sample([x for x in miss_mu if x['kind'] == 'mahalle'], 15)])
    print("\nÖrnek PTT koy_alt eşleşmeyen:",
          [(r["plate"], r["ilce"], r["raw"]) for r in random.sample([x for x in miss_mu if x['kind'] == 'koy_alt'], 10)])

    # murat vs epigra (2022 -> 2024 PTT değişimi)
    km = Counter((r["plate"], r["ilce"], r["kind"], r["name"], r["parent"]) for r in MU)
    ke = Counter((r["plate"], r["ilce"], r["kind"], r["name"], r["parent"]) for r in EP)
    print("\n== PTT 2022 (epigra) vs 2024 (murat): yalnız murat", sum((km - ke).values()),
          "| yalnız epigra", sum((ke - km).values()))
    print("   yalnız murat örnek:", list((km - ke))[:10])
    print("   yalnız epigra örnek:", list((ke - km))[:10])
    pk_diff = sum(1 for a, b in zip(sorted((r['plate'], r['ilce'], r['raw'], r['pk']) for r in MU),
                                     sorted((r['plate'], r['ilce'], r['raw'], r['pk']) for r in EP)) if a != b)
    print("   sıralı satır farkı (yaklaşık):", pk_diff)

    # türetilmiş çıktı
    write_csv(DERIVED / "mahalle_nvi_vs_ptt.csv",
              ["plaka", "il", "ilce", "kimlikNo", "mahalleTur", "bilesenAdi", "ptt_eslesme"],
              [(r["plate"], r["src"]["_il"], r["src"]["_ilce"], r["src"]["kimlikNo"], r["src"]["mahalleTur"],
                r["src"]["bilesenAdi"], m) for r, m in zip(nvi_recs, r_nm)])
    write_csv(DERIVED / "ptt_parsed_murat.csv",
              ["plaka", "ilce_fold", "kind", "name_fold", "parent_fold", "sub_fold", "pk", "raw", "nvi_eslesme"],
              [(r["plate"], r["ilce"], r["kind"], r["name"], r["parent"], r["sub"], r["pk"], r["raw"], m)
               for r, m in zip(MU, r_mn)])


if __name__ == "__main__":
    main()
