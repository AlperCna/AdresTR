using System.IO.Compression;
using System.Text;

namespace AdresTR;

/// <summary>
/// AdresTR binary gazetteer format, version 1.
/// </summary>
/// <remarks>
/// <code>
/// header   "ADTR" | uint16 format version | uint16 reserved      (uncompressed)
/// payload  zlib-compressed:
///   string dataVersion, string sources
///   7bit stringCount, string[stringCount]          (index 0 = null/empty)
///   7bit n, n × il    { byte plaka, 7bit name, 7bit wikidata, float lat, float lon }
///   7bit n, n × ilçe  { 7bit id, byte plaka, 7bit name, 7bit wikidata, float lat, float lon }
///   7bit n, n × birim { 7bit id, 7bit ilçe, byte kind, 7bit name, 7bit parent, 7bit postal+1,
///                       7bit semt, 7bit wikidata, 7bit64 nvi, float lat, float lon }
///   7bit n, n × alias { byte level, 7bit target, 7bit name, byte kind, 7bit source }
/// </code>
/// Missing coordinates are stored as NaN. Zlib (Deflate) is used instead of Brotli because Brotli is not
/// available in browser WebAssembly.
/// </remarks>
internal static class GazetteerSerializer
{
    private const ushort FormatVersion = 1;
    private static readonly byte[] Magic = "ADTR"u8.ToArray();

    public static void Write(Gazetteer gazetteer, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(gazetteer);
        ArgumentNullException.ThrowIfNull(stream);

        stream.Write(Magic);
        Span<byte> version = stackalloc byte[4];
        BitConverter.TryWriteBytes(version, FormatVersion);
        stream.Write(version);

        using var zlib = new ZLibStream(stream, CompressionLevel.SmallestSize, leaveOpen: true);
        WritePayload(gazetteer, zlib);
    }

    /// <summary>Writes the uncompressed payload. Used for content comparison.</summary>
    internal static void WritePayload(Gazetteer g, Stream stream)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        var strings = new StringTable();

        // Pass 1: intern strings in a deterministic order.
        foreach (Province p in g.Provinces)
        {
            strings.Add(p.Name);
        }

        foreach (District d in g.Districts)
        {
            strings.Add(d.Name);
        }

        foreach (SettlementUnit u in g.Units)
        {
            strings.Add(u.Name);
            strings.Add(u.ParentName);
            strings.Add(u.Semt);
        }

        foreach (GazetteerAlias a in g.Aliases)
        {
            strings.Add(a.Name);
            strings.Add(a.Source);
        }

        w.Write(g.DataVersion);
        w.Write(g.Sources);

        w.Write7BitEncodedInt(strings.Count);
        foreach (string s in strings.Items)
        {
            w.Write(s);
        }

        w.Write7BitEncodedInt(g.Provinces.Count);
        foreach (Province p in g.Provinces)
        {
            w.Write((byte)p.Plaka);
            w.Write7BitEncodedInt(strings[p.Name]);
            w.Write7BitEncodedInt(p.WikidataId);
            WriteLocation(w, p.Location);
        }

        w.Write7BitEncodedInt(g.Districts.Count);
        foreach (District d in g.Districts)
        {
            w.Write7BitEncodedInt(d.Id);
            w.Write((byte)d.Province.Plaka);
            w.Write7BitEncodedInt(strings[d.Name]);
            w.Write7BitEncodedInt(d.WikidataId);
            WriteLocation(w, d.Location);
        }

        w.Write7BitEncodedInt(g.Units.Count);
        foreach (SettlementUnit u in g.Units)
        {
            w.Write7BitEncodedInt(u.Id);
            w.Write7BitEncodedInt(u.District.Id);
            w.Write((byte)u.Kind);
            w.Write7BitEncodedInt(strings[u.Name]);
            w.Write7BitEncodedInt(strings[u.ParentName]);
            w.Write7BitEncodedInt(u.PostalCode is null ? 0 : int.Parse(u.PostalCode, System.Globalization.CultureInfo.InvariantCulture) + 1);
            w.Write7BitEncodedInt(strings[u.Semt]);
            w.Write7BitEncodedInt(u.WikidataId);
            w.Write7BitEncodedInt64(u.NviId);
            WriteLocation(w, u.Location);
        }

