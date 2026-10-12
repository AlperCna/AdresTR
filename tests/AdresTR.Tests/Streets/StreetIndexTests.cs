using System.IO.Compression;
using System.Text;
using AdresTR.Data;

namespace AdresTR.Tests.Streets;

public class StreetIndexTests
{
    // Kadıköy = 3423, Konak = 3521 (ilçe ids of the bundled gazetteer).
    private const string Csv = "ilce_id,tur,ad,kanit\n3423,cadde,Moda,12\n3423,cadde,\"Bağdat, Cadde\",3\n3423,sokak,Rasimpaşa,2\n3521,cadde,Kıbrıs Şehitleri,9\n3521,sokak,1453,1\n";

    private static StreetIndex Index()
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            gz.Write(Encoding.UTF8.GetBytes(Csv));
        }

        ms.Position = 0;
        return StreetIndex.Load(ms);
    }

    private static readonly AddressParser Parser = new(TurkishGazetteer.Default, Index());

    [Fact]
    public void Loads_names_per_district()
    {
        StreetIndex index = Index();
        Assert.Equal(5, index.Count);
        Assert.True(index.Contains(3423, "MODA"));
        Assert.True(index.Contains(3423, "bagdat, cadde"));
        Assert.False(index.Contains(3521, "Moda"));
    }

    [Fact]
    public void Find_corrects_unique_typos_only()
    {
        StreetIndex index = Index();
        Assert.Equal("Rasimpaşa", index.Find(3423, "Rasimpasa", out bool corrected));
        Assert.False(corrected);
        Assert.Equal("Rasimpaşa", index.Find(3423, "Rasimpasha", out corrected));
        Assert.True(corrected);
        Assert.Null(index.Find(3423, "Hiçbiryer", out _));
    }

    [Fact]
    public void Rejects_a_file_with_the_wrong_header()
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            gz.Write("a,b\n"u8);
        }

        ms.Position = 0;
        Assert.Throws<InvalidDataException>(() => StreetIndex.Load(ms));
    }

    [Fact]
    public void Parser_verifies_an_existing_street()
    {
        ParseResult r = Parser.Parse("Caferağa Mah. Moda Cad. No:12 Kadıköy İstanbul");
        Assert.True(r.StreetVerified);
        Assert.DoesNotContain(r.Corrections, c => c.Field == "street");
    }

    [Fact]
    public void Parser_corrects_a_street_typo()
    {
        ParseResult r = Parser.Parse("Caferağa Mah. Modaa Cad. No:12 Kadıköy İstanbul");
        Assert.True(r.StreetVerified);
        Assert.Equal("Moda", r.Street!.Value);
        Assert.Contains(r.Corrections, c => c.Kind == CorrectionKind.Typo && c.Field == "street" && c.From == "Modaa");
    }

    [Fact]
    public void Parser_flags_an_unknown_street_and_stays_silent_without_an_index()
    {
        Assert.False(Parser.Parse("Caferağa Mah. Olmayan Cad. No:12 Kadıköy İstanbul").StreetVerified);
        Assert.Null(TurkishGazetteer.Parser.Parse("Caferağa Mah. Moda Cad. No:12 Kadıköy İstanbul").StreetVerified);
        Assert.Null(Parser.Parse("Cumhuriyet Mah. Atatürk Cad. No:5").StreetVerified); // ilçe not determined
    }

    [Fact]
    public void Numbered_streets_are_verified()
    {
        Assert.True(Parser.Parse("Alsancak Mah. 1453 Sok. No:5 Konak İzmir").StreetVerified);
    }
}
