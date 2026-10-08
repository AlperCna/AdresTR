# Gerçek değerlendirme verisi kaynakları

> Tarih: 2026-10-08 · Hedef: kişisel veri içermeyen, yeniden kullanılabilir **en az 500, en fazla 2.000 gerçek Türkçe adres** (PLAN.md Faz 3c "Gerçek set").
> Sayılar aksi yazılmadıkça bu tarihte dosya indirilip ya da sayfa sorgulanıp **ölçüldü**.
> **[DOĞRULANMADI]** = birincil kaynakta teyit edilemedi. Hukuki değerlendirmeler hukuki görüş değildir.
> Hata kalıpları için: [yazim-hatalari.md](yazim-hatalari.md).

---

## 0. Özet ve öneri

**İlk 3 kaynak:**

1. **İBB Açık Veri: Sağlık Kurum ve Kuruluşları + Muhtarlıklar + Semt Pazarları**
   - Yaklaşık 21 bin satır.
   - Serbest metin adresin yanında yapılandırılmış ilçe/mahalle ve koordinat var.
   - İBB Açık Veri Lisansı (CC BY 4.0 uyumlu).
   - Toplu indirme var. Efor düşük.
   - Gold il/ilçe/mahalle doğrudan sütunlardan gelir.
2. **İzmir BB Açık Veri: Eczane listesi + CBS uç noktaları (ASM, hastane, muhtarlık)**
   - Yaklaşık 2 bin serbest metin, yüzlerce yapılandırılmış kayıt.
   - CC BY 4.0. Toplu indirme var.
   - **En gürültülü gerçek metin:** yapışık token, ASCII, numaralı sokak, tarif, telefon. Parser'ı gerçekten zorlar.
3. **OpenStreetMap Türkiye `addr:*`**
   - Yaklaşık 244 bin housenumber ve 81 ilin hepsi. Tamamen yapılandırılmış.
   - ODbL. **Ayrı `osm` konfigürasyonu** olarak tutulmalı (ADR-005).
   - Ulusal kapsamı ve il çeşitliliğini sağlar.

**Yedek / koşullu kaynaklar:**

- **TBB şube listesi:** 9.017 şube, ulusal, serbest metin. Ancak **ticari kullanım yazılı izne bağlı.** İzin alınırsa 1 numaralı ulusal kaynak olur. Alınmazsa yalnızca yerel ve yayımlanmayan değerlendirmede kullanılır.
- **MEB okul adresleri:** UAVT düzeninde, çok kaliteli. Ancak robots.txt AI tarayıcılarını açıkça yasaklıyor, liste uç noktası doğrudan erişimi reddediyor ve sayfalarda "Tüm Hakları Saklıdır" yazıyor. **Kazıma önerilmez.** Bilgi edinme başvurusu ya da elle seçilmiş küçük bir challenge alt kümesi düşünülebilir.

**Önerilen karışım (yaklaşık 1.500 gerçek adres, test bölümü):**

| Alt küme | Satır | Neden |
|---|---|---|
| İBB sağlık tesisleri (kişi adlı kategoriler hariç, 39 ilçeye tabakalı) | 500 | Yarı standart metin. Gold mahalle sütunda. |
| İBB muhtarlıklar | 150 | Kişi adı yok, temiz metin. Mahalle = muhtarlığın mahallesi. |
| İBB semt pazarları | 100 | Kısaltma gürültüsü (`mh`, `cd`, `K.Bakkalköy`). Mahalle/cadde/sokak ayrı sütunlarda. |
| İzmir eczane listesi (gürültü türüne göre tabakalı) | 600 | En gerçekçi kullanıcı yazımı. |
| İzmir CBS (ASM, hastane, muhtarlık) | 150 | Tamamen yapılandırılmış. Metin şablonla üretilir ve bu ayrıca etiketlenir. |
| **Toplam (CC BY 4.0 lisanslı gerçek set)** | **≈1.500** | İki büyükşehir. Ulusal kapsam için OSM ayrı. |
| OSM POI (81 il, ayrı ODbL konfigürasyonu) | 500–1.000 | Ulusal çeşitlilik. Metin şablonla üretilir. |

**Bilinen zayıflık:** CC BY setinin tamamı İstanbul ve İzmir'den geliyor. Ulusal kapsam için TBB izni ya da Kadıköy, Gaziantep, Konya gibi küçük belediye setleri eklenebilir (bkz. §4).

---

## 1. Değerlendirme ölçütleri

| Ölçüt | Yüksek | Orta | Düşük |
|---|---|---|---|
| **Hacim** | ≥5.000 kayıt | 500–5.000 | <500 |
| **Adres kalitesi** (benchmark için) | Serbest metin **ve** yapılandırılmış alanlar (gold hazır) | Yalnızca serbest metin ya da yalnızca yapılandırılmış | Adres yok ya da yalnızca ilçe |
| **Lisans netliği** | Açık lisans (CC BY / ODbL) yazılı | Kullanım koşulu var ama kısıtlı | Belirsiz ya da "tüm hakları saklıdır" |
| **Efor** (düşük iyidir) | Düşük: tek dosya indirme | Orta: form/AJAX ya da çok sayfa | Yüksek: kazıma, engel, izin gerekir |

---

## 2. Karşılaştırma tablosu

