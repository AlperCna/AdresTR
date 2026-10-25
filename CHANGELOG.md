# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- `ParseResult.BuildingNumber` (REST: `components.buildingNumber`): "Bina No" / "Bina Numarası" is its own field instead of overwriting the
  door number ("No 5 Bina No 2" → door 5, building 2). `ToCanonicalString()` writes it as "Bina No:2".

### Changed
- Tokenizer: a street abbreviation glued to an unmarked name is split when a door label follows ("Erguvancd, No: 37",
  "100. Yılsk, No.34"), without needing street data. Synthetic test exact match 98.0% → 98.2%, synthetic dev 99.6% → 99.8%;
  real and challenge sets unchanged.
- Parser: "3. Kat 7 Nolu Daire", "İç Kapı No 3" and "Kat:Giriş" are read as floor/flat (the door number is no longer overwritten);
  a building name cannot be a bare number ("No:100 İş Hanı"); `ToCanonicalString()` writes the real building type
  ("Plaza", "İş Hanı", "Rezidans", "Apt.") instead of "Sitesi"/"Apt." for everything.

## [0.1.0-preview.5] - 2026-10-22

### Added
- Launch material (`docs/launch`), Hugging Face dataset card (`eval/huggingface`), clients release workflow.

### Changed
- Parser: glued village names ("Akpınarköyü", "Dikenliköyü") are split even when the glued form is a gazetteer alias key, and
  glued "cadde"/"bulvar" ("zafercadde") are split into name and type. Synthetic test exact match 97.4% → 98.0%.
- Parser: door ranges keep a trailing letter ("No:17-19/B"); an unlabeled number right before a street type ("Cd. 2190.sk") is a
  street name, not a door. Real test exact match 95.8% → 96.1%.
- Parser: "üniversitesi" is no longer split into a site ("Üniver Sitesi"); a bare "N:5" is a door number; `ToCanonicalString()`
  writes "Apt." for apartments instead of "Sitesi".
- Parser: a written ilçe is no longer silently replaced by the only ilçe of the il that has a neighbourhood of that name
  ("Kuzey Mah. … Gebze Kocaeli" now stays Gebze with no unit instead of resolving to Körfez).
- Parser: "Bul." and "Bld." are recognised as boulevard abbreviations.
- Parser: bare "Küme Evler" street type, "Krş." landmark, units named like a site; ADR-0004 updated for AdresTR.Streets.

## [0.1.0-preview.4] - 2026-10-12

### Added
- `AdresTR.Streets` (ODbL, separate package): 248,712 street names for 968 of 973 ilçe from OpenStreetMap; `StreetIndex` in the core
  lets `AddressParser` verify streets (`StreetVerified`) and fix street typos. `OsmStreets.Parser` for a ready-made instance.

## [0.1.0-preview.3] - 2026-10-12

### Added
- Curated semt aliases with source URLs (Bahariye, Söğütlüçeşme, Draman, Beysukent, İncirli) and a Wikipedia-based
  candidate extractor (`data/scripts/build_semt_from_wikipedia.py`); gazetteer now has 18,827 aliases.
- Documentation site (DocFX) at /docs/: guide, API reference, REST/self-hosting, integrations, benchmark, privacy.
- API clients: `clients/python` (`adrestr-client`, sync + async, httpx) and `clients/typescript` (`adrestr-client`, fetch, zero
  dependencies) covering parse/trace, batch, CSV, validate, match, dedupe, reference data and autocomplete; tested in CI.
- `AdresTR.AspNetCore` (`AddAdresTR()`, `[TurkishAddress]` validation attribute) and `AdresTR.FluentValidation`
  (`MustBeTurkishAddress()`); both published with each release.
- Playground: parse trace view, OpenStreetMap map, "Match" tab (same-place comparison), il → ilçe → mahalle form, TR/EN toggle.
- `AddressParser.Parse(text, new ParseOptions { IncludeTrace = true })` returns a `ParseTrace` (tokens, hypothesis count,
  best alternative readings with scores) that explains how a result was reached; `POST /v1/parse?trace=true`.
- REST: `POST /v1/match` (same place?) and `POST /v1/dedupe` (duplicate groups).
- `AddressMatcher`: decides whether two addresses are the same place (`MatchLevel`: Different … SameFlat, with a
  confidence-discounted score and human-readable reasons) and groups duplicates (blocking by resolved mahalle).

## [0.1.0-preview.2] - 2026-10-09

### Added
- REST API (Faz 6): `/v1/parse`, `/v1/parse/batch`, `/v1/parse/csv`, `/v1/validate`, reference and autocomplete
  endpoints; per-IP rate limiting, output caching, CORS, validation, privacy notice, OpenAPI + Scalar.
- Multi-arch, non-root chiseled container published to GHCR on every release; deployment guide (`docs/deploy.md`).
- In-browser playground (Faz 7, Blazor WebAssembly AOT on GitHub Pages): live highlighting, resolution, corrections,
  candidates, JSON and batch mode with CSV download; addresses never leave the device.

### Changed
- Gazetteer loads about 2× faster: payload inflated in one pass, name keys computed once, plain dictionaries.

## [0.1.0-preview.1] - 2026-10-08

First preview on NuGet: `AdresTR` and `AdresTR.Data`.

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
- Samples (`samples/QuickStart`, `samples/CsvCleaner`), package icon and NuGet README, tag-triggered release
  workflow with NuGet Trusted Publishing (no stored API key).

[Unreleased]: https://github.com/AlperCna/AdresTR/compare/v0.1.0-preview.5...HEAD
[0.1.0-preview.5]: https://github.com/AlperCna/AdresTR/compare/v0.1.0-preview.4...v0.1.0-preview.5
[0.1.0-preview.4]: https://github.com/AlperCna/AdresTR/compare/v0.1.0-preview.3...v0.1.0-preview.4
[0.1.0-preview.3]: https://github.com/AlperCna/AdresTR/compare/v0.1.0-preview.2...v0.1.0-preview.3
[0.1.0-preview.2]: https://github.com/AlperCna/AdresTR/compare/v0.1.0-preview.1...v0.1.0-preview.2
[0.1.0-preview.1]: https://github.com/AlperCna/AdresTR/releases/tag/v0.1.0-preview.1
