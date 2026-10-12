---
language:
  - tr
license: other
license_name: per-configuration (see below)
pretty_name: AdresTR Turkish Address Benchmark
task_categories:
  - token-classification
tags:
  - address
  - geocoding
  - turkish
  - benchmark
size_categories:
  - 1K<n<10K
configs:
  - config_name: synthetic
    data_files:
      - split: dev
        path: synthetic/dev.jsonl
      - split: test
        path: synthetic/test.jsonl
  - config_name: real
    data_files:
      - split: dev
        path: real/dev.jsonl
      - split: test
        path: real/test.jsonl
  - config_name: challenge
    data_files:
      - split: all
        path: challenge/challenge.jsonl
---

# AdresTR Turkish Address Benchmark

The first open benchmark for **Turkish free-text address parsing and resolution**, built for the open-source
[AdresTR](https://github.com/AlperCna/AdresTR) library (MIT). Each row is an address text with character spans, component
values and the official gazetteer ids (il / ilçe / mahalle) that the text determines.

| Config | Rows | What it is | License |
|---|---|---|---|
| `synthetic` | dev 1,000 · test 2,000 | Real il/ilçe/mahalle/köy names, invented streets and numbers, 4 layouts, 12 logged noise operations (ASCII, typos, glued words, missing il/ilçe …) | CC BY 4.0 |
| `real` | dev 300 · test 1,200 | Public-institution addresses from İBB and İzmir open data (no personal data) | per row (`license` field): İBB Açık Veri Lisansı, CC BY 4.0 (attribution in `real/LICENSE.md`) |
| `challenge` | 250 | Hand-written hard cases: semt names, former village names, ambiguous names, numbered streets, glued tokens, D/K ambiguity … | CC0 1.0 |

## Format

JSON Lines. Fields: `id`, `text`, `source`, `license`, `split`, `spans` (`start`/`end`/`label`), `gold` (`fields`: what the
text *says*; `il`/`ilce`/`birim`: gazetteer ids of what the text *determines*, `null` = ambiguous), `noise`, `tags`, `note`.
Full specification: [SCHEMA.md](https://github.com/AlperCna/AdresTR/blob/main/eval/SCHEMA.md). Gazetteer ids refer to
the AdresTR gazetteer snapshot `2026.10`.

## Intended use and splits

Evaluation of address parsers, normalizers and resolvers. **Tune on `dev`, report on `test`.** Metrics (field F1, exact match,
id accuracy, MRR, calibration error) are implemented in `AdresTR.Eval`; results of AdresTR, libpostal and a regex baseline:
[eval/results](https://github.com/AlperCna/AdresTR/blob/main/eval/results/README.md).

## Privacy

No row contains personal data: synthetic rows use invented streets and numbers, real rows come from published institution
addresses (no names, phone numbers of individuals or home addresses), challenge rows are written by hand with fictitious numbers.

## Citation

```
@misc{adrestr2026,
  title  = {AdresTR: an open benchmark and parser for Turkish addresses},
  author = {Can, Alper},
  year   = {2026},
  url    = {https://github.com/AlperCna/AdresTR}
}
```

## Uploading (maintainer)

`huggingface-cli login`, then `huggingface-cli upload AlperCna/turkish-address-benchmark eval . --repo-type dataset` from the
repository root after copying this card to `eval/README.md` of the upload (or upload `eval/synthetic`, `eval/real`,
`eval/challenge`, `eval/SCHEMA.md` and this file as `README.md`).
