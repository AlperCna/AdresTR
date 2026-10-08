# AdresTR

**Turkish free-text address parser, normalizer and validator for .NET.**
Türkçe serbest metin adresleri ayrıştıran, normalize eden ve resmi il/ilçe/mahalle hiyerarşisine göre doğrulayan .NET kütüphanesi. → [Türkçe README](README.tr.md)

[![CI](https://github.com/AlperCna/AdresTR/actions/workflows/ci.yml/badge.svg)](https://github.com/AlperCna/AdresTR/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/AdresTR.svg?label=NuGet)](https://www.nuget.org/packages/AdresTR.Data)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

> 🚧 **Preview release.** APIs may still change before 1.0. The roadmap and the research behind it are public: [plan](docs/plan/PLAN.md) · [research](docs/plan/ARASTIRMA.md) · [decisions](docs/adr/).

## Why?

Turkish addresses are typed as free text, and they are messy:

```
kadikoy caferaga mh moda cd no:12 d3 istanbul
atillamahallesi475sokibrahimapartno20kat4daire4
İSTANBUL ŞİŞLİ MECİDİYEKÖY MAH. 1203/5 SK. NO:17/A
```

E-commerce and logistics companies pay for this every day. Hepsiburada even ran a TEKNOFEST 2025 hackathon on it.
Yet there is **no free, offline, explainable** Turkish address parser:

- Google's Address Validation API does not support Turkey.
- libpostal has no *mahalle* or *ilçe* labels and its Turkish dictionaries date from 2016–17.
- Hackathon solutions output opaque cluster IDs, not reusable libraries.
- There is no public labeled Turkish address benchmark.

AdresTR aims to fill that gap, and to publish the first open Turkish address benchmark along the way.

## Quick start

```bash
dotnet add package AdresTR.Data --prerelease
```

```csharp
using AdresTR;
using AdresTR.Data;

ParseResult r = TurkishGazetteer.Parser.Parse("kadikoy caferaga mh moda cd no:12 d3 istanbul");

r.Unit;                 // Caferağa (mahalle, id 34230005) — null when the text is ambiguous
r.District;             // Kadıköy
r.Street;               // "Moda" (r.StreetType == StreetType.Cadde)
r.DoorNumber; r.Flat;   // "12", "3"
r.Confidence;           // calibrated probability that the parse is correct
r.Corrections;          // Diacritics: kadikoy → Kadıköy, …
r.ToCanonicalString();  // "Caferağa Mah. Moda Cad. No:12 D:3 34710 Kadıköy/İstanbul"
```

- **Parses** il, ilçe, mahalle/köy, semt, street (type + name), site, blok, door, floor, flat, postal code, landmark.
- **Resolves** to official names and stable ids; understands *semt* names (Moda → Caferağa), pre-2014 village
  names, abbreviations, glued words (`147sok`, `CaferağaMah.`), typos and ASCII-only input.
- **Never guesses:** "Cumhuriyet Mah." without an ilçe returns no unit, plus ranked candidates.
- **Explains** every correction and returns a calibrated confidence.
- **Offline & deterministic**, no ICU dependency, ~0.2–0.5 ms per address on one core.

## Benchmark

Test splits of the [AdresTR benchmark](eval/README.md) (never used for tuning). Exact match = every annotated
component correct; birim = the neighbourhood the text determines, resolved to the right gazetteer id.

| Test set | AdresTR | libpostal | regex baseline |
|---|---:|---:|---:|
| Synthetic (2,000, noisy) — exact match | **97.4%** | 10.0% | 14.0% |
| Real public-institution addresses (1,200) — exact match | **95.8%** | 26.2% | 8.2% |
| Hand-written hard cases (167) — exact match | **86.8%** | 24.6% | 21.0% |
| Real — birim accuracy | **100%** | – | 19.2% |
| Real — calibration error (ECE) | **0.036** | – | – |

Full tables with confidence intervals, per-field F1 and per-phenomenon breakdowns: [eval/results](eval/results/README.md).
libpostal has no mahalle/ilçe concept for Turkey, so it cannot return gazetteer ids.

## Also available

`AdresTR.Text.TurkishText` — culture-independent Turkish text handling ([why it matters](docs/adr/0002-icu-independent-turkish-text.md)):

```csharp
TurkishText.Fold("  KADIKÖY’de\u00A0Şişli ");   // "kadikoy'de sisli"  (matching key)
TurkishText.ToUpperTr("istanbul");               // "İSTANBUL"
TurkishText.ToTitleTr("ığdır");                  // "Iğdır"
```

`AdresTR.Data` — the bundled gazetteer (81 il, 973 ilçe, 78,790 mahalle/köy/… units with postal codes, 18,816 aliases):

```csharp
var g = TurkishGazetteer.Default;
g.FindProvinces("Urfa")[0].Entity.Name;                 // "Şanlıurfa"
g.FindUnitsByPostalCode("34710");                       // Caferağa, …
```

## Roadmap

| Phase | Scope | Status |
|---|---|---|
| 0 | Repo, CI, ADRs | ✅ |
| 1 | Gazetteer: il / ilçe / mahalle / postal codes, aliases, versioned binary format | ✅ (curation ongoing) |
| 2 | Turkish text core | ✅ |
| 3 | Benchmark: synthetic + real + challenge sets, metrics, baselines | ✅ |
| 4 | Parser MVP with calibrated confidence and corrections log | ✅ |
| 5 | NuGet v0.1 (`0.1.0-preview.1`) | ✅ |
| 6 | REST API + Docker ([deploy guide](docs/deploy.md)) | ✅ |
| 7 | In-browser playground | ⏳ |
| 8 | Docs, Hugging Face dataset, v1.0 | ⏳ |

## Data & licensing

Code: [MIT](LICENSE). Data is licensed per source; see [data/LICENSE-DATA.md](data/LICENSE-DATA.md).
Street data is not bundled ([ADR-0004](docs/adr/0004-no-bundled-street-data.md)).

## Privacy

AdresTR is a library: it stores nothing and sends nothing. See [ADR-0009](docs/adr/0009-privacy-by-design.md).

## Contributing

Contributions are welcome, especially abbreviation variants, *semt* aliases and tricky real-world examples
(without personal data). See [CONTRIBUTING.md](CONTRIBUTING.md).
