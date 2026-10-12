# ASP.NET Core and FluentValidation

```bash
dotnet add package AdresTR.AspNetCore --prerelease
dotnet add package AdresTR.FluentValidation --prerelease
```

```csharp
builder.Services.AddAdresTR().AddValidation();

public class Order
{
    [Required, TurkishAddress(RequireDoorNumber = true, MinimumConfidence = 0.8)]
    public string? Address { get; set; }
}

// FluentValidation
RuleFor(x => x.Address).NotEmpty().MustBeTurkishAddress(o => o.RequireDoorNumber = true);
```

[TurkishAddressOptions](xref:AdresTR.TurkishAddressOptions): `RequireUnit` (default true), `MinimumConfidence` (default 0.6),
`RequireDoorNumber`. Ambiguous addresses fail with an actionable message ("matches 30 places. Add the ilçe or il.").
Null and empty values are valid; combine with `[Required]` / `NotEmpty()`.
