using System.Globalization;
using System.Text;

namespace AdresTR;

/// <summary>Type of a street-level component (CSBM: cadde, sokak, bulvar, meydan …).</summary>
public enum StreetType
{
    /// <summary>Cadde.</summary>
    Cadde,

    /// <summary>Sokak.</summary>
    Sokak,

    /// <summary>Bulvar.</summary>
    Bulvar,

    /// <summary>Yol.</summary>
    Yol,

    /// <summary>Meydan.</summary>
    Meydan,

    /// <summary>Çıkmaz.</summary>
    Cikmaz,

    /// <summary>Küme evler.</summary>
    KumeEvler,
}

/// <summary>A component found in the input text.</summary>
/// <param name="Value">Normalized value (official name for il/ilçe/mahalle, cleaned text otherwise).</param>
/// <param name="Text">The text as written in the input.</param>
/// <param name="Start">Start offset in the input.</param>
/// <param name="End">End offset (exclusive) in the input.</param>
public sealed record AddressComponent(string Value, string Text, int Start, int End);

/// <summary>What kind of change the parser made or what it inferred.</summary>
public enum CorrectionKind
{
    /// <summary>Turkish letters restored ("kadikoy" → "Kadıköy").</summary>
    Diacritics,

    /// <summary>Spelling mistake corrected with fuzzy matching.</summary>
    Typo,

    /// <summary>A semt name was resolved to its official mahalle.</summary>
    Semt,

    /// <summary>A former name (e.g. a village before Law 6360) was resolved to the current unit.</summary>
    HistoricName,

    /// <summary>A common short form or alternative spelling ("Urfa" → "Şanlıurfa").</summary>
    Alias,

    /// <summary>A glued word was split ("147sok", "CaferağaMah.").</summary>
    Split,

    /// <summary>A level that is not written was inferred from the others (e.g. il from a unique ilçe).</summary>
    Inferred,

    /// <summary>The postal code contradicts the province written in the text.</summary>
    PostalCodeConflict,

    /// <summary>A repeated component was ignored ("Konak Konak İzmir").</summary>
    Duplicate,
}

/// <summary>A change the parser made, with the affected component.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Field">Affected component ("il", "ilce", "mahalle" …).</param>
/// <param name="From">Text as written, when applicable.</param>
/// <param name="To">Value used instead.</param>
public sealed record Correction(CorrectionKind Kind, string Field, string? From, string To);

/// <summary>The result of parsing one address.</summary>
public sealed class ParseResult
{
    internal ParseResult(string input) => Input = input;

    /// <summary>The input text.</summary>
    public string Input { get; }

    /// <summary>İl as written (value = official name).</summary>
    public AddressComponent? Il { get; internal set; }

    /// <summary>İlçe as written (value = official name).</summary>
    public AddressComponent? Ilce { get; internal set; }

    /// <summary>Mahalle, köy or other settlement unit as written (value = official name when resolved).</summary>
    public AddressComponent? Mahalle { get; internal set; }

    /// <summary>Semt (colloquial area name), when the text uses one instead of the official mahalle.</summary>
    public AddressComponent? Semt { get; internal set; }

    /// <summary>Street type (cadde, sokak …).</summary>
    public StreetType? StreetType { get; internal set; }

    /// <summary>Street name without the type word ("Moda", "1203/5").</summary>
    public AddressComponent? Street { get; internal set; }

    /// <summary>Site or building name without the type word.</summary>
    public AddressComponent? Site { get; internal set; }

    /// <summary>Block identifier ("A", "B2").</summary>
    public AddressComponent? Blok { get; internal set; }

    /// <summary>Door number (dış kapı no), e.g. "12" or "17/A".</summary>
    public AddressComponent? DoorNumber { get; internal set; }

    /// <summary>Floor ("0" = ground floor, "-1" = basement).</summary>
    public AddressComponent? Floor { get; internal set; }

