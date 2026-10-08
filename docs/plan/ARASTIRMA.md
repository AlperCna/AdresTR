# AdresTR — Araştırma Raporu

> Tarih: 2026-10-08 · Bu belge PLAN.md'deki kararların dayanağıdır.
> **[DOĞRULANMADI]** işaretli maddeler birincil kaynaktan teyit edilemedi.

---

## 1. Problem ve boşluk

**Problem:** Türkiye'de adresler serbest metin yazılıyor: `kadikoy caferaga mh moda cd no:12 d3`, `atillamahallesi475sokibrahimapartno20kat4daire4`. E-ticaret ve kargo için bu, yanlış teslimat ve manuel düzeltme maliyeti demek.

**Sektör kanıtı:** Hepsiburada, TEKNOFEST 2025'te "Yapay Zeka Destekli Adres Çözümleme Hackathonu" düzenledi. Konu son kilometre teslimatındaki adres tutarsızlıklarıydı. Kaggle aşamasında yaklaşık 255 takım, yaklaşık 850 bin adres ve 10 binden fazla etiket vardı. Ödüller 120 bin, 100 bin ve 90 bin TL'ydi.
- Görev aslında **eşleştirme/sınıflandırma** idi: adresi opak bir bölge etiketine atamak. Ayrıştırma (parsing) değildi.
- **Veri seti özel.** Kaggle yarışması davetiyeli, veriyi kullanma hakkımız yok. Bir katılımcının CC0 diye yeniden yüklediği kopya da kullanılmamalı.
- Kazanan yaklaşımlar:
  - GeoMind (1.): BGE-M3 + BM25 ile aday bulma, ardından BERTurk cross-encoder ile yeniden sıralama.
  - Atom (2.): BERTurk sınıflandırıcı + DBSCAN.
  - Hiçbiri yeniden kullanılabilir bir kütüphane değil.

**Mevcut açık kaynak durumu (GitHub API, 2026-10-08):**

| Ne var | Durum |
|---|---|
| il/ilçe/mahalle **veri setleri** | Talep yüksek: melihkorkmaz 411★, emreuenal 156★, melihozkara 101★ |
| Serbest metin Türkçe adres **ayrıştırıcı** | Yok denecek kadar az. En iyisi `mtarikozcan/turkish-address-system` (1★, terk edilmiş, F1 iddiası doğrulanmamış). |
| NuGet'te Türkçe serbest metin ayrıştırıcı | **Yok.** Sadece il/ilçe arama paketleri var. |
| Açık, etiketli Türkçe adres veri seti | **Yok.** deprem-ml NER modelinin verisi özel. |
| libpostal (4.9k★) | Türkçe sözlüğü çok küçük (2016-17). `mh`, `sitesi`, `blok`, `küme evler`, `OSB` gibi kalıplar eksik. **Mahalle/ilçe etiketi yok.** Son sürüm 2018. |
| Deepparse | Türkiye ne eğitim ne zero-shot ülkeleri arasında. Mahalle için bir alan yok. |
| Google Address Validation API | **Türkiye'yi desteklemiyor.** Geocoding API çalışıyor ve 1.000 istek başına 5 $. |
| Temsor `/v1/tr/address/parse` | HTTP 410, kullanımdan kaldırılmış. |
| Başarsoft GeoCoder | Ticari, kapalı, fiyatı yayınlanmamış. |

**Sonuç:** Ücretsiz, çevrimdışı çalışan, açıklanabilir, resmi hiyerarşiye göre doğrulayan bir Türkçe adres kütüphanesi yok. .NET ekosisteminde ise hiç yok.

---

## 2. Türk adres yapısı (resmi)

**Mevzuat:** Adres ve Numaralamaya İlişkin Yönetmelik (RG 31.07.2006/26245). Son değişiklik 24.12.2025.

**Hiyerarşi:** il → ilçe → (bucak/köy/mezra/belediye) → mahalle → meydan/bulvar/cadde/sokak/küme evler → site/blok/mevki → dış kapı no → iç kapı no → posta kodu.
- Zorunlu alanlar: il, ilçe, dış kapı no, posta kodu.
- Birden fazla bağımsız bölüm varsa iç kapı no da zorunlu.

**UAVT adres kodu:** 10 haneli. Bina başına değil, bağımsız bölüm (daire, dükkân) başına verilir. Kapı numarası değişse de sabit kalır.

**Resmi sayılar** (e-icisleri):

