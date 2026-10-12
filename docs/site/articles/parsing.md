# Parsing

[AddressParser.Parse](xref:AdresTR.AddressParser) returns a [ParseResult](xref:AdresTR.ParseResult).

## Components and offsets

`Il`, `Ilce`, `Mahalle`, `Semt`, `Street` (+ `StreetType`), `Site`, `Blok`, `DoorNumber`, `Floor`, `Flat`, `PostalCode` and
`Landmark` are [AddressComponent](xref:AdresTR.AddressComponent)s: normalized `Value`, the `Text` as written and
`Start`/`End` offsets into the input. They describe **what the text says**.

## Resolution

`Province`, `District` and `Unit` describe **what the text determines**, using the official gazetteer. A component can be
written without being determined, and the other way round: "Moda, Kadıköy" determines the il (İstanbul) without writing it.

AdresTR never guesses. "Cumhuriyet Mah. Atatürk Cad. No:5" matches hundreds of neighbourhoods, so `Unit` is `null` and
`UnitCandidates` lists them ranked.

## Confidence

`Confidence` is the probability mass of the chosen reading among the parser's N-best alternatives, with a temperature fitted
on the dev splits of the benchmark (expected calibration error ≈ 0.04 on real addresses).

## Corrections

[Corrections](xref:AdresTR.Correction) explain every change: `Diacritics` (kadikoy → Kadıköy), `Typo`, `Semt` (Moda →
Caferağa), `HistoricName` (pre-2014 "X Köyü" → X Mahallesi), `Alias` (Urfa → Şanlıurfa), `Split` (`147sok`), `Inferred`,
`PostalCodeConflict`, `Duplicate`.

## Trace

```csharp
var r = parser.Parse(text, new ParseOptions { IncludeTrace = true });
r.Trace.Tokens; r.Trace.Alternatives;   // how the result was reached
```

## What it handles

Abbreviations (`mh`, `cd`, `sk`, `blv`), ASCII-only and UPPERCASE input, glued words (`CaferağaMah.`, `147sok`), numbered
streets (`1203/5 Sk.`), `No:17/5` (door 17, flat 5), `17/A`, floors, site/block, OSB, küme evler, phone numbers, landmarks
("PTT karşısı"), repeated names and typos (scoped fuzzy matching).
