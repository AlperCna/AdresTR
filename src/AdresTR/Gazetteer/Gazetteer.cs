using System.Collections.Frozen;
using AdresTR.Text;

namespace AdresTR;

/// <summary>An entity found by name, and the alias that matched when it was not the official name.</summary>
/// <typeparam name="T"><see cref="Province"/>, <see cref="District"/> or <see cref="SettlementUnit"/>.</typeparam>
/// <param name="Entity">The matched entity.</param>
/// <param name="Alias">The alias that matched, or <see langword="null"/> when the official name matched.</param>
public readonly record struct GazetteerMatch<T>(T Entity, GazetteerAlias? Alias);

/// <summary>
/// Immutable, in-memory Turkish administrative gazetteer: il → ilçe → settlement units, with aliases,
/// postal codes and name indexes. Build one with <see cref="GazetteerBuilder"/> or load one with <see cref="Load"/>.
/// </summary>
/// <remarks>Thread-safe: all members are read-only after construction.</remarks>
public sealed class Gazetteer
{
    private readonly Province?[] _provincesByPlaka;
    private readonly FrozenDictionary<int, District> _districts;
    private readonly FrozenDictionary<int, SettlementUnit> _units;
    private readonly FrozenDictionary<string, GazetteerMatch<Province>[]> _provinceIndex;
    private readonly FrozenDictionary<string, GazetteerMatch<District>[]> _districtIndex;
    private readonly FrozenDictionary<string, GazetteerMatch<SettlementUnit>[]> _unitIndex;
    private readonly FrozenDictionary<string, SettlementUnit[]> _unitsByPostalCode;
    private Parsing.ParserIndex? _parserIndex;

    internal Gazetteer(
        string dataVersion,
        string sources,
        IReadOnlyList<Province> provinces,
        IReadOnlyList<District> districts,
        IReadOnlyList<SettlementUnit> units,
        IReadOnlyList<GazetteerAlias> aliases)
    {
        DataVersion = dataVersion;
        Sources = sources;
        Provinces = provinces;
        Districts = districts;
        Units = units;
        Aliases = aliases;

        _provincesByPlaka = new Province?[82];
        foreach (Province p in provinces)
        {
            _provincesByPlaka[p.Plaka] = p;
        }

        _districts = districts.ToFrozenDictionary(d => d.Id);
        _units = units.ToFrozenDictionary(u => u.Id);

        _provinceIndex = BuildIndex(provinces, p => p.Name, EntityLevel.Il, aliases, id => _provincesByPlaka[id]);
        _districtIndex = BuildIndex(districts, d => d.Name, EntityLevel.Ilce, aliases, id => _districts.GetValueOrDefault(id));
        _unitIndex = BuildIndex(units, u => u.Name, EntityLevel.Birim, aliases, id => _units.GetValueOrDefault(id));

        _unitsByPostalCode = units
            .Where(u => u.PostalCode is not null)
            .GroupBy(u => u.PostalCode!, StringComparer.Ordinal)
            .ToFrozenDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
    }

    /// <summary>Data snapshot version, e.g. "2026.10".</summary>
    public string DataVersion { get; }

    /// <summary>Provenance of the data (JSON describing sources, licenses and hashes).</summary>
    public string Sources { get; }

    /// <summary>All provinces, ordered by plate code.</summary>
    public IReadOnlyList<Province> Provinces { get; }

    /// <summary>All districts, ordered by id.</summary>
    public IReadOnlyList<District> Districts { get; }

    /// <summary>All settlement units, ordered by id.</summary>
    public IReadOnlyList<SettlementUnit> Units { get; }

    /// <summary>All aliases.</summary>
    public IReadOnlyList<GazetteerAlias> Aliases { get; }

    /// <summary>Returns the province with the given plate code, or <see langword="null"/>.</summary>
    public Province? GetProvince(int plaka) =>
        plaka is >= 1 and <= 81 ? _provincesByPlaka[plaka] : null;

    /// <summary>Returns the district with the given id, or <see langword="null"/>.</summary>
    public District? GetDistrict(int id) => _districts.GetValueOrDefault(id);

    /// <summary>Returns the settlement unit with the given id, or <see langword="null"/>.</summary>
    public SettlementUnit? GetUnit(int id) => _units.GetValueOrDefault(id);

    /// <summary>Finds provinces by official name or alias. Case, diacritics, spaces and punctuation are ignored.</summary>
    public IReadOnlyList<GazetteerMatch<Province>> FindProvinces(string name) =>
        _provinceIndex.GetValueOrDefault(Key(name)) ?? [];

