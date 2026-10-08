using AdresTR.Text;

namespace AdresTR.Eval;

/// <summary>Precision / recall / F1 counts for one field.</summary>
internal sealed class FieldScore
{
    public int TruePositive { get; set; }

    public int FalsePositive { get; set; }

    public int FalseNegative { get; set; }

    /// <summary>Examples in which the field was annotated.</summary>
    public int Support { get; set; }

    public double Precision => Ratio(TruePositive, TruePositive + FalsePositive);

    public double Recall => Ratio(TruePositive, TruePositive + FalseNegative);

    public double F1 => Ratio(2 * TruePositive, (2 * TruePositive) + FalsePositive + FalseNegative);

    internal static double Ratio(int a, int b) => b == 0 ? double.NaN : (double)a / b;
}

/// <summary>Per-example outcome, kept so metrics can be bootstrapped and broken down.</summary>
internal sealed record ExampleOutcome(
    EvalExample Example,
    bool ExactMatch,
    IReadOnlyDictionary<string, (int Tp, int Fp, int Fn)> Fields,
    bool? IlCorrect,
    bool? IlceCorrect,
    bool? BirimCorrect,
    int? BirimRank,
    double? Confidence,
    bool PredictedIds);

/// <summary>Aggregated metrics for one system on one set.</summary>
internal sealed class EvalResult
{
    public required string System { get; init; }

    public required string Set { get; init; }

    public int Count { get; init; }

    public double ExactMatch { get; init; }

    public (double Low, double High) ExactMatchCi { get; init; }

    public double MicroF1 { get; init; }

    public (double Low, double High) MicroF1Ci { get; init; }

    public double MacroF1 { get; init; }

    public required Dictionary<string, FieldScore> Fields { get; init; }

    public double IlAccuracy { get; init; }

    public double IlceAccuracy { get; init; }

    public double BirimAccuracy { get; init; }

    public (double Low, double High) BirimAccuracyCi { get; init; }

    public double BirimAt5 { get; init; }

    public double BirimMrr { get; init; }

    public double? Ece { get; init; }

    public double? MillisecondsPerAddress { get; init; }

    public required Dictionary<string, (int N, double ExactMatch, double MicroF1)> BySource { get; init; }

    public required Dictionary<string, (int N, double ExactMatch, double MicroF1)> ByTag { get; init; }
}

/// <summary>Scores predictions against gold examples following the comparison rules of eval/SCHEMA.md.</summary>
internal static class Metrics
{
    private const int BootstrapSamples = 1000;