        w.Write7BitEncodedInt(g.Aliases.Count);
        foreach (GazetteerAlias a in g.Aliases)
        {
            w.Write((byte)a.Level);
            w.Write7BitEncodedInt(a.TargetId);
            w.Write7BitEncodedInt(strings[a.Name]);
            w.Write((byte)a.Kind);
            w.Write7BitEncodedInt(strings[a.Source]);
        }
    }

    public static Gazetteer Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Span<byte> header = stackalloc byte[8];
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not an AdresTR gazetteer (bad magic).");
        }

        ushort version = BitConverter.ToUInt16(header[4..6]);
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Unsupported AdresTR gazetteer format version {version}; expected {FormatVersion}.");
        }

        // Inflate in one go: BinaryReader issues many tiny reads, which are slow on a decompression stream
        // (and very slow in WebAssembly).
        var payload = new MemoryStream(capacity: 4 * 1024 * 1024);
        using (var zlib = new ZLibStream(stream, CompressionMode.Decompress, leaveOpen: true))
        {
            zlib.CopyTo(payload);
        }

        payload.Position = 0;
        using var r = new BinaryReader(payload, Encoding.UTF8, leaveOpen: false);

        try
        {
            var builder = new GazetteerBuilder(r.ReadString()) { Sources = r.ReadString() };

            var strings = new string?[r.Read7BitEncodedInt()];
            for (int i = 0; i < strings.Length; i++)
            {
                strings[i] = r.ReadString();
            }

            strings[0] = null;

            for (int n = r.Read7BitEncodedInt(); n > 0; n--)
            {
                int plaka = r.ReadByte();
                string name = strings[r.Read7BitEncodedInt()]!;
                builder.AddProvince(plaka, name, r.Read7BitEncodedInt(), ReadLocation(r));
            }

            for (int n = r.Read7BitEncodedInt(); n > 0; n--)
            {
                int id = r.Read7BitEncodedInt();
                int plaka = r.ReadByte();
                string name = strings[r.Read7BitEncodedInt()]!;
                builder.AddDistrict(id, plaka, name, r.Read7BitEncodedInt(), ReadLocation(r));
            }

            for (int n = r.Read7BitEncodedInt(); n > 0; n--)
            {
                int id = r.Read7BitEncodedInt();
                int districtId = r.Read7BitEncodedInt();
                var kind = (UnitKind)r.ReadByte();
                string name = strings[r.Read7BitEncodedInt()]!;
                string? parent = strings[r.Read7BitEncodedInt()];
                int postal = r.Read7BitEncodedInt();
                string? semt = strings[r.Read7BitEncodedInt()];
                int wikidata = r.Read7BitEncodedInt();
                long nvi = r.Read7BitEncodedInt64();
                builder.AddUnit(
                    id, districtId, kind, name, parent,
                    postal == 0 ? null : (postal - 1).ToString("D5", System.Globalization.CultureInfo.InvariantCulture),
                    semt, wikidata, nvi, ReadLocation(r));
            }

            for (int n = r.Read7BitEncodedInt(); n > 0; n--)
            {
                var level = (EntityLevel)r.ReadByte();
                int target = r.Read7BitEncodedInt();
                string name = strings[r.Read7BitEncodedInt()]!;
                var kind = (AliasKind)r.ReadByte();
                string source = strings[r.Read7BitEncodedInt()] ?? string.Empty;
                builder.AddAlias(new GazetteerAlias(level, target, name, kind, source));
            }

            return builder.Build();
        }
        catch (Exception ex) when (ex is EndOfStreamException or IndexOutOfRangeException or FormatException or GazetteerValidationException)
        {
            throw new InvalidDataException("Corrupt AdresTR gazetteer: " + ex.Message, ex);
        }
    }

    private static void WriteLocation(BinaryWriter w, GeoPoint? location)
    {
        w.Write(location is { } l ? (float)l.Latitude : float.NaN);
        w.Write(location is { } m ? (float)m.Longitude : float.NaN);
    }

    private static GeoPoint? ReadLocation(BinaryReader r)
    {
        float lat = r.ReadSingle();
        float lon = r.ReadSingle();
        return float.IsNaN(lat) || float.IsNaN(lon) ? null : new GeoPoint(Math.Round(lat, 6), Math.Round(lon, 6));
    }

    private sealed class StringTable
    {
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal) { [string.Empty] = 0 };

        public List<string> Items { get; } = [string.Empty];

        public int Count => Items.Count;

        public int this[string? s] => string.IsNullOrEmpty(s) ? 0 : _index[s];

        public void Add(string? s)
        {
            if (!string.IsNullOrEmpty(s) && !_index.ContainsKey(s))
            {
                _index[s] = Items.Count;
                Items.Add(s);
            }
        }
    }
}
