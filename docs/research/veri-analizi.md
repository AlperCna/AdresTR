# Faz 1 Veri Analizi — Gazetteer kaynakları (il / ilçe / mahalle)

**Tarih:** 2026-10-08
**Kapsam:** NVİ (melihozkara), PTT türevleri (muratgozel, epigra) ve Wikidata kaynaklarını sayısal olarak karşılaştırmak; Faz 1 gazetteer'ı için birincil kaynak, birleştirme (join) stratejisi, normalizasyon kuralları ve elle kurulacak tabloları belirlemek.

İşaretler: **[DOĞRULANMADI]** kaynakla doğrulanmamış çıkarım. Diğer tüm sayılar aşağıdaki scriptlerin çıktısıdır.

---

## 0. Yeniden üretim

Ham veri `data/raw/<kaynak>/` altında. Bu klasör `data/raw/.gitignore` ile git dışında tutuluyor; kök `.gitignore` dosyasında `data/raw` kuralı **yok**, onun yerine bu yerel `.gitignore` eklendi. Scriptler `data/scripts/` altında. Türetilmiş CSV'ler `data/raw/_derived/` altına yazılıyor.

```bash
python -I data/scripts/fetch_wikidata.py data/raw/wikidata   # Wikidata SPARQL -> CSV (UA: AdresTR-data/0.1 (+https://github.com/AlperCna/AdresTR))
python -I data/scripts/a1_ilce.py            # il/ilçe karşılaştırması
python -I data/scripts/a2_mahalle.py         # NVİ <-> PTT mahalle örtüşmesi
python -I data/scripts/a2b_residual.py       # eşleşmeyenlerde bulanık eşleşme, PTT 2022/2024 farkı
python -I data/scripts/a3_semt.py            # PTT semt sütunu
python -I data/scripts/a4_pk_ambig_quirks.py # posta kodu, belirsizlik, çakışma, biçim
python -I data/scripts/a5_wikidata.py        # Wikidata <-> NVİ, koordinat kapsamı, 6360 izi
```

`common.py` iki şey içeriyor: kaynak yükleyiciler ve `fold()`. `fold()` sırasıyla şunları yapar:
1. Türkçe küçük harf (I→ı, İ→i).
2. U+0307 karakterini siler.
3. ASCII katlama (ç→c, ğ→g, ı/i→i, ö→o, ş→s, ü→u, â→a).
4. Tek geçişte sonek soyar: `mah`, `mah.`, `mh`, `mahallesi`, `köyü`, `(köyü)`, `beldesi`.
5. `.`, `-`, `'` ve `/` karakterlerini boşluğa çevirip boşlukları sadeleştirir.

### İndirilenler

| Kaynak | Dosya | Boyut | Not |
|---|---|---|---|
| melihozkara (NVİ, 2026-10-06) | `iller.jsonl`, 81 × `ilceler.jsonl`, 81 × `mahalleler.jsonl`, `degisiklik-raporu/mahalle-*.json`, README, `walker.mjs`, `build-sql.mjs` | ~14 MB | Sokak dosyaları **indirilmedi**: 81 × `sokaklar.jsonl` = **190,3 MB**; `sql/titlecase_data_06-10-2026.zip` 19,1 MB; `sql/uppercase_…zip` 18,9 MB; `sokak-eklenen.json` 2,1 MB, `sokak-silinen.json` 0,7 MB, `sokak-ad-degisen.json` 0,6 MB |
| muratgozel/turkey-neighbourhoods (MIT) | `src/data/*.json` + setup kodu | ~11 MB | npm 4.0.3 (2024-03-31) ile aynı içerik. Ham PTT kaynağı `https://postakodu.ptt.gov.tr/Dosyalar/pk_list.zip`, bugün `ptt.gov.tr/posta-kodu` adresine 302 ile yönleniyor (dosya yok). |
| epigra/tr-geozones (MIT) | `src/Database/Seeders/*.php` (il, ilçe, semt, mahalle) | ~15 MB | README'deki SQL dump linki (`epigra.com/…2020-09-28.zip`) 301 dönüyor. Seeder'lar yeterli olduğu için indirilmedi. |
| Wikidata (CC0) | `wd_il`, `wd_ilce`, `wd_mahalle`, `wd_koy`, `wd_belde`, `wd_eski_koy`, `wd_tuik_nbh`, `wd_tuik_vill` (.csv) | ~7 MB | 2026-10-08 sorgusu |

---

## 1. Kaynakların yapısı

### 1.1 NVİ (melihozkara) — `mahalleler.jsonl` alanları

`koyAdi, koyKayitNo, koyKurumBelediyeTur, mahalleTur, bilesenAdi, adi, kimlikNo, il_id, ilce_id`

