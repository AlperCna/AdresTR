# Staging (PTT + Wikidata) ile NVİ kopyasının karşılaştırması

Oluşturma: 2026-10-08. Üreten: `data/scripts/report_nvi_diff.py`.

> Bu rapor **paketlenen verinin parçası değildir**. NVİ kopyası (melihozkara, 2026-10-06, lisanssız) yalnızca karşılaştırma için okunur. Staging dosyalarına buradan hiçbir değer geri yazılmaz.

## Sayılar

| Seviye | Staging | NVİ |
|---|---|---|
| İl | 81 | 81 |
| İlçe | 973 | 973 |
| Birim satırı | 78790 | 73398 |
| Köy (distinct) | 18229 | 18249 |
| Mahalle (staging mahalle+osb / NVİ tip 1) | 32395 | 32471 |

İlçe kümesi (plaka + katlanmış ad): yalnız staging'de 0, yalnız NVİ'de 0.

Staging tür dağılımı: mahalle 32171, mevki 27374, koy 18229, yayla 374, mezra 265, osb 224, kume_evler 140, site 13

## NVİ'den staging'e (NVİ kaydı staging'de var mı?)

Eşleşme kademeleri:
- **tam:** ad + üst köy/belde tutuyor.
- **ad:** üst birim yok sayılıyor.
- **köy-eki:** `XKÖY` = `X Köyü`.

Anahtar: plaka, katlanmış ilçe, sınıf (mahalle/köy/alt birim) ve boşluksuz katlanmış ad.

| NVİ türü | n | tam | ad | köy-eki | **yok** | yok % |
|---|---|---|---|---|---|---|
| 1-mahalle | 32471 | 32053 | 16 | 4 | **398** | 1.2 |
| 3-mezra | 5093 | 4781 | 150 | 0 | **162** | 3.2 |
| 4-mevki | 21684 | 19786 | 969 | 0 | **929** | 4.3 |
| 5-yayla evleri | 1250 | 1161 | 49 | 0 | **40** | 3.2 |
| 6-mevki(?) | 606 | 566 | 14 | 0 | **26** | 4.3 |
| köy (koyKayitNo) | 18249 | 17375 | 0 | 373 | **501** | 2.7 |
| **toplam** | 79353 | 75722 | 1198 | 377 | **2056** | 2.6 |

## Staging'den NVİ'ye (staging birimi NVİ'de var mı?)

| staging tur | n | tam | ad | köy-eki | **yok** | yok % |
|---|---|---|---|---|---|---|
| koy | 18229 | 17376 | 0 | 373 | **480** | 2.6 |
| kume_evler | 140 | 112 | 27 | 0 | **1** | 0.7 |
| mahalle | 32171 | 31880 | 25 | 5 | **261** | 0.8 |
| mevki | 27374 | 25597 | 1197 | 4 | **576** | 2.1 |
| mezra | 265 | 252 | 9 | 0 | **4** | 1.5 |
| osb | 224 | 171 | 0 | 0 | **53** | 23.7 |
| site | 13 | 12 | 1 | 0 | **0** | 0.0 |
| yayla | 374 | 335 | 22 | 0 | **17** | 4.5 |
| **toplam** | 78790 | 75735 | 1281 | 382 | **1392** | 1.8 |

**Eksiklerin dağılımı (il bazında, ilk 10):**

- NVİ'de var, staging'de yok: BİTLİS 147, MUŞ 122, ŞIRNAK 111, KASTAMONU 76, ARTVİN 74, HAKKARİ 73, ÇORUM 67, GİRESUN 63, ELAZIĞ 62, BİNGÖL 59
- Staging'de var, NVİ'de yok: Elazığ 80, Tokat 61, Çorum 60, Kastamonu 51, Afyonkarahisar 48, Artvin 48, Gaziantep 48, Giresun 44, Zonguldak 43, Gümüşhane 39

## Wikidata'dan gelen `nvi_id` doğrulaması

Staging'deki `nvi_id` değerleri yalnız Wikidata'dan geliyor:
- mahalle/osb için P12883 → NVİ `kimlikNo`
- köy için P13588 → NVİ `koyKayitNo`

