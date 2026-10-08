# Real evaluation set — licenses

Every row carries a `license` field. The rows are derived from public-sector open data; the AdresTR **code**
license (MIT) does not apply to them.

| `license` value | Sources | License | Attribution |
|---|---|---|---|
| `IBB-Acik-Veri` | `ibb-saglik`, `ibb-muhtarlik`, `ibb-pazar-tpl` | İstanbul Büyükşehir Belediyesi Açık Veri Lisansı 1.0 (<https://data.ibb.gov.tr/license>) — free use including commercial use, copying, adaptation; compatible with CC BY 4.0 and ODC-By | required |
| `CC-BY-4.0` | `izmir-eczane`, `izmir-saglik-tpl`, `izmir-muhtarlik-tpl` | Creative Commons Attribution 4.0 International, as declared by İzmir Büyükşehir Belediyesi for its open-data portal (<https://acikveri.bizizmir.com/tr/license>) | required |

## Required attribution

Redistributions of `eval/real/` (for example a Hugging Face dataset) must keep this notice:

> Contains public sector information from İstanbul Büyükşehir Belediyesi (İBB Açık Veri Portalı,
> https://data.ibb.gov.tr) licensed under the İBB Açık Veri Lisansı, and from İzmir Büyükşehir Belediyesi
> (İzmir Açık Veri Portalı, https://acikveri.bizizmir.com) licensed under CC BY 4.0.
> Atıf 4.0 Uluslararası (CC BY 4.0) kapsamında lisanslanan kamu sektörü bilgilerini içerir.
> Modified: rows were filtered, sampled and annotated by the AdresTR project; templated sources (`-tpl`) were
> rendered from structured columns.

Dataset pages and download dates are listed in [`SOURCES.md`](SOURCES.md).

## Notes per source

- **İBB (all three datasets).** The İBB license does not cover personal data contained in the information
  ("Bilgilerdeki kişisel veriler"). For that reason person-named health categories and names with personal titles
  are excluded, and no facility names are published. The license grants no right to suggest endorsement by İBB.
- **İzmir eczane.** Pharmacy names usually contain the pharmacist's name, so `ADI` and `TELEFON` are never
  emitted; rows whose address text contains a mobile number are excluded. Landline numbers that the publisher put
  inside the address field are business contact data and are kept verbatim (tag `phone`).
- **İzmir muhtarlık.** The `ACIKLAMA` field contains the muhtar's name and is never used.
- **İzmir sağlık (CBS web service).** Same CC BY 4.0 license as the dataset page that lists the endpoints.
- No warranty: both publishers provide the data "as is"; errors in the source data may remain in the gold labels
  (see the manual review in `SOURCES.md`).

`license` values follow the SPDX-like ids of `eval/SCHEMA.md` (`IBB-Acik-Veri` is not an SPDX id).
