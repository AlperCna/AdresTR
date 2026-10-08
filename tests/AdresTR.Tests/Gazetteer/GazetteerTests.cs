namespace AdresTR.Tests.Gazetteer;

public class GazetteerTests
{
    internal static AdresTR.Gazetteer Sample() => new GazetteerBuilder("test.1") { Sources = """{"test":true}""" }
        .AddProvince(34, "İstanbul", 406, new GeoPoint(41.01, 28.97))
        .AddProvince(35, "İzmir", 35997)
        .AddProvince(63, "Şanlıurfa")
        .AddDistrict(3417, 34, "Kadıköy", 1000, new GeoPoint(40.99, 29.03))
        .AddDistrict(3433, 34, "Şişli")
        .AddDistrict(3402, 34, "Başakşehir")
        .AddDistrict(3509, 35, "Bornova")
        .AddDistrict(6305, 63, "Eyyübiye")
        .AddUnit(34170001, 3417, UnitKind.Mahalle, "Caferağa", postalCode: "34710", semt: "Moda", wikidataId: 123, nviId: 40123)
        .AddUnit(34170002, 3417, UnitKind.Mahalle, "Fenerbahçe", postalCode: "34726")
        .AddUnit(34330001, 3433, UnitKind.Mahalle, "Gazi Osman Paşa", postalCode: "34380")
        .AddUnit(34020001, 3402, UnitKind.Mahalle, "Cumhuriyet", postalCode: "34480")
        .AddUnit(35090001, 3509, UnitKind.Mahalle, "Cumhuriyet", postalCode: "35040")
        .AddUnit(35090002, 3509, UnitKind.Mahalle, "100. Yıl", postalCode: "35040")
        .AddUnit(63050001, 6305, UnitKind.Koy, "Akören", parentName: "Bozova", postalCode: "63850")
        .AddAlias(new GazetteerAlias(EntityLevel.Il, 63, "Urfa", AliasKind.Yazim, "el"))
        .AddAlias(new GazetteerAlias(EntityLevel.Birim, 34170001, "Moda", AliasKind.Semt, "ptt-semt"))
        .AddAlias(new GazetteerAlias(EntityLevel.Birim, 34170001, "CAFERAĞA", AliasKind.Semt, "ptt-semt"))
        .Build();

    [Fact]
    public void Builds_hierarchy()
    {
        AdresTR.Gazetteer g = Sample();

        Assert.Equal(3, g.Provinces.Count);
        Assert.Equal("İstanbul", g.GetProvince(34)!.Name);
        Assert.Equal(3, g.GetProvince(34)!.Districts.Count);
        Assert.Same(g.GetProvince(34), g.GetDistrict(3417)!.Province);
        Assert.Equal(["Caferağa", "Fenerbahçe"], g.GetDistrict(3417)!.Units.Select(u => u.Name));
        Assert.Same(g.GetDistrict(3417), g.GetUnit(34170001)!.District);
        Assert.Null(g.GetProvince(0));
        Assert.Null(g.GetProvince(82));
        Assert.Null(g.GetDistrict(1));
    }

    [Theory]
    [InlineData("Gazi Osman Paşa", "gaziosmanpasa")]
    [InlineData("GAZİOSMANPAŞA", "gaziosmanpasa")]
    [InlineData("100. Yıl", "100yil")]
    [InlineData("100.YIL", "100yil")]
    [InlineData("Kuva-i Milliye", "kuvaimilliye")]
    [InlineData("Tan'ın Komları", "taninkomlari")]
    public void Key_ignores_case_diacritics_spaces_and_punctuation(string name, string key) =>
        Assert.Equal(key, AdresTR.Gazetteer.Key(name));

    [Theory]
    [InlineData("istanbul")]
    [InlineData("ISTANBUL")]
    [InlineData("İstanbul")]
    public void Finds_province_by_any_casing(string name)
    {
        var match = Assert.Single(Sample().FindProvinces(name));
        Assert.Equal(34, match.Entity.Plaka);
        Assert.Null(match.Alias);
    }

    [Fact]
    public void Finds_province_by_alias_and_reports_it()
    {
        var match = Assert.Single(Sample().FindProvinces("urfa"));
        Assert.Equal(63, match.Entity.Plaka);
        Assert.Equal(AliasKind.Yazim, match.Alias!.Kind);
    }

    [Fact]
    public void Finds_units_with_compact_spelling()
    {
        var match = Assert.Single(Sample().FindUnits("gaziosmanpasa"));
        Assert.Equal("Gazi Osman Paşa", match.Entity.Name);
    }

    [Fact]
    public void Ambiguous_unit_names_return_all_candidates_and_can_be_scoped()
    {
        AdresTR.Gazetteer g = Sample();
        Assert.Equal(2, g.FindUnits("cumhuriyet").Count);
        Assert.Equal(35090001, Assert.Single(g.FindUnits("cumhuriyet", districtId: 3509)).Entity.Id);
        Assert.Empty(g.FindUnits("cumhuriyet", districtId: 3417));
    }

