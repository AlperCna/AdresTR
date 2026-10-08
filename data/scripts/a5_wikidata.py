"""Wikidata (CC0) ↔ NVİ eşleşmesi: kimlik (P12883/P13588/P14366/P14358) ve (ilçe, katlanmış ad) ile."""
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import *  # noqa

sys.stdout.reconfigure(encoding="utf-8")

iller, ilceler, nvi = load_nvi()
il_name = {i["kimlikNo"]: i["adi"] for i in iller}
ilce_by_kimlik = {i["kimlikNo"]: i for i in ilceler}
nvi_by_kimlik = {m["kimlikNo"]: m for m in nvi}
nvi_by_koy = defaultdict(list)
for m in nvi:
    if m["koyAdi"] != "MERKEZ":
        nvi_by_koy[m["koyKayitNo"]].append(m)


def group(rows, multi=("parent", "cls", "replacedBy", "replaces", "coord", "dissolved", "nbhId", "villId", "yerelnet", "distId", "plaka", "provId", "iso", "inception")):
    """SPARQL satırlarını item başına topla (çoklu değerler set)."""
    out = {}
    for r in rows:
        it = qid(r["item"])
        o = out.setdefault(it, {"item": it, "trLabel": r.get("trLabel", "")})
        for k in multi:
            if k in r and r[k]:
                o.setdefault(k, set()).add(qid(r[k]) if r[k].startswith("http") else r[k])
    return out


def clean_label(s: str) -> str:
    # "Caferağa, Kadıköy" / "Akpınar (Aladağ)" / "Eğirdir ilçesi" -> çekirdek ad
    s = re.sub(r"\s*\(.*?\)\s*$", "", s or "")
    s = s.split(",")[0].strip()
    s = re.sub(r"\s+(ilçesi|İlçesi|ilçe|Mahallesi|mahallesi|köyü|Köyü)$", "", s)
    return s


# ---------------------------------------------------------------- İL
wil = group(load_wd("wd_il"))
print("== İL: Wikidata", len(wil), "öğe")
plate_by_q = {}
for q, o in wil.items():
    pl = int(next(iter(o.get("provId") or o.get("plaka", {"0"}))))  # P14358 / ISO daha güvenilir
    plate_by_q[q] = pl
    if o.get("plaka") != {f"{pl:02d}"}:
        print("   HATA: P395 (plaka) != P14358/ISO:", o["trLabel"], "P395=", o.get("plaka"), "P14358=", o.get("provId"), o.get("iso"))
    if fold(o["trLabel"]) != fold(il_name.get(pl, "")):
        print("   il adı farkı:", o["trLabel"], il_name.get(pl))
print("   plaka (P395) dolu:", sum(1 for o in wil.values() if o.get("plaka")),
      "| P14358 dolu:", sum(1 for o in wil.values() if o.get("provId")),
      "| koordinat:", sum(1 for o in wil.values() if o.get("coord")))

# ---------------------------------------------------------------- İLÇE
wilce = group(load_wd("wd_ilce"))
active = {q: o for q, o in wilce.items() if not o.get("dissolved")}
print("\n== İLÇE: Wikidata", len(wilce), "öğe | P576 (kaldırıldı) olan:", len(wilce) - len(active),
      "| aktif:", len(active))
print("   P14366 dolu:", sum(1 for o in wilce.values() if o.get("distId")),
      "| P1366 (yerine geçen) dolu:", sum(1 for o in wilce.values() if o.get("replacedBy")),
      "| koordinat:", sum(1 for o in active.values() if o.get("coord")))

# ilçe öğesi -> (plaka, fold ad)
nvi_ilce_key = {(i["il_id"], fold(i["adi"])): i for i in ilceler}
ilce_q_to_nvi = {}
unmatched_active = []
how = Counter()
for q, o in wilce.items():
    m = None
    for d in o.get("distId", ()):
        if d.isdigit() and int(d) in ilce_by_kimlik:
            m = ilce_by_kimlik[int(d)]
            how["P14366"] += 1
            break
    if m is None:
        plates = {plate_by_q[p] for p in o.get("parent", ()) if p in plate_by_q}
        lab = fold(clean_label(o["trLabel"]))
        lab2 = re.sub(r"^(.*) merkez$", "merkez", lab)  # "Bolu Merkez" -> "merkez"
        for pl in plates:
            for cand in (lab, lab2):
                if (pl, cand) in nvi_ilce_key:
                    m = nvi_ilce_key[(pl, cand)]
                    how["ad"] += 1
                    break
            if m:
                break
    if m:
        ilce_q_to_nvi[q] = m
    elif q in active:
        unmatched_active.append(o)
