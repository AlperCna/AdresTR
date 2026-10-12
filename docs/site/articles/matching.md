# Matching and duplicates

[AddressMatcher](xref:AdresTR.AddressMatcher) decides whether two addresses refer to the same place.

```csharp
var matcher = new AddressMatcher(TurkishGazetteer.Parser);
AddressMatch m = matcher.Compare(
    "Caferağa Mah. Moda Cad. No:12 D:3 Kadıköy/İstanbul",
    "kadikoy caferaga mh moda cd no 12 daire 3 istanbul");
m.Level;    // SameFlat
m.Reasons;  // "same mahalle: Caferağa (Kadıköy/İstanbul)", "same street: Moda", "same door: 12", "same flat: 3"
```

[MatchLevel](xref:AdresTR.MatchLevel): `Different` < `Unknown` < `SameDistrict` < `SameUnit` < `SameStreet` < `SameBuilding` <
`SameFlat`. A semt and its official mahalle are the same unit. Ambiguous addresses are never matched blindly (`Unknown`).

## Duplicates

```csharp
IReadOnlyList<IReadOnlyList<int>> groups = matcher.Group(addresses, MatchLevel.SameBuilding);
```

Addresses are blocked by their resolved mahalle, compared pairwise inside each block and merged with union-find. Every input
index appears in exactly one group.
