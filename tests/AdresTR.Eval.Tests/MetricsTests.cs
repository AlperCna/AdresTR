using System.Text.Json.Nodes;
using AdresTR.Data;

namespace AdresTR.Eval.Tests;

public class MetricsTests
{
    private static EvalExample Example(Dictionary<string, string?> fields, int? birim = null, string id = "x") => new()
    {
        Id = id,
        Text = "irrelevant",
        Source = "test",
        License = "CC0-1.0",
        Split = "dev",
        Fields = fields,
        Birim = birim is null ? OptionalId.Absent : OptionalId.Of(birim),
    };

    [Theory]
    [InlineData("mahalle", "Caferağa", "CAFERAGA", true)]
    [InlineData("mahalle", "Gazi Osman Paşa", "gaziosmanpasa", true)]
    [InlineData("il", "İstanbul", "Istanbul", true)]
    [InlineData("dis_kapi", "17/A", "17 / a", true)]
    [InlineData("dis_kapi", "17/A", "17A", false)]
    [InlineData("daire", "5", "05", false)]
    [InlineData("csbm_tur", "cadde", "CADDE", true)]
    [InlineData("csbm_tur", "cadde", "sokak", false)]
    public void Values_are_compared_with_schema_rules(string field, string gold, string predicted, bool equal) =>
        Assert.Equal(equal, Metrics.ValuesEqual(field, gold, predicted));

    [Fact]
    public void Scores_true_and_false_positives_and_negatives()
    {
        EvalExample e = Example(new() { ["il"] = "İstanbul", ["ilce"] = "Kadıköy", ["daire"] = null, ["kat"] = "3" });
        var p = new Prediction { Id = "x", Fields = { ["il"] = "istanbul", ["ilce"] = "Üsküdar", ["daire"] = "5" } };

        ExampleOutcome o = Metrics.Score(e, p);

        Assert.Equal((1, 0, 0), o.Fields["il"]);   // correct
        Assert.Equal((0, 1, 1), o.Fields["ilce"]); // wrong value = FP + FN
        Assert.Equal((0, 1, 0), o.Fields["daire"]); // predicted something that isn't there
        Assert.Equal((0, 0, 1), o.Fields["kat"]);  // missed
        Assert.False(o.ExactMatch);
    }

    [Fact]
    public void Unannotated_fields_are_ignored()
    {
        EvalExample e = Example(new() { ["il"] = "İzmir" });
        var p = new Prediction { Id = "x", Fields = { ["il"] = "İzmir", ["dis_kapi"] = "12" } };

        ExampleOutcome o = Metrics.Score(e, p);

        Assert.True(o.ExactMatch);
        Assert.False(o.Fields.ContainsKey("dis_kapi"));
    }

    [Fact]
    public void Blank_prediction_counts_as_absent()
    {
        ExampleOutcome o = Metrics.Score(Example(new() { ["blok"] = null }), new Prediction { Id = "x", Fields = { ["blok"] = "  " } });
        Assert.True(o.ExactMatch);
    }

    [Fact]
    public void Missing_prediction_scores_everything_as_missed()
    {
        ExampleOutcome o = Metrics.Score(Example(new() { ["il"] = "İzmir" }, birim: 35090001), null);
        Assert.False(o.ExactMatch);
        Assert.Equal(false, o.BirimCorrect);
        Assert.Null(o.BirimRank);
    }

    [Fact]
    public void Ranks_gold_unit_among_candidates()
    {
        var p = new Prediction { Id = "x", Birim = 1, Candidates = { new(1, 0.6), new(3, 0.1), new(2, 0.3) } };
        ExampleOutcome o = Metrics.Score(Example([], birim: 3), p);
        Assert.Equal(3, o.BirimRank);
        Assert.Equal(false, o.BirimCorrect);
    }

