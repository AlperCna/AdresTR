using System.Globalization;

namespace AdresTR.DataBuilder;

/// <summary>Reads the staging CSVs described in data/staging/README.md into a validated <see cref="Gazetteer"/>.</summary>
internal static class StagingReader
{
    public static Gazetteer Read(string directory)
    {
        string version = File.ReadAllText(Path.Combine(directory, "VERSION")).Trim();
        var builder = new GazetteerBuilder(version);

        string sources = Path.Combine(directory, "SOURCES.json");
        if (File.Exists(sources))
        {
            builder.Sources = File.ReadAllText(sources).ReplaceLineEndings("\n").Trim();
        }

        foreach (var r in Csv.ReadFile(Path.Combine(directory, "il.csv")))
        {
            builder.AddProvince(Int(r["plaka"]), r["ad"], Qid(r["wikidata"]), Point(r["enlem"], r["boylam"]));
        }

        foreach (var r in Csv.ReadFile(Path.Combine(directory, "ilce.csv")))
        {
            builder.AddDistrict(Int(r["ilce_id"]), Int(r["plaka"]), r["ad"], Qid(r["wikidata"]), Point(r["enlem"], r["boylam"]));
        }

        foreach (var r in Csv.ReadFile(Path.Combine(directory, "birim.csv")))
        {
            builder.AddUnit(
                Int(r["birim_id"]), Int(r["ilce_id"]), Kind(r["tur"]), r["ad"], Null(r["ust_ad"]),
                Null(r["posta_kodu"]), Null(r["semt"]), Qid(r["wikidata"]),
                r["nvi_id"].Length == 0 ? 0 : long.Parse(r["nvi_id"], CultureInfo.InvariantCulture),
                Point(r["enlem"], r["boylam"]));
        }

        string aliases = Path.Combine(directory, "alias.csv");
        if (File.Exists(aliases))
        {
            foreach (var r in Csv.ReadFile(aliases))
            {
                builder.AddAlias(new GazetteerAlias(Level(r["hedef"]), Int(r["hedef_id"]), r["alias"], Alias(r["tur"]), r["kaynak"]));
            }
        }

        return builder.Build();
    }

    private static int Int(string s) => int.Parse(s, NumberStyles.None, CultureInfo.InvariantCulture);

    private static string? Null(string s) => s.Length == 0 ? null : s;

    private static int Qid(string s) =>
        s.Length == 0 ? 0 : int.Parse(s.AsSpan(s.StartsWith('Q') ? 1 : 0), NumberStyles.None, CultureInfo.InvariantCulture);

    private static GeoPoint? Point(string lat, string lon) =>
        lat.Length == 0 || lon.Length == 0
            ? null
            : new GeoPoint(double.Parse(lat, CultureInfo.InvariantCulture), double.Parse(lon, CultureInfo.InvariantCulture));

    private static UnitKind Kind(string s) => s switch
    {
        "mahalle" => UnitKind.Mahalle,
        "koy" => UnitKind.Koy,
        "belde" => UnitKind.Belde,
        "mezra" => UnitKind.Mezra,
        "mevki" => UnitKind.Mevki,
        "yayla" => UnitKind.Yayla,
        "kume_evler" => UnitKind.KumeEvler,
        "osb" => UnitKind.Osb,
        "site" => UnitKind.Site,
        "diger" => UnitKind.Diger,
        _ => throw new GazetteerValidationException($"birim.csv: unknown tur '{s}'"),
    };

    private static EntityLevel Level(string s) => s switch
    {
        "il" => EntityLevel.Il,
        "ilce" => EntityLevel.Ilce,
        "birim" => EntityLevel.Birim,
        _ => throw new GazetteerValidationException($"alias.csv: unknown hedef '{s}'"),
    };

    private static AliasKind Alias(string s) => s switch
    {
        "semt" => AliasKind.Semt,
        "tarihsel" => AliasKind.Tarihsel,
        "yazim" => AliasKind.Yazim,
        _ => throw new GazetteerValidationException($"alias.csv: unknown tur '{s}'"),
    };
}
