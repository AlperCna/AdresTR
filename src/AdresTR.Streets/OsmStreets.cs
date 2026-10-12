using AdresTR.Data;

[assembly: CLSCompliant(true)]

namespace AdresTR.Streets;

/// <summary>
/// Street names per ilçe from OpenStreetMap (© OpenStreetMap contributors, ODbL 1.0). OpenStreetMap is incomplete:
/// a street missing here is not proof that it does not exist.
/// </summary>
public static class OsmStreets
{
    /// <summary>Manifest resource name of the embedded street file.</summary>
    public const string ResourceName = "AdresTR.Streets.streets.csv.gz";

    private static readonly Lazy<StreetIndex> s_default = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<AddressParser> s_parser =
        new(() => new AddressParser(TurkishGazetteer.Default, Default), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The bundled street index, loaded once on first access (thread-safe).</summary>
    public static StreetIndex Default => s_default.Value;

    /// <summary>A shared parser over the bundled gazetteer that also verifies and corrects streets.</summary>
    /// <example><code>var r = OsmStreets.Parser.Parse("Caferağa Mah. Modaa Cad. No:12 Kadıköy"); // r.Street.Value == "Moda"</code></example>
    public static AddressParser Parser => s_parser.Value;

    /// <summary>Loads a fresh copy of the bundled street index.</summary>
    public static StreetIndex Load()
    {
        using Stream stream = typeof(OsmStreets).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing from AdresTR.Streets.");
        return StreetIndex.Load(stream);
    }
}