    [Fact]
    public void Aggregates_micro_and_macro_f1()
    {
        List<ExampleOutcome> outcomes =
        [
            Metrics.Score(Example(new() { ["il"] = "A", ["ilce"] = "B" }, id: "1"), new Prediction { Id = "1", Fields = { ["il"] = "A", ["ilce"] = "B" } }),
            Metrics.Score(Example(new() { ["il"] = "A", ["ilce"] = "B" }, id: "2"), new Prediction { Id = "2", Fields = { ["il"] = "A" } }),
        ];

        EvalResult r = Metrics.Aggregate("s", "set", outcomes);

        Assert.Equal(0.5, r.ExactMatch);
        Assert.Equal(1.0, r.Fields["il"].F1);
        Assert.Equal(2.0 / 3, r.Fields["ilce"].F1, 6);           // TP 1, FN 1
        Assert.Equal(2.0 * 3 / ((2.0 * 3) + 1), r.MicroF1, 6);  // TP 3, FN 1
        Assert.Equal((1.0 + (2.0 / 3)) / 2, r.MacroF1, 6);
        Assert.InRange(r.ExactMatchCi.Low, 0, 0.5);
        Assert.InRange(r.ExactMatchCi.High, 0.5, 1);
    }

    [Fact]
    public void Expected_calibration_error_is_zero_for_perfect_calibration_and_large_for_overconfidence()
    {
        ExampleOutcome Make(bool correct, double confidence) =>
            Metrics.Score(Example(new() { ["il"] = "A" }), new Prediction { Id = "x", Fields = { ["il"] = correct ? "A" : "B" }, Confidence = confidence });

        List<ExampleOutcome> calibrated = [Make(true, 1.0), Make(true, 1.0), Make(false, 0.0), Make(false, 0.0)];
        List<ExampleOutcome> overconfident = [Make(true, 0.99), Make(false, 0.99), Make(false, 0.99), Make(false, 0.99)];

        Assert.Equal(0, Metrics.ExpectedCalibrationError(calibrated)!.Value, 6);
        Assert.Equal(0.74, Metrics.ExpectedCalibrationError(overconfident)!.Value, 2);
        Assert.Null(Metrics.ExpectedCalibrationError([Metrics.Score(Example([]), null)]));
    }

    [Fact]
    public void Bootstrap_is_deterministic()
    {
        List<ExampleOutcome> outcomes = [.. Enumerable.Range(0, 50).Select(i =>
            Metrics.Score(Example(new() { ["il"] = "A" }, id: $"{i}"), new Prediction { Id = $"{i}", Fields = { ["il"] = i % 3 == 0 ? "B" : "A" } }))];

        Assert.Equal(
            Metrics.Bootstrap(outcomes, s => s.Count(o => o.ExactMatch) / (double)s.Count),
            Metrics.Bootstrap(outcomes, s => s.Count(o => o.ExactMatch) / (double)s.Count));
    }

    [Fact]
    public void Jsonl_distinguishes_absent_from_null()
    {
        const string line = """
            {"id":"a","text":"t","source":"s","license":"CC0-1.0","split":"dev","gold":{"il":34,"birim":null,"fields":{"il":"İstanbul","kat":null}}}
            """;

        EvalExample e = Jsonl.ParseExample(JsonNode.Parse(line)!.AsObject());

        Assert.Equal(OptionalId.Of(34), e.Il);
        Assert.Equal(OptionalId.Absent, e.Ilce);
        Assert.Equal(OptionalId.Of(null), e.Birim);
        Assert.True(e.Fields.ContainsKey("kat"));
        Assert.Null(e.Fields["kat"]);
        Assert.False(e.Fields.ContainsKey("daire"));

        string roundTrip = Jsonl.ToJson(e).ToJsonString(Jsonl.Options);
        Assert.Contains("\"birim\":null", roundTrip, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ilce\"", roundTrip, StringComparison.Ordinal);
    }

    [Fact]
    public void Regex_baseline_parses_a_clean_official_address()
    {
        var baseline = new RegexBaseline(TurkishGazetteer.Default);
        var e = new EvalExample
        {
            Id = "x", Source = "test", License = "CC0-1.0", Split = "dev", Fields = new Dictionary<string, string?>(),
            Text = "Caferağa Mah. Moda Cad. No:12 D:3 34710 Kadıköy/İstanbul",
        };

        Prediction p = baseline.Predict(e);

        Assert.Equal(34, p.Il);
        Assert.Equal(3423, p.Ilce);
        Assert.Equal("Caferağa", p.Get("mahalle"));
        Assert.NotNull(p.Birim);
        Assert.Equal("12", p.Get("dis_kapi"));
        Assert.Equal("3", p.Get("daire"));
        Assert.Equal("34710", p.Get("posta_kodu"));
        Assert.Equal("cadde", p.Get("csbm_tur"));
    }
}
