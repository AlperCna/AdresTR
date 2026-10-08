namespace AdresTR.Parsing;

/// <summary>Candidate lists for fuzzy matching, scoped by hierarchy (ADR-0003).</summary>
internal sealed class ParserIndex
{
    public ParserIndex(Gazetteer gazetteer)
    {
        Provinces = [.. gazetteer.Provinces.Select(p => (AdresTR.Gazetteer.Key(p.Name), p))];
        Districts = [.. gazetteer.Districts.Select(d => (AdresTR.Gazetteer.Key(d.Name), d))];
        UnitsByDistrict = gazetteer.Districts.ToDictionary(
            d => d.Id,
            d => d.Units.Select(u => (AdresTR.Gazetteer.Key(u.Name), u)).ToArray());
        UnitsByProvince = gazetteer.Provinces.ToDictionary(
            p => p.Plaka,
            p => p.Districts.SelectMany(d => UnitsByDistrict[d.Id]).ToArray());
    }

    public (string Key, Province Entity)[] Provinces { get; }

    public (string Key, District Entity)[] Districts { get; }

    public Dictionary<int, (string Key, SettlementUnit Entity)[]> UnitsByDistrict { get; }

    public Dictionary<int, (string Key, SettlementUnit Entity)[]> UnitsByProvince { get; }
}

/// <summary>Fuzzy name matching with optimal string alignment (restricted Damerau-Levenshtein) distance.</summary>
internal static class Fuzzy
{
    /// <summary>
    /// Maximum allowed edits for a folded, space-free key of the given length. Short names are never fuzzy-matched
    /// ("Of", "Çay", "Han" would match ordinary words).
    /// </summary>
    public static int MaxDistance(int length, bool scoped) => length switch
    {
        <= 3 => 0,
        <= 5 => 1,
        <= 10 => scoped ? 2 : 1,
        _ => 2,
    };

    /// <summary>Returns the entries within the allowed distance, best first.</summary>
    public static List<(T Entity, int Distance)> Search<T>(string key, IReadOnlyList<(string Key, T Entity)> candidates, bool scoped)
    {
        var result = new List<(T, int)>();
        int max = MaxDistance(key.Length, scoped);
        if (max == 0)
        {
            return result;
        }

        foreach ((string candidate, T entity) in candidates)
        {
            if (Math.Abs(candidate.Length - key.Length) > max || candidate.Length <= 3)
            {
                continue;
            }

            // The first letter is almost never mistyped in Turkish place names.
            if (candidate[0] != key[0] && (candidate.Length < 2 || key.Length < 2 || candidate[1] != key[1]))
            {
                continue;
            }

            int d = Distance(key, candidate, max);
            if (d > 0 && d <= max)
            {
                result.Add((entity, d));
            }
        }

        result.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return result;
    }

    /// <summary>OSA distance with early exit; returns <paramref name="max"/> + 1 when the distance exceeds it.</summary>
    public static int Distance(string a, string b, int max)
    {
        int n = a.Length;
        int m = b.Length;
        if (Math.Abs(n - m) > max)
        {
            return max + 1;
        }

        Span<int> prev2 = stackalloc int[m + 1];
        Span<int> prev = stackalloc int[m + 1];
        Span<int> current = stackalloc int[m + 1];
        for (int j = 0; j <= m; j++)
        {
            prev[j] = j;
        }

        for (int i = 1; i <= n; i++)
        {
            current[0] = i;
            int rowMin = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int value = Math.Min(Math.Min(prev[j] + 1, current[j - 1] + 1), prev[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    value = Math.Min(value, prev2[j - 2] + 1);
                }

                current[j] = value;
                rowMin = Math.Min(rowMin, value);
            }

            if (rowMin > max)
            {
                return max + 1;
            }

            Span<int> t = prev2;
            prev2 = prev;
            prev = current;
            current = t;
        }

        return prev[m];
    }
}
