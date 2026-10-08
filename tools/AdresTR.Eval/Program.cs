// AdresTR.Eval — benchmark tooling (Faz 3). See eval/README.md.
//
//   generate                       writes eval/synthetic/{dev,test}.jsonl (fixed seeds)
//   run --system <name|file> --set <path.jsonl> [--split dev|test]
//                                  scores one system on one set and prints the result
//   report                         scores every system on every set and writes eval/results/
//   validate                       checks every eval/**/*.jsonl against eval/SCHEMA.md and the gazetteer

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdresTR;
using AdresTR.Data;
using AdresTR.Eval;

string root = FindRepoRoot();
string evalDir = Path.Combine(root, "eval");
Gazetteer gazetteer = TurkishGazetteer.Default;

switch (args.FirstOrDefault())
{
    case "generate":
    {
        const int Seed = 20261008;
        string dir = Path.Combine(evalDir, "synthetic");
        Jsonl.WriteExamples(Path.Combine(dir, "dev.jsonl"), new SyntheticGenerator(gazetteer, Seed).Generate("dev", 1000));
        Jsonl.WriteExamples(Path.Combine(dir, "test.jsonl"), new SyntheticGenerator(gazetteer, Seed + 1).Generate("test", 2000));
        Console.WriteLine($"Wrote {Path.GetRelativePath(root, dir)}/dev.jsonl (1000) and test.jsonl (2000), gazetteer {gazetteer.DataVersion}.");
        return 0;
    }

    case "run":
    {
        string system = Option("--system") ?? "regex";
        string set = Option("--set") ?? throw new ArgumentException("--set is required");
        string? split = Option("--split");
        List<EvalExample> examples = [.. Jsonl.ReadExamples(Path.GetFullPath(set)).Where(e => split is null || e.Split == split)];
        EvalResult result = Evaluate(system, Path.GetFileNameWithoutExtension(set), examples);
        Console.WriteLine(Report.Summary(result));
        Console.WriteLine(Report.FieldTable(result));
        return 0;
    }

    case "report":
    {
        var results = new List<EvalResult>();
        foreach ((string name, List<EvalExample> examples) in DiscoverSets())
        {
            results.Add(Evaluate("regex", name, examples));
            string predictions = Path.Combine(evalDir, "predictions");
            if (Directory.Exists(predictions))
            {
                foreach (string systemDir in Directory.GetDirectories(predictions).Order(StringComparer.Ordinal))
                {
                    string file = Path.Combine(systemDir, name + ".jsonl");
                    if (File.Exists(file))
                    {
                        results.Add(Evaluate(file, name, examples));
                    }
                }
            }
        }

        string outDir = Path.Combine(evalDir, "results");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "README.md"), Report.Leaderboard(results, gazetteer.DataVersion).ReplaceLineEndings("\n"));
        File.WriteAllText(Path.Combine(outDir, "results.json"), Report.Json(results).ReplaceLineEndings("\n"));
        Console.WriteLine(Report.Leaderboard(results, gazetteer.DataVersion));
        return 0;
    }

    case "validate":
    {
        int errors = 0;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(evalDir, "*.jsonl", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}predictions{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            int n = 0;
            foreach (EvalExample e in Jsonl.ReadExamples(file))
            {
                n++;
                foreach (string problem in Validation.Check(e, gazetteer))
                {
                    errors++;
                    Console.Error.WriteLine($"{Path.GetRelativePath(root, file)} {e.Id}: {problem}");
                }

                if (!ids.Add(e.Id))
                {
                    errors++;
                    Console.Error.WriteLine($"{Path.GetRelativePath(root, file)} {e.Id}: duplicate id");
                }
            }

            Console.WriteLine($"{Path.GetRelativePath(root, file)}: {n} examples");
        }

        Console.WriteLine(errors == 0 ? "All eval files are valid." : $"{errors} problem(s) found.");
        return errors == 0 ? 0 : 1;
    }

    default:
        Console.Error.WriteLine("Usage: AdresTR.Eval generate | run --system <regex|predictions.jsonl> --set <file> [--split dev|test] | report | validate");
        return 2;
}

EvalResult Evaluate(string system, string setName, List<EvalExample> examples)
{
    if (system == "regex")
    {
        IAddressSystem s = new RegexBaseline(gazetteer);
        var stopwatch = Stopwatch.StartNew();
        List<Prediction> predictions = [.. examples.Select(s.Predict)];
        stopwatch.Stop();
        var outcomes = examples.Zip(predictions, Metrics.Score).ToList();
        return Metrics.Aggregate(s.Name, setName, outcomes, stopwatch.Elapsed.TotalMilliseconds / Math.Max(1, examples.Count));
    }

    Dictionary<string, Prediction> fromFile = Jsonl.ReadPredictions(system);
    string name = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(system))) ?? system;
    return Metrics.Aggregate(name, setName, [.. examples.Select(e => Metrics.Score(e, fromFile.GetValueOrDefault(e.Id)))]);
}

IEnumerable<(string Name, List<EvalExample> Examples)> DiscoverSets()
{
    foreach (string set in new[] { "synthetic", "real", "challenge" })
    {
        string dir = Path.Combine(evalDir, set);
        if (!Directory.Exists(dir))
        {
            continue;
        }

        List<EvalExample> all = [.. Directory.GetFiles(dir, "*.jsonl").Order(StringComparer.Ordinal).SelectMany(Jsonl.ReadExamples)];
        foreach (string split in new[] { "dev", "test" })
        {
            List<EvalExample> part = [.. all.Where(e => e.Split == split)];
            if (part.Count > 0)
            {
                yield return ($"{set}-{split}", part);
            }
        }
    }
}

string? Option(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
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
