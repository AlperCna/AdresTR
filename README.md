# AdresTR

**Turkish free-text address parser, normalizer and validator for .NET.**
Türkçe serbest metin adresleri ayrıştıran, normalize eden ve resmi il/ilçe/mahalle hiyerarşisine göre doğrulayan .NET kütüphanesi. → [Türkçe README](README.tr.md)

[![CI](https://github.com/AlperCna/AdresTR/actions/workflows/ci.yml/badge.svg)](https://github.com/AlperCna/AdresTR/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![Status](https://img.shields.io/badge/status-early%20development-orange)

> 🚧 **Early development.** The roadmap and the research behind it are public: [plan](docs/plan/PLAN.md) · [research](docs/plan/ARASTIRMA.md) · [decisions](docs/adr/).

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

## What it will do (v1.0)

```csharp
var parser = AddressParser.CreateDefault();
var result = parser.Parse("kadikoy caferaga mh moda cd no:12 d3 istanbul");

result.Mahalle.Value;          // "Caferağa"  (id + confidence)
result.ToCanonicalString();    // "Caferağa Mah. Moda Cad. No:12 D:3 34710 Kadıköy/İstanbul"
result.Corrections;            // diacritics, abbreviations, inferred postal code…
```

- **Parse** il, ilçe, mahalle/köy, semt, street (type + name), site, blok, door, floor, flat, postal code.
- **Normalize** to official names and stable IDs; understands *semt* names (Moda → Caferağa) and pre-2014 village names.
- **Validate** the hierarchy and postal code consistency.
- **Explain** every correction, with per-field confidence and top-k alternatives.
- **Offline & deterministic**, no ICU dependency — runs in containers, Native AOT and the browser (Blazor WASM).

## Available today

`AdresTR.Text.TurkishText` — culture-independent Turkish text handling ([why it matters](docs/adr/0002-icu-independent-turkish-text.md)):

```csharp
TurkishText.Fold("  KADIKÖY’de Şişli ");   // "kadikoy'de sisli"  (matching key)
TurkishText.ToUpperTr("istanbul");               // "İSTANBUL"
TurkishText.ToTitleTr("ığdır");                  // "Iğdır"
TurkishText.Normalize("i̇stanbul");          // "istanbul" (repairs JS/Python lowercasing of İ)
```

Results are identical with or without ICU and regardless of `CultureInfo.CurrentCulture`; CI verifies this.

## Roadmap

| Phase | Scope | Status |
|---|---|---|
| 0 | Repo, CI, ADRs | ✅ |
| 1 | Gazetteer: il / ilçe / mahalle / postal codes, aliases, versioned binary format | 🔄 |
| 2 | Turkish text core | ✅ |
| 3 | Benchmark: synthetic + real + challenge sets, metrics, baselines | ⏳ |
| 4 | Parser MVP with confidence and corrections log | ⏳ |
| 5 | NuGet v0.1 | ⏳ |
| 6 | REST API + Docker | ⏳ |
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
