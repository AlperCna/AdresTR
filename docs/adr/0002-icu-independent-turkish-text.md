# 0002. ICU-independent Turkish text handling

- Status: accepted · Date: 2026-10-08

## Context
Measured on .NET 10: in globalization-invariant mode (chiseled/Alpine images, Blazor WASM,
Native AOT) `ToLower(new CultureInfo("tr-TR"))` silently uses invariant rules, `Normalize()` is a
no-op and `CompareOptions.IgnoreNonSpace` never folds `ı`. JavaScript/Python lowercase `İ` to
`i` + U+0307, which then shows up in input.

## Decision
`AdresTR.Text.TurkishText` implements folding (`Fold`), casing (`ToUpperTr`, `ToLowerTr`,
`ToTitleTr`) and cleanup (`Normalize`) with hand-written tables. Library code never calls
culture-sensitive string APIs; comparisons are ordinal on folded keys.
CI runs the test suite a second time with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.

## Consequences
Identical results on every host; enables the in-browser playground and the cheapest container
images. We own a small amount of Unicode code and its tests.
