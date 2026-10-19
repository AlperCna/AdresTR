using AdresTR.Data;
using CsCheck;

namespace AdresTR.Tests.Parsing;

public class AddressParserTests
{
    private static readonly AddressParser Parser = TurkishGazetteer.Parser;

    private const int Caferaga = 34230005;
    private const int Alsancak = 35210010;

    [Fact]
    public void Parses_an_official_address_completely()
    {
        ParseResult r = Parser.Parse("Caferağa Mah. Moda Cad. No:12 D:3 34710 Kadıköy/İstanbul");

        Assert.Equal(Caferaga, r.Unit?.Id);
        Assert.Equal(3423, r.District?.Id);
        Assert.Equal(34, r.Province?.Plaka);
        Assert.Equal("Caferağa", r.Mahalle?.Value);
        Assert.Equal("Moda", r.Street?.Value);
        Assert.Equal(StreetType.Cadde, r.StreetType);
        Assert.Equal("12", r.DoorNumber?.Value);
        Assert.Equal("3", r.Flat?.Value);
        Assert.Equal("34710", r.PostalCode?.Value);
        Assert.True(r.Confidence > 0.9, $"confidence {r.Confidence}");
        Assert.Equal("Caferağa Mah. Moda Cad. No:12 D:3 34710 Kadıköy/İstanbul", r.ToCanonicalString());
    }

    [Fact]
    public void Component_offsets_point_into_the_input()
    {
        const string input = "Caferağa Mah. Moda Cad. No:12 Kadıköy/İstanbul";
        ParseResult r = Parser.Parse(input);

        Assert.Equal("Caferağa", input[r.Mahalle!.Start..r.Mahalle.End]);
        Assert.Equal("Moda", input[r.Street!.Start..r.Street.End]);
        Assert.Equal("Kadıköy", input[r.Ilce!.Start..r.Ilce.End]);
        Assert.Equal("İstanbul", r.Il!.Text);
    }

    [Theory]
    [InlineData("kadikoy caferaga mh moda cd no 12 d 3 istanbul")]
    [InlineData("KADIKÖY CAFERAĞA MAHALLESİ MODA CADDESİ NO:12 DAİRE:3 İSTANBUL")]
    [InlineData("İstanbul Kadıköy Caferağa Mah. Moda Cad. No:12 D:3")]
    [InlineData("Moda Cad. No:12/3 Caferağa Mah. Kadıköy İstanbul")]
    [InlineData("CaferağaMah. ModaCad. No:12 D:3 Kadıköy/İstanbul")]
    [InlineData("Caferağa Mah., Moda Cad., No:12, D:3, Kadıköy, İstanbul")]
    public void Spelling_case_order_and_glued_variants_resolve_to_the_same_unit(string input)
    {
        ParseResult r = Parser.Parse(input);
        Assert.Equal(Caferaga, r.Unit?.Id);
        Assert.Equal("12", r.DoorNumber?.Value);
        Assert.Equal("3", r.Flat?.Value);
    }

    [Theory]
    [InlineData("no: 69 akpınarköyü 05302 merzifon amasya", 5050021)]
    [InlineData("Akpınarköyü Merzifon Amasya", 5050021)]
    public void Glued_koyu_suffix_resolves_the_village_even_when_the_glued_form_is_an_alias(string input, int unitId)
    {
        ParseResult r = Parser.Parse(input);
        Assert.Equal(unitId, r.Unit?.Id);
    }

    [Fact]
    public void Glued_cadde_suffix_is_split_into_street_name_and_type()
    {
        ParseResult r = Parser.Parse("adana feke akkaya mh zafercadde no. 78");
        Assert.Equal("zafer", r.Street?.Value, ignoreCase: true);
        Assert.Equal(StreetType.Cadde, r.StreetType);
    }

    [Fact]
    public void Door_range_keeps_its_trailing_letter()
    {
        ParseResult r = Parser.Parse("MEVLANA MAH. 1751 SOK. NO:17-19/B");
        Assert.Equal("17-19/B", r.DoorNumber?.Value);
    }

    [Fact]
    public void Unlabeled_number_before_a_street_type_is_a_street_name_not_a_door()
    {
        ParseResult r = Parser.Parse("100.Yıl mh Barboros cd 2190.sk Bağcılar/İstanbul");
        Assert.Null(r.DoorNumber);
    }

    [Fact]
    public void Canonical_string_keeps_apartment_versus_site()
    {
        Assert.Contains("Çiçek Apt.", Parser.Parse("Caferağa Mah. Moda Cad. Çiçek Apt. No:12 Kadıköy İstanbul").ToCanonicalString());
        Assert.Contains("Yıldız Sitesi", Parser.Parse("Caferağa Mah. Moda Cad. Yıldız Sitesi No:12 Kadıköy İstanbul").ToCanonicalString());
    }

    [Fact]
    public void Universitesi_is_not_split_into_a_site()
    {
        ParseResult r = Parser.Parse("bursa nilüfer görükle mah. kampüs cad. no 5 uludağ üniversitesi karşısı");
        Assert.Null(r.Site);
    }

