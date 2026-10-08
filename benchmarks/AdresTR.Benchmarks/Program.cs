using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using AdresTR.Text;

BenchmarkSwitcher.FromAssembly(typeof(TurkishTextBenchmarks).Assembly).Run(args);

[MemoryDiagnoser]
public class TurkishTextBenchmarks
{
    private const string Address = "  KADIKÖY CAFERAĞA MAH. MODA CD. NO:12 D:3  İSTANBUL ";
    private readonly char[] _buffer = new char[Address.Length];

    [Benchmark(Baseline = true)]
    public string FoldString() => TurkishText.Fold(Address);

    [Benchmark]
    public int FoldSpan() => TurkishText.Fold(Address, _buffer);

    [Benchmark]
    public string Normalize() => TurkishText.Normalize(Address);

    [Benchmark]
    public string ToTitleTr() => TurkishText.ToTitleTr(Address);
}
