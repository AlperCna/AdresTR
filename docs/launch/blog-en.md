# Building the first open benchmark (and parser) for Turkish addresses

`kadikoy caferaga mh moda cd no:12 d3 istanbul`

To a person this is clear. To a computer: is "kadikoy" a district or a neighbourhood? What is "mh"? Is "d3" a flat or a
compass direction? Wrong address means wrong delivery, which is why Hepsiburada ran a TEKNOFEST 2025 hackathon on it.

I looked for a **free, offline, explainable** Turkish address parser and found none:

- Google's Address Validation API does not support Turkey.
- libpostal has no neighbourhood or district concept for Turkey; its Turkish dictionaries date from 2016–17.
- Hackathon solutions output opaque cluster ids, and the datasets are closed.
- There is nothing on NuGet.

## AdresTR

An open-source (MIT) .NET library: rules, the official gazetteer and scoped fuzzy matching. No server, no LLM call.

```csharp
var r = TurkishGazetteer.Parser.Parse("kadikoy caferaga mh moda cd no:12 d3 istanbul");
r.Unit.Id;               // 34230005 (Caferağa neighbourhood)
r.ToCanonicalString();   // "Caferağa Mah. Moda Cad. No:12 D:3 34710 Kadıköy/İstanbul"
r.Corrections;           // kadikoy → Kadıköy, ...
```

It parses il, ilçe, mahalle/köy, street, door, flat and postal code and resolves them to official ids. It understands semt names
(Moda → Caferağa), pre-2014 village names, abbreviations, glued words (`147sok`), typos and ASCII-only input. It **never guesses**:
"Cumhuriyet Mah. No:5" exists in hundreds of places, so it returns no unit and ranks the candidates. Every correction is explained and
the confidence is calibrated. It can also tell whether two addresses are the same place and group duplicates.

Try it in the browser (everything runs client-side, nothing is sent anywhere): https://alpercna.github.io/AdresTR/

## The Turkish-I problem, measured

`"İSTANBUL".ToLower()` depends on the culture. In Alpine containers and Blazor WebAssembly, .NET falls into invariant mode where
`tr-TR` silently loses its rules and `Normalize()` is a no-op (I measured it). So the core never touches ICU or cultures: it uses
hand-written tables, and the whole test suite runs with and without ICU in CI. A property-based tester (CsCheck) found two real
edge cases (`ı` + combining dot) that example tests missed.

## Benchmark first

I built the evaluation sets before the parser, because "is it good?" needs a number:

| Test set | AdresTR | libpostal | regex |
|---|---:|---:|---:|
| Synthetic, noisy (2,000) | **98.0%** | 10.0% | 14.0% |
| Real public-institution addresses (1,200) | **95.8%** | 26.2% | 8.2% |
| Hand-written hard cases (167) | **86.8%** | 24.6% | 21.0% |

"Exact match" = every component correct. Real addresses come from İBB and İzmir open data (no personal data). Test splits were never
used for tuning. On the hard cases dev is 96% and test 86.8%: the gap is a warning that my rules overfit dev.

The dataset and the evaluator are open: https://github.com/AlperCna/AdresTR/tree/main/eval

## Honest limits

- OpenStreetMap street coverage is partial; streets live in a separate (ODbL) package, and "not found" is a hint, not proof.
- Semt → mahalle mappings are curated by hand with sources, so there are few so far.
- NVİ's address registry is behind a CAPTCHA with no license; the data comes from PTT-derived (MIT) lists and Wikidata (CC0).

## Install

```bash
dotnet add package AdresTR.Data --prerelease
```

There is also a REST API and Docker image, ASP.NET Core / FluentValidation packages, and Python and TypeScript clients.
Contributions are very welcome: new abbreviations, semt mappings and hard (non-personal) examples: https://github.com/AlperCna/AdresTR
