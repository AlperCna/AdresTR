using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AdresTR.Eval;

/// <summary>Label set of eval/SCHEMA.md.</summary>
internal static class Labels
{
    public const string Il = "il";
    public const string Ilce = "ilce";
    public const string Mahalle = "mahalle";
    public const string Semt = "semt";
    public const string CsbmTur = "csbm_tur";
    public const string CsbmAd = "csbm_ad";
    public const string Site = "site";
    public const string Blok = "blok";
    public const string DisKapi = "dis_kapi";
    public const string Kat = "kat";
    public const string Daire = "daire";
    public const string PostaKodu = "posta_kodu";
    public const string Tarif = "tarif";
    public const string Diger = "diger";

    /// <summary>Fields scored by the evaluator, in report order.</summary>
    public static readonly string[] Scored =
        [Il, Ilce, Mahalle, Semt, CsbmTur, CsbmAd, Site, Blok, DisKapi, Kat, Daire, PostaKodu, Tarif];

    public static readonly HashSet<string> All = [.. Scored, Diger];
}

/// <summary>Character span <c>[Start, End)</c> in UTF-16 code units.</summary>
internal sealed record Span(int Start, int End, string Label);

/// <summary>An id that may be absent (not annotated), null (annotated as undetermined) or a value.</summary>
internal readonly record struct OptionalId(bool Annotated, int? Value)
{
    public static readonly OptionalId Absent = new(false, null);

    public static OptionalId Of(int? value) => new(true, value);
}

/// <summary>One evaluation example (a JSONL line).</summary>
internal sealed class EvalExample
{
    public required string Id { get; init; }

    public required string Text { get; init; }

    public required string Source { get; init; }

    public required string License { get; init; }

    public required string Split { get; init; }

    public IReadOnlyList<Span>? Spans { get; init; }

    public OptionalId Il { get; init; } = OptionalId.Absent;

    public OptionalId Ilce { get; init; } = OptionalId.Absent;

    public OptionalId Birim { get; init; } = OptionalId.Absent;

    /// <summary>Annotated fields. A missing key means "not annotated"; a null value means "annotated as absent".</summary>
    public required IReadOnlyDictionary<string, string?> Fields { get; init; }

    public IReadOnlyList<string> Noise { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    public string? Note { get; init; }
}

/// <summary>Ranked birim candidate of a prediction.</summary>
internal sealed record Candidate(int Birim, double Score);

/// <summary>A system's output for one example.</summary>
internal sealed class Prediction
{
    public required string Id { get; init; }

    public Dictionary<string, string?> Fields { get; init; } = new(StringComparer.Ordinal);

    public int? Il { get; set; }

    public int? Ilce { get; set; }

    public int? Birim { get; set; }

    public double? Confidence { get; set; }

    public List<Candidate> Candidates { get; init; } = [];

    public string? Get(string field) => Fields.GetValueOrDefault(field);
}

/// <summary>Reads and writes eval and prediction JSONL files.</summary>
internal static class Jsonl
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public static IEnumerable<EvalExample> ReadExamples(string path)
    {
        int line = 0;
        foreach (string text in File.ReadLines(path, Encoding.UTF8))
        {
            line++;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            EvalExample example;
            try
            {
                example = ParseExample(JsonNode.Parse(text)!.AsObject());
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or NullReferenceException)
            {
                throw new InvalidDataException($"{path}:{line}: {ex.Message}", ex);
            }

            Validate(example, $"{path}:{line}");
            yield return example;
        }
    }

    public static EvalExample ParseExample(JsonObject o)
    {
        JsonObject gold = o["gold"]!.AsObject();
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in gold["fields"]!.AsObject())
        {
            fields[key] = value?.GetValueKind() == JsonValueKind.Null || value is null ? null : value.ToString();
        }

        return new EvalExample
        {
            Id = (string)o["id"]!,
            Text = (string)o["text"]!,
            Source = (string)o["source"]!,
            License = (string)o["license"]!,
            Split = (string)o["split"]!,
            Spans = o["spans"]?.AsArray().Select(s => new Span((int)s!["start"]!, (int)s["end"]!, (string)s["label"]!)).ToList(),
            Il = Id(gold, "il"),
            Ilce = Id(gold, "ilce"),
            Birim = Id(gold, "birim"),
            Fields = fields,
            Noise = Strings(o["noise"]),
            Tags = Strings(o["tags"]),
            Note = (string?)o["note"],
        };

