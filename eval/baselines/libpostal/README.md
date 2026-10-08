# libpostal baseline

External baseline for the AdresTR benchmark (Faz 3). [libpostal](https://github.com/openvenues/libpostal)
(MIT) is the best-known open address parser; it has no Turkish-specific model, only small Turkish dictionaries
from 2016-17, and no mahalle/ilçe label of its own (see `docs/plan/ARASTIRMA.md` §1). This folder runs it in
Docker and converts its output to the prediction format in `eval/SCHEMA.md`.

| File | What |
|---|---|
| `run.py` | Calls the libpostal HTTP service for every `text` of an eval JSONL, maps labels, writes predictions. Stdlib only (`python -I`). |
| `smoke.jsonl` | 10 invented addresses with real place names (CC0-1.0), SCHEMA format, for a smoke test. |
| `README.md` | This file. |

Outputs: `eval/predictions/libpostal/<set>-<split>.jsonl` (predictions) and
`eval/predictions/libpostal/raw/<set>-<split>.jsonl` (raw libpostal output per row: `id`, `text`, `libpostal`),
for `synthetic`, `real`, `challenge` × `dev`, `test`, plus `smoke-dev`.

## Versions

| What | Value |
|---|---|
| Image | `pelias/libpostal-service` tag `master-2026-07-08-61c5d6e058f021c42b33781f0b5fd566fdec954d` |
| Image digest (pinned) | `sha256:c6bc1df8161404bc511852c76c30d735e3f912141efed85d4873f793363da087` (linux/amd64, created 2026-07-08, ~1.0 GB compressed) |
| HTTP server | `wof-libpostal-server` from [whosonfirst/go-whosonfirst-libpostal](https://github.com/whosonfirst/go-whosonfirst-libpostal), port 4400 |
| libpostal C library | 1.1.3 (`libpostal.pc` in the image; built from `openvenues/libpostal` master by [pelias/libpostal_baseimage](https://github.com/pelias/libpostal_baseimage), Ubuntu 24.04) |
| libpostal data | default (non-Senzing) model: `data_version` v1, `base_data_file_version` v1.0.0, `parser_model_file_version` v1.0.0, `language_classifier_model_file_version` v1.0.0 (parser files dated 2017-03-28); 1.9 GB under `/usr/share/libpostal` |

The last tagged libpostal release is v1.1 (2018); the parser model has not been retrained since v1.0 (2017).

## Start the container

Needs Docker Desktop running. **The first pull is ~1 GB compressed / ~2 GB on disk**, because the image
carries the full libpostal model data. The running service uses ~1.9 GB RAM.

```bash
IMG=pelias/libpostal-service@sha256:c6bc1df8161404bc511852c76c30d735e3f912141efed85d4873f793363da087
docker pull $IMG
docker run -d --name adrestr-libpostal -p 4400:4400 $IMG
# the model takes a few seconds to load; ready when this returns JSON:
curl -s --max-time 5 "http://localhost:4400/parse?address=Moda+Cad.+No:12+Kad%C4%B1k%C3%B6y+%C4%B0stanbul"
# [{"label":"road","value":"moda cad."},{"label":"house_number","value":"no 12"},
#  {"label":"city_district","value":"kadıköy"},{"label":"city","value":"i̇stanbul"}]
```

The container is stopped after each run. Restart / clean up:

```bash
docker start adrestr-libpostal     # restart the existing container (no re-download)
docker stop adrestr-libpostal
docker rm adrestr-libpostal        # remove the container (the image stays)
```

Note: on this machine `docker info` can hang in the `docker-ai` CLI plugin scan; check the daemon with
`docker version --format '{{.Server.Version}}'` or call the HTTP endpoint, always with a timeout.

API: `GET /parse?address=<text>` → `[{"label": ..., "value": ...}, ...]`. Values come back **lower-cased and
re-tokenised** (`No:12` → `no 12`, `İ` → `i̇` with a combining dot); a label can occur more than once.

## Run

From the repository root:

```bash
python -I eval/baselines/libpostal/run.py \
    --input  eval/synthetic/dev.jsonl \
    --output eval/predictions/libpostal/synthetic-dev.jsonl \
    --raw    eval/predictions/libpostal/raw/synthetic-dev.jsonl

# the challenge file holds both splits; select one with --split
python -I eval/baselines/libpostal/run.py --input eval/challenge/challenge.jsonl --split test \
    --output eval/predictions/libpostal/challenge-test.jsonl \
    --raw    eval/predictions/libpostal/raw/challenge-test.jsonl
```

Options: `--url` (default `http://localhost:4400/parse`, or env `LIBPOSTAL_URL`), `--split dev|test`,
`--workers 4`, `--timeout 30`, `--from-raw FILE` (re-map saved raw output, no container needed),
`--no-repair` (disable the type-word repairs below), `--il-csv` (default `data/staging/il.csv`).
The service handles the full benchmark (4,750 rows) in a few minutes at most.

Output lines contain only `id` and `fields`; no `ids`, `confidence` or `candidates` (libpostal cannot resolve to
gazetteer ids and has no calibrated score). Keys libpostal did not produce are omitted (= not predicted).
Nothing is inferred: every predicted value is (part of) a component libpostal extracted from the text, so an
address without a province name never gets an `il`.

## Label mapping

| libpostal label | AdresTR field | Post-processing |
|---|---|---|
| `state`, `state_district`, `city`, `city_district` | `il` / `ilce` | admin rule below |
| `suburb` | `mahalle` | trailing type word removed (`mahallesi`, `mahalle`, `mah`, `mh`, `mhl`, `köy(ü)`, `mevki(i)`) |
| `road` | `csbm_ad` + `csbm_tur` | split at the last street type word (table below); anything libpostal appended after it is dropped |
| `house_number` | `dis_kapi` | the door-number token after `no`/`numara` (or the leading one): digits + optional letter + optional `/x`, `-x` (x = number or one letter). `no 12 d 3` → `12`, `29/konya` → `29`. Values without a digit are skipped |
| `unit` | `daire` | the number after `d`/`daire`/`iç kapı`, else the first number. A unit containing a `blok` word goes to `blok` instead |
| `level` | `kat` | the number after `kat`/`k` (or before `. kat`), else the first number; `zemin` → `0`, `bodrum` → `-1` |
| `postcode` | `posta_kodu` | first 5-digit group |
| `house` | `site` | trailing `sitesi`/`site`/`apartman(ı)`/`apt`/`ap` removed (but see house repair) |
| `entrance`, `staircase` | `blok` | `blok`/`blk`/`bl` removed |
| `country`, `country_region`, `island`, `world_region`, `near`, `category`, `po_box` | — | ignored |

Repeated labels: for the admin labels the **last** occurrence is used (Turkish addresses go from small to large,
so the hierarchy is at the end); for all other labels the first occurrence that yields a well-formed value.

Street type words (compared case/diacritic-insensitively; one- or two-word forms):

| `csbm_tur` | written forms |
|---|---|
| `cadde` | cadde, caddesi, cad, cd, cadd |
| `sokak` | sokak, sokağı, sok, sk |
| `bulvar` | bulvar, bulvarı, bulv, blv, blvr, bul |
| `meydan` | meydan, meydanı, mey, meyd |
| `kume_evler` | küme evler, küme evleri, kümeevler |
| `cikmaz` | çıkmaz, çıkmazı, çık, çkmz |
| `yol` | yol, yolu |

These are standard forms from `data/curated/abbreviations.draft.csv`, deliberately **without** its typo variants
(handing libpostal our typo list would be handing it part of AdresTR). A type word is never taken from the first
word of the road (`Meydan Sokak` → `Meydan` + `sokak`).

### Admin rule (il / ilçe)

libpostal's own Turkish boundary config
([`resources/boundaries/osm/tr.yaml`](https://github.com/openvenues/libpostal/blob/master/resources/boundaries/osm/tr.yaml))
maps OSM admin levels as: level 4 (il) → `state`, 6 (ilçe) → `state_district`, 8 → `city`, 10 → `suburb`;
inside İstanbul the il itself is `city` and ilçes are `city_district` (Ankara's ilçes too). In practice the model
puts the province in `state` or `city` and the ilçe in `city`, `city_district` or `state_district`. The rule uses
**only libpostal's output plus the 81 province names of `data/staging/il.csv`** (no ilçe/mahalle gazetteer):

1. `il` = the first admin value, in label order `state`, `state_district`, `city`, `city_district`, that equals a
   province name (case/diacritic-insensitive). If none does, `il` = `state` when present.
2. `ilce` = the first remaining admin value in order `state_district`, `city_district`, `city`, `state` that is
   neither the il value nor another province name.
3. *Repair*: if no il was found and one admin value is several words whose first or last word is a province name
   (`city_district = "sariveliler/karaman"`), it is split into il + ilçe.

### Type-word repairs (default on; `--no-repair` turns them off)

libpostal very often puts the mahalle into `road` or `house`. Using the same type-word lists as above (no
gazetteer):

- `road` containing a mahalle type word after its first word (`caferağa mah. moda cad.`): the part up to the type
  word becomes `mahalle` (if no `suburb`), the rest stays `road`.
- `house` ending in a mahalle type word (`kızılay mahallesi`, `ihsaniye mah.`) → `mahalle` instead of `site`;
  `house` ending in `ilçe`/`ilçesi` (`osmangazi ilçesi`) → `ilce` if none.

The mahalle taken this way keeps every word libpostal put before the type word, e.g. `MALATYA ÇANAKÇI MAH.` →
`MALATYA ÇANAKÇI` (wrong); we do not trim it with a gazetteer.

### Surface forms

`run.py` finds each libpostal value's tokens in the original text (Turkish-aware case folding) and outputs the
substring as written (`İSTANBUL`, `Kadıköy`); if it cannot, it outputs libpostal's value without the combining
dot. The evaluator compares names by `Gazetteer.Key`, so this mostly affects readability.

## Known limitations

- No Turkish-specific training data; abbreviations such as `mh`, `sitesi`, `blok`, `küme evler`, `OSB` and
  `K:`/`D:` are mostly unknown to the model.
- No mahalle label: mahalle only comes from `suburb` (rare) or the repairs above. `semt`, `tarif`, `diger` are
  never predicted.
- `X/Y` mahalle/ilçe pairs (`SOĞANLI/BAHÇELİEVLER`, common in the real set) are usually labelled `house` and
  end up as a wrong `site`.
- Phone numbers are split into `house_number`/`road` pieces (`tel 0532 555 22 70` → `dis_kapi` `555`).
- `No:12 D:3` is one `house_number` (`no 12 d 3`): we keep the door number, the flat number is lost.
- Space-separated door letters (`No:118 D`) lose the letter (ambiguous with `D:` = daire).
- No ids or confidence: id-based metrics (il/ilçe/birim accuracy, top-k) do not apply to this baseline.

## Smoke test

`smoke.jsonl` (10 rows) → `eval/predictions/libpostal/smoke-dev.jsonl`. Raw libpostal output and the mapped
fields (repairs on):

| # | Text | libpostal | Mapped fields |
|---|---|---|---|
| 1 | Caferağa Mah. Moda Cad. No:12 D:3 Kadıköy/İstanbul | road `caferağa mah. moda cad.` · house_number `no 12 d 3` · house `kadıköy/i̇stanbul` | mahalle Caferağa, csbm Moda/cadde, dis_kapi 12, site `Kadıköy/İstanbul` ✗ (il, ilçe, daire lost) |
| 2 | Kızılay Mahallesi Atatürk Bulvarı No: 115 Çankaya Ankara | house `kızılay mahallesi` · road `atatürk bulvarı` · house_number `no 115` · city `çankaya` · state `ankara` | all correct |
| 3 | Alsancak Mh. 1453 Sk. No:7/A Konak İZMİR | house `alsancak mh.` · road `1453 sk.` · house_number `no 7/a` · suburb `konak` · city `i̇zmi̇r` | il İZMİR, csbm 1453/sokak, dis_kapi 7/A; mahalle `Konak` ✗ (ilçe as suburb), Alsancak lost (`mh.` + `suburb` present) |
| 4 | BARBAROS MAH. BEGONYA SOK. NO:5 KAT:2 DAİRE:4 ATAŞEHİR İSTANBUL 34746 | house_number `barbaros mah.` · road `begonya sok.` · house_number `no` · level `5 kat 2` · house `dai̇re 4 ataşehi̇r` · city `i̇stanbul` · postcode `34746` | il, csbm, kat 2, posta_kodu correct; mahalle, dis_kapi, daire, ilçe lost; site `DAİRE:4 ATAŞEHİR` ✗ |
| 5 | Güzelyalı Mahallesi Mithatpaşa Caddesi No 1050 Daire 8 Konak İzmir | road `güzelyalı mahallesi mithatpaşa caddesi` · house_number `no 1050` · road `daire` · house_number `8` · city `konak` · state `i̇zmir` | all correct except daire (lost) |
| 6 | Menderes Mah. Gazi Mustafa Kemal Blv. Güneş Sitesi B Blok No:21 Mezitli Mersin | road `menderes mah. gazi mustafa kemal blv. güneş` · house_number `sitesi b blok no 21` · city `mezitli` · state `mersin` | il, ilçe, mahalle, csbm, dis_kapi 21 correct; site and blok lost |
| 7 | Osmangazi ilçesi Hamitler Mahallesi Kardelen Sokak No:3 Bursa | house `osmangazi ilçesi` · road `hamitler mahallesi kardelen sokak` · house_number `no 3` · city `bursa` | all correct |
| 8 | ihsaniye mah. ataturk cad. no:45 kat 3 nilufer bursa | house `ihsaniye mah.` · road `ataturk cad.` · house_number `no 45` · level `kat 3` · city `nilufer` · state `bursa` | all correct |
| 9 | Kemalpaşa Mah. Saraybahçe Küme Evler No:14 Serdivan Sakarya | suburb `kemalpaşa mah.` · city `saraybahçe` · house `küme evler no 14` · city `serdivan` · state `sakarya` | il, ilçe, mahalle correct; csbm and dis_kapi lost; site `Küme Evler No:14` ✗ |
| 10 | Emek Mah. Bişkek Cad. No:22/1 Çankaya 06490 | road `emek mah. bişkek cad.` · house_number `no 22/1` · city `çankaya` · postcode `06490` | all correct (no il in the text, none predicted) |

Observations: libpostal recognises numbered and named roads with standard type words and `No:` door numbers
reasonably well, and picks up province names. Its main failure modes on Turkish input are (a) the mahalle, which
it has no concept of and puts into `house`, `road` or even `house_number`; (b) `K:`/`D:`/`DAİRE` flat and floor
markers; (c) site/blok/küme evler; (d) slash-joined `ilçe/il` or `mahalle/ilçe` pairs. Without the repairs
(`--no-repair`) rows 1, 5, 6, 7 and 10 get the mahalle glued into `csbm_ad` (`Caferağa Mah. Moda`) and rows 2,
3, 7, 8 a mahalle or ilçe as `site`.