| Birim | Sayı |
|---|---|
| İl | 81 |
| İlçe | 973 (922 + 51 merkez ilçe) |
| Mahalle | 32.312 |
| Köy | 18.214 |
| Bağlı birim (mezra vb.) | 23.571 |

- **Önemli:** NVİ'nin "mahalle" listesi 73.398 kayıt içerir. Mahalle, köy, mezra ve mevkiyi bir `mahalleTur` alanıyla karıştırır. Bu tür alanını modellemek gerekiyor.
- Sokak (CSBM) sayısı: **1.276.922** (NVİ, 2026-10-06).

**Değişim hızı:**
- 6360 sayılı Kanun (2012, 2014 seçimlerinde yürürlüğe girdi) yaklaşık 16.000 köyü mahalleye çevirdi ve 1.000'den fazla beldeyi kaldırdı. **2014 öncesi veri setleri ciddi biçimde yanlış.**
- NVİ'de Nisan→Ekim 2026 arası sokak düzeyinde +9.032 / −2.939 kayıt ve 1.957 isim değişikliği var. Yani her yarım yılda yaklaşık %1 değişim.
- Bekleyen değişiklikler **[DOĞRULANMADI]**:
  - Mahalleye çevrilen köylere yeniden tüzel kişilik verilmesi için bir kanun teklifi komisyonda (2026-03).
  - Yaklaşık 25 ilçenin il yapılması planı ("106 il").
- → **Veri sürümlenmeli.** Tarihli anlık görüntüler tutulmalı ve kararlı anahtar olarak NVİ `kimlikNo` kullanılmalı.

---

## 3. Veri kaynakları ve lisanslar (en büyük teknik dışı risk)

