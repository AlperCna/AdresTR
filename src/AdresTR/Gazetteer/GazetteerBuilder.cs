namespace AdresTR;

/// <summary>Thrown when gazetteer data violates an integrity rule.</summary>
public sealed class GazetteerValidationException : Exception
{
    /// <summary>Creates the exception from the list of violations.</summary>
    public GazetteerValidationException(IReadOnlyList<string> errors)
        : base($"Gazetteer validation failed with {errors.Count} error(s):{Environment.NewLine}" +
               string.Join(Environment.NewLine, errors.Take(50)))
    {
        Errors = errors;
    }

    /// <summary>Creates the exception with a single message.</summary>
    public GazetteerValidationException(string message)
        : this([message])
    {
    }

    /// <summary>Creates an empty exception.</summary>
    public GazetteerValidationException()
        : this([])
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    public GazetteerValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [message];
    }

    /// <summary>All violations found.</summary>
    public IReadOnlyList<string> Errors { get; }
}

/// <summary>Collects gazetteer records, validates them and builds an immutable <see cref="Gazetteer"/>.</summary>
public sealed class GazetteerBuilder
{
    private readonly List<(int Plaka, string Name, int Wikidata, GeoPoint? Location)> _provinces = [];
    private readonly List<(int Id, int Plaka, string Name, int Wikidata, GeoPoint? Location)> _districts = [];
    private readonly List<UnitRecord> _units = [];
    private readonly List<GazetteerAlias> _aliases = [];