        static OptionalId Id(JsonObject gold, string key) =>
            !gold.ContainsKey(key) ? OptionalId.Absent : OptionalId.Of(gold[key] is null ? null : (int)gold[key]!);

        static string[] Strings(JsonNode? node) =>
            node?.AsArray().Select(n => (string)n!).ToArray() ?? [];
    }

    public static void Validate(EvalExample e, string where)
    {
        if (e.Split is not ("dev" or "test"))
        {
            throw new InvalidDataException($"{where}: split must be dev or test");
        }

        foreach (string key in e.Fields.Keys)
        {
            if (!Labels.All.Contains(key))
            {
                throw new InvalidDataException($"{where}: unknown field '{key}'");
            }
        }

        foreach (Span s in e.Spans ?? [])
        {
            if (s.Start < 0 || s.End > e.Text.Length || s.Start >= s.End || !Labels.All.Contains(s.Label))
            {
                throw new InvalidDataException($"{where}: invalid span {s}");
            }
        }
    }

    public static JsonObject ToJson(EvalExample e)
    {
        var fields = new JsonObject();
        foreach (var (key, value) in e.Fields)
        {
            fields[key] = value;
        }

        var gold = new JsonObject();
        AddId(gold, "il", e.Il);
        AddId(gold, "ilce", e.Ilce);
        AddId(gold, "birim", e.Birim);
        gold["fields"] = fields;

        var o = new JsonObject
        {
            ["id"] = e.Id,
            ["text"] = e.Text,
            ["source"] = e.Source,
            ["license"] = e.License,
            ["split"] = e.Split,
        };

        if (e.Spans is not null)
        {
            o["spans"] = new JsonArray([.. e.Spans.Select(s => (JsonNode)new JsonObject { ["start"] = s.Start, ["end"] = s.End, ["label"] = s.Label })]);
        }

        o["gold"] = gold;
        if (e.Noise.Count > 0)
        {
            o["noise"] = new JsonArray([.. e.Noise.Select(n => (JsonNode)n)]);
        }

        if (e.Tags.Count > 0)
        {
            o["tags"] = new JsonArray([.. e.Tags.Select(t => (JsonNode)t)]);
        }

        if (e.Note is not null)
        {
            o["note"] = e.Note;
        }

        return o;

        static void AddId(JsonObject gold, string key, OptionalId id)
        {
            if (id.Annotated)
            {
                gold[key] = id.Value;
            }
        }
    }

    public static void WriteExamples(string path, IEnumerable<EvalExample> examples)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
        foreach (EvalExample e in examples)
        {
            writer.WriteLine(ToJson(e).ToJsonString(Options));
        }
    }

    public static Dictionary<string, Prediction> ReadPredictions(string path)
    {
        var result = new Dictionary<string, Prediction>(StringComparer.Ordinal);
        foreach (string text in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            JsonObject o = JsonNode.Parse(text)!.AsObject();
            var p = new Prediction { Id = (string)o["id"]! };
            foreach (var (key, value) in o["fields"]?.AsObject() ?? [])
            {
                p.Fields[key] = value?.ToString();
            }

            if (o["ids"] is JsonObject ids)
            {
                p.Il = (int?)ids["il"];
                p.Ilce = (int?)ids["ilce"];
                p.Birim = (int?)ids["birim"];
            }

            p.Confidence = (double?)o["confidence"];
            foreach (JsonNode? c in o["candidates"]?.AsArray() ?? [])
            {
                p.Candidates.Add(new Candidate((int)c!["birim"]!, (double)c["score"]!));
            }

            result[p.Id] = p;
        }

        return result;
    }

    public static void WritePredictions(string path, IEnumerable<Prediction> predictions)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
        foreach (Prediction p in predictions)
        {
            var fields = new JsonObject();
            foreach (var (key, value) in p.Fields)
            {
                fields[key] = value;
            }

            var o = new JsonObject { ["id"] = p.Id, ["fields"] = fields };
            if (p.Il is not null || p.Ilce is not null || p.Birim is not null)
            {
                o["ids"] = new JsonObject { ["il"] = p.Il, ["ilce"] = p.Ilce, ["birim"] = p.Birim };
            }

            if (p.Confidence is not null)
            {
                o["confidence"] = p.Confidence;
            }

            if (p.Candidates.Count > 0)
            {
                o["candidates"] = new JsonArray([.. p.Candidates.Select(c => (JsonNode)new JsonObject { ["birim"] = c.Birim, ["score"] = c.Score })]);
            }

            writer.WriteLine(o.ToJsonString(Options));
        }
    }
}
