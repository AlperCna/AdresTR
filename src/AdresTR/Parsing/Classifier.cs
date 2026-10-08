using System.Text;

namespace AdresTR.Parsing;

/// <summary>Roles a span of tokens can play.</summary>
internal enum Role : byte
{
    Il,
    Ilce,
    Mahalle,
    Semt,
    Street,
    Site,
    Blok,
    Door,
    Floor,
    Flat,
    Postal,
    Landmark,
    Noise,
    Ignore,
}

/// <summary>A hypothesis: tokens [From, To) play <see cref="Role"/>.</summary>
internal sealed class Hyp
{
    public required int From { get; init; }

    public required int To { get; init; }

    public required Role Role { get; init; }

    /// <summary>Local plausibility in [0, 1].</summary>
    public required double Local { get; init; }

    /// <summary>Normalized value (cleaned text for streets, numbers for doors …).</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Original-text span of the value (without type words).</summary>
    public int TextStart { get; init; }

    public int TextEnd { get; init; }

    /// <summary>Gazetteer key of the value, for il/ilçe/mahalle/semt.</summary>
    public string Key { get; init; } = string.Empty;

    public Province[] Provinces { get; init; } = [];

    public District[] Districts { get; init; } = [];

    public SettlementUnit[] Units { get; init; } = [];

    /// <summary>Alias used to find the entities, if any (same for all entities of the hypothesis).</summary>
    public GazetteerAlias? Alias { get; init; }

    /// <summary>The name was followed by its type word ("Mah.", "Cad.", "ilçesi").</summary>
    public bool KeywordSupported { get; init; }

    public StreetType? StreetType { get; init; }

    /// <summary>For a door hypothesis like "No:17/5": the flat part ("5").</summary>
    public string? FlatValue { get; init; }

    public int FlatStart { get; init; }

    public int FlatEnd { get; init; }

    /// <summary>Edit distance when the name was matched fuzzily (0 = exact).</summary>
    public int Fuzzy { get; init; }

    /// <summary>Unit kind preferred by the keyword ("Köyü" → köy).</summary>
    public UnitKind? PreferredKind { get; init; }

    public override string ToString() => $"{Role}[{From},{To}) '{Value}' {Local:F2}";
}

/// <summary>Generates hypotheses for every span of the token sequence.</summary>
internal sealed class Classifier(Gazetteer gazetteer, string text, IReadOnlyList<Token> tokens)
{
    private const int MaxNameTokens = 7;

    private readonly List<Hyp>[] _byStart = Enumerable.Range(0, tokens.Count).Select(_ => new List<Hyp>()).ToArray();