| # | Kaynak | Hacim | Adres kalitesi | Lisans | Efor | Karar |
|---|---|---|---|---|---|---|
| A1 | İBB Sağlık Kurum ve Kuruluşları | **20.469** (Y) | Serbest metin + ilçe/mahalle + koordinat (Y) | İBB Açık Veri Lisansı (Y) | Düşük | ✅ **Öncelik 1** |
| A2 | İBB Muhtarlık Adres Bilgileri | 963 (O) | Serbest metin + ilçe/mahalle + koordinat (Y) | İBB AVL (Y) | Düşük | ✅ |
| A3 | İBB Semt Pazarları | 385 (D) | Mahalle/cadde/sokak sütunları, kısaltmalı (Y) | İBB AVL (Y) | Düşük | ✅ küçük |
| A4 | İBB Mezarlıklar | 580 (O) | Yalnızca ilçe/mahalle (D) | İBB AVL (Y) | Düşük | Gazetteer testi için |
| B1 | İzmir Eczane Listesi | **2.036** (O) | Gürültülü serbest metin + ilçe kodu + koordinat (Y) | CC BY 4.0 (Y) | Düşük | ✅ **Öncelik 2** |
| B2 | İzmir CBS uç noktaları (ASM 353, hastane 84, muhtarlık ~1.290…) | Yüzler (O) | Tamamen yapılandırılmış: ilçe/mahalle/yol/kapı no (Y) | CC BY 4.0 (Y) | Düşük | ✅ gold kaynağı |
| C1 | OSM Türkiye `addr:*` | **244.484** housenumber (Y) | Yapılandırılmış etiketler. Serbest metin yok (`addr:full` 297). (O) | ODbL, share-alike (Y ama kısıtlayıcı) | Orta | ✅ **Öncelik 3**, ayrı paket |
| D1 | TBB şube listesi | **9.017** (Y) | Serbest metin + il/ilçe (Y) | Ticari olmayan serbest, ticari için yazılı izin (O) | Orta (Drupal AJAX) | ⚠️ izin gerekli |
| D2 | MEB okul adresleri | On binlerce **[DOĞRULANMADI]** (Y) | UAVT düzeninde temiz metin (Y) | "Tüm Hakları Saklıdır"; robots.txt AI botlarını yasaklıyor (D) | Yüksek | ❌ kazıma yok |
| D3 | Sağlık Bakanlığı (ulusal tesis listesi) | — | Bulunamadı | — | — | ❌ (A1/B2 karşılıyor) |
| D4 | Eczacı odaları / nöbetçi eczane | Değişken | Serbest metin | Belirsiz **[DOĞRULANMADI]** | Yüksek | ❌ (B1 karşılıyor) |
| D5 | PTT şube, noter, YÖK, adliye, e-Devlet, TÜİK | — | — | — | — | ❌ toplu açık liste bulunamadı (§3.3) |
| D6 | Ankara Açık Veri | — | — | Lisans bulunamadı | — | ❌ (§3.4) |
| D7 | Küçük belediye portalları (Kadıköy, Gaziantep, Konya, Tuzla, Manisa) | Onlar–yüzler (D) | Değişken | Portal bazında **[DOĞRULANMADI]** | Orta | Ulusal çeşitlilik için opsiyonel |
| ✗ | Hepsiburada TEKNOFEST verisi (ve türevleri) | ~850 bin | Gerçek müşteri adresleri | Yarışma verisi, kullanım hakkı yok | — | ❌ **Kullanılmaz** (ARASTIRMA.md §1) |

---

## 3. Ayrıntılar

### 3.1 MEB okul adresleri

**Erişim yolu:**

- İl MEM sayfası: `https://www.meb.gov.tr/baglantilar/mem/index_ilmem.php?ILKODU=<il>`
- İlçe başına okul listesi: `https://www.meb.gov.tr/baglantilar/okullar/index.php?ILKODU=<il>&ILCEKODU=<ilçe>`
- Bu liste DataTables ile `okullar_ajax.php` adresine POST atarak doluyor. Alanlar `OKUL_ADI`, `HOST`, `YOL`.
- Her okulun adresi kendi sitesinde: `https://<HOST>.meb.k12.tr/tema/iletisim.php` ya da `…/meb_iys_dosyalar/<YOL>/okulumuz_hakkinda.html`.

**Adres biçimi (örnek):** `ÜNİVERSİTELER MAH. ODTÜ KÜME EVLERİ ANKARA FEN LİSESİ MERKEZ BİNA BLOK NO 158 ÇANKAYA / ANKARA`. UAVT düzeninde ve çok temiz. `küme evler` ve `blok` gibi nadir bileşenler de var.

**Engeller:**

- `meb.gov.tr` ve `*.meb.k12.tr` robots.txt dosyaları GPTBot, ClaudeBot, CCBot, Google-Extended, PerplexityBot gibi **AI tarayıcılarını ve arşivleyicileri `Disallow: /` ile yasaklıyor.** Genel `User-agent: *` kuralı yok.
- `okullar_ajax.php` doğrudan POST'a **"Erişim yetkiniz yok!"** yanıtı veriyor. Oturum ya da Referer kontrolü var.
- Okul sayfalarının altbilgisinde "M.E.B © - Tüm Hakları Saklıdır" yazıyor.
- Bulk download ve açık veri seti yok. GitHub ve Kaggle'da da hazır bir okul adresi seti bulunamadı.