    /// <summary>Flat number (iç kapı no / daire).</summary>
    public AddressComponent? Flat { get; internal set; }

    /// <summary>Five-digit postal code as written.</summary>
    public AddressComponent? PostalCode { get; internal set; }

    /// <summary>Landmark description ("PTT karşısı").</summary>
    public AddressComponent? Landmark { get; internal set; }

    /// <summary>The province the text determines, or <see langword="null"/> when it is ambiguous or absent.</summary>
    public Province? Province { get; internal set; }

    /// <summary>The district the text determines, or <see langword="null"/>.</summary>
    public District? District { get; internal set; }

    /// <summary>The settlement unit the text determines, or <see langword="null"/>.</summary>
    public SettlementUnit? Unit { get; internal set; }

    /// <summary>Candidate units ranked by plausibility (the first one equals <see cref="Unit"/> when it is determined).</summary>
    public IReadOnlyList<(SettlementUnit Unit, double Score)> UnitCandidates { get; internal set; } = [];

    /// <summary>Overall confidence in [0, 1] that the parse is correct.</summary>
    public double Confidence { get; internal set; }

    /// <summary>Changes and inferences the parser made.</summary>
    public IReadOnlyList<Correction> Corrections { get; internal set; } = [];

    /// <summary>Formats the address in the official order: "Caferağa Mah. Moda Cad. No:12 D:3 34710 Kadıköy/İstanbul".</summary>
    public string ToCanonicalString()
    {
        var sb = new StringBuilder();
        void Add(string? part)
        {
            if (!string.IsNullOrEmpty(part))
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(part);
            }
        }

        string? mahalle = Unit?.Name ?? Mahalle?.Value;
        string? mahalleWord = Unit?.Kind switch
        {
            UnitKind.Koy => "Köyü",
            UnitKind.Osb or UnitKind.Site => null,
            _ => mahalle is null ? null : "Mah.",
        };
        Add(mahalle is null ? Semt?.Value : $"{mahalle}{(mahalleWord is null ? "" : " " + mahalleWord)}");

        if (Street is not null)
        {
            Add($"{Display(Street.Value)} {StreetAbbreviation(StreetType)}".TrimEnd());
        }

        if (Site is not null)
        {
            Add($"{Display(Site.Value)} Sitesi");
        }

        if (Blok is not null)
        {
            Add($"{Blok.Value} Blok");
        }

        if (DoorNumber is not null)
        {
            Add($"No:{DoorNumber.Value}");
        }

        if (Floor is not null)
        {
            Add($"K:{Floor.Value}");
        }

        if (Flat is not null)
        {
            Add($"D:{Flat.Value}");
        }

        Add(PostalCode?.Value ?? Unit?.PostalCode);

        string? ilce = District?.Name ?? Ilce?.Value;
        string? il = Province?.Name ?? Il?.Value;
        Add(ilce is not null && il is not null ? $"{ilce}/{il}" : ilce ?? il);
        return sb.ToString();
    }

    /// <inheritdoc />
    public override string ToString() => ToCanonicalString();

    /// <summary>Names written all in lower or upper case are shown in Turkish title case ("moda" → "Moda").</summary>
    private static string Display(string written) =>
        written.Any(char.IsLetter) && (written.Where(char.IsLetter).All(char.IsLower) || written.Where(char.IsLetter).All(char.IsUpper))
            ? Text.TurkishText.ToTitleTr(written)
            : written;

    private static string StreetAbbreviation(StreetType? type) => type switch
    {
        AdresTR.StreetType.Cadde => "Cad.",
        AdresTR.StreetType.Sokak => "Sok.",
        AdresTR.StreetType.Bulvar => "Blv.",
        AdresTR.StreetType.Yol => "Yolu",
        AdresTR.StreetType.Meydan => "Meydanı",
        AdresTR.StreetType.Cikmaz => "Çıkmazı",
        AdresTR.StreetType.KumeEvler => "Küme Evleri",
        _ => string.Empty,
    };

    internal static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