    public List<Hyp>[] Run()
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            Noise(i);
            Gazetteer(i);
            UnknownNamedUnit(i);
            Street(i);
            Site(i);
            Blok(i);
            Door(i);
            Floor(i);
            Flat(i);
            Postal(i);
            Landmark(i);
        }

        return _byStart;
    }

    private void Add(Hyp h) => _byStart[h.From].Add(h);

    private Keyword KeywordAt(int i) => i < tokens.Count ? Lexicon.At(tokens, i).Keyword : Keyword.None;

    private int KeywordLength(int i) => i < tokens.Count ? Lexicon.At(tokens, i).Length : 0;

    private string Slice(int fromToken, int toToken) => text[tokens[fromToken].Start..tokens[toToken - 1].End];

    private static string CleanSpaces(string s) => string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The span [i, j) does not cross a comma/semicolon and contains no noise.</summary>
    private bool Contiguous(int i, int j)
    {
        for (int k = i; k < j; k++)
        {
            if (tokens[k].Noise || (k > i && tokens[k].Break == BreakKind.Section))
            {
                return false;
            }
        }

        return true;
    }

    private void Noise(int i)
    {
        Token t = tokens[i];
        Keyword kw = KeywordAt(i);

        if (t.Noise && (i == 0 || !tokens[i - 1].Noise))
        {
            int j = i;
            while (j < tokens.Count && tokens[j].Noise)
            {
                j++;
            }

            Add(new Hyp { From = i, To = j, Role = Role.Noise, Local = 1 });
        }

        if (kw == Keyword.Phone)
        {
            int j = i + 1;
            while (j < tokens.Count && tokens[j].Noise)
            {
                j++;
            }

            Add(new Hyp { From = i, To = j, Role = Role.Noise, Local = 1 });
        }

        if (kw == Keyword.Ignore || t.ApostropheSuffix)
        {
            Add(new Hyp { From = i, To = i + 1, Role = Role.Ignore, Local = 1 });
        }
    }

    private void Gazetteer(int i)
    {
        var key = new StringBuilder();
        for (int j = i + 1; j <= Math.Min(tokens.Count, i + MaxNameTokens); j++)
        {
            if (!Contiguous(i, j))
            {
                break;
            }

            key.Append(tokens[j - 1].Fold);
            string k = key.ToString();
            if (k.All(char.IsAsciiDigit))
            {
                continue;
            }

            (Keyword after, int afterLength) = (KeywordAt(j), KeywordLength(j));

            // A name that ends with a type word ("Yeni Mah" → unit "Yenimah") or is followed by a street/site word
            // ("Mustafa Kemalpaşa Cad." is a street, not the ilçe) is an unlikely admin reading.
            bool endsWithKeyword = j - i > 1 && Lexicon.Of(tokens[j - 1].Fold) is not (Keyword.None or Keyword.Osb);
            bool streetFollows = Lexicon.StreetTypeOf(after) is not null || after is Keyword.Site or Keyword.Bina;
            double factor = (endsWithKeyword ? 0.5 : 1) * (streetFollows ? 0.35 : 1);

            Provinces(i, j, k, after, afterLength, factor);
            Districts(i, j, k, after, afterLength, factor);
            Units(i, j, k, after, afterLength, factor);

            if (j - i <= 2)
            {
                FuzzyAdmin(i, j, k, after, afterLength);
            }

            // "Gürsu Organize Sanayi Bölgesi" → unit "Gürsu OSB".
            if (after == Keyword.Osb)
            {
                Units(i, j, k + "osb", Keyword.None, 0, 1, extendTo: j + afterLength);
            }
        }
    }

    /// <summary>Typo-tolerant il/ilçe candidates ("Sniop" → Sinop) for names with no exact match.</summary>
    private void FuzzyAdmin(int i, int j, string key, Keyword after, int afterLength)
    {
        if (key.Length < 4 || Enumerable.Range(i, j - i).Any(x => tokens[x].Kind != TokenKind.Alpha || Lexicon.Of(tokens[x].Fold) != Keyword.None) ||
            gazetteer.FindProvincesByKey(key).Count > 0 || gazetteer.FindDistrictsByKey(key).Count > 0 || gazetteer.FindUnitsByKey(key).Count > 0)
        {
            return;
        }

        ParserIndex index = gazetteer.ParserIndex;
        foreach ((Province p, int d) in Fuzzy.Search(key, index.Provinces, scoped: false).Take(2))
        {
            Add(new Hyp
            {
                From = i, To = after == Keyword.Il ? j + afterLength : j, Role = Role.Il, Local = 0.75 - (0.1 * d), Value = p.Name,
                Key = AdresTR.Gazetteer.Key(p.Name), TextStart = tokens[i].Start, TextEnd = tokens[j - 1].End, Provinces = [p], Fuzzy = d,
                KeywordSupported = after == Keyword.Il,
            });
        }

        foreach (IGrouping<int, (District Entity, int Distance)> group in Fuzzy.Search(key, index.Districts, scoped: false).GroupBy(x => x.Distance).Take(1))
        {
            foreach (IGrouping<string, (District Entity, int Distance)> byName in group.GroupBy(x => AdresTR.Gazetteer.Key(x.Entity.Name)).Take(3))
            {
                Add(new Hyp
                {
                    From = i, To = after == Keyword.Ilce ? j + afterLength : j, Role = Role.Ilce, Local = 0.65 - (0.1 * group.Key),
                    Value = byName.First().Entity.Name, Key = byName.Key, TextStart = tokens[i].Start, TextEnd = tokens[j - 1].End,
                    Districts = [.. byName.Select(x => x.Entity)], Fuzzy = group.Key, KeywordSupported = after == Keyword.Ilce,
                });
            }
        }
    }

    private void Provinces(int i, int j, string key, Keyword after, int afterLength, double factor)
    {
        IReadOnlyList<GazetteerMatch<Province>> matches = gazetteer.FindProvincesByKey(key);
        if (matches.Count == 0)
        {
            return;
        }

        bool cue = after == Keyword.Il;
        foreach (GazetteerMatch<Province> m in matches)
        {
            Add(new Hyp
            {
                From = i, To = cue ? j + afterLength : j, Role = Role.Il, Local = factor * (m.Alias is null ? 0.9 : 0.85),
                Value = m.Entity.Name, Key = key, TextStart = tokens[i].Start, TextEnd = tokens[j - 1].End,
                Provinces = [m.Entity], Alias = m.Alias, KeywordSupported = cue,
            });
        }
    }

    private void Districts(int i, int j, string key, Keyword after, int afterLength, double factor)
    {
        IReadOnlyList<GazetteerMatch<District>> matches = gazetteer.FindDistrictsByKey(key);
        if (matches.Count == 0)
        {
            return;
        }

        bool cue = after == Keyword.Ilce;
        foreach (IGrouping<GazetteerAlias?, GazetteerMatch<District>> group in matches.GroupBy(m => m.Alias))
        {
            Add(new Hyp
            {
                From = i, To = cue ? j + afterLength : j, Role = Role.Ilce,
                Local = factor * (key.Length <= 3 ? 0.45 : group.Key is null ? 0.85 : 0.8),
                Value = group.First().Entity.Name, Key = key, TextStart = tokens[i].Start, TextEnd = tokens[j - 1].End,
                Districts = [.. group.Select(m => m.Entity)], Alias = group.Key, KeywordSupported = cue,
            });
        }
    }

    private void Units(int i, int j, string key, Keyword after, int afterLength, double factor, int extendTo = -1)
    {
        IReadOnlyList<GazetteerMatch<SettlementUnit>> matches = gazetteer.FindUnitsByKey(key);
        if (matches.Count == 0)
        {
            return;
        }

        bool unitKeyword = after is Keyword.Mahalle or Keyword.Koy or Keyword.Belde or Keyword.Mevkii or Keyword.KumeEvler or Keyword.Osb;
        int end = extendTo > 0 ? extendTo : unitKeyword ? j + afterLength : j;
        UnitKind? preferred = after switch
        {
            Keyword.Koy => UnitKind.Koy,
            Keyword.Mahalle => UnitKind.Mahalle,
            Keyword.Mevkii => UnitKind.Mevki,
            Keyword.KumeEvler => UnitKind.KumeEvler,
            Keyword.Osb => UnitKind.Osb,
            _ => null,
        };

        foreach (IGrouping<(AliasKind?, string?), GazetteerMatch<SettlementUnit>> group in matches.GroupBy(m => (m.Alias?.Kind, m.Alias?.Name)))
        {
            AliasKind? aliasKind = group.Key.Item1;
            bool osbName = group.All(m => m.Entity.Kind == UnitKind.Osb);
            bool supported = unitKeyword || extendTo > 0 || osbName;
            double local = (aliasKind, supported) switch
            {
                (null, true) => 0.95,
                (null, false) => 0.6,
                (AliasKind.Semt, _) => 0.7,
                (_, true) => 0.85,
                _ => 0.6,
            };

            // "Ulus Mh." names a mahalle, not the semt "Ulus"; "Kazanlı OSB" is the OSB, not the mahalle "Kazanlı".
            if (aliasKind == AliasKind.Semt && unitKeyword)
            {
                local = 0.3;
            }

            if (after == Keyword.Osb && extendTo < 0 && !osbName)
            {
                local = 0.5;
            }

            Add(new Hyp
            {
                From = i, To = aliasKind == AliasKind.Semt ? j : end, Role = aliasKind == AliasKind.Semt ? Role.Semt : Role.Mahalle,
                Local = factor * local,
                Value = aliasKind == AliasKind.Semt ? CleanSpaces(Slice(i, j)) : group.First().Entity.Name,
                Key = key, TextStart = tokens[i].Start, TextEnd = tokens[j - 1].End,
                Units = [.. group.Select(m => m.Entity)], Alias = group.First().Alias,
                KeywordSupported = supported, PreferredKind = preferred,
            });
        }
    }

    /// <summary>"Bilinmeyen Mah." — a name followed by a unit keyword that is not in the gazetteer (typo or new unit).</summary>
    private void UnknownNamedUnit(int i)
    {
        for (int k = i + 1; k <= Math.Min(tokens.Count - 1, i + 4); k++)
        {
            Keyword kw = KeywordAt(k);
            if (kw is not (Keyword.Mahalle or Keyword.Koy or Keyword.Mevkii))
            {
                if (kw != Keyword.None || !Contiguous(i, k + 1))
                {
                    break;
                }

                continue;
            }

            if (!Contiguous(i, k) || tokens[k].Break == BreakKind.Section || Enumerable.Range(i, k - i).Any(x => KeywordAt(x) is not Keyword.None))
            {
                break;
            }

            string key = string.Concat(Enumerable.Range(i, k - i).Select(x => tokens[x].Fold));
            if (gazetteer.FindUnitsByKey(key).Count == 0 && key.Length >= 3)
            {
                Add(new Hyp
                {
                    From = i, To = k + KeywordLength(k), Role = Role.Mahalle, Local = 0.5, Value = CleanSpaces(Slice(i, k)),
                    Key = key, TextStart = tokens[i].Start, TextEnd = tokens[k - 1].End, KeywordSupported = true,
                    PreferredKind = kw == Keyword.Koy ? UnitKind.Koy : UnitKind.Mahalle,
                });
            }

            break;
        }
    }

    /// <summary>
    /// A token that can be part of a name even though it looks like a keyword: "Dr." in "Dr. Fahri Atabey Cad.",
    /// "K." in "K. Çekmece" — a short keyword that is not followed by a number.
    /// </summary>
    private bool IsNameToken(int x)
    {
        Keyword kw = KeywordAt(x);
        if (kw == Keyword.None)
        {
            return true;
        }

        bool numberFollows = x + 1 < tokens.Count && tokens[x + 1].Kind != TokenKind.Alpha;
        return kw is Keyword.Daire or Keyword.Kat or Keyword.Ignore && !numberFollows && x + 1 < tokens.Count;
    }

    private void Street(int i)
    {
        if (!IsNameToken(i) || tokens[i].Noise)
        {
            return;
        }

        int added = 0;
        for (int k = i + 1; k <= Math.Min(tokens.Count - 1, i + 6) && added < 2; k++)
        {
            Keyword kw = KeywordAt(k);
            if (!Contiguous(i, k + 1) || tokens[k].Break == BreakKind.Section)
            {
                break;
            }

            // "127 nolu sokak": the name is a number followed by "nolu".
            if (kw == Keyword.Nolu && k == i + 1 && tokens[i].IsNum && Lexicon.StreetTypeOf(KeywordAt(k + 1)) is StreetType numberedType)
            {
                AddStreet(i, i + 1, k + 1 + KeywordLength(k + 1), numberedType, 0.9);
                break;
            }

            if (Lexicon.StreetTypeOf(kw) is StreetType type)
            {
                AddStreet(i, k, k + KeywordLength(k), type, tokens[i].IsNum ? 0.9 : 0.85);
                added++;

                // "Tugay Yolu Cad.": a street word followed by another one is part of the name; offer both readings.
                if (Lexicon.StreetTypeOf(KeywordAt(k + KeywordLength(k))) is null)
                {
                    break;
                }

                continue;
            }

            // A name directly followed by "No": "İnkılap Mah. Mehmet Akif No:101" (street without type word).
            if (kw == Keyword.No && k + 1 < tokens.Count && tokens[k + 1].Kind != TokenKind.Alpha && k - i <= 4)
            {
                AddStreet(i, k, k, null, 0.4);
                break;
            }

            if (!IsNameToken(k))
            {
                break;
            }
        }

        void AddStreet(int from, int nameEnd, int to, StreetType? type, double local)
        {
            // Street words inside the name are part of it: "Tugay Yolu Cad.", "Meydan Sk.".
            if (nameEnd <= from || Enumerable.Range(from, nameEnd - from).Any(x => !(IsNameToken(x) || Lexicon.StreetTypeOf(KeywordAt(x)) is not null) || tokens[x].Noise))
            {
                return;
            }

            Add(new Hyp
            {
                From = from, To = to, Role = Role.Street, Local = local, Value = CleanSpaces(Slice(from, nameEnd)),
                TextStart = tokens[from].Start, TextEnd = tokens[nameEnd - 1].End, StreetType = type, KeywordSupported = type is not null,
            });
        }
    }

    private void Site(int i)
    {
        for (int k = i + 1; k <= Math.Min(tokens.Count - 1, i + 4); k++)
        {
            Keyword kw = KeywordAt(k);
            if (kw is Keyword.Site or Keyword.Bina)
            {
                if (Contiguous(i, k) && !Enumerable.Range(i, k - i).Any(x => KeywordAt(x) is not Keyword.None))
                {
                    Add(new Hyp
                    {
                        From = i, To = k + KeywordLength(k), Role = Role.Site, Local = 0.8, Value = CleanSpaces(Slice(i, k)),
                        TextStart = tokens[i].Start, TextEnd = tokens[k - 1].End, KeywordSupported = true,
                    });
                }

                break;
            }

            if (kw is not Keyword.None)
            {
                break;
            }
        }
    }

    private void Blok(int i)
    {
        // "A Blok", "B2 Blok", "Blok C"
        static bool IsBlockId(Token t) => t.Fold.Length <= 3 && (t.Kind != TokenKind.Alpha || t.Fold.Length == 1);

        if (KeywordAt(i) == Keyword.Blok && i + 1 < tokens.Count && IsBlockId(tokens[i + 1]))
        {
            Add(BlokHyp(i, i + 2, i + 1));
        }

        if (IsBlockId(tokens[i]) && KeywordAt(i + 1) == Keyword.Blok)
        {
            Add(BlokHyp(i, i + 2, i));
        }

        Hyp BlokHyp(int from, int to, int id) => new()
        {
            From = from, To = to, Role = Role.Blok, Local = 0.9, Value = text[tokens[id].Start..tokens[id].End].ToUpperInvariant(),
            TextStart = tokens[id].Start, TextEnd = tokens[id].End, KeywordSupported = true,
        };
    }

    private void Door(int i)
    {
        bool labeled = KeywordAt(i) == Keyword.No;
        int n = labeled ? i + 1 : i;
        if (n >= tokens.Count || tokens[n].Kind == TokenKind.Alpha || tokens[n].Noise || tokens[n].Fold.Length > 5)
        {
            return;
        }

        // Unlabeled numbers are doors only right after a street ("Moda Cad. 12/3").
        bool afterStreet = n > 0 && Lexicon.StreetTypeOf(Lexicon.Of(tokens[n - 1].Fold)) is not null;
        if (!labeled && !afterStreet)
        {
            return;
        }

        double local = labeled ? 0.92 : 0.6;
        Token first = tokens[n];
        int doorEnd = first.End;
        int next = n + 1;

        // A letter written apart: "No:58 F", "No:12 A" — unless it starts a flat/floor/block ("D:5", "A Blok").
        if (IsDoorLetter(next))
        {
            doorEnd = tokens[next].End;
            next++;
        }

        // Slash or hyphen part: "No:17/5" (door 17, flat 5), "No:3 /2C", "No:70/C/2", "No:1 -3" (range).
        if (next < tokens.Count && tokens[next].Break != BreakKind.Section && (tokens[next].SlashBefore || tokens[next].HyphenBefore) &&
            tokens[next].Kind != TokenKind.Alpha && tokens[next].Fold.Length <= 4)
        {
            Token second = tokens[next];
            if (second.SlashBefore)
            {
                int flatEnd = second.End;
                if (next + 1 < tokens.Count && tokens[next + 1].HyphenBefore && tokens[next + 1].Fold.Length == 1 && tokens[next + 1].Break == BreakKind.Part)
                {
                    flatEnd = tokens[next + 1].End; // "No:5/1-B" → flat "1-B"
                    next++;
                }

                Add(DoorHyp(i, next + 1, first.Start, doorEnd, local, second.Start, flatEnd));
                Add(DoorHyp(i, next + 1, first.Start, flatEnd, local - 0.35));
                return;
            }

            // Range "1-3", "4-6B"
            Add(DoorHyp(i, next + 1, first.Start, second.End, local));
            return;
        }

        Add(DoorHyp(i, next, first.Start, doorEnd, local));

        bool IsDoorLetter(int x)
        {
            if (x >= tokens.Count || !tokens[x].IsAlpha || tokens[x].Fold.Length != 1 || tokens[x].Break is BreakKind.Section or BreakKind.Soft)
            {
                return false;
            }

            // "17/A" is always a door letter; "58 F" only if no number or "Blok" follows ("D 5" is a flat, "A Blok" a block).
            return tokens[x].SlashBefore ||
                   !(x + 1 < tokens.Count && (tokens[x + 1].Kind != TokenKind.Alpha || KeywordAt(x + 1) == Keyword.Blok));
        }

        Hyp DoorHyp(int from, int to, int start, int end, double l, int flatStart = -1, int flatEnd = -1) => new()
        {
            From = from, To = to, Role = Role.Door, Local = l,
            Value = string.Concat(text[start..end].Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant(),
            TextStart = start, TextEnd = end, KeywordSupported = labeled,
            FlatValue = flatStart < 0 ? null : string.Concat(text[flatStart..flatEnd].Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant(),
            FlatStart = Math.Max(flatStart, 0), FlatEnd = Math.Max(flatEnd, 0),
        };
    }

    private void Floor(int i)
    {
        Keyword kw = KeywordAt(i);
        if (kw == Keyword.Kat && i + 1 < tokens.Count && FloorValue(tokens[i + 1]) is string v)
        {
            Add(new Hyp
            {
                From = i, To = i + 2, Role = Role.Floor, Local = tokens[i].Fold.Length == 1 ? 0.85 : 0.92, Value = v,
                TextStart = tokens[i + 1].Start, TextEnd = tokens[i + 1].End, KeywordSupported = true,
            });
        }

        // "3. kat", "3 kat", "zemin kat"
        if (KeywordAt(i + 1) == Keyword.Kat && tokens[i + 1].Fold.Length > 1 && FloorValue(tokens[i]) is string w)
        {
            Add(new Hyp
            {
                From = i, To = i + 2, Role = Role.Floor, Local = 0.9, Value = w,
                TextStart = tokens[i].Start, TextEnd = tokens[i].End, KeywordSupported = true,
            });
        }

        static string? FloorValue(Token t) => t.Fold switch
        {
            "zemin" or "z" => "0",
            "bodrum" or "b" => "-1",
            _ when t.IsNum && t.Fold.Length <= 2 => t.Fold.TrimStart('0') is "" ? "0" : t.Fold.TrimStart('0'),
            _ => null,
        };
    }

    private void Flat(int i)
    {
        // Glued "d3" / "D12" (flat) and "k2" (floor) after a door number.
        if (tokens[i].Kind == TokenKind.AlphaNum && tokens[i].Fold.Length is >= 2 and <= 4 && tokens[i].Fold[0] is 'd' or 'k' &&
            tokens[i].Fold.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0 && i > 0 && tokens[i - 1].Kind != TokenKind.Alpha)
        {
            Add(new Hyp
            {
                From = i, To = i + 1, Role = tokens[i].Fold[0] == 'd' ? Role.Flat : Role.Floor, Local = 0.8,
                Value = tokens[i].Fold[1..].TrimStart('0') is "" ? "0" : tokens[i].Fold[1..].TrimStart('0'),
                TextStart = tokens[i].Start + 1, TextEnd = tokens[i].End, KeywordSupported = true,
            });
        }

        if (KeywordAt(i) == Keyword.Daire && i + 1 < tokens.Count && tokens[i + 1].Kind != TokenKind.Alpha && tokens[i + 1].Fold.Length <= 5)
        {
            Token v = tokens[i + 1];
            Add(new Hyp
            {
                From = i, To = i + 2, Role = Role.Flat, Local = tokens[i].Fold.Length == 1 ? 0.85 : 0.92,
                Value = text[v.Start..v.End].ToUpperInvariant(), TextStart = v.Start, TextEnd = v.End, KeywordSupported = true,
            });
        }
    }

    private void Postal(int i)
    {
        Token t = tokens[i];
        if (t.IsNum && t.Fold.Length == 5 && int.Parse(t.Fold.AsSpan(0, 2), System.Globalization.CultureInfo.InvariantCulture) is >= 1 and <= 81)
        {
            Add(new Hyp
            {
                From = i, To = i + 1, Role = Role.Postal, Local = 0.9, Value = t.Fold, TextStart = t.Start, TextEnd = t.End,
                Provinces = gazetteer.GetProvince(int.Parse(t.Fold.AsSpan(0, 2), System.Globalization.CultureInfo.InvariantCulture)) is { } p ? [p] : [],
            });
        }
    }

    private void Landmark(int i)
    {
        for (int k = i + 1; k <= Math.Min(tokens.Count - 1, i + 4); k++)
        {
            Keyword kw = KeywordAt(k);
            if (kw == Keyword.Landmark)
            {
                if (Contiguous(i, k + 1))
                {
                    Add(new Hyp
                    {
                        From = i, To = k + 1, Role = Role.Landmark, Local = 0.75, Value = CleanSpaces(Slice(i, k + 1)),
                        TextStart = tokens[i].Start, TextEnd = tokens[k].End, KeywordSupported = true,
                    });
                }

                break;
            }

            if (kw is not (Keyword.None or Keyword.Ignore))
            {
                break;
            }
        }
    }
}
