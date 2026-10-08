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
            results.Add(Evaluate("adrestr", name, examples));
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

    case "errors":
    {
        // Error analysis on dev data only: prints the examples a system gets wrong, field by field.
        string set = Option("--set") ?? throw new ArgumentException("--set is required");
        string split = Option("--split") ?? "dev";
        if (split != "dev")
        {
            Console.Error.WriteLine("Error analysis is only allowed on dev splits (never tune on test).");
            return 2;
        }

        int limit = int.Parse(Option("--limit") ?? "30", System.Globalization.CultureInfo.InvariantCulture);
        string? tag = Option("--tag");
        IAddressSystem s = new AdresTRSystem(new AddressParser(gazetteer));
        int shown = 0;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (EvalExample e in Jsonl.ReadExamples(Path.GetFullPath(set)).Where(e => e.Split == split && (tag is null || e.Tags.Contains(tag) || e.Noise.Contains(tag))))
        {
            Prediction p = s.Predict(e);
            ExampleOutcome o = Metrics.Score(e, p);
            var wrong = e.Fields.Where(f => f.Key != Labels.Diger && !Metrics.ValuesEqual(f.Key, f.Value, string.IsNullOrWhiteSpace(p.Get(f.Key)) ? null : p.Get(f.Key))).ToList();
            bool idWrong = o.BirimCorrect == false || o.IlceCorrect == false || o.IlCorrect == false;
            foreach (var w in wrong)
            {
                counts[w.Key] = counts.GetValueOrDefault(w.Key) + 1;
            }

            if (idWrong)
            {
                counts["ids"] = counts.GetValueOrDefault("ids") + 1;
            }

            if ((wrong.Count == 0 && !idWrong) || shown >= limit)
            {
                continue;
            }

            shown++;
            Console.WriteLine($"{e.Id}  {e.Text}");
            foreach (var w in wrong)
            {
                Console.WriteLine($"    {w.Key,-11} gold={w.Value ?? "∅"}  pred={p.Get(w.Key) ?? "∅"}");
            }

            if (idWrong)
            {
                Console.WriteLine($"    ids        gold il={e.Il.Value?.ToString() ?? "∅"} ilce={e.Ilce.Value?.ToString() ?? "∅"} birim={e.Birim.Value?.ToString() ?? "∅"}  pred il={p.Il?.ToString() ?? "∅"} ilce={p.Ilce?.ToString() ?? "∅"} birim={p.Birim?.ToString() ?? "∅"}");
            }
        }

        Console.WriteLine("Wrong per field: " + string.Join(", ", counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}")));
        return 0;
    }

    case "gate":
    {
        // Accuracy regression gate (ADR-0008): dev metrics may not drop more than 0.5 points below eval/gate.json.
        const double Tolerance = 0.005;
        string gateFile = Path.Combine(evalDir, "gate.json");
        bool update = args.Contains("--update");
        var current = new JsonObject();
        foreach ((string name, List<EvalExample> examples) in DiscoverSets().Where(s => s.Name.EndsWith("-dev", StringComparison.Ordinal)))
        {
            EvalResult r = Evaluate("adrestr", name, examples);
            current[name] = new JsonObject
            {
                ["exact_match"] = Math.Round(r.ExactMatch, 4),
                ["micro_f1"] = Math.Round(r.MicroF1, 4),
                ["birim_accuracy"] = double.IsNaN(r.BirimAccuracy) ? null : Math.Round(r.BirimAccuracy, 4),
            };
        }

        if (update || !File.Exists(gateFile))
        {
            File.WriteAllText(gateFile, current.ToJsonString(new JsonSerializerOptions(Jsonl.Options) { WriteIndented = true }).ReplaceLineEndings("\n") + "\n");
            Console.WriteLine($"Wrote {Path.GetRelativePath(root, gateFile)}");
            Console.WriteLine(current.ToJsonString());
            return 0;
        }

        JsonObject baseline = JsonNode.Parse(File.ReadAllText(gateFile))!.AsObject();
        int failures = 0;
        foreach (var (set, metrics) in baseline)
        {
            foreach (var (metric, value) in metrics!.AsObject())
            {
                if (value is null)
                {
                    continue;
                }

                double expected = (double)value;
                double actual = (double?)current[set]?[metric] ?? double.NaN;
                bool ok = actual >= expected - Tolerance;
                failures += ok ? 0 : 1;
                Console.WriteLine(FormattableString.Invariant($"{(ok ? "ok  " : "FAIL")} {set,-16} {metric,-15} {actual:F4} (gate {expected:F4})"));
            }
        }

        return failures == 0 ? 0 : 1;
    }

    case "calibrate":
    {
        // Fits the softmax temperature on all dev splits (never test) by minimizing expected calibration error.
        List<EvalExample> dev = [.. DiscoverSets().Where(s => s.Name.EndsWith("-dev", StringComparison.Ordinal)).SelectMany(s => s.Examples)];
        var rows = new List<(double T, double Ece, double Exact)>();
        foreach (double t in new[] { 0.04, 0.06, 0.08, 0.1, 0.12, 0.15, 0.2, 0.3, 0.5, 1.0 })
        {
            var system = new AdresTRSystem(new AddressParser(gazetteer, t));
            List<ExampleOutcome> outcomes = [.. dev.Select(e => Metrics.Score(e, system.Predict(e)))];
            rows.Add((t, Metrics.ExpectedCalibrationError(outcomes) ?? double.NaN, outcomes.Count(o => o.ExactMatch) / (double)outcomes.Count));
        }

        foreach (var (t, ece, exact) in rows)
        {
            Console.WriteLine(FormattableString.Invariant($"T={t,5:F2}  ECE={ece:F4}  exact={exact:F4}"));
        }

        Console.WriteLine(FormattableString.Invariant($"Best T = {rows.MinBy(r => r.Ece).T} (n={dev.Count} dev examples)"));
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
    if (system is "regex" or "adrestr")
    {
        IAddressSystem s = system == "regex" ? new RegexBaseline(gazetteer) : new AdresTRSystem(new AddressParser(gazetteer));
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
