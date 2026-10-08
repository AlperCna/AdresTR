using AdresTR;

[assembly: CLSCompliant(true)]

namespace AdresTR.Data;

/// <summary>
/// The bundled Turkish gazetteer: 81 il, 973 ilçe and their settlement units with postal codes.
/// Sources and licenses are listed in LICENSE-DATA.md inside the package.
/// </summary>
public static class TurkishGazetteer
{
    /// <summary>Manifest resource name of the embedded binary gazetteer.</summary>
    public const string ResourceName = "AdresTR.Data.gazetteer.bin";

    private static readonly Lazy<AdresTR.Gazetteer> s_default = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<AddressParser> s_parser = new(() => new AddressParser(Default), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The bundled gazetteer, loaded once on first access (thread-safe).</summary>
    public static AdresTR.Gazetteer Default => s_default.Value;

    /// <summary>A shared, thread-safe <see cref="AddressParser"/> over <see cref="Default"/>.</summary>
    /// <example><code>var result = TurkishGazetteer.Parser.Parse("caferaga mh moda cd no:12 kadikoy");</code></example>
    public static AddressParser Parser => s_parser.Value;

    /// <summary>Loads a fresh copy of the bundled gazetteer.</summary>
    public static AdresTR.Gazetteer Load()
    {
        using Stream stream = typeof(TurkishGazetteer).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing from AdresTR.Data.");
        return AdresTR.Gazetteer.Load(stream);
    }
}
