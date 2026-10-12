# AdresTR

Turkish free-text address parser, normalizer and validator for .NET. Offline, deterministic and explainable.

```csharp
ParseResult r = TurkishGazetteer.Parser.Parse("kadikoy caferaga mh moda cd no:12 d3 istanbul");
r.ToCanonicalString();   // Caferağa Mah. Moda Cad. No:12 D:3 34710 Kadıköy/İstanbul
r.Unit?.Id;              // 34230005
```

- [Getting started](articles/getting-started.md)
- [Try it in the browser](https://alpercna.github.io/AdresTR/) (runs client-side; nothing is sent anywhere)
- [API reference](api/index.md)
- [Benchmark](articles/benchmark.md): 97.4 % exact match on noisy synthetic, 95.8 % on real public-institution addresses