    /// <summary>Finds districts by official name or alias, optionally only within one province.</summary>
    /// <param name="name">Name to look up.</param>
    /// <param name="plaka">Plate code to restrict the search to, or 0 for all provinces.</param>
    public IReadOnlyList<GazetteerMatch<District>> FindDistricts(string name, int plaka = 0)
    {
        GazetteerMatch<District>[] all = _districtIndex.GetValueOrDefault(Key(name)) ?? [];
        return plaka == 0 ? all : Array.FindAll(all, m => m.Entity.Province.Plaka == plaka);
    }

    /// <summary>Finds settlement units by official name or alias, optionally only within one district.</summary>
    /// <param name="name">Name to look up, without the type suffix ("Caferağa", not "Caferağa Mahallesi").</param>
    /// <param name="districtId">District to restrict the search to, or 0 for all districts.</param>
    public IReadOnlyList<GazetteerMatch<SettlementUnit>> FindUnits(string name, int districtId = 0)
    {
        GazetteerMatch<SettlementUnit>[] all = _unitIndex.GetValueOrDefault(Key(name)) ?? [];
        return districtId == 0 ? all : Array.FindAll(all, m => m.Entity.District.Id == districtId);
    }

    internal IReadOnlyList<GazetteerMatch<Province>> FindProvincesByKey(string key) =>
        _provinceIndex.GetValueOrDefault(key) ?? [];

    internal IReadOnlyList<GazetteerMatch<District>> FindDistrictsByKey(string key) =>
        _districtIndex.GetValueOrDefault(key) ?? [];

    internal IReadOnlyList<GazetteerMatch<SettlementUnit>> FindUnitsByKey(string key) =>
        _unitIndex.GetValueOrDefault(key) ?? [];

    /// <summary>Lookup structures used by the parser (fuzzy candidate lists), built on first use.</summary>
    internal Parsing.ParserIndex ParserIndex => LazyInitializer.EnsureInitialized(ref _parserIndex, () => new Parsing.ParserIndex(this));

    /// <summary>Returns the settlement units served by a five-digit postal code.</summary>
    public IReadOnlyList<SettlementUnit> FindUnitsByPostalCode(string postalCode) =>
        _unitsByPostalCode.GetValueOrDefault(postalCode) ?? [];

    /// <summary>Reads a gazetteer written by <see cref="Save"/>.</summary>
    /// <exception cref="InvalidDataException">The stream is not a valid AdresTR gazetteer.</exception>
    public static Gazetteer Load(Stream stream) => GazetteerSerializer.Read(stream);

    /// <summary>Writes the gazetteer in the AdresTR binary format.</summary>
    public void Save(Stream stream) => GazetteerSerializer.Write(this, stream);

    /// <summary>
    /// The lookup key of a name: <see cref="TurkishText.Fold(string?)"/> with spaces and <c>. - ' /</c> removed,
    /// so "Gazi Osman Paşa", "GAZİOSMANPAŞA" and "gaziosmanpasa" share a key, as do "100. Yıl" and "100.YIL".
    /// </summary>
    public static string Key(string? name)
    {
        string folded = TurkishText.Fold(name);
        return string.Create(folded.Length - CountSkipped(folded), folded, static (span, source) =>
        {
            int w = 0;
            foreach (char c in source)
            {
                if (!IsSkipped(c))
                {
                    span[w++] = c;
                }
            }
        });

        static int CountSkipped(string s)
        {
            int n = 0;
            foreach (char c in s)
            {
                if (IsSkipped(c))
                {
                    n++;
                }
            }

            return n;
        }

        static bool IsSkipped(char c) => c is ' ' or '.' or '-' or '\'' or '/';
    }

    private static FrozenDictionary<string, GazetteerMatch<T>[]> BuildIndex<T>(
        IReadOnlyList<T> entities,
        Func<T, string> name,
        EntityLevel level,
        IReadOnlyList<GazetteerAlias> aliases,
        Func<int, T?> resolve)
        where T : class
    {
        var index = new Dictionary<string, List<GazetteerMatch<T>>>(StringComparer.Ordinal);

        foreach (T entity in entities)
        {
            Add(Key(name(entity)), new GazetteerMatch<T>(entity, null));
        }

        foreach (GazetteerAlias alias in aliases)
        {
            if (alias.Level == level && resolve(alias.TargetId) is { } target)
            {
                string key = Key(alias.Name);
                // An alias equal to the official name adds nothing.
                if (!index.TryGetValue(key, out var existing) || !existing.Exists(m => ReferenceEquals(m.Entity, target)))
                {
                    Add(key, new GazetteerMatch<T>(target, alias));
                }
            }
        }

        return index.ToFrozenDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.Ordinal);

        void Add(string key, GazetteerMatch<T> match)
        {
            if (key.Length == 0)
            {
                return;
            }

            if (!index.TryGetValue(key, out var list))
            {
                index[key] = list = [];
            }

            list.Add(match);
        }
    }
}
