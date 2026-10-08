using System.Globalization;
using System.Text;
using AdresTR.Text;

namespace AdresTR.Eval;

/// <summary>
/// Generates synthetic, fully span-annotated addresses from the gazetteer: real il/ilçe/mahalle/köy names,
/// invented streets, doors and flats, rendered with varied templates and abbreviations, then corrupted by
/// logged noise operations. Gold il/ilçe/birim ids are set only when the remaining text determines them.
/// </summary>
internal sealed class SyntheticGenerator
{
    // Invented, generic street and site names (CC0). Streets are not validated against any street register.
    private static readonly string[] StreetNames =
    [
        "Atatürk", "Cumhuriyet", "İstiklal", "Gazi", "Fatih Sultan Mehmet", "Mimar Sinan", "Yunus Emre", "Barış",
        "Çiçek", "Lale", "Gül", "Menekşe", "Papatya", "Akasya", "Ihlamur", "Çınar", "Zafer", "Hürriyet", "İnönü",
        "Mevlana", "Kanuni", "Yavuz Selim", "Öğretmenler", "Şehitler", "Değirmen", "Okul", "Pınar", "Gündoğdu",
        "Yeşil", "Kavak", "Söğüt", "Ilgaz", "Uğur", "Işık", "Güneş", "Bağlar", "Kardelen", "Erguvan",
        "100. Yıl", "19 Mayıs", "29 Ekim", "30 Ağustos",
    ];

    private static readonly string[] SiteNames =
        ["Güneş", "Yıldız", "Park", "Bahçe", "Mavi", "Yeşilvadi", "Gökkuşağı", "Huzur", "Deniz", "Lale", "Çamlık", "Doğa"];

    private static readonly string[] Landmarks =
        ["PTT karşısı", "Belediye yanı", "Cami arkası", "Okul karşısı", "Eczane yanı", "Park karşısı", "Market üstü"];

    private static readonly string[] MahalleWords = ["Mahallesi", "Mah.", "Mah", "Mh.", "mh", "mah.", "MAH."];
    private static readonly string[] KoyWords = ["Köyü", "Köyü", "köyü", "Köy"];
    private static readonly string[] CaddeWords = ["Caddesi", "Cad.", "Cd.", "Cad", "cd", "Cadde"];
    private static readonly string[] SokakWords = ["Sokak", "Sokağı", "Sok.", "Sk.", "sk", "sok"];
    private static readonly string[] BulvarWords = ["Bulvarı", "Blv.", "Bulv.", "bulvarı"];
    private static readonly string[] DoorWords = ["No:", "No:", "No.", "No", "no:", "NO:"];
    private static readonly string[] FlatWords = ["D:", "D.", "Daire", "daire:", "Daire:"];
    private static readonly string[] FloorWords = ["K:", "Kat", "kat:", "Kat:"];
    private static readonly string[] SiteWords = ["Sitesi", "Sit.", "Sitesi"];

    private static readonly HashSet<string> Abbreviations =
        ["mah.", "mah", "mh.", "mh", "cad.", "cad", "cd.", "cd", "sok.", "sok", "sk.", "sk", "blv.", "bulv.", "sit.", "koy"];

    private static readonly Dictionary<char, string> QwertyNeighbours = new()
    {
        ['a'] = "sqz", ['b'] = "vgn", ['c'] = "xvç", ['d'] = "sfe", ['e'] = "wrd", ['f'] = "dgr", ['g'] = "fhğ",
        ['h'] = "gjy", ['i'] = "uoı", ['ı'] = "uoi", ['j'] = "hku", ['k'] = "jli", ['l'] = "kşo", ['m'] = "nö",
        ['n'] = "bm", ['o'] = "ipı", ['p'] = "oğ", ['r'] = "etf", ['s'] = "adş", ['t'] = "ryg", ['u'] = "yiü",
        ['v'] = "cb", ['y'] = "tuh", ['z'] = "xa", ['ç'] = "cö", ['ğ'] = "pü", ['ö'] = "çm", ['ş'] = "slğ", ['ü'] = "ğu",
    };

