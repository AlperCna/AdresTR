# AdresTR.AspNetCore

ASP.NET Core integration for [AdresTR](https://github.com/AlperCna/AdresTR): dependency injection and a `[TurkishAddress]` validation attribute.

```csharp
builder.Services.AddAdresTR().AddValidation();   // parser, matcher, gazetteer + minimal-API validation

public class Order
{
    [Required, TurkishAddress(RequireDoorNumber = true, MinimumConfidence = 0.8)]
    public string? Address { get; set; }
}
```

Ambiguous or unresolvable addresses fail validation with a message such as "The address is ambiguous: the neighbourhood
(mahalle) matches 30 places. Add the ilçe or il." Works with MVC model validation and .NET 10 minimal APIs.
