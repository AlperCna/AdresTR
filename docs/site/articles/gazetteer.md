# Gazetteer and data licenses

The bundled snapshot (`2026.10`) has 81 il, 973 ilçe and 78,790 units (mahalle, köy, mezra, mevkii, OSB …) with postal
codes, 18,816 aliases (semt names, pre-2014 village names, short forms) and Wikidata ids/coordinates where available.

| Data | Source | License |
|---|---|---|
| Names, postal codes, PTT delivery area | PTT-derived lists (muratgozel/turkey-neighbourhoods, epigra/tr-geozones) | MIT |
| Coordinates, Wikidata ids, former-village aliases | Wikidata | CC0 1.0 |
| Curated semt and name aliases | `data/curated` | CC0 1.0 |

**Not bundled:** street names. The only complete street list (NVİ) is behind a CAPTCHA and has no license; OpenStreetMap
streets (ODbL) would have to ship as a separate, share-alike package. The parser recognizes streets by pattern.

The data is versioned (`Gazetteer.DataVersion`), keeps stable ids and can be replaced at runtime with
`Gazetteer.Load(stream)`. Details: [LICENSE-DATA.md](https://github.com/AlperCna/AdresTR/blob/main/data/LICENSE-DATA.md).