    /// <summary>Creates a builder for the given data snapshot version (e.g. "2026.10").</summary>
    public GazetteerBuilder(string dataVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataVersion);
        DataVersion = dataVersion;
    }

    /// <summary>Data snapshot version.</summary>
    public string DataVersion { get; }

    /// <summary>Provenance JSON stored alongside the data.</summary>
    public string Sources { get; set; } = "{}";

    /// <summary>Adds a province.</summary>
    public GazetteerBuilder AddProvince(int plaka, string name, int wikidataId = 0, GeoPoint? location = null)
    {
        _provinces.Add((plaka, name, wikidataId, location));
        return this;
    }

    /// <summary>Adds a district.</summary>
    public GazetteerBuilder AddDistrict(int id, int plaka, string name, int wikidataId = 0, GeoPoint? location = null)
    {
        _districts.Add((id, plaka, name, wikidataId, location));
        return this;
    }

    /// <summary>Adds a settlement unit.</summary>
    public GazetteerBuilder AddUnit(
        int id, int districtId, UnitKind kind, string name, string? parentName = null, string? postalCode = null,
        string? semt = null, int wikidataId = 0, long nviId = 0, GeoPoint? location = null)
    {
        _units.Add(new UnitRecord(id, districtId, kind, name, parentName, postalCode, semt, wikidataId, nviId, location));
        return this;
    }

    /// <summary>Adds an alias.</summary>
    public GazetteerBuilder AddAlias(GazetteerAlias alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        _aliases.Add(alias);
        return this;
    }

    /// <summary>Validates all records and builds the gazetteer.</summary>
    /// <exception cref="GazetteerValidationException">One or more integrity rules are violated.</exception>
    public Gazetteer Build()
    {
        var errors = new List<string>();

        var provinces = new Dictionary<int, Province>();
        foreach (var p in _provinces.OrderBy(p => p.Plaka))
        {
            if (p.Plaka is < 1 or > 81)
            {
                errors.Add($"il {p.Plaka}: plate code must be 1-81");
            }
            else if (!provinces.TryAdd(p.Plaka, new Province(p.Plaka, CheckName(p.Name, $"il {p.Plaka}"), p.Wikidata, CheckLocation(p.Location, $"il {p.Plaka}"))))
            {
                errors.Add($"il {p.Plaka}: duplicate plate code");
            }
        }

        var districts = new Dictionary<int, District>();
        foreach (var d in _districts.OrderBy(d => d.Id))
        {
            string where = $"ilçe {d.Id}";
            if (!provinces.TryGetValue(d.Plaka, out Province? province))
            {
                errors.Add($"{where}: unknown il {d.Plaka}");
            }
            else if (!districts.TryAdd(d.Id, new District(d.Id, province, CheckName(d.Name, where), d.Wikidata, CheckLocation(d.Location, where))))
            {
                errors.Add($"{where}: duplicate id");
            }
        }

        var units = new Dictionary<int, SettlementUnit>();
        foreach (UnitRecord u in _units.OrderBy(u => u.Id))
        {
            string where = $"birim {u.Id}";
            if (!districts.TryGetValue(u.DistrictId, out District? district))
            {
                errors.Add($"{where}: unknown ilçe {u.DistrictId}");
                continue;
            }

            if (u.Kind is < UnitKind.Mahalle or > UnitKind.Diger)
            {
                errors.Add($"{where}: unknown kind {(byte)u.Kind}");
            }

            string? postalCode = string.IsNullOrEmpty(u.PostalCode) ? null : u.PostalCode;
            if (postalCode is not null &&
                (postalCode.Length != 5 || postalCode.AsSpan().ContainsAnyExceptInRange('0', '9') ||
                 int.Parse(postalCode.AsSpan(0, 2), System.Globalization.CultureInfo.InvariantCulture) != district.Province.Plaka))
            {
                errors.Add($"{where}: postal code '{postalCode}' must be 5 digits starting with plate {district.Province.Plaka:D2}");
            }

            var unit = new SettlementUnit(
                u.Id, district, u.Kind, CheckName(u.Name, where), OptionalName(u.ParentName, where),
                postalCode, OptionalName(u.Semt, where), u.Wikidata, u.NviId, CheckLocation(u.Location, where));

            if (!units.TryAdd(u.Id, unit))
            {
                errors.Add($"{where}: duplicate id");
            }
        }

        foreach (GazetteerAlias a in _aliases)
        {
            bool exists = a.Level switch
            {
                EntityLevel.Il => provinces.ContainsKey(a.TargetId),
                EntityLevel.Ilce => districts.ContainsKey(a.TargetId),
                EntityLevel.Birim => units.ContainsKey(a.TargetId),
                _ => false,
            };

            if (!exists)
            {
                errors.Add($"alias '{a.Name}': unknown {a.Level} {a.TargetId}");
            }

            CheckName(a.Name, $"alias of {a.Level} {a.TargetId}");
        }

        if (errors.Count > 0)
        {
            throw new GazetteerValidationException(errors);
        }

        foreach (Province p in provinces.Values)
        {
            p.Districts = [.. districts.Values.Where(d => d.Province == p)];
        }

        foreach (IGrouping<District, SettlementUnit> g in units.Values.GroupBy(u => u.District))
        {
            g.Key.Units = [.. g];
        }

        return new Gazetteer(
            DataVersion, Sources,
            [.. provinces.Values], [.. districts.Values], [.. units.Values],
            [.. _aliases.OrderBy(a => a.Level).ThenBy(a => a.TargetId).ThenBy(a => a.Name, StringComparer.Ordinal)]);

        string CheckName(string? name, string where)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add($"{where}: empty name");
                return string.Empty;
            }

            if (name != name.Trim() || name.Contains("  ", StringComparison.Ordinal))
            {
                errors.Add($"{where}: name '{name}' has leading, trailing or double spaces");
            }

            return name;
        }

        string? OptionalName(string? name, string where) =>
            string.IsNullOrEmpty(name) ? null : CheckName(name, where);

        GeoPoint? CheckLocation(GeoPoint? location, string where)
        {
            // Generous bounding box around Türkiye.
            if (location is { } l && (l.Latitude is < 35 or > 43 || l.Longitude is < 25 or > 45.5))
            {
                errors.Add($"{where}: location {l.Latitude},{l.Longitude} is outside Türkiye");
            }

            return location;
        }
    }

    private sealed record UnitRecord(
        int Id, int DistrictId, UnitKind Kind, string Name, string? ParentName, string? PostalCode,
        string? Semt, int Wikidata, long NviId, GeoPoint? Location);
}