# 3) çocuk oyu: kimlikle NVİ'ye bağlanan mahalle/köylerin P131'i bu ilçe öğesini gösteriyorsa
votes = defaultdict(Counter)
for fname, fld, kind in (("wd_mahalle", "nbhId", "m"), ("wd_koy", "villId", "k")):
    for r in load_wd(fname):
        v = r.get(fld, "")
        if not v.isdigit() or not r.get("parent"):
            continue
        v = int(v)
        m = nvi_by_kimlik.get(v) if kind == "m" else (nvi_by_koy.get(v) or [None])[0]
        if m:
            votes[qid(r["parent"])][m["ilce_id"]] += 1
for q, o in wilce.items():
    if q in ilce_q_to_nvi or q not in votes:
        continue
    (best, n), = votes[q].most_common(1)
    if n >= 3 and n / sum(votes[q].values()) >= 0.8:
        ilce_q_to_nvi[q] = ilce_by_kimlik[best]
        how["cocuk-oyu"] += 1
unmatched_active = [o for o in unmatched_active if o["item"] not in ilce_q_to_nvi]
matched_nvi = {m["kimlikNo"] for m in ilce_q_to_nvi.values()}
dup = Counter(m["kimlikNo"] for m in ilce_q_to_nvi.values())
print("   aynı NVİ ilçesine bağlanan birden fazla Wikidata öğesi:", sum(1 for c in dup.values() if c > 1))
print("   NVİ'ye bağlanan Wikidata ilçe öğesi:", len(ilce_q_to_nvi), dict(how),
      "| kapsanan NVİ ilçe:", len(matched_nvi), "/", len(ilceler))
print("   eşlenemeyen AKTİF Wikidata ilçe:", len(unmatched_active),
      [(o["trLabel"], [wil[p]["trLabel"] for p in o.get("parent", ()) if p in wil]) for o in unmatched_active][:40])
print("   Wikidata'da karşılığı olmayan NVİ ilçe:",
      [(i["_il"], i["adi"]) for i in ilceler if i["kimlikNo"] not in matched_nvi])
dis = [o for o in wilce.values() if o.get("dissolved")]
print("   kaldırılmış ilçe örnekleri:", [(o["trLabel"], sorted(o["dissolved"])[0][:4],
                                         [wilce.get(r, {}).get("trLabel", r) for r in o.get("replacedBy", ())]) for o in dis[:25]])
# ilçe etiket varyantları (NVİ ile aynı katlanmış değil)
var = [(o["trLabel"], m["adi"]) for q, o in wilce.items() if (m := ilce_q_to_nvi.get(q)) and fold(clean_label(o["trLabel"])) != fold(m["adi"])]
print("   etiket ≠ NVİ adı:", len(var), var[:30])

# ---------------------------------------------------------------- MAHALLE / KÖY
wbelde = group(load_wd("wd_belde"))
parent_ilce = dict(ilce_q_to_nvi)  # q -> nvi ilçe
for q, o in wbelde.items():
    for p in o.get("parent", ()):
        if p in ilce_q_to_nvi:
            parent_ilce[q] = ilce_q_to_nvi[p]


