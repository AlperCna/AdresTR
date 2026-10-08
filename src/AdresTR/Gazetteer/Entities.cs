namespace AdresTR;

/// <summary>Type of a settlement unit below the ilçe level.</summary>
public enum UnitKind : byte
{
    /// <summary>Mahalle (urban neighbourhood, including former villages converted by Law 6360).</summary>
    Mahalle = 1,

    /// <summary>Köy (village).</summary>
    Koy = 2,

    /// <summary>Belde (town with its own municipality).</summary>
    Belde = 3,

    /// <summary>Mezra (hamlet attached to a village).</summary>
    Mezra = 4,

    /// <summary>Mevkii (locality).</summary>
    Mevki = 5,

    /// <summary>Yayla (highland settlement).</summary>
    Yayla = 6,

    /// <summary>Küme evler (clustered houses).</summary>
    KumeEvler = 7,

    /// <summary>Organize sanayi bölgesi (organized industrial zone).</summary>
    Osb = 8,

    /// <summary>Site (housing estate) registered as a unit.</summary>
    Site = 9,

    /// <summary>Any other unit type.</summary>
    Diger = 10,
}

/// <summary>Administrative level an alias points to.</summary>
public enum EntityLevel : byte
{
    /// <summary>İl (province).</summary>
    Il = 1,

    /// <summary>İlçe (district).</summary>
    Ilce = 2,

    /// <summary>Settlement unit (mahalle, köy, …).</summary>
    Birim = 3,
}

/// <summary>Why an alternative name exists.</summary>
public enum AliasKind : byte
{
    /// <summary>A semt (colloquial area or postal delivery area) name.</summary>
    Semt = 1,

    /// <summary>A former official name (e.g. "X Köyü" before Law 6360).</summary>
    Tarihsel = 2,

    /// <summary>A common alternative spelling or short form (e.g. "Urfa", "Antep").</summary>
    Yazim = 3,
}

/// <summary>A WGS84 coordinate.</summary>
/// <param name="Latitude">Latitude in degrees.</param>
/// <param name="Longitude">Longitude in degrees.</param>
public readonly record struct GeoPoint(double Latitude, double Longitude);

/// <summary>İl (province), identified by its plate code.</summary>
public sealed class Province
{
    internal Province(int plaka, string name, int wikidata, GeoPoint? location)
    {
        Plaka = plaka;
        Name = name;
        WikidataId = wikidata;
        Location = location;
    }

    /// <summary>Plate code, 1–81. Also the first two digits of every postal code in the province.</summary>
    public int Plaka { get; }

    /// <summary>Official name, e.g. "İstanbul".</summary>
    public string Name { get; }

    /// <summary>Wikidata item number (Q-number without the "Q"), or 0 when unknown.</summary>
    public int WikidataId { get; }

    /// <summary>Representative point, when known.</summary>
    public GeoPoint? Location { get; }

    /// <summary>Districts of the province.</summary>
    public IReadOnlyList<District> Districts { get; internal set; } = [];

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>İlçe (district).</summary>
public sealed class District
{
    internal District(int id, Province province, string name, int wikidata, GeoPoint? location)
    {
        Id = id;
        Province = province;
        Name = name;
        WikidataId = wikidata;
        Location = location;
    }

    /// <summary>Stable AdresTR identifier (plaka × 100 + sequence).</summary>
    public int Id { get; }

    /// <summary>The province this district belongs to.</summary>
    public Province Province { get; }

    /// <summary>Official name, e.g. "Kadıköy" or "Merkez".</summary>
    public string Name { get; }

    /// <summary>Wikidata item number, or 0 when unknown.</summary>
    public int WikidataId { get; }

    /// <summary>Representative point, when known.</summary>
    public GeoPoint? Location { get; }

    /// <summary>Settlement units of the district.</summary>
    public IReadOnlyList<SettlementUnit> Units { get; internal set; } = [];

    /// <inheritdoc />
    public override string ToString() => $"{Name}/{Province.Name}";
}

/// <summary>Settlement unit below the district: mahalle, köy, belde, mezra, mevkii, …</summary>
public sealed class SettlementUnit
{
    internal SettlementUnit(
        int id, District district, UnitKind kind, string name, string? parentName,
        string? postalCode, string? semt, int wikidata, long nviId, GeoPoint? location)
    {
        Id = id;
        District = district;
        Kind = kind;
        Name = name;
        ParentName = parentName;
        PostalCode = postalCode;
        Semt = semt;
        WikidataId = wikidata;
        NviId = nviId;
        Location = location;
    }

    /// <summary>Stable AdresTR identifier (district id × 10000 + sequence).</summary>
    public int Id { get; }

    /// <summary>The district this unit belongs to.</summary>
    public District District { get; }

    /// <summary>Unit type.</summary>
    public UnitKind Kind { get; }

    /// <summary>Official name without the type suffix, e.g. "Caferağa".</summary>
    public string Name { get; }

    /// <summary>Name of the enclosing köy or belde for nested units, otherwise <see langword="null"/>.</summary>
    public string? ParentName { get; }

    /// <summary>Five-digit PTT postal code, when known.</summary>
    public string? PostalCode { get; }

    /// <summary>PTT delivery area (semt) name, when known.</summary>
    public string? Semt { get; }

    /// <summary>Wikidata item number, or 0 when unknown.</summary>
    public int WikidataId { get; }

    /// <summary>NVİ (UAVT) identifier as published on Wikidata, or 0 when unknown.</summary>
    public long NviId { get; }

    /// <summary>Representative point, when known.</summary>
    public GeoPoint? Location { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({Kind}), {District}";
}

/// <summary>An alternative name for an il, ilçe or settlement unit.</summary>
/// <param name="Level">Level of the entity the alias refers to.</param>
/// <param name="TargetId">Plate code (il), district id or unit id.</param>
/// <param name="Name">The alternative name as written.</param>
/// <param name="Kind">Why the alias exists.</param>
/// <param name="Source">Short source tag, e.g. "ptt-semt", "wikidata-6360", "el".</param>
public sealed record GazetteerAlias(EntityLevel Level, int TargetId, string Name, AliasKind Kind, string Source);