    private readonly Gazetteer _gazetteer;
    private readonly Random _random;
    private readonly SettlementUnit[] _mahalle;
    private readonly SettlementUnit[] _koy;
    private readonly SettlementUnit[] _osb;
    private readonly ILookup<string, SettlementUnit> _unitsByKey;

    public SyntheticGenerator(Gazetteer gazetteer, int seed)
    {
        _gazetteer = gazetteer;
        _random = new Random(seed);
        _mahalle = [.. gazetteer.Units.Where(u => u.Kind == UnitKind.Mahalle)];
        _koy = [.. gazetteer.Units.Where(u => u.Kind == UnitKind.Koy)];
        _osb = [.. gazetteer.Units.Where(u => u.Kind == UnitKind.Osb)];
        _unitsByKey = gazetteer.Units.ToLookup(u => AdresTR.Gazetteer.Key(u.Name), StringComparer.Ordinal);
    }

    public IEnumerable<EvalExample> Generate(string split, int count)
    {
        for (int i = 1; i <= count; i++)
        {
            yield return Next($"syn-{split}-{i:D6}", split);
        }
    }

    private EvalExample Next(string id, string split)
    {
        SettlementUnit unit = PickUnit();
        var address = new Address(unit);
        var tags = new SortedSet<string>(StringComparer.Ordinal);
        List<Piece> pieces = Render(address, tags);

        var noise = new List<string>();
        int operations = Weighted([25, 35, 25, 15]);
        foreach (string op in Shuffle(["ascii", "uppercase", "lowercase", "typo", "glued", "missing-il", "missing-ilce", "reordered", "phone", "landmark", "broken-i", "duplicate-token"]))
        {
            if (noise.Count == operations)
            {
                break;
            }

            if ((op == "uppercase" && noise.Contains("lowercase")) || (op == "lowercase" && noise.Contains("uppercase")) ||
                (op == "missing-il" && noise.Contains("missing-ilce")) || (op == "missing-ilce" && noise.Contains("missing-il")))
            {
                continue;
            }

            if (Apply(op, pieces))
            {
                noise.Add(op);
                if (op != "lowercase")
                {
                    tags.Add(op);
                }
            }
        }

        if (pieces.Any(p => p.Label is null or Labels.CsbmTur && Abbreviations.Contains(TurkishText.Fold(p.Text))))
        {
            tags.Add("abbreviation");
        }

        return Build(id, split, address, pieces, noise, tags);
    }

    private SettlementUnit PickUnit()
    {
        int roll = _random.Next(100);
        SettlementUnit[] pool = roll < 78 ? _mahalle : roll < 98 ? _koy : _osb;

        if (_random.Next(2) == 0)
        {
            // Uniform over provinces, so small provinces are represented too.
            Province province = _gazetteer.Provinces[_random.Next(_gazetteer.Provinces.Count)];
            SettlementUnit[] inProvince = [.. pool.Where(u => u.District.Province == province)];
            if (inProvince.Length > 0)
            {
                return inProvince[_random.Next(inProvince.Length)];
            }
        }

        return pool[_random.Next(pool.Length)];
    }

