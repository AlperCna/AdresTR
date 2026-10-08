import sys, requests, csv, io
UA = "AdresTR-data/0.1 (+https://github.com/AlperCna/AdresTR)"
q = sys.argv[1] if len(sys.argv) > 1 else sys.stdin.read()
r = requests.get("https://query.wikidata.org/sparql", params={"query": q}, headers={"User-Agent": UA, "Accept": "text/csv"}, timeout=300)
r.raise_for_status()
sys.stdout.reconfigure(encoding="utf-8")
print(r.text[:20000])
