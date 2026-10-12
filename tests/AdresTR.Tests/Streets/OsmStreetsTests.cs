using AdresTR.Streets;

namespace AdresTR.Tests.Streets;

/// <summary>Smoke tests on the real OpenStreetMap street data (coverage is partial, so only famous streets are asserted).</summary>
public class OsmStreetsTests
{
    [Fact]
    public void Bundled_index_covers_known_streets()
    {
        StreetIndex streets = OsmStreets.Default;
        Assert.True(streets.Count > 100_000, $"only {streets.Count} streets");
        Assert.True(streets.Contains(3423, "Bağdat"), "Kadıköy: Bağdat Caddesi");
        Assert.True(streets.Contains(3423, "Moda"), "Kadıköy: Moda Caddesi");
    }

    [Fact]
    public void Parser_uses_the_bundled_streets()
    {
        ParseResult r = OsmStreets.Parser.Parse("Caferağa Mah. Moda Cad. No:12 Kadıköy İstanbul");
        Assert.True(r.StreetVerified);
        Assert.Equal(34230005, r.Unit?.Id);
    }
}
