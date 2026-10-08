# AdresTR staging verisi / staging data

**TR:** Bu klasördeki CSV'ler C# DataBuilder için **sözleşme** niteliğindedir. Yalnız iki kaynaktan üretilir:
- PTT türevi açık veri: muratgozel/turkey-neighbourhoods (MIT); epigra/tr-geozones (MIT) semt sütunu ve çapraz kontrol için.
- Wikidata (CC0).

NVİ kopyası (melihozkara) bu dosyalara **girdi değildir**.

**EN:** These CSVs are the **contract** consumed by the C# DataBuilder. They are built only from:
- PTT-derived open data: muratgozel (MIT), plus epigra (MIT) for the semt column and cross-checking.
- Wikidata (CC0).

The NVİ copy (melihozkara) is **not** an input.

## Biçim / Format

- UTF-8 (BOM yok), LF satır sonu, RFC 4180 tırnaklama, ilk satır başlık.
- Satırlar id'ye göre sıralı (`alias.csv`: hedef, hedef_id, alias).
- Koordinatlar ondalık derece, `.` ayraçlı, en fazla 6 ondalık. Boş = bilinmiyor.

## Dosyalar / Files

| Dosya | Sütunlar | Not |
|---|---|---|
| `il.csv` | `plaka,ad,wikidata,enlem,boylam` | 81 satır. `plaka` 1..81. Wikidata plaka için P14358/ISO kullanılır, P395 kullanılmaz. |
| `ilce.csv` | `ilce_id,plaka,ad,wikidata,enlem,boylam` | 973 satır. `ilce_id = plaka*100 + seq`. Merkez ilçeler `Merkez` adını taşır. |
| `birim.csv` | `birim_id,ilce_id,tur,ad,ust_ad,posta_kodu,semt,wikidata,nvi_id,enlem,boylam` | PTT birimi başına bir satır, artı sentetik köy satırları (aşağıda). `birim_id = ilce_id*10000 + seq`. |
| `alias.csv` | `hedef,hedef_id,alias,tur,kaynak` | `hedef` ∈ {il, ilce, birim}; `tur` ∈ {semt, tarihsel, yazim}; `kaynak` ∈ {el, ptt-semt, wikidata-6360} |
| `VERSION` | — | Veri sürümü, tek satır: `2026.10` (LF ile biter). |
| `SOURCES.json` | — | Girdi dosyaları (sha256, URL, lisans, anlık görüntü tarihi), çıktı sayıları, kapsam, doğrulama sonuçları |
| `retired_ids.csv` | `hedef,id,dogal_anahtar,tarih` | Yalnız bir id emekliye ayrıldığında oluşur. Bu id'ler bir daha kullanılmaz. |

### `birim.csv` alanları

- **`tur`** ∈ {mahalle, koy, belde, mezra, mevki, yayla, kume_evler, osb, site, diger}. Eşleme `data/scripts/build_staging.py` başındaki yorumda.
  - `X Mah (B Beldesi)` → `mahalle`, `ust_ad = B`. PTT'de beldenin kendisi satır olmadığı için `belde` şu an üretilmiyor.
  - `X Mah (K Köyü)` → türü addaki sonekten belirlenir (mezra, yayla, kume_evler, osb, site); sonek yoksa `mevki`.
- **`ad`:** Tür soneki olmadan, Türkçe başlık biçiminde.
  - Örnekler: `Caferağa`, `100. Yıl`, `Kuva-i Milliye`, `Acıdere OSB`.
  - OSB ve Site adın parçası olarak kalır.
  - Sonek soyulunca ad boş kalacaksa (ör. `Yayla Evleri`) sonek korunur.
- **`ust_ad`:** İç içe PTT kayıtlarında üst birimler, içten dışa ` / ` ile birleştirilir. Örnek: `Küme7 Mah (seydiahmet Mah) (koçak Köyü)` → `Seydiahmet / Koçak`.
- **`posta_kodu`:** 5 haneli metin (baştaki sıfır korunur). İlk iki hane plakaya eşit (%100). Yalnız sentetik köy satırlarında boş olabilir.
- **Sentetik köy satırları:** PTT'de bazı köylerin kendi `X Köyü` satırı yoktur; bu köyler yalnız alt birimlerinin üst birimi olarak geçer (`Y Mah (X Köyü)`). Her böyle köy için `tur=koy` satırı üretilir:
  - `ad`: köy adı.
  - `ust_ad`: boş (PTT köyün üstünü vermez).
  - `posta_kodu`: yalnız tüm alt birimler tek bir kodu paylaşıyorsa dolu, aksi halde boş.
  - `semt`: aynı kural.
  - Wikidata eşleşmesi diğer köylerle aynı kurallarla yapılır (yalnız tekil eşleşme).
  - Bu satırlar ID kararlılığı kurallarına tabidir: mevcut kayıtların ardına yeni `seq` alırlar.
