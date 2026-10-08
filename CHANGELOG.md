# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Repository scaffold, CI (Linux, Windows and globalization-invariant mode), ADRs 0000–0010.
- `AdresTR.Text.TurkishText`: ICU-independent folding, Turkish casing and normalization.
- API skeleton with OpenAPI/Scalar, health check and `POST /v1/text/normalize`.
- Gazetteer model (`Gazetteer`, `Province`, `District`, `SettlementUnit`, aliases), validating
  `GazetteerBuilder`, compact zlib binary format, name/alias/postal-code lookups.
- `AdresTR.Data` with the bundled 2026.10 snapshot: 81 il, 973 ilçe, 78,790 units, 18,816 aliases
  (PTT-derived MIT data + Wikidata CC0), built reproducibly from `data/staging` by `tools/AdresTR.DataBuilder`.
- Benchmark (`eval/`): synthetic (3,000), real public-institution (1,500) and hand-written challenge (250) sets,
  evaluator with bootstrap CIs and calibration error, regex and libpostal baselines, CI accuracy gate.
- `AddressParser` (Faz 4): tokenizer with glued-word splitting, keyword lexicon, hypothesis classifier, beam
  solver, hierarchical il/ilçe/mahalle resolution with scoped fuzzy matching, calibrated confidence, ranked unit
  candidates, corrections log and canonical formatting. `TurkishGazetteer.Parser` for a shared instance.
  Test results: exact match 97.4% (synthetic), 95.8% (real), 86.8% (challenge).
