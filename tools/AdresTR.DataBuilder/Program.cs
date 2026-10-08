// AdresTR.DataBuilder — turns the committed staging CSVs (data/staging, produced by
// data/scripts/build_staging.py from MIT/CC0 sources) into the binary gazetteer embedded in AdresTR.Data.
//
//   dotnet run --project tools/AdresTR.DataBuilder -- build   writes src/AdresTR.Data/Resources/gazetteer.bin
//   dotnet run --project tools/AdresTR.DataBuilder -- check   fails if the committed .bin is out of date
//   dotnet run --project tools/AdresTR.DataBuilder -- stats   prints counts and coverage

using System.Globalization;
using AdresTR;
using AdresTR.DataBuilder;

string command = args.FirstOrDefault() ?? "build";
string root = FindRepoRoot();
string staging = Path.Combine(root, "data", "staging");
string output = Path.Combine(root, "src", "AdresTR.Data", "Resources", "gazetteer.bin");

try
{
    Gazetteer gazetteer = StagingReader.Read(staging);

    switch (command)
    {
        case "build":
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using (FileStream file = File.Create(output))
            {
                gazetteer.Save(file);
            }

            Console.WriteLine($"Wrote {Path.GetRelativePath(root, output)} ({new FileInfo(output).Length / 1024.0:F0} KiB).");
            PrintStats(gazetteer);
            return 0;

        case "check":
            if (!File.Exists(output))
            {
                Console.Error.WriteLine($"{output} does not exist. Run the 'build' command.");
                return 1;
            }

            Gazetteer committed;
            using (FileStream file = File.OpenRead(output))
            {
                committed = Gazetteer.Load(file);
            }

            if (!Payload(committed).AsSpan().SequenceEqual(Payload(gazetteer)))
            {
                Console.Error.WriteLine("gazetteer.bin is out of date with data/staging. Run: dotnet run --project tools/AdresTR.DataBuilder -- build");
                return 1;
            }

            Console.WriteLine("gazetteer.bin is up to date.");
            return 0;

        case "stats":
            PrintStats(gazetteer);
            return 0;

        default:
            Console.Error.WriteLine($"Unknown command '{command}'. Use build, check or stats.");
            return 2;
    }
}
catch (GazetteerValidationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static byte[] Payload(Gazetteer g)
{
    using var ms = new MemoryStream();
    GazetteerSerializer.WritePayload(g, ms);
    return ms.ToArray();
}

static void PrintStats(Gazetteer g)
{
    Console.WriteLine($"Data version {g.DataVersion}: {g.Provinces.Count} il, {g.Districts.Count} ilçe, {g.Units.Count} birim, {g.Aliases.Count} alias");
    foreach (IGrouping<UnitKind, SettlementUnit> kind in g.Units.GroupBy(u => u.Kind).OrderBy(k => k.Key))
    {
        int n = kind.Count();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  {kind.Key,-10} {n,7}   postal {Pct(kind.Count(u => u.PostalCode is not null), n),6}   coords {Pct(kind.Count(u => u.Location is not null), n),6}   wikidata {Pct(kind.Count(u => u.WikidataId != 0), n),6}"));
    }

    foreach (IGrouping<AliasKind, GazetteerAlias> kind in g.Aliases.GroupBy(a => a.Kind).OrderBy(k => k.Key))
    {
        Console.WriteLine($"  alias {kind.Key,-9} {kind.Count(),7}");
    }

    static string Pct(int part, int total) =>
        total == 0 ? "-" : (100.0 * part / total).ToString("F1", CultureInfo.InvariantCulture) + "%";
}

static string FindRepoRoot()
{
    for (DirectoryInfo? dir = new(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "AdresTR.slnx")))
        {
            return dir.FullName;
        }
    }

    throw new InvalidOperationException("Run inside the AdresTR repository (AdresTR.slnx not found).");
}
