"""Wikidata SPARQL'den il / ilçe / mahalle / köy verisini CSV olarak indirir.

Kullanım:  python -I fetch_wikidata.py <cikti_klasoru>
Lisans: Wikidata CC0.
"""
import csv
import io
import sys
import time
from pathlib import Path

import requests

UA = "AdresTR-data/0.1 (+https://github.com/AlperCna/AdresTR)"
ENDPOINT = "https://query.wikidata.org/sparql"

PREFIX = """
PREFIX wd: <http://www.wikidata.org/entity/>
PREFIX wdt: <http://www.wikidata.org/prop/direct/>
PREFIX p: <http://www.wikidata.org/prop/>
PREFIX ps: <http://www.wikidata.org/prop/statement/>
PREFIX pq: <http://www.wikidata.org/prop/qualifier/>
PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
"""

QUERIES = {
    # İller: plaka (P395), Turkey province ID (P14358), ISO 3166-2 (P300)
    "wd_il": """
SELECT ?item ?trLabel ?plaka ?provId ?iso ?coord ?dissolved WHERE {
  ?item wdt:P31 wd:Q48336 .
  OPTIONAL { ?item rdfs:label ?trLabel FILTER(LANG(?trLabel)="tr") }
  OPTIONAL { ?item wdt:P395 ?plaka }
  OPTIONAL { ?item wdt:P14358 ?provId }
  OPTIONAL { ?item wdt:P300 ?iso }
  OPTIONAL { ?item wdt:P625 ?coord }
  OPTIONAL { ?item wdt:P576 ?dissolved }
}""",
    # İlçeler: üst il (P131), kaldırılma (P576), yerine geçen (P1366), Turkey district ID (P14366)
    "wd_ilce": """
SELECT ?item ?trLabel ?parent ?distId ?coord ?dissolved ?replacedBy ?inception WHERE {
  ?item wdt:P31 wd:Q1147395 .
  OPTIONAL { ?item rdfs:label ?trLabel FILTER(LANG(?trLabel)="tr") }
  OPTIONAL { ?item wdt:P131 ?parent }
  OPTIONAL { ?item wdt:P14366 ?distId }
  OPTIONAL { ?item wdt:P625 ?coord }
  OPTIONAL { ?item wdt:P576 ?dissolved }
  OPTIONAL { ?item wdt:P1366 ?replacedBy }
  OPTIONAL { ?item wdt:P571 ?inception }
}""",
    # Mahalleler (Q17051044): Turkey neighborhood ID (P12883, TÜİK)
    "wd_mahalle": """
SELECT ?item ?trLabel ?parent ?coord ?nbhId ?villId ?yerelnet ?dissolved ?replacedBy ?replaces WHERE {
  ?item wdt:P31 wd:Q17051044 .
  OPTIONAL { ?item rdfs:label ?trLabel FILTER(LANG(?trLabel)="tr") }
  OPTIONAL { ?item wdt:P131 ?parent }
  OPTIONAL { ?item wdt:P625 ?coord }
  OPTIONAL { ?item wdt:P12883 ?nbhId }
  OPTIONAL { ?item wdt:P13588 ?villId }
  OPTIONAL { ?item wdt:P2123 ?yerelnet }
  OPTIONAL { ?item wdt:P576 ?dissolved }
  OPTIONAL { ?item wdt:P1366 ?replacedBy }
  OPTIONAL { ?item wdt:P1365 ?replaces }
}""",
    # Köyler (Q1529096): Turkey village ID (P13588, TÜİK), YerelNet (P2123)
    "wd_koy": """
SELECT ?item ?trLabel ?parent ?coord ?nbhId ?villId ?yerelnet ?dissolved ?replacedBy ?replaces WHERE {
  ?item wdt:P31 wd:Q1529096 .
  OPTIONAL { ?item rdfs:label ?trLabel FILTER(LANG(?trLabel)="tr") }
  OPTIONAL { ?item wdt:P131 ?parent }
  OPTIONAL { ?item wdt:P625 ?coord }
  OPTIONAL { ?item wdt:P12883 ?nbhId }
  OPTIONAL { ?item wdt:P13588 ?villId }
  OPTIONAL { ?item wdt:P2123 ?yerelnet }
  OPTIONAL { ?item wdt:P576 ?dissolved }
  OPTIONAL { ?item wdt:P1366 ?replacedBy }
  OPTIONAL { ?item wdt:P1365 ?replaces }
}""",
    # TÜİK kimlikli her şeyin P31 dağılımı (sınıf dışı kalanları görmek için)
    "wd_tuik_nbh": """
SELECT ?item ?id ?cls WHERE { ?item wdt:P12883 ?id . OPTIONAL { ?item wdt:P31 ?cls } }""",
    "wd_tuik_vill": """
SELECT ?item ?id ?cls WHERE { ?item wdt:P13588 ?id . OPTIONAL { ?item wdt:P31 ?cls } }""",
    # Beldeler (Q815324 "belde") ve eski köyler (Q136544643 "former village of Turkey")
    "wd_belde": """
SELECT ?item ?trLabel ?parent ?dissolved ?replacedBy WHERE {
  ?item wdt:P31 wd:Q815324 .
  OPTIONAL { ?item wdt:P1366 ?replacedBy }
  OPTIONAL { ?item rdfs:label ?trLabel FILTER(LANG(?trLabel)="tr") }
  OPTIONAL { ?item wdt:P131 ?parent }
  OPTIONAL { ?item wdt:P576 ?dissolved }
}""",
    "wd_eski_koy": """
SELECT ?item ?trLabel ?parent ?coord ?nbhId ?villId ?dissolved ?replacedBy ?cls WHERE {
  ?item wdt:P31 wd:Q136544643 .
  OPTIONAL { ?item wdt:P31 ?cls }
  OPTIONAL { ?item rdfs:label ?trLabel FILTER(LANG(?trLabel)="tr") }
  OPTIONAL { ?item wdt:P131 ?parent }
  OPTIONAL { ?item wdt:P625 ?coord }
  OPTIONAL { ?item wdt:P12883 ?nbhId }
  OPTIONAL { ?item wdt:P13588 ?villId }
  OPTIONAL { ?item wdt:P576 ?dissolved }
  OPTIONAL { ?item wdt:P1366 ?replacedBy }
}""",
    # Köy/mahalle öğelerinin P131 hedeflerinin (çoğu kasaba/belediye öğesi) kendi P131'leri
    # ve bir üst seviye: köy -> kasaba -> ilçe zincirini çözmek için.
    "wd_parent_up": """
SELECT DISTINCT ?parent ?parentLabel ?up ?up2 WHERE {
  { SELECT DISTINCT ?parent WHERE {
      { ?x wdt:P31 wd:Q1529096 } UNION { ?x wdt:P31 wd:Q17051044 }
      ?x wdt:P131 ?parent . } }
  OPTIONAL { ?parent rdfs:label ?parentLabel FILTER(LANG(?parentLabel)="tr") }
  OPTIONAL { ?parent wdt:P131 ?up . OPTIONAL { ?up wdt:P131 ?up2 } }
}""",
}


def run(name: str, query: str, out_dir: Path) -> None:
    for attempt in range(4):
        r = requests.post(
            ENDPOINT,
            data={"query": PREFIX + query},
            headers={"User-Agent": UA, "Accept": "text/csv"},
            timeout=320,
        )
        if r.status_code == 200:
            break
        print(f"{name}: HTTP {r.status_code}, retry {attempt}", file=sys.stderr)
        time.sleep(15 * (attempt + 1))
    r.raise_for_status()
    text = r.content.decode("utf-8")
    rows = list(csv.reader(io.StringIO(text)))
    (out_dir / f"{name}.csv").write_text(text, encoding="utf-8", newline="")
    print(f"{name}: {len(rows) - 1} satır")


def main() -> None:
    out_dir = Path(sys.argv[1])
    out_dir.mkdir(parents=True, exist_ok=True)
    only = set(sys.argv[2:])
    for name, q in QUERIES.items():
        if only and name not in only:
            continue
        run(name, q, out_dir)
        time.sleep(3)


if __name__ == "__main__":
    main()