    [Fact]
    public void Semt_alias_finds_official_unit()
    {
        var match = Assert.Single(Sample().FindUnits("moda"));
        Assert.Equal("Caferağa", match.Entity.Name);
        Assert.Equal("ptt-semt", match.Alias!.Source);
    }

    [Fact]
    public void Alias_equal_to_official_name_is_not_duplicated()
    {
        var match = Assert.Single(Sample().FindUnits("caferaga"));
        Assert.Null(match.Alias);
    }

    [Fact]
    public void Finds_districts_scoped_to_province()
    {
        AdresTR.Gazetteer g = Sample();
        Assert.Equal(3417, Assert.Single(g.FindDistricts("kadikoy")).Entity.Id);
        Assert.Empty(g.FindDistricts("kadikoy", plaka: 35));
        Assert.Empty(g.FindDistricts("yok böyle bir yer"));
        Assert.Empty(g.FindDistricts(""));
    }

    [Fact]
    public void Finds_units_by_postal_code()
    {
        AdresTR.Gazetteer g = Sample();
        Assert.Equal(["Cumhuriyet", "100. Yıl"], g.FindUnitsByPostalCode("35040").Select(u => u.Name));
        Assert.Empty(g.FindUnitsByPostalCode("99999"));
    }

    [Fact]
    public void Round_trips_through_binary_format()
    {
        AdresTR.Gazetteer original = Sample();
        using var stream = new MemoryStream();
        original.Save(stream);
        stream.Position = 0;

        AdresTR.Gazetteer loaded = AdresTR.Gazetteer.Load(stream);

        Assert.Equal(original.DataVersion, loaded.DataVersion);
        Assert.Equal(original.Sources, loaded.Sources);
        Assert.Equal(
            original.Units.Select(u => (u.Id, u.District.Id, u.Kind, u.Name, u.ParentName, u.PostalCode, u.Semt, u.WikidataId, u.NviId)),
            loaded.Units.Select(u => (u.Id, u.District.Id, u.Kind, u.Name, u.ParentName, u.PostalCode, u.Semt, u.WikidataId, u.NviId)));
        Assert.Equal(original.Aliases, loaded.Aliases);
        Assert.Equal(41.01, loaded.GetProvince(34)!.Location!.Value.Latitude, 4);
        Assert.Null(loaded.GetProvince(63)!.Location);
        Assert.Equal("Moda", Assert.Single(loaded.FindUnits("moda")).Alias!.Name);
    }

    [Fact]
    public void Load_rejects_foreign_data()
    {
        using var stream = new MemoryStream("NOPE....."u8.ToArray());
        Assert.Throws<InvalidDataException>(() => AdresTR.Gazetteer.Load(stream));
    }

    [Fact]
    public void Load_rejects_truncated_data()
    {
        using var full = new MemoryStream();
        Sample().Save(full);
        using var truncated = new MemoryStream(full.ToArray()[..(int)(full.Length / 2)]);
        Assert.ThrowsAny<Exception>(() => AdresTR.Gazetteer.Load(truncated));
    }

    [Fact]
    public void Validation_collects_all_errors()
    {
        var builder = new GazetteerBuilder("bad")
            .AddProvince(34, "İstanbul")
            .AddProvince(34, "İstanbul again")
            .AddProvince(99, "Nowhere")
            .AddDistrict(3417, 34, "Kadıköy ")
            .AddDistrict(3501, 35, "Bornova")
            .AddUnit(1, 3417, UnitKind.Mahalle, "Caferağa", postalCode: "35710")
            .AddUnit(2, 9999, UnitKind.Mahalle, "Orphan")
            .AddUnit(3, 3417, UnitKind.Mahalle, "", location: new GeoPoint(51.5, -0.1))
            .AddAlias(new GazetteerAlias(EntityLevel.Ilce, 1, "Ghost", AliasKind.Yazim, "el"));

        var ex = Assert.Throws<GazetteerValidationException>(builder.Build);

        Assert.Contains(ex.Errors, e => e.Contains("duplicate plate code", StringComparison.Ordinal));
        Assert.Contains(ex.Errors, e => e.Contains("plate code must be 1-81", StringComparison.Ordinal));
        Assert.Contains(ex.Errors, e => e.Contains("double spaces", StringComparison.Ordinal));
        Assert.Contains(ex.Errors, e => e.Contains("unknown il 35", StringComparison.Ordinal));
        Assert.Contains(ex.Errors, e => e.Contains("postal code '35710'", StringComparison.Ordinal));
        Assert.Contains(ex.Errors, e => e.Contains("unknown ilçe 9999", StringComparison.Ordinal));
        Assert.Contains(ex.Errors, e => e.Contains("empty name", StringComparison.Ordinal));
        Assert.Contains(ex.Errors, e => e.Contains("outside Türkiye", StringComparison.Ordinal));
        Assert.Contains(ex.Errors, e => e.Contains("alias 'Ghost'", StringComparison.Ordinal));
    }
}
