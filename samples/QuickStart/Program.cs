using AdresTR;
using AdresTR.Data;

string[] addresses =
[
    "kadikoy caferaga mh moda cd no:12 d3 istanbul",
    "ALSANCAK MAH. 1453 SK. NO:5/2 KONAK/İZMİR",
    "Moda, Kadıköy",
    "Cumhuriyet Mah. Atatürk Cad. No:5",
    "Balaban Köyü Arnavutköy İstanbul",
];

foreach (string address in addresses)
{
    ParseResult r = TurkishGazetteer.Parser.Parse(address);

    Console.WriteLine(address);
    Console.WriteLine($"  → {r.ToCanonicalString()}");
    Console.WriteLine($"    unit: {r.Unit?.ToString() ?? "(not determined)"}   confidence: {r.Confidence:P0}");

    if (r.Unit is null && r.UnitCandidates.Count > 1)
    {
        Console.WriteLine($"    {r.UnitCandidates.Count} candidates, e.g. {string.Join("; ", r.UnitCandidates.Take(3).Select(c => c.Unit.District))}");
    }

    foreach (Correction c in r.Corrections)
    {
        Console.WriteLine($"    {c.Kind}: {c.Field} {c.From} → {c.To}");
    }

    Console.WriteLine();
}