| Sonuç | Sayı |
|---|---|
| ilçe + ad tutuyor | 43507 |
| ilçe tutuyor, ad farklı | 403 |
| NVİ'de yok | 119 |
| farklı ilçe | 10 |

Örnek uyuşmazlıklar (birim_id, staging adı, NVİ il/ilçe, NVİ adı). Bunların çoğu ad değişikliği ya da yazım varyantı; "farklı ilçe" satırları birleştirme hatası adayı:

- 1040008 `Çürükler` → ADANA/FEKE `YEŞİLVADİ`
- 1060040 `Kıralan` → ADANA/KARAİSALI `HACIKIRI`
- 2060173 `Yazıca` → ADIYAMAN/MERKEZ `ÇENÇENG`
- 3010012 `Aşağı Beltarla` → AFYONKARAHİSAR/BAŞMAKÇI `BELTARLA`
- 3030031 `Kale` → AFYONKARAHİSAR/BOLVADİN `KARABAĞ`
- 3030063 `Yukarı Gökçeyayla` → AFYONKARAHİSAR/BOLVADİN `GÖKÇEYAYLA`
- 3070047 `Aktoprak` → AFYONKARAHİSAR/DİNAR `GÜRDEN`
- 3070051 `Bağcılar` → AFYONKARAHİSAR/DİNAR `HACIBEŞİRLİ`
- 3070054 `Bülücalan` → AFYONKARAHİSAR/DİNAR `BÜLÜÇALANI`
- 3070064 `Çürüklü` → AFYONKARAHİSAR/DİNAR `TÜRKMEN`
- 3150025 `Bekteş` → AFYONKARAHİSAR/SANDIKLI `BEKTAŞ`
- 3160078 `Yörük Mezarı` → AFYONKARAHİSAR/SİNANPAŞA `YÜRÜKMEZARI`
- 3170045 `Senir` → AFYONKARAHİSAR/ŞUHUT `SİNİRKÖY`
- 4020053 `Halaç` → AĞRI/DOĞUBAYAZIT `HALLAÇ`
- 4020086 `Tutumlu` → AĞRI/DOĞUBAYAZIT `TULUMLU`
- 4050098 `Soğanköy` → AĞRI/MERKEZ `SOĞAN ELEŞKİRT`
- 5010012 `Çamurlu` → AMASYA/GÖYNÜCEK `KARADAĞ`
- 5010022 `Kafarlı` → AMASYA/GÖYNÜCEK `GAFFARLI`
- 5010033 `Sığırçayı` → AMASYA/GÖYNÜCEK `ULUPINAR`
- 5010034 `Şıhlar` → AMASYA/GÖYNÜCEK `ŞEYHLER`
- 5010035 `Şıhoğlu` → AMASYA/GÖYNÜCEK `ŞEYHOĞLU`
- 5030017 `Umarca` → AMASYA/HAMAMÖZÜ `OMARCA`
- 5040059 `Büyükkızılca` → AMASYA/MERKEZ `KIZILCA`
- 6010011 `Galaba` → ANKARA/AKYURT `KALABA`
- 6040002 `Afşar` → ANKARA/BALA `AFŞAR DADALOĞLU`

## Örnekler

### NVİ'de olup staging'de olmayan (rastgele 25)

