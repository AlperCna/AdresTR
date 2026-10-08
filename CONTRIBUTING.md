# Contributing to AdresTR

Thanks for helping! Türkçe katkılar da memnuniyetle karşılanır.

## Ground rules

- **No personal data.** Never paste a real person's home address into issues, tests or datasets.
  Use business/public-institution addresses or change door and flat numbers.
- **Measure.** Parser changes must not lower accuracy on the dev set (`tests/AdresTR.Accuracy`, from Faz 3).
- **No culture-sensitive string APIs** in library code (`ToLower()`, `ToUpper()`, `StringComparison.CurrentCulture*`,
  `CultureInfo("tr-TR")`). Use `AdresTR.Text.TurkishText` and ordinal comparisons. See ADR-0002.

## Easy first contributions

- Add abbreviation or typo variants to `data/curated/` (one row each, with a source if possible).
- Add *semt → mahalle* aliases for your city.
- Add tricky (non-personal) examples to the challenge set.

## Development

```bash
dotnet build AdresTR.slnx
dotnet test --solution AdresTR.slnx
```

Run the suite once more without ICU, like CI does:

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test --solution AdresTR.slnx
```

Benchmarks: `dotnet run -c Release --project benchmarks/AdresTR.Benchmarks`.

## Pull requests

- Use [Conventional Commits](https://www.conventionalcommits.org/) for PR titles (`feat:`, `fix:`, `data:`, `docs:` …).
- Significant design changes get a short ADR in `docs/adr/`.
