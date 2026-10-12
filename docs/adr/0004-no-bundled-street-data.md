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

## Update (2026-10-12)

Street names now ship as an **optional, separate package** `AdresTR.Streets` (OpenStreetMap, ODbL 1.0, 248,712 streets for 968
ilçe). The core `AdresTR` and `AdresTR.Data` packages still contain no street data; the parser only uses a `StreetIndex` when one
is passed in, to verify streets and fix street typos. OSM coverage is partial, so `StreetVerified == false` is a hint.

