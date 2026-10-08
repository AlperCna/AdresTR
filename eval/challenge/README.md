# Challenge set (`challenge.jsonl`)

250 hand-written, hard Turkish addresses for the AdresTR benchmark (Faz 3). Every row follows
[`eval/SCHEMA.md`](../SCHEMA.md): full `spans`, all 14 `gold.fields` keys, gold `il` / `ilce` / `birim`
ids resolved against `data/staging/` (data version `2026.10`), at least one tag and a `note` that
explains the trap.

- **Source:** `challenge`
- **License:** CC0-1.0 (every row)
- **Ids:** `chal-0001` … `chal-0250`
- **Split:** fixed by position. Every 3rd row (`chal-0003`, `chal-0006`, …) is `dev` (83 rows). The other
  167 rows are `test`. Test rows must not be used for tuning.

## What is in it

The cases cover the phenomena in `docs/research/parser-tasarimi.md` §6 and §10,
`docs/research/yazim-hatalari.md` and `docs/research/veri-analizi.md` §8–9:

- semt names (Moda, Nişantaşı, Taksim, Ataköy …), including semt-looking names that are really
  official mahalle (Kızılay, Göztepe, Alsancak, Mavişehir)
- historic forms: `X Köyü` for mahalle that have a tarihsel alias, and the old ilçe names Eyüp, Kazan
  and Ilıca
- ambiguous names (Cumhuriyet, Yeni, Fatih, Atatürk, Bahçelievler), with and without ilçe or postal
  code
- `Merkez` ilçe, including a büyükşehir that has none (Kayseri, Antalya), and `Merkezefendi`
- mahalle named like an ilçe or an il (`Konak Konak İzmir`, `Samsun Köyü`, `Ordu Mah. Sinop`)
- numbered and slashed streets (`1203/5 Sk.`, `864 sokak`, `127 nolu sokak`)
- door and flat notation (`No:17/5`, `17/A`, `5-7`, `17 A blok`)
- K/D ambiguity (`K.2 D.6`, `D Blok`, `Kuzey Sok.`, `K.Bakkalköy`, `Bodrum Kat Bodrum`, `D-100`)
- glued tokens, broken İ (`i smet`, U+0307, `ıstanbul`), ASCII-only text, upper case and typos
- missing or reordered components
- phone numbers, landmarks, site/blok, OSB, küme evler and köy addresses
- postal-code-only disambiguation, `PK` (posta kutusu) and duplicate tokens

## Tag counts

A row can carry several tags.

| Tag | Rows |
|---|---:|
| `ambiguous-name` | 76 |
| `missing-il` | 70 |
| `numbered-street` | 45 |
| `reordered` | 33 |
| `merkez` | 25 |
| `semt` | 24 |
| `koy` | 23 |
| `duplicate-token` | 22 |
| `slash-door` | 22 |
| `abbreviation` | 20 |
| `missing-ilce` | 20 |
| `uppercase` | 20 |
| `ascii` | 19 |
| `glued` | 17 |
| `typo` | 17 |
| `postal-code` | 15 |
| `site-blok` | 15 |
| `d-k-ambiguity` | 14 |
| `osb` | 13 |
| `broken-i` | 11 |
| `historic-name` | 11 |
| `landmark` | 10 |
| `phone` | 9 |
| `kume-evler` | 8 |

**Gold ids:**
- `birim` is set in 218 rows and null in 32.
- `ilce` is null in 7 rows.
- `il` is null in 1 row (the bare `Cumhuriyet`).

## Annotation conventions

These conventions apply on top of `SCHEMA.md`.

**`gold.fields` contains only what the text says.**
- A component that is not written is `null`, even when it can be inferred. For example,
  `Etiler Mah. Beşiktaş` has `fields.il = null` but `gold.il = 34`.
- `il`, `ilce` and `mahalle` hold the official gazetteer name, even when the text is misspelled,
  ASCII, glued or an alias (`Bornva` → `Bornova`, `Eyüp` → `Eyüpsultan`).
- Other fields keep the text as written, with spaces normalised (`csbm_ad`, `site`, `semt`, `tarif`,
  `diger`). For ordinal streets the trailing dot is dropped (`2004. Cad.` → `2004`).

