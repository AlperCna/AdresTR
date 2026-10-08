# Data sources and licenses

The AdresTR **code** is MIT-licensed (see `LICENSE`). **Data** is licensed separately, per source.
Nothing in `AdresTR` or `AdresTR.Data` is derived from OpenStreetMap (ODbL); OSM-derived
artifacts, if any, are published as separate packages under ODbL.

| Data | Source | License | Status |
|---|---|---|---|
| il (81), plate codes | Public administrative facts | Facts — not copyrightable | planned (Faz 1) |
| ilçe (973) | Cross-checked: PTT-derived lists, HDX COD-AB-TUR, NVİ snapshot | MIT / CC BY-IGO (attribution below) | planned (Faz 1) |
| mahalle / köy, postal codes | [muratgozel/turkey-neighbourhoods](https://github.com/muratgozel/turkey-neighbourhoods), [epigra/tr-geozones](https://github.com/epigra/tr-geozones) (PTT-derived) | MIT | planned (Faz 1) |
| Centroid coordinates | [Wikidata](https://www.wikidata.org/) | CC0 1.0 | planned (Faz 1) |
| il / ilçe boundaries & centroids | [HDX COD-AB-TUR](https://data.humdata.org/dataset/cod-ab-tur) (Harita Genel Müdürlüğü) | CC BY-IGO | planned (Faz 1) |
| Abbreviations, semt and historic-name aliases | Curated in `data/curated/` (community-maintained) | CC0 1.0 | in progress |

Street (cadde/sokak) data is **not** bundled. See `docs/adr/0004-no-bundled-street-data.md`.

This table is updated whenever a data snapshot is published. Each binary gazetteer embeds the
source hashes and the snapshot version (e.g. `2026.10`).
