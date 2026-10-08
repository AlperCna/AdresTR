using AdresTR.Data;

namespace AdresTR.Tests.Gazetteer;

/// <summary>Sanity checks on the real bundled data (AdresTR.Data).</summary>
public class BundledGazetteerTests
{
    private static readonly AdresTR.Gazetteer G = TurkishGazetteer.Default;

    [Fact]
    public void Has_all_provinces_and_districts()
    {
        Assert.Equal(81, G.Provinces.Count);
        Assert.Equal(973, G.Districts.Count);
        Assert.All(G.Provinces, p => Assert.NotEmpty(p.Districts));
        Assert.True(G.Units.Count > 70_000, $"only {G.Units.Count} units");
    }

    [Fact]
    public void Default_instance_is_cached()
    {
        Assert.Same(TurkishGazetteer.Default, TurkishGazetteer.Default);
        Assert.NotSame(TurkishGazetteer.Default, TurkishGazetteer.Load());
    }

    [Theory]
    [InlineData(34, "İstanbul")]
    [InlineData(76, "Iğdır")]
    [InlineData(63, "Şanlıurfa")]
    [InlineData(3, "Afyonkarahisar")]
    public void Province_names_use_turkish_spelling(int plaka, string name) =>
        Assert.Equal(name, G.GetProvince(plaka)!.Name);

    [Fact]
    public void Resolves_a_known_neighbourhood_end_to_end()
    {
        var kadikoy = Assert.Single(G.FindDistricts("KADIKOY", plaka: 34)).Entity;
        var caferaga = Assert.Single(G.FindUnits("caferaga", kadikoy.Id)).Entity;

        Assert.Equal("Caferağa", caferaga.Name);
        Assert.Equal(UnitKind.Mahalle, caferaga.Kind);
        Assert.Equal("34710", caferaga.PostalCode);
        Assert.Contains(caferaga, G.FindUnitsByPostalCode("34710"));
    }

    [Theory]
    [InlineData("Urfa", 63)]
    [InlineData("Antep", 27)]
    [InlineData("Afyon", 3)]
    public void Common_province_short_forms_are_aliases(string alias, int plaka) =>
        Assert.Contains(G.FindProvinces(alias), m => m.Entity.Plaka == plaka && m.Alias is not null);

    [Fact]
    public void Renamed_district_is_found_by_its_old_name()
    {
        var match = Assert.Single(G.FindDistricts("Eyüp", plaka: 34));
        Assert.Equal("Eyüpsultan", match.Entity.Name);
        Assert.Equal(AliasKind.Tarihsel, match.Alias!.Kind);
    }

    [Fact]
    public void Curated_semt_resolves_to_official_neighbourhood()
    {
        var match = Assert.Single(G.FindUnits("moda", districtId: 3423));
        Assert.Equal("Caferağa", match.Entity.Name);
        Assert.Equal(AliasKind.Semt, match.Alias!.Kind);
    }

    [Fact]
    public void Ambiguous_neighbourhood_names_are_common()
    {
        // "Cumhuriyet" exists in hundreds of districts: the parser must never guess it without context.
        Assert.True(G.FindUnits("Cumhuriyet").Count > 300);
    }

    [Fact]
    public void Every_postal_code_starts_with_the_plate_code() =>
        Assert.All(G.Units.Where(u => u.PostalCode is not null),
            u => Assert.Equal(u.District.Province.Plaka.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), u.PostalCode![..2]));
}