def analyze(name, rows, id_field, id_kind):
    items = group(rows)
    n = len(items)
    act = {q: o for q, o in items.items() if not o.get("dissolved")}
    with_coord = sum(1 for o in items.values() if o.get("coord"))
    with_id = sum(1 for o in items.values() if o.get(id_field))
    print(f"\n== {name}: {n} öğe | P576 olan {n - len(act)} | koordinatlı {with_coord} (%{100 * with_coord / n:.1f})"
          f" | {id_field} dolu {with_id} (%{100 * with_id / n:.1f})"
          f" | P1366 {sum(1 for o in items.values() if o.get('replacedBy'))} | P1365 {sum(1 for o in items.values() if o.get('replaces'))}"
          f" | P2123 YerelNet {sum(1 for o in items.values() if o.get('yerelnet'))}")
    # 1) kimlik ile
    res = {}
    name_ok = Counter()
    for q, o in items.items():
        for v in o.get(id_field, ()):
            if not v.isdigit():
                continue
            v = int(v)
            if id_kind == "mahalle" and v in nvi_by_kimlik:
                m = nvi_by_kimlik[v]
                res[q] = ("id", m)
                name_ok[fold(clean_label(o["trLabel"])) == fold(m["_name"])] += 1
                break
            if id_kind == "koy" and v in nvi_by_koy:
                m = nvi_by_koy[v][0]
                res[q] = ("id", m)
                name_ok[fold(clean_label(o["trLabel"])).replace(" ", "") == fold(m["koyAdi"]).replace(" ", "")] += 1
                break
    print(f"   kimlikle NVİ'ye bağlanan: {len(res)} | ad da tutuyor: {name_ok[True]} / tutmuyor: {name_ok[False]}")
    # 2) (ilçe, ad) ile
    idx = defaultdict(list)
    for m in nvi:
        if id_kind == "mahalle":
            idx[(m["ilce_id"], fold(m["_name"]).replace(" ", ""))].append(m)
        else:
            idx[(m["ilce_id"], fold(m["koyAdi"]).replace(" ", ""))].append(m)
    by_name = 0
    no_parent = 0
    amb = 0
    for q, o in items.items():
        pi = [parent_ilce[p] for p in o.get("parent", ()) if p in parent_ilce]
        if not pi:
            no_parent += 1
            continue
        lab = fold(clean_label(o["trLabel"])).replace(" ", "")
        hits = [m for p in pi for m in idx.get((p["kimlikNo"], lab), [])]
        if hits:
            by_name += 1
            if len({h["kimlikNo"] for h in hits}) > 1 and id_kind == "mahalle":
                amb += 1
            res.setdefault(q, ("ad", hits[0]))
    print(f"   (ilçe, katlanmış ad) ile eşleşen: {by_name} (%{100 * by_name / n:.1f}) | P131 ilçeye çözülemeyen: {no_parent}"
          f" | birden çok NVİ adayı: {amb}")
    print(f"   kimlik VEYA ad ile toplam bağlanan: {len(res)} (%{100 * len(res) / n:.1f})")
    return items, res


mitems, mres = analyze("MAHALLE (Q17051044)", load_wd("wd_mahalle"), "nbhId", "mahalle")
kitems, kres = analyze("KÖY (Q1529096)", load_wd("wd_koy"), "villId", "koy")
eitems, eres = analyze("ESKİ KÖY (Q136544643)", load_wd("wd_eski_koy"), "villId", "koy")

# NVİ tarafından kapsam: tip1 mahalle ve köy (koyKayitNo) başına koordinat
coord_by_kimlik = {}
for items, res in ((mitems, mres),):
    for q, (how_, m) in res.items():
        if items[q].get("coord"):
            coord_by_kimlik[m["kimlikNo"]] = next(iter(items[q]["coord"]))
coord_by_koy = {}
for items, res in ((kitems, kres), (eitems, eres)):
    for q, (how_, m) in res.items():
        if items[q].get("coord"):
            coord_by_koy[m["koyKayitNo"]] = next(iter(items[q]["coord"]))

mah1 = [m for m in nvi if m["mahalleTur"] == 1]
koys = {}
for m in nvi:
    if m["koyAdi"] != "MERKEZ" and m["koyKurumBelediyeTur"] != 4:
        koys[m["koyKayitNo"]] = m
print("\n== NVİ kapsamı (Wikidata koordinatı)")
c1 = sum(1 for m in mah1 if m["kimlikNo"] in coord_by_kimlik)
print(f"   tip1 mahalle: {c1}/{len(mah1)} (%{100 * c1 / len(mah1):.1f})")
c2 = sum(1 for k in koys if k in coord_by_koy)
print(f"   köy (koyKayitNo): {c2}/{len(koys)} (%{100 * c2 / len(koys):.1f})")
# alt birimler (mevki/mezra) köy koordinatı ile kapsanır
sub = [m for m in nvi if m["mahalleTur"] in (3, 4, 5, 6)]
c3 = sum(1 for m in sub if m["koyKayitNo"] in coord_by_koy)
print(f"   köy alt birimleri (3/4/5/6), köy koordinatından: {c3}/{len(sub)} (%{100 * c3 / len(sub):.1f})")

per_il = defaultdict(lambda: [0, 0])
for m in mah1:
    per_il[m["_il"]][1] += 1
    per_il[m["_il"]][0] += m["kimlikNo"] in coord_by_kimlik
for k, m in koys.items():
    per_il[m["_il"]][1] += 1
    per_il[m["_il"]][0] += k in coord_by_koy
