using AdresTR.Parsing;
using AdresTR.Text;

namespace AdresTR;

/// <summary>
/// Parses Turkish free-text addresses into components, resolves il/ilçe/mahalle against a <see cref="Gazetteer"/>
/// and explains every correction. Rule-based, deterministic and offline (ADR-0001).
/// </summary>
/// <remarks>Instances are immutable and thread-safe; create one per gazetteer and reuse it.</remarks>
public sealed class AddressParser
{
    /// <summary>Inputs longer than this are truncated (protects against pathological input).</summary>
    public const int MaxInputLength = 512;

    /// <summary>
    /// Softmax temperature over the N-best scores, fitted on the dev splits of eval/ (minimum expected calibration
    /// error); see <c>AdresTR.Eval calibrate</c>.
    /// </summary>
    internal const double DefaultTemperature = 0.08;

    private readonly Gazetteer _gazetteer;

    internal double Temperature { get; }

    /// <summary>Creates a parser over the given gazetteer (e.g. <c>TurkishGazetteer.Default</c> from AdresTR.Data).</summary>
    public AddressParser(Gazetteer gazetteer)
        : this(gazetteer, DefaultTemperature)
    {
    }

    internal AddressParser(Gazetteer gazetteer, double temperature)
    {
        Temperature = temperature;
        ArgumentNullException.ThrowIfNull(gazetteer);
        _gazetteer = gazetteer;
        _ = gazetteer.ParserIndex;
    }

    /// <summary>Parses one address.</summary>
    public ParseResult Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string input = text.Length > MaxInputLength ? text[..MaxInputLength] : text;
        var result = new ParseResult(text);

        List<Token> tokens = Tokenizer.Tokenize(input, _gazetteer);
        if (tokens.Count == 0)
        {
            return result;
        }

        List<Hyp>[] hyps = new Classifier(_gazetteer, input, tokens).Run();
        List<Resolution> resolutions = [.. Solver.Solve(tokens, hyps).Select(p => Resolve(p, tokens)).OrderByDescending(r => r.Score)];
        if (resolutions.Count == 0)
        {
            return result;
        }

