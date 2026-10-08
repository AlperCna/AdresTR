# 0005. MIT code, separately licensed data

- Status: accepted · Date: 2026-10-08

## Decision
Code is MIT. Data provenance and licenses are listed in `data/LICENSE-DATA.md` and packed with
`AdresTR.Data`. Only MIT/CC0/CC BY-compatible sources enter the default packages. ODbL
(OpenStreetMap-derived) data never enters them; if published, it ships as a separate ODbL package.
The benchmark dataset is published separately (synthetic: CC BY 4.0; OSM-derived: ODbL).