| Kaynak | İçerik | Lisans / risk |
|---|---|---|
| **adres.nvi.gov.tr** | En güncel ve eksiksiz kaynak | reCAPTCHA var, lisans belirtilmemiş, robots.txt yok. **CAPTCHA aşma yapmayacağız.** |
| **PTT posta kodu** | mahalle → posta kodu | Eski toplu indirme dosyası (`pk_list.zip`) artık **404** veriyor. Yeni sitede yalnızca arama var. Kullanım şartı bulunamadı. |
| melihozkara/il-ilce-mahalle-sokak-veritabani | 81 / 973 / 73.398 / 1.276.922, NVİ `kimlikNo` ile, 2026-10-06 tarihli | **Lisans yok.** En güncel kaynak. Yazarından CC0/CC BY lisansı istenebilir. |
| muratgozel/turkey-neighbourhoods | 81 / 973 / 73.305 mahalle / 2.771 posta kodu, 2024-03 | **MIT**, PTT kaynaklı, en temiz anlık görüntü |
| epigra/tr-geozones | 73.304 mahalle + posta kodu, 2022-08 | **MIT**, PTT kaynaklı |
| emreuenal/... | 1.148.699 sokak, 2021 | **GPL-3.0** (MIT ile uyumsuz), eski (970 ilçe) |
| **Wikidata** | 81 il, yaklaşık 1.050 ilçe (kaldırılanlar dahil), 27.975 mahalle (22.595'i koordinatlı), 19.394 köy | **CC0.** Koordinat için en güvenli kaynak. |
| **HDX COD-AB-TUR** | il ve ilçe sınırları (973) | CC BY-IGO, atıf gerekir |
| **OpenStreetMap** (Geofabrik, 619 MB) | addr:street 261 bin, housenumber 244 bin (binaların yaklaşık %4'ü), admin_level=8 için 13.8 bin ilişki (eksik) | **ODbL.** Türetilen veritabanı da ODbL (share-alike) olmak zorunda. Ayrı bir paket olarak tutulmalı. Test korpusu için iyi, otorite kaynak olarak değil. |
| OpenAddresses | Türkiye kaynağı **yok** | — |
| Faker tr_TR / Bogus `tr` | Faker'da TR adres yok. Bogus'ta 41 sokak var, ilçe ve mahalle yok. | İşe yaramaz. Sadece "naif sentetik" karşılaştırması için. |

**Posta kodu analizi** (PTT kaynaklı 73.305 satır):
- İlk 2 hane **%100 il plaka kodu**.
- 2.771 farklı kod var. Her mahallenin tam olarak **1** kodu var.
- Bir kod medyan 8, en fazla 669 mahalleyi kapsıyor.
- → Posta kodu güçlü bir **il** kısıtı ve orta güçte bir **ilçe** kısıtı sağlar.

**Hukuk:**
- Yer adları olgudur, telif konusu değildir.
- FSEK Ek Madde 8 veritabanı hakkı **tam bir NVİ sokak tablosunu** yeniden dağıtmayı riskli kılar.
- il/ilçe/mahalle isim listeleri düşük risklidir. **Sokak verisi varsayılan pakete konmayacak.**

**KVKK:**
- Gazetteer kişisel veri içermez.
- **Kullanıcının gönderdiği adres kişisel veridir.** API'yi işleten kişi veri sorumlusu olur.
- Önlemler:
  - Ham adresi loglamamak.
  - Durumsuz (stateless) parse.
  - Kendi sunucunda barındırma (self-host) seçeneği.
  - Tarayıcıda çalışan playground.
- LLM'e adres göndermek varsayılan olarak kapalı olmalı.

---

## 4. Türk adreslerinde gerçek hatalar

Kaynaklar: Hepsiburada örnek satırları, hackathon repoları, akademik çalışmalar.

- **Kısaltmalar:**
  - mah/mh/mahallesi, cad/cd/caddesi, sok/sk/sokağı, blv/bulv, apt/ap, no:/no., k:/kat, d:/daire, blok, sitesi, mevkii, küme evler, OSB.
  - `D` hem daire hem doğu, `K` hem kat hem kuzey olabilir. Hangisi olduğu konuma bakarak çözülür.
- **Yapışık yazım:** `147sok`, `atillamahallesi475sok...`
- **Numaralı sokaklar:** `1203/5 Sk.` (İzmir), `864 sokak`, `127 nolu sokak`. Buradaki sayı bir isimdir, kapı numarası değildir.
- **Daire gösterimi:** `No:17/5` (bina/daire), `17/A`, `K:3 D:5`, `kat 3 daire 5`.
- **Kırık İ:** `i smet`, `ati lla`. Python/JS `"İ".lower()` sonucu i + U+0307 (birleşik nokta) üretir, noktalama temizliği de bunu böler.
- **ASCII yazım:** ı→i, ş→s, ğ→g, ü→u, ö→o, ç→c.
- **Yazım hataları:** `mahlesi`, `mhallesi`, `maallesi`, `sokar`, `cadessi`, `bulvr`, `nomara`, `cumhuriye`, `iz mir`.
- **Tekrar ve fazlalık:** `konak konak izmir`, sondaki `tr`, telefon numarası, işyeri adı, "… karşısı" gibi tarifler.
- **Semt ≠ mahalle:**
  - Moda bir semttir. Resmi mahallesi Caferağa (Kadıköy).
  - Bağdat Caddesi iki ilçeden geçer.
  - → Bir **semt alias tablosu** gerekli.
- **Eski köy adları:** 6360 sonrası aynı yer için hem "X Köyü" hem "X Mahallesi" yazılıyor.
- **Belirsiz mahalle adları:** Cumhuriyet, Atatürk, Yeni, Fatih gibi adlar yüzlerce ilçede var. İlçe ya da posta kodu kanıtı yoksa düşük güven ve alternatifler döndürülmeli.
- **"Merkez"** sahte bir ilçe adıdır.
- Bazı il ve ilçe adları çakışıyor. Bazı mahalle adları da ilçe adlarıyla aynı.

**Akademik:**

| Çalışma | Yöntem | Bulgu |
|---|---|---|
| Ünal vd. (Huawei TR), arXiv 2306.13947 | Token tagging, 1.248 sorgu | Makro F1 yaklaşık 0,45–0,50 |
| deprem-ml BERTurk-128k NER | NER | Makro F1 0,84. Zayıf sınıflar: **bina 0,70, kapı no 0,71** → bu alanlarda kurallar ML'i yener |
| Kürklü & Akagündüz (ODTÜ), SIU 2024 | T5 ile standardizasyon | — |
| Matcı & Avdan (2018) | Kural tabanlı standardizasyon | Geocoding başarısı ciddi biçimde arttı |
| Yıldırım vd. (İTÜ, 2014) | Geocoding hata kaynakları | Hataların %35'i kapı numarası aralıklarından, %28'i sokak veritabanından, %15'i eksik adresten |

Türkçe adres için yayımlanmış bir CRF veya BiLSTM-CRF çalışması **yok**. Bu bir boşluk.

---

## 5. Teknik bulgular (.NET 10)

### Türkçe metin ve .NET

Aşağıdakiler .NET 10.0.3'te **ölçüldü**:

| İfade | ICU açık | Invariant mode |
|---|---|---|
| `"İ".ToLowerInvariant()` | U+0130 değişmeden kalır (U+0307 üretmez) | U+0130 |
| `"I".ToLower(tr-TR)` | `ı` | `i` → **Türkçe kurallar sessizce kaybolur** |
| `OrdinalIgnoreCase("İSTANBUL","istanbul")` | False | False |
| `IgnoreNonSpace` ile `"Kadıköy"` / `"kadikoy"` | **Eşleşmez** (ı ayrışmaz) | Eşleşmez |
| `Normalize(FormD)` | Çalışır | **İşlem yapmaz** |

- Chiseled ve Alpine Docker imajları varsayılan olarak invariant moddadır.
- → **Çekirdek kütüphane ICU'ya bağımlı olmamalı.** Elle yazılmış, tablo tabanlı bir `TurkishText.FoldKey` kullanılacak: ç→c, ğ→g, ı/I/İ→i, ö→o, ş→s, ü→u, â→a, î→i, û→u. Ardından U+0300–036F aralığı silinir, NBSP boşluğa çevrilir, boşluklar daraltılır.
- CI testleri **iki kez** koşulacak, ikincisi `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` ile.

### Bulanık eşleştirme

1 milyon sentetik sokak adı üzerinde **ölçüldü**:

| Ölçüm | Sonuç |
|---|---|
| 1M ad, `string` nesnesi olarak | 63 MB |
| Trigram indeksi | 66 MB |
| Global trigram + OSA sorgusu | p50 **20 ms**, p99 32 ms |

- → Global bulanık arama 10 ms hedefini kaçırır. **Hiyerarşik kapsam şart:** önce il/ilçe, sonra o kapsam içinde arama.
- Kütüphaneler:
  - **SymSpell** 6.7.3 (MIT): token düzeyinde yazım düzeltme.
  - Kendi yazılacak, bant sınırlı, bellek ayırmayan OSA/Damerau.
  - Kendi token trie'si (Aho-Corasick benzeri).
  - `SearchValues<string>` hangi kalıbın eşleştiğini söylemez, etiketleme için uygun değil.
- **Lucene.NET** hâlâ beta. **pg_trgm** kütüphane içi çalışmaya uygun değil (Türkçe LC_CTYPE ile bilinen tarihsel bir çökme var).

### Veri depolama
- Özel bir ikili gazetteer:
  - UTF-8 string havuzu + offset dizileri, ad başına bir `string` nesnesi yerine.
  - Üst kimlikli varlık tabloları, alias tablosu, token → varlık posting listeleri.
  - Yerleşik **Brotli** ile sıkıştırma.
- nuget.org paket sınırı yaklaşık **250 MB**.
- Tembel yükleme. Dış veri dosyası yolu desteklenecek; böylece veri, kod yayımlamadan güncellenebilir.

### ML ve LLM (sonraki fazlar)

| Seçenek | Durum |
|---|---|
| CRF (.NET) | Bakımı yapılan bir kütüphane yok |
| ML.NET NER | TorchSharp önizleme aşamasında, libtorch gerekir → önerilmiyor |
| **ONNX** | Python'da BERTurk-128k eğit, int8 ONNX'e dönüştür. .NET'te **Microsoft.ML.OnnxRuntime 1.30.0** + **Microsoft.ML.Tokenizers 2.0.0** ile çalıştır. ORT paketi yaklaşık 150 MB → **isteğe bağlı ayrı paket**, model indirme talep üzerine. |
| LLM | **Microsoft.Extensions.AI 10.10.0** `GetResponseAsync<T>()` ile yapılandırılmış çıktı. LLM önerir, gazetteer doğrular. Sadece API'de, isteğe bağlı, KVKK nedeniyle varsayılan kapalı. |

### API (.NET 10 minimal API)
- Yerleşik bileşenler:
  - `AddOpenApi()` (OpenAPI 3.1)
  - `AddValidation()`
  - `AddProblemDetails()`
  - `AddRateLimiter` (IP'ye göre bölümleme + `UseForwardedHeaders`)
  - `AddOutputCache` (**yalnızca GET**; POST için HybridCache, katlanmış girdiyle anahtarlanır)
  - `AddHealthChecks`
- Paketler:
  - **Scalar.AspNetCore 2.17.14** (`.WithProxy(null)`)
  - **OpenTelemetry 1.19.x**
  - **Sep 0.17.1** (CSV akışı)
  - Asp.Versioning.Http 10.2.3 (tek sürüm için `/v1` route grubu yeterli)

### Ücretsiz barındırma (2026)

| Seçenek | Durum |
|---|---|
| **Azure Container Apps** | Aylık ücretsiz kota: 180 bin vCPU-s, 2M istek, sıfıra ölçeklenir → **en uygun** |
| Render | Ücretsiz, 15 dk boşta kalınca uyur |
| Fly.io / Railway | Kalıcı ücretsiz katman yok |
| Oracle Always Free | En çok RAM; kotası azalmış olabilir **[DOĞRULANMADI]** |
| Playground | Statik site → GitHub Pages / Cloudflare Pages |

### Mühendislik (NuGet sürümleri 2026-10-08)
- Test ve ölçüm:
  - **xunit.v3 4.0.1**
  - **Verify.XunitV3 33.3.2** (snapshot)
  - **CsCheck 4.9.1** (özellik tabanlı test)
  - **BenchmarkDotNet 0.15.8**
  - Stryker.NET (mutasyon testi)
- Kapsam ölçümü: `coverlet.collector` MTP ile **çalışmaz**. `Microsoft.Testing.Extensions.CodeCoverage` kullanılacak.
- Sürümleme: **MinVer 8.0.0** (`fetch-depth: 0` gerekir) + release-please.
- **NuGet Trusted Publishing (OIDC):** uzun ömürlü API anahtarları **2026-11-01'de sona eriyor** (Microsoft).
- Container: `dotnet publish /t:PublishContainer` ile çoklu mimari imaj → GHCR.
- Dependabot, CodeQL, conventional commits.
- Dokümantasyon: **DocFX 2.81** (MkDocs Material bakım modunda).
- Topluluk: Contributor Covenant 3.0.
- .NET 8 ve 9'un desteği **2026-11-10'da bitiyor** → sadece `net10.0` hedeflenecek.

---

## 6. Değerlendirme (benchmark) bulguları

- Kamuya açık bir Türkçe adres benchmark'ı **yok**. Yayımlamak başlı başına bir fark yaratır.
- Mevcut araçlar farklı metrikler raporluyor:
  - libpostal %99,45 → **tam ayrıştırma doğruluğu** (bütün dizi doğru olmalı).
  - deepparse ~%99 → **dizi başına etiket doğruluğu**.
  - Biz ikisini de adıyla raporlayacağız.
- Raporlanacak metrikler:
  - Alan bazında P/R/F1 (seqeval strict, mikro + makro)
  - exact match
  - kanonik ID doğruluğu
  - çözümleme Acc@1, Acc@5, MRR
  - doğrulayıcı için yanlış ret ve yanlış kabul oranları
  - kalibrasyon: ECE (15 kova), Brier, risk–kapsam eğrisi
  - gürültü türüne göre sağlamlık
  - hız: adres/sn, p50/p95
  - bootstrap %95 güven aralığı, sistem karşılaştırmasında McNemar
- Test boyutu ve %95 güven aralığı (p≈0,9):

  | n | Güven aralığı |
  |---|---|
  | 500 | ±2,6 puan |
  | 1.000 | ±1,9 puan |
  | 2.000 | ±1,3 puan |

  → dev ≈ 1.000; test ≈ 2.000 (en az 500'ü gerçek adres); artı 200–300 elle yazılmış zor vaka.
- Etiketleme araçları:
  - Label Studio 1.23.2 (28,4k★)
  - Argilla (Hugging Face entegrasyonlu)
  - Etiketlerin %10'u çift etiketlenip Cohen κ raporlanacak.
- Hugging Face'te iki konfigürasyon: `synthetic` (CC-BY-4.0/CC0) ve `osm` (ODbL). Veri kartı ve canary GUID eklenecek.

---

## 7. Görünürlük ve lansman bulguları

- Hacker News gönderileri yıldızları artırıyor (Borges & Valente 2019; NAIST DiD çalışması, 18.253 repo).
- 2025'te 138 lansman üzerinde yapılan bir çalışma: ortalama ilk 24 saatte +121, ilk haftada +289 yıldız. Medyan çok daha düşük. "Show HN" önekinin kontrollü analizde ek etkisi yok.
- r/dotnet kendi projesini tanıtmayı belli günlerle sınırlıyor **[DOĞRULANMADI: hangi günler]**.
- Benzer Türk araçları:

  | Repo | Gösterge |
  |---|---|
  | zemberek-nlp | 1.369★ |
  | il-ilce-mahalle (melihkorkmaz) | 411★ |
  | vnlp | 291★ |
  | evilayet | 221★ |
  | ssg/TurkishId | 88★ ama **17.257 NuGet indirme** |
  | turkey-neighbourhoods | npm'de **ayda yaklaşık 14,6 bin** indirme |

  → Yıldız ile kullanım aynı şey değil. CV'ye indirme sayısı da yazılmalı. Gerçekçi hedef ilk yıl 100–300★.
- İşe alım tarafı:
  - HackerRank 2025: geliştiricilerin %66'sı gerçek dünya görevlerini tercih ediyor.
  - Forbes Türkiye 2026: beceri temelli işe alım öne çıkıyor.
  - Patika: özgün, tutorial olmayan projeler öne çıkıyor.
  - **Türkiye'ye özgü, GitHub'ın ağırlığını ölçen bir anket bulunamadı.**
- awesome listeleri:
  - quozd/awesome-dotnet (21,6k★; şartlar: kullanışlı, bakımlı, kararlı, belgelenmiş, test edilmiş)
  - awesome-turkish-language-models
  - miratcan/awesome-tr

---

## Kaynaklar (seçme)

- Hepsiburada / TEKNOFEST: [Webrazzi](https://webrazzi.com/2025/09/23/hepsiburada-nin-adres-cozumleme-hackathon-unun-detaylarini-alexey-shevenkov-ile-konustuk/) · [Kazananlar (DHA)](https://www.dha.com.tr/kurumsal/hepsiburada-e-ticarette-yapay-zeka-destekli-adres-cozumleme-hackathonunun-kazananlarini-acikladi-2720908) · [Şartname içeren repo](https://github.com/arch-yunus/Teknofest_adres_cozumleme_hackathon) · [GeoMind çözümü](https://github.com/huseyinardaarslan/teknofest-hepsiburada-address-matching)
- Mevzuat ve resmi sayılar: [Yönetmelik](https://www.alomaliye.com/2006/07/31/adres-ve-numaralamaya-iliskin-yonetmelik/) · [e-İçişleri sayılar](https://www.e-icisleri.gov.tr/Anasayfa/MulkiIdariBolumleri.aspx)
- Veri setleri: [melihozkara veri seti](https://github.com/melihozkara/il-ilce-mahalle-sokak-veritabani) · [turkey-neighbourhoods](https://github.com/muratgozel/turkey-neighbourhoods) · [HDX COD-AB-TUR](https://data.humdata.org/dataset/cod-ab-tur) · [Geofabrik taginfo TR](https://taginfo.geofabrik.de/europe:turkey/) · [OSMF Geocoding Guideline](https://osmfoundation.org/wiki/Licence/Community_Guidelines/Geocoding_-_Guideline)
- Mevcut araçlar ve rakipler: [libpostal TR sözlükleri](https://github.com/openvenues/libpostal/tree/master/resources/dictionaries/tr) · [Google Address Validation kapsamı](https://developers.google.com/maps/documentation/address-validation/coverage) · [Deepparse](https://deepparse.org/)
- Akademik ve ML: [arXiv 2306.13947](https://arxiv.org/abs/2306.13947) · [deprem-ml adres NER](https://huggingface.co/deprem-ml/adres_ner_v2_bert_128k) · [Deepparse makalesi](https://arxiv.org/pdf/2311.11846)
- .NET teknik: [.NET invariant mode](https://github.com/dotnet/runtime/blob/main/docs/design/features/globalization-invariant-mode.md) · [SymSpell](https://github.com/wolfgarbe/SymSpell) · [NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) · [nuget.org boyut sınırı](https://learn.microsoft.com/nuget/nuget-org/publish-a-package)
- Barındırma: [Azure Container Apps fiyatları](https://azure.microsoft.com/en-us/pricing/details/container-apps/)
- Lansman ve işe alım: [Show HN etkisi (arXiv 2511.04453)](https://arxiv.org/pdf/2511.04453) · [HackerRank 2025](https://www.hackerrank.com/reports/developer-skills-report-2025) · [awesome-dotnet](https://github.com/quozd/awesome-dotnet)
- KVKK: [Kamu kurumları adres yayımlama kararı (NTV)](https://www.ntv.com.tr/turkiye/karar-resmi-gazetede-yayimlandi-kamu-kurumlari-icin-kvkk-karari-1734844)
