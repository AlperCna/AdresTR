# AdresTR — Proje Planı

> Türkçe serbest metin adresleri **ayrıştıran, normalize eden ve resmi il/ilçe/mahalle hiyerarşisine göre doğrulayan** açık kaynak .NET kütüphanesi, REST API ve tarayıcıda çalışan playground.
>
> Dayanak: [ARASTIRMA.md](ARASTIRMA.md) · Plan tarihi: 2026-10-08 · Çalışma adı: **AdresTR** (NuGet'te `AdresTR` adı 2026-10-08 itibarıyla boş)

---

## 0. Özet

| | |
|---|---|
| **Problem** | Türkçe adresler dağınık yazılıyor. Ücretsiz, çevrimdışı çalışan, açıklanabilir bir ayrıştırıcı/doğrulayıcı yok. Google'ın doğrulama API'si Türkiye'yi desteklemiyor, libpostal'da mahalle/ilçe etiketi yok, NuGet'te hiç yok. |
| **Çözüm** | Kurallar + resmi gazetteer + kapsamlı bulanık eşleştirme. Her alan için güven skoru, alternatifler ve "ne düzeltildi" logu. |
| **Fark** | İlk .NET Türkçe adres ayrıştırıcı. Çevrimdışı ve deterministik. Semt ve eski köy adlarını anlar. ASCII yazımı tolere eder. **İlk açık Türkçe adres benchmark'ını yayımlar.** |
| **Teslimatlar** | NuGet paketi · REST API (Docker) · tarayıcıda çalışan playground · Hugging Face benchmark veri seti · TR/EN yazılar |
| **Süre (tahmini)** | Haftada yaklaşık 15–20 saatle **10–12 hafta** v1.0'a kadar. Faz 9 sonrası açık uçlu. |
| **Teknoloji** | .NET 10, C#, xUnit v3, BenchmarkDotNet, Docker, Azure Container Apps, Blazor WASM (veya Angular), GitHub Actions |

### Kapsam

**v1.0'da var:**
- Serbest metni bileşenlere ayırma: il, ilçe, mahalle/köy, semt, cadde/sokak/bulvar (tür + ad), site, blok, dış kapı no, kat, daire, posta kodu, ek tarif ("… karşısı").
- il/ilçe/mahalle'yi resmi adlara ve kimliklere normalize etme.
- Hiyerarşi tutarlılığını ve posta kodu ↔ il/ilçe uyumunu doğrulama.
- Güven skoru, top-k alternatif, düzeltme logu.
- Kanonik çıktı formatı: PTT/UAVT düzeninde tek satır.

**v1.0'da yok (Faz 9+):**
- Sokağın gerçekten o mahallede olup olmadığını doğrulama (sokak verisi lisans riski taşıyor, ayrı paket olacak).
- Geocoding (koordinat); v1'de yalnızca mahalle merkezi.
- İki adresin aynı yer olup olmadığını eşleştirme.
- ML/LLM.

---

## 1. Mimari

### 1.1 Veri akışı

```
ham metin
 │
 ├─ 1. Normalize      TurkishText: NFC, U+0307 temizliği, boşluk/noktalama, orijinal offset haritası
 ├─ 2. Fold           ASCII katlanmış anahtar (ç→c, ğ→g, ı/İ→i, ö→o, ş→s, ü→u), Ordinal karşılaştırma
 ├─ 3. Tokenize       yapışık token bölme (147sok → 147 sok), kesme işareti eki (Kadıköy'de → Kadıköy)
 ├─ 4. Desen etiketle posta kodu (5 hane, ilk 2 = plaka), No/Kat/Daire/Blok, n/m, 17/A, numaralı sokak
 ├─ 5. Anahtar sözcük mah/mh/mahallesi, cad/cd, sok/sk, blv, apt, sitesi, köyü, mevkii, küme evler, OSB
 ├─ 6. Aday üret      token trie ile tam eşleşme → kapsamlı bulanık (OSA/SymSpell) → alias (semt, eski köy)
 ├─ 7. Çöz            (il ⊃ ilçe ⊃ mahalle) tutarlı demetler üzerinde beam search, posta kodu kısıtı
 ├─ 8. Skorla         benzerlik + anahtar sözcük komşuluğu + konum + posta kodu uyumu − belirsizlik cezası
 └─ 9. Çıktı          ParseResult: alanlar + kimlikler + kanonik metin + güven + alternatifler + düzeltmeler + span'ler
```

### 1.2 Örnek çıktı (hedef)

```json
{
  "input": "kadikoy caferaga mh moda cd no:12 d3 istanbul",
  "components": {
    "il":      { "value": "İstanbul",  "id": 34,     "confidence": 0.99, "span": [37, 45] },
    "ilce":    { "value": "Kadıköy",   "id": 1421,   "confidence": 0.97, "span": [0, 7] },
    "mahalle": { "value": "Caferağa",  "id": 40123,  "confidence": 0.95, "span": [8, 19] },
    "csbm":    { "type": "Cadde", "value": "Moda", "confidence": 0.80, "verified": false },
    "disKapiNo": "12", "daire": "3",
    "postaKodu": { "value": "34710", "inferred": true }
  },
  "canonical": "Caferağa Mah. Moda Cad. No:12 D:3 34710 Kadıköy/İstanbul",
  "confidence": 0.93,
  "corrections": [
    { "kind": "diacritics", "from": "kadikoy", "to": "Kadıköy" },
    { "kind": "abbreviation", "from": "mh", "to": "Mahallesi" },
    { "kind": "inferred", "field": "postaKodu", "reason": "mahalle→PTT tablosu" }
  ],
  "alternatives": [],
  "warnings": ["csbm gazetteer ile doğrulanmadı (sokak verisi yüklü değil)"],
  "dataVersion": "2026.10"
}
```
> Kimlik ve posta kodu değerleri şimdilik örnek amaçlıdır.

### 1.3 Repo yapısı

```
AdresTR/
├─ src/
│  ├─ AdresTR/                    # Çekirdek: net10.0, SIFIR bağımlılık, ICU'dan bağımsız
│  │   ├─ Text/                   #   TurkishText (fold, upper/title, normalize)
│  │   ├─ Tokenization/           #   tokenizer + regex source generator desenleri
│  │   ├─ Lexicon/                #   kısaltma/anahtar sözcük sözlüğü
│  │   ├─ Gazetteer/              #   IGazetteer, ikili format okuyucu, trie, posting
│  │   ├─ Matching/               #   OSA/Damerau, SymSpell sarmalayıcı
│  │   ├─ Resolution/             #   beam search, skorlama, güven
│  │   └─ AddressParser.cs        #   public API
│  ├─ AdresTR.Data/               # Gömülü il/ilçe/mahalle/posta kodu verisi (Brotli), ayrı lisans dosyası
│  ├─ AdresTR.Extensions.AspNetCore/  # (Faz 9) model binding, DI
│  └─ AdresTR.Api/                # Minimal API host
├─ tools/
│  ├─ AdresTR.DataBuilder/        # Ham kaynak → doğrulanmış → ikili gazetteer
│  └─ AdresTR.BenchGen/           # Sentetik benchmark üretici
├─ tests/
│  ├─ AdresTR.Tests/              # Birim + özellik tabanlı + snapshot
│  ├─ AdresTR.Api.Tests/          # WebApplicationFactory
│  └─ AdresTR.Accuracy/           # Benchmark koşucusu + regresyon kapısı
├─ benchmarks/AdresTR.Benchmarks/ # BenchmarkDotNet
├─ web/playground/                # Blazor WASM (veya Angular)
├─ data/
│  ├─ raw/        (git'e girmez)   # İndirilen kaynaklar
│  ├─ curated/                     # Elle bakılan tablolar: alias, semt, kısaltma (CSV, PR ile güncellenir)
│  └─ LICENSE-DATA.md
├─ eval/          # dev/test/challenge setleri (JSONL), etiketleme rehberi
├─ docs/          # DocFX, adr/, mimari diyagram
├─ .github/       # workflows, issue forms, dependabot
├─ Directory.Build.props · Directory.Packages.props · global.json · .editorconfig
├─ README.md (EN) · README.tr.md · CONTRIBUTING.md · CODE_OF_CONDUCT.md · CHANGELOG.md · LICENSE
```

### 1.4 Public API taslağı

```csharp
var parser = AddressParser.CreateDefault();          // gömülü veri, tembel yükleme
ParseResult r = parser.Parse("caferaga mh moda cd no 12 kadikoy");
r.Mahalle?.Value; r.Confidence; r.Corrections; r.ToCanonicalString();

ValidationResult v = parser.Validate(r);             // hiyerarşi + posta kodu tutarlılığı
IReadOnlyList<ParseResult> top = parser.ParseTopK(input, k: 5);

// İleri kullanım
var parser2 = AddressParser.Create(o => {
    o.Gazetteer = Gazetteer.Load("adrestr-2026.10.bin");   // dış veri dosyası
    o.StreetData = StreetData.Load("sokak.bin");           // (Faz 9) isteğe bağlı
    o.MinConfidence = 0.6;
});
```

---

## 2. Önceden alınmış kararlar (ADR'ye dönüşecek)

| # | Karar | Gerekçe |
|---|---|---|
| ADR-001 | MVP **kural + gazetteer + bulanık** olacak; ML/LLM sonra ve isteğe bağlı | Deterministik, açıklanabilir, çevrimdışı. BERT NER bina ve kapı no'da yalnızca yaklaşık 0,70 F1 aldı; burada kurallar daha iyi. |
| ADR-002 | Çekirdek **ICU'dan bağımsız**, Türkçe katlama elle yazılmış tablo ile | Invariant modda `tr-TR` sessizce bozuluyor (ölçüldü). Chiseled imajlar, Blazor WASM ve AOT'de de doğru çalışması gerekiyor. |
| ADR-003 | Bulanık arama **hiyerarşik kapsamlı** | 1M ad üzerinde global aramanın p50'si 20 ms (ölçüldü). Kapsamlı arama 1 ms'nin altında olmalı. |
| ADR-004 | **Sokak verisi varsayılan pakette yok**; dış veya ayrı paket olarak | FSEK veritabanı hakkı, NVİ CAPTCHA'sı ve 100 MB'ı aşan boyut. |
| ADR-005 | Kod **MIT**, veri ayrı lisanslı (`LICENSE-DATA.md`); ODbL (OSM) verisi asla çekirdek pakete girmez | .NET'te norm MIT. ODbL share-alike'tır ve MIT ile karışmamalı. |
| ADR-006 | Veri **sürümlü** (`2026.10`); kararlı anahtar NVİ `kimlikNo` | Sokaklarda yarım yılda yaklaşık %1 değişim; 6360 gibi idari değişiklikler. |
| ADR-007 | Sadece `net10.0` | .NET 8 ve 9'un desteği 2026-11-10'da bitiyor. |
| ADR-008 | **Benchmark, parser'dan önce** kurulacak | Her iyileştirme ölçülebilsin; README'deki rakamlar gerçek olsun. |
| ADR-009 | API ham adresi **loglamaz**, durumsuz çalışır; LLM yolu varsayılan kapalı | KVKK |
| ADR-010 | NuGet'e yayın **Trusted Publishing (OIDC)** ile | Uzun ömürlü API anahtarları 2026-11-01'de sona eriyor. |

---

## 3. Fazlar

Her fazın sonunda: **çalışan bir şey + test + commit + kısa not (CHANGELOG / ADR)**.

### Faz 0 — Kurulum ve temeller · ~3 gün

**Hedef:** Boş ama profesyonel bir repo, ilk günden yeşil CI.

- [ ] GitHub'da `AdresTR` reposunu aç (public). Açıklama ve topic'leri gir: `turkish`, `address-parser`, `dotnet`, `nlp`, `geocoding`.
- [ ] NuGet'te `AdresTR` önek rezervasyonu için başvur (isteğe bağlı).
- [ ] `global.json` (.NET 10 SDK), `Directory.Build.props`:
  - `Nullable`, `TreatWarningsAsErrors`, `Deterministic`, `ContinuousIntegrationBuild`
- [ ] `Directory.Packages.props` (merkezi paket yönetimi), `.editorconfig`, `.gitignore` (`data/raw/` dahil)
- [ ] Solution iskeleti (yukarıdaki yapı), boş projeler, bir "smoke" testi
- [ ] GitHub Actions `ci.yml`:
  - restore, build, test
  - **matris:** ubuntu + windows + **invariant-mode** job (`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`)
  - kapsam: `Microsoft.Testing.Extensions.CodeCoverage` → Codecov
- [ ] Dependabot (nuget, github-actions, docker), CodeQL, PR başlığı için conventional-commit lint
- [ ] LICENSE (MIT), `CODE_OF_CONDUCT.md` (Contributor Covenant 3.0), issue formları (bug, yeni kısaltma, yanlış ayrıştırma)
- [ ] `docs/adr/` altına ADR-001 … ADR-010 (Bölüm 2'den, kısa şablonla)
- [ ] README iskeleti: "neden", durum rozeti, yol haritası linki

**Bitti sayılır:** `main` dalında CI yeşil, 3 işletim/mod matrisi geçiyor, ADR'ler commit'lenmiş.

---

### Faz 1 — Veri katmanı (gazetteer) · ~1,5 hafta

**Hedef:** Lisansı temiz, sürümlü, doğrulanmış il/ilçe/mahalle/posta kodu verisi ve bunu üreten tekrarlanabilir bir araç.

**1a. Kaynakları topla** (`tools/AdresTR.DataBuilder`, `data/raw/`):
- [ ] **il (81):** plaka kodu ile, elle hazırlanmış tablo
- [ ] **ilçe (973):** NVİ snapshot (melihozkara) + HDX COD-AB-TUR + PTT türevi listeler ile çapraz kontrol
- [ ] **mahalle/köy:**
  - birincil: PTT türevi MIT listeler (muratgozel 73.305 ve epigra 73.304)
  - çapraz kontrol: melihozkara NVİ snapshot (73.398, `mahalleTur` alanı ile)
- [ ] **posta kodu:** mahalle → PTT kodu (2.771 kod)
- [ ] **koordinat:** Wikidata SPARQL (CC0), il/ilçe/mahalle/köy merkezleri; il/ilçe için HDX merkezleri
- [ ] Lisans e-postaları: melihozkara ve muratgozel'e "verinizi CC0/CC BY ile kullanabilir miyim" diye yaz. Yanıtları `data/LICENSE-DATA.md`'ye işle.

**1b. Elle bakılan tablolar** (`data/curated/*.csv`, topluluk PR'larına açık):
- [ ] `abbreviations.csv`: libpostal TR sözlüğü + hackathon repolarındaki gerçek yazım hataları (mahlesi, sokar, cadessi…)
- [ ] `semt_alias.csv`: semt → mahalle adayları (Moda→Caferağa, Levent→…, Bostancı→…). İlk 100–200 satır büyük şehirlerden.
- [ ] `historic_alias.csv`: 6360 öncesi köy/belde → güncel mahalle. Kaynak: NVİ diff dosyaları + Wikidata.
- [ ] `ambiguous_names.csv`: otomatik üretilir (aynı adı taşıyan mahalle sayısı; Cumhuriyet, Atatürk, Yeni…)

**1c. Doğrulama ve üretim:**
- [ ] Doğrulama kuralları (build'i kırar):
  - 81 il, 973 ilçe
  - her mahallenin tek bir üst ilçesi olmalı
  - posta kodunun ilk 2 hanesi = plaka
  - yinelenen kimlik yok
- [ ] Kaynaklar arası fark raporu (`data/reports/diff-2026.10.md`)
- [ ] **İkili format v1:**
  - UTF-8 string havuzu + varlık tabloları (üst kimlikle)
  - katlanmış token sözlüğü → posting listeleri
  - alias tablosu
  - Brotli ile sıkıştırma
  - başlıkta sürüm ve kaynak hash'leri
- [ ] `AdresTR.Data` projesi: `.bin` dosyasını gömülü kaynak olarak içerir
- [ ] Hedef boyut: **5 MB'ın altında** (sokak yokken)

**Bitti sayılır:**
- `dotnet run --project tools/AdresTR.DataBuilder` sıfırdan `adrestr-2026.10.bin` üretiyor.
- Doğrulama raporu temiz.
- `LICENSE-DATA.md` her kaynağı ve lisansını listeliyor.

---

### Faz 2 — Türkçe metin çekirdeği · ~4 gün

**Hedef:** Projenin "Türkçe I problemi" konusundaki güvenilirliği, ki projenin vitrini bu.

- [ ] `TurkishText.FoldKey(ReadOnlySpan<char>)`:
  - `string.Create` ile tablo tabanlı
  - ç→c, ğ→g, ı/I/İ/i→i, ö→o, ş→s, ü→u, â→a, î→i, û→u
  - U+0300–036F ve U+200B–200D silinir; NBSP → boşluk; ’/ʼ → `'`; boşluklar daraltılır
- [ ] `TurkishText.ToUpperTr`, `ToTitleTr`: i↔İ, ı↔I eşlemeleri elle; çıktıda "İstanbul", "Iğdır" doğru olmalı
- [ ] `TurkishText.Normalize`: NFC (ICU yoksa güvenli geri dönüş), orijinal metne **offset haritası**
- [ ] Testler:
  - **Birim:** `"İSTANBUL"`, `"istanbul"`, `"ISTANBUL"`, `"i\u0307stanbul"` (U+0307) → aynı anahtar
  - **Özellik tabanlı (CsCheck):**
    - `Fold(Fold(x)) == Fold(x)`
    - büyük/küçük harf ve aksan değişmezliği
    - hiçbir girdide exception yok
  - **Invariant-mode CI job'unda** da aynı sonuç
- [ ] BenchmarkDotNet: `FoldKey` için 0 bellek ayırma (span sürümü) ve ns/op

**Bitti sayılır:** %100 dal kapsamı, invariant ve ICU modlarında aynı çıktı, benchmark sonucu README'de.

---

### Faz 3 — Benchmark ve değerlendirme altyapısı · ~1,5 hafta

**Hedef:** Parser yazılmadan önce "iyi mi?" sorusunu sayıyla cevaplayabilmek. **Projeyi ödev projelerinden ayıran ana faz.**

**3a. Etiket şeması ve rehber** (`eval/GUIDELINES.md`):
- [ ] Etiketler:
  - `il`, `ilce`, `mahalle`, `semt`
  - `csbm_tur`, `csbm_ad`, `site`, `blok`
  - `dis_kapi`, `kat`, `daire`, `posta_kodu`
  - `tarif`, `diger`
- [ ] Format: JSONL. Her satırda `text`, `spans[]`, `gold` (kanonik kimlikler), `source`, `noise[]`.

**3b. Sentetik üretici** (`tools/AdresTR.BenchGen`, sabit seed):
- [ ] 20–40 şablon: resmi düzen, "Kadıköy/İstanbul" eğik çizgi stili, ters sıra, eksik bileşenli varyantlar
- [ ] Gürültü katmanları (her örnek için hangi katmanın uygulandığı loglanır):
  - ASCII katlama, kısaltma varyantı
  - Türkçe Q klavyeye göre yazım hatası
  - yapışık yazım
  - bileşen düşürme, sıra değiştirme
  - büyük/küçük harf ve I/İ/ı tuzakları
  - fazladan telefon veya tarif
  - kırık İ (`i smet`)
- [ ] İl ve gürültü türüne göre tabakalı örnekleme

**3c. Gerçek ve zor setler:**
- [ ] **Gerçek set (en az 500):**
  - resmi kurum adresleri (MEB okulları, hastaneler, belediyeler); her satırda kaynak URL
  - kişisel adres **yok**
- [ ] **OSM POI seti** (ayrı, ODbL): işyerlerinin `addr:*` etiketlerinden serbest metin üretilir
- [ ] **Challenge set (200–300):** elle yazılır. Semt, eski köy, numaralı sokak, belirsiz mahalle, `1203/5 Sk.`, `No:17/5`, `D` ve `K` belirsizliği gibi vakalar.
- [ ] Etiketleme: Label Studio (yerelde Docker ile). Örneklerin %10'unu iki kez etiketle ve Cohen κ hesapla.
- [ ] Bölümler: dev yaklaşık 1.000 · test yaklaşık 2.000 (≥500 gerçek) · challenge. **Test seti ayarlama sırasında kullanılmaz.**

**3d. Değerlendirme koşucusu** (`tests/AdresTR.Accuracy`):
- [ ] Metrikler:
  - alan bazında P/R/F1 (strict span, mikro ve makro)
  - exact match
  - kanonik ID doğruluğu (il/ilçe/mahalle)
  - Acc@1, Acc@5, MRR
  - doğrulayıcı için yanlış ret ve yanlış kabul oranları
  - ECE (15 kova) ve güvenilirlik grafiği
  - gürültü türüne göre kırılım
  - bootstrap %95 güven aralığı
- [ ] Çıktı: `eval/results/<system>-<date>.json` + Markdown tablo
- [ ] **Baseline'lar:**
  - (1) naif regex
  - (2) libpostal (Docker ile; etiket eşlemesi: state→il, city/city_district→ilçe, suburb→mahalle)
  - (3) isteğe bağlı LLM zero-shot (model kimliği, tarih, sıcaklık 0, JSON şema, 1.000 adres başına maliyet)
- [ ] CI regresyon kapısı: dev setinde F1 0,5 puandan fazla düşerse PR kırmızı olur

**Bitti sayılır:** `dotnet test tests/AdresTR.Accuracy` baseline'lar için bir tablo üretiyor; veri setleri `eval/` altında.

---

### Faz 4 — Parser MVP · ~2,5 hafta

**Hedef:** il/ilçe/mahalle + kapı bloğu + CSBM türü ve adı, güven skoru ve açıklamayla birlikte.

**4a. Tokenizer ve desenler** (`[GeneratedRegex]`):
- [ ] Rakam↔harf sınırında bölme, bilinen son ekleri ayırma (`sok`, `mah`, `no`, `kat`, `daire`, `apt`, `blok`)
- [ ] Kesme işareti eki ayırma: `Kadıköy'de`, `İzmir'in`
- [ ] Desenler:
  - posta kodu (5 hane + plaka geçerliliği)
  - `No: 12`, `No.12`, `no12`, `12/3`, `17/A`, `K:3`, `Kat 3`, `D:5`, `Daire 5`, `Blok A`, `A Blok`, `zemin kat`, `bodrum`
- [ ] **Numaralı sokak** ile kapı numarasının ayrılması: sayıdan sonra `sok/sk/sokak/nolu` geliyorsa ad, `no` sonrasındaysa kapı numarası

**4b. Anahtar sözcük etiketleyici:**
- [ ] Kısaltma sözlüğü (Faz 1b) + bağlama duyarlı kurallar (`D` ve `K` bir sayıdan sonra gelirse daire/kat, yön değil)
- [ ] CSBM türleri: cadde, sokak, bulvar, meydan, küme evler, çıkmaz; site, apartman, blok, OSB, mevkii

**4c. Aday üretimi:**
- [ ] Token trie üzerinde katlanmış çok sözcüklü tam eşleşmeler (il, ilçe, mahalle, semt alias, eski ad alias)
- [ ] Bulanık geri dönüş:
  - bant sınırlı OSA (Damerau), bellek ayırmayan, kendi implementasyonumuz
  - eşik ad uzunluğuna göre (≤4 karakterde 0, ≤8'de 1, daha uzunlarda 2 hata)
  - **kapsam:** önce il ve ilçe tüm listede, mahalle yalnızca aday ilçeler içinde
- [ ] "Mahalle" anahtar sözcüğünün sol komşusu güçlü bir mahalle adayıdır, "Cad/Sok" anahtarının sol komşusu CSBM adıdır

**4d. Hiyerarşik çözümleyici ve skor:**
- [ ] Beam search (genişlik yaklaşık 16) üzerinde tutarlı (il, ilçe, mahalle) demetleri; eksik alanlara izin var
- [ ] Skor özellikleri:
  - benzerlik
  - anahtar sözcük komşuluğu
  - konum (Türkçede küçükten büyüğe: ilçe ve il genelde sonda)
  - posta kodu uyumu
  - kapsanan token oranı
  - belirsizlik cezası (`ambiguous_names`)
  - nüfus önceliği (yalnızca beraberlik bozmak için)
- [ ] Çıkarım ve geri çekilme:
  - posta kodu → il (ve ilçe kümesi)
  - tekil mahalle adı → ilçe
  - semt → mahalle adayları
- [ ] Güven: top-1/top-2 skor farkı + kapsam + düzenleme mesafesi, **dev setinde kalibre edilir** (isotonic veya basit Platt)
- [ ] Düzeltme logu: `diacritics`, `abbreviation`, `typo`, `alias_semt`, `alias_historic`, `inferred`, `reordered`
- [ ] Kanonik çıktı: `ToCanonicalString()` (UAVT/PTT düzeni) ve `ToJson()`

**4e. Kalite:**
- [ ] Verify ile snapshot testleri: 100+ temsilî girdi
- [ ] Özellik tabanlı testler: parser hiçbir girdide exception atmaz, 10.000 karakterlik girdi zaman aşımına uğramaz (ReDoS koruması: regex timeout ve girdi uzunluğu sınırı)
- [ ] Mutasyon testi (Stryker.NET) en az çekirdek resolver üzerinde
- [ ] BenchmarkDotNet: hedef **< 1 ms/adres p50** ve tek çekirdekte ≥ 5.000 adres/sn

**Başlangıç hedefleri** (Faz 3 baseline'ından sonra revize edilecek):

| Metrik (test seti) | Hedef |
|---|---|
| il doğruluğu | ≥ 0,98 |
| ilçe doğruluğu | ≥ 0,93 |
| mahalle Acc@1 / Acc@5 | ≥ 0,85 / ≥ 0,95 |
| kapı no / daire F1 | ≥ 0,92 |
| ECE | ≤ 0,05 |
| libpostal'a göre | il/ilçe/mahalle'de anlamlı üstünlük (McNemar p<0,05) |

**Bitti sayılır:** hedeflerin çoğu tutturuldu. Tutturulamayanlar README'de dürüstçe raporlandı ve hata analizi `docs/error-analysis.md`'ye yazıldı.

---

### Faz 5 — NuGet paketi v0.1 · ~3 gün

- [ ] Paket meta verisi:
  - `PackageReadmeFile`, ikon, `PackageLicenseExpression`
  - `snupkg`, SourceLink, `EnablePackageValidation`
  - `IsAotCompatible`, `IsTrimmable` (analyzer uyarıları sıfır)
- [ ] MinVer (tag tabanlı, `fetch-depth: 0`) + release-please (CHANGELOG, tag)
- [ ] `release.yml`: tag → test → pack → **NuGet Trusted Publishing** (`NuGet/login@v1`, `id-token: write`)
- [ ] `samples/ConsoleSample` ve `samples/CsvCleaner`: CSV adres sütununu temizleyen gerçek bir örnek
- [ ] **v0.1.0** yayımla (önizleme), 3 satırlık hızlı başlangıcı README'ye ekle

**Bitti sayılır:** `dotnet add package AdresTR --prerelease` çalışıyor, NuGet sayfasında README görünüyor.

---

### Faz 6 — REST API, Docker, dağıtım · ~1 hafta

**Uç noktalar** (`/v1` route grubu):

| Uç nokta | Açıklama |
|---|---|
| `POST /v1/parse` | Tek adres → `ParseResult` |
| `POST /v1/parse/batch` | JSON dizi, en fazla 1.000 adres |
| `POST /v1/parse/csv` | Sep ile akış halinde CSV giriş/çıkış (sütun adı parametre) |
| `POST /v1/validate` | Yapılandırılmış adres → tutarlılık raporu |
| `GET /v1/autocomplete?q=&level=` | il/ilçe/mahalle önerisi (OutputCache) |
| `GET /v1/reference/il`, `/il/{plaka}/ilce`, `/ilce/{id}/mahalle` | Referans veri |
| `GET /health/live`, `/health/ready` | Sağlık kontrolü |

- [ ] .NET 10 minimal API:
  - `AddOpenApi` + **Scalar** UI (`.WithProxy(null)`)
  - `AddValidation`, `AddProblemDetails`
  - `AddRateLimiter` (IP bölümleme + `UseForwardedHeaders`, 429 + `Retry-After`)
  - HybridCache (POST parse için), OutputCache (GET)
- [ ] **KVKK:**
  - ham adres loglanmaz (yalnızca uzunluk, süre, güven)
  - `/privacy` sayfası, durumsuz çalışma
  - README'de "self-host önerilir" notu
- [ ] OpenTelemetry: özel `Meter` (parse süresi histogramı, güven histogramı, geri dönüş oranı); yerelde Aspire dashboard
- [ ] Testler: `WebApplicationFactory` ile entegrasyon testleri, sözleşme (OpenAPI) snapshot'ı
- [ ] Docker:
  - `dotnet publish /t:PublishContainer`, `-noble-chiseled` (ICU'suz çekirdek sayesinde)
  - linux-x64 + linux-arm64 → **GHCR**
- [ ] `docker-compose.yml` (API + Aspire dashboard)
- [ ] **Dağıtım:** Azure Container Apps (sıfıra ölçeklenir, ücretsiz kota) + GitHub Actions ile otomatik dağıtım; alternatif Render
- [ ] Yük testi (k6 veya `bombardier`): p95 ve RPS değerlerini README'ye yaz

**Bitti sayılır:** herkese açık URL'de Scalar dokümantasyonu var, `curl` örneği çalışıyor, imaj GHCR'da.

---

### Faz 7 — Playground (canlı demo) · ~1 hafta

**Hedef:** README'deki GIF'in kaynağı. Bir işe alımcının 10 saniyede "vay" demesi.

- [ ] **Blazor WASM** (önerilen): parser **tarayıcıda** çalışır, adres cihazdan çıkmaz. Bu KVKK açısından güçlü bir mesaj ve çekirdeğin ICU'dan bağımsız olmasının ödülü.
  - Alternatif: Angular + API (daha önceki deneyimine uyar ama sunucuya bağımlı)
- [ ] Ekran:
  - girdi kutusu
  - **renkli span vurgulama** (her bileşen ayrı renkte)
  - bileşen tablosu + güven çubukları
  - düzeltme logu
  - kanonik çıktı ve JSON (kopyala)
- [ ] "Örnek dene" düğmeleri: challenge setinden 8–10 zor örnek
- [ ] Toplu mod: CSV yapıştır → temizlenmiş CSV indir
- [ ] "Yanlış mı ayrıştırdı?" → önceden doldurulmuş GitHub issue linki (adres kullanıcının onayıyla eklenir)
- [ ] Dağıtım: GitHub Pages (Actions ile), özel 404/SPA ayarı
- [ ] Erişilebilirlik ve mobil kontrolü, açık/koyu tema

**Bitti sayılır:** `alpercna.github.io/AdresTR` canlı, README'de 15 saniyelik GIF var.

---

### Faz 8 — Dokümantasyon, benchmark yayını, lansman · ~1,5 hafta

**8a. Dokümantasyon:**
- [ ] README (EN) + README.tr.md:
  - hero GIF, "neden", 3 satırlık hızlı başlangıç
  - **benchmark tablosu** (AdresTR vs regex vs libpostal vs LLM), rozetler
  - veri lisansı özeti
- [ ] DocFX sitesi (GitHub Pages): kavramlar, API referansı, "Türkçe I problemi" sayfası, veri güncelleme rehberi
- [ ] `docs/architecture.md` + bir diyagram; ADR'ler güncel
- [ ] `CONTRIBUTING.md`: yeni kısaltma veya semt ekleme rehberi; 5–10 adet `good first issue`

**8b. Veri seti yayını:**
- [ ] Hugging Face: `AlperCna/turkish-address-benchmark`
  - `synthetic` (CC-BY-4.0) ve `osm` (ODbL) konfigürasyonları
  - veri kartı (kaynak, gürültü tarifi, bilinen yanlılıklar, KVKK beyanı), canary GUID

**8c. Lansman** (sırayla):

| Zaman | Adım |
|---|---|
| T-1 hafta | 5–10 .NET geliştiriciye sessiz lansman, geri bildirim al, v0.9 |
| T-1 hafta | Yazılar: TR (Medium/dev.to) "Türkçe adresleri neden kimse düzgün ayrıştıramıyor?" ve EN "Building the first open Turkish address benchmark" |
| Gün 1 (Salı/Çarşamba) | LinkedIn (TR) + X dizisi + TR blog yazısı; r/CodingTR |
| Gün 2 | **Show HN** (ABD sabahı), r/dotnet (izinli günlerde), r/csharp |
| Hafta 1 | Her issue'ya 24 saat içinde yanıt; v1.0.1 düzeltme sürümü |
| Hafta 3–6 | awesome-dotnet, awesome-turkish-language-models ve awesome-tr'ye PR (her listeye tek link) |
| Ay 2–3 | Topluluk sunumu (dotnet İstanbul / Kommunity, Microsoft Reactor); "öğrendiklerim" yazısı; Webrazzi'ye benchmark haberi |

- [ ] Profil:
  - repoyu sabitle (pin)
  - profil README'sindeki "Featured" tablosunun en üstüne ekle
  - LinkedIn Featured bölümü
  - CV satırı **gerçek sayılarla**, örnek: "2.000 adreslik TR benchmark'ında mahalle Acc@1 0,xx, libpostal 0,yy; 5k adres/sn"

**Bitti sayılır:** v1.0.0 NuGet'te; HF veri seti, canlı demo, iki yazı ve lansman gönderileri yayında.

---

### Faz 9 — v1.x genişleme (isteğe bağlı, öncelik sırasıyla)

1. **Sokak doğrulama paketi:**
   - `AdresTR.Streets`: kullanıcının kendi getirdiği veya ayrı lisanslı sokak verisi
   - mahalle kapsamında SymSpell (token düzeyinde)
   - ADR-004'e uygun
2. **Entegrasyon paketleri:**
   - `AdresTR.Extensions.AspNetCore` (model binding, `[TurkishAddress]` doğrulama özniteliği)
   - FluentValidation kuralı
   - Bu, ssg/TurkishId'nin indirme sayısını büyüten yoldu.
3. **İstemci SDK'ları:** OpenAPI'den Python (`httpx`) ve TypeScript istemcisi → PyPI ve npm (trusted publishing)
4. **Veri güncelleme otomasyonu:** aylık GitHub Action (kaynak değişikliği fark raporu → PR) ve sürümlü veri yayını
5. **ML yolu:**
   - Python'da BERTurk-128k token sınıflandırıcı; benchmark + playground düzeltmelerinden toplanan etiketlerle eğitilir
   - ONNX int8 → `AdresTR.Ml.Onnx` (isteğe bağlı paket, model talep üzerine indirilir)
   - yalnızca düşük güvende devreye girer, gazetteer yine doğrular
6. **LLM geri dönüşü:**
   - API'de `Microsoft.Extensions.AI` ile, isteğe bağlı ve varsayılan kapalı
   - LLM önerir, gazetteer doğrular
7. **Adres eşleştirme (aynı yer mi?):**
   - Hepsiburada probleminin kendisi
   - GeoMind tarifi: BM25 + dense retrieval + cross-encoder; kanonik kimlikler üzerinden blocking
8. **Native AOT C ABI:** Python ve Node'dan doğrudan çağrılabilen paylaşımlı kütüphane

---

## 4. Takvim (tahmini, haftada 15–20 saat)

| Hafta | Faz |
|---|---|
| 1 | Faz 0 + Faz 1 başlangıç |
| 2 | Faz 1 |
| 3 | Faz 2 + Faz 3 başlangıç |
| 4 | Faz 3 |
| 5–7 | Faz 4 |
| 7 | Faz 5 (v0.1 önizleme) |
| 8 | Faz 6 |
| 9 | Faz 7 |
| 10–11 | Faz 8 → **v1.0** |
| 12+ | Faz 9 |

**Kısa yol** (zaman daralırsa): Faz 0 → 1 → 2 → 3 (yalnızca sentetik + 200 gerçek örnek) → 4 → 5 → 7 (WASM, API'siz) → 8. API (Faz 6) v1.1'e kayar.

---

## 5. Riskler ve önlemler

| Risk | Olasılık | Etki | Önlem |
|---|---|---|---|
| Veri lisansı belirsiz (NVİ/PTT) | Yüksek | Yüksek | MIT lisanslı PTT türevleri + CC0 Wikidata; yazarlardan izin; sokak verisi paket dışı; `LICENSE-DATA.md` şeffaflığı |
| Veri eskimesi (6360, yeni il/ilçe) | Orta | Orta | Sürümlü veri, dış dosya yükleme, aylık fark raporu (Faz 9.4) |
| Belirsiz mahalle adları doğruluğu düşürür | Yüksek | Orta | Düşük güven + alternatifler; posta kodu ve ilçe kanıtı; dürüst raporlama |
| Semt alias tablosu eksik kalır | Yüksek | Orta | Büyük şehirlerden başla; issue şablonu ile topluluk katkısı |
| Gerçek test verisi az | Orta | Yüksek | Resmi kurum adresleri + OSM POI + elle challenge seti; sentetik veriyle karıştırmadan ayrı raporla |
| Türkçe I / ICU hataları | Orta | Yüksek | ADR-002 + invariant-mode CI job'u |
| Kapsam kayması (ML'e erken atlamak) | Orta | Orta | Faz sırası sabit; ML yalnızca Faz 9'da |
| KVKK (barındırılan API) | Düşük | Yüksek | Log yok, durumsuz çalışma, WASM playground, self-host rehberi |
| Motivasyon ve süre | Orta | Yüksek | Her faz kendi başına gösterilebilir bir çıktı üretir; Faz 5'te erken bir NuGet yayını |

---

## 6. Alınan kararlar (2026-10-08)

| Konu | Karar |
|---|---|
| Ad | **AdresTR**; repo [AlperCna/AdresTR](https://github.com/AlperCna/AdresTR) |
| Playground | **Blazor WASM**; parser tarayıcıda çalışır |
| Lisans | Kod **MIT**, veri kaynak bazında (`data/LICENSE-DATA.md`) |
| README | İngilizce ana dosya + `README.tr.md` |
| Veri yolu | Pakete yalnızca **PTT türevi (MIT) + Wikidata (CC0)** girer. NVİ kopyası yalnızca fark raporu için kullanılır; izin gelirse birincil kaynak olur (`docs/research/veri-analizi.md` §11). |

---

## 7. Araştırma sonrası plan güncellemeleri

Ayrıntılar `docs/research/` altındaki raporlarda.

| Faz | Güncelleme | Kaynak |
|---|---|---|
| 1 | **Wikidata NVİ kimliklerini CC0 ile taşıyor:** P12883 (mahalle `kimlikNo`), P13588 (köy), P14366 (ilçe), P14358 (plaka). Plaka için P395 kullanılmamalı (Konya ve Uşak yanlış). | veri-analizi.md §7 |
| 1 | İki PTT kaynağı (muratgozel ve epigra) pratikte aynı 2022-08 anlık görüntüsü. PTT "semt" sütunu dağıtım bölgesi adı; 364 tanesi gerçek alias adayı. | veri-analizi.md §5 |
| 1 | 6360 sayılı Kanun'a ait köy→mahalle alias'ları Wikidata P2123'ten otomatik üretilebilir (16.505 kayıt). | veri-analizi.md §10 |
| 1 | Sıkıştırma **Brotli yerine ZLib**: .NET 10 tarayıcı (WASM) build'lerinde Brotli desteklenmiyor. | teslimat-altyapisi.md §5 |
| 2 | ✅ Tamamlandı. CsCheck özellik testleri gerçek bir uç durum buldu: `ı` + U+0307. | — |
| 3 | Gerçek veri: **İBB Açık Veri** (sağlık tesisi 20.469 satır, muhtarlık 963), **İzmir Açık Veri** (CC BY 4.0; eczane 2.036 satır, en gürültülü gerçek metin), OSM (ODbL, ayrı set). TBB izin gerektiriyor; MEB kazınmayacak. | gercek-veri-kaynaklari.md |
| 3–4 | 300 satırlık kısaltma taslağı (`data/curated/abbreviations.draft.csv`) ve 18 hata kalıbı. Not: `pk` = posta kutusu, `nolu` genelde kapı numarası değil. | yazim-hatalari.md |
| 4 | Şablon **pelias/parser**: kapsanan karakter × güven × (1 − ceza). Yapı için soldan sağa beam search; il → ilçe → mahalle için hiyerarşik beam (her seviyede "yok" adayı). 27 skor özelliği; güven top-N uzlaşısından hesaplanıp isotonic/Platt ile kalibre edilir. 58 snapshot test vakası hazır. | parser-tasarimi.md |
| 4 | Snapshot testleri için **Verify kullanılmayacak**: paket lisansı bakım ücreti/muafiyet beyanı istiyor. Kendi JSON snapshot yardımcımız yazılacak. | — |
| 5–8 | Actions sürümleri: checkout@v7, setup-dotnet@v6, upload-artifact@v7, codecov@v7, release-please@v5. MinVer + release-please (`simple`) birlikte çalışıyor (GitHub App token gerekli). NuGet uzun ömürlü anahtarları 2026-11-01'de bitiyor → Trusted Publishing. | teslimat-altyapisi.md |
| 6 | Azure Container Apps: `--logs-destination none`, GHCR'dan çekme, max 1 replika, $1 bütçe alarmı. Öğrenci kredileri büyük olasılıkla geçerli değil. KVKK: yurt dışında barındırma = Madde 9 aktarımı → playground ana demo, API durumsuz ve loglamasız. | teslimat-altyapisi.md §4, §8 |
| 7 | Tek GitHub Pages sitesi: playground `/AdresTR/`, DocFX `/AdresTR/docs/`. | teslimat-altyapisi.md §6 |
| 9 | Model: **ELECTRA-small Türkçe** (13,7M parametre, MIT, int8 ile ~14 MB), BERTurk'ten damıtılır. .NET `BertTokenizer` için `LowerCaseBeforeTokenization=false` ve `RemoveNonSpacingMarks=false` **zorunlu** (Türkçe I hatası). Kural tabanlı eşleştirici ML'den bağımsız olarak erken yayımlanabilir. | ml-ve-eslestirme.md |

### Durum

- [x] Faz 0: repo, CI (linux, windows, ICU'suz), ADR'ler
- [x] Faz 2: `TurkishText`
- [x] Faz 1: gazetteer 2026.10 (PTT MIT + Wikidata CC0), ikili format, DataBuilder, CI kontrolü
- [x] Faz 3: sentetik/gerçek/zor vaka setleri, değerlendirici, regex + libpostal baseline, CI doğruluk kapısı
- [x] Faz 4: parser MVP. Test tam eşleşme: sentetik %97,4 · gerçek %95,8 · zor vakalar %86,8 (libpostal: %10,0 / %26,2 / %24,6)
- [x] Faz 5: `AdresTR` ve `AdresTR.Data` 0.1.0-preview.1 NuGet'te (Trusted Publishing, tag ile tetiklenen release.yml), GitHub Release, örnekler, ikon
- [ ] Veri sahiplerine izin e-postaları: melihozkara (NVİ kopyası), TBB (şube listesi)