**Gold ids record what the address determines with the staging gazetteer.**
- The builder computes the ids from the spans with the procedure in `SCHEMA.md` ("Two questions, two
  kinds of gold"). It fails if they differ from the ids declared in the case file. The procedure:
  - Candidates are the units whose official name matches the written mahalle. Semt and historic
    aliases are used only when no official name matches.
  - Candidates are kept only if they agree with the written il, ilçe and postal code (`birim.csv`).
  - A type word right after the name restricts the type: `Mah./Mh./Mahallesi` → mahalle or OSB,
    `Köyü` → köy (or a mahalle with a historic `X Köyü` alias), `Mevkii` → mevki, `Küme Evleri` →
    küme evler.
  - Without a type word, settlements (mahalle, köy, OSB) are preferred. Mevki, yayla, küme evler and
    site units count only when no settlement matches.
  - For a nested unit (`Kepçeli Köyü Kuruca Küme Evleri`), the parent name filters on `ust_ad`.
  - A written ilçe that does not exist in the written il is ignored (`Merkez Kayseri`).
  - Without a written mahalle, `birim` is null and il/ilçe come from what is written. A nationally
    unique ilçe name determines its il.
- A misspelled name (row tagged `typo`, `glued`, `broken-i` or `abbreviation`) that matches nothing
  by itself is read as the official name the annotator declared.
- Partial names are not completed. `Fatih Mah. Sapanca` has `birim = null`, because the official name
  is Kurtköy Fatih.
- An id is `null` when more than one unit stays possible. Examples: `Nişantaşı` alone (Teşvikiye or
  Harbiye), or `Cumhuriyet Mah. Merkez Düzce` (two units).
- A postal code alone, with no settlement name, never determines a `birim` (`34710 Kadıköy İstanbul`).

**Semt.**
- A semt is labelled `semt`, and `fields.mahalle` stays null.
- If `alias.csv` maps the semt to exactly one unit in the ilçe, `gold.birim` is that unit
  (Moda → Caferağa).
- Semts that are not in `alias.csv` (Yeldeğirmeni) get `birim = null`.

**Doors and flats.**
- `No:25/4` is door 25, flat 4.
- A letter after the slash belongs to the door (`17/A`).
- `No:3/2C` is door 3, flat 2C. `No:17 A` is door 17A. Ranges stay in the door (`5-7`).
- Two deliberate edge cases are not covered by the SCHEMA examples. In each, a flat is also written
  explicitly, so the whole `N/M` is kept as the door instead of being split:
  - `chal-0092`: `No:17/2 D:5` → door `17/2`, flat `5`
  - `chal-0097`: `No:86/1 A Blok No.3` → door `86/1`, block `A`, flat `3`
- `Zemin` and `Bodrum` floors are `0` and `-1`.

**Nested units (küme evler under a köy).**
- Both the köy and the küme evler unit are labelled `mahalle`.
- `gold.birim` and `fields.mahalle` give the innermost unit.

**Street-level `Küme Evler` (UAVT style, not in the gazetteer)** is `csbm_tur = kume_evler`.

**`diger`** holds phone numbers, `PK` (posta kutusu) numbers and free-text instructions.

**`abbreviation` tag.** Used only for non-canonical abbreviations: `mh`, `cd`, `sk` without a dot, bare
`k`/`d`, `Blv.`, `Krş.`, `O.S.B.`, `Küme Evl.`, `Afyon`, or a spelled-out `Organize Sanayi Bölgesi`.
The conventional `Mah. … Cad. No:` form is not tagged.

**`missing-il`, `missing-ilce`, `postal-code` and `landmark`** match the spans exactly. The builder
enforces this.

**Privacy.**
- No person names appear, except inside official street or mahalle names (`Abdi İpekçi Cad.`,
  `Kazım Dirik`).
- Door and flat numbers are fictitious.
- Phone numbers use the 555 exchange.

## Regenerate

The cases live in `data/scripts/challenge_cases.py`. Each case is declared as `(text, label)` segments
plus `adm=(il, ilce, birim)` names and tags. `data/scripts/build_challenge.py` does the rest:
1. Joins the segments and computes UTF-16 span offsets. It asserts there are no astral characters.
2. Resolves names to ids from `data/staging/`.
3. Derives `gold.fields`.
4. Validates the rows and writes this file.

Validation covers:
- labels, tags and non-empty spans
- the il ⊃ ilçe ⊃ birim hierarchy
- span text that folds to the gold name (or is explained by a tag)
- postal-code prefixes
- semt-alias consistency
- declared ids equal to the ids determined by the SCHEMA procedure

```bash
python -I data/scripts/build_challenge.py          # validate + write eval/challenge/challenge.jsonl
python -I data/scripts/build_challenge.py --check  # validate only; exit 1 if the file is out of date
```

When `data/staging` changes, run `--check` again. A gazetteer change that makes a name ambiguous or
unresolvable fails the build and names the case.
