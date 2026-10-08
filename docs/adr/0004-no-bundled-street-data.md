# 0004. Street data is not bundled

- Status: accepted · Date: 2026-10-08

## Context
The complete street list (~1.28M CSBM records) exists only in NVİ (reCAPTCHA-protected, no
license) and scraped copies (GPL or unlicensed). FSEK Ek Madde 8 database rights make bulk
redistribution risky; the data is also large and changes ~1% per half year.

## Decision
`AdresTR` and `AdresTR.Data` ship il/ilçe/mahalle/postal-code data only. Streets are parsed by
pattern (type keyword + name) and reported as `verified: false`. A later optional package or a
user-supplied file can enable street validation.

## Consequences
Clean licensing; street names are not spell-corrected against a gazetteer in v1.
