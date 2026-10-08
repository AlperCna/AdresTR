# AdresTR benchmark

The first open benchmark for Turkish free-text address parsing. It is used to measure every parser change
([ADR-0008](../docs/adr/0008-benchmark-before-parser.md)) and is published separately as a dataset.

**Results:** [results/README.md](results/README.md) · **Format:** [SCHEMA.md](SCHEMA.md)

## Sets

| Set | Rows | How it is made | Labels | License |
|---|---|---|---|---|
| `synthetic/` | dev 1,000 · test 2,000 | `AdresTR.Eval generate`: real il/ilçe/mahalle/köy names from the gazetteer, invented streets and numbers, 4 layouts, varied abbreviations, 0–3 logged noise operations | Full spans and fields; gold ids only when the text determines them | CC BY 4.0 |
| `real/` | dev 300 · test 1,200 ([SOURCES.md](real/SOURCES.md)) | Public-institution addresses from İBB and İzmir open data (no personal data) | Written il/ilçe/mahalle aligned with the publisher's structured columns; other fields only when reliably aligned | per source |
| `challenge/` | dev 83 · test 167 ([README](challenge/README.md)) | Hand-written hard cases (semt, historic names, ambiguity, numbered streets, glued tokens …) | Full | CC0 1.0 |

**Splits:** `dev` may be used for tuning and error analysis; `test` is only for reporting. Never tune on test.

**Synthetic noise operations:** `ascii`, `uppercase`, `lowercase`, `typo` (keyboard-neighbour substitution,
deletion, transposition, duplication), `glued` (`CaferağaMah.`, `147sok`), `missing-il`, `missing-ilce`,
`reordered`, `phone`, `landmark`, `broken-i` (`i smet`), `duplicate-token`. Each example records which ones were applied.

**Two kinds of gold** ([SCHEMA.md](SCHEMA.md)): `fields` measure *parsing* (what the text says), ids measure
*resolution* (what the text determines). When the text no longer identifies the unit (e.g. a "Cumhuriyet Mahallesi"
whose ilçe was dropped), the gold `birim` is `null` and a system that guesses is penalized.

`real/` rows whose text was built from structured columns have a `-tpl` source suffix; read the per-source breakdown
when comparing systems on real data.

## Metrics

| Metric | Definition |
|---|---|
| Field P / R / F1 | Per label over annotated fields. A wrong value counts as one FP and one FN. Values are compared with the rules in SCHEMA.md (case, diacritics, spacing ignored for names). |
| Micro / macro F1 | Micro: pooled counts over all fields. Macro: mean of per-field F1 over fields that occur. |
| Exact match | All annotated fields of the example are correct. Ids are reported separately, so systems without a gazetteer (libpostal, LLMs) are comparable. |
| il / ilçe accuracy | Gold id equals predicted id (null = null counts as correct) over examples where the id is annotated. |
| birim@1, birim@5, MRR | Rank of the gold unit among the system's ranked candidates (or its single prediction), over examples with a non-null gold unit. |
| ECE | Expected calibration error of the system's overall confidence against exact match, 15 equal-width bins. |
| CIs | Percentile bootstrap, 1,000 resamples, fixed seed. |

libpostal's "full-parse accuracy" corresponds to our exact match; deepparse's "accuracy" is per-sequence tag accuracy.
We report both field-level and example-level numbers to avoid that ambiguity.

## Commands

```bash
dotnet run --project tools/AdresTR.Eval -- generate      # regenerate synthetic sets (deterministic)
dotnet run --project tools/AdresTR.Eval -- validate      # schema + gazetteer consistency of every set
dotnet run --project tools/AdresTR.Eval -- report        # score all systems on all sets → results/
dotnet run --project tools/AdresTR.Eval -- run --system regex --set eval/synthetic/dev.jsonl
```

External systems (libpostal, LLMs) write predictions to `predictions/<system>/<set>-<split>.jsonl`; `report`
picks them up automatically. See [baselines/](baselines/).

## Baselines

- **regex-baseline**: a few regular expressions plus exact gazetteer lookups, the "afternoon script" floor.
- **libpostal**: [baselines/libpostal](baselines/libpostal/).
- **LLM zero-shot** (planned): model id, date, temperature 0, JSON schema and cost will be recorded with the results.

## Privacy

No row contains personal data. Real rows come from published institution addresses; phone numbers in synthetic
and challenge rows are fictitious (`555` blocks). Report any problem via an issue.
