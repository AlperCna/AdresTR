using System.Text;
using AdresTR;
using AdresTR.Text;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AdresTR.Api;

/// <summary>Maps the /v1 endpoints.</summary>
internal static class Endpoints
{
    public const int MaxBatchItems = 1000;
    public const int MaxCsvBytes = 5 * 1024 * 1024;

    public static void MapAdresTR(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder v1 = app.MapGroup("/v1").RequireRateLimiting("per-ip");

        v1.MapPost("/parse", Parse)
            .WithName("Parse").WithTags("Parse")
            .WithSummary("Parses one Turkish address into components and resolves il/ilçe/mahalle.");

        v1.MapPost("/parse/batch", ParseBatch)
            .WithName("ParseBatch").WithTags("Parse")
            .WithSummary($"Parses up to {MaxBatchItems} addresses.");

        v1.MapPost("/parse/csv", ParseCsv)
            .WithName("ParseCsv").WithTags("Parse")
            .Accepts<string>("text/csv")
            .Produces<string>(StatusCodes.Status200OK, "text/csv")
            .WithSummary("Cleans the address column of a CSV file (UTF-8, header row, max 5 MB) and appends structured columns.");

        v1.MapPost("/validate", Validate)
            .WithName("Validate").WithTags("Validate")
            .WithSummary("Checks that a structured il / ilçe / mahalle / postal code combination exists and is consistent.");

        RouteGroupBuilder reference = v1.MapGroup("/").WithTags("Reference").CacheOutput("reference");
        reference.MapGet("/provinces", (Gazetteer g) => g.Provinces.Select(p => new ProvinceDto(p.Plaka, p.Name)))
            .WithName("ListProvinces").WithSummary("All 81 il.");
        reference.MapGet("/provinces/{plaka:int}/districts", Districts)
            .WithName("ListDistricts").WithSummary("İlçe of an il.");
        reference.MapGet("/districts/{id:int}/units", Units)
            .WithName("ListUnits").WithSummary("Mahalle, köy and other units of an ilçe.");
        reference.MapGet("/units/{id:int}", Unit)
            .WithName("GetUnit").WithSummary("One settlement unit.");
        reference.MapGet("/autocomplete", Autocomplete)
            .WithName("Autocomplete").WithSummary("Name suggestions by prefix (case, diacritics and spaces ignored).");
    }

    private static Results<Ok<ParseResponse>, ValidationProblem> Parse(ParseRequest request, AddressParser parser, Gazetteer gazetteer) =>
        TypedResults.Ok(ParseResponse.From(parser.Parse(request.Text ?? string.Empty), gazetteer.DataVersion));

