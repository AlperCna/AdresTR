using System.ComponentModel.DataAnnotations;
using AdresTR;

namespace AdresTR.Api;

/// <summary>An address to parse.</summary>
/// <param name="Text">Free-text address, at most 512 characters.</param>
public sealed record ParseRequest([property: Required, MaxLength(AddressParser.MaxInputLength)] string Text);

/// <summary>Up to 1,000 addresses to parse in one call.</summary>
/// <param name="Items">The addresses.</param>
public sealed record BatchParseRequest([property: Required, MaxLength(1000)] string[] Items);

/// <summary>Results of a batch call, in request order.</summary>
public sealed record BatchParseResponse(IReadOnlyList<ParseResponse> Results);

/// <summary>A component as written in the input.</summary>
/// <param name="Value">Normalized value (official name for il/ilçe/mahalle).</param>
/// <param name="Text">The text as written.</param>
/// <param name="Start">Start offset in the input.</param>
/// <param name="End">End offset (exclusive) in the input.</param>
public sealed record ComponentDto(string Value, string Text, int Start, int End)
{
    internal static ComponentDto? From(AddressComponent? c) => c is null ? null : new(c.Value, c.Text, c.Start, c.End);
}

/// <summary>Components found in the text.</summary>
public sealed record ComponentsDto(
    ComponentDto? Il,
    ComponentDto? Ilce,
    ComponentDto? Mahalle,
    ComponentDto? Semt,
    ComponentDto? Street,
    StreetType? StreetType,
    ComponentDto? Site,
    ComponentDto? Blok,
    ComponentDto? DoorNumber,
    ComponentDto? Floor,
    ComponentDto? Flat,
    ComponentDto? PostalCode,
    ComponentDto? Landmark);

/// <summary>An il.</summary>
public sealed record ProvinceDto(int Plaka, string Name)
{
    internal static ProvinceDto? From(Province? p) => p is null ? null : new(p.Plaka, p.Name);
}

/// <summary>An ilçe.</summary>
public sealed record DistrictDto(int Id, string Name, int Plaka)
{
    internal static DistrictDto? From(District? d) => d is null ? null : new(d.Id, d.Name, d.Province.Plaka);
}

/// <summary>A settlement unit (mahalle, köy, …).</summary>
public sealed record UnitDto(
    int Id, string Name, UnitKind Kind, string? ParentName, string? PostalCode, int DistrictId, string District, string Province,
    long? NviId, string? Wikidata, double? Latitude, double? Longitude)
{
    internal static UnitDto? From(SettlementUnit? u) => u is null
        ? null
        : new(u.Id, u.Name, u.Kind, u.ParentName, u.PostalCode, u.District.Id, u.District.Name, u.District.Province.Name,
            u.NviId == 0 ? null : u.NviId, u.WikidataId == 0 ? null : $"Q{u.WikidataId}", u.Location?.Latitude, u.Location?.Longitude);
}

/// <summary>What the text determines; null levels are absent or ambiguous.</summary>
public sealed record ResolvedDto(ProvinceDto? Il, DistrictDto? Ilce, UnitDto? Birim);

/// <summary>A ranked candidate unit.</summary>
public sealed record CandidateDto(int Id, string Name, string District, string Province, double Score);

/// <summary>A correction or inference made by the parser.</summary>
public sealed record CorrectionDto(CorrectionKind Kind, string Field, string? From, string To);

/// <summary>The parse of one address.</summary>
public sealed record ParseResponse(
    string Input,
    string Canonical,
    double Confidence,
    ComponentsDto Components,
    ResolvedDto Resolved,
    IReadOnlyList<CandidateDto> Candidates,
    IReadOnlyList<CorrectionDto> Corrections,
    string DataVersion)
{
    internal static ParseResponse From(ParseResult r, string dataVersion) => new(
        r.Input,
        r.ToCanonicalString(),
        r.Confidence,
        new ComponentsDto(
            ComponentDto.From(r.Il), ComponentDto.From(r.Ilce), ComponentDto.From(r.Mahalle), ComponentDto.From(r.Semt),
            ComponentDto.From(r.Street), r.StreetType, ComponentDto.From(r.Site), ComponentDto.From(r.Blok),
            ComponentDto.From(r.DoorNumber), ComponentDto.From(r.Floor), ComponentDto.From(r.Flat),
            ComponentDto.From(r.PostalCode), ComponentDto.From(r.Landmark)),
        new ResolvedDto(ProvinceDto.From(r.Province), DistrictDto.From(r.District), UnitDto.From(r.Unit)),
        [.. r.UnitCandidates.Take(5).Select(c => new CandidateDto(c.Unit.Id, c.Unit.Name, c.Unit.District.Name, c.Unit.District.Province.Name, c.Score))],
        [.. r.Corrections.Select(c => new CorrectionDto(c.Kind, c.Field, c.From, c.To))],
        dataVersion);
}

/// <summary>A structured address to check against the gazetteer. All fields are optional.</summary>
public sealed record ValidateRequest(string? Il, string? Ilce, string? Mahalle, string? PostaKodu);

/// <summary>Consistency of a structured address with the gazetteer.</summary>
/// <param name="Valid">True when every given field exists and all of them agree.</param>
/// <param name="Il">The il, when it can be identified.</param>
/// <param name="Ilce">The ilçe, when it can be identified.</param>
/// <param name="Birim">The unit, when it can be identified uniquely.</param>
/// <param name="Problems">Human-readable problems, empty when valid.</param>
public sealed record ValidateResponse(bool Valid, ProvinceDto? Il, DistrictDto? Ilce, UnitDto? Birim, IReadOnlyList<string> Problems);

/// <summary>A name suggestion for autocomplete.</summary>
public sealed record SuggestionDto(string Level, int Id, string Name, string? Context);
