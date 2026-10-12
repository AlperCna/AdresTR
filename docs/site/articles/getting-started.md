# Getting started

```bash
dotnet add package AdresTR.Data --prerelease
```

`AdresTR.Data` contains the bundled gazetteer (81 il, 973 ilçe, 78,790 mahalle/köy/… units, postal codes, aliases) and
depends on `AdresTR` (parser, text utilities, gazetteer model).

```csharp
using AdresTR;
using AdresTR.Data;

ParseResult r = TurkishGazetteer.Parser.Parse("ALSANCAK MAH. 1453 SK. NO:5/2 KONAK/İZMİR");

r.Province?.Name;        // İzmir
r.District?.Name;        // Konak
r.Unit?.Name;            // Alsancak   (null when the text is ambiguous)
r.Street?.Value;         // 1453  (r.StreetType == StreetType.Sokak)
r.DoorNumber?.Value;     // 5
r.Flat?.Value;           // 2
r.Confidence;            // calibrated probability that the parse is correct
```

`TurkishGazetteer.Parser` is a shared, thread-safe instance. Create your own with `new AddressParser(gazetteer)` to use a
different data snapshot (`Gazetteer.Load(stream)`).

## Text utilities

[TurkishText](xref:AdresTR.Text.TurkishText) works without ICU or any culture: `Fold`, `Normalize`, `ToUpperTr`,
`ToLowerTr`, `ToTitleTr`. The library behaves identically in containers, Native AOT and Blazor WebAssembly.