- **il:** `kimlikNo` = plaka kodu (81/81).
- **ilçe:** `kimlikNo` (ör. Adalar 1103) ve ayrıca `ilceKayitNo` var.
- **Tip 0 kayıtlar:** `adi = null` (12.296 null değerin 12.294'ü). Ad `koyAdi` alanında.
- **Büyük harf:** Tüm adlar BÜYÜK HARF (73.398/73.398).

**`mahalleTur` kodları.** Anlamlar `bilesenAdi` son eki ile `koyKurumBelediyeTur` alanından çıkarıldı. Kodların resmî tanımı bulunamadı **[DOĞRULANMADI]**, ancak son ekler %100 tutarlı.

| mahalleTur | Sayı | `bilesenAdi` kalıbı | Anlam |
|---|---|---|---|
| 0 | 12.294 | `X KÖYÜ` | Köy. Yalnızca alt birimi (mevki/mezra/yayla) **listelenmeyen** köyler tip 0 olarak görünüyor. |
| 1 | 32.471 | `X MAHALLESİ`; beldelerde `B BELDESİ, X MAHALLESİ` | Mahalle. Alt kırılım: (a) `koyKurumBelediyeTur=3` ve `koyAdi=MERKEZ` → belediye mahallesi, 30.791; (b) `=4` → belde mahallesi, 1.659; (c) `null` → köy içinde "MAHALLESİ" etiketli birim, 21. Bu 21 kaydın 9'unda köyün adı gerçekten "MERKEZ" (Hakkari). |
| 3 | 5.093 | `K KÖYÜ, X MEZRASI` | Mezra |
| 4 | 21.684 | `K KÖYÜ, X MEVKİİ` (3 tanesi `B BELDESİ, X MEVKİİ`) | Mevki / köy mahallesi |
| 5 | 1.250 | `K KÖYÜ, X YAYLA EVLERİ` | Yayla evleri |
| 6 | 606 | `K KÖYÜ, X MEVKİİ` | Yine "MEVKİİ" etiketli. Tip 4'ten farkı belirlenemedi **[DOĞRULANMADI]**. Örnekler arasında OSB, kaplıca ve "MERKEZ" var. |

**`koyKurumBelediyeTur` kodları [DOĞRULANMADI, çıkarım]:**
- 3: il/ilçe belediyesi (koyAdi=MERKEZ)
- 4: belde belediyesi
- null: köy

Bu kodlardan türetilen sayılar:
- **Distinct köy** (`koyKayitNo`, belde hariç): **18.249**. Resmî sayı 18.214, fark %0,2.
- **Belde:** 399.
- **Alt birimi olan ama tip 0 kaydı olmayan köy:** 5.955. Bu yüzden **"köy listesi" tip 0'dan değil, `koyKayitNo` üzerinden kurulmalı.**

**NVİ değişim raporu (2026-04 → 2026-10):**
- 104 mahalle silindi. 96'sı "MÜCAVİR" mevkileri, hepsi tip 4.
- 6 mahalle eklendi.
- 42 ad değişikliği oldu. `eskiAd`/`yeniAd` değerleri `kimlikNo` ile birlikte veriliyor.

**Repo geçmişinde 3 anlık görüntü var:**
- 2025-02-18: yalnız SQL zip
- 2026-04-12: jsonl
- 2026-10-06: jsonl

**Lisans ve köken riski.** Repo **lisanssız**. README'ye göre veri NVİ'den çekilirken **reCAPTCHA, 2Captcha servisiyle çözülmüş**. Bizim "CAPTCHA aşmayacağız" ilkemizle çelişen bir yolla üretilmiş veriyi yeniden dağıtmak ek bir hukuki/itibar riski taşıyor (bkz. §8).

### 1.2 PTT türevleri

- **muratgozel:** Satır yapısı `[plaka, il, ilçe, mahalle, PK]`. Ham PTT xlsx'teki **semt/bucak sütununu atmış**.
- **epigra:** il → ilçe ("county") → **semt/bucak/belde ("district")** → mahalle + PK.
- **İki dosya özünde aynı.** Ortak satırların hiçbirinde posta kodu farklı değil. Yapısal (katlanmış ad + üst birim) olarak yalnız 1 satır farklı: murat'ta İskilip'te fazladan "Çuhalar Mah (Güneyaluç Köyü)". Ham metindeki farkların hepsi murat'ın `titleCase` hatası: parantez içi küçük harfle başlıyor ("(çakırhüyük Beldesi)"), 29.844 satır.
- **Sonuç:** "PTT 2024-03" verisi pratikte **PTT 2022-08 anlık görüntüsü**. PTT toplu dosyası 2022'den sonra güncellenmemiş görünüyor. [Yayın tarihi DOĞRULANMADI]

**PTT "Mahalle" sütunu serbest metin ve yapı içeriyor.** Parse edilen türler (murat):

| PTT türü | Kalıp | Sayı |
|---|---|---|
| mahalle | `X Mah` | 30.718 |
| köy | `X Köyü` | 12.729 |
| köy alt birimi | `X Mah (K Köyü)` veya 3 seviyeli `X Mah (Y Mah) (K Köyü)` (198) | 28.210 |
| belde mahallesi | `X Mah (B Beldesi)` | 1.609 |
| mahalle alt birimi | `X  Mah (Y Mah)` | 24 |
| diğer | `K Köyü Yayla Evleri`, `Kıran Köy` … | 15 |

PTT'deki distinct köy sayısı 18.226, belde sayısı 388.

### 1.3 Wikidata — kimlik özellikleri (önemli bulgu)

| Özellik | Etiket | Kullanım | NVİ karşılığı |
|---|---|---|---|
| P395 | vehicle registration plate code | 81 il | plaka. **2 hata:** Konya P395=53 (doğrusu 42), Uşak P395=62 (doğrusu 64) |
| **P14358** | Turkey province ID | 81 il | plaka (81/81 doğru; ISO 3166-2 P300 ile tutarlı) |
| **P14366** | Turkey district ID | 275 ilçe öğesi | **NVİ ilçe `kimlikNo`** (275/275 eşleşiyor) |
| **P12883** | Turkey neighborhood ID (TÜİK) | 28.438 öğe / 28.327 distinct ID | **NVİ mahalle `kimlikNo`**. 28.156 ID NVİ'de var (%99,4); bunların 28.104'ü tip 1. |
| **P13588** | Turkey village ID (TÜİK) | 18.667 öğe / 18.678 ID | **NVİ `koyKayitNo`**. 18.228 ID NVİ'de var (%97,6). |
| P2123 | YerelNet village ID | 18.441 köy + **16.528 mahalle** öğesi | Eski köy kimliği (bkz. §7) |

**Sonuç:** TÜİK/NVİ aynı kimlik numaralarını kullanıyor. Wikidata, NVİ kimlikleriyle **CC0** bir köprü sunuyor. Wikidata ile NVİ arasında isim eşleştirmesi gerekmiyor.

---

## 2. Satır sayıları (seviye × kaynak)

| Seviye | NVİ 2026-10-06 | PTT murat (2024-03 ≈ 2022) | PTT epigra 2022-08 | Wikidata 2026-10-08 | Resmî (e-içişleri) |
|---|---|---|---|---|---|
| İl | 81 | 81 | 81 | 81 | 81 |
| İlçe | 973 (51'i `MERKEZ`) | 973 (51 `Merkez`) | 973 | 1.052 öğe (P31=Q1147395). 1.016'sı NVİ'ye bağlandı; 966/973 NVİ ilçesi kapsandı. | 973 |
| Belde | 399 | 388 | 388 | 486 öğe (Q815324) | — |
| Semt / PK bölgesi | — | (atılmış) | 2.771 | — | — |
| Posta kodu | — | 2.771 | 2.771 | — | — |
| Mahalle (tip 1) | 32.471 | 30.718 + 1.609 belde | aynı | 27.975 öğe (Q17051044) | 32.312 |
| Köy (distinct) | 18.249 | 18.226 | 18.226 | 19.394 öğe (Q1529096) | 18.214 |
| Köy alt birimi (mezra/mevki/yayla) | 28.633 (tip 3/4/5/6) | 28.210 | 28.209 | — | (bağlı birim 23.571) |
| Toplam "mahalle" satırı | **73.398** | **73.305** | **73.304** | — | — |

- Wikidata'da `P576` (dissolved) değeri ilçe, mahalle, köy ve belde sınıflarının **hiçbirinde yok** (0).
- Kaldırılmış birimler sınıf üyeliğini koruyor, bu yüzden Wikidata'yı "güncel liste" olarak tek başına kullanmak hatalı olur (bkz. §6).

---

## 3. İlçe adları — kaynaklar arası tutarlılık

**NVİ, murat ve epigra: (plaka, katlanmış ad) anahtarıyla 973/973 birebir eşleşiyor, hiç fark yok.**
- Büyük/küçük harf dışında yazım farkı da yok.
- `Eyüpsultan` her üç kaynakta güncel adıyla geçiyor; `Eyüp` hiçbirinde yok.
- `19 MAYIS` üç kaynakta da rakamla yazılmış. Rakam içeren tek ilçe bu.
- `Kahramankazan` üç kaynakta da güncel.

**Merkez ilçeler:**
- NVİ de PTT de 51 merkez ilçeyi il adıyla değil, **`MERKEZ`** adıyla tutuyor.
- 30 büyükşehir ilinde `MERKEZ` ilçesi yok.
- "Merkez" adı 51 farklı ilde tekrar ediyor. Yani ilçe adı tek başına bir kimlik değil.

**Birden fazla ilde geçen ilçe adı: 25.** Merkez hariç örnekler:
- Aksu, Altınyayla, Aydıncık, Ayvacık, Bayat, Bozkurt, Edremit, Ereğli, Gölbaşı, Gönen, Kale, Kemalpaşa, Kemer, Köprübaşı, Ortaköy, Ovacık, Pazar, Pınarbaşı, Saray, Ulubey, Yenice, Yenipazar, Yeşilyurt
- **Yenişehir** üç ilde geçiyor: Bursa, Diyarbakır, Mersin.

**İlçe adı = il adı:** NVİ adlarında 0. Ama kullanıcı metninde "Kars Merkez", "Bolu" gibi il adı merkez ilçe yerine kullanılıyor. Wikidata da merkez ilçeleri "Isparta (ilçe)", "Siirt" gibi etiketlerle tutuyor.

**Wikidata ilçe etiketleri:** NVİ'den farklı 85 etiket var.
- Çoğu "X (ilçe)", "X ilçesi", "X İlçesi" kalıbında, ya da merkez ilçe için il adı.
- Gerçek farklar:
  - `Kazan (rayon)` → KAHRAMANKAZAN
  - `Ilıca` → AZİZİYE
  - `Yahyal İlçesi` (yazım hatası) → YAHYALI
- 50 NVİ ilçesine birden fazla Wikidata öğesi bağlanıyor (kasaba öğesi ile ilçe öğesi ayrı).
- Eşlenemeyen 36 aktif öğenin çoğu eski merkez ilçe: "Merkez (Şanlıurfa)", "Kahramanmaraş", "Balıkesir (ilçe)", "Erzurum (ilçe)", "Merkez (Kocaeli)" vb. Bunlar `P576` taşımıyor.

---

## 4. Mahalle örtüşmesi: PTT (2022/24) ↔ NVİ (2026-10)

**Eşleştirme anahtarı:** (plaka, katlanmış ilçe, birim türü, katlanmış ad, katlanmış üst birim [köy/belde]). Kademeler sırasıyla:

1. **tam:** ad ve üst birim tutuyor.
2. **isim:** üst birim yok sayılıyor.
3. **kompakt:** boşluklar kaldırılıyor.
4. **köy-eki:** sondaki "köy" kaldırılıyor. Örnek: NVİ `ÇİFTLİKKÖY` ↔ PTT `Çiftlik Köyü`.

### 4.1 NVİ → PTT (mahalleTur kırılımı)

| mahalleTur | n | tam | isim | kompakt | köy-eki | **yok** | yok % |
|---|---|---|---|---|---|---|---|
| 0 köy | 12.294 | 11.709 | 11 | 60 | 229 | **285** | 2,3 |
| 1 mahalle | 32.471 | 32.112 | 30 | 10 | 5 | **314** | 1,0 |
| 3 mezra | 5.093 | 4.772 | 160 | 2 | 1 | **158** | 3,1 |
| 4 mevki | 21.684 | 19.760 | 1.041 | 3 | 0 | **880** | 4,1 |
| 5 yayla evleri | 1.250 | 1.162 | 63 | 1 | 0 | **24** | 1,9 |
| 6 mevki(?) | 606 | 578 | 17 | 0 | 0 | **11** | 1,8 |
| **Toplam** | 73.398 | 70.093 | 1.322 | 76 | 235 | **1.672** | **2,3** |

### 4.2 PTT → NVİ (PTT türü kırılımı)

| PTT türü | n | tam | isim | kompakt | köy-eki | **yok** | yok % |
|---|---|---|---|---|---|---|---|
| mahalle | 30.718 | 30.455 | 10 | 9 | 5 | **239** | 0,8 |
| belde mahallesi | 1.609 | 1.593 | 6 | 1 | 0 | **9** | 0,6 |
| köy | 12.729 | 11.744 | 27 | 60 | 225 | **673** | 5,3 |
| köy alt birimi | 28.210 | 26.315 | 1.423 | 3 | 8 | **461** | 1,6 |
| mahalle alt birimi | 24 | 0 | 13 | 0 | 0 | **11** | 45,8 |
| diğer | 15 | 0 | 15 | 0 | 0 | 0 | 0 |
| **Toplam** | 73.305 | 70.107 | 1.494 | 73 | 238 | **1.393** | **1,9** |

**Özet:**
- NVİ kayıtlarının **%97,7**'si (71.726) PTT'de bir karşılık buluyor, dolayısıyla posta kodu alabiliyor. Tip 1 mahallede bu oran **%99,0** (32.157).
- NVİ'de olup PTT'de olmayan: **1.672**. PTT'de olup NVİ'de olmayan: **1.393**.
- "İsim" kademesindeki 1.322 eşleşmenin büyük kısmı köy alt birimlerinde. Bu kayıtlarda üst köy adı farklı (köy adı değişmiş ya da PTT'de 3 seviyeli yapı var). Posta kodu ataması burada **orta güvenle** yapılmalı.

**Eşleşmeyenlerin yapısı:**
- **Bulanık eşleşme:** Aynı ilçedeki eşleşmeyen PTT adlarıyla `difflib` ratio ≥ 0,8 veren NVİ kaydı 265 (213 köy, 32 mahalle).
  - Bunlar gerçek yazım/ad varyantları: `KURUOBA`~`koruoba`, `NUSRETİYE`~`nusratiye`, `CAMBAZ`~`canbaz`, `BEŞDUT`~`bestut`, `AŞAĞIMÜSLİMLER`~`asagi muslumler`, `YİĞİTHARMANI`~`yigitharman`, `HÜYÜK`~`uyuk`.
  - **Elle doğrulama tablosu gerekiyor.**
- **Kalan farkların çoğu gerçek değişiklik [kısmen DOĞRULANMADI]:** 2022 sonrası yeni, bölünmüş, kaldırılmış ya da adı değişmiş mahalleler (ör. Gaziantep Şahinbey, Kayseri Kocasinan örnekleri).
  - Bir kısmı yine yazım farkı. Örnek: PTT `Ulucamii`/`Yeni Camii` ↔ NVİ `ULUCAMİ`/`YENİCAMİ` (Antakya). Bu da "cami/camii" kuralı gerektiriyor (§11.3).
- **İl yoğunlaşması:**
  - NVİ tarafı: Bitlis 141, Muş 116, Şırnak 67, Artvin 67 (mezra/mevki ağırlıklı).
  - PTT tarafı: plaka 13 (Bitlis) 140, 49 (Muş) 111, 23 (Elazığ) 72.
- **Deprem bölgesi:** Hatay'da tip 1 mahallenin 23/601'i, Kahramanmaraş'ta 13/724'ü PTT'de yok.

---

## 5. PTT "semt/bucak" sütunu (epigra `districts`)

**Gerçekte ne içeriyor:** Sütun **posta dağıtım bölgesinin adı**.
- (il, ilçe, semt) ile posta kodu arasında **birebir** ilişki var: 2.771 semt = 2.771 PK. Her semtin 1 kodu, her kodun 1 semti var.
- İlçe başına semt sayısı: minimum 1, medyan 2, maksimum 21. Tek semtli ilçe 325.
- Semt başına mahalle: medyan 8, maksimum 669.

**Semt adlarının sınıflandırması (2.771):**

| Sınıf | Sayı |
|---|---|
| İlçe adıyla aynı (merkez posta bölgesi) | 847 |
| Kendi mahallelerinden birinin adı | 705 |
| "… Merkezköyler" / "… Köyler" (kırsal toplu bölge) | 458 |
| Belde adı | 383 |
| Köy adı | 14 |
| **Bağımsız semt adı (gerçek alias adayı)** | **364** |

**Sorulan örnekler:**

| Aranan | Semt sütununda | Değerlendirme |
|---|---|---|
| **Moda** | **Yok** | Kadıköy semtleri: Bostancı, Caddebostan, Caferağa, Erenköy, … Caferağa semti yalnız Caferağa Mah'ı kapsıyor. "Moda" hiçbir yerde semt değil; yalnız Şemdinli'de bir "Moda Mah" var. |
| **Levent** | Var (Beşiktaş) | Levent semti = Konaklar + Levent Mah. "Levent" ayrıca 6 farklı ilçede mahalle adı. |
| **Bostancı** | Var (Kadıköy; ayrıca Trabzon Ortahisar) | Bostancı semti = Bostancı Mah. |
| **Kızılay** | **Yok** | Ankara Çankaya'da semt adı "Yenişehir" (Kızılay, Kocatepe, Meşrutiyet, … 10 mahalle). "Kızılay Mah" ayrıca var. |
| **Alsancak** | Var (Konak) | Alsancak semti = Alsancak, Kültür, Mimar Sinan Mah. |
| **Bornova Merkez** | **Yok** | Semt adı "Bornova" (35040). Kapsamı: Beşyol, Çamiçi, Kavaklıdere, … Ayrıca "Merkez Mah" Altındağ semtinde. |
| Nişantaşı | Yok | Teşvikiye ve Harbiye ayrı semt olarak var. |
| Etiler | Var (Beşiktaş) | Etiler Mah ile aynı. |

**Değerlendirme:**
- Semt sütunu **semt→mahalle alias tablosu için kısmi bir tohum**. Ankara'daki "Bilkent", "Balgat", "Dikmen", "Çayyolu", "Esat", "100.Yıl" ve İzmir'deki "Basmane", "Kemeraltı", "Pasaport" gerçek gündelik semt adları; her biri 1–14 mahalleye açılıyor.
- Ancak:
  - PTT semti posta bölgesi olduğu için halk semtleriyle örtüşmüyor (Moda, Kızılay ve Nişantaşı yok).
  - Bazı adlar boşluksuz bozulmuş: `Remzioğuzarık`, `Gen.hikmetakıncı`, `Pttevleri`, `Ondokuzmayıs`.
  - Yaklaşık 1.700 kayıt bilgi taşımıyor (ilçe adı veya "Merkezköyler").
- **Öneri:** `ptt_semt_alias_adaylari.csv` (1.069 satır: 364 bağımsız + 705 mahalle adıyla aynı) aday listesi olarak kullanılsın; tablo elle kürasyonla genişletilsin.

---

## 6. Posta kodu ↔ mahalle

PTT/murat verisinde 73.305 satır ve 2.771 kod var.

- Kodların hepsi 5 haneli rakam.
- **İlk 2 hane = il plakası: 73.305/73.305 (%100).**
- Bir (il, ilçe, mahalle) için birden fazla kod: **0**. Yani her mahallenin tam 1 kodu var.
- Birden fazla ilçeye yayılan kod: **0**. Posta kodu ilçeyi **kesin** belirliyor.
- İlçe başına kod: medyan 2, maksimum 21. Kod başına mahalle: medyan 8, p90 73, maksimum 669.
- Son 3 hane `x00` (700, 800, 500, …) baskın. [Merkez/kırsal ayrımı yorumu DOĞRULANMADI]
- (PK, katlanmış mahalle adı) ikilisi 8 durumda tekil değil (ör. 72402 "Karpuzlu", 73400 "Başak"). Bunlarda ayırt etmek için üst köy bilgisi gerekiyor.

**Sonuç:** PK → il %100, PK → ilçe %100. PK + mahalle adı ikilisi neredeyse her zaman tek kayda iniyor.

---

## 7. Wikidata ↔ NVİ/PTT

| Sınıf | Öğe | Koordinatlı | Kimlik dolu | Kimlikle NVİ'ye bağlanan | (ilçe, katlanmış ad) ile eşleşen | Toplam bağlanan |
|---|---|---|---|---|---|---|
| Mahalle Q17051044 | 27.975 | 22.478 (%80,4) | P12883: 27.952 (%99,9) | 27.823. Adı da tutan 27.302, tutmayan 521. | 24.061 (%86,0). P131 ilçeye çözülemeyen 3.406. | **27.829 (%99,5)** |
| Köy Q1529096 | 19.394 | 19.376 (%99,9) | P13588: 18.243 (%94,1) | 18.208. Adı da tutan 17.710. | 4.145 (%21,4) | **18.216 (%93,9)** |
| Eski köy Q136544643 | 217 | 199 | ~0 | 0 | 0 | 0 |
| Belde Q815324 | 486 | — | — | — | — | — |

- **Köylerde isim eşleşmesi neden düşük:** Köy öğelerinin `P131` değeri ilçe öğesini değil, ilçe merkezindeki **belediye/şehir** öğesini (Q15284/Q515) gösteriyor. Örnek: "Çorum" şehir öğesi. Bu yüzden isimle eşleştirme düşük kalıyor, ama kimlik ile %94'ü bağlanıyor.
- **İl düzeyinde:** `P395` yerine `P14358`/`P300` kullanılmalı (Konya/Uşak hatası).

**NVİ tarafından Wikidata koordinat kapsamı:**

| Birim | Kapsam |
|---|---|
| Tip 1 mahalle | **22.410 / 32.471 (%69,0)** |
| Köy (`koyKayitNo`) | **18.205 / 18.249 (%99,8)** |
| Köy alt birimleri (köy koordinatı miras alınarak) | 28.573 / 28.633 (%99,8) |

**İl başına (mahalle + köy) koordinat kapsamı:**
- Medyan %78. %50'nin altında 3 il var.
- En düşük: Yalova %46, Isparta %48, Afyonkarahisar %50, Aksaray %53, Osmaniye %54, Karaman %54, Nevşehir %55, Niğde %55.
- En yüksek: Adana %100, İstanbul %99, Erzurum %98, Ankara %98, Samsun %95.
- Tam tablo: `_derived/wd_coord_coverage_per_il.csv`.

---

## 8. Belirsizlik (ambiguity) ve çakışmalar

### 8.1 Tip 1 mahalle (32.471 kayıt, 16.104 distinct katlanmış ad)

**En sık 30 ad:**

> cumhuriyet 408, yeni 388, fatih 252, bahcelievler 164, merkez 156, ataturk 147, hurriyet 145, karsiyaka 129, yenikoy 107, esentepe 105, istiklal 94, yesilyurt 88, kurtulus 88, orta 87, zafer 84, yenice 75, kale 70, inonu 63, pinarbasi 59, akpinar 56, yesilkoy 55, camlica 55, yukari 54, ortakoy 53, yenidogan 51, derekoy 50, yunus emre 49, istasyon 47, hamidiye 46, yesiltepe 46

**Ad başına tekrar dağılımı:**

| Tekrar | Ad sayısı | Kayıt sayısı |
|---|---|---|
| 1 | 12.197 | 12.197 |
| 2 | 1.800 | 3.600 |
| 3–5 | 1.365 | 4.939 |
| 6–10 | 427 | 3.174 |
| 11–50 | 290 | 5.528 |
| 51–100 | 15 | 1.032 |
| >100 | 10 | 2.001 |

**Tekillik:**
- Türkiye genelinde tekil ada sahip kayıt: **12.197 (%37,6)**.
- İl içinde tekil: **25.571 (%78,8)**.
- İlçe içinde tekil: **31.897 (%98,2)**. İlçe içinde çakışan 574 kayıt var; çoğu farklı beldelerde aynı adı taşıyan mahalle (ör. Bucak'ta 3 ayrı "Pazar"). Bu yüzden **üst birim (belde/köy) anahtarın parçası olmalı.**

**Mahalle + köy (50.720 yerleşim adı):**
- Türkiye genelinde tekil %33,2, il içinde %81,8, ilçe içinde %98,6.

**Tüm 73.398 NVİ birimi:**
- Türkiye genelinde tekil %33,0, il içinde %72,3, ilçe içinde %88,1.
- Ad listesinde "merkez" 3.110 kez, **"köyün kendisi"** 491 kez geçiyor. "KÖYÜN KENDİSİ" bir NVİ mevki adı ve gerçek bir yer adı olarak ele alınmamalı.

**Yalnız katlama sonrası çakışan kayıtlar (aynı ilçe):**
- 3 durumda tek fark katlama: `KARŞIYAKA`/`KARSIYAKA`, `CUMHURİYET (KÖY)`/`CUMHURİYET`, `KÜLTÜR `/`KÜLTÜR` (sondaki boşluk).
- 6 durumda yalnız boşluk farkı var: `GAZİ OSMAN PAŞA`/`GAZİOSMANPAŞA`, `KEMAL PAŞA`/`KEMALPAŞA` …
- Bunlar NVİ'de **ayrı kimlikli** kayıtlar. Normalizasyon onları birleştirdiği için tie-break kuralı gerekiyor.

### 8.2 İsim çakışmaları

**Mahalle adı = ilçe adı:**
- Tip 1 mahalle adı herhangi bir ilçe adıyla aynı: **3.918**.
- Aynı ildeki bir ilçe adıyla aynı: **248**. Örnekler: Yüreğir/SEYHAN, Yüreğir/SARIÇAM, Pamukkale/KALE, Silvan/BAĞLAR, Nizip/ŞAHİNBEY, Akdeniz/TOROSLAR, Susurluk/BURHANİYE.
- Kendi ilçesinin adını taşıyan: **51** (Osmangazi/OSMANGAZİ, Karesi/KARESİ, Pamukkale/PAMUKKALE …).
- "MERKEZ" adlı tip 1 mahalle: 156.

**Diğer çakışmalar:**
- Köy/belde adı = bir ilçe adı: 1.535.
- Tip 1 mahalle adı = bir il adı: 128. Örnekler: Sakarya 26, Osmaniye 23, Karaman 17, Aydın 9, Iğdır 8, Düzce 7.
- Köy/belde adı = bir il adı: 48.
- İlçe adı = il adı: 0 (NVİ adlarıyla). Merkez ilçe kullanımı için bkz. §3.

---

## 9. Biçim tuhaflıkları

| Tuhaflık | NVİ `adi/koyAdi` | PTT (murat) | Örnek |
|---|---|---|---|
| Tümü BÜYÜK HARF | 73.398/73.398 | — (title case) | `19 MAYIS` |
| Sonek gömülü | `bilesenAdi`: `MAHALLESİ`, `KÖYÜ`, `MEVKİİ`, `MEZRASI`, `YAYLA EVLERİ`, `BELDESİ, …` | `Mah`, `Mh.`, `Mahallesi Mah`, `Yenimah.`, `Köyü`, `Beldesi` | `Ahmed-i Hani Mahallesi Mah` |
| Parantezle hiyerarşi | — (virgülle: `K KÖYÜ, X MEVKİİ`) | 29.845 satır; 3 seviyeli 198 | `Küme7 Mah (seydiahmet Mah) (koçak Köyü)` |
| Ad içinde parantez | 223 | — | `CUMHURİYET (KÖY)`, `AŞAĞIKINIK MERKEZ(ÇATACIK)` |
| titleCase hatası | — | 29.844 (parantez içi küçük harf) | `(çakırhüyük Beldesi)`, `P.t.t`, `B.ayranlı` |
| Rakam | 391; başta rakam 237 | 388 | `19 MAYIS`, `100.YIL`, `1.KÜME`, `KÜME3`, `2000 Evler` |
| Nokta / sıra sayısı | 345 | 346 | `100.YIL` vs `100. Yıl`, `1.KADRİYE` |
| Roma rakamı | 2 (`II OSB`, `SAKARYA I.ORGANİZE…`) | 1 | `ŞEYH ŞABAN-I VELİ` (Farsça -i tamlaması, Roma rakamı değil!) |
| Tire | 154 | 156 | `KUVA-İ MİLLİYE`, `FEVZİPAŞA-VEHBİBEY`, `Çiçek-1` |
| Kesme işareti | 2 | 2 | `TAN'IN KOMLARI` |
| Eğik çizgi | 6 | 6 | `TUZTAŞI/DEĞİRMEN` |
| Çift boşluk | 41 (`bilesenAdi`'nde 848) | 160 (130'u `  Mah` öncesi) | `AŞAĞI  EMİRLER` |
| Baş/son boşluk | **814** | — | `OSMANGAZİ ` |
| Şapkalı harf | 1 | 1 | `KÂHYALAR` |
| OSB | 119 | 118 | `GAZİOSMANPAŞA OSB` / `GAZİOSMANPAŞAOSB` |
| Küme evler | 170 | 175 | `1.KÜME`, `KURUCA KÜME EVLERİ` |
| Site | 32 | 32 | `DEMİRLİ SAHİL SİTESİ` |
| Ad içinde "köy" eki farkı | `ÇİFTLİKKÖY` | `Çiftlik Köyü` | 235 eşleşmeyi köy-eki kuralı kurtarıyor |
| Bitişik/ayrık yazım | `GAZİ OSMAN PAŞA` | `Gaziosmanpaşa` | Kompakt karşılaştırma gerekli |

---

## 10. 6360 dönemi tarihsel adlar (2014'te mahalleye dönen köyler)

| Aday kaynak | Bulgu | Uygulanabilirlik |
|---|---|---|
| **Wikidata P2123 (YerelNet köy ID) taşıyan mahalle öğeleri** | **16.528** mahalle öğesi eski köy kimliği taşıyor. 16.505'i NVİ'ye kimlikle bağlı (16.504'ü tip 1). **%98,8'i 30 büyükşehir ilinde.** Bu 6360'ın etki alanıyla birebir uyuşuyor (yasa yaklaşık 16 bin köyü dönüştürdü). | **Yüksek.** CC0, kimlik tabanlı, hazır: `_derived/wd_6360_aday.csv`. Ad çoğunlukla aynı kaldı ("X Köyü" → "X Mah", aynı ilçe), dolayısıyla alias kural ile üretilebilir. Eksikleri: eski ilçe bilgisi yok (2012'de kurulan yeni ilçeler); ad değişikliği yok. |
| Wikidata "former village of Turkey" (Q136544643) | 217 öğe. Kimlik ve `P576` yok. | Düşük, küçük kapsamlı |
| Wikidata `P576` / `P1366` | Türkiye il/ilçe/mahalle/köy/belde sınıflarında `P576` = 0. İlçede `P1366` = 5, köyde 2. | **Kullanılamaz.** Kaldırılmış ilçe öğeleri (ör. "Merkez (Şanlıurfa)") işaretsiz duruyor. |
| Wikidata mahalle + P13588 (TÜİK köy ID) | 418 öğe | Ek sinyal |
| YerelNet (yerelnet.org.tr) | Site kapalı; Wikidata formatter web.archive üzerinden. 2014 öncesi köy sayfaları arşivde olabilir. | Orta-düşük, arşiv kazıma gerekir **[DOĞRULANMADI]** |
| TÜİK ADNKS köy/mahalle nüfus tabloları (2013 vs 2014) | Aynı kimlikleri (P12883/P13588 = NVİ) kullanıyor. Yıllar arası fark tam bir köy→mahalle geçiş listesi verir. | Potansiyel olarak en iyi resmî kaynak. Erişim (biruni.tuik.gov.tr) ve lisans **[DOĞRULANMADI]**. |
| melihozkara NVİ diff dosyaları | Yalnız 2026-04 → 2026-10. Repo geçmişinde 3 anlık görüntü var (2025-02 SQL, 2026-04, 2026-10). | 6360 için **yok**. İleriye dönük değişiklik takibi için iyi model. |
| PTT 2022 dosyası | Hâlâ `X Mah (B Beldesi)` yapısında 388 güncel belde içeriyor. 2014'te kaldırılan ~1.000+ belde yok. | Kaldırılan beldeler için yok |

**Sonuç:** "X Köyü ↔ X Mahallesi" alias'ları Wikidata P2123 listesinden **otomatik** üretilebilir.

Elle küratörlük gerektirenler:
- **Kaldırılan belde → mahalle** eşlemeleri.
- **2008 (5747) ve 2012 (6360) ilçe yeniden yapılanması.** Eski "Merkez" ilçe → yeni ilçe(ler).

---

## 11. Faz 1 için öneriler

### 11.1 Seviye başına birincil kaynak

| Seviye | Birincil | Tamamlayıcı | Gerekçe |
|---|---|---|---|
| İl | NVİ (`kimlikNo`=plaka) | Wikidata: koordinat, ISO, QID | Tüm kaynaklar 81/81. Wikidata'da plaka için `P395` değil `P14358` kullanılmalı. |
| İlçe | NVİ `kimlikNo` + ad | Wikidata: `P14366` (275), yoksa (il, temizlenmiş etiket) ve çocuk oyu → 966/973 | NVİ ve PTT adları %100 aynı |
| Mahalle / köy / alt birim | **NVİ `kimlikNo` + `mahalleTur` + `koyKayitNo`** | PTT: posta kodu (ad join, %97,7) + semt. Wikidata: koordinat (kimlik join). | En güncel ve en eksiksiz kaynak. Tür alanı var. Kimlik Wikidata/TÜİK ile ortak. |
| Posta kodu | PTT (MIT, 2022 anlık görüntüsü) | — | Tek kaynak. Güncelliği risk: yeni mahallelerin ~%1'inde kod yok, ilçe düzeyinde PK fallback kullanılacak. |
| Koordinat | Wikidata (CC0) | — | Köy %99,8, tip 1 mahalle %69 |

**Lisans kararı (açık konu).** NVİ verisinin tek açık kopyası melihozkara. Bu kopya lisanssız ve CAPTCHA çözücüyle çekilmiş. İki yol var:

- **(A) Yazardan CC0 veya CC BY izni alınır, NVİ birincil olur.** Önerilen yol. Yazar e-postası README'de var. İzin gelene kadar NVİ verisi **paket içine gömülmez, yalnız doğrulama/derleme girdisi** olarak kullanılır.
- **(B) "Temiz" yedek yol:** gazetteer şu iki kaynaktan kurulur:
  - Wikidata (CC0): tip 1 mahallelerin 28.104'ü (%86,6) ve köylerin 18.228'i NVİ kimliğiyle.
  - PTT (MIT): 73.305 satır; mevki/mezra dahil tüm kalan birimler ve posta kodu.

  Eksik kalan: 2022 sonrası değişen ~%2–3 ve NVİ kimliği olmayan alt birimler. Pipeline her iki yolu da destekleyecek şekilde tasarlanmalı.
- Sokak verisi hiçbir yolda pakete girmez (zaten §3 kararı).

### 11.2 Join stratejisi

1. **Kararlı anahtar:** NVİ `kimlikNo` (mahalle düzeyi), `koyKayitNo` (köy), ilçe `kimlikNo`, plaka.
2. **Wikidata → NVİ:** önce kimlik, isim yalnız fallback.
   - P12883 → `kimlikNo`
   - P13588 → `koyKayitNo`
   - P14366 → ilçe `kimlikNo`
   - P14358 → plaka
   - İlçe fallback'i: (plaka, temizlenmiş etiket) → çocuk oyu (kimlikli mahallelerin `P131` çoğunluğu, ≥%80 ve ≥3 oy).
   - 521 mahalle ve 498 köyde kimlik tutup ad tutmuyor. Bunlar ad değişikliği adayı ve alias kaynağı olarak saklanmalı.
3. **PTT → NVİ:** (plaka, fold(ilçe), tür, fold(ad), fold(üst köy/belde)). Kademeler:
   - tam
   - ad (üst yok sayılır)
   - kompakt (boşluksuz)
   - köy-eki
   - bulanık (≥0,8, **yalnız elle onaylı tablo üzerinden**)

   PTT adı önce yapıya ayrıştırılmalı: `X Mah (Y Mah) (K Köyü)` → ad X, ara birim Y, köy K. Bu ayrıştırıcı `a2_mahalle.parse_ptt` içinde.
4. **Posta kodu ataması:**
   - tam/kompakt/köy-eki kademeleri → doğrudan.
   - "isim" kademesi → orta güven.
   - Eşleşmeyen NVİ kaydı → ilçenin "Merkezköyler" kodu ya da en sık kodu (düşük güven, işaretli).

### 11.3 Gereken normalizasyon kuralları

1. Türkçe küçük harf (I→ı, İ→i). U+0307 silinir. ASCII katlama (ç ğ ı ö ş ü â î û). Arama anahtarında iki biçim de tutulur: aksanlı ve katlanmış.
2. `trim` ve çoklu boşluk sadeleştirme. NVİ'de 814 sondaki boşluk ve 41 çift boşluk var.
3. Sonek sözlüğü (tek geçiş, ad içi "Yeni Mahalle" korunur):
   - mah, mah., mh, mh., mahalle(si)
   - köy(ü), (köyü), beldesi
   - mevkii, mezrası, yayla evleri / yaylası
4. **Kompakt anahtar:** boşluklar kaldırılır (`GAZİ OSMAN PAŞA` = `GAZİOSMANPAŞA`, `ALTI EYLÜL` = `ALTIEYLÜL`). İlçe içinde 6 gerçek çakışma var; tie-break olarak tam yazım tercih edilir.
5. **Köy-eki kuralı:** `XKÖY` ↔ `X Köyü` ↔ `X Köy`.
6. **Sayı ve sıra:**
   - `100.YIL` = `100. Yıl` = `Yüzüncü Yıl`
   - `19 MAYIS` = `Ondokuz Mayıs` = `Ondokuzmayıs`
   - `1.KÜME` = `Küme 1` = `KÜME1`
   - Sayı-kelime tablosu gerekiyor.
7. **Kısaltmalar:** `P.T.T`, `Gen.`, `Şht.` (Şehit), `Prof. Dr.`, `Hz.`, `K.` / `Y.` / `A.` (Küçük/Yukarı/Aşağı: `Y.YUMAKLI`, `B.ayranlı`, `A.tepecik`).
8. Farsça tamlama tiresi (`KUVA-İ MİLLİYE`, `ŞABAN-I VELİ`) Roma rakamı sanılmamalı.
9. `Cami`/`Camii`, `Ulucami`/`Ulucamii` gibi çift-i varyantları.
10. Tür bilgisi ayrı alana alınmalı (mahalle/köy/belde/mevki/mezra/yayla), adın içinde bırakılmamalı. "KÖYÜN KENDİSİ" ve "MERKEZ" mevkileri özel ele alınmalı.

### 11.4 Elle kurulacak (curated) tablolar

| # | Tablo | Tohum veri | Tahmini boyut |
|---|---|---|---|
| 1 | **İl alias:** Afyon, Urfa, (K.)Maraş, Antep, İçel→Mersin, Hakkâri, Adapazarı/İzmit (il mi ilçe mi), Ist./İst. | El ile | ~30 |
| 2 | **İlçe alias + tarihsel:** Eyüp→Eyüpsultan, Kazan→Kahramankazan, Ilıca→Aziziye, 19 Mayıs/Ondokuzmayıs, "<İl> Merkez"↔`MERKEZ`, 30 büyükşehrin eski merkez ilçesi→yeni ilçeler (ör. Şanlıurfa Merkez→Eyyübiye/Haliliye/Karaköprü; Kahramanmaraş→Onikişubat/Dulkadiroğlu) | Wikidata'da eşleşmeyen 36 aktif ilçe öğesi + el | ~150 |
| 3 | **Semt → mahalle alias:** Moda→Caferağa, Nişantaşı→Teşvikiye/Harbiye, Kızılay, Bağdat Cd. (iki ilçe), Taksim … | `_derived/ptt_semt_alias_adaylari.csv` (364 + 705 satır) + el | Faz 1'de büyük şehirler için ~500 |
| 4 | **Köy → mahalle (6360) alias** | `_derived/wd_6360_aday.csv` (16.505, otomatik) | Otomatik |
| 5 | **Kaldırılan belde → güncel mahalle/ilçe** | TÜİK/Wikipedia **[DOĞRULANMADI]** | ~1.000+ |
| 6 | **NVİ ↔ PTT elle eşleşme düzeltmeleri** | 265 bulanık çift + 1.672 / 1.393 eşleşmeyen (`_derived/mahalle_nvi_vs_ptt.csv`, `ptt_parsed_murat.csv`) | Yüzlerce |
| 7 | **Sayı-kelime ve kısaltma sözlüğü** | §9 ve §11.3 | ~100 |
| 8 | **Belirsiz ad öncelikleri (stoplist):** Cumhuriyet, Yeni, Fatih, Merkez, Atatürk … (en sık 30) | §8.1 | 30–100 |
| 9 | **Wikidata hata listesi:** Konya/Uşak `P395`, `Yahyal` | §1.3, §3 | Küçük. Wikidata'ya düzeltme olarak da gönderilebilir. |
| 10 | **mahalleTur 4 / 6 farkı** ve `koyKurumBelediyeTur` kodlarının resmî anlamı | NVİ'ye soru ya da örnek inceleme | — |

### 11.5 Açık / doğrulanmamış konular

- `mahalleTur=6` ile `4` arasındaki fark ve `koyKurumBelediyeTur` kodlarının resmî anlamı.
- PTT toplu dosyasının gerçek yayın tarihi. İçerik 2022-08 ile birebir aynı; daha yeni bir PTT kaynağı var mı?
- TÜİK ADNKS tablolarına erişim ve lisans (6360 ile belde geçişi için).
- melihozkara lisans talebinin sonucu.
