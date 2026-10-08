using AdresTR;
using AdresTR.Data;
using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class GazetteerBenchmarks
{
    private readonly Gazetteer _gazetteer = TurkishGazetteer.Load();

    [Benchmark]
    public Gazetteer LoadEmbedded() => TurkishGazetteer.Load();

    [Benchmark]
    public int FindUnitsAmbiguous() => _gazetteer.FindUnits("cumhuriyet").Count;

    [Benchmark]
    public int FindUnitsScoped() => _gazetteer.FindUnits("CAFERAĞA", districtId: 3423).Count;

    [Benchmark]
    public int FindDistrict() => _gazetteer.FindDistricts("kadikoy", plaka: 34).Count;
}