    private static Results<Ok<BatchParseResponse>, ValidationProblem> ParseBatch(BatchParseRequest request, AddressParser parser, Gazetteer gazetteer)
    {
        if (request.Items.Any(t => t is null || t.Length > AddressParser.MaxInputLength))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["items"] = [$"Every item must be a string of at most {AddressParser.MaxInputLength} characters."],
            });
        }

        return TypedResults.Ok(new BatchParseResponse([.. request.Items.Select(t => ParseResponse.From(parser.Parse(t), gazetteer.DataVersion))]));
    }

    private static async Task<IResult> ParseCsv(HttpRequest request, AddressParser parser, string? column, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxCsvBytes)
        {
            return TypedResults.Problem($"CSV files are limited to {MaxCsvBytes / (1024 * 1024)} MB.", statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        char[] buffer = new char[MaxCsvBytes + 1];
        int length = await reader.ReadBlockAsync(buffer, cancellationToken);
        if (length > MaxCsvBytes)
        {
            return TypedResults.Problem($"CSV files are limited to {MaxCsvBytes / (1024 * 1024)} MB.", statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        List<List<string>> rows = Csv.Read(new string(buffer, 0, length));
        if (rows.Count == 0)
        {
            return TypedResults.Problem("The CSV file is empty.", statusCode: StatusCodes.Status400BadRequest);
        }

        string name = column ?? "adres";
        int index = rows[0].FindIndex(h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return TypedResults.Problem($"Column '{name}' not found. Use ?column=<name>. Columns: {string.Join(", ", rows[0])}", statusCode: StatusCodes.Status400BadRequest);
        }

        var output = new StringBuilder();
        output.Append(Csv.Line([.. rows[0], "il", "ilce", "mahalle", "posta_kodu", "birim_id", "adres_kanonik", "guven"])).Append('\n');
        foreach (List<string> row in rows.Skip(1))
        {
            string text = index < row.Count ? row[index] : string.Empty;
            ParseResult r = parser.Parse(text);
            output.Append(Csv.Line(
            [
                .. row,
                r.Province?.Name ?? string.Empty,
                r.District?.Name ?? string.Empty,
                r.Unit?.Name ?? r.Mahalle?.Value ?? string.Empty,
                r.PostalCode?.Value ?? r.Unit?.PostalCode ?? string.Empty,
                r.Unit?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                r.ToCanonicalString(),
                r.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            ])).Append('\n');
        }

        return TypedResults.Text(output.ToString(), "text/csv; charset=utf-8");
    }

    private static Ok<ValidateResponse> Validate(ValidateRequest request, Gazetteer g)
    {
        var problems = new List<string>();
        Province? il = null;
        if (!string.IsNullOrWhiteSpace(request.Il))
        {
            il = g.FindProvinces(request.Il).FirstOrDefault().Entity;
            if (il is null)
            {
                problems.Add($"Unknown il '{request.Il}'.");
            }
        }

        District? ilce = null;
        if (!string.IsNullOrWhiteSpace(request.Ilce))
        {
            var districts = g.FindDistricts(request.Ilce, il?.Plaka ?? 0);
            if (districts.Count == 0)
            {
                problems.Add(il is null ? $"Unknown ilçe '{request.Ilce}'." : $"İlçe '{request.Ilce}' is not in {il.Name}.");
            }
            else if (districts.Count == 1)
            {
                ilce = districts[0].Entity;
                il ??= ilce.Province;
            }
            else
            {
                problems.Add($"İlçe '{request.Ilce}' exists in several il; give the il.");
            }
        }

        SettlementUnit? birim = null;
        if (!string.IsNullOrWhiteSpace(request.Mahalle))
        {
            List<SettlementUnit> units = [.. g.FindUnits(StripType(request.Mahalle), ilce?.Id ?? 0).Select(m => m.Entity)
                .Where(u => il is null || u.District.Province == il)];
            if (!string.IsNullOrWhiteSpace(request.PostaKodu) && units.Count > 1)
            {
                units = units.FindAll(u => u.PostalCode == request.PostaKodu.Trim()) is { Count: > 0 } byCode ? byCode : units;
            }

            if (units.Count == 0)
            {
                problems.Add(ilce is not null ? $"Mahalle '{request.Mahalle}' is not in {ilce.Name}." : $"Unknown mahalle '{request.Mahalle}'.");
            }
            else if (units.Select(u => u.Id).Distinct().Count() == 1)
            {
                birim = units[0];
                ilce ??= birim.District;
                il ??= ilce.Province;
            }
            else if (ilce is null)
            {
                problems.Add($"Mahalle '{request.Mahalle}' exists in {units.Count} places; give the ilçe.");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.PostaKodu))
        {
            string code = request.PostaKodu.Trim();
            if (code.Length != 5 || !code.All(char.IsAsciiDigit))
            {
                problems.Add($"Postal code '{code}' must have 5 digits.");
            }
            else if (il is not null && int.Parse(code.AsSpan(0, 2), System.Globalization.CultureInfo.InvariantCulture) != il.Plaka)
            {
                problems.Add($"Postal code {code} does not belong to {il.Name} ({il.Plaka:D2}xxx).");
            }
            else if (birim?.PostalCode is string expected && expected != code)
            {
                problems.Add($"The postal code of {birim.Name} is {expected}, not {code}.");
            }
        }

        return TypedResults.Ok(new ValidateResponse(problems.Count == 0, ProvinceDto.From(il), DistrictDto.From(ilce), UnitDto.From(birim), problems));

        static string StripType(string name)
        {
            string folded = TurkishText.Fold(name);
            foreach (string suffix in new[] { " mahallesi", " mah.", " mah", " mh.", " mh", " koyu", " koy" })
            {
                if (folded.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return name[..^suffix.Length].TrimEnd();
                }
            }

            return name;
        }
    }

    private static Results<Ok<IEnumerable<DistrictDto>>, NotFound> Districts(int plaka, Gazetteer g) =>
        g.GetProvince(plaka) is { } p ? TypedResults.Ok(p.Districts.Select(d => new DistrictDto(d.Id, d.Name, plaka))) : TypedResults.NotFound();

    private static Results<Ok<IEnumerable<UnitDto>>, NotFound> Units(int id, Gazetteer g) =>
        g.GetDistrict(id) is { } d ? TypedResults.Ok(d.Units.Select(u => UnitDto.From(u)!)) : TypedResults.NotFound();

    private static Results<Ok<UnitDto>, NotFound> Unit(int id, Gazetteer g) =>
        g.GetUnit(id) is { } u ? TypedResults.Ok(UnitDto.From(u)!) : TypedResults.NotFound();

    private static Results<Ok<IEnumerable<SuggestionDto>>, ValidationProblem> Autocomplete(string q, string? level, int? plaka, int? districtId, Gazetteer g)
    {
        string prefix = AdresTR.Gazetteer.Key(q);
        if (prefix.Length < 2)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["q"] = ["At least 2 letters are required."] });
        }

        IEnumerable<SuggestionDto> results = (level ?? "unit") switch
        {
            "province" => g.Provinces.Where(p => Matches(p.Name)).Select(p => new SuggestionDto("province", p.Plaka, p.Name, null)),
            "district" => g.Districts.Where(d => (plaka is null || d.Province.Plaka == plaka) && Matches(d.Name))
                .Select(d => new SuggestionDto("district", d.Id, d.Name, d.Province.Name)),
            _ => (districtId is int id ? g.GetDistrict(id)?.Units ?? []
                    : plaka is int p ? g.GetProvince(p)?.Districts.SelectMany(d => d.Units) ?? []
                    : g.Units)
                .Where(u => u.Kind is UnitKind.Mahalle or UnitKind.Koy or UnitKind.Osb && Matches(u.Name))
                .Select(u => new SuggestionDto("unit", u.Id, u.Name, $"{u.District.Name}/{u.District.Province.Name}")),
        };

        return TypedResults.Ok(results.Take(20));

        bool Matches(string name) => AdresTR.Gazetteer.Key(name).StartsWith(prefix, StringComparison.Ordinal);
    }
}
