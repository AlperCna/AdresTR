namespace AdresTR.Parsing;

/// <summary>A (partial) assignment of hypotheses to the token sequence, as an immutable linked list.</summary>
internal sealed class Path
{
    public static readonly Path Empty = new(null, null, 0, 0, 0, 0, "|");

    private Path(Path? previous, Hyp? hyp, int position, uint used, double score, int lastRank, string adminKeys)
    {
        Previous = previous;
        Hyp = hyp;
        Position = position;
        Used = used;
        Score = score;
        LastRank = lastRank;
        AdminKeys = adminKeys;
    }

    /// <summary>Keys and spans of the admin hypotheses chosen so far ("|il:istanbul@5-6|…"), for recombination and duplicates.</summary>
    public string AdminKeys { get; }

    public Path? Previous { get; }

    /// <summary>The hypothesis chosen at this step, or null for a skipped token.</summary>
    public Hyp? Hyp { get; }

    public int Position { get; }

    public uint Used { get; }

    public double Score { get; }

    public int LastRank { get; }

    public Path Extend(Hyp? hyp, int position, uint used, double delta, int lastRank) =>
        new(this, hyp, position, used, Score + delta, lastRank,
            hyp is { Role: Role.Il or Role.Ilce or Role.Mahalle or Role.Semt } h ? $"{AdminKeys}{h.Role}:{h.Key}:{h.Alias?.Name}@{h.From}|" : AdminKeys);

    /// <summary>An admin hypothesis with this key was already chosen.</summary>
    public bool HasAdminKey(string key) => AdminKeys.Contains($":{key}:", StringComparison.Ordinal);

    public IEnumerable<Hyp> Hyps()
    {
        var list = new List<Hyp>();
        for (Path? p = this; p is not null; p = p.Previous)
        {
            if (p.Hyp is not null)
            {
                list.Add(p.Hyp);
            }
        }

        list.Reverse();
        return list;
    }

    /// <summary>Signature for recombination: same position, roles and admin choices → keep the best only.</summary>
    public string Signature() => $"{Position}|{Used}|{LastRank}{AdminKeys}";
}

/// <summary>Left-to-right beam search over the hypothesis lattice (each role used at most once).</summary>
internal static class Solver
{
    private const int BeamWidth = 48;
    private const int Results = 16;

    // Canonical order of the local block (big → small). Admin roles, postal code, landmark and noise are free.
    private static int Rank(Role role) => role switch
    {
        Role.Semt => 1,
        Role.Mahalle => 2,
        Role.Street => 4,
        Role.Site => 6,
        Role.Blok => 7,
        Role.Door => 8,
        Role.Floor => 9,
        Role.Flat => 10,
        _ => 0,
    };

    private static uint Bit(Role role) => role is Role.Landmark or Role.Noise or Role.Ignore ? 0u : 1u << (int)role;

    public static List<Path> Solve(IReadOnlyList<Token> tokens, List<Hyp>[] hypsByStart)
    {
        int n = tokens.Count;
        double totalChars = Math.Max(1, tokens.Where(t => !t.Noise).Sum(t => t.Fold.Length));
        var beams = new List<Path>[n + 1];
        for (int i = 0; i <= n; i++)
        {
            beams[i] = [];
        }

        beams[0].Add(Path.Empty);

        for (int i = 0; i < n; i++)
        {
            List<Path> beam = Prune(beams[i]);
            foreach (Path path in beam)
            {
                // Skip token i.
                beams[i + 1].Add(path.Extend(null, i + 1, path.Used, -SkipPenalty(tokens[i]), path.LastRank));

                foreach (Hyp h in hypsByStart[i])
                {
                    // A repeated admin name ("Konak Konak İzmir", "Pınarbaşı Pınarbaşı") can be ignored at no cost.
                    if (h.Role is Role.Il or Role.Ilce or Role.Mahalle or Role.Semt && path.HasAdminKey(h.Key))
                    {
                        beams[h.To].Add(path.Extend(Duplicate(h), h.To, path.Used, 0, path.LastRank));
                    }

                    uint bit = Bit(h.Role);
                    if ((path.Used & bit) != 0 || (h.FlatValue is not null && (path.Used & Bit(Role.Flat)) != 0))
                    {
                        continue;
                    }

                    if (h.Role == Role.Flat && (path.Used & Bit(Role.Flat)) != 0)
                    {
                        continue;
                    }

                    uint used = path.Used | bit | (h.FlatValue is not null ? Bit(Role.Flat) : 0u);
                    int rank = Rank(h.Role);
                    double delta = HypScore(h, tokens, totalChars);
                    int lastRank = path.LastRank;
                    if (rank > 0)
                    {
                        if (rank < lastRank)
                        {
                            delta -= 0.4;
                        }

                        lastRank = Math.Max(lastRank, rank);
                    }

                    beams[h.To].Add(path.Extend(h, h.To, used, delta, lastRank));
                }
            }
        }

        return [.. Prune(beams[n]).Take(Results)];
    }

    private static Hyp Duplicate(Hyp h) => new()
    {
        From = h.From, To = h.To, Role = Role.Ignore, Local = 1, Value = h.Value, Key = h.Key, TextStart = h.TextStart, TextEnd = h.TextEnd,
    };

    private static List<Path> Prune(List<Path> paths)
    {
        var best = new Dictionary<string, Path>(StringComparer.Ordinal);
        foreach (Path p in paths)
        {
            string sig = p.Signature();
            if (!best.TryGetValue(sig, out Path? existing) || existing.Score < p.Score)
            {
                best[sig] = p;
            }
        }

        return [.. best.Values.OrderByDescending(p => p.Score).Take(BeamWidth)];
    }

    private static double SkipPenalty(Token t)
    {
        if (t.Noise || t.ApostropheSuffix)
        {
            return 0;
        }

        // A stray type word ("Mah." without a name) costs less than an unexplained word.
        if (Lexicon.Of(t.Fold) is not Keyword.None)
        {
            return 0.3;
        }

        return t.Kind == TokenKind.Alpha ? 0.6 : 0.8;
    }

    private static double HypScore(Hyp h, IReadOnlyList<Token> tokens, double totalChars)
    {
        if (h.Role is Role.Noise or Role.Ignore)
        {
            return 0;
        }

        double chars = 0;
        bool crossesSection = false;
        for (int k = h.From; k < h.To; k++)
        {
            chars += tokens[k].Fold.Length;
            crossesSection |= k > h.From && tokens[k].Break == BreakKind.Section;
        }

        double score = (4.0 * h.Local * chars / totalChars) + (0.25 * h.Local);
        if (h.KeywordSupported)
        {
            score += 0.3;
        }

        if (crossesSection)
        {
            score -= 0.8;
        }

        return score;
    }
}
