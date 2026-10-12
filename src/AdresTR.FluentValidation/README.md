# AdresTR.FluentValidation

A [FluentValidation](https://fluentvalidation.net) rule for Turkish addresses, powered by [AdresTR](https://github.com/AlperCna/AdresTR).

```csharp
RuleFor(x => x.Address).NotEmpty().MustBeTurkishAddress(o => o.RequireDoorNumber = true);
```

Defaults: the address must resolve to a single mahalle/köy with confidence ≥ 0.6. Null/empty values pass (use `NotEmpty()`).