    private List<Piece> Render(Address a, SortedSet<string> tags)
    {
        bool koy = a.Unit.Kind == UnitKind.Koy;
        if (koy)
        {
            tags.Add("koy");
        }

        if (a.Unit.Kind == UnitKind.Osb)
        {
            tags.Add("osb");
        }

        bool hasStreet = koy ? _random.Next(100) < 15 : _random.Next(100) < 85;
        bool hasSite = !koy && _random.Next(100) < 12;
        bool hasDoor = hasStreet ? _random.Next(100) < 92 : _random.Next(100) < 40;
        bool hasFlat = hasDoor && !koy && _random.Next(100) < 60;
        bool hasFloor = hasDoor && !koy && _random.Next(100) < 30;
        bool hasPostal = a.Unit.PostalCode is not null && _random.Next(100) < 25;
        bool slashDoor = hasFlat && !hasFloor && _random.Next(100) < 35;

        string streetType = _random.Next(100) switch { < 40 => "cadde", < 90 => "sokak", _ => "bulvar" };
        string streetName = PickStreetName(a.Unit, streetType, tags);
        string door = _random.Next(1, 160).ToString(CultureInfo.InvariantCulture);
        if (_random.Next(100) < 10)
        {
            door += _random.Next(2) == 0 ? "/" + "ABCD"[_random.Next(4)] : "ABCD"[_random.Next(4)].ToString();
            if (hasDoor)
            {
                tags.Add("slash-door");
            }
        }

        string flat = _random.Next(1, 40).ToString(CultureInfo.InvariantCulture);
        string floor = _random.Next(0, 12).ToString(CultureInfo.InvariantCulture);
        string site = SiteNames[_random.Next(SiteNames.Length)];
        string blok = "ABCDEFGH"[_random.Next(8)].ToString();

        // Components as ordered piece groups; the style decides the order and separators.
        List<Piece> mahalle = [new(a.Unit.Name, Labels.Mahalle), new(koy ? Pick(KoyWords) : Pick(MahalleWords), null)];
        if (a.Unit.Kind == UnitKind.Osb)
        {
            mahalle = [new(a.Unit.Name, Labels.Mahalle)];
        }

        List<Piece> street = !hasStreet ? [] :
        [
            new(streetName, Labels.CsbmAd),
            new(Pick(streetType switch { "cadde" => CaddeWords, "sokak" => SokakWords, _ => BulvarWords }), Labels.CsbmTur, Value: streetType),
        ];

        List<Piece> building = [];
        if (hasSite)
        {
            building.Add(new(site, Labels.Site));
            building.Add(new(Pick(SiteWords), null));
            if (_random.Next(2) == 0)
            {
                building.Add(new(blok, Labels.Blok));
                building.Add(new("Blok", null));
            }
            else
            {
                building.Add(new("Blok", null));
                building.Add(new(blok, Labels.Blok));
            }

            tags.Add("site-blok");
        }

        List<Piece> unitPieces = [];
        if (hasDoor)
        {
            string doorWord = Pick(DoorWords);
            unitPieces.Add(new(doorWord, null, Sep: doorWord.EndsWith(':') || doorWord.EndsWith('.') ? (_random.Next(2) == 0 ? "" : " ") : " "));
            if (slashDoor)
            {
                unitPieces.Add(new(door, Labels.DisKapi, Sep: "/"));
                unitPieces.Add(new(flat, Labels.Daire));
                tags.Add("slash-door");
            }
            else
            {
                unitPieces.Add(new(door, Labels.DisKapi));
            }
        }

        if (hasFloor)
        {
            string w = Pick(FloorWords);
            unitPieces.Add(new(w, null, Sep: w.EndsWith(':') ? "" : " "));
            unitPieces.Add(new(floor, Labels.Kat));
        }

        if (hasFlat && !slashDoor)
        {
            string w = Pick(FlatWords);
            unitPieces.Add(new(w, null, Sep: w.EndsWith(':') || w.EndsWith('.') ? "" : " "));
            unitPieces.Add(new(flat, Labels.Daire));
        }

        Piece ilce = new(a.Unit.District.Name, Labels.Ilce);
        Piece il = new(a.Unit.District.Province.Name, Labels.Il);
        Piece? postal = hasPostal ? new(a.Unit.PostalCode!, Labels.PostaKodu) : null;
        if (hasPostal)
        {
            tags.Add("postal-code");
        }

        var pieces = new List<Piece>();
        switch (_random.Next(100))
        {
            case < 45: // official: mahalle, street, building, no/kat/daire, postal, ilçe/il
                pieces.AddRange([.. mahalle, .. street, .. building, .. unitPieces]);
                AddPostal();
                pieces.Add(ilce with { Sep = _random.Next(3) == 0 ? " / " : "/" });
                pieces.Add(il);
                break;
            case < 60: // il first
                pieces.Add(il);
                pieces.Add(ilce);
                pieces.AddRange([.. mahalle, .. street, .. building, .. unitPieces]);
                AddPostal();
                tags.Add("reordered");
                break;
            case < 75: // street first
                pieces.AddRange([.. street, .. building, .. unitPieces, .. mahalle]);
                AddPostal();
                pieces.Add(ilce);
                pieces.Add(il);
                break;
            default: // comma separated
                AddWithComma(mahalle);
                AddWithComma(street);
                AddWithComma(building);
                AddWithComma(unitPieces);
                AddPostal();
                pieces.Add(ilce with { Sep = ", " });
                pieces.Add(il);
                break;
        }

        return pieces;

        void AddPostal()
        {
            if (postal is not null)
            {
                pieces.Add(postal);
            }
        }

        void AddWithComma(List<Piece> group)
        {
            if (group.Count == 0)
            {
                return;
            }

            pieces.AddRange(group[..^1]);
            pieces.Add(group[^1] with { Sep = ", " });
        }
    }

