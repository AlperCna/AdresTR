using System.Text.Json;
using AdresTR.Data;

namespace AdresTR.Eval.Tests;

public class SyntheticGeneratorTests
{
    private static readonly AdresTR.Gazetteer G = TurkishGazetteer.Default;

    [Fact]
    public void Is_deterministic_for_a_seed()
    {
        string Render(int seed) => string.Join('\n', new SyntheticGenerator(G, seed).Generate("dev", 50)
            .Select(e => Jsonl.ToJson(e).ToJsonString(Jsonl.Options)));

        Assert.Equal(Render(7), Render(7));
        Assert.NotEqual(Render(7), Render(8));
    }

    [Fact]
    public void Generated_examples_are_valid_and_consistent()
    {
        List<EvalExample> examples = [.. new SyntheticGenerator(G, 42).Generate("test", 500)];

        Assert.All(examples, e =>
        {
            Jsonl.Validate(e, e.Id);
            Assert.Empty(Validation.Check(e, G));
            Assert.NotNull(e.Spans);

            foreach (Span s in e.Spans!)
            {
                string text = e.Text[s.Start..s.End];
                Assert.Equal(text.Trim(), text);
                Assert.NotEmpty(text);
            }

            // Every scored label key is annotated (null or value).
            Assert.All(Labels.Scored, label => Assert.True(e.Fields.ContainsKey(label), $"{e.Id} misses {label}"));

            // Unit-level values that appear in a span are the gold values (no typo applied to them).
            foreach (Span s in e.Spans!.Where(s => s.Label is Labels.DisKapi or Labels.Daire or Labels.Kat or Labels.PostaKodu))
            {
                Assert.Equal(e.Fields[s.Label], e.Text[s.Start..s.End]);
            }
        });
    }

    [Fact]
    public void Undetermined_units_have_null_gold_and_determined_units_are_unique()
    {
        List<EvalExample> examples = [.. new SyntheticGenerator(G, 3).Generate("dev", 400)];

        Assert.Contains(examples, e => e.Birim.Value is null);
        Assert.Contains(examples, e => e.Birim.Value is not null);
        Assert.All(examples.Where(e => e.Birim.Value is not null), e =>
            Assert.Equal(e.Ilce.Value, G.GetUnit(e.Birim.Value!.Value)!.District.Id));
    }

    [Fact]
    public void Fields_say_what_the_text_says()
    {
        List<EvalExample> examples = [.. new SyntheticGenerator(G, 11).Generate("dev", 600)];

        Assert.All(examples.Where(e => e.Noise.Contains("missing-il")), e => Assert.Null(e.Fields[Labels.Il]));
        Assert.All(examples.Where(e => e.Noise.Contains("missing-ilce")), e => Assert.Null(e.Fields[Labels.Ilce]));
        Assert.All(examples, e => Assert.Equal(e.Fields[Labels.Il] is not null, e.Spans!.Any(s => s.Label == Labels.Il)));

        // The il can still be determined from a unique ilçe/mahalle even when it is not written.
        Assert.Contains(examples, e => e.Fields[Labels.Il] is null && e.Il.Value is not null);
    }

    [Fact]
    public void Covers_noise_operations_and_tags()
    {
        List<EvalExample> examples = [.. new SyntheticGenerator(G, 5).Generate("dev", 1000)];
        string[] noise = [.. examples.SelectMany(e => e.Noise).Distinct()];

        foreach (string op in new[] { "ascii", "uppercase", "lowercase", "typo", "glued", "missing-il", "missing-ilce", "reordered", "phone", "landmark", "broken-i", "duplicate-token" })
        {
            Assert.Contains(op, noise);
        }

        Assert.Contains(examples, e => e.Noise.Count == 0);
        Assert.Contains(examples, e => e.Tags.Contains("koy"));
        Assert.Contains(examples, e => e.Tags.Contains("numbered-street"));
        Assert.Contains(examples, e => e.Tags.Contains("slash-door"));
    }

    [Fact]
    public void Output_is_valid_json_lines()
    {
        foreach (EvalExample e in new SyntheticGenerator(G, 9).Generate("dev", 20))
        {
            string line = Jsonl.ToJson(e).ToJsonString(Jsonl.Options);
            Assert.DoesNotContain('\n', line);
            using JsonDocument doc = JsonDocument.Parse(line);
            Assert.Equal(e.Text, doc.RootElement.GetProperty("text").GetString());
        }
    }
}
