# AdresTR

Turkish free-text address parser, normalizer and validator for .NET.
Türkçe serbest metin adresleri ayrıştırır, normalize eder ve resmi il/ilçe/mahalle hiyerarşisine göre doğrular.

```csharp
using AdresTR;
using AdresTR.Data;   // package AdresTR.Data: bundled gazetteer + shared parser

ParseResult r = TurkishGazetteer.Parser.Parse("kadikoy caferaga mh moda cd no:12 d3 istanbul");

r.Unit;                 // Caferağa (mahalle) — null when the text is ambiguous
r.District;             // Kadıköy
r.DoorNumber; r.Flat;   // "12", "3"
r.Confidence;           // calibrated probability that the parse is correct
r.Corrections;          // Diacritics: kadikoy → Kadıköy, …
r.ToCanonicalString();  // "Caferağa Mah. Moda Cad. No:12 D:3 34710 Kadıköy/İstanbul"
```

- Parses il, ilçe, mahalle/köy, semt, street (type + name), site, blok, door, floor, flat, postal code, landmark.
- Resolves to official names and stable ids; understands semt names, pre-2014 village names, abbreviations,
  glued words, typos and ASCII-only input. Never guesses an ambiguous neighbourhood.
- Offline, deterministic, no ICU dependency (works in containers, Native AOT and Blazor WebAssembly).

| Test set | AdresTR exact match | libpostal |
|---|---:|---:|
| Synthetic, noisy (2,000) | 97.4% | 10.0% |
| Real public-institution addresses (1,200) | 95.8% | 26.2% |
| Hand-written hard cases (167) | 86.8% | 24.6% |

**Packages:** `AdresTR` (parser, text utilities, gazetteer model) · `AdresTR.Data` (bundled 2026.10 gazetteer: 81 il,
973 ilçe, 78,790 units with postal codes; depends on `AdresTR`).

Documentation, benchmark and data licenses: https://github.com/AlperCna/AdresTR ·
[Benchmark](https://github.com/AlperCna/AdresTR/tree/main/eval) ·
[Data sources](https://github.com/AlperCna/AdresTR/blob/main/data/LICENSE-DATA.md) ·
[Changelog](https://github.com/AlperCna/AdresTR/blob/main/CHANGELOG.md)

AdresTR stores and sends nothing: all processing is local. Code: MIT. Data: see LICENSE-DATA.md in the package.
