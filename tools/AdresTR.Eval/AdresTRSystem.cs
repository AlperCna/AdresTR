namespace AdresTR.Eval;

/// <summary>AdresTR's own parser, evaluated in-process.</summary>
internal sealed class AdresTRSystem(AddressParser parser) : IAddressSystem
{
    public string Name => "adrestr";

    public Prediction Predict(EvalExample example)
    {
        ParseResult r = parser.Parse(example.Text);
        var p = new Prediction
        {
            Id = example.Id,
            Il = r.Province?.Plaka,
            Ilce = r.District?.Id,
            Birim = r.Unit?.Id,
            Confidence = r.Confidence,
        };

        p.Fields[Labels.Il] = r.Il?.Value;
        p.Fields[Labels.Ilce] = r.Ilce?.Value;
        p.Fields[Labels.Mahalle] = r.Mahalle?.Value;
        p.Fields[Labels.Semt] = r.Semt?.Value;
        p.Fields[Labels.CsbmTur] = r.StreetType switch
        {
            StreetType.Cadde => "cadde",
            StreetType.Sokak => "sokak",
            StreetType.Bulvar => "bulvar",
            StreetType.Yol => "yol",
            StreetType.Meydan => "meydan",
            StreetType.Cikmaz => "cikmaz",
            StreetType.KumeEvler => "kume_evler",
            _ => null,
        };
        p.Fields[Labels.CsbmAd] = r.Street?.Value;
        p.Fields[Labels.Site] = r.Site?.Value;
        p.Fields[Labels.Blok] = r.Blok?.Value;
        p.Fields[Labels.DisKapi] = r.DoorNumber?.Value;
        p.Fields[Labels.Kat] = r.Floor?.Value;
        p.Fields[Labels.Daire] = r.Flat?.Value;
        p.Fields[Labels.PostaKodu] = r.PostalCode?.Value;
        p.Fields[Labels.Tarif] = r.Landmark?.Value;
        foreach ((SettlementUnit unit, double score) in r.UnitCandidates.Take(10))
        {
            p.Candidates.Add(new Candidate(unit.Id, score));
        }

        return p;
    }
}
