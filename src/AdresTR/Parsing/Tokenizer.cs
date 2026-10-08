using System.Text;
using System.Text.RegularExpressions;
using AdresTR.Text;

namespace AdresTR.Parsing;

/// <summary>How a token is separated from the previous one.</summary>
internal enum BreakKind : byte
{
    /// <summary>First token.</summary>
    Start,

    /// <summary>Comma, semicolon or newline: a strong boundary.</summary>
    Section,

    /// <summary>Colon or parenthesis.</summary>
    Soft,

    /// <summary>Whitespace (possibly with a trailing period of the previous token).</summary>
    Space,

    /// <summary>Slash, hyphen, period or apostrophe without whitespace (inside a word or number group).</summary>
    Part,

    /// <summary>No separator at all: the token was split from a glued word ("147sok", "CaferağaMah").</summary>
    Glued,
}

internal enum TokenKind : byte
{
    Alpha,
    Num,
    AlphaNum,
}

/// <summary>A word or number of the input, with its position in the original text.</summary>
/// <param name="Start">Start offset in the original text.</param>
/// <param name="End">End offset (exclusive) in the original text.</param>
/// <param name="Fold">ASCII-folded lowercase form, letters and digits only.</param>
/// <param name="Kind">Letters, digits or both.</param>
/// <param name="Break">Separator before this token.</param>
/// <param name="Separator">The raw separator characters before this token.</param>
/// <param name="ApostropheSuffix">The token is a suffix after an apostrophe ("de" in "Kadıköy'de").</param>
/// <param name="Noise">The token is inside a phone number, e-mail or URL.</param>
internal sealed record Token(
    int Start, int End, string Fold, TokenKind Kind, BreakKind Break, string Separator, bool ApostropheSuffix, bool Noise)
{
    public bool IsNum => Kind == TokenKind.Num;

    public bool IsAlpha => Kind == TokenKind.Alpha;

    /// <summary>The separator before this token contains a slash ("Kadıköy/İstanbul", "17/5").</summary>
    public bool SlashBefore => Separator.Contains('/', StringComparison.Ordinal);

    /// <summary>The separator before this token contains a hyphen.</summary>
    public bool HyphenBefore => Separator.Contains('-', StringComparison.Ordinal);
}

/// <summary>Splits an address into <see cref="Token"/>s, keeping offsets into the original text.</summary>
internal static partial class Tokenizer
{
    // Long keyword suffixes that may be glued to a name: "atillamahallesi", "bagdatcaddesi".
    private static readonly string[] LongGluedSuffixes =
        ["mahallesi", "mahalle", "caddesi", "sokagi", "sokak", "bulvari", "sitesi", "apartmani", "koyu"];

    // Short ones are split only when the remaining prefix is a known gazetteer name: "caferagamah".
    private static readonly string[] ShortGluedSuffixes = ["mah", "mh", "cad", "cd", "sok", "sk", "koy", "bulv", "blv"];

