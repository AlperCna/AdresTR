# Türkçe adreslerde gerçek yazım hatası kalıpları

> Tarih: 2026-10-08 · Kısa katalog. Sözlük taslağı: [`data/curated/abbreviations.draft.csv`](../../data/curated/abbreviations.draft.csv).
> Örnekler kurum/işyeri adresleridir, kişisel adres yoktur. Telefon numaraları `[TEL]` ile maskelendi.
> **[DOĞRULANMADI]** işaretli maddeler birincil kaynaktan teyit edilmedi.

## Kaynak kısaltmaları

| Kod | Kaynak | Lisans | Ne için kullanıldı |
|---|---|---|---|
| **İZM** | İzmir BB Açık Veri, [nöbetçi eczaneler ve eczane listesi](https://acikveri.bizizmir.com/dataset/nobetci-eczaneler-ve-eczane-listesi) (`eczane-listesi.csv`, 2.036 kayıt) | CC BY 4.0 ([lisans](https://acikveri.bizizmir.com/tr/license)) | Serbest metin `ADRES` alanı; en gürültülü kaynak |
| **İBB** | İBB Açık Veri, [İstanbul sağlık kurum ve kuruluşları](https://data.ibb.gov.tr/dataset/istanbul-saglik-kurum-ve-kuruluslari-verisi) (`saglik-tesisleri.xlsx`, 20.469 kayıt) | İBB Açık Veri Lisansı ([lisans](https://data.ibb.gov.tr/license)) | Yarı standart `ADRES` alanı |
| **İBB-P** | İBB Açık Veri, [semt pazarları](https://data.ibb.gov.tr/dataset/istanbul-ili-semt-pazarlari) (385 kayıt) | İBB Açık Veri Lisansı | Mahalle/Cadde/Sokak ayrı sütunlarda |
| **TBB** | [TBB şube listesi](https://www.tbb.org.tr/banka-ve-sektor-bilgileri/banka-bilgileri/subeler) (bir il sorgusu) | Ticari olmayan kullanım serbest, ticari kullanım yazılı izne bağlı ([koşullar](https://www.tbb.org.tr/kullanim-kosullari)) | Yalnızca örnek gözlem |
| **OSM** | [Geofabrik taginfo Türkiye](https://taginfo.geofabrik.de/europe:turkey/keys/addr:street), `addr:street` değerleri (veri tarihi 2026-10-06) | ODbL | Yalnızca kalıbın varlığına kanıt. Veri kopyalanmadı. |
| **HB** | TEKNOFEST 2025 Hepsiburada hackathon depolarındaki normalizasyon regex/sözlükleri: [finch-stack](https://github.com/finch-stack/Hepsiburada-Hackathon-Teknofest), [Asli-nur-t](https://github.com/Asli-nur-t/T3-TEKNOFEST-2025-Hepsiburada-Address-Matching-Resolution), [whofeeddrogon](https://github.com/whofeeddrogon/TEKNOFEST-2025-Hepsiburada-AI-Powered-Address-Parsing-Hackathon) | Depolarda lisans yok. Yalnızca varyant listeleri (olgular) alındı, kod alınmadı. | Ham veri yok. Kalıplar, katılımcıların gerçek ~850 bin adreste gördüklerini gösteriyor. **Dolaylı kanıt.** |
| **MT** | [mtarikozcan/turkish-address-system](https://github.com/mtarikozcan/turkish-address-system), `config/*.json` | MIT | `spelling_corrections.json` kısmen sentetik görünüyor (`kadıköyy`, `bebekk` gibi). Düşük güven. |
| **YÖN** | Adres ve Numaralamaya İlişkin Yönetmelik (RG 31.07.2006/26245), [metin](https://www.alomaliye.com/2006/07/31/adres-ve-numaralamaya-iliskin-yonetmelik/) | Mevzuat | Ne "doğru" sayılır |

## Katalog

| # | Kalıp | Gerçek örnekler | Kaynak | Sıklık (ölçülen) | Parser için not |
|---|---|---|---|---|---|
| 1 | **Yapışık tokenlar**: tür + nokta + ad/no bitişik | `MAH.GÜNSAZAK BLV.NO:61/C` · `KEMALPASA CAD.NO:60/B` · `MH.SEYFİ DEMİRSOY CD.NO:19/A` · `SOKAK. NO14/A` · `MENDERES CAD.NO.74/C` | İZM | İZM: 244/2.036 kayıtta `MAH.X` / `CAD.X` türü yapışma, 134 kayıtta `X.NO:` | Noktadan sonra boşluk yokken de böl. `NO14` → `NO 14`. |
| 2 | **Bitişik yazılmış tür/ad** | `İÇERENKÖYMAHALLESİ` · `BARBAROSMAHALLESİ` · `BASINSITESI` / `BASINSİTESİ MAH.` · `ESKIIZMIR CAD.` · `Akkuzulu Kümeevler Sokağı` · `Kuruçay Kümeevleri Sokağı` | İBB [mezarlıklar](https://data.ibb.gov.tr/dataset/mezarlik-adres-bilgileri), İZM, OSM | — | Sonek ayırma (`…mahallesi$`) ve gazetteer'a karşı bitişik/ayrık iki yönlü arama gerekir. |
| 3 | **Kırık İ / ı** | `INÖNÜ CAD.` (İ yerine I) · `KÖYIÇI MAH.` · `ÇIGLI-IZMIR` · `Canakkale Sehıtlerı Cad.` (ı↔i, ş→s karışık) · `Cumhuri\u0307yet`, `Şehi\u0307t Er …` (U+0307 birleşik nokta: hatalı `İ`→`i\u0307` küçültmesi) · `mahallesı`, `daıre` | İZM, İBB, OSM, MT | OSM'de `Cumhuri\u0307yet Caddesi` tek başına 458 nesne | Katlamadan önce U+0307'yi sil. I/İ/ı/i tek anahtara katlanmalı. |
| 4 | **ASCII yazım** (Türkçe harf yok) | `SAIR ESREF BULV.` · `KEMALPASA` · `Menderes Bulvari` · `Yokus cesmesi sokak` · `ALTINDAG` | İZM, OSM | OSM'de `Menderes Bulvari` 827 nesne | Mevcut ASCII katlama karşılıyor. Sözlüğe yalnızca gözlenen ASCII varyantlar eklendi. |
| 5 | **Numaralı sokaklar** (İzmir'de yaygın, eğik çizgili alt numara dahil) | `7448/4 SK` · `65/11 Sok No:4/A` · `8809/1 SOK. NO:149/B` · `2437. Sk. No:101` · `851. Sk (160. Sk.)` (yeni ad, parantezde eski numara) | İZM, İBB, OSM | İZM: 824/2.036 (≈%40) kayıtta ≥3 haneli sokak numarası; İBB: 912 kayıt numaralı sokakla başlıyor | `/` hem sokak adının içinde hem kapı no'da geçer. Sokak türünden **önceki** sayı sokak adıdır. |
| 6 | **Eğik çizgi ve tire notasyonu** | Kapı/iç kapı: `No:51/3B`, `No:10 /1`, `No : 175 / A`, `NO:1-3A`, `No:118 -120 B`, `NO:126/1A-B` · İlçe/il: `SİLAHTARAĞA/EYÜPSULTAN`, `BUCA -IZMIR`, `KARABAGLAR-IZMIR` · Çoklu mahalle: `Zeynepkamil/Barbaros/Selimiye/Salacak` | İBB, İZM, TBB | İBB: 3.847 kayıtta `No:N/…`, 4.745 kayıtta harfli no (`No:25 F`) | Yönetmelik: harfli numara (md.26/4: 1A, 1B, 1AA), avlu aralığı tireyle (md.27), birleşme ve bölünmede `/` ve `-` (md.33–36). `N/M` çoğu zaman *dış kapı/iç kapı*dır ama kesin değildir. |
| 7 | **Semt / resmi olmayan yer adı** (genelde sonda) | `… NO:60/B ALTINDAG` · `220 SOK. NO:14 BASINSITESI` · `GAZI CAD. NO:126/1A-B GÜLTEPE` · `GÖKTÜRK MERKEZ/EYÜPSULTAN` · `1580/1 Sok. 1/C Mersinli Son Durağı` | İZM, İBB | İZM: 210 kayıt no'dan sonra serbest bir yer adıyla bitiyor | PTT talimatı (md.5.3.1) beşinci satıra semt/bucak/belde yazılmasına izin verir. Semt etiketi ayrı tutulmalı. |
| 8 | **Tür yığılması / ad içinde tür sözcüğü** | `Halife Çıkmazı Sokağı` · `Baraj Yolu Cad.` · `Balıklı Kazlıçeşme Yolu Sk.` · `İETT Blokları Yolu` · `YENİ MAHALLE MAH.` (Yenimahalle Mahallesi) · `Meydan Sokağı` · `Siteler Mevki Cumhuriyet Bulv.` | OSM, İBB, İZM | — | Son tür sözcüğü belirleyicidir. Öncekiler adın parçası sayılmalı. |
| 9 | **Tekrarlanan token** | `ESKIIZMIR CAD.NO.788/C ESKIZIMIR KARABAGLAR-IZMIR` (semt yazım hatasıyla tekrar) · `ATATÜRK MAH. ISTASYON CAD. NO:86/1 A BLOK NO.3` (iki `NO`) · hackathon kodunda `kat kat`, `daire daire` temizleme kuralları | İZM, HB (whofeeddrogon) | İZM: 4 kayıtta iki `NO` (ikisi `no lu` kullanımı) | İkinci `NO` blok içi numara olabilir. Körlemesine silme. |
| 10 | **Adres içinde telefon** | `… GİRNE BULVARI NO:51/3B([TEL])` · `… NO:117/5 ([TEL])` · `… AİLE SAĞLIĞI MERKEZİ  [TEL]` | İZM | İZM: 6 kayıt (cep telefonu dahil) | `0?5\d{2}…` ve `0?2\d{2}…` kalıplarını `diger` olarak ayır. Benchmark'a koymadan önce **maskele** (kişisel veri olabilir). |
| 11 | **Yer tarifi** (karşısı/yanı/arkası/üstü/altı) | `MEZARLIK YANI` · `(ZİRAAT BANKASI KARŞISI)` · `Buca Devlet Hastanesi Krş.` · `YEDİGÖLLER İŞ MERKEZİ TANSAŞ ÜSTÜ` · `Ufuk Apt. Altı` · `ATA CADDESİ GİRİŞİ` · `… Parkı Karşısı - Arda Pide Yanı` | İZM, TBB | İZM: 189/2.036 (≈%9) | `tarif` etiketi. Sözcük sonda ama nesnesi önde (`X YANI`). `altı` sayıyla çakışır (6). |
| 12 | **`Nolu` belirsizliği** | `5 Nolu ASM ve Kurtuluş Parkı karşısı` · `5 NO LU AÇS YANI` · `/6/ NOLU AİLE SAĞLIĞI MERKEZİ` · `1 Nolu Sümbül Sok.` | İZM, OSM | — | `nolu` kapı numarası **değildir**. Kurum ya da sokak sırasını gösterir. |
| 13 | **Ad içinde unvan/kısaltma** | `Dr. Sadık Ahmet Cad.` · `Prof. Dr. Hıfzı Özcan Cd` · `Şht. Ömer Halisdemir Blv.` · `M.Akif Cd.` · `ORG. HULUSİ AKAR BULVARI` · `K.Bakkalköy mh` (K = Küçük) | İBB, İBB-P, OSM, İZM | — | `dr` (daire/doktor), `k` (kat/kuzey/küçük), `m.` (mahalle/Mehmet), `org.` (organize/orgeneral) çakışır. Ad içinde açılmamalı. |
| 14 | **Boşluk/noktalama tutarsızlığı** | `No: 76/C` · `No:10 /1` · `No : 175 / A` · `Bağdat Cd No:151` (noktasız) · çift boşluk, sondaki boşluk | İBB, TBB, İZM | İBB: 5.712/20.469 (≈%28) kayıtta çift boşluk | Tokenizer `No` ile `:` ve `/` çevresindeki boşlukları normalize etmeli. |
| 15 | **Eksik bileşen / ters sıra** | Yalnızca `SOĞANLI/BAHÇELİEVLER` (yol yok) · `8791 SOKAK NO:9 BALATÇIK` (mahalle yok) · `YAVUZ CAD. OSMANGAZI MAH. 289` (cadde mahalleden önce, `No` yok) · `Saray Cd. No:10 D:No:10` | İBB, İZM | İBB: 1.396 kayıtta yol yok | Sıra varsayma. `No` olmadan sondaki sayı kapı numarası olabilir. |
| 16 | **Büyük/küçük harf karışık** | `16 eylül mah. 3001 sk. no:3/G` · `Çamtepe mah. Güngören cad.` · `OSMANGAZI MAH. YAVUZ CAD. NO:299/A bayraklı` | İZM | İZM: 140 kayıtta küçük harf | Türkçe kurallarla küçült (`I`→`ı`, `İ`→`i`). |
| 17 | **Klavye/telaffuz yazım hataları** (tür sözcüklerinde) | `mahlesi`, `mhallesi`, `maallesi`, `mahellesi` · `sokar`, `sokk`, `soklar` · `cadessi`, `caddee` · `bulvr`, `bulwar`, `bulbar` · `nomara`, `nomra` · `apartaman` · `Cadeesi` · `Tranvay` | HB (finch-stack regex), OSM, İZM | — (ham veri yok) | Tür sözcüklerine ayrı, küçük edit-distance bütçesi. Ad alanına uygulanmamalı. |
| 18 | **Alan karışması** (yapılandırılmış alana tam adres yazma) | OSM `addr:street` değerinde `Kazım Dirik Mah 364 Sk No 6 İzmir` · `6250. Sk No:7, 35467 Ürkmez Seferihisar İzmir` | OSM | `addr:full` yalnızca 297 nesne, ama `addr:street` içinde tam adres de var | OSM'den gold üretirken bu kayıtlar elenmeli. |

## Sözlük taslağındaki başlıca çakışmalar

`abbreviations.draft.csv` içinde aynı varyantın birden çok kanonik karşılığı olan satırlar (bağlamla çözülmeli):

- `d` / `d.` → **daire** ya da **doğu**. libpostal `d`'yi `ambiguous_expansions.txt` dosyasında listeler. whofeeddrogon sözlüğünde `d.` iki kez tanımlı.
- `k` / `k.` → **kat**, **kuzey**, ayrıca ad içinde **Küçük** (`K.Bakkalköy`).
- `b` → **batı**, ama `B BLOK` ve `No:17 B` (harfli kapı no) çok daha sık.
- `bl` / `bl.` → **bulvar** (libpostal) ya da **blok** (mtarikozcan).
- `pk` → **posta kutusu** (libpostal, PTT talimatı md.5.3.1 "PK ibaresi"). Posta *kodu* değildir. mtarikozcan bunu **parkı** olarak açıyor.
- `dr` / `dr.` → **daire** (whofeeddrogon) ya da **doktor** (Asli-nur-t). Adresteki gerçek kullanım çoğunlukla unvan.
- `ist.` → **İstanbul** (mtarikozcan) ya da **istasyonu** (whofeeddrogon).
- `so` → **sokak** kısaltması (finch-stack) ya da **Sağlık Ocağı** (İZM: `GEDİZ SO KARŞISI`).
- `da` → **daire** ya da Türkçe `da/de` bağlacı.
- `nolu` → kapı numarası **değil**, "numaralı" anlamında sıra/kurum belirteci.
- `merkez` → ilçe adı (`Merkez`) ya da semt eki (`GÖKTÜRK MERKEZ`).
- `altı` → tarif (`Apt. Altı`) ya da sayı (6).
- Hackathon hatası: finch-stack regex'i `kat`, `kati`, `k.` biçimlerini **daire**'ye eşliyor. Kopyalanmamalı.

## Lisans notu (sözlük)

`data/curated/` CC0 olarak planlandı (`data/LICENSE-DATA.md`). Taslakta yalnızca tek tek kısaltma/varyant **olguları** var. Hiçbir kaynaktan kod ya da yapı kopyalanmadı. Her satırda gözlenen kaynak(lar) yazılı.

- OSM, İBB, İzmir ve TBB kaynakları yalnızca "bu varyant gerçekte kullanılıyor" kanıtı olarak anıldı. Veri alınmadı.
- Hackathon depolarında lisans yok. Oradan alınan varyantlar kısaltma olgusudur. Yine de CC0 yayımından önce gözden geçirilmeli. **[DOĞRULANMADI: hukuki görüş değildir]**
