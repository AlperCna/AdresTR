using System.ComponentModel.DataAnnotations;
using AdresTR.Data;
using Microsoft.Extensions.DependencyInjection;

namespace AdresTR;

/// <summary>Rules shared by <c>[TurkishAddress]</c> and the FluentValidation rule.</summary>
public sealed class TurkishAddressOptions
{
    /// <summary>The address must resolve to a single mahalle/köy (default true).</summary>
    public bool RequireUnit { get; set; } = true;

    /// <summary>Minimum parse confidence in [0, 1] (default 0.6).</summary>
    public double MinimumConfidence { get; set; } = 0.6;

    /// <summary>The address must contain a door number (default false).</summary>
    public bool RequireDoorNumber { get; set; }

    /// <summary>Returns a human-readable problem, or <see langword="null"/> when <paramref name="result"/> satisfies the rules.</summary>
    public string? Check(ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (RequireUnit && result.Unit is null)
        {
            return result.UnitCandidates.Count > 1
                ? $"The address is ambiguous: the neighbourhood (mahalle) matches {result.UnitCandidates.Count} places. Add the ilçe or il."
                : "The address could not be resolved to a neighbourhood (mahalle).";
        }

        if (result.Confidence < MinimumConfidence)
        {
            return $"The address could not be parsed reliably (confidence {result.Confidence:P0}).";
        }

        if (RequireDoorNumber && result.DoorNumber is null)
        {
            return "The address has no door number.";
        }

        return null;
    }
}

/// <summary>
/// Validates that a string is a Turkish address that AdresTR can resolve. Null or empty values are valid
/// (combine with <see cref="RequiredAttribute"/> to require a value).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class TurkishAddressAttribute : ValidationAttribute
{
    /// <summary>The address must resolve to a single mahalle/köy (default true).</summary>
    public bool RequireUnit { get; set; } = true;

    /// <summary>Minimum parse confidence in [0, 1] (default 0.6).</summary>
    public double MinimumConfidence { get; set; } = 0.6;

    /// <summary>The address must contain a door number (default false).</summary>
    public bool RequireDoorNumber { get; set; }

    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return ValidationResult.Success;
        }

        ArgumentNullException.ThrowIfNull(validationContext);
        AddressParser parser = validationContext.GetService(typeof(AddressParser)) as AddressParser ?? TurkishGazetteer.Parser;
        string? problem = new TurkishAddressOptions
        {
            RequireUnit = RequireUnit,
            MinimumConfidence = MinimumConfidence,
            RequireDoorNumber = RequireDoorNumber,
        }.Check(parser.Parse(text));

        return problem is null
            ? ValidationResult.Success
            : new ValidationResult(ErrorMessage ?? problem, validationContext.MemberName is { } member ? [member] : null);
    }
}

/// <summary>Dependency injection registration.</summary>
public static class AdresTRServiceCollectionExtensions
{
    /// <summary>Registers <see cref="Gazetteer"/>, <see cref="AddressParser"/> and <see cref="AddressMatcher"/> as singletons.</summary>
    public static IServiceCollection AddAdresTR(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(_ => TurkishGazetteer.Default);
        services.AddSingleton(_ => TurkishGazetteer.Parser);
        services.AddSingleton(sp => new AddressMatcher(sp.GetRequiredService<AddressParser>()));
        return services;
    }
}
