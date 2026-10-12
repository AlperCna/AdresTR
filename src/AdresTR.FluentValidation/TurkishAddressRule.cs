using AdresTR.Data;
using FluentValidation;

namespace AdresTR;

/// <summary>FluentValidation extensions for Turkish addresses.</summary>
public static class TurkishAddressRuleExtensions
{
    /// <summary>
    /// The string must be a Turkish address that AdresTR can resolve. Null or empty values pass (add
    /// <c>NotEmpty()</c> to require one).
    /// </summary>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <param name="configure">Adjusts <see cref="TurkishAddressOptions"/> (defaults: unit required, confidence ≥ 0.6).</param>
    /// <param name="parser">Parser to use; defaults to <see cref="TurkishGazetteer.Parser"/>.</param>
    public static IRuleBuilderOptionsConditions<T, string?> MustBeTurkishAddress<T>(
        this IRuleBuilder<T, string?> ruleBuilder,
        Action<TurkishAddressOptions>? configure = null,
        AddressParser? parser = null)
    {
        ArgumentNullException.ThrowIfNull(ruleBuilder);
        var options = new TurkishAddressOptions();
        configure?.Invoke(options);
        AddressParser p = parser ?? TurkishGazetteer.Parser;

        return ruleBuilder.Custom((value, context) =>
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (options.Check(p.Parse(value)) is { } problem)
            {
                context.AddFailure(problem);
            }
        });
    }
}
