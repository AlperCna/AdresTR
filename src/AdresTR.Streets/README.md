# AdresTR.Streets

Optional street names per ilçe for [AdresTR](https://github.com/AlperCna/AdresTR), derived from OpenStreetMap.

```csharp
using AdresTR.Streets;

var r = OsmStreets.Parser.Parse("Caferağa Mah. Modaa Cad. No:12 Kadıköy");
r.Street!.Value;       // "Moda"  (typo corrected, reported in r.Corrections)
r.StreetVerified;      // true   (false = not found in OpenStreetMap, null = no ilçe / no index)
```

**License: ODbL 1.0.** The street data is © OpenStreetMap contributors and licensed under the
[Open Database License](https://opendatacommons.org/licenses/odbl/1-0/) (attribution + share-alike for derived databases). This
is why it is a separate package: `AdresTR` and `AdresTR.Data` stay MIT/CC0. Keep the attribution
"© OpenStreetMap contributors" where you show data derived from this package. OpenStreetMap coverage in Türkiye is partial;
`StreetVerified == false` is a hint, not proof.
