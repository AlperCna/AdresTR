# Curated tables / Elle bakılan tablolar

Community-maintained tables merged into `data/staging/` by `data/scripts/build_staging.py`. Licensed CC0 1.0.
Topluluk tarafından bakılan tablolar; `build_staging.py` bunları `data/staging/alias.csv` içine katar.

| File | Purpose |
|---|---|
| `semt_alias.csv` | Semt (colloquial area) → official mahalle(s). `mahalleler` is `|`-separated; one alias row per mahalle. |
| `abbreviations.draft.csv` | Draft abbreviation/typo lexicon for the parser (Faz 4). Rows marked unverified need review before use. |

Rules: no personal data; cite a source in `kaynak`/`not` when possible; one semt may map to several mahalle.

## Sourcing policy for semt_alias.csv

Every row needs a source URL that states the mapping (tr.wikipedia, municipality, İstanbul Ansiklopedisi …). Never use OpenStreetMap
(ODbL). Only add a semt that is not an official mahalle name of that ilçe. `data/scripts/build_semt_from_wikipedia.py` proposes
candidates from Wikipedia semt articles; a human reviews them (partial or surrounding-area sentences are rejected).
