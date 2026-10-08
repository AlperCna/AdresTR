# Evaluation data format (contract)

All evaluation sets are UTF-8 JSON Lines (`.jsonl`, one object per line, LF). This file is the contract between
the dataset builders (`data/scripts/`, `tools/AdresTR.Eval generate`) and the evaluator (`tools/AdresTR.Eval run`).

## Example

```json
{
  "id": "syn-dev-000042",
  "text": "Caferağa Mah. Moda Cad. No:12 D:3 Kadıköy/İstanbul",
  "source": "synthetic",
  "license": "CC-BY-4.0",
  "split": "dev",
  "spans": [
    {"start": 0,  "end": 8,  "label": "mahalle"},
    {"start": 14, "end": 18, "label": "csbm_ad"},
    {"start": 19, "end": 23, "label": "csbm_tur"},
    {"start": 27, "end": 29, "label": "dis_kapi"},
    {"start": 32, "end": 33, "label": "daire"},
    {"start": 34, "end": 41, "label": "ilce"},
    {"start": 42, "end": 50, "label": "il"}
  ],
  "gold": {
    "il": 34, "ilce": 3423, "birim": 34230005,
    "fields": {
      "il": "İstanbul", "ilce": "Kadıköy", "mahalle": "Caferağa",
      "csbm_tur": "cadde", "csbm_ad": "Moda",
      "dis_kapi": "12", "daire": "3", "kat": null, "blok": null, "site": null,
      "posta_kodu": null
    }
  },
  "noise": ["abbreviation"],
  "tags": []
}
```

## Fields

| Field | Type | Required | Meaning |
|---|---|---|---|
| `id` | string | ✓ | Unique within the repository. Prefix by set: `syn-`, `real-<source>-`, `chal-`, `osm-`. |
| `text` | string | ✓ | The raw address exactly as given to the parser. Never normalized. |
| `source` | string | ✓ | Dataset tag (`synthetic`, `ibb-saglik`, `izmir-eczane`, `challenge`, …). |
| `license` | string | ✓ | SPDX-like id of the row's license (`CC-BY-4.0`, `CC0-1.0`, `ODbL-1.0`, `IBB-Acik-Veri`). |
| `split` | `"dev"` \| `"test"` | ✓ | Test rows are never used for tuning. |
| `spans` | array | – | Character spans `[start, end)` (UTF-16 code units, like .NET `string`) with a `label` from the label set. Omit when not span-annotated. |
| `gold.il` / `gold.ilce` / `gold.birim` | int \| null | – | AdresTR gazetteer ids (`data/staging`) of what the **text determines** (see below). `null` = the text does not determine it; key absent = not annotated. |
| `gold.fields` | object | ✓ | Expected component values (see below). **Key absent = not annotated (ignored in metrics). `null` = annotated as absent.** |
| `noise` | string[] | – | Synthetic noise operations applied (for breakdowns). |
| `tags` | string[] | – | Phenomena present (for breakdowns): see the tag list below. |
| `note` | string | – | Free-text remark for humans. |

## Two questions, two kinds of gold

- **Parsing — `gold.fields`: what does the text say?** A component gets a value only if it is written in the text
  (in any spelling, abbreviation or with typos); il/ilçe/mahalle values are normalized to the official name.
  Components that are not written are `null`, even when they could be inferred.
- **Resolution — `gold.il/ilce/birim`: what does the text determine?** Start from the units whose official name
  (or semt/historic alias) matches the written mahalle; keep those consistent with the written il, ilçe and postal
  code. `birim` = the single remaining unit, else `null`; `ilce` = the single remaining district, else `null`;
  `il` = the written il or the single remaining province, else `null`. Without a written mahalle, `birim` is `null`
  and il/ilçe are resolved from what is written (a nationally unique ilçe name determines its il).
  A system that guesses where the text is ambiguous is penalized; real sets keep the publisher's true location in `note`.

## Label set (`spans[].label` and `gold.fields` keys)

| Label | Value in `gold.fields` | Example text → value |
|---|---|---|
| `il` | official il name | `ISTANBUL` → `İstanbul` |
| `ilce` | official ilçe name | `kadikoy` → `Kadıköy` |
| `mahalle` | official name of the settlement unit (mahalle, köy, mevkii …), without type word | `caferaga mh` → `Caferağa` |
| `semt` | semt as written (only when it is not the official mahalle) | `Moda` → `Moda` |
| `csbm_tur` | one of `cadde`, `sokak`, `bulvar`, `meydan`, `kume_evler`, `cikmaz`, `yol` | `Cd.` → `cadde` |
| `csbm_ad` | street name without the type word, as written but with spacing cleaned | `1203/5 Sk.` → `1203/5` |
| `site` | site / apartment / building name without the type word | `Güneş Sitesi` → `Güneş` |
| `blok` | block identifier | `B Blok` → `B` |
| `dis_kapi` | door number including letter suffix, `/` kept | `No:17/A` → `17/A` |
| `kat` | floor | `K:3`, `kat 3` → `3`; `zemin` → `0`; `bodrum` → `-1` |
| `daire` | flat (iç kapı) number | `D:5`, `daire 5` → `5` |
| `posta_kodu` | 5 digits | `34710` |
| `tarif` | landmark description | `PTT karşısı` → `PTT karşısı` |
| `diger` | anything else that should be ignored (phone number, person/company name) | — |

Spans mark the text that expresses the component, excluding type words (`Mah.`, `Cad.`, `No:`), which are not
labeled. `csbm_tur` spans cover the type word itself.

## Comparison rules used by the evaluator

- `il`, `ilce`, `mahalle`, `semt`, `csbm_ad`, `site`, `tarif`: compared by `Gazetteer.Key` (case, diacritics, spaces and `. - ' /` ignored).
- `csbm_tur`: exact enum value.
- `dis_kapi`, `blok`: case-insensitive after removing spaces (`17 / a` = `17/A`).
- `kat`, `daire`, `posta_kodu`: exact string after trimming.
- Ids: exact integer equality.

## Tags

`semt`, `historic-name`, `ambiguous-name`, `merkez`, `numbered-street`, `slash-door`, `glued`, `broken-i`, `ascii`,
`typo`, `missing-ilce`, `missing-il`, `reordered`, `phone`, `landmark`, `site-blok`, `osb`, `kume-evler`, `koy`,
`postal-code`, `abbreviation`, `uppercase`, `duplicate-token`, `d-k-ambiguity`.

## Prediction format (systems → evaluator)

Systems that run outside .NET (libpostal, LLMs) write predictions as JSONL with the same `id`:

```json
{"id": "syn-dev-000042", "fields": {"il": "İstanbul", "ilce": "Kadıköy", "mahalle": "Caferağa", "csbm_tur": "cadde", "csbm_ad": "Moda", "dis_kapi": "12", "daire": "3"},
 "ids": {"il": 34, "ilce": 3423, "birim": 34230005}, "confidence": 0.93,
 "candidates": [{"birim": 34230005, "score": 0.93}, {"birim": 6120015, "score": 0.12}]}
```

Missing `fields` keys mean "not predicted" (treated as `null`). `ids`, `confidence` and `candidates` are optional.
