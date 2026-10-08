using System.Globalization;
using AdresTR.Text;

namespace AdresTR.Tests.Text;

public class TurkishTextTests
{
    [Theory]
    [InlineData("İSTANBUL", "istanbul")]
    [InlineData("ISTANBUL", "istanbul")]
    [InlineData("istanbul", "istanbul")]
    [InlineData("i̇stanbul", "istanbul")] // JS/Python "İSTANBUL".toLowerCase()
    [InlineData("İSTANBUL", "istanbul")] // decomposed İ
    [InlineData("Kadıköy", "kadikoy")]
    [InlineData("KADIKÖY", "kadikoy")]
    [InlineData("Şişli", "sisli")]
    [InlineData("Şişli", "sisli")] // decomposed ş
    [InlineData("Șișli", "sisli")] // Romanian s-comma look-alike
    [InlineData("Çağlayan", "caglayan")]
    [InlineData("Iğdır", "igdir")]
    [InlineData("Kâğıthane", "kagithane")]
    [InlineData("Ünye", "unye")]
    [InlineData("  Moda  Cd. ", "moda cd.")]
    [InlineData("a\tb\r\nc", "a b c")]
    [InlineData("Kadıköy’de", "kadikoy'de")]
    [InlineData("Ka​dı­köy", "kadikoy")]
    [InlineData("No:12/3", "no:12/3")]
    [InlineData("", "")]
    public void Fold_produces_ascii_matching_key(string input, string expected) =>
        Assert.Equal(expected, TurkishText.Fold(input));

    [Fact]
    public void Fold_of_null_is_empty() => Assert.Equal(string.Empty, TurkishText.Fold(null));

    [Fact]
    public void Fold_returns_same_instance_when_already_folded()
    {
        const string folded = "caferaga mah. moda cd.";
        Assert.Same(folded, TurkishText.Fold(folded));
    }

    [Fact]
    public void Fold_span_rejects_short_destination() =>
        Assert.Throws<ArgumentException>(() => TurkishText.Fold("abc".AsSpan(), new char[2]));

    [Fact]
    public void Fold_handles_long_input_without_stack_buffer()
    {
        string input = string.Concat(Enumerable.Repeat("KADIKÖY ", 100));
        Assert.Equal(string.Join(' ', Enumerable.Repeat("kadikoy", 100)), TurkishText.Fold(input));
    }

    [Theory]
    [InlineData("istanbul", "İSTANBUL")]
    [InlineData("ılgaz", "ILGAZ")]
    [InlineData("iğdır", "İĞDIR")]
    [InlineData("i̇zmir", "İZMİR")]
    [InlineData("ı̇zmir", "İZMİR")] // found by CsCheck: dotless i + combining dot
    [InlineData("İ̇zmir", "İZMİR")]
    [InlineData("çeşme", "ÇEŞME")]
    public void ToUpperTr_uses_turkish_dotted_and_dotless_i(string input, string expected) =>
        Assert.Equal(expected, TurkishText.ToUpperTr(input));

    [Theory]
    [InlineData("İSTANBUL", "istanbul")]
    [InlineData("ILGAZ", "ılgaz")]
    [InlineData("IĞDIR", "ığdır")]
    [InlineData("DİYARBAKIR", "diyarbakır")]
    [InlineData("İZMİR", "izmir")]
    [InlineData("ı̇zmir", "izmir")]
    [InlineData("İ̇ZMİR", "izmir")]
    public void ToLowerTr_uses_turkish_dotted_and_dotless_i(string input, string expected) =>
        Assert.Equal(expected, TurkishText.ToLowerTr(input));

    [Theory]
    [InlineData("İSTANBUL", "İstanbul")]
    [InlineData("ığdır", "Iğdır")]
    [InlineData("19 MAYIS", "19 Mayıs")]
    [InlineData("KADIKÖY'DE", "Kadıköy'de")]
    [InlineData("afyonkarahisar-merkez", "Afyonkarahisar-Merkez")]
    [InlineData("caferağa mah.", "Caferağa Mah.")]
    [InlineData("kadıköy/istanbul", "Kadıköy/İstanbul")]
    [InlineData("", "")]
    public void ToTitleTr_capitalizes_words_with_turkish_rules(string input, string expected) =>
        Assert.Equal(expected, TurkishText.ToTitleTr(input));

    [Theory]
    [InlineData("Şişli", "Şişli")]
    [InlineData("Çankaya", "Çankaya")]
    [InlineData("Ağri", "Ağri")]
    [InlineData("i̇stanbul", "istanbul")]
    [InlineData("İSTANBUL", "İSTANBUL")]
    [InlineData("ı̇zmir", "izmir")]
    [InlineData("Ka​dıköy", "Kadıköy")]
    [InlineData("  a   b  ", "a b")]
    [InlineData("Kadıköy’de", "Kadıköy'de")]
    [InlineData("Șișli", "Şişli")]
    [InlineData("Gazi–Mustafa", "Gazi-Mustafa")]
    public void Normalize_repairs_turkish_text_but_keeps_case(string input, string expected) =>
        Assert.Equal(expected, TurkishText.Normalize(input));

    [Theory]
    [InlineData("KADIKÖY", "kadikoy")]
    [InlineData("Şişli", "SISLI")]
    [InlineData("İstanbul", "ISTANBUL")]
    public void EqualsFolded_ignores_case_and_diacritics(string a, string b) =>
        Assert.True(TurkishText.EqualsFolded(a, b));

    [Fact]
    public void Results_do_not_depend_on_current_culture()
    {
        CultureInfo turkish;
        try
        {
            turkish = new CultureInfo("tr-TR");
        }
        catch (CultureNotFoundException)
        {
            Assert.Skip("tr-TR culture is unavailable (globalization-invariant mode).");
            return;
        }

        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = turkish;
            Assert.Equal("istanbul", TurkishText.Fold("ISTANBUL"));
            Assert.Equal("İSTANBUL", TurkishText.ToUpperTr("istanbul"));
            Assert.Equal("ılgaz", TurkishText.ToLowerTr("ILGAZ"));

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal("istanbul", TurkishText.Fold("ISTANBUL"));
            Assert.Equal("İSTANBUL", TurkishText.ToUpperTr("istanbul"));
            Assert.Equal("ılgaz", TurkishText.ToLowerTr("ILGAZ"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