        Fill(result, resolutions, input, tokens);
        return result;
    }

    private Resolution Resolve(Parsing.Path path, IReadOnlyList<Token> tokens)
    {
        var r = new Resolution(path);
        List<Hyp> hyps = [.. path.Hyps()];
        r.Il = hyps.Find(h => h.Role == Role.Il);
        r.Ilce = hyps.Find(h => h.Role == Role.Ilce);
        r.Mahalle = hyps.Find(h => h.Role == Role.Mahalle);
        r.Semt = hyps.Find(h => h.Role == Role.Semt);
        r.Postal = hyps.Find(h => h.Role == Role.Postal);
        Hyp? unitHyp = r.Mahalle ?? r.Semt;
        double score = path.Score;

        // İl and ilçe must agree.
        HashSet<Province>? provinces = r.Il is null ? null : [.. r.Il.Provinces];
        List<District>? districts = r.Ilce?.Districts.ToList();
        if (districts is not null && provinces is not null)
        {
            List<District> consistent = districts.FindAll(d => provinces.Contains(d.Province));
            if (consistent.Count == 0)
            {
                score -= 2.5;
                r.Conflict = true;
                districts = null;
            }
            else
            {
                districts = consistent;
                score += 0.5;
            }
        }

        // Postal code narrows the province when the text does not name it.
        Province? postalProvince = r.Postal?.Provinces.FirstOrDefault();
        if (r.Postal is not null && postalProvince is not null)
        {
            if (provinces is not null)
            {
                if (provinces.Contains(postalProvince))
                {
                    score += 0.3;
                }
                else
                {
                    score -= 1.0;
                    r.PostalConflict = true;
                }
            }
            else if (districts is not null)
            {
                List<District> inPostal = districts.FindAll(d => d.Province == postalProvince);
                if (inPostal.Count > 0)
                {
                    districts = inPostal;
                    score += 0.3;
                }
            }
        }

        // Units.
        List<SettlementUnit> units = [];
        if (unitHyp is not null)
        {
            units = [.. unitHyp.Units.Where(u =>
                (provinces is null || provinces.Contains(u.District.Province)) &&
                (districts is null || districts.Contains(u.District)) &&
                (provinces is not null || districts is not null || postalProvince is null || u.District.Province == postalProvince))];

            if (unitHyp.Units.Length > 0 && units.Count == 0)
            {
                // The name exists, but not where the text says: probably a typo of a local name, or a wrong il/ilçe.
                score -= 1.5;
                r.Conflict = true;
            }

            if (units.Count == 0 && unitHyp.Role == Role.Mahalle)
            {
                units = FuzzyUnits(unitHyp, provinces, districts, r, ref score);
            }

            if (r.Postal is not null && units.Count > 1 && units.FindAll(u => u.PostalCode == r.Postal.Value) is { Count: > 0 } byCode)
            {
                units = byCode;
                score += 0.3;
            }

            units = PreferKinds(units, unitHyp.PreferredKind);
            if (units.Count > 0 && (provinces is not null || districts is not null))
            {
                score += 0.6;
            }

            if (units.Count > 1)
            {
                score -= Math.Min(0.6, 0.12 * Math.Log2(units.Count));
            }

            if (units.Count == 1 && units[0].Kind is UnitKind.Mevki or UnitKind.Mezra or UnitKind.Yayla or UnitKind.Diger && !unitHyp.KeywordSupported)
            {
                score -= 0.3;
            }
        }

        // Districts and provinces implied by the units.
        List<District> finalDistricts = units.Count > 0 ? [.. units.Select(u => u.District).Distinct()] : districts ?? [];
        if (districts is not null && units.Count > 0)
        {
            finalDistricts = finalDistricts.FindAll(districts.Contains);
        }

        List<Province> finalProvinces = provinces is not null
            ? [.. provinces]
            : finalDistricts.Count > 0
                ? [.. finalDistricts.Select(d => d.Province).Distinct()]
                : postalProvince is not null ? [postalProvince] : [];

        if (r.Ilce is not null && finalDistricts.Count > 1)
        {
            score -= 0.2 * Math.Log2(finalDistricts.Count);
        }

        // Very short district names ("Of", "Çay", "Han") need context.
        if (r.Ilce is not null && r.Ilce.Key.Length <= 3 && r.Il is null && units.Count == 0)
        {
            score -= 1.0;
        }

        score -= AdminOrderPenalty(hyps, r);

        r.Units = units;
        r.Unit = units.Count == 1 ? units[0] : null;
        r.District = finalDistricts.Count == 1 ? finalDistricts[0] : null;
        r.Province = finalProvinces.Count == 1 ? finalProvinces[0] : null;
        r.Score = score;
        return r;
    }

    private List<SettlementUnit> FuzzyUnits(Hyp unitHyp, HashSet<Province>? provinces, List<District>? districts, Resolution r, ref double score)
    {
        ParserIndex index = _gazetteer.ParserIndex;
        IEnumerable<(string Key, SettlementUnit Entity)> scope =
            districts is { Count: > 0 } ? districts.SelectMany(d => index.UnitsByDistrict[d.Id])
            : provinces is { Count: 1 } ? index.UnitsByProvince[provinces.First().Plaka]
            : r.Postal is not null ? _gazetteer.FindUnitsByPostalCode(r.Postal.Value).Select(u => (AdresTR.Gazetteer.Key(u.Name), u))
            : [];

        List<(SettlementUnit Entity, int Distance)> found = Fuzzy.Search(unitHyp.Key, [.. scope], scoped: true);
        if (found.Count == 0)
        {
            return [];
        }

        int best = found[0].Distance;
        r.UnitFuzzy = best;
        score -= 0.5 * best;
        return [.. found.Where(f => f.Distance == best).Select(f => f.Entity)];
    }

    /// <summary>Prefers the unit kind named by the keyword, and real settlements over mevkii/mezra with the same name.</summary>
    private static List<SettlementUnit> PreferKinds(List<SettlementUnit> units, UnitKind? preferred)
    {
        if (units.Count <= 1)
        {
            return units;
        }

        if (preferred is UnitKind kind && units.FindAll(u => u.Kind == kind) is { Count: > 0 } exact)
        {
            return exact;
        }

        List<SettlementUnit> settlements = units.FindAll(u => u.Kind is UnitKind.Mahalle or UnitKind.Koy or UnitKind.Osb);
        return settlements.Count > 0 ? settlements : units;
    }

    /// <summary>İl/ilçe belong at the end of the address, or all together at the start.</summary>
    private static double AdminOrderPenalty(List<Hyp> hyps, Resolution r)
    {
        List<Hyp> local = hyps.FindAll(h => h.Role is Role.Mahalle or Role.Semt or Role.Street or Role.Site or Role.Blok or Role.Door or Role.Floor or Role.Flat);
        if (local.Count == 0)
        {
            return r.Il is not null && r.Ilce is not null && r.Il.From < r.Ilce.From ? 0.2 : 0;
        }

        int localStart = local.Min(h => h.From);
        int localEnd = local.Max(h => h.To);
        double penalty = 0;
        bool head = false;
        foreach (Hyp admin in new[] { r.Ilce, r.Il }.OfType<Hyp>())
        {
            if (admin.To <= localStart)
            {
                head = true;
            }
            else if (admin.From < localEnd)
            {
                penalty += 0.4;
            }
        }

        if (head)
        {
            penalty += 0.15;
        }

        if (r.Il is not null && r.Ilce is not null)
        {
            bool tail = r.Il.From >= localEnd && r.Ilce.From >= localEnd;
            if (tail && r.Il.From < r.Ilce.From)
            {
                penalty += 0.3;
            }
        }

        return penalty;
    }

    private void Fill(ParseResult result, List<Resolution> resolutions, string input, IReadOnlyList<Token> tokens)
    {
        Resolution best = resolutions[0];

        // Confidence: softmax over the N-best scores.
        // Confidence: softmax over the N-best scores, summed over all paths that produce the same output.
        double max = best.Score;
        double[] weights = [.. resolutions.Select(r => Math.Exp((r.Score - max) / Temperature))];
        double total = weights.Sum();
        string bestOutput = OutputSignature(best);
        double same = resolutions.Select((r, k) => OutputSignature(r) == bestOutput ? weights[k] : 0).Sum();
        result.Confidence = Math.Round(same / total, 4);

        var candidates = new Dictionary<SettlementUnit, double>();
        for (int k = 0; k < resolutions.Count; k++)
        {
            double p = weights[k] / total;
            List<SettlementUnit> units = resolutions[k].Units;
            foreach (SettlementUnit u in units.Take(10))
            {
                candidates[u] = candidates.GetValueOrDefault(u) + (p / Math.Min(units.Count, 10));
            }
        }

        result.UnitCandidates = [.. candidates.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Id).Select(kv => (kv.Key, Math.Round(kv.Value, 4)))];
        result.Province = best.Province;
        result.District = best.District;
        result.Unit = best.Unit;

        var corrections = new List<Correction>();
        List<Hyp> hyps = [.. best.Path.Hyps()];
        foreach (Hyp h in hyps)
        {
            string written = input[h.TextStart..h.TextEnd];
            var component = new AddressComponent(h.Value, written, h.TextStart, h.TextEnd);
            switch (h.Role)
            {
                case Role.Il:
                    result.Il = component with { Value = best.Province?.Name ?? h.Value };
                    AdminCorrections(corrections, "il", written, result.Il.Value, h);
                    break;
                case Role.Ilce:
                    result.Ilce = component with { Value = best.District?.Name ?? h.Value };
                    AdminCorrections(corrections, "ilce", written, result.Ilce.Value, h);
                    break;
                case Role.Mahalle:
                    string name = best.Unit?.Name ?? (best.Units.Count > 0 && best.Units.All(u => u.Name == best.Units[0].Name) ? best.Units[0].Name : h.Value);
                    result.Mahalle = component with { Value = name };
                    AdminCorrections(corrections, "mahalle", written, name, h, best.UnitFuzzy);

                    // Law 6360: the text says "köy", but the unit is a mahalle today.
                    if (h.Alias is null && h.PreferredKind == UnitKind.Koy && best.Unit?.Kind == UnitKind.Mahalle)
                    {
                        corrections.Add(new Correction(CorrectionKind.HistoricName, "mahalle", input[h.TextStart..tokens[h.To - 1].End], $"{best.Unit.Name} Mahallesi"));
                    }
                    break;
                case Role.Semt:
                    result.Semt = component;
                    if (best.Unit is not null)
                    {
                        corrections.Add(new Correction(CorrectionKind.Semt, "mahalle", written, best.Unit.Name));
                    }

                    break;
                case Role.Street:
                    result.Street = component;
                    result.StreetType = h.StreetType;
                    break;
                case Role.Site:
                    result.Site = component;
                    break;
                case Role.Blok:
                    result.Blok = component;
                    break;
                case Role.Door:
                    result.DoorNumber = component;
                    if (h.FlatValue is not null)
                    {
                        result.Flat = new AddressComponent(h.FlatValue, input[h.FlatStart..h.FlatEnd], h.FlatStart, h.FlatEnd);
                    }

                    break;
                case Role.Floor:
                    result.Floor = component;
                    break;
                case Role.Flat:
                    result.Flat = component;
                    break;
                case Role.Postal:
                    result.PostalCode = component;
                    break;
                case Role.Landmark:
                    result.Landmark = component;
                    break;
                case Role.Ignore when h.Key.Length > 0:
                    corrections.Add(new Correction(CorrectionKind.Duplicate, "-", written, written));
                    break;
            }

            if (Enumerable.Range(h.From, h.To - h.From).Any(k => tokens[k].Break == BreakKind.Glued && k > h.From))
            {
                corrections.Add(new Correction(CorrectionKind.Split, h.Role.ToString().ToLowerInvariant(), input[tokens[h.From].Start..tokens[h.To - 1].End], h.Value));
            }
        }

        if (best.Province is not null && best.Il is null)
        {
            corrections.Add(new Correction(CorrectionKind.Inferred, "il", null, best.Province.Name));
        }

        if (best.District is not null && best.Ilce is null)
        {
            corrections.Add(new Correction(CorrectionKind.Inferred, "ilce", null, best.District.Name));
        }

        if (best.PostalConflict && best.Postal is not null)
        {
            corrections.Add(new Correction(CorrectionKind.PostalCodeConflict, "posta_kodu", best.Postal.Value, best.Province?.Name ?? string.Empty));
        }

        result.Corrections = corrections;
    }

    /// <summary>What a resolution outputs: component values and resolved ids (paths differing only internally are equal).</summary>
    private static string OutputSignature(Resolution r) =>
        string.Join('|', r.Path.Hyps().Where(h => h.Role is not (Role.Noise or Role.Ignore)).OrderBy(h => h.Role).Select(h => $"{h.Role}={h.Value}/{h.FlatValue}")) +
        $"#{r.Province?.Plaka}/{r.District?.Id}/{r.Unit?.Id}";

    private static void AdminCorrections(List<Correction> corrections, string field, string written, string official, Hyp h, int unitFuzzy = 0)
    {
        if (h.Alias is { } alias)
        {
            CorrectionKind kind = alias.Kind switch
            {
                AliasKind.Semt => CorrectionKind.Semt,
                AliasKind.Tarihsel => CorrectionKind.HistoricName,
                _ => CorrectionKind.Alias,
            };
            corrections.Add(new Correction(kind, field, written, official));
        }
        else if (h.Fuzzy > 0 || unitFuzzy > 0)
        {
            corrections.Add(new Correction(CorrectionKind.Typo, field, written, official));
        }
        else if (!string.Equals(TurkishText.ToLowerTr(written), TurkishText.ToLowerTr(official), StringComparison.Ordinal) &&
                 TurkishText.EqualsFolded(written, official))
        {
            corrections.Add(new Correction(CorrectionKind.Diacritics, field, written, official));
        }
    }

    /// <summary>One structural assignment with its administrative resolution.</summary>
    private sealed class Resolution(Parsing.Path path)
    {
        public Parsing.Path Path { get; } = path;

        public double Score { get; set; }

        public Hyp? Il { get; set; }

        public Hyp? Ilce { get; set; }

        public Hyp? Mahalle { get; set; }

        public Hyp? Semt { get; set; }

        public Hyp? Postal { get; set; }

        public List<SettlementUnit> Units { get; set; } = [];

        public SettlementUnit? Unit { get; set; }

        public District? District { get; set; }

        public Province? Province { get; set; }

        public int UnitFuzzy { get; set; }

        public bool Conflict { get; set; }

        public bool PostalConflict { get; set; }
    }
}