    public static bool ValuesEqual(string field, string? gold, string? predicted)
    {
        if (gold is null || predicted is null)
        {
            return gold is null && predicted is null;
        }

        return field switch
        {
            Labels.CsbmTur => string.Equals(gold.Trim(), predicted.Trim(), StringComparison.OrdinalIgnoreCase),
            Labels.DisKapi or Labels.Blok => Compact(gold) == Compact(predicted),
            Labels.Kat or Labels.Daire or Labels.PostaKodu => gold.Trim() == predicted.Trim(),
            _ => AdresTR.Gazetteer.Key(gold) == AdresTR.Gazetteer.Key(predicted),
        };

        static string Compact(string s) => TurkishText.Fold(s).Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    public static ExampleOutcome Score(EvalExample e, Prediction? p)
    {
        p ??= new Prediction { Id = e.Id };
        var fields = new Dictionary<string, (int, int, int)>(StringComparer.Ordinal);
        bool exact = true;

        foreach (var (field, gold) in e.Fields)
        {
            if (field == Labels.Diger)
            {
                continue;
            }

            string? predicted = Normalize(p.Get(field));
            bool equal = ValuesEqual(field, gold, predicted);
            exact &= equal;

            fields[field] = (gold, predicted) switch
            {
                (not null, not null) when equal => (1, 0, 0),
                (not null, not null) => (0, 1, 1),
                (not null, null) => (0, 0, 1),
                (null, not null) => (0, 1, 0),
                _ => (0, 0, 0),
            };
        }

        bool? il = Check(e.Il, p.Il);
        bool? ilce = Check(e.Ilce, p.Ilce);
        bool? birim = Check(e.Birim, p.Birim);

        // Exact match is defined on the annotated fields only, so systems without gazetteer ids
        // (libpostal, LLMs) are comparable; id resolution is reported separately.

        int? rank = null;
        if (e.Birim is { Annotated: true, Value: int goldBirim })
        {
            IEnumerable<int> ranked = p.Candidates.Count > 0
                ? p.Candidates.OrderByDescending(c => c.Score).Select(c => c.Birim)
                : p.Birim is int b ? [b] : [];
            int position = 1;
            foreach (int id in ranked)
            {
                if (id == goldBirim)
                {
                    rank = position;
                    break;
                }

                position++;
            }
        }

        return new ExampleOutcome(e, exact, fields, il, ilce, birim, rank, p.Confidence, p.Il is not null || p.Ilce is not null || p.Birim is not null || p.Candidates.Count > 0);

        static bool? Check(OptionalId gold, int? predicted) => gold.Annotated ? gold.Value == predicted : null;

        static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static EvalResult Aggregate(string system, string set, IReadOnlyList<ExampleOutcome> outcomes, double? msPerAddress = null)
    {
        var fieldScores = Labels.Scored.ToDictionary(f => f, _ => new FieldScore(), StringComparer.Ordinal);
        foreach (ExampleOutcome o in outcomes)
        {
            foreach (var (field, (tp, fp, fn)) in o.Fields)
            {
                FieldScore s = fieldScores[field];
                s.Support++;
                s.TruePositive += tp;
                s.FalsePositive += fp;
                s.FalseNegative += fn;
            }
        }

        var scored = fieldScores.Where(kv => kv.Value.Support > 0).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        double[] f1s = [.. scored.Values.Where(s => s.TruePositive + s.FalsePositive + s.FalseNegative > 0).Select(s => s.F1)];

        // Systems without a gazetteer (libpostal, LLMs) return no ids: id metrics are not applicable to them.
        bool ids = outcomes.Any(o => o.PredictedIds);
        var birimOutcomes = ids ? outcomes.Where(o => o.Example.Birim is { Annotated: true, Value: not null }).ToList() : [];
        var ilOutcomes = ids ? outcomes.Where(o => o.IlCorrect is not null).ToList() : [];
        var ilceOutcomes = ids ? outcomes.Where(o => o.IlceCorrect is not null).ToList() : [];

        return new EvalResult
        {
            System = system,
            Set = set,
            Count = outcomes.Count,
            ExactMatch = Mean(outcomes, o => o.ExactMatch),
            ExactMatchCi = Bootstrap(outcomes, s => Mean(s, o => o.ExactMatch)),
            MicroF1 = MicroF1(outcomes),
            MicroF1Ci = Bootstrap(outcomes, MicroF1),
            MacroF1 = f1s.Length == 0 ? double.NaN : f1s.Average(),
            Fields = scored,
            IlAccuracy = Mean(ilOutcomes, o => o.IlCorrect == true),
            IlceAccuracy = Mean(ilceOutcomes, o => o.IlceCorrect == true),
            BirimAccuracy = Mean(birimOutcomes, o => o.BirimRank == 1),
            BirimAccuracyCi = Bootstrap(birimOutcomes, s => Mean(s, o => o.BirimRank == 1)),
            BirimAt5 = Mean(birimOutcomes, o => o.BirimRank is <= 5),
            BirimMrr = birimOutcomes.Count == 0 ? double.NaN : birimOutcomes.Average(o => o.BirimRank is int r ? 1.0 / r : 0),
            Ece = ExpectedCalibrationError(outcomes),
            MillisecondsPerAddress = msPerAddress,
            BySource = Breakdown(outcomes, o => [o.Example.Source]),
            ByTag = Breakdown(outcomes, o => [.. o.Example.Tags, .. o.Example.Noise.Select(n => "noise:" + n)]),
        };
    }

    internal static double MicroF1(IReadOnlyList<ExampleOutcome> outcomes)
    {
        int tp = 0, fp = 0, fn = 0;
        foreach (ExampleOutcome o in outcomes)
        {
            foreach (var (t, p, n) in o.Fields.Values)
            {
                tp += t;
                fp += p;
                fn += n;
            }
        }

        return tp == 0 ? (fp + fn == 0 ? double.NaN : 0) : 2.0 * tp / (2.0 * tp + fp + fn);
    }

    /// <summary>
    /// Expected calibration error (15 equal-width bins) of the prediction confidence against example correctness
    /// (exact match). Returns null when the system reports no confidence.
    /// </summary>
    internal static double? ExpectedCalibrationError(IReadOnlyList<ExampleOutcome> outcomes)
    {
        var withConfidence = outcomes.Where(o => o.Confidence is not null).ToList();
        if (withConfidence.Count == 0)
        {
            return null;
        }

        const int bins = 15;
        double ece = 0;
        foreach (var bin in withConfidence.GroupBy(o => Math.Min(bins - 1, (int)(Math.Clamp(o.Confidence!.Value, 0, 1) * bins))))
        {
            double accuracy = bin.Average(o => o.ExactMatch ? 1.0 : 0.0);
            double confidence = bin.Average(o => o.Confidence!.Value);
            ece += (double)bin.Count() / withConfidence.Count * Math.Abs(accuracy - confidence);
        }

        return ece;
    }

    /// <summary>Percentile bootstrap 95% confidence interval (fixed seed for reproducibility).</summary>
    internal static (double Low, double High) Bootstrap(IReadOnlyList<ExampleOutcome> outcomes, Func<IReadOnlyList<ExampleOutcome>, double> metric)
    {
        if (outcomes.Count == 0)
        {
            return (double.NaN, double.NaN);
        }

        var random = new Random(20261008);
        var values = new double[BootstrapSamples];
        var sample = new ExampleOutcome[outcomes.Count];
        for (int b = 0; b < BootstrapSamples; b++)
        {
            for (int i = 0; i < sample.Length; i++)
            {
                sample[i] = outcomes[random.Next(outcomes.Count)];
            }

            values[b] = metric(sample);
        }

        Array.Sort(values);
        return (values[(int)(0.025 * BootstrapSamples)], values[(int)(0.975 * BootstrapSamples) - 1]);
    }

    private static double Mean(IReadOnlyList<ExampleOutcome> outcomes, Func<ExampleOutcome, bool> predicate) =>
        outcomes.Count == 0 ? double.NaN : outcomes.Count(predicate) / (double)outcomes.Count;

    private static Dictionary<string, (int, double, double)> Breakdown(IReadOnlyList<ExampleOutcome> outcomes, Func<ExampleOutcome, string[]> keys) =>
        outcomes
            .SelectMany(o => keys(o).Select(k => (Key: k, Outcome: o)))
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var list = g.Select(x => x.Outcome).ToList();
                    return (list.Count, Mean(list, o => o.ExactMatch), MicroF1(list));
                },
                StringComparer.Ordinal);
}