rows = sorted(((il, a, b, 100 * a / b) for il, (a, b) in per_il.items()), key=lambda x: x[3])
print("   il başına (mahalle+köy) koordinat kapsamı — en düşük 15:", [(r[0], f"%{r[3]:.0f}") for r in rows[:15]])
print("   en yüksek 10:", [(r[0], f"%{r[3]:.0f}") for r in rows[-10:]])
vals = sorted(r[3] for r in rows)
print(f"   medyan il kapsamı %{vals[len(vals) // 2]:.0f}; <%50 olan il: {sum(1 for v in vals if v < 50)}")
write_csv(DERIVED / "wd_coord_coverage_per_il.csv", ["il", "koordinatli", "toplam", "yuzde"], rows)

# kimlik eşleşmesi çıktı
write_csv(DERIVED / "wd_nvi_link.csv", ["qid", "sinif", "yontem", "nvi_kimlikNo", "nvi_koyKayitNo", "coord", "wd_label"],
          [(q, cls, how_, m["kimlikNo"], m["koyKayitNo"], next(iter(items[q].get("coord", {""}))), items[q]["trLabel"])
           for cls, items, res in (("mahalle", mitems, mres), ("koy", kitems, kres), ("eski_koy", eitems, eres))
           for q, (how_, m) in res.items()])

# eski köy (6360) örnekleri
print("\n== ESKİ KÖY (Q136544643) örnekleri:",
      [(o["trLabel"], sorted(o.get("dissolved", {""}))[0][:4], [mitems.get(r, {}).get("trLabel", r) for r in o.get("replacedBy", ())])
       for o in list(eitems.values())[:12]])
print("   P576 yılı dağılımı (eski köy):", Counter(sorted(o["dissolved"])[0][:4] for o in eitems.values() if o.get("dissolved")))
print("   P576 yılı dağılımı (köy sınıfı):", Counter(sorted(o["dissolved"])[0][:4] for o in kitems.values() if o.get("dissolved")).most_common(8))
both = sum(1 for o in mitems.values() if o.get("villId"))
print("   mahalle sınıfında olup TÜİK köy kimliği (P13588) de taşıyan:", both,
      "(6360 ile mahalleye dönmüş köy adayı)")
print("   köy sınıfı + mahalle sınıfı aynı öğede:", len(set(mitems) & set(kitems)))
wb = wbelde
print("\n== BELDE (Q815324):", len(wb), "öğe | P576:", sum(1 for o in wb.values() if o.get("dissolved")),
      "| P1366:", sum(1 for o in wb.values() if o.get("replacedBy")),
      "| P576 yılları:", Counter(sorted(o["dissolved"])[0][:4] for o in wb.values() if o.get("dissolved")).most_common(5))

# 6360 izi: mahalle öğesinde YerelNet köy kimliği (P2123) = eskiden köy
yn = [(q, o) for q, o in mitems.items() if o.get("yerelnet")]
yn_linked = [(q, mres[q][1]) for q, o in yn if q in mres]
print("\n== 6360 izi (Wikidata)")
print("   YerelNet köy kimliği (P2123) taşıyan MAHALLE öğesi:", len(yn), "| bunlardan NVİ'ye bağlı:", len(yn_linked))
print("   NVİ tip dağılımı:", Counter(m["mahalleTur"] for _, m in yn_linked))
print("   il başına (ilk 10):", Counter(m["_il"] for _, m in yn_linked).most_common(10))
bs = {"ADANA", "ANKARA", "ANTALYA", "AYDIN", "BALIKESİR", "BURSA", "DENİZLİ", "DİYARBAKIR", "ERZURUM", "ESKİŞEHİR",
      "GAZİANTEP", "HATAY", "İSTANBUL", "İZMİR", "KAHRAMANMARAŞ", "KAYSERİ", "KOCAELİ", "KONYA", "MALATYA", "MANİSA",
      "MARDİN", "MERSİN", "MUĞLA", "ORDU", "SAKARYA", "SAMSUN", "ŞANLIURFA", "TEKİRDAĞ", "TRABZON", "VAN"}
in_bs = sum(1 for _, m in yn_linked if m["_il"] in bs)
print(f"   bunların büyükşehir illerinde olanı: {in_bs} (%{100 * in_bs / max(1, len(yn_linked)):.1f})")
write_csv(DERIVED / "wd_6360_aday.csv", ["qid", "yerelnet_koy_id", "nvi_kimlikNo", "il", "ilce", "ad"],
          [(q, ";".join(sorted(mitems[q]["yerelnet"])), m["kimlikNo"], m["_il"], m["_ilce"], m["adi"]) for q, m in yn_linked])