- BARTIN/MERKEZ: GÜRGENPINARI MAHALLESİ (tip 1)
- KARABÜK/OVACIK: AMBARÖZÜ KÖYÜ, ASARCIK MEVKİİ (tip 4)
- BARTIN/MERKEZ: CELİLBEYOĞLU KÖYÜ, SARAÇLAR MAHALLESİ (tip 1)
- HATAY/KUMLU: YENİKÖY MAHALLESİ (tip 1)
- HAKKARİ/YÜKSEKOVA: İNANLI MAHALLESİ (tip 1)
- OSMANİYE/MERKEZ: AKYAR MAHALLESİ (tip 1)
- HAKKARİ/YÜKSEKOVA: YONCALIK MAHALLESİ (tip 1)
- ÇORUM/ORTAKÖY: KAVAKALANI KÖYÜ (tip 0)
- ŞIRNAK/SİLOPİ: DEREBAŞI KÖYÜ, KÖYÜN KENDİSİ MEVKİİ (tip 4)
- RİZE/İKİZDERE: ÇİFTEKÖPRÜ KÖYÜ, ARÇOVİT MEVKİİ (tip 4)
- GİRESUN/DOĞANKENT: ÜÇTAŞ KÖYÜ, YEMİŞEN YAYLA EVLERİ (tip 5)
- ÇORUM/MERKEZ: KARABÜRÇEK KÖYÜ, KARABÜRÇEK MÜCAVİR MEVKİİ (tip 4)
- BİTLİS/TATVAN: BOLALAN KÖYÜ, KÖYÜN KENDİSİ MEVKİİ (tip 4)
- YOZGAT/YERKÖY: SARAY KÖYÜ, ORGANİZE SANAYİ BÖLGESİ MEVKİİ (tip 4)
- BARTIN/MERKEZ: UZUNÖZ MAHALLESİ (tip 1)
- GÜMÜŞHANE/TORUL: KİRAZLIK KÖYÜ, BAŞ MH.(MEZRA) MEZRASI (tip 3)
- BİNGÖL/SOLHAN: OYMAPINAR KÖYÜ, MORDERE MEZRASI (tip 3)
- BURDUR/ÇAVDIR: KOZAĞAC KÖYÜ, BELEN MEVKİİ (tip 4)
- BOLU/MUDURNU: BEYDERESİ KÖYÜ, YAYLA EVİ(ALANBAŞI) YAYLA EVLERİ (tip 5)
- BİTLİS/MUTKİ: KOVANLI KÖYÜ, KÖYÜN KENDİSİ MEVKİİ (tip 4)
- HATAY/REYHANLI: 15 TEMMUZ MAHALLESİ (tip 1)
- KÜTAHYA/GEDİZ: KURTÇAM KÖYÜ, KURTÇAM KÖYÜ MEVKİİ (tip 4)
- BİTLİS/MUTKİ: ÇATALERİK KÖYÜ, KÖYÜN KENDİSİ MEVKİİ (tip 4)
- KARABÜK/ESKİPAZAR: KAPICILAR KÖYÜ (tip 0)
- SİVAS/DİVRİĞİ: PURUNÖNÜ KÖYÜ (tip 0)

### Staging'de olup NVİ'de olmayan (rastgele 25)

- Niğde/merkez: Balhasan [mahalle, 51100]
- Zonguldak/caycuma: Şehler [koy, 67960]
- Edirne/meric: Serem [koy, 22680]
- Tokat/merkez: Hasanbaba (Hasanbaba) [mevki, 60010]
- Elazığ/merkez: Hankendi [koy, 23150]
- Ardahan/merkez: Sulakyurt (Sulakyurt) [mevki, 75002]
- Giresun/kesap: Harmandarlı [koy, 28902]
- Sivas/merkez: Uzuntepe [mahalle, 58060]
- Adıyaman/kahta: Mülk (Çıralık) [mevki, 02402]
- Düzce/akcakoca: Beyhanlı [koy, 81652]
- Bilecik/merkez: Taşçılar (Taşçılar) [mevki, 11230]
- Rize/guneysu: Selamet [koy, 53390]
- Kilis/merkez: Çakkallıpınar [koy, 79002]
- Giresun/bulancak: Sanayii [mahalle, 28300]
- Konya/karatay: Kerim Dede [mahalle, 42020]
- Uşak/banaz: Hatıplar [koy, 64502]
- Bilecik/merkez: Dereşemsettin (Dereşemsettin) [mevki, 11230]
- Eskişehir/odunpazari: 75. Yıl (Sultandere) [mahalle, 26250]
- Gümüşhane/torul: Dikmeler (Budak) [mevki, 29802]
- Elazığ/merkez: Yadigar (Yemişlik) [mevki, 23350]
- Tokat/erbaa: Alpaslan (Akça) [mevki, 60502]
- Giresun/gorele: Türkelli (Türkelli) [mevki, 28802]
- Afyonkarahisar/dinar: Yaka (Yaka) [mevki, 03402]
- Artvin/yusufeli: Murhaçgil (Dutluk / Tekkale) [mevki, 08890]
- Yozgat/akdagmadeni: Örenkale [koy, 66302]

