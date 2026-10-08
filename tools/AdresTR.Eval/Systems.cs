using System.Text.RegularExpressions;
using AdresTR.Text;

namespace AdresTR.Eval;

/// <summary>An address system that can be evaluated in-process.</summary>
internal interface IAddressSystem
{
    string Name { get; }

    Prediction Predict(EvalExample example);
}

/// <summary>
/// Deliberately naive baseline: a handful of regular expressions over the folded text plus exact gazetteer
/// lookups. It represents what a developer writes in an afternoon, and is the floor every real parser must beat.
/// </summary>
internal sealed partial class RegexBaseline(Gazetteer gazetteer) : IAddressSystem
{
    public string Name => "regex-baseline";

    public Prediction Predict(EvalExample example)
    {
        string t = TurkishText.Fold(example.Text);
        var p = new Prediction { Id = example.Id };

        if (PostalCode().Match(t) is { Success: true } pk)
        {
            p.Fields[Labels.PostaKodu] = pk.Groups[1].Value;
        }

        if (Door().Match(t) is { Success: true } door)
        {
            p.Fields[Labels.DisKapi] = door.Groups[1].Value;
        }

        if (Flat().Match(t) is { Success: true } flat)
        {
            p.Fields[Labels.Daire] = flat.Groups[1].Value;
        }

        if (Floor().Match(t) is { Success: true } floor)
        {
            p.Fields[Labels.Kat] = floor.Groups[1].Value;
        }

        if (Neighbourhood().Match(t) is { Success: true } mahalle)
        {
            p.Fields[Labels.Mahalle] = mahalle.Groups[1].Value;
        }

        if (Street().Match(t) is { Success: true } street)
        {
            p.Fields[Labels.CsbmAd] = street.Groups[1].Value;
            p.Fields[Labels.CsbmTur] = street.Groups[2].Value switch
            {
                var s when s.StartsWith("ca", StringComparison.Ordinal) || s.StartsWith("cd", StringComparison.Ordinal) => "cadde",
                var s when s.StartsWith('b') => "bulvar",
                _ => "sokak",
            };
        }

        // İlçe and il: "Kadıköy/İstanbul" at the end, otherwise the last two words.
        string[] tail;
        if (SlashTail().Match(t) is { Success: true } slash)
        {
            tail = [slash.Groups[1].Value, slash.Groups[2].Value];
        }
        else
        {
            string[] words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            tail = words.Length >= 2 ? words[^2..] : words;
        }

        if (tail.Length == 2 && gazetteer.FindProvinces(tail[1]) is [var il, ..])
        {
            p.Il = il.Entity.Plaka;
            p.Fields[Labels.Il] = il.Entity.Name;
            if (gazetteer.FindDistricts(tail[0], il.Entity.Plaka) is [var ilce])
            {
                p.Ilce = ilce.Entity.Id;
                p.Fields[Labels.Ilce] = ilce.Entity.Name;
            }
        }

        if (p.Ilce is int districtId && p.Get(Labels.Mahalle) is string m && gazetteer.FindUnits(m, districtId) is [var unit])
        {
            p.Birim = unit.Entity.Id;
            p.Fields[Labels.Mahalle] = unit.Entity.Name;
        }

        return p;
    }

    [GeneratedRegex(@"\b(\d{5})\b")]
    private static partial Regex PostalCode();

    [GeneratedRegex(@"\b(?:no|numara)\s*[:.]?\s*(\d+[a-z]?(?:/\d+[a-z]?)?)")]
    private static partial Regex Door();

    [GeneratedRegex(@"\b(?:d|daire)\s*[:.]?\s*(\d+)\b")]
    private static partial Regex Flat();

    [GeneratedRegex(@"\b(?:k|kat)\s*[:.]?\s*(\d+)\b")]
    private static partial Regex Floor();

    [GeneratedRegex(@"(\S+)\s+(?:mahallesi|mahalle|mah\.?|mh\.?)(?=\s|$|,)")]
    private static partial Regex Neighbourhood();

    [GeneratedRegex(@"(\S+)\s+(caddesi|cadde|cad\.?|cd\.?|sokagi|sokak|sok\.?|sk\.?|bulvari|bulvar|blv\.?)(?=\s|$|,)")]
    private static partial Regex Street();

    [GeneratedRegex(@"(\S+)\s*/\s*(\S+)\s*$")]
    private static partial Regex SlashTail();
}