    [Fact]
    public void Bare_n_colon_is_a_door_number_label()
    {
        ParseResult r = Parser.Parse("Ordu Altınordu Düz Mah. Belediye Cd. N:5");
        Assert.Equal("5", r.DoorNumber?.Value);
    }

    [Fact]
    public void Restores_diacritics_and_reports_it()
    {
        ParseResult r = Parser.Parse("caferaga mah kadikoy istanbul");
        Assert.Contains(r.Corrections, c => c.Kind == CorrectionKind.Diacritics && c.Field == "ilce" && c.To == "Kadıköy");
    }

    [Fact]
    public void Corrects_typos_in_district_names()
    {
        ParseResult r = Parser.Parse("Caferağa Mah. Kadıkkoy İstanbul");
        Assert.Equal(Caferaga, r.Unit?.Id);
        Assert.Contains(r.Corrections, c => c.Kind == CorrectionKind.Typo && c.Field == "ilce");
    }

    [Fact]
    public void Corrects_typos_in_neighbourhood_names_within_the_district()
    {
        ParseResult r = Parser.Parse("Cafferağa Mah. Moda Cad. Kadıköy İstanbul");
        Assert.Equal(Caferaga, r.Unit?.Id);
        Assert.Contains(r.Corrections, c => c.Kind == CorrectionKind.Typo && c.Field == "mahalle");
    }

    [Fact]
    public void Resolves_a_semt_to_its_official_neighbourhood()
    {
        ParseResult r = Parser.Parse("Moda Kadıköy İstanbul");
        Assert.Equal("Moda", r.Semt?.Value);
        Assert.Null(r.Mahalle);
        Assert.Equal(Caferaga, r.Unit?.Id);
        Assert.Contains(r.Corrections, c => c.Kind == CorrectionKind.Semt);
    }

    [Fact]
    public void Resolves_a_former_village_name_to_the_current_neighbourhood()
    {
        ParseResult r = Parser.Parse("Balaban Köyü Arnavutköy İstanbul");
        Assert.Equal(34020006, r.Unit?.Id);
        Assert.Contains(r.Corrections, c => c.Kind == CorrectionKind.HistoricName);
    }

    [Fact]
    public void Infers_missing_levels_from_a_unique_neighbourhood()
    {
        ParseResult r = Parser.Parse("Caferağa Mah. Moda Cad. No:5 Kadıköy");
        Assert.Equal(34, r.Province?.Plaka);
        Assert.Null(r.Il);
        Assert.Contains(r.Corrections, c => c.Kind == CorrectionKind.Inferred && c.Field == "il");
    }

    [Fact]
    public void Does_not_guess_an_ambiguous_neighbourhood()
    {
        ParseResult r = Parser.Parse("Cumhuriyet Mah. Atatürk Cad. No:5");
        Assert.Null(r.Unit);
        Assert.Equal("Cumhuriyet", r.Mahalle?.Value);
        Assert.True(r.UnitCandidates.Count > 1);
    }

    [Fact]
    public void Postal_code_disambiguates()
    {
        ParseResult r = Parser.Parse("Alsancak Mah. 1453 Sok. No:5 35220");
        Assert.Equal(Alsancak, r.Unit?.Id);
        Assert.Equal("1453", r.Street?.Value);
        Assert.Equal(StreetType.Sokak, r.StreetType);
    }

    [Fact]
    public void Reports_a_postal_code_that_contradicts_the_province()
    {
        ParseResult r = Parser.Parse("Caferağa Mah. Moda Cad. No:5 06100 Kadıköy İstanbul");
        Assert.Equal(Caferaga, r.Unit?.Id);
        Assert.Contains(r.Corrections, c => c.Kind == CorrectionKind.PostalCodeConflict);
    }

    [Fact]
    public void Resolves_province_short_forms()
    {
        ParseResult r = Parser.Parse("Haliliye Urfa");
        Assert.Equal(63, r.Province?.Plaka);
        Assert.Equal("Şanlıurfa", r.Il?.Value);
        Assert.Contains(r.Corrections, c => c.Kind == CorrectionKind.Alias);
    }

    [Theory]
    [InlineData("Alsancak Mah. 1203/5 Sk. No:3 Konak İzmir", "1203/5", StreetType.Sokak)]
    [InlineData("Alsancak Mah. 127 nolu sokak No:3 Konak İzmir", "127", StreetType.Sokak)]
    [InlineData("Alsancak Mah. 1453sok No:3 Konak İzmir", "1453", StreetType.Sokak)]
    [InlineData("Alsancak Mah. Dr. Fahri Atabey Cad. No:3 Konak İzmir", "Dr. Fahri Atabey", StreetType.Cadde)]
    [InlineData("Alsancak Mah. Tugay Yolu Cad. No:3 Konak İzmir", "Tugay Yolu", StreetType.Cadde)]
    public void Parses_street_names(string input, string street, StreetType type)
    {
        ParseResult r = Parser.Parse(input);
        Assert.Equal(street, r.Street?.Value);
        Assert.Equal(type, r.StreetType);
        Assert.Equal("3", r.DoorNumber?.Value);
        Assert.Equal(Alsancak, r.Unit?.Id);
    }

