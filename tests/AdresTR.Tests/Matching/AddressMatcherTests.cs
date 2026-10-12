using AdresTR.Data;

namespace AdresTR.Tests.Matching;

public class AddressMatcherTests
{
    private static readonly AddressMatcher Matcher = new(TurkishGazetteer.Parser);

    [Fact]
    public void Same_flat_written_differently_matches_fully()
    {
        AddressMatch m = Matcher.Compare(
            "Caferağa Mah. Moda Cad. No:12 D:3 Kadıköy/İstanbul",
            "kadikoy caferaga mh moda cd no 12 daire 3 istanbul");

        Assert.Equal(MatchLevel.SameFlat, m.Level);
        Assert.True(m.Score > 0.8);
    }

    [Fact]
    public void Different_flats_in_the_same_building()
    {
        AddressMatch m = Matcher.Compare("Caferağa Mah. Moda Cad. No:12 D:3 Kadıköy", "Caferağa Mah. Moda Cad. No:12 D:5 Kadıköy");
        Assert.Equal(MatchLevel.SameBuilding, m.Level);
    }

    [Fact]
    public void Different_doors_on_the_same_street()
    {
        AddressMatch m = Matcher.Compare("Caferağa Mah. Moda Cad. No:12 Kadıköy", "Caferağa Mah. Moda Cad. No:14 Kadıköy");
        Assert.Equal(MatchLevel.SameStreet, m.Level);
        Assert.Contains(m.Reasons, r => r.Contains("door differs", StringComparison.Ordinal));
    }

    [Fact]
    public void Semt_and_official_name_are_the_same_unit()
    {
        AddressMatch m = Matcher.Compare("Moda Kadıköy İstanbul", "Caferağa Mahallesi Kadıköy");
        Assert.Equal(MatchLevel.SameUnit, m.Level);
    }

    [Fact]
    public void Different_districts_do_not_match()
    {
        AddressMatch m = Matcher.Compare("Caferağa Mah. Kadıköy İstanbul", "Alsancak Mah. Konak İzmir");
        Assert.Equal(MatchLevel.Different, m.Level);
        Assert.Equal(0, m.Score);
    }

    [Fact]
    public void Ambiguous_addresses_are_not_matched_blindly()
    {
        AddressMatch m = Matcher.Compare("Cumhuriyet Mah. No:5", "Cumhuriyet Mah. No:5");
        Assert.Equal(MatchLevel.Unknown, m.Level);
    }

    [Fact]
    public void Groups_duplicate_addresses()
    {
        string[] addresses =
        [
            "Caferağa Mah. Moda Cad. No:12 D:3 Kadıköy/İstanbul",
            "Alsancak Mah. 1453 Sok. No:5 Konak İzmir",
            "kadikoy caferaga mh moda cd no 12 d 3 istanbul",
            "ALSANCAK MAH. 1453 SK. NO:5 KONAK/IZMIR",
            "Caferağa Mah. Moda Cad. No:14 Kadıköy",
        ];

        IReadOnlyList<IReadOnlyList<int>> groups = Matcher.Group(addresses);

        Assert.Equal(3, groups.Count);
        Assert.Contains(groups, g => g.SequenceEqual([0, 2]));
        Assert.Contains(groups, g => g.SequenceEqual([1, 3]));
        Assert.Contains(groups, g => g.SequenceEqual([4]));
    }
}
