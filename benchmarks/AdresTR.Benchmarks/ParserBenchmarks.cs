using AdresTR;
using AdresTR.Data;
using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class ParserBenchmarks
{
    private readonly AddressParser _parser = TurkishGazetteer.Parser;

    [Params(
        "Caferağa Mah. Moda Cad. No:12 D:3 34710 Kadıköy/İstanbul",
        "kadikoy caferaga mh moda cd no 12 d 3 istanbul",
        "ATILLAMAHALLESI475SOKIBRAHIMAPTNO20KAT4DAIRE4 KONAK IZMIR",
        "Cumhuriyet Mah. Atatürk Cad. No:5")]
    public string Address { get; set; } = string.Empty;

    [Benchmark]
    public ParseResult Parse() => _parser.Parse(Address);
}
