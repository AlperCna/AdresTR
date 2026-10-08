# Data sources and licenses

The AdresTR **code** is MIT-licensed (see `LICENSE`). **Data** is licensed separately, per source.
Nothing in `AdresTR` or `AdresTR.Data` is derived from OpenStreetMap (ODbL); OSM-derived
artifacts, if any, are published as separate packages under ODbL.

| Data | Source | License | Status |
|---|---|---|---|
| il (81), plate codes | Public administrative facts; names cross-checked with PTT-derived lists | Facts — not copyrightable | ✅ 2026.10 |
| ilçe (973) | PTT-derived lists (below) | MIT | ✅ 2026.10 |
| mahalle / köy / mezra / mevkii, postal codes, PTT semt | [muratgozel/turkey-neighbourhoods](https://github.com/muratgozel/turkey-neighbourhoods) (© Murat Gözel), cross-checked with [epigra/tr-geozones](https://github.com/epigra/tr-geozones) (© Epigra) — both PTT-derived | MIT | ✅ 2026.10 |
| Coordinates, Wikidata QIDs, NVİ ids (P12883/P13588), former-village aliases (P2123) | [Wikidata](https://www.wikidata.org/) | CC0 1.0 | ✅ 2026.10 |
| il / ilçe boundaries | [HDX COD-AB-TUR](https://data.humdata.org/dataset/cod-ab-tur) (Harita Genel Müdürlüğü) | CC BY-IGO | not used yet |
| Curated semt and name aliases | `data/curated/` (community-maintained) | CC0 1.0 | ✅ started |

Street (cadde/sokak) data is **not** bundled. See `docs/adr/0004-no-bundled-street-data.md`.

The exact input files, their SHA-256 hashes and snapshot dates are listed in `data/staging/SOURCES.json`.
This table is updated whenever a data snapshot is published. Each binary gazetteer embeds the
source hashes and the snapshot version (e.g. `2026.10`).
