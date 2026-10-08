namespace AdresTR.Text;

/// <summary>
/// Culture-independent Turkish text helpers.
/// </summary>
/// <remarks>
/// Nothing in this class depends on ICU, <see cref="System.Globalization.CultureInfo.CurrentCulture"/>
/// or <c>tr-TR</c> culture data. That matters because in globalization-invariant mode (chiseled/Alpine
/// containers, Blazor WebAssembly, Native AOT) <c>ToLower(new CultureInfo("tr-TR"))</c> silently falls
/// back to invariant casing and <see cref="string.Normalize()"/> becomes a no-op.
/// All Turkish-specific mappings (I/ı, İ/i, ç, ğ, ö, ş, ü) are hand-written here instead.
/// </remarks>
public static class TurkishText
{
    private const char CombiningDotAbove = '\u0307';
    private const char Drop = '\0';
    private const char Space = ' ';

    /// <summary>
    /// Produces the ASCII-folded matching key of <paramref name="text"/>: lowercase, Turkish letters folded
    /// to ASCII (ç→c, ğ→g, ı/İ/I→i, ö→o, ş→s, ü→u), diacritics and invisible characters removed,
    /// apostrophes and dashes unified, whitespace collapsed and trimmed.
    /// </summary>
    /// <example><c>Fold("  KADIKÖY’de\u00A0Şişli ")</c> returns <c>"kadikoy'de sisli"</c>.</example>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        Span<char> buffer = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
        int length = Fold(text, buffer);
        return length == text.Length && buffer[..length].SequenceEqual(text) ? text : new string(buffer[..length]);
    }

    /// <summary>
    /// Allocation-free variant of <see cref="Fold(string?)"/>. Folding never lengthens text, so a
    /// <paramref name="destination"/> as long as <paramref name="source"/> is always sufficient.
    /// </summary>
    /// <returns>The number of characters written to <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than <paramref name="source"/>.</exception>
    public static int Fold(ReadOnlySpan<char> source, Span<char> destination)
    {
        if (destination.Length < source.Length)
        {
            throw new ArgumentException("Destination must be at least as long as the source.", nameof(destination));
        }

        int written = 0;
        bool pendingSpace = false;

        foreach (char c in source)
        {
            char folded = FoldChar(c);
            if (folded == Drop)
            {
                continue;
            }

            if (folded == Space)
            {
                pendingSpace = written > 0;
                continue;
            }

            if (pendingSpace)
            {
                destination[written++] = Space;
                pendingSpace = false;
            }

            destination[written++] = folded;
        }

        return written;
    }

    /// <summary>Compares two strings by their <see cref="Fold(string?)"/> keys.</summary>
    public static bool EqualsFolded(string? a, string? b) =>
        string.Equals(Fold(a), Fold(b), StringComparison.Ordinal);

    /// <summary>
    /// Cleans text for display while preserving case and Turkish letters: composes decomposed Turkish
    /// letters (s + U+0327 → ş, I + U+0307 → İ, …), repairs the <c>i\u0307</c> artifact that JavaScript/Python
    /// produce when lowercasing <c>İ</c>, replaces Romanian look-alikes (ș, ț), removes invisible
    /// characters, unifies apostrophes and dashes, and collapses whitespace.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = new System.Text.StringBuilder(text.Length);
        bool pendingSpace = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            char next = i + 1 < text.Length ? text[i + 1] : Drop;

            char composed = Compose(c, next);
            if (composed != Drop)
            {
                c = composed;
                i++;
            }
            else
            {
                c = NormalizeChar(c);
            }

            if (c == Drop)
            {
                continue;
            }

            if (c == Space)
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                result.Append(Space);
                pendingSpace = false;
            }

            result.Append(c);
        }

        return result.ToString();
    }

    /// <summary>Turkish lowercase: I → ı, İ → i; everything else uses invariant rules.</summary>
    public static string ToLowerTr(string? text) => ChangeCase(text, upper: false);

    /// <summary>Turkish uppercase: i → İ, ı → I; everything else uses invariant rules.</summary>
    public static string ToUpperTr(string? text) => ChangeCase(text, upper: true);

    /// <summary>
    /// Turkish title case for place names: <c>"İSTANBUL"</c> → <c>"İstanbul"</c>, <c>"ığdır"</c> → <c>"Iğdır"</c>,
    /// <c>"KADIKÖY'DE"</c> → <c>"Kadıköy'de"</c>. A word starts after whitespace, '-', '/', '(' or '"';
    /// letters after an apostrophe stay lowercase.
    /// </summary>
    public static string ToTitleTr(string? text)
    {
        string lower = ToLowerTr(text);
        if (lower.Length == 0)
        {
            return lower;
        }

        return string.Create(lower.Length, lower, static (span, source) =>
        {
            bool wordStart = true;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                span[i] = wordStart ? UpperChar(c) : c;
                wordStart = char.IsWhiteSpace(c) || c is '-' or '/' or '(' or '"';
            }
        });
    }

    private static string ChangeCase(string? text, bool upper)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // Any i-variant followed by a combining dot above ("i\u0307", "İ" decomposed, even "ı\u0307") is a dotted i.
            if (c is 'i' or 'I' or 'ı' or 'İ' && i + 1 < text.Length && text[i + 1] == CombiningDotAbove)
            {
                result.Append(upper ? 'İ' : 'i');
                while (i + 1 < text.Length && text[i + 1] == CombiningDotAbove)
                {
                    i++;
                }

                continue;
            }

            result.Append(upper ? UpperChar(c) : LowerChar(c));
        }

        return result.ToString();
    }

    private static char UpperChar(char c) => c switch
    {
        'i' => 'İ',
        'ı' => 'I',
        _ => char.ToUpperInvariant(c),
    };

    private static char LowerChar(char c) => c switch
    {
        'I' => 'ı',
        'İ' => 'i',
        _ => char.ToLowerInvariant(c),
    };

    /// <summary>Maps one character to its folded form, <see cref="Space"/> for whitespace or <see cref="Drop"/> to remove it.</summary>
    private static char FoldChar(char c)
    {
        if (c < 0x80)
        {
            if (c is >= 'A' and <= 'Z')
            {
                return (char)(c | 0x20);
            }

            if (c is ' ' or '\t' or '\n' or '\r' or '\f' or '\v')
            {
                return Space;
            }

            if (c == '`')
            {
                return '\'';
            }

            return c < 0x20 || c == 0x7F ? Drop : c;
        }

        switch (c)
        {
            case 'ç' or 'Ç':
                return 'c';
            case 'ğ' or 'Ğ':
                return 'g';
            case 'ı' or 'İ' or 'î' or 'Î' or 'ì' or 'Ì' or 'í' or 'Í' or 'ï' or 'Ï':
                return 'i';
            case 'ö' or 'Ö' or 'ô' or 'Ô' or 'ò' or 'Ò' or 'ó' or 'Ó' or 'õ' or 'Õ':
                return 'o';
            case 'ş' or 'Ş' or 'ș' or 'Ș':
                return 's';
            case 'ü' or 'Ü' or 'û' or 'Û' or 'ù' or 'Ù' or 'ú' or 'Ú':
                return 'u';
            case 'â' or 'Â' or 'à' or 'À' or 'á' or 'Á' or 'ä' or 'Ä' or 'ã' or 'Ã' or 'å' or 'Å':
                return 'a';
            case 'é' or 'É' or 'è' or 'È' or 'ê' or 'Ê' or 'ë' or 'Ë':
                return 'e';
            case 'ț' or 'Ț':
                return 't';
            case 'ñ' or 'Ñ':
                return 'n';
        }

        char normalized = NormalizeChar(c);
        if (normalized != c)
        {
            return normalized;
        }

        if (c is >= '\u0300' and <= 'ͯ')
        {
            return Drop;
        }

        return char.ToLowerInvariant(c);
    }

    /// <summary>Case-preserving cleanup shared by <see cref="Normalize"/> and <see cref="FoldChar"/>.</summary>
    private static char NormalizeChar(char c)
    {
        switch (c)
        {
            case '\u00AD' or '\u200B' or '\u200C' or '\u200D' or '\u2060' or '\uFEFF':
                return Drop;
            case '‘' or '’' or 'ʼ' or '´' or '′' or '`':
                return '\'';
            case '“' or '”' or '„' or '«' or '»':
                return '"';
            case >= '‐' and <= '―' or '−':
                return '-';
            case 'ș':
                return 'ş';
            case 'Ș':
                return 'Ş';
            case 'ț':
                return 't';
            case 'Ț':
                return 'T';
        }

        if (c is >= '\u0300' and <= 'ͯ')
        {
            return Drop;
        }

        if (char.IsWhiteSpace(c))
        {
            return Space;
        }

        return char.IsControl(c) ? Drop : c;
    }

    /// <summary>Composes a base letter and a combining mark into a precomposed Turkish letter, or returns <see cref="Drop"/>.</summary>
    private static char Compose(char letter, char mark) => (letter, mark) switch
    {
        ('c', '\u0327') => 'ç',
        ('C', '\u0327') => 'Ç',
        ('s', '\u0327') => 'ş',
        ('S', '\u0327') => 'Ş',
        ('g', '\u0306') => 'ğ',
        ('G', '\u0306') => 'Ğ',
        ('o', '\u0308') => 'ö',
        ('O', '\u0308') => 'Ö',
        ('u', '\u0308') => 'ü',
        ('U', '\u0308') => 'Ü',
        ('I' or 'İ', CombiningDotAbove) => 'İ',
        ('i' or 'ı', CombiningDotAbove) => 'i',
        ('a', '\u0302') => 'â',
        ('A', '\u0302') => 'Â',
        ('i', '\u0302') => 'î',
        ('I', '\u0302') => 'Î',
        ('u', '\u0302') => 'û',
        ('U', '\u0302') => 'Û',
        _ => Drop,
    };
}
