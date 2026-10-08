# Samples

| Sample | What it shows |
|---|---|
| [QuickStart](QuickStart/Program.cs) | Parse a few addresses, print the canonical form, candidates and corrections. |
| [CsvCleaner](CsvCleaner/Program.cs) | Clean the address column of a CSV (e-commerce orders) and append il / ilçe / mahalle / postal code / canonical address / confidence, flagging rows that need a human check. |

```bash
dotnet run --project samples/QuickStart
dotnet run --project samples/CsvCleaner -- samples/CsvCleaner/orders.csv adres
```

The sample orders are invented and contain no personal data.