- **`semt`:** PTT dağıtım bölgesi adı (posta kodu ile birebir).
- **`wikidata`, `nvi_id`, `enlem`, `boylam`:** Yalnız Wikidata'dan gelir.
  - `nvi_id`: mahalle/osb için P12883 (NVİ mahalle kimlikNo), köy için P13588 (NVİ köy kayıt no). İki farklı kimlik uzayıdır; `tur` ile birlikte yorumlanmalı.
  - Eşleşme yalnız **tekil** olduğunda doldurulur: bir öğe tek bir birime, bir birim tek bir öğeye.

### Elle bakılan semt alias'ları / Curated semt aliases

**TR:**
- **Kaynak:** `data/curated/semt_alias.csv`, sütunlar `il,ilce,semt,mahalleler,kaynak,not`. `mahalleler` alanı `|` ile ayrılmış resmî mahalle adlarıdır. Lisans CC0; ayrıntılar `data/curated/README.md`.
- **Dönüşüm:** Her (semt, mahalle) çifti için `alias.csv` dosyasına bir satır yazılır: `hedef=birim`, `tur=semt`, `kaynak=el`.
- **Çözümleme:** il, ilçe ve mahalle katlanmış adla çözülür. Mahalle yalnız `tur` ∈ {mahalle, osb} birimleri arasında aranır.
- **Hata:** Herhangi bir başvuru tekil çözülemezse derleme, satır numarasını gösteren açık bir hatayla durur.
- **Tekrar:** Aynı (hedef, hedef_id, katlanmış alias) zaten varsa satır atlanır.
- **İz:** Dosyanın sha256'sı `SOURCES.json` → `kuratorlu_girdiler` altında.

**EN:**
- `data/curated/semt_alias.csv` is merged as `hedef=birim, tur=semt, kaynak=el`, one row per (semt, mahalle).
- An il/ilçe/mahalle reference that does not resolve uniquely by folded name fails the build.
- An alias that already exists for the same target is skipped.

## ID kararlılığı / ID stability

**TR:**
- **Doğal anahtarlar:**
  - ilçe: `(plaka, fold(ad))`
  - birim: `(ilce_id, tur, fold(ad), fold(ust_ad))`; çakışma varsa `+ posta_kodu`.
- **İlk çalıştırma:** `seq`, ebeveyn içinde doğal anahtar sırasıyla 1'den verilir.
- **Sonraki çalıştırmalar:**
  - Mevcut `data/staging/*.csv` okunur ve aynı doğal anahtar aynı id'yi alır.
  - Yeni varlıklar, ebeveyndeki en büyük `seq` (mevcut + emekli) + 1'den numara alır.
  - Kaybolan varlıkların id'leri `retired_ids.csv`'ye eklenir ve **asla yeniden kullanılmaz**.
- **Not:** Bir birimin `tur`, `ad` veya `ust_ad` değeri değişirse doğal anahtar da değişir. Bu durumda birim yeni id alır ve eski id emekliye ayrılır.

**EN:**
- **Natural keys:** as listed above.
- **First run:** seq values are assigned in natural-key order within each parent.
- **Later runs:**
  - Existing ids are preserved for the same natural key.
  - New entities get max(existing ∪ retired seq) + 1.
  - Vanished ids are appended to `retired_ids.csv` and never reused.
- **Note:** If a unit's `tur`, `ad` or `ust_ad` changes, its natural key changes, so it gets a new id and the old id is retired.

## Yeniden üretim / Regenerate

```bash
# 1) Ham veri (git dışı, data/raw/): bkz. docs/research/veri-analizi.md §0
python -I data/scripts/fetch_wikidata.py data/raw/wikidata wd_il wd_ilce wd_mahalle wd_koy wd_belde wd_parent_up
# 2) Staging
python -I data/scripts/build_staging.py
# 3) (İsteğe bağlı, pakete girmez) NVİ ile karşılaştırma raporu -> docs/research/staging-vs-nvi.md
python -I data/scripts/report_nvi_diff.py
```

`build_staging.py` doğrulama hatası (81 il, 973 ilçe, tekil id, yetim birim, boş ad, boşluk sorunu, geçersiz tür) bulursa sıfırdan farklı kodla çıkar. Doğrulama ayrıntıları `SOURCES.json` → `dogrulama` altında.