    [Theory]
    [InlineData("No:17/5", "17", "5")]
    [InlineData("No:17/A", "17/A", null)]
    [InlineData("No:58 F", "58F", null)]
    [InlineData("No:3 /2C", "3", "2C")]
    [InlineData("No:70/c/2", "70/C", "2")]
    [InlineData("No:1 -3", "1-3", null)]
    [InlineData("No:12 D:4", "12", "4")]
    [InlineData("No 12 daire 4", "12", "4")]
    public void Parses_door_and_flat_numbers(string doorText, string door, string? flat)
    {
        ParseResult r = Parser.Parse($"Alsancak Mah. Kıbrıs Şehitleri Cad. {doorText} Konak İzmir");
        Assert.Equal(door, r.DoorNumber?.Value);
        Assert.Equal(flat, r.Flat?.Value);
    }

    [Theory]
    [InlineData("K:3", "3")]
    [InlineData("Kat 3", "3")]
    [InlineData("3. kat", "3")]
    [InlineData("zemin kat", "0")]
    [InlineData("bodrum kat", "-1")]
    public void Parses_floors(string floorText, string floor)
    {
        ParseResult r = Parser.Parse($"Alsancak Mah. Kıbrıs Şehitleri Cad. No:5 {floorText} Konak İzmir");
        Assert.Equal(floor, r.Floor?.Value);
    }

    [Fact]
    public void Parses_site_and_block()
    {
        ParseResult r = Parser.Parse("Alsancak Mah. Güneş Sitesi B Blok K:2 D:7 Konak İzmir");
        Assert.Equal("Güneş", r.Site?.Value);
        Assert.Equal("B", r.Blok?.Value);
        Assert.Equal("2", r.Floor?.Value);
        Assert.Equal("7", r.Flat?.Value);
    }

    [Fact]
    public void Phone_numbers_are_not_doors_or_postal_codes()
    {
        ParseResult r = Parser.Parse("Alsancak Mah. Kıbrıs Şehitleri Cad. No:5 Konak İzmir Tel: 0232 555 12 34");
        Assert.Equal("5", r.DoorNumber?.Value);
        Assert.Null(r.PostalCode);
        Assert.Equal(Alsancak, r.Unit?.Id);
    }

    [Fact]
    public void Parses_landmarks()
    {
        ParseResult r = Parser.Parse("Alsancak Mah. Kıbrıs Şehitleri Cad. No:5 PTT karşısı Konak İzmir");
        Assert.Equal("PTT karşısı", r.Landmark?.Value);
    }

    [Fact]
    public void Ignores_repeated_names()
    {
        ParseResult r = Parser.Parse("Alsancak Mah. Konak Konak İzmir");
        Assert.Equal(3521, r.District?.Id);
        Assert.Contains(r.Corrections, c => c.Kind == CorrectionKind.Duplicate);
    }

    [Fact]
    public void Trace_explains_tokens_and_alternatives()
    {
        ParseResult r = Parser.Parse("147sok no:5 Alsancak Konak", new ParseOptions { IncludeTrace = true });

        Assert.NotNull(r.Trace);
        Assert.Contains(r.Trace!.Tokens, t => t.Glued && t.Folded == "sok");
        Assert.True(r.Trace.HypothesisCount > 0);
        TraceAlternative best = r.Trace.Alternatives[0];
        Assert.Equal(1, best.Rank);
        Assert.Equal("Alsancak", best.Unit);
        Assert.Contains(best.Components, c => c.Role == "street" && c.Value == "147");
        Assert.InRange(r.Trace.Alternatives.Sum(a => a.Probability), 0, 1.0001);
        Assert.Null(Parser.Parse("Alsancak Konak").Trace);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,,;;;")]
    [InlineData("Tel: 0532 555 12 34")]
    public void Handles_inputs_without_an_address(string input)
    {
        ParseResult r = Parser.Parse(input);
        Assert.Null(r.Unit);
        Assert.Equal(input, r.Input);
    }

    [Fact]
    public void Truncates_very_long_input() =>
        Assert.NotNull(Parser.Parse(string.Concat(Enumerable.Repeat("Caferağa Mah. ", 500))));

    [Fact]
    public void Never_throws_on_arbitrary_input() =>
        Gen.String[Gen.Char, 0, 120].Sample(s =>
        {
            ParseResult r = Parser.Parse(s);
            Assert.InRange(r.Confidence, 0, 1);
        }, iter: 300);

    [Fact]
    public void Never_throws_on_address_like_input() =>
        Gen.Select(
                Gen.OneOfConst("Caferağa", "kadıköy", "İSTANBUL", "Mah.", "mh", "Cad.", "sk", "No:", "12", "/", "5", "D:", "K:", "A", "Blok", ",", "34710", "Tel:", "0532", "karşısı", "Köyü", "OSB"),
                Gen.Int[0, 2])
            .Array[0, 14]
            .Sample(parts =>
            {
                string input = string.Join(" ", parts.Select(p => p.Item1));
                _ = Parser.Parse(input).ToCanonicalString();
            }, iter: 300);
}