    private string PickStreetName(SettlementUnit unit, string type, SortedSet<string> tags)
    {
        bool izmir = unit.District.Province.Plaka == 35;
        if (type == "sokak" && _random.Next(100) < (izmir ? 60 : 12))
        {
            tags.Add("numbered-street");
            int n = _random.Next(100, 3000);
            return izmir && _random.Next(3) == 0
                ? $"{n}/{_random.Next(1, 9)}"
                : n.ToString(CultureInfo.InvariantCulture);
        }

        return StreetNames[_random.Next(StreetNames.Length)];
    }

    private bool Apply(string op, List<Piece> pieces)
    {
        switch (op)
        {
            case "ascii":
                Transform(pieces, Ascii);
                return true;

            case "uppercase":
                Transform(pieces, TurkishText.ToUpperTr);
                return true;

            case "lowercase":
                Transform(pieces, TurkishText.ToLowerTr);
                return true;

            case "typo":
            {
                int[] candidates = Indexes(pieces, p => p.Label is Labels.Mahalle or Labels.Ilce or Labels.Il or Labels.CsbmAd && p.Text.Count(char.IsLetter) >= 5);
                if (candidates.Length == 0)
                {
                    return false;
                }

                int i = candidates[_random.Next(candidates.Length)];
                pieces[i] = pieces[i] with { Text = Typo(pieces[i].Text) };
                return true;
            }

            case "glued":
            {
                int[] candidates = Indexes(pieces, p => p.Label is Labels.Mahalle or Labels.CsbmAd && p.Sep == " ");
                candidates = [.. candidates.Where(i => i + 1 < pieces.Count && (pieces[i + 1].Label is null or Labels.CsbmTur))];
                if (candidates.Length == 0)
                {
                    return false;
                }

                int i = candidates[_random.Next(candidates.Length)];
                pieces[i] = pieces[i] with { Sep = "" };
                return true;
            }

            case "missing-il":
                return Remove(pieces, Labels.Il);

            case "missing-ilce":
                return pieces.Exists(p => p.Label == Labels.Il) && Remove(pieces, Labels.Ilce);

            case "reordered":
            {
                int ilce = pieces.FindIndex(p => p.Label == Labels.Ilce);
                int il = pieces.FindIndex(p => p.Label == Labels.Il);
                if (ilce <= 0 || il <= 0)
                {
                    return false;
                }

                Piece a = pieces[ilce] with { Sep = " " };
                Piece b = pieces[il] with { Sep = " " };
                pieces.RemoveAll(p => p.Label is Labels.Ilce or Labels.Il);
                pieces.InsertRange(0, [b, a]);
                return true;
            }

            case "phone":
                pieces.Add(new("Tel:", null));
                pieces.Add(new($"0{_random.Next(2) switch { 0 => "532", _ => "212" }} 555 {_random.Next(10, 99)} {_random.Next(10, 99)}", Labels.Diger));
                return true;

            case "landmark":
                pieces.Add(new(Landmarks[_random.Next(Landmarks.Length)], Labels.Tarif));
                return true;

            case "broken-i":
            {
                int[] candidates = Indexes(pieces, p => p.Label is Labels.Mahalle or Labels.Ilce or Labels.Il && p.Text.IndexOf('i', 1) is > 0 and var k && k < p.Text.Length - 1);
                if (candidates.Length == 0)
                {
                    return false;
                }

                int i = candidates[_random.Next(candidates.Length)];
                string t = pieces[i].Text;
                int at = t.IndexOf('i', 1);
                pieces[i] = pieces[i] with { Text = t[..(at + 1)] + " " + t[(at + 1)..] };
                return true;
            }

            case "duplicate-token":
            {
                int ilce = pieces.FindIndex(p => p.Label == Labels.Ilce);
                if (ilce < 0)
                {
                    return false;
                }

                pieces.Insert(ilce + 1, pieces[ilce] with { Label = null, Sep = pieces[ilce].Sep });
                pieces[ilce] = pieces[ilce] with { Sep = " " };
                return true;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(op), op, null);
        }

        static void Transform(List<Piece> pieces, Func<string, string> f)
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                pieces[i] = pieces[i] with { Text = f(pieces[i].Text) };
            }
        }

        static bool Remove(List<Piece> pieces, string label)
        {
            int i = pieces.FindIndex(p => p.Label == label);
            if (i < 0)
            {
                return false;
            }

            if (i > 0)
            {
                pieces[i - 1] = pieces[i - 1] with { Sep = pieces[i].Sep };
            }

            pieces.RemoveAt(i);
            return true;
        }

        static int[] Indexes(List<Piece> pieces, Func<Piece, bool> predicate) =>
            [.. pieces.Select((p, i) => (p, i)).Where(x => predicate(x.p)).Select(x => x.i)];
    }

    private string Typo(string text)
    {
        int[] letters = [.. Enumerable.Range(1, text.Length - 1).Where(i => char.IsLetter(text[i]))];
        int at = letters[_random.Next(letters.Length)];
        char c = text[at];
        return _random.Next(4) switch
        {
            0 when QwertyNeighbours.TryGetValue(char.ToLowerInvariant(c), out string? near) =>
                text[..at] + near[_random.Next(near.Length)] + text[(at + 1)..],
            1 => text.Remove(at, 1),
            2 when at + 1 < text.Length && char.IsLetter(text[at + 1]) =>
                text[..at] + text[at + 1] + text[at] + text[(at + 2)..],
            _ => text.Insert(at, c.ToString()),
        };
    }

    private static string Ascii(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            sb.Append(c switch
            {
                'ç' => 'c', 'Ç' => 'C', 'ğ' => 'g', 'Ğ' => 'G', 'ı' => 'i', 'İ' => 'I',
                'ö' => 'o', 'Ö' => 'O', 'ş' => 's', 'Ş' => 'S', 'ü' => 'u', 'Ü' => 'U',
                'â' => 'a', 'Â' => 'A', 'î' => 'i', 'û' => 'u',
                _ => c,
            });
        }

        return sb.ToString();
    }

    private EvalExample Build(string id, string split, Address a, List<Piece> pieces, List<string> noise, SortedSet<string> tags)
    {
        var text = new StringBuilder();
        var spans = new List<Span>();
        for (int i = 0; i < pieces.Count; i++)
        {
            Piece p = pieces[i];
            if (p.Label is not null)
            {
                spans.Add(new Span(text.Length, text.Length + p.Text.Length, p.Label));
            }

            text.Append(p.Text);
            if (i < pieces.Count - 1)
            {
                text.Append(p.Sep);
            }
        }

        bool Written(string label) => pieces.Exists(p => p.Label == label);
        string? Value(string label) => pieces.FirstOrDefault(p => p.Label == label) is { } p ? (p.Value ?? p.Text.Trim()) : null;

        // Which units does the remaining text allow? Uses official names, so typos don't change the gold.
        IEnumerable<SettlementUnit> candidates = _unitsByKey[AdresTR.Gazetteer.Key(a.Unit.Name)];
        if (Written(Labels.Il))
        {
            candidates = candidates.Where(u => u.District.Province == a.Unit.District.Province);
        }

        if (Written(Labels.Ilce))
        {
            string key = AdresTR.Gazetteer.Key(a.Unit.District.Name);
            candidates = candidates.Where(u => AdresTR.Gazetteer.Key(u.District.Name) == key);
        }

        if (Written(Labels.PostaKodu))
        {
            candidates = candidates.Where(u => u.PostalCode == a.Unit.PostalCode);
        }

        SettlementUnit[] remaining = [.. candidates];
        if (_unitsByKey[AdresTR.Gazetteer.Key(a.Unit.Name)].Skip(1).Any())
        {
            tags.Add("ambiguous-name");
        }

        bool birimKnown = remaining.Length == 1;
        bool ilceKnown = remaining.Select(u => u.District).Distinct().Count() == 1;
        bool ilKnown = Written(Labels.Il) || remaining.Select(u => u.District.Province).Distinct().Count() == 1;

        var fields = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Labels.Il] = ilKnown ? a.Unit.District.Province.Name : null,
            [Labels.Ilce] = ilceKnown ? a.Unit.District.Name : null,
            [Labels.Mahalle] = a.Unit.Name,
            [Labels.Semt] = null,
            [Labels.CsbmTur] = Value(Labels.CsbmTur),
            [Labels.CsbmAd] = Value(Labels.CsbmAd),
            [Labels.Site] = Value(Labels.Site),
            [Labels.Blok] = Value(Labels.Blok),
            [Labels.DisKapi] = Value(Labels.DisKapi),
            [Labels.Kat] = Value(Labels.Kat),
            [Labels.Daire] = Value(Labels.Daire),
            [Labels.PostaKodu] = Value(Labels.PostaKodu),
            [Labels.Tarif] = Value(Labels.Tarif),
        };

        return new EvalExample
        {
            Id = id,
            Text = text.ToString(),
            Source = "synthetic",
            License = "CC-BY-4.0",
            Split = split,
            Spans = spans,
            Il = OptionalId.Of(ilKnown ? a.Unit.District.Province.Plaka : null),
            Ilce = OptionalId.Of(ilceKnown ? a.Unit.District.Id : null),
            Birim = OptionalId.Of(birimKnown ? a.Unit.Id : null),
            Fields = fields,
            Noise = noise,
            Tags = [.. tags],
        };
    }

    private string Pick(string[] options) => options[_random.Next(options.Length)];

    private int Weighted(int[] weights)
    {
        int roll = _random.Next(weights.Sum());
        for (int i = 0; i < weights.Length; i++)
        {
            if ((roll -= weights[i]) < 0)
            {
                return i;
            }
        }

        return weights.Length - 1;
    }

    private string[] Shuffle(string[] items)
    {
        string[] copy = [.. items];
        _random.Shuffle(copy);
        return copy;
    }

    private sealed record Address(SettlementUnit Unit);

    /// <summary>A rendered piece of text, its label (null for type words) and the separator that follows it.</summary>
    private sealed record Piece(string Text, string? Label, string Sep = " ", string? Value = null);
}