**Hukuki not:** Tek tek adresler olgu niteliğinde, ama sistematik derleme FSEK'teki veri tabanı yapımcısı hakkına (md. 39) takılabilir **[DOĞRULANMADI]**. Robots.txt'nin açık AI yasağı da etik bir sınır.

**Değerlendirme:** Kalite yüksek, lisans ve etik açıdan kırmızı.

- **Kazıma önerilmez.**
- Alternatif 1: 4982 sayılı Kanun kapsamında MEB'e bilgi edinme başvurusu yapılıp okul adres listesi (CSV) istenebilir. Yeniden yayım hakkı ayrıca sorulmalı.
- Alternatif 2: challenge setine elle, kaynak URL'siyle en fazla birkaç düzine örnek alınabilir.

### 3.2 Sağlık Bakanlığı, hastane, ASM, eczane

- **Sağlık Bakanlığı Açık Veri Portalı** (`acikveri.saglik.gov.tr`) 2026-10-08 itibarıyla **tek veri seti** içeriyor: MammosighTR, bir görüntü seti. Tesis listesi yok.
- Bakanlık sitesinde toplu "sağlık tesisleri listesi" (Excel/CSV) **bulunamadı**. İl sağlık müdürlüklerinin ASM listeleri HTML sayfalarında, biçimleri ilden ile değişiyor **[DOĞRULANMADI]**.
- **Daha iyi kaynak: A1, İBB Sağlık Kurum ve Kuruluşları.**
  - Sayfa: [data.ibb.gov.tr/dataset/istanbul-saglik-kurum-ve-kuruluslari-verisi](https://data.ibb.gov.tr/dataset/istanbul-saglik-kurum-ve-kuruluslari-verisi)
  - Dosya: `saglik-tesisleri.xlsx` (1,5 MB)
  - Sütunlar: `Sağlık Tesisi Adı | Ana Kategori | Alt Kategori | İlçe Adı | Mahalle Adı | ADRES | Latitude | Longitude`
  - **20.469 satır.** Öne çıkan alt kategoriler: Eczane 5.806, Sağlık Diğer 2.460, Gözlükçü/Optik 1.672, Diş Hekimi 1.649, Doktor/Muayenehane 1.543, ADSM 1.317, Medikal 1.208, **ASM 1.057**, Veteriner 791, Psikolog 556, Özel Hastane 268, Devlet Hastanesi 81, Şehir Hastanesi 66, EAH 56, Üniversite Hastanesi 45.
  - `ADRES` biçimi `<yol> <tür>. No:<n> <MAHALLE>/<İLÇE>`, örneğin `Çayır Cad. No:25 F YALIKÖY/BEYKOZ`.
  - Gürültü: harfli no 4.745, `No:N /M` 3.847, numaralı sokak 912, yolsuz kayıt 1.396, çift boşluk 5.712. Ayrıca bozuk karakterler var (`Canakkale Sehıtlerı Cad.`).
  - **KVKK:** "Doktor/Muayenehane", "Psikologlar", "Diyetisyen", "Diş Hekimi" kategorilerinde tesis adı **kişi adı** (ör. `Op. Dr. …`). Bu kategoriler **tamamen dışlanmalı.** Diğerlerinde de yalnızca `ADRES` ile ilçe/mahalle alınmalı, ad alınmamalı. İBB lisansı kişisel veriyi zaten kapsam dışı tutuyor.
- **İzmir eczane listesi (B1):**
  - Sayfa: [acikveri.bizizmir.com/dataset/nobetci-eczaneler-ve-eczane-listesi](https://acikveri.bizizmir.com/dataset/nobetci-eczaneler-ve-eczane-listesi)
  - Dosyalar: `https://openfiles.izmir.bel.tr/111324/docs/eczane-listesi.csv` (ve `.xlsx`). Canlı API: `https://openapi.izmir.bel.tr/api/ibb/eczaneler`.
  - Sütunlar: `LOKASYON_Y;LOKASYON_X;ADI;TELEFON;ADRES;BOLGE_ID;BOLGE;ECZANE_ID;ILCE_ID`
  - **2.036 satır.** `ILCE_ID` 33 farklı değer alıyor, 196 satırda `-1`. Bu kodun UAVT ilçe kodu olup olmadığı **[DOĞRULANMADI]**. 2.033 satırda koordinat var.
  - `BOLGE` nöbet bölgesi (ör. `KARŞIYAKA 4`), resmi birim değil.
  - Gürültü düzeyi en yüksek kaynak (bkz. yazim-hatalari.md).
  - Eczane adları çoğunlukla eczacının adını taşır. **`ADI` ve `TELEFON` alınmamalı.** `ADRES` içindeki telefonlar maskelenmeli.
- **İzmir CBS sağlık uç noktaları (B2):**
  - Örnekler: `https://openapi.izmir.bel.tr/api/ibb/cbs/ailesagligimerkezleri` (353 kayıt), `…/cbs/hastaneler` (84 kayıt). `acilyardimistasyonu`, `agizvedissagligimerkezleri` gibi başka uç noktalar da var.
  - Alanlar: `ILCE, MAHALLE, YOL, KAPINO, ENLEM, BOYLAM, ADI, ACIKLAMA`. Yol türü yazılmıyor (ör. `YOL: "LOZAN"`).
  - **Gold için ideal.** Serbest metin yok.
- **Nöbetçi eczane (eczacı odaları):** Günlük değişen HTML. Kullanım koşulları belirsiz. B1 aynı ihtiyacı açık lisansla karşıladığı için gerek yok.

### 3.3 Belediye, muhtarlık, PTT, noter, banka, YÖK, adliye, e-Devlet, TÜİK

| Kaynak | Bulgu (2026-10-08) |
|---|---|
| **İBB Muhtarlık Adres Bilgileri** (A2) | [Sayfa](https://data.ibb.gov.tr/dataset/muhtarlik-adres-bilgileri). GeoJSON: `/dataset/muhtarlik-adres-bilgileri/resource/71f75529-…/geojson_download` (400 KB). **963 kayıt.** Alanlar: `Muhtarlık Adı, İlçe Adı, Mahalle Adı, Adres, Longtitude, Latitude`. **Muhtar adı yok.** Adres örneği: `Muhsin Yazıcıoğlu Cad. No:65 /1 SİLAHTARAĞA/EYÜPSULTAN`. |
| **İzmir Muhtarlıklar** | [Sayfa](https://acikveri.bizizmir.com/dataset/muhtarliklar). `izbb-muhtarliklar.csv`, yaklaşık 1.290 kayıt. Alanlar: `ILCE;KAPINO;ENLEM;ACIKLAMA;ILCEID;MAHALLE;ADI;BOYLAM;YOL`. **`ACIKLAMA` alanında muhtarın adı soyadı var: bu sütun atılmalı.** Yapılandırılmış gold olarak kullanılabilir. |
| **İBB Semt Pazarları** (A3) | `istanbul-ili-balkc-olan-semt-pazarlar_2025.xlsx`, **385 satır.** Sütunlar: `İlçe, Pazar Adı, Koordinat, Mahalle, Cadde, Sokak, Gün, Pazar Tipi`. Değerler kısaltmalı: `Yenişehir mh`, `İmar İskan Blokları cd`, `K.Bakkalköy mh`. |
| **TBB şube listesi** (D1) | [Sayfa](https://www.tbb.org.tr/banka-ve-sektor-bilgileri/banka-bilgileri/subeler). "Bugün itibariyle toplam şube sayısı: **9017**". Form alanları banka, şube tipi (Yurtiçi Şube/ATM…) ve il. **"Excel'e Aktar"** düğmesi var. Sonuç sütunları: `Banka, Şube Adı, Adres, İlçe, Şehir, Telefon, Faks, Açılış`. Örnekler: `Uşak Cad. No : 175 / A`, `Atatürk Cad. Ufuk Apt. Altı No : 8`, `Sümer Mah. Ankara Bulv. No:1`. Her banka kendi yazım stilini kullanıyor, bu yüzden **çeşitlilik yüksek.** robots.txt yalnızca `/core/` gibi teknik yolları kapatıyor. **[Kullanım koşulları](https://www.tbb.org.tr/kullanim-kosullari):** "kaynak gösterilmek suretiyle izinsiz yayımlanabilir", ama "ticari amaçlarla kullanımı … yazılı iznine tabidir". MIT kütüphane ve HF veri seti ticari kullanıma açık olacağı için **yazılı izin alınmadan yayımlanmamalı.** |
| **PTT şubeleri** | robots.txt her şeye izin veriyor (`Allow: /`). Toplu şube listesi (CSV/Excel) **bulunamadı [DOĞRULANMADI]**. Posta kodu verisi zaten türev depolardan (epigra, muratgozel) kullanılıyor. |
| **Noterler** | `tnb.org.tr` → `portal.tnb.org.tr` yönlendirmesi. Noter arama var ama toplu liste ve lisans **[DOĞRULANMADI]**. Noterlik adı kişi değil, fakat noter bir kişi. Düşük öncelik. |
| **YÖK / üniversiteler** | `yok.gov.tr/universiteler/universitelerimiz` 404 döndü. Yaklaşık 200 üniversite var, hacim düşük. **[DOĞRULANMADI]** |
| **Adliyeler** | İncelenmedi, toplu açık liste bilinmiyor **[DOĞRULANMADI]**. |
| **e-Devlet** | NVİ adres sorgulama giriş ve doğrulama gerektiriyor. Toplu kullanım yasak ve kişisel veri riski var. Kullanılmaz. |
| **TÜİK** | Adres düzeyinde veri yayımlamıyor, yalnızca istatistik. Uygun değil. |
| **Ulusal açık veri portalı** | CBDDO'nun 2019'da duyurduğu `veri.gov.tr` için yayında olduğuna dair kanıt bulunamadı **[DOĞRULANMADI]**. |

### 3.4 İBB, Ankara ve İzmir açık veri portalları

**İBB** ([data.ibb.gov.tr](https://data.ibb.gov.tr))

- CKAN. robots.txt `/api/` yolunu kapatıyor ve `Crawl-Delay: 10` istiyor. `/dataset/…/download/…` dosya bağlantıları açık.
- **Lisans:** İBB Açık Veri Lisansı. Ticari dahil kopyalama, dağıtma ve uyarlama serbest. Atıf zorunlu. Lisans, CC BY 4.0 ve ODC-By ile uyumlu olduğunu açıkça söylüyor. Varsayılan atıf metni: "Atıf 4.0 Uluslararası (CC BY 4.0) kapsamında lisanslanan kamu sektörü bilgilerini içerir". **Kişisel veri kapsam dışı.**
- "adres" araması 9 veri seti, "konum" araması 19 veri seti döndürdü.

| Veri seti | Dosya | Satır | Adres alanı |
|---|---|---|---|
| [istanbul-saglik-kurum-ve-kuruluslari-verisi](https://data.ibb.gov.tr/dataset/istanbul-saglik-kurum-ve-kuruluslari-verisi) | `saglik-tesisleri.xlsx` | 20.469 | ✅ serbest + ilçe/mahalle |
| [muhtarlik-adres-bilgileri](https://data.ibb.gov.tr/dataset/muhtarlik-adres-bilgileri) | GeoJSON | 963 | ✅ serbest + ilçe/mahalle |
| [istanbul-ili-semt-pazarlari](https://data.ibb.gov.tr/dataset/istanbul-ili-semt-pazarlari) | xlsx (2025) | 385 | ✅ mahalle/cadde/sokak sütunları |
| [mezarlik-adres-bilgileri](https://data.ibb.gov.tr/dataset/mezarlik-adres-bilgileri) | `ilce-bazli-mezarliklar.xlsx` | 580 | ilçe/mahalle (bitişik yazım hataları) |
| [bina-bilgileri-igdas](https://data.ibb.gov.tr/dataset/bina-bilgileri-igdas) | xlsx | — | İncelenmedi (ilçe düzeyi istatistik olabilir) **[DOĞRULANMADI]** |
| [akaryakit-istasyonlari](https://data.ibb.gov.tr/dataset/akaryakit-istasyonlari) | `fuel_station.csv` | 721 | ❌ yalnızca ilçe/mahalle + UAVT kodları, adlar anonim |
| [ibb-lokasyon-verileri](https://data.ibb.gov.tr/dataset/ibb-lokasyon-verileri) | csv/xlsx | 907 / 4.447 | ❌ yalnızca ad + koordinat |
| itfaiye istasyonları, kent lokantaları, sosyal tesisler, Halk Ekmek büfeleri, yurtlar | xlsx | — | İncelenmedi. Çoğu ad + koordinat **[DOĞRULANMADI]** |

**Ankara**

- `acikveri.ankara.bel.tr` 2026-10-08'de **NXDOMAIN** döndü (DNS kaydı yok).
- Belediyenin açık veri ürünü "Şeffaf Ankara" ([seffaf.ankara.bel.tr](https://seffaf.ankara.bel.tr)). Bu bir JavaScript harita uygulaması (TeoMap). Haberlere göre "1000'den fazla açık veri dosyası" var.
- Toplu indirme, CKAN API ve yazılı lisans **bulunamadı.**
- **Değerlendirme: şimdilik kullanılmaz.** Lisans açıklanırsa yeniden bakılmalı.

**İzmir** ([acikveri.bizizmir.com](https://acikveri.bizizmir.com))

- CKAN. robots.txt İBB ile aynı (`/api/` kapalı, Crawl-Delay 10).
- **Lisans:** "aksi belirtilmedikçe CC BY 4.0". Atıf zorunlu.

| Veri seti | Kaynak | Satır | Adres alanı |
|---|---|---|---|
| [nobetci-eczaneler-ve-eczane-listesi](https://acikveri.bizizmir.com/dataset/nobetci-eczaneler-ve-eczane-listesi) | `openfiles.izmir.bel.tr/111324/docs/eczane-listesi.csv` | 2.036 | ✅ gürültülü serbest metin |
| [saglik-kurumlari](https://acikveri.bizizmir.com/dataset/saglik-kurumlari) | `openapi.izmir.bel.tr/api/ibb/cbs/*` | ASM 353, hastane 84, … | ✅ yapılandırılmış (YOL/KAPINO/MAHALLE/ILCE) |
| [muhtarliklar](https://acikveri.bizizmir.com/dataset/muhtarliklar) | `izbb-muhtarliklar.csv` | yaklaşık 1.290 | ✅ yapılandırılmış (**ACIKLAMA'daki muhtar adı atılmalı**) |
| [izmir-buyuksehir-belediyesi-kutuphaneleri-bilgileri](https://acikveri.bizizmir.com/dataset/izmir-buyuksehir-belediyesi-kutuphaneleri-bilgileri) | `kutuphanaler_adres_bilgileri.csv` | yaklaşık 30 | ✅ serbest metin (`Alsancak Mah. Atatürk Cad. No: 454 Konak/İzmir`) |
| [izelman-anaokullari-konum-verisi](https://acikveri.bizizmir.com/dataset/izelman-anaokullari-konum-verisi) | csv | 12 | ✅ mahalle + adres ayrı |
| [izsu-sube-ve-vezne-bilgileri](https://acikveri.bizizmir.com/dataset/izsu-sube-ve-vezne-bilgileri) | csv | — | ⚠️ dosya `Unexpected error` döndü. API'den denenmeli. |
| [degisen-yol-sokak-isimleri](https://acikveri.bizizmir.com/dataset/degisen-yol-sokak-isimleri) | csv/xlsx | — | Eski↔yeni sokak adı eşlemesi. **Alias sözlüğü için değerli**, incelenmedi. |

### 3.5 OpenStreetMap

**Hacim** (Geofabrik taginfo Türkiye, veri tarihi 2026-10-06):

| Etiket | Nesne |
|---|---|
| `addr:street` | 261.256 |
| `addr:housenumber` | 244.484 |
| `addr:city` | 302.609 |
| `addr:district` | 276.880 |
| `addr:neighbourhood` | 210.044 |
| `addr:postcode` | 181.082 |
| `addr:province` | 66.247 |
| `addr:place` | 2.702 |
| `addr:full` | **297** |
| `addr:suburb` | 207 |

- Türkiye'de mahalle için `addr:neighbourhood` kullanılıyor, `addr:suburb` neredeyse hiç kullanılmıyor.
- **Adı da olan POI sayısı** (`name` + `addr:street` + `addr:housenumber`) Overpass sorgusu zaman aşımına uğradığı için ölçülemedi **[DOĞRULANMADI]**. Housenumber taşıyan nesnelerin çoğu adsız bina.
- `turkey-latest.osm.pbf` boyutu **619 MB** (Geofabrik, 2026-10-06).

**Çıkarma, toplu (osmium):**

```bash
curl -O https://download.geofabrik.de/europe/turkey-latest.osm.pbf
# Birden çok ifade VEYA ile birleşir; VE için zincirle:
osmium tags-filter turkey-latest.osm.pbf nwr/addr:housenumber -o tr-hn.osm.pbf
osmium tags-filter tr-hn.osm.pbf nwr/addr:street -o tr-addr.osm.pbf
osmium tags-filter tr-addr.osm.pbf nwr/name -o tr-addr-poi.osm.pbf     # yalnızca adlı POI
osmium export tr-addr-poi.osm.pbf -f geojsonseq -o tr-addr-poi.geojsonseq
```

**Çıkarma, küçük alan (Overpass):**

```
[out:json][timeout:120];
area["name"="Kadıköy"]["admin_level"="6"]->.a;      // TR: il=4, ilçe=6, mahalle=8
nwr(area.a)["addr:street"]["addr:housenumber"]["name"];
out tags center;
```

Tüm Türkiye için Overpass kullanılmamalı. Bu çalışmada da zaman aşımı alındı. Toplu iş için Geofabrik ve osmium kullanılmalı.

**Kalite notları:**

- `addr:street` değerlerinde `Sk.`, `Cd.`, `Blv.`, `Bulv.`, `Sok.` gibi kısaltmalı biçimler var. Yaklaşık 1.000 farklı `… Sk.` değeri bulundu.
- Diğer gözlemler:
  - kırık İ: `Cumhuri\u0307yet`
  - ASCII: `Menderes Bulvari`
  - bitişik yazım: `Kümeevleri`
  - eski/yeni numara bir arada: `851. Sk (160. Sk.)`
  - alana tam adres yazılması: `Kazım Dirik Mah 364 Sk No 6 İzmir`
- Bu kayıtlar gold üretmeden önce temizlenmeli ya da elenmeli.
- Bazı yüksek frekanslı sokak değerlerinin (`Kayacık Caddesi` 635, `Akkuzulu Kümeevler Sokağı` 448) toplu içe aktarmadan gelmiş olabileceği **[DOĞRULANMADI]**. Örneklemde sokak başına üst sınır konmalı.

**ODbL sonuçları** ([OSMF Geocoding Guideline](https://osmfoundation.org/wiki/Licence/Community_Guidelines/Geocoding_-_Guideline)):

- OSM'den çıkarılan **benchmark satırları "derived database"dir.** ODbL ile yayımlanmalı ("© OpenStreetMap contributors" atfı ve share-alike).
- Etiketlerden şablonla **üretilen serbest metin de türevdir.**
- Benchmark **skorları ve grafikleri** "produced work" sayılır. Yalnızca atıf yeter.
- Sonuç: OSM seti HF'de `osm` konfigürasyonu olarak ODbL altında, **ayrı** tutulur. MIT kod ve CC0/CC BY veri ile karıştırılmaz (ADR-005, `data/LICENSE-DATA.md`).
- Çekirdek kütüphaneye OSM'den hiçbir şey girmez. Sözlük taslağında OSM yalnızca "bu varyant gerçekte var" kanıtı olarak anıldı.

---

## 4. Diğer gözlenen küçük kaynaklar (opsiyonel, ulusal çeşitlilik için)

Belediye CKAN portallarında "adres" araması (2026-10-08):

| Portal | Bulunan veri setleri |
|---|---|
| Kadıköy ([acikveri.kadikoy.bel.tr](https://acikveri.kadikoy.bel.tr)) | eğitim birimleri, hizmet birimleri, kültür-sanat birimleri, kütüphaneler, sosyal hizmet birimleri, sağlık birimleri |
| Gaziantep ([acikveri.gaziantep.bel.tr](https://acikveri.gaziantep.bel.tr)) | GASMEK kurs merkezleri, spor tesislerinin adres ve kapasite bilgileri, irtibat ofisleri |
| Konya ([acikveri.konya.bel.tr](https://acikveri.konya.bel.tr)) | misafirhaneler. `saglik-tesisleri.csv` yalnızca ad, tür ve X/Y içeriyor, adres yok. |
| Tuzla ([veri.tuzla.bel.tr](https://veri.tuzla.bel.tr)) | mahalle muhtarlıkları, Kur'an kursları, ACEM kurs merkezleri |
| Manisa ([acikveri.manisa.bel.tr](https://acikveri.manisa.bel.tr)) | çocuk-gençlik merkezleri ve kreşler |

- Her biri onlarca ya da yüzlerce kayıt. Lisansları portal bazında **[DOĞRULANMADI]**.
- Antalya, Balıkesir, Kocaeli, Sakarya, Kayseri, Çanakkale, Beyoğlu, Küçükçekmece ve Eyüpsultan portallarında arama sayfası sonuç döndürmedi. Bunlar JS tabanlı ya da farklı yazılım olabilir.

---

## 5. Yarı otomatik etiketleme (yapılandırılmış alan → gold)

Hedef format PLAN.md 3a'daki şema: `text`, `spans[]`, `gold`, `source`, `noise[]`. Etiketler: `il, ilce, mahalle, semt, csbm_tur, csbm_ad, site, blok, dis_kapi, kat, daire, posta_kodu, tarif, diger`.

### 5.1 Genel boru hattı

1. **Al ve temizle.**
   - Yalnızca adres ve idari sütunları tut. Ad, telefon ve kişi adı içeren sütunları **at.**
   - Metindeki telefonları `[TEL]` ile maskele ve `noise[]` listesine `phone_masked` ekle.
   - İBB'de kişi adlı kategorileri ele: Doktor/Muayenehane, Psikologlar, Diyetisyen, Diş Hekimi.
2. **Gold kimlikleri sütunlardan al.**
   - il: kaynaktan sabit (İstanbul/İzmir).
   - ilçe: `İlçe Adı` (İBB) ya da `ILCE` (İzmir CBS). İzmir eczane için `ILCE_ID` ya da koordinat.
   - mahalle: `Mahalle Adı` / `MAHALLE`.
   - Hepsini AdresTR gazetteer'ına (`turkey-neighbourhoods` vb.) bağlayıp **kanonik ID**'ye çevir.
   - Eşleşmeyenleri (yazım farkı, birleşmiş mahalle) elle inceleme kuyruğuna at.
3. **Tutarlılık kontrolü.**
   - İBB `ADRES` alanının sonundaki `MAHALLE/İLÇE` ile sütunlar aynı mı? Değilse işaretle.
   - Koordinat, gold mahalle merkezine X km'den uzaksa işaretle. İl bazında eşik (ör. 5 km) **[DOĞRULANMADI]**.
4. **Span hizalama.**
   - Gold değerleri (mahalle, ilçe, yol adı, kapı no) Türkçe katlama ve kısaltma sözlüğü normalizasyonundan sonra orijinal metinde ara.
   - Bulunan karakter aralıklarını `spans[]` olarak yaz.
   - Hizalanamayan tokenları sırayla işle:
     - `tarif` kalıpları (`yanı`, `karşısı`, `üstü`…)
     - `site`/`blok` kalıpları
     - kalanlar `diger`
5. **Ön-etiket + insan düzeltmesi.**
   - Kurallarla doldurulamayan alanları (İzmir eczane metnindeki csbm, kat, daire) AdresTR prototipi ve libpostal ile ön-etiketle. Ardından Label Studio'da düzelt.
   - %10'unu iki kişi etiketlesin ve Cohen κ raporlansın (PLAN 3c).
   - **Gold il/ilçe/mahalle insan tarafından değiştirilmez.** Yalnızca kaynak hatası işaretlenir.
6. **Gürültü etiketi.**
   - Her örneğe otomatik `noise[]` ekle: `ascii_only`, `glued_tokens`, `numbered_street`, `slash_no`, `landmark`, `phone_masked`, `semt_suffix`, `mixed_case`, `missing_mahalle`.
   - yazim-hatalari.md'deki regex'ler bu iş için kullanılabilir. Sonuçlar gürültü türüne göre raporlanır.

### 5.2 Kaynak bazında kurallar

| Kaynak | Otomatik gold | Regex/kural | İnsan payı (tahmini) |
|---|---|---|---|
| İBB sağlık / muhtarlık | il, ilçe, mahalle (sütun) | `^(?<ad>.+?)\s(?<tur>Cad\.\|Cd\.?\|Sk\.\|Sok\.\|Bulvarı\|Blv\.\|Yolu\|Yokuşu\|Küme Evler)\s*No:\s*(?<no>\S+(?:\s?[/-]\s?\S+)?)\s+(?<mah>.+)/(?<ilce>.+)$` → csbm_ad, csbm_tur, dis_kapi | Düşük (≈%5–10 eşleşmeyen) **[DOĞRULANMADI]** |
| İBB semt pazarları | ilçe, mahalle, cadde, sokak (sütunlar) | Metin yok. Şablonla üretilir (`{mahalle} {cadde} {sokak} {ilçe}/İstanbul`), `noise` = `template`. | Düşük |
| İzmir eczane | il; ilçe (`ILCE_ID` kod tablosu ya da koordinat) | csbm/no/blok için ön-etiket | **Yüksek** (gürültülü metin, ≈%30–50 düzeltme) **[DOĞRULANMADI]** |
| İzmir CBS (ASM/hastane/muhtarlık) | ilçe, mahalle, yol adı, kapı no | Yol türü yok → türsüz csbm. Metin şablonla. | Düşük |
| OSM | `addr:province`/`addr:city` → il, `addr:district` → ilçe, `addr:neighbourhood` → mahalle, `addr:street` → csbm (son tür sözcüğü sözlükle ayrılır), `addr:housenumber` → dis_kapi, `addr:postcode` → posta_kodu, `addr:unit`/`addr:floor` → daire/kat | Şablon + gürültü katmanları (BenchGen ile aynı). `addr:street` içinde tam adres olanlar elenir. | Düşük, ama kalite denetimi için %5 örneklem |
| TBB (izin alınırsa) | il, ilçe (sütun) | Ön-etiket + insan | Orta |

### 5.3 Raporlama ayrımı

- **`real` (CC BY 4.0):** İBB + İzmir serbest metinleri. Metin insan yazımı.
- **`real-structured-templated` (CC BY 4.0):** İzmir CBS + İBB pazarları. Bileşenler gerçek, metin şablon.
- **`osm` (ODbL):** OSM. Bileşenler gerçek, metin şablon.
- Sentetik setle **karıştırılmaz.** Her alt küme ayrı skorlanır (PLAN risk tablosu: "sentetik veriyle karıştırmadan ayrı raporla").
- HF veri kartına atıf metinleri eklenir:
  - İBB ve İzmir: "Atıf 4.0 Uluslararası (CC BY 4.0) kapsamında lisanslanan kamu sektörü bilgilerini içerir", kaynak veri seti URL'leri ve erişim tarihi.
  - OSM: "© OpenStreetMap contributors, ODbL".

---

## 6. Açık sorular ve riskler

- **Yeniden üretilebilirlik:** Açık veri dosyaları sürümsüz değişiyor (ör. İBB `ibb.lokasyonlar.03.2025.xlsx`). İndirilen ham dosyaların SHA-256 değeri ve indirme tarihi `data/raw/_derived` manifestine yazılmalı. Benchmark yalnızca türetilmiş JSONL ile yayımlanmalı.
- **Kişisel veri:** İşyeri adresi tek başına kişisel veri değildir, ama kişi adıyla birleşince olabilir. Bu yüzden:
  - ad, telefon ve muhtar sütunları hiçbir zaman alınmaz;
  - kişi adlı sağlık kategorileri dışlanır;
  - metindeki telefonlar maskelenir.
- **Bölgesel yanlılık:** Yalnızca İstanbul ve İzmir. TBB izni ya da küçük il portalları ile dengelenmeli. OSM ulusal kapsam sağlar ama ayrı rapor edilir.
- **TBB:** Kullanım koşullarındaki ticari kullanım sınırı nedeniyle yazılı izin talebi gönderilmeli. Bu işlem kullanıcıya bırakıldı.
- **MEB:** Bilgi edinme başvurusu yapılıp yapılmayacağı proje sahibinin kararı.
- **Doğrulanmamış:**
  - `ILCE_ID` kodlarının anlamı
  - İzmir mahalle sınır verisinin varlığı
  - adlı OSM POI sayısı
  - küçük portalların lisansları

---

## 7. Kaynaklar (erişim 2026-10-08)

- İBB Açık Veri Portalı: <https://data.ibb.gov.tr> · Lisans: <https://data.ibb.gov.tr/license>
- İzmir BB Açık Veri Portalı: <https://acikveri.bizizmir.com> · Lisans: <https://acikveri.bizizmir.com/tr/license> · Açık API: <https://openapi.izmir.bel.tr>
- Şeffaf Ankara: <https://seffaf.ankara.bel.tr> · haber: [Cumhuriyet](https://www.cumhuriyet.com.tr/turkiye/ankara-buyuksehir-belediyesinin-seffaf-ankara-projesi-tanitildi-1962916)
- TBB şubeler: <https://www.tbb.org.tr/banka-ve-sektor-bilgileri/banka-bilgileri/subeler> · Kullanım koşulları: <https://www.tbb.org.tr/kullanim-kosullari>
- MEB il MEM / okullar: <https://www.meb.gov.tr/baglantilar/mem/index_ilmem.php?ILKODU=20> · robots: <https://www.meb.gov.tr/robots.txt>
- Sağlık Bakanlığı Açık Veri: <https://acikveri.saglik.gov.tr>
- Türkiye açık veri portalları listesi: <https://github.com/ozancanozdemir/Turkiye-deki-Acik-Veri-Portallari-Open-Data-Portals-in-Turkey->
- Geofabrik Türkiye: <https://download.geofabrik.de/europe/turkey.html> · taginfo: <https://taginfo.geofabrik.de/europe:turkey/>
- OSMF Geocoding Guideline: <https://osmfoundation.org/wiki/Licence/Community_Guidelines/Geocoding_-_Guideline>
- Adres ve Numaralamaya İlişkin Yönetmelik (RG 26245): <https://www.alomaliye.com/2006/07/31/adres-ve-numaralamaya-iliskin-yonetmelik/>
- PTT Adres Standardı Uygulama Talimatı: <https://www.lexpera.com.tr/mevzuat/tebligler/adres-standarti-uygulama-talimati-1>
