using System.IO.Compression;
using System.Text;
using AdresTR.Parsing;

namespace AdresTR;

/// <summary>
/// Street names per ilçe, used by <see cref="AddressParser"/> to verify and spell-correct streets.
/// The core library ships no street data (ADR-0004); load one from the optional AdresTR.Streets package
/// (OpenStreetMap, ODbL) or from your own gzip CSV (<c>ilce_id,tur,ad,kanit</c>).
/// </summary>
public sealed class StreetIndex
{
    private readonly Dictionary<int, Dictionary<string, string>> _byDistrict;
    private readonly Dictionary<int, (string Key, string Display)[]> _lists;

    private StreetIndex(Dictionary<int, Dictionary<string, string>> byDistrict)
    {
        _byDistrict = byDistrict;
        _lists = byDistrict.ToDictionary(kv => kv.Key, kv => kv.Value.Select(e => (e.Key, e.Value)).ToArray());
        Count = byDistrict.Sum(kv => kv.Value.Count);
    }

    /// <summary>Number of distinct (ilçe, street) entries.</summary>
    public int Count { get; }

    /// <summary>Loads a gzip-compressed CSV with the header <c>ilce_id,tur,ad,kanit</c>.</summary>
    /// <exception cref="InvalidDataException">The data is not a valid street file.</exception>
    public static StreetIndex Load(Stream gzipCsv)
    {
        ArgumentNullException.ThrowIfNull(gzipCsv);
        using var gzip = new GZipStream(gzipCsv, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        if (reader.ReadLine() != "ilce_id,tur,ad,kanit")
        {
            throw new InvalidDataException("Expected the header 'ilce_id,tur,ad,kanit'.");
        }

        var byDistrict = new Dictionary<int, Dictionary<string, string>>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            int first = line.IndexOf(',', StringComparison.Ordinal);
            int second = first < 0 ? -1 : line.IndexOf(',', first + 1);
            int last = line.LastIndexOf(',');
            if (first < 0 || second < 0 || last <= second || !int.TryParse(line.AsSpan(0, first), out int district))
            {
                throw new InvalidDataException($"Invalid street record: {line}");
            }

            string name = line[(second + 1)..last];
            if (name.Length >= 2 && name[0] == '"' && name[^1] == '"')
            {
                name = name[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
            }

            string key = Gazetteer.Key(name);
            if (key.Length == 0)
            {
                continue;
            }

            if (!byDistrict.TryGetValue(district, out var names))
            {
                byDistrict[district] = names = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            names.TryAdd(key, name);
        }

        return new StreetIndex(byDistrict);
    }

    /// <summary>Whether the ilçe has a street with this name (case, diacritics and spacing ignored).</summary>
    public bool Contains(int districtId, string name) =>
        _byDistrict.TryGetValue(districtId, out var names) && names.ContainsKey(Gazetteer.Key(name));

    /// <summary>
    /// Finds the street of an ilçe: the exact name when present, otherwise the unique closest spelling within the
    /// typo budget of <see cref="Parsing.Fuzzy"/>. Returns <see langword="null"/> when there is none or it is ambiguous.
    /// </summary>
    public string? Find(int districtId, string name, out bool corrected)
    {
        corrected = false;
        if (!_byDistrict.TryGetValue(districtId, out var names))
        {
            return null;
        }

        string key = Gazetteer.Key(name);
        if (names.TryGetValue(key, out string? exact))
        {
            return exact;
        }

        var found = Fuzzy.Search(key, _lists[districtId], scoped: true);
        if (found.Count == 0 || (found.Count > 1 && found[1].Distance == found[0].Distance))
        {
            return null;
        }

        corrected = true;
        return found[0].Entity;
    }
}
