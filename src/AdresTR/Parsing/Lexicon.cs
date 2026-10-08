using System.Collections.Frozen;

namespace AdresTR.Parsing;

/// <summary>Type words of Turkish addresses.</summary>
internal enum Keyword : byte
{
    None,
    Mahalle,
    Koy,
    Belde,
    Mevkii,
    KumeEvler,
    Cadde,
    Sokak,
    Bulvar,
    Yol,
    Meydan,
    Cikmaz,
    Site,
    Bina,
    Blok,
    No,
    Nolu,
    Kat,
    Daire,
    Ilce,
    Il,
    Osb,
    Landmark,
    Phone,
    Ignore,
}

/// <summary>
/// Folded keyword variants (sources: Adres ve Numaralamaya İlişkin Yönetmelik, libpostal tr dictionaries, real-world
/// misspellings from data/curated/abbreviations.draft.csv). Matching is on folded single tokens, plus a few
/// two- and three-token phrases.
/// </summary>
internal static class Lexicon
{
    private static readonly FrozenDictionary<string, Keyword> Single = new Dictionary<string, Keyword>
    {
        // mahalle
        ["mahallesi"] = Keyword.Mahalle, ["mahalle"] = Keyword.Mahalle, ["mah"] = Keyword.Mahalle, ["mh"] = Keyword.Mahalle,
        ["mahalesi"] = Keyword.Mahalle, ["mahhallesi"] = Keyword.Mahalle, ["mahallasi"] = Keyword.Mahalle, ["mahlesi"] = Keyword.Mahalle,
        ["mhallesi"] = Keyword.Mahalle, ["maallesi"] = Keyword.Mahalle, ["mhl"] = Keyword.Mahalle, ["mahl"] = Keyword.Mahalle,
        ["mahalleis"] = Keyword.Mahalle, ["mahallsi"] = Keyword.Mahalle,

        // köy, belde, mevkii
        ["koyu"] = Keyword.Koy, ["koy"] = Keyword.Koy, ["ky"] = Keyword.Koy,
        ["beldesi"] = Keyword.Belde, ["belde"] = Keyword.Belde,
        ["mevkii"] = Keyword.Mevkii, ["mevki"] = Keyword.Mevkii, ["mevkisi"] = Keyword.Mevkii, ["mvk"] = Keyword.Mevkii,

        // streets
        ["caddesi"] = Keyword.Cadde, ["cadde"] = Keyword.Cadde, ["cad"] = Keyword.Cadde, ["cd"] = Keyword.Cadde,
        ["cadesi"] = Keyword.Cadde, ["cddesi"] = Keyword.Cadde, ["caddsi"] = Keyword.Cadde, ["cadessi"] = Keyword.Cadde, ["cadd"] = Keyword.Cadde,
        ["sokagi"] = Keyword.Sokak, ["sokak"] = Keyword.Sokak, ["sok"] = Keyword.Sokak, ["sk"] = Keyword.Sokak,
        ["skk"] = Keyword.Sokak, ["sokk"] = Keyword.Sokak, ["sokag"] = Keyword.Sokak, ["sokar"] = Keyword.Sokak, ["sokakk"] = Keyword.Sokak,
        ["bulvari"] = Keyword.Bulvar, ["bulvar"] = Keyword.Bulvar, ["bulv"] = Keyword.Bulvar, ["blv"] = Keyword.Bulvar, ["bulvr"] = Keyword.Bulvar,
        ["yolu"] = Keyword.Yol, ["yol"] = Keyword.Yol,
        ["meydani"] = Keyword.Meydan, ["meydan"] = Keyword.Meydan, ["myd"] = Keyword.Meydan,
        ["cikmazi"] = Keyword.Cikmaz, ["cikmaz"] = Keyword.Cikmaz, ["cikm"] = Keyword.Cikmaz, ["ckm"] = Keyword.Cikmaz,

        // buildings
        ["sitesi"] = Keyword.Site, ["site"] = Keyword.Site, ["sit"] = Keyword.Site, ["sitel"] = Keyword.Site,
        ["apartmani"] = Keyword.Bina, ["apartman"] = Keyword.Bina, ["apt"] = Keyword.Bina, ["ap"] = Keyword.Bina,
        ["konutlari"] = Keyword.Bina, ["rezidans"] = Keyword.Bina, ["residence"] = Keyword.Bina, ["plaza"] = Keyword.Bina,
        ["ishani"] = Keyword.Bina, ["pasaji"] = Keyword.Bina, ["ismerkezi"] = Keyword.Bina,
        ["blok"] = Keyword.Blok, ["blk"] = Keyword.Blok, ["bl"] = Keyword.Blok, ["blogu"] = Keyword.Blok,

        // numbers
        ["no"] = Keyword.No, ["numara"] = Keyword.No, ["nu"] = Keyword.No, ["num"] = Keyword.No, ["numarasi"] = Keyword.No, ["nomara"] = Keyword.No,
        ["nolu"] = Keyword.Nolu, ["numarali"] = Keyword.Nolu, ["nlu"] = Keyword.Nolu,
        ["kat"] = Keyword.Kat, ["k"] = Keyword.Kat, ["kati"] = Keyword.Kat,
        ["daire"] = Keyword.Daire, ["d"] = Keyword.Daire, ["dr"] = Keyword.Daire, ["dai"] = Keyword.Daire, ["dair"] = Keyword.Daire, ["dairesi"] = Keyword.Daire,

        // admin cues
        ["ilcesi"] = Keyword.Ilce, ["ilce"] = Keyword.Ilce,
        ["ili"] = Keyword.Il,
        ["osb"] = Keyword.Osb,

        // landmarks (postpositions)
        // ("altı", "arası", "içi", "önü" are left out: they are also parts of place and street names.)
        ["karsisi"] = Keyword.Landmark, ["yani"] = Keyword.Landmark, ["arkasi"] = Keyword.Landmark, ["bitisigi"] = Keyword.Landmark,
        ["ustu"] = Keyword.Landmark, ["civari"] = Keyword.Landmark, ["yakini"] = Keyword.Landmark, ["kosesi"] = Keyword.Landmark,
        ["girisi"] = Keyword.Landmark,

        // phone labels
        ["tel"] = Keyword.Phone, ["telefon"] = Keyword.Phone, ["gsm"] = Keyword.Phone, ["cep"] = Keyword.Phone,
        ["fax"] = Keyword.Phone, ["faks"] = Keyword.Phone,

        // words carrying no address information
        ["turkiye"] = Keyword.Ignore, ["turkey"] = Keyword.Ignore, ["tr"] = Keyword.Ignore, ["tc"] = Keyword.Ignore,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly (string[] Tokens, Keyword Keyword)[] Phrases =
    [
        (["kume", "evleri"], Keyword.KumeEvler),
        (["kume", "evler"], Keyword.KumeEvler),
        (["organize", "sanayi", "bolgesi"], Keyword.Osb),
        (["organize", "sanayi"], Keyword.Osb),
        (["o", "s", "b"], Keyword.Osb),
        (["is", "merkezi"], Keyword.Bina),
        (["is", "hani"], Keyword.Bina),
        (["ic", "kapi"], Keyword.Daire),
    ];

    /// <summary>Single-letter keywords are only keywords next to a number ("K:3", "D 5").</summary>
    public static bool NeedsNumber(string fold) => fold.Length == 1;

    /// <summary>Returns the keyword and its length in tokens at <paramref name="i"/>, or <see cref="Keyword.None"/>.</summary>
    public static (Keyword Keyword, int Length) At(IReadOnlyList<Token> tokens, int i)
    {
        foreach ((string[] phrase, Keyword keyword) in Phrases)
        {
            if (i + phrase.Length <= tokens.Count && phrase.Select((w, k) => tokens[i + k].Fold == w).All(x => x))
            {
                return (keyword, phrase.Length);
            }
        }

        return Single.TryGetValue(tokens[i].Fold, out Keyword k1) ? (k1, 1) : (Keyword.None, 0);
    }

    /// <summary>Returns the keyword of a single folded word.</summary>
    public static Keyword Of(string fold) => Single.GetValueOrDefault(fold);

    public static StreetType? StreetTypeOf(Keyword keyword) => keyword switch
    {
        Keyword.Cadde => StreetType.Cadde,
        Keyword.Sokak => StreetType.Sokak,
        Keyword.Bulvar => StreetType.Bulvar,
        Keyword.Yol => StreetType.Yol,
        Keyword.Meydan => StreetType.Meydan,
        Keyword.Cikmaz => StreetType.Cikmaz,
        Keyword.KumeEvler => StreetType.KumeEvler,
        _ => null,
    };
}