    public static List<Token> Tokenize(string text, Gazetteer gazetteer)
    {
        bool[] noise = NoiseMask(text);
        var tokens = new List<Token>();
        int i = 0;
        int previousEnd = 0;
        bool afterApostrophe = false;

        while (i < text.Length)
        {
            if (!IsWordChar(text[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < text.Length && (IsWordChar(text[i]) || IsCombining(text[i])))
            {
                i++;
            }

            string separator = text[previousEnd..start];
            bool apostropheSuffix = afterApostrophe && separator.Length == 1 && IsApostrophe(separator[0]);
            afterApostrophe = i < text.Length && IsApostrophe(text[i]);

            foreach ((int s, int e) in SplitRun(text, start, i))
            {
                string fold = FoldRange(text, s, e);
                if (fold.Length == 0)
                {
                    continue;
                }

                bool first = s == start;
                string sep = first ? separator : string.Empty;
                BreakKind kind = tokens.Count == 0 ? BreakKind.Start : first ? Classify(sep) : BreakKind.Glued;
                tokens.Add(new Token(s, e, fold, KindOf(fold), kind, sep, first && apostropheSuffix, IsNoise(noise, s, e)));
            }

            previousEnd = i;
        }

        return SplitGluedKeywords(tokens, text, gazetteer);
    }

    /// <summary>Splits a run of letters and digits at letter/digit boundaries, keeping single letters with numbers ("17a", "b2").</summary>
    private static IEnumerable<(int Start, int End)> SplitRun(string text, int start, int end)
    {
        var segments = new List<(int Start, int End, bool Digit)>();
        int s = start;
        for (int k = start + 1; k <= end; k++)
        {
            if (k == end || (!IsCombining(text[k]) && char.IsDigit(text[k]) != char.IsDigit(text[s])))
            {
                segments.Add((s, k, char.IsDigit(text[s])));
                s = k;
            }
        }

        // Merge one-letter segments into the neighbouring number: "17a" → "17a", "b2" → "b2".
        var merged = new List<(int Start, int End, bool Digit)>();
        foreach (var seg in segments)
        {
            if (merged.Count > 0 && ((IsSingleLetter(seg) && merged[^1].Digit) || (IsSingleLetter(merged[^1]) && seg.Digit)))
            {
                merged[^1] = (merged[^1].Start, seg.End, true);
            }
            else
            {
                merged.Add(seg);
            }
        }

        return merged.Select(m => (m.Start, m.End));

        bool IsSingleLetter((int Start, int End, bool Digit) seg) =>
            !seg.Digit && text.AsSpan(seg.Start, seg.End - seg.Start).ToString().Count(char.IsLetter) == 1;
    }

    private static List<Token> SplitGluedKeywords(List<Token> tokens, string text, Gazetteer gazetteer)
    {
        var result = new List<Token>(tokens.Count + 2);
        foreach (Token t in tokens)
        {
            if (t.IsAlpha && !IsKnownName(t.Fold, gazetteer) && TrySplit(t, out Token? name, out Token? keyword))
            {
                result.Add(name!);
                result.Add(keyword!);
            }
            else
            {
                result.Add(t);
            }
        }

        return result;

        bool TrySplit(Token t, out Token? name, out Token? keyword)
        {
            foreach (string suffix in LongGluedSuffixes)
            {
                if (t.Fold.Length - suffix.Length >= 3 && t.Fold.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return Split(t, suffix.Length, out name, out keyword);
                }
            }

            // Short suffixes ("CaferağaMah.", "ErguvanCad.", "CEVİLİMH."): split when the prefix is a known name,
            // when the suffix starts with a capital after a lowercase letter, or when an abbreviation period follows.
            bool periodAfter = t.End < text.Length && text[t.End] == '.';
            foreach (string suffix in ShortGluedSuffixes)
            {
                if (t.Fold.Length - suffix.Length >= 3 && t.Fold.EndsWith(suffix, StringComparison.Ordinal) &&
                    (IsKnownName(t.Fold[..^suffix.Length], gazetteer) || periodAfter || CamelBoundary(t, suffix.Length)))
                {
                    return Split(t, suffix.Length, out name, out keyword);
                }
            }

            name = keyword = null;
            return false;
        }

        bool CamelBoundary(Token t, int suffixLength)
        {
            int cut = CutOffset(t, suffixLength);
            return cut > t.Start && char.IsLower(text[cut - 1]) && char.IsUpper(text[cut]);
        }

        // The original offset where the folded suffix starts (folding is 1:1 except dropped marks).
        int CutOffset(Token t, int suffixLength)
        {
            int cut = t.End;
            int remaining = suffixLength;
            while (remaining > 0 && cut > t.Start)
            {
                cut--;
                if (TurkishText.FoldChar(text[cut]) is not ('\0' or ' '))
                {
                    remaining--;
                }
            }

            return cut;
        }

        bool Split(Token t, int suffixLength, out Token? name, out Token? keyword)
        {
            int cut = CutOffset(t, suffixLength);
            name = t with { End = cut, Fold = t.Fold[..^suffixLength] };
            keyword = new Token(cut, t.End, t.Fold[^suffixLength..], TokenKind.Alpha, BreakKind.Glued, string.Empty, false, t.Noise);
            return true;
        }
    }

    private static bool IsKnownName(string key, Gazetteer gazetteer) =>
        gazetteer.FindUnitsByKey(key).Count > 0 || gazetteer.FindDistrictsByKey(key).Count > 0 || gazetteer.FindProvincesByKey(key).Count > 0;

    private static BreakKind Classify(string separator)
    {
        if (separator.AsSpan().IndexOfAny(",;\n\r") >= 0)
        {
            return BreakKind.Section;
        }

        if (separator.AsSpan().IndexOfAny(":()") >= 0)
        {
            return BreakKind.Soft;
        }

        if (separator.Any(char.IsWhiteSpace))
        {
            return BreakKind.Space;
        }

        return separator.Length == 0 ? BreakKind.Glued : BreakKind.Part;
    }

    private static string FoldRange(string text, int start, int end)
    {
        var sb = new StringBuilder(end - start);
        for (int k = start; k < end; k++)
        {
            char f = TurkishText.FoldChar(text[k]);
            if (char.IsAsciiLetterOrDigit(f))
            {
                sb.Append(f);
            }
        }

        return sb.ToString();
    }

    private static TokenKind KindOf(string fold) =>
        fold.All(char.IsAsciiDigit) ? TokenKind.Num : fold.Any(char.IsAsciiDigit) ? TokenKind.AlphaNum : TokenKind.Alpha;

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c);

    private static bool IsCombining(char c) => c is >= '̀' and <= 'ͯ';

    private static bool IsApostrophe(char c) => c is '\'' or '’' or '‘' or '`' or 'ʼ' or '´';

    private static bool IsNoise(bool[] mask, int start, int end)
    {
        for (int k = start; k < end; k++)
        {
            if (!mask[k])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Marks characters that belong to phone numbers, e-mail addresses or URLs.</summary>
    private static bool[] NoiseMask(string text)
    {
        var mask = new bool[text.Length];
        foreach (Regex regex in (ReadOnlySpan<Regex>)[Phone(), Email(), Url()])
        {
            foreach (Match m in regex.Matches(text))
            {
                Array.Fill(mask, true, m.Index, m.Length);
            }
        }

        return mask;
    }

    // 10–11 digit Turkish phone numbers in common groupings; 5-digit postal codes and short door numbers never match.
    [GeneratedRegex(@"(?<!\d)(?:\+?90[\s.\-]?|0)?\(?[2-5]\d{2}\)?[\s.\-]?\d{3}[\s.\-]?\d{2}[\s.\-]?\d{2}(?!\d)")]
    private static partial Regex Phone();

    [GeneratedRegex(@"\S+@\S+\.\w+")]
    private static partial Regex Email();

    [GeneratedRegex(@"(?:https?://|www\.)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Url();
}
