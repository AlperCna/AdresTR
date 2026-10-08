# 0007. Target net10.0 only

- Status: accepted · Date: 2026-10-08

.NET 8 and .NET 9 leave support on 2026-11-10; .NET 10 is LTS. Targeting only `net10.0` keeps the
code modern (spans, `SearchValues`, `FrozenDictionary`, source-generated regex) and CI simple.
