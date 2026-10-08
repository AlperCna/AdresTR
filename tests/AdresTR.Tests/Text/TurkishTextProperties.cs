using AdresTR.Text;
using CsCheck;

namespace AdresTR.Tests.Text;

public class TurkishTextProperties
{
    private const string Alphabet =
        "abcçdefgğhıijklmnoöprsştuüvyzABCÇDEFGĞHIİJKLMNOÖPRSŞTUÜVYZâîûÂÎÛqwxQWX0123456789 .,:/-'\u00A0\u0307\u0327";

    private static readonly Gen<string> TurkishStrings = Gen.String[Gen.Char[Alphabet], 0, 60];

    private static readonly Gen<string> AnyStrings = Gen.String[Gen.Char, 0, 60];

    [Fact]
    public void Fold_is_idempotent() =>
        AnyStrings.Sample(s => TurkishText.Fold(TurkishText.Fold(s)) == TurkishText.Fold(s));

    [Fact]
    public void Fold_output_is_ascii_for_turkish_text() =>
        TurkishStrings.Sample(s => TurkishText.Fold(s).All(char.IsAscii));

    [Fact]
    public void Fold_has_no_leading_trailing_or_double_spaces() =>
        AnyStrings.Sample(s =>
        {
            string folded = TurkishText.Fold(s);
            return folded == folded.Trim() && !folded.Contains("  ", StringComparison.Ordinal);
        });

    [Fact]
    public void Fold_ignores_turkish_case() =>
        TurkishStrings.Sample(s =>
            TurkishText.Fold(TurkishText.ToUpperTr(s)) == TurkishText.Fold(s)
            && TurkishText.Fold(TurkishText.ToLowerTr(s)) == TurkishText.Fold(s)
            && TurkishText.Fold(TurkishText.ToTitleTr(s)) == TurkishText.Fold(s));

    [Fact]
    public void Fold_is_stable_under_normalize() =>
        TurkishStrings.Sample(s => TurkishText.Fold(TurkishText.Normalize(s)) == TurkishText.Fold(s));

    [Fact]
    public void Span_and_string_overloads_agree() =>
        AnyStrings.Sample(s =>
        {
            var buffer = new char[s.Length];
            int length = TurkishText.Fold(s, buffer);
            return new string(buffer, 0, length) == TurkishText.Fold(s);
        });

    [Fact]
    public void Lowering_after_uppering_equals_lowering() =>
        TurkishStrings.Sample(s => TurkishText.ToLowerTr(TurkishText.ToUpperTr(s)) == TurkishText.ToLowerTr(s));

    [Fact]
    public void Nothing_throws_on_arbitrary_input() =>
        AnyStrings.Sample(s =>
        {
            _ = TurkishText.Fold(s);
            _ = TurkishText.Normalize(s);
            _ = TurkishText.ToUpperTr(s);
            _ = TurkishText.ToLowerTr(s);
            _ = TurkishText.ToTitleTr(s);
        });
}
