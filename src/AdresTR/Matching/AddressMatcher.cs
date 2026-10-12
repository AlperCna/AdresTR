using AdresTR.Text;

namespace AdresTR;

/// <summary>How close two addresses are, from no match to the same flat.</summary>
public enum MatchLevel
{
    /// <summary>The addresses point to different places.</summary>
    Different,

    /// <summary>Not enough information to decide (e.g. neither neighbourhood could be resolved).</summary>
    Unknown,

    /// <summary>Same ilçe, but the neighbourhood differs or is unknown.</summary>
    SameDistrict,

    /// <summary>Same mahalle/köy.</summary>
    SameUnit,

    /// <summary>Same street in the same mahalle.</summary>
    SameStreet,

    /// <summary>Same door number on the same street.</summary>
    SameBuilding,

    /// <summary>Same flat (iç kapı) in the same building.</summary>
    SameFlat,
}

/// <summary>The result of comparing two addresses.</summary>
/// <param name="Level">How close the addresses are.</param>
/// <param name="Score">Similarity in [0, 1], discounted by the parses' confidence.</param>
/// <param name="Reasons">Human-readable evidence, e.g. "same mahalle: Caferağa", "door differs: 12 ≠ 14".</param>
public sealed record AddressMatch(MatchLevel Level, double Score, IReadOnlyList<string> Reasons);

/// <summary>
/// Decides whether two Turkish addresses refer to the same place, and groups duplicate addresses.
/// Rule-based on top of <see cref="AddressParser"/>: blocking by the resolved mahalle, then street, door and flat.
/// </summary>
public sealed class AddressMatcher
{
    private readonly AddressParser _parser;

    /// <summary>Creates a matcher that parses with <paramref name="parser"/>.</summary>
    public AddressMatcher(AddressParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        _parser = parser;
    }

    /// <summary>Compares two free-text addresses.</summary>
    public AddressMatch Compare(string a, string b) => Compare(_parser.Parse(a), _parser.Parse(b));

    /// <summary>Compares two parsed addresses.</summary>
    public static AddressMatch Compare(ParseResult a, ParseResult b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var reasons = new List<string>();
        double confidence = Math.Min(a.Confidence, b.Confidence);

        if (a.Province is not null && b.Province is not null && a.Province != b.Province)
        {
            reasons.Add($"il differs: {a.Province.Name} ≠ {b.Province.Name}");
            return new AddressMatch(MatchLevel.Different, 0, reasons);
        }

        if (a.District is not null && b.District is not null && a.District != b.District)
        {
            reasons.Add($"ilçe differs: {a.District.Name} ≠ {b.District.Name}");
            return new AddressMatch(MatchLevel.Different, 0, reasons);
        }

        if (a.Unit is null || b.Unit is null)
        {
            if (a.District is not null && a.District == b.District)
            {
                reasons.Add($"same ilçe: {a.District.Name}; mahalle not resolved");
                return new AddressMatch(MatchLevel.SameDistrict, Math.Round(0.3 * confidence, 4), reasons);
            }

            reasons.Add("mahalle could not be resolved for both addresses");
            return new AddressMatch(MatchLevel.Unknown, 0, reasons);
        }

        if (a.Unit != b.Unit)
        {
            reasons.Add($"mahalle differs: {a.Unit.Name} ≠ {b.Unit.Name}");
            return new AddressMatch(a.District == b.District ? MatchLevel.SameDistrict : MatchLevel.Different, 0, reasons);
        }

        reasons.Add($"same mahalle: {a.Unit.Name} ({a.Unit.District})");
        MatchLevel level = MatchLevel.SameUnit;

        string? streetA = NameKey(a.Street?.Value);
        string? streetB = NameKey(b.Street?.Value);
        if (streetA is null || streetB is null)
        {
            reasons.Add("street missing in one address");
            return Result(level);
        }

        if (streetA != streetB || (a.StreetType is { } ta && b.StreetType is { } tb && ta != tb))
        {
            reasons.Add($"street differs: {a.Street!.Value} ≠ {b.Street!.Value}");
            return Result(level);
        }

        reasons.Add($"same street: {a.Street!.Value}");
        level = MatchLevel.SameStreet;

        string? doorA = Compact(a.DoorNumber?.Value);
        string? doorB = Compact(b.DoorNumber?.Value);
        if (doorA is null || doorB is null || doorA != doorB)
        {
            reasons.Add(doorA is null || doorB is null ? "door number missing in one address" : $"door differs: {a.DoorNumber!.Value} ≠ {b.DoorNumber!.Value}");
            return Result(level);
        }

        reasons.Add($"same door: {a.DoorNumber!.Value}");
        level = MatchLevel.SameBuilding;

        string? flatA = Compact(a.Flat?.Value);
        string? flatB = Compact(b.Flat?.Value);
        bool blokConflict = Compact(a.Blok?.Value) is { } ba && Compact(b.Blok?.Value) is { } bb && ba != bb;
        if (flatA is not null && flatA == flatB && !blokConflict)
        {
            reasons.Add($"same flat: {a.Flat!.Value}");
            level = MatchLevel.SameFlat;
        }
        else if (flatA is not null && flatB is not null)
        {
            reasons.Add(blokConflict ? "block differs" : $"flat differs: {a.Flat!.Value} ≠ {b.Flat!.Value}");
        }

        return Result(level);

        AddressMatch Result(MatchLevel l)
        {
            double baseScore = l switch
            {
                MatchLevel.SameFlat => 1.0,
                MatchLevel.SameBuilding => 0.9,
                MatchLevel.SameStreet => 0.7,
                MatchLevel.SameUnit => 0.5,
                _ => 0,
            };
            return new AddressMatch(l, Math.Round(baseScore * confidence, 4), reasons);
        }
    }

    /// <summary>
    /// Groups addresses that match at <paramref name="minimum"/> level or closer (default: same building).
    /// Returns groups of input indexes; every input appears in exactly one group.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<int>> Group(IReadOnlyList<string> addresses, MatchLevel minimum = MatchLevel.SameBuilding)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        ParseResult[] parsed = [.. addresses.Select(_parser.Parse)];
        int[] parent = [.. Enumerable.Range(0, parsed.Length)];

        // Blocking: only addresses resolved to the same unit can match at unit level or closer.
        foreach (IGrouping<int, int> block in Enumerable.Range(0, parsed.Length)
                     .Where(i => parsed[i].Unit is not null)
                     .GroupBy(i => parsed[i].Unit!.Id))
        {
            int[] members = [.. block];
            for (int x = 0; x < members.Length; x++)
            {
                for (int y = x + 1; y < members.Length; y++)
                {
                    if (Compare(parsed[members[x]], parsed[members[y]]).Level >= minimum)
                    {
                        Union(members[x], members[y]);
                    }
                }
            }
        }

        return [.. Enumerable.Range(0, parsed.Length).GroupBy(Find).Select(g => (IReadOnlyList<int>)[.. g])];

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        void Union(int i, int j) => parent[Find(i)] = Find(j);
    }

    private static string? NameKey(string? value) => string.IsNullOrWhiteSpace(value) ? null : Gazetteer.Key(value);

    private static string? Compact(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : TurkishText.Fold(value).Replace(" ", string.Empty, StringComparison.Ordinal);
}
