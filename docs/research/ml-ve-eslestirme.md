# AdresTR — ML, LLM geri dönüşü ve adres eşleştirme araştırması (Faz 9)

> Tarih: 2026-10-08 · Kapsam: PLAN.md Faz 9.5 (ML yolu), 9.6 (LLM geri dönüşü), 9.7 (adres eşleştirme)
> Dayanak: [PLAN.md](../plan/PLAN.md), [ARASTIRMA.md](../plan/ARASTIRMA.md)
>
> İşaretler:
> - **[DOĞRULANMADI]**: birincil kaynaktan teyit edilemedi veya yalnızca ikincil kaynakta geçiyor.
> - **[TAHMİN]**: bizim hesabımız. Ölçülmedi, ölçülmeden README'ye girmemeli.
> - Sürüm numaraları ve dosya boyutları 2026-10-08'de NuGet, PyPI ve Hugging Face API'lerinden okundu.

---

## 0. Özet (TL;DR)

| Konu | Karar / öneri |
|---|---|
| **Model** | Düzenli olarak dağıtılacak model **`dbmdz/electra-small-turkish-cased-discriminator`** (13,7M parametre, MIT, int8 ile yaklaşık 14 MB). Bunu **`dbmdz/bert-base-turkish-cased`** (111M, MIT) öğretmen modelinden damıtarak (distillation) eğitiyoruz. "Doğru ama ağır" seçenek olarak öğretmen modeli de isteğe bağlı sunulabilir. 128k sözlüklü BERTurk'ün kazancı küçük (WikiANN +0,66 F1, deprem-ml +0,01 makro F1), boyutu ise yaklaşık 1,7 kat. |
| **Veri** | Kişisel veri kullanılmaz. (1) BenchGen sentetik + gürültülü üretim (BIO etiketleri üreticiden bedavaya gelir), (2) resmi kurum adresleri üzerinde kural parser'ının yüksek güvenli çıktıları (zayıf denetim), (3) playground'dan açık rızayla gelen düzeltmeler. Huawei'nin 1.248 sorguluk çalışması makro F1 ≈ 0,50'de kaldı. Az gerçek veri yetmiyor, sentetik ön-eğitim şart. |
| **Pipeline** | HF `Trainer` → `optimum-cli export onnx --task token-classification` → `quant_pre_process` → `quantize_dynamic` (MatMul + Gather) → Python/.NET parite testi. .NET tarafında **Microsoft.ML.OnnxRuntime 1.30.0** + **Microsoft.ML.Tokenizers 2.0.0 `BertTokenizer`** kullanılır. **Dikkat:** `LowerCaseBeforeTokenization` varsayılanı `true` ve bu Türkçe I/İ'yi bozar. Mutlaka `false` yapılmalı. |
| **Gecikme** | Tipik bir adres (30–60 token) için ELECTRA-small int8 ile tek istekte yaklaşık 1–4 ms, BERT-base int8 ile yaklaşık 10–30 ms **[TAHMİN]**. ML yalnızca düşük güvenli girdilerde çalışacağı için ortalama maliyet bunun bir kesri olur. |
| **LLM** | `IChatClient.GetResponseAsync<T>()`. Aday ID listesi her istekte JSON şemasına **enum** olarak gömülür. Böylece LLM yalnızca **seçer ve span işaretler**, varlık uyduramaz. Gazetteer doğrular. Varsayılan kapalı, opt-in. Yurt dışı sağlayıcı KVKK m.9'a takılır, bu yüzden Ollama (Qwen3.5 / Gemma 4, Apache-2.0) ile self-host seçeneği sunulur. |
| **Eşleştirme** | Kanonik ID'lerle **blocking** (il, ilçe, mahalle) → cadde/sokak karşılaştırması (katlanmış ad üzerinde Jaro–Winkler, numaralı sokakta tam eşitlik) → dış kapı tam eşitlik → iç kapı/kat/daire. Sonuç bir **seviye** olarak döner: SameUnit / SameBuilding / SameStreet / SameMahalle / Different / Unknown. Embedding yalnızca tetikleyici koşul sağlanırsa eklenir. Metrikler: pairwise P/R/F1, B³, blocking için pair completeness ve reduction ratio. |
| **Sıra** | ML ancak kurallar platoya ulaştıktan **ve** tetikleyici eşikler aşıldıktan sonra devreye girer (Bölüm 6). Kural tabanlı eşleştirici ML'den bağımsızdır ve erken yayınlanabilir. |

---

## 1. Model seçimi: Türkçe token sınıflandırma (CPU / .NET)

### 1.1 Aday tablosu

Parametre sayıları HF `safetensors.total` alanından veya makalelerden alındı. int8 boyutları **[TAHMİN]**. Hesap şöyle yapıldı: tüm ağırlıklar (Gather/embedding dahil) int8'e çevrilirse yaklaşık 1 bayt/parametre, embedding fp32 kalırsa `4 × embedding + 1 × geri kalan`. Gözlenen kıyas noktaları:
- `Xenova/bert-base-cased` `model_int8.onnx` = 109 MB
- `Xenova/distilbert-base-uncased` int8 = 67 MB
- `akdeniz27/bert-base-turkish-cased-ner-quantized` = 185 MB. Embedding'i quantize edilmemiş, 32k × 768 fp32 ≈ 98 MB.

| Model | Mimari / sözlük | Parametre | int8 ONNX (tahmin) | Lisans | Türkçe NER sonucu |
|---|---|---|---|---|---|
| `dbmdz/bert-base-turkish-128k-cased` | BERT-base, WordPiece 128k, cased | 184,3M (embedding 98,3M) | ~185 MB (Gather int8) / ~480 MB (embedding fp32) | MIT | WikiANN-tr **93,92**; deprem-ml adres makro F1 **0,84** |
| `dbmdz/bert-base-turkish-cased` (32k) | BERT-base, WordPiece 32k | 111M | ~110 MB / ~185 MB (akdeniz27 gözlemi) | MIT | WikiANN-tr 93,26; deprem-ml 0,83; TabiBench token-clf ortalaması 93,67 |
| `dbmdz/distilbert-base-turkish-cased` | 6 katman, 32k | 68M | ~68 MB | MIT | WikiANN-tr 91,17; Huawei adres makro F1 0,363 / 0,407 (MLP başlıklı) |
| `dbmdz/convbert-base-turkish-cased` | ConvBERT, 32k | 107M | ~107 MB | MIT | WikiANN-tr **93,93**; deprem-ml **0,70** (adreste zayıf) |
| `dbmdz/electra-base-turkish-cased-discriminator` | ELECTRA-base, 32k | 111M | ~110 MB | MIT | WikiANN-tr 93,60; deprem-ml 0,76 (mC4 varyantı) |
| **`dbmdz/electra-small-turkish-cased-discriminator`** | ELECTRA-small: 12 katman, gizli boyut 256, embedding 128 | **13,7M** | **~14 MB** | MIT | WikiANN-tr **91,07** |
| `ytu-ce-cosmos/turkish-base-bert-uncased` | BERT-base, 32k, uncased | 110,7M | ~110 MB | MIT (README gövdesinde; HF metadata'da lisans alanı yok) | TabiBench token-clf ortalaması 93,60 |
| `ytu-ce-cosmos/turkish-medium-bert-uncased` | 8 katman, gizli 512 | 42,2M | ~42 MB | MIT (README gövdesinde) | NER raporlanmamış |
| `ytu-ce-cosmos/turkish-small-bert-uncased` | 4 katman, gizli 512 | 29,6M | ~30 MB | MIT (README gövdesinde) | NER raporlanmamış |
| `ytu-ce-cosmos/turkish-mini-bert-uncased` | 4 katman, gizli 256 | 11,6M | ~12 MB | MIT (README gövdesinde) | NER raporlanmamış |
| `ytu-ce-cosmos/turkish-tiny-bert-uncased` | 2 katman, gizli 128 | 4,6M | ~5 MB | MIT | NER raporlanmamış |
| `FacebookAI/xlm-roberta-base` | XLM-R, SentencePiece 250k | 278M (embedding ~192M) | ~280 MB | MIT | deprem-ml 0,75; Huawei'nin en iyisi makro F1 **0,497** (`akdeniz27/xlm-roberta-base-turkish-ner`) |
| `microsoft/Multilingual-MiniLM-L12-H384` | XLM-R tokenizer, küçük | ~118M **[DOĞRULANMADI]** | ~118 MB | MIT **[DOĞRULANMADI]** | Türkçe adres sonucu yok |
| `jhu-clsp/mmBERT-base` (2025) | ModernBERT, 22 katman, Gemma tokenizer 256k | 307M (embedding 196,6M) | ~310 MB | MIT | TabiBench token-clf ortalaması **93,81** (en yüksek) |
| `jhu-clsp/mmBERT-small` (2025) | ModernBERT, 22 katman, gizli 384, 256k | ~140M (fp32 dosya 564 MB) | ~140 MB | MIT | Türkçe NER sonucu bulunamadı |
| `boun-tabilab/TabiBERT` (2025-12) | ModernBERT-base, BPE 50k (Kumru tokenizer), 8k bağlam | 149M | ~150 MB | **Apache-2.0** | TabiBench token-clf ortalaması 93,42 |
| `VRLLab/TurkishBERTweet` | RoBERTa, BPE 100k, sosyal medya | ~163M | ~165 MB | MIT | TabiBench token-clf ortalaması 92,02 |
| `deprem-ml/adres_ner_v2_bert_128k` | BERTurk-128k üzerine adres NER | 184M | — | **Lisans belirtilmemiş**, veri özel | makro F1 0,84 (957 varlık) |

Kaynak notları:
- WikiANN rakamları stefan-it/turkish-bert README'sinden, 5 seed ortalaması.
- TabiBench rakamları TabiBERT makalesinin (arXiv 2512.23065) Tablo 5'inden. Kategori, WikiNER + WikiANN-TR + 2 POS veri setinin mikro-F1 ortalamasıdır, sadece NER değildir.
- deprem-ml rakamları model kartından.
- Huawei rakamları arXiv 2306.13947 Tablo 3'ten. Token düzeyinde, 25 BIO etiketi üzerinden makro ortalama, seqeval span F1 değil.

### 1.2 Gözlemler

1. **Genel NER'de modeller arası fark küçük, adreste büyük.**
   - WikiANN'de base modellerin hepsi 93–94 arasında.
   - Adres verisinde (deprem-ml) BERTurk 0,83–0,84, ConvBERTurk ise 0,70.
   - Ders: Genel NER skoruna bakarak seçim yapmak yanıltıcı. **Kendi benchmark'ımızda en az 3 aday karşılaştırılmalı** (Bölüm 6, M2).
2. **128k sözlüğün kazancı küçük, maliyeti büyük.**
   - Kazanç: deprem-ml'de +0,01, WikiANN'de +0,66 puan.
   - Maliyet: embedding 4 kat büyüyor, toplam parametre ~1,66 kat (184M'e karşı 111M).
   - Adres metni kısa ve sözlük dışı (OOV) özel adlarla dolu. 128k'nın faydası rare-word bölünmesinin azalması. Bu fayda bizde de görülebilir, o yüzden **öğretmen** olarak denenebilir, ama dağıtılacak model olarak değil.
3. **ELECTRA-small en iyi "boyut/kalite" noktası.**
   - 13,7M parametreyle WikiANN'de 91,07. Base'e göre −2,2 F1, boyut ise ~8 kat küçük.
   - Cased WordPiece kullanıyor, `vocab.txt` dosyası doğrudan .NET `BertTokenizer` ile yüklenebiliyor.
   - Adres verisinde damıtma (distillation) ile farkın kapanması beklenir **[TAHMİN]**.
4. **Uncased cosmos modelleri Türkçe küçültme ister.**
   - Model kartı açıkça şunu söylüyor: `do_lower_case=True` kullanma, `text.replace("I","ı").lower()` yap.
   - .NET'te bu işi kendi `TurkishText` küçültmemiz üstlenir (Bölüm 3.4). Hata riski cased modele göre daha yüksek.
   - Cosmos small/mini/medium/base'in HF metadata'sında lisans alanı boş. README gövdesinde "MIT" yazıyor. Paket zincirinde bunu açıkça belgelemek gerekir.
5. **ModernBERT ailesi (TabiBERT, mmBERT) 2025–2026'nın yenileri, ama bizim için şimdilik değil.**
   - mmBERT-base TabiBench token sınıflandırmada en yüksek skoru aldı (93,81). Ancak 256k Gemma sözlüğü yüzünden 307M parametre var. TabiBERT makalesine göre Türkçede fertility (kelime başına token) TabiBERT'ten %41 daha yüksek.
   - TabiBERT Apache-2.0 ve modern, ama token sınıflandırmada BERTurk'ten iyi değil (93,42'ye karşı 93,67).
   - İkisi de BPE/SentencePiece `tokenizer.json` kullanıyor. `Microsoft.ML.Tokenizers` ile birebir parite riski WordPiece'e göre yüksek **[DOĞRULANMADI]**.
   - ModernBERT'in ONNX dışa aktarımı optimum'da destekleniyor, ancak transformers 5.x ile uyumluluğu **[DOĞRULANMADI]** (Bölüm 3.2).
   - → **İzleme listesi.** Sonuçları yeniden değerlendirmek için M2'de bir deney satırı eklenebilir.
6. **XLM-R / mBERT gerekmiyor.** Türkçe tek dilli modeller hem deprem-ml'de hem Huawei'de (MLP başlıklı ayarda) ya eşit ya daha iyi. XLM-R'nin 250k sözlüğü boyutu şişiriyor.
7. **2025–2026 Türkçe encoder taraması** (HF araması, 2026-10-08):
   - TabiBERT (2025-03 yüklendi, makale 2025-12)
   - `99eren99/ModernBERT-base-Turkish-uncased-mlm` (2025-01, topluluk modeli)
   - `akdeniz27/tabibert-base-tr-uncased-ner` (2026-03, TabiBERT üzerine NER)
   - Bunlar dışında kurumsal bir yeni Türkçe encoder bulunamadı. Kumru (VNGRS) decoder bir LLM'dir, encoder değildir.

### 1.3 Öneri

| Rol | Model | Gerekçe |
|---|---|---|
| **Dağıtılan (varsayılan)** | `dbmdz/electra-small-turkish-cased-discriminator` → damıtılmış `adrestr-ner-small` | Yaklaşık 14 MB, milisaniye düzeyinde gecikme, MIT, cased WordPiece. .NET `BertTokenizer` ile Türkçe büyük/küçük harf sorunu yok. |
| **Öğretmen / "accurate" seçeneği** | `dbmdz/bert-base-turkish-cased` (32k) | Adres verisinde 128k ile neredeyse aynı. int8 ile yaklaşık 110 MB. İsteğe bağlı ikinci model olarak indirilebilir. |
| Deney (M2'de karşılaştırma) | `bert-base-turkish-128k-cased`, `ytu-ce-cosmos/turkish-small-bert-uncased`, `TabiBERT` | 128k öğretmen kalitesini artırıyor mu? Uncased small, ELECTRA-small'dan iyi mi? ModernBERT bir sıçrama yapıyor mu? |

> Not: deprem-ml modelinin lisansı yok ve verisi özel. Bu yüzden **başlangıç noktası olarak kullanılmaz**, yalnızca kıyas olarak alıntılanır.

---

## 2. Eğitim verisi stratejisi (kişisel veri olmadan)

### 2.1 Etiket şeması (BIO)

`eval/GUIDELINES.md` (Faz 3a) ile birebir aynı etiket adları kullanılır. Benchmark etiketleri ile model etiketleri aynı sözlükten gelir.

```
O
B-IL        I-IL
B-ILCE      I-ILCE
B-MAHALLE   I-MAHALLE      # mahalle + köy (tür ayrı bir alan, span değil)
B-SEMT      I-SEMT
B-CSBM_TUR  I-CSBM_TUR     # "Cad.", "Sokağı", "Bulvarı"
B-CSBM_AD   I-CSBM_AD      # "Moda", "1203/5", "Atatürk"
B-SITE      I-SITE
B-BLOK      I-BLOK
B-DIS_KAPI  I-DIS_KAPI
B-KAT       I-KAT
B-DAIRE     I-DAIRE
B-POSTA_KODU
B-TARIF     I-TARIF        # "... karşısı", "... yanı"
# isteğe bağlı, LLM'e göndermeden önce maskeleme için:
B-TELEFON   I-TELEFON
B-KISI      I-KISI         # yalnızca sentetik ad listeleriyle eğitilir
```

- Toplam etiket sayısı 2 × 14 + 1 = 29. İsteğe bağlı iki PII sınıfıyla 33.
- **Anahtar sözcükler span'e dahil değildir:**
  - `Caferağa Mah.` için `Caferağa` B-MAHALLE, `Mah.` O olur. `mah/mh` zaten kurallarla yakalanıyor. Model ad sınırını öğrenir.
  - İstisna CSBM: tür sözcüğü ayrı etiket alır (`CSBM_TUR`), çünkü kanonik çıktıda kullanılıyor.
- **Etiketler kelime düzeyindedir.** Kelime sınırlarını bizim tokenizer'ımız (Faz 4a) belirler: `147sok` → `147 sok`, `Kadıköy'de` → `Kadıköy 'de`. Model bu kelimeleri `is_split_into_words=True` ile alır. Böylece kural tarafı ile ML tarafı aynı token sınırlarında konuşur (Bölüm 3.1 ve 3.4).

### 2.2 Kaynaklar

| # | Kaynak | Nasıl etiketlenir | Hacim (öneri) | Not |
|---|---|---|---|---|
| A | **BenchGen sentetik + gürültü** (Faz 3b) | Üretici her bileşenin offset'ini zaten biliyor. Gürültü katmanları (ASCII katlama, Q-klavye yazım hatası, yapışık yazım, sıra değiştirme, kırık İ…) offset haritasını taşımak zorunda. | 100k–300k örnek | Etiket gürültüsü sıfır. Dağılım farkı yüksek. **Test setine asla girmez.** |
| B | **Resmi kurum adresleri** (MEB okulları, hastaneler, belediyeler; Faz 3c'deki kaynak URL'leriyle) | Kural parser'ının çıktısı. Yalnızca kalibre güveni ≥ 0,90 olan alanlar etiketlenir, diğer token'lar **maskelenir** (label = −100, kayba girmez). | 5k–20k | Zayıf denetim. Gerçek dağılıma yakın. Eğitime giren kurum adresleri dev/test bölümlerinden **dışlanır**. |
| C | **Gold (elle düzeltilmiş)** | Label Studio'da B'den örneklenen ve kuralların zorlandığı vakalar. Belirsizlik örneklemesi kullanılır (model ile kural çelişkisi, düşük güven). | 1k–3k | Değerlendirme seti Faz 3'teki dev/test'tir. Eğitim gold'u bunlardan ayrı tutulur. |
| D | **Playground düzeltmeleri** (opt-in) | Kullanıcı yanlış etiketi düzeltir ve "katkı olarak gönder" der. | Organik | Bkz. 2.4 KVKK. Haftalık toplu PR olarak dataset reposuna girer. |
| — | OSM `addr:*` (ODbL) | — | — | **Model eğitiminde kullanılmaz.** ODbL'li veriden eğitilmiş ağırlıkların "Produced Work" mü "Derivative Database" mi sayılacağı belirsiz **[DOĞRULANMADI, hukuki]**. Yalnızca ayrı `osm` değerlendirme konfigürasyonunda kullanılır. |

**Zayıf denetim aracı:** Ayrı bir framework gerekmiyor. Kaynak B'de tek "labeling function" kural parser'ının kendisi. Birden fazla etiketleme fonksiyonu olursa (kural, gazetteer tam eşleşme, regex) çelişkileri birleştirmek için skweak (MIT, spaCy tabanlı, HMM ile birleştirme) veya Snorkel kullanılabilir. MVP'de kural parser yeterli.

**Aktif öğrenme döngüsü** (M3 sonrası):
1. Modeli gerçek, etiketsiz havuzda (kurum adresleri) çalıştır.
2. Şu üç kovadan örnek seç:
   - (a) model–kural çelişkisi
   - (b) model entropisi yüksek
   - (c) kural güveni 0,4–0,7 arası
3. Kovalar çakıştığında öncelik çelişki kovasındadır.
4. Haftada 200 örnek etiketle → C'ye ekle → yeniden eğit.

### 2.3 Ne kadar veri gerekir?

| Çalışma | Veri | Sonuç | Ders |
|---|---|---|---|
| **Ünal, Aygün, Gerek (Huawei TR)**, arXiv 2306.13947 | Petal Maps kullanıcı sorguları, **toplam 1.248** (yaklaşık 874 eğitim / 187 doğrulama / 187 test), 14 sınıf, 25 BIO etiketi, CoNLL formatında, POI ağırlıklı ve dengesiz (DOOR en az) | En iyi token-makro F1 **0,497** (XLM-R-tr-ner), en iyi örnek düzeyi doğruluk **0,654** (BERTurk-ner). DistilBERTurk 0,36–0,41. | Birkaç yüz gerçek örnekle nadir sınıflar (kapı, kat, blok) öğrenilemiyor. Makro F1'i nadir sınıflar aşağı çekiyor. |
| **deprem-ml adres NER v2** (BERTurk-128k) | Özel veri seti. Test desteği **957 varlık**. Eğitim boyutu kartta yok **[DOĞRULANMADI]**. 5 epoch, lr 5e-5. | Makro F1 **0,84**, overall (mikro) F1 0,86. il 0,97, ilçe 0,92 ama **bina 0,70, kapı no 0,71, site 0,69, sokak 0,73, mahalle 0,79**. | ML il/ilçe gibi sık ve kapalı sözlüklü alanlarda iyi. Yapısal alanlarda (kapı, bina) kurallar daha iyi. Bu, ADR-001'in gerekçesi. ML'i **ad sınırları** (mahalle/sokak/site adı) için kullan, sayısal alanları kurallara bırak. |

**[TAHMİN] Hedef hacimler:**
- **Sentetik ön-eğitim:** Etiket başına en az 5k span görülmeli. Nadir sınıflar (BLOK, SITE, TARIF) için şablonlar fazla örneklenmeli. 100k cümle bunu rahatça karşılar.
- **Gerçek ince ayar:** Sınıf başına en az 300–500 span hedeflenir. Tipik adreste 5–7 span olduğu için **2k–5k gerçek adres** (B + C) yeterli.
- **Dev/test:** Faz 3'teki 1.000 / 2.000 aynen kullanılır. Model bu setlerle **eğitilmez**.
- Huawei sonucu, gerçek verinin tek başına 1k civarında kalmasının yetersiz olduğunu gösteriyor. deprem-ml'in sonucu ise birkaç bin etiketli örnekle 0,8+ seviyesine çıkılabileceğini düşündürüyor **[DOĞRULANMADI, eğitim boyutu bilinmiyor]**.

### 2.4 Playground düzeltmeleri ve KVKK

- **Varsayılan: hiçbir şey gönderilmez.** Playground Blazor WASM'da, tarayıcıda çalışır.
- "Düzeltmeyi bağış olarak gönder" düğmesine basıldığında:
  1. Kullanıcıya bunun **kamuya açık bir veri setine CC0 olarak gireceği** açıkça söylenir. Onay kutusu ve tarih tutulur.
  2. Dış kapı, kat ve daire numaraları **otomatik olarak rastgele bir değere çevrilir**: aynı biçim korunur (`17/A` → `23/B`), etiketler aynı kalır. Telefon ve kişi adı span'leri silinir.
  3. Kullanıcıya "kişisel adresiniz yerine bir işyeri/kamu adresi tercih edin" uyarısı gösterilir.
  4. Gönderim, GitHub issue ya da form üzerinden yapılır. Ham adres API loglarına girmez (ADR-009).
- Dönüştürme sonrası adres, gerçek bir kişiyle ilişkilendirilebilir olmaktan büyük ölçüde çıkar. Yine de mahalle + sokak düzeyinde kalan bilgi için hukuki görüş alınmalıdır **[DOĞRULANMADI, hukuki]**.

---

## 3. Uçtan uca pipeline

### 3.1 Eğitim (Python, HF Transformers)

Sürümler (PyPI, 2026-10-08):
- `transformers` 5.19.0
- `onnxruntime` 1.30.0
- `onnx` 1.23.2
- `optimum` 2.3.0 ve `optimum-onnx` 0.1.0 (2025-12-23)

ONNX dışa aktarma `optimum` çekirdeğinden ayrılıp `optimum-onnx` paketine taşındı.

```python
# train_ner.py — outline
from datasets import load_dataset
from transformers import (AutoTokenizer, AutoModelForTokenClassification,
                          DataCollatorForTokenClassification, Trainer, TrainingArguments)
import evaluate, numpy as np

LABELS = ["O", "B-IL", "I-IL", "B-ILCE", "I-ILCE", ...]       # from eval/labels.json (single source of truth)
id2label = dict(enumerate(LABELS)); label2id = {l: i for i, l in id2label.items()}

base = "dbmdz/bert-base-turkish-cased"                          # teacher; student: electra-small
tok = AutoTokenizer.from_pretrained(base)                       # do_lower_case=False from tokenizer_config.json
model = AutoModelForTokenClassification.from_pretrained(
    base, num_labels=len(LABELS), id2label=id2label, label2id=label2id)

ds = load_dataset("json", data_files={"train": "train.jsonl", "dev": "dev.jsonl"})
# each row: {"words": [...AdresTR tokenizer output...], "tags": [...BIO per word or "-" for masked...]}

def align(batch):
    enc = tok(batch["words"], is_split_into_words=True, truncation=True, max_length=128)
    labels = []
    for i, tags in enumerate(batch["tags"]):
        word_ids, prev, row = enc.word_ids(batch_index=i), None, []
        for w in word_ids:
            if w is None:            row.append(-100)                  # [CLS]/[SEP]/pad
            elif w != prev:          row.append(-100 if tags[w] == "-" else label2id[tags[w]])
            else:                    row.append(-100)                  # label only first sub-token
            prev = w
        labels.append(row)
    enc["labels"] = labels
    return enc

tokd = ds.map(align, batched=True, remove_columns=ds["train"].column_names)
seqeval = evaluate.load("seqeval")
def metrics(p):
    preds = p.predictions.argmax(-1)
    true_p = [[id2label[a] for a, l in zip(pr, la) if l != -100] for pr, la in zip(preds, p.label_ids)]
    true_l = [[id2label[l] for a, l in zip(pr, la) if l != -100] for pr, la in zip(preds, p.label_ids)]
    r = seqeval.compute(predictions=true_p, references=true_l, mode="strict", scheme="IOB2")
    return {"f1": r["overall_f1"], "precision": r["overall_precision"], "recall": r["overall_recall"]}

args = TrainingArguments("out", learning_rate=3e-5, num_train_epochs=5, per_device_train_batch_size=32,
                         warmup_ratio=0.1, weight_decay=0.01, eval_strategy="epoch", save_strategy="epoch",
                         load_best_model_at_end=True, metric_for_best_model="f1", seed=42)
Trainer(model=model, args=args, train_dataset=tokd["train"], eval_dataset=tokd["dev"],
        data_collator=DataCollatorForTokenClassification(tok), compute_metrics=metrics).train()
```

Notlar:
- **Hizalama:** Yalnızca ilk alt-token etiketlenir, diğerleri −100 alır. Çıkarımda da ilk alt-token'ın logit'i kullanılır.
- **Hiperparametre başlangıcı:**
  - WikiANN için en iyi ayar `bs8-e10-lr3e-05` (stefan-it).
  - deprem-ml `bs16, e5, lr5e-5, wd0.1, warmup0.1` kullanmış.
  - ELECTRA-small için daha yüksek lr gerekir: WikiANN'de en iyisi `lr5e-05`.
- **Damıtma (öğrenci = ELECTRA-small):**
  - Öğretmen etiketsiz gerçek havuz ve sentetik veri üzerinde soft-label (logit) üretir.
  - Öğrenci şu kayıpla eğitilir: `loss = α·CE(gold) + (1−α)·T²·KL(softmax(s/T) ‖ softmax(t/T))`, `T=2`, `α=0,5` ile başlanır.
  - Basit yol: `Trainer.compute_loss` override edilir.
- **Model kartı + metrikler:** seqeval strict span F1 (mikro + makro), sınıf bazında tablo, gürültü türüne göre kırılım. Sonuçlar `eval/results/ner-<model>-<date>.json` dosyasına yazılır (Faz 3d formatı).

### 3.2 ONNX dışa aktarma

```bash
pip install "optimum-onnx[onnxruntime]"
optimum-cli export onnx --model ./out/best --task token-classification --opset 17 ./onnx/
# produces onnx/model.onnx + tokenizer files (vocab.txt, tokenizer_config.json, config.json)
```

- Girişler `input_ids`, `attention_mask`, `token_type_ids` (BERT/ELECTRA için). Hepsi `int64` ve `[batch, seq]` dinamik eksenli. Çıkış `logits` `[batch, seq, num_labels]`.
- **[DOĞRULANMADI]** `optimum-onnx` 0.1.0 (2025-12) ile transformers 5.19 uyumu test edilmedi. Uyumsuzsa iki yol var:
  - (a) eğitimden sonra dışa aktarma için ayrı bir venv'de transformers 4.x sabitlenir. BERT/ELECTRA ağırlık formatı uyumlu.
  - (b) doğrudan `torch.onnx.export(model, (ids, mask, types), "model.onnx", input_names=..., dynamic_axes=..., opset_version=17)` kullanılır.
- Dışa aktarmadan önce `model.config._attn_implementation = "eager"` ayarlanır. Böylece ORT'nin BERT füzyonu (Attention, SkipLayerNorm, BiasGelu) devreye girebilir **[DOĞRULANMADI, bir HF model kartında tavsiye ediliyor]**.

### 3.3 int8 dinamik quantization ve parite

```python
from onnxruntime.quantization import quantize_dynamic, QuantType
from onnxruntime.quantization.shape_inference import quant_pre_process

quant_pre_process("onnx/model.onnx", "onnx/model.pre.onnx")            # shape inference + optimization here, not during quantize
quantize_dynamic("onnx/model.pre.onnx", "onnx/model.int8.onnx",
                 weight_type=QuantType.QInt8,
                 op_types_to_quantize=["MatMul", "Gather"],              # Gather => embeddings int8 (otherwise fp32 embeddings dominate size)
                 per_channel=True, reduce_range=True)                    # reduce_range: avoid U8S8 saturation on AVX2/AVX512 w/o VNNI
```

ORT dokümanının ilgili tavsiyeleri:
- Transformer modellerinde **dinamik** quantization önerilir.
- Optimizasyon quantization sırasında değil, ön-işlemede yapılmalı.
- VNNI olmayan AVX2/AVX512 işlemcilerde U8S8 satürasyon yapabilir. Çare `reduce_range` veya U8U8.
- Per-channel kullanılıyorsa AVX2/AVX512'de genelde `reduce_range` de gerekir.

**Parite kapısı (CI'da, model yayınından önce):**
1. **Python fp32 ↔ Python int8:** dev setinde seqeval F1 farkı ≤ 0,5 puan. Kelime düzeyi etiket uyuşması ≥ %99.
2. **Python int8 ↔ .NET int8:** 2.000 örnekte `input_ids` **birebir aynı** olmalı (tokenizer paritesi). Logit farkı max |Δ| ≤ 1e-3, argmax etiketleri %100 aynı.
3. Test girdileri Türkçe tuzakları içermeli: `İSTANBUL`, `ISPARTA`, `IĞDIR`, `i\u0307stanbul` (U+0307), `Kadıköy'de`, `No:17/5`, NBSP, tam genişlik rakamlar, emoji, 300+ karakter (truncation).
4. Çıktı: `model.int8.onnx`, `vocab.txt`, `labels.json`, `model-manifest.json`. Manifest'te `sha256`, `opset`, `labels`, `maxLength`, `lowercase`, `source` (HF commit), `metrics` bulunur.

### 3.4 .NET çıkarım

**Paketler (NuGet, 2026-10-08):**

| Paket | Sürüm | Yayın | Not |
|---|---|---|---|
| `Microsoft.ML.OnnxRuntime` | **1.30.0** | 2026-09-10 | nupkg **157 MB** (tüm platformlar). Yayımlanan uygulamaya yalnızca hedef RID'in native kütüphanesi kopyalanır: win-x64 16,5 MB · linux-x64 29,0 MB · linux-arm64 25,1 MB · osx-arm64 43,9 MB. |
| `Microsoft.ML.OnnxRuntime.Managed` | 1.30.0 | 2026-09-10 | 1 MB, yalnızca managed sarmalayıcı |
| `Microsoft.ML.Tokenizers` | **2.0.0** (kararlı) | 2025-11-11 | Bir de `3.0.0-preview.26457.2` var (2026-09). Kararlı olan seçilmeli. |

**`BertTokenizer` ve Türkçe: kaynak kod incelemesi** (`dotnet/machinelearning` main, `BertOptions.cs`, `BertNormalizer.cs`):

| Konu | Bulgu | Sonuç |
|---|---|---|
| `vocab.txt` yükleme | `BertTokenizer.Create(string vocabFilePath, BertOptions options)` ve `CreateAsync(Stream, …)` var. BERTurk ve ELECTRA-tr repolarında `vocab.txt` bulunuyor (32k: 251 KB, 128k: 1,18 MB). | Doğrudan yüklenir. |
| **`LowerCaseBeforeTokenization` varsayılanı `true`** | HF `tokenizer_config.json` dosyasında `do_lower_case: false` yazıyor, ama .NET bu dosyayı **okumaz**. | **Mutlaka `false` verilmeli.** Aksi halde cased model küçük harfli girdi görür ve doğruluk sessizce düşer. |
| Küçültme algoritması | `UppercaseLetter` kategorisindeki karakterlerde `ToLowerInvariant` çağrılıyor. .NET invariant casing **`I → i`** yapar (Türkçede `ı` olmalı) ve **`İ`'yi değiştirmez** (ARASTIRMA §5'te ölçüldü). | Uncased cosmos modelleri için bu bayrak **kullanılmaz**. Bunun yerine `TurkishText.ToLowerTr` ile önce biz küçültürüz, ardından bayrak `false` ile tokenize ederiz. Kural, model kartındaki `replace("I","ı").lower()` ile birebir aynıdır. |
| `RemoveNonSpacingMarks` | Varsayılan `false`. `true` olursa FormD'ye ayrıştırıp işaretleri siler: `ş→s`, `ç→c`, `ğ→g`, `ö→o`, `ü→u`. | **`false` kalmalı.** HF'de `strip_accents=None` ve `lowercase=False` olduğunda aksan silinmez. |
| Unicode normalizasyonu | Normalizer sonunda `Normalize(FormC)` çağrılıyor. Invariant modda normalizasyon çalışmayabilir (ARASTIRMA §5: FormD işlem yapmıyor). | Girdiyi tokenizer'a vermeden önce kendi `TurkishText.Normalize` (NFC + U+0307 temizliği) fonksiyonumuzdan geçiririz. Böylece ICU olsa da olmasa da aynı sonuç çıkar. |
| Kontrol karakterleri / `�` | Siliniyor. Boşluk kategorisindeki karakterler (NBSP dahil) `' '` yapılıyor. | HF `_clean_text` ile aynı davranış. Offset kaymasını önlemek için kendi normalizasyonumuzu önce uygularız. |
| Offset'ler | `EncodedToken.Offset` (`Range`) değeri **normalize edilmiş metne** göredir. | Offset'lere güvenmek yerine kelime kelime kodlama yaparız (aşağıda). Orijinal offset'ler zaten bizim tokenizer'ımızda var. |

**Kelime düzeyi kodlama** (`is_split_into_words` eşdeğeri). HF hızlı tokenizer'ı `is_split_into_words=True` ile her kelimeyi ayrı ön-tokenize eder. .NET'te de her kelimeyi ayrı kodlayıp `word_ids` dizisini kendimiz kurarız:

```csharp
public sealed class OnnxAddressTagger : IAddressTagger, IDisposable
{
    private readonly InferenceSession _session;
    private readonly BertTokenizer _tok;
    private readonly string[] _labels;           // from labels.json
    private readonly int _maxLen;                // 128
    private readonly bool _uncased;              // cosmos models only

    public OnnxAddressTagger(ModelFiles files, int intraOpThreads = 1)
    {
        var so = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = intraOpThreads,          // 1 for server throughput, 2-4 for single-request latency
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };
        _session = new InferenceSession(files.ModelPath, so);
        _tok = BertTokenizer.Create(files.VocabPath, new BertOptions
        {
            LowerCaseBeforeTokenization = false,   // ALWAYS false; Turkish lowering is done by TurkishText if needed
            RemoveNonSpacingMarks = false,         // keep ş/ç/ğ/ö/ü
            ApplyBasicTokenization = true,         // same punctuation split as HF BasicTokenizer
        });
        // _labels, _maxLen, _uncased from model-manifest.json
    }

    public IReadOnlyList<TaggedWord> Tag(IReadOnlyList<Token> words)   // words = AdresTR tokenizer output (with original offsets)
    {
        var ids = new List<long>(_maxLen) { _tok.ClassificationTokenId };
        var firstSubTokenOfWord = new int[words.Count];
        bool truncated = false;
        for (int w = 0; w < words.Count; w++)
        {
            string text = _uncased ? TurkishText.ToLowerTr(words[w].Text) : words[w].Text;
            var pieces = _tok.EncodeToIds(text, addSpecialTokens: false);
            if (truncated || ids.Count + pieces.Count >= _maxLen - 1) { truncated = true; firstSubTokenOfWord[w] = -1; continue; }
            firstSubTokenOfWord[w] = ids.Count;
            foreach (int id in pieces) ids.Add(id);
        }
        ids.Add(_tok.SeparatorTokenId);

        int n = ids.Count;
        var inputIds = new DenseTensor<long>(ids.ToArray(), [1, n]);
        var mask = new DenseTensor<long>(Enumerable.Repeat(1L, n).ToArray(), [1, n]);
        var types = new DenseTensor<long>(new long[n], [1, n]);
        using var results = _session.Run([
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", types)]);
        var logits = results[0].AsTensor<float>();                  // [1, n, L]

        var output = new TaggedWord[words.Count];
        for (int w = 0; w < words.Count; w++)
        {
            int t = firstSubTokenOfWord[w];
            if (t < 0) { output[w] = new(words[w], "O", 0f); continue; }
            (int best, float p) = Softmax.ArgMax(logits, t, _labels.Length);
            output[w] = new(words[w], _labels[best], p);
        }
        return BioRepair.Fix(output);   // I-X without preceding B-X/I-X => B-X (seqeval-compatible)
    }
}
```

Notlar:
- Kod taslaktır. ORT 1.30 C# API'sinde `OrtValue.CreateTensorValueFromMemory` + `RunOptions` yolu daha az bellek ayırır. Sıcak yolda o kullanılmalı.
- `EncodeToIds(string, bool addSpecialTokens, …)` overload'ı 2.0.0'da mevcut (kaynakta doğrulandı).
- Kelime içi alt-token'lar zaten etiketlenmediği için **BIO → span çözümü kelime düzeyinde** yapılır:
  1. Ardışık `B-X I-X…` kelimeler bir span olur.
  2. Span'in karakter aralığı = ilk kelimenin `Start` değeri ile son kelimenin `End` değeri arası (orijinal metin offset'i, bizim offset haritamızdan).
  3. Span güveni = kelime olasılıklarının minimumu (muhafazakâr) veya geometrik ortalaması.
- **Toplu işleme (batch):**
  - `/v1/parse/batch` ve CSV yolunda düşük güvenli adresler toplanır, 16–32'lik gruplar halinde işlenir.
  - Uzunluğa göre sıralanıp gruplanırsa (length bucketing) dolgu (padding) israfı azalır.
  - `attention_mask` dolguyu 0 yapar.
  - Sunucuda `IntraOpNumThreads=1` ile istek başına bir oturum çağrısı ve çekirdek sayısı kadar paralellik yüksek throughput verir. Tek istek gecikmesi için 2–4 thread daha iyidir **[TAHMİN, ölçülmeli]**.
- `InferenceSession` thread-safe. Tek bir örnek singleton olarak DI'a kaydedilir.

**Beklenen CPU gecikmesi [TAHMİN, ölçülmeli]:**

Elimizdeki tek somut kıyas noktası: DistilBERT (6 katman, 768) int8 statik quantization, 128 token, AWS c6i.xlarge (4 vCPU = 2 fiziksel çekirdek) üzerinde **p95 26,75 ms** (fp32 75,69 ms) (philschmid, Optimum). Buradan ölçekleme:

| Model | 128 token, batch 1, ~2 çekirdek | Tipik adres (~40 token) | Gerekçe |
|---|---|---|---|
| BERT-base int8 (12 katman) | ~45–60 ms | ~12–25 ms | DistilBERT'in yaklaşık 2 katı katman. Kısa dizide maliyet token sayısıyla neredeyse doğrusal. |
| DistilBERTurk int8 | ~25–30 ms | ~7–12 ms | Kıyas noktasının kendisi |
| ELECTRA-small int8 (12 × 256) | ~4–8 ms | ~1–4 ms | Katman başı FLOP oranı (256/768)² ≈ 1/9 |
| cosmos-small int8 (4 × 512) | ~4–7 ms | ~1–3 ms | (4·512²)/(12·768²) ≈ 0,15 |

- Modern bir masaüstü/sunucu çekirdeğinde (AVX-512 VNNI / AMX, 4–8 thread) bu değerlerin yarısı veya daha azı beklenir.
- Kural parser'ının hedefi < 1 ms. ML yalnızca düşük güvenli girdilerde çalışırsa (hedef ≤ %15), ortalamaya etkisi ELECTRA-small ile yaklaşık 0,2–0,6 ms olur **[TAHMİN]**.
- Ölçüm planı: `benchmarks/AdresTR.Benchmarks` altına `OnnxTaggerBenchmarks` eklenir. 40 ve 128 token, thread sayısı 1/2/4, int8 ve fp32 ölçülür. Sonuç CPU modeli ve ORT sürümüyle birlikte README'ye yazılır.

### 3.5 Paket boyutu ve talep üzerine model indirme

**Paketleme:**
- `AdresTR` (çekirdek) **sıfır bağımlılık** olarak kalır (PLAN §1.3).
- `AdresTR.Ml.Onnx` ayrı bir paket olur. `Microsoft.ML.OnnxRuntime` ve `Microsoft.ML.Tokenizers` paketlerine bağımlıdır. **Model dosyası pakete girmez.**
  - Kullanıcı tarafı maliyet: NuGet önbelleğine bir kerelik 157 MB indirme, yayımlanan uygulamaya RID başına 16–44 MB.
  - Platformlara ayrılmış resmi native ORT paketi bulunamadı **[DOĞRULANMADI]**.
- AOT/trim: ORT managed katmanı P/Invoke kullanır, AOT uyumluluğu **[DOĞRULANMADI]**. `AdresTR.Ml.Onnx` için `IsAotCompatible` iddiası, test edilene kadar konmaz.
- Blazor WASM playground'da ORT native çalışmaz. Tarayıcıda ML için `onnxruntime-web` + JS interop gerekir **[DOĞRULANMADI]**. v1'de playground ML'siz kalır.

**İndirme:**
- **URL deseni:** `https://huggingface.co/{repo}/resolve/{commitSha}/{path}`.
  - Revizyon olarak **commit SHA'sı** sabitlenir, `main` kullanılmaz. Örneğin `dbmdz/bert-base-turkish-cased` için bugünkü commit `b6e1de16…`.
  - Bizim yayımlayacağımız model reposu da aynı şekilde sabitlenir (`AlperCna/adrestr-ner-small`).
- **Bütünlük:** Beklenen SHA-256 değerleri pakete gömülü `model-manifest.json` dosyasında tutulur, yani güven kökü NuGet paket imzasıdır.
  - HF LFS dosyalarının sha256 değeri API'de de görünür: `GET /api/models/{repo}?blobs=true` → `siblings[].lfs.sha256`. Yine de çalışma zamanında karşılaştırma **manifest'e** göre yapılır, sunucunun söylediğine göre değil.
  - Uyuşmazlıkta dosya silinir ve exception atılır.
- **Önbellek dizini (öncelik sırasıyla):**
  1. `AdresTROptions.ModelCacheDirectory`
  2. `ADRESTR_MODEL_DIR`
  3. `$XDG_CACHE_HOME/adrestr/models`
  4. Windows'ta `%LOCALAPPDATA%\AdresTR\models`, diğerlerinde `~/.cache/adrestr/models`
  - Yapı: `{modelId}/{sha256-prefix}/model.int8.onnx`.
- **Atomiklik:** Dosya `*.partial` olarak indirilir. Hash doğrulandıktan sonra `File.Move(overwrite:false)` yapılır. Eşzamanlı süreçler için `FileShare.None` ile bir `.lock` dosyası kullanılır.
- **Çevrimdışı / kurumsal:**
  - `ADRESTR_OFFLINE=1` → ağ yok. Model yoksa açık hata mesajı verilir.
  - `ModelBaseUrl` ayarı ile ayna (mirror) desteklenir (iç Artifactory, GitHub Releases).
  - `AdresTR.Ml.Onnx.Models.Small` adında, modeli **içeren** ayrı bir NuGet paketi de yayımlanabilir. ~14 MB için sorun yok. Base model (~110 MB) için de nuget.org'un ~250 MB sınırı içinde kalır.
- Anonim HF indirmelerinde hız sınırı olabilir **[DOĞRULANMADI]**. Docker imajında model build aşamasında indirilir ve imaja gömülür. Çalışma zamanında indirme yapılmaz.

### 3.6 Parser'a entegrasyon

```
… 5. Anahtar sözcük → 6. Aday üret → 7. Çöz → 8. Skorla
                        ▲
                        └─ (yalnızca confidence < τ_ml veya çözülmemiş alan varsa)
                           IAddressTagger.Tag(words) → span önerileri
                           → aday üretimine ek kaynak ("ml_span") + skorlayıcıya özellik (ml_agreement)
```

- **ML hiçbir zaman nihai ID üretmez.** Yalnızca span sınırı ve tür önerir. ID'yi gazetteer çözer, hiyerarşi tutarlılığını beam search denetler (ADR-001 korunur).
- Kural ve ML bir alanda çelişirse:
  - Sayısal alanlarda (DIS_KAPI, KAT, DAIRE, POSTA_KODU) **kural kazanır** (deprem-ml'de bu alanlarda F1 ≈ 0,70).
  - Ad alanlarında beam search skoruna `ml_agreement` özelliği eklenir. Ağırlık dev setinde öğrenilir.
- Çıktıya `"modelVersion": "adrestr-ner-small@2026.12"` alanı ve `corrections[].kind = "ml_span"` kaydı eklenir. Açıklanabilirlik korunur.

---

## 4. LLM geri dönüşü (Microsoft.Extensions.AI)

### 4.1 Güncel API

Sürümler (NuGet, 2026-10-08): `Microsoft.Extensions.AI` 10.10.0, `Microsoft.Extensions.AI.Abstractions` 10.10.1, `Microsoft.Extensions.AI.OpenAI` 10.10.1, `Anthropic` 12.54.1, `OllamaSharp` 5.5.0.

`ChatClientStructuredOutputExtensions` (dotnet/extensions kaynağından doğrulandı):

```csharp
Task<ChatResponse<T>> GetResponseAsync<T>(this IChatClient chatClient,
    IEnumerable<ChatMessage> messages, JsonSerializerOptions serializerOptions,
    ChatOptions? options = null, bool? useJsonSchemaResponseFormat = null,   // default true
    CancellationToken cancellationToken = default);
// + overloads taking string / ChatMessage, with or without serializerOptions

// ChatResponse<T> : ChatResponse  →  T Result { get; }   bool TryGetResult(out T? result)
```

- `useJsonSchemaResponseFormat` varsayılan olarak `true`. Şema `ChatOptions.ResponseFormat` alanına `ChatResponseFormatJson` olarak konur. Model desteklemiyorsa hata verebilir.
- `false` verilirse şema prompt içinde talimat olarak gönderilir.
- **Her istekte değişen şema** (aday ID'lerinin enum olarak gömülmesi) için `GetResponseAsync<T>` uygun değildir, çünkü şema derleme zamanındaki `T` tipinden üretilir. Bunun yerine:
  1. `ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(JsonElement schema, schemaName, description)` ayarlanır.
  2. Düz `GetResponseAsync` çağrılır.
  3. `response.Text`, `System.Text.Json` ile `LlmParse` tipine deserialize edilir.
  - `ForJsonSchema(JsonElement…)` ve `ForJsonSchema<T>()` overload'ları mevcut (kaynakta doğrulandı).

**Sağlayıcıların JSON şema desteği:**

| Sağlayıcı / adaptör | Native JSON şema | `IChatClient` | Kaynak |
|---|---|---|---|
| OpenAI / Azure OpenAI (`Microsoft.Extensions.AI.OpenAI`) | Evet (`response_format: json_schema`, strict) | `AsIChatClient()` | MS Learn quickstart |
| **Anthropic** (resmî `Anthropic` C# SDK) | Evet (`output_config.format` = `json_schema`) | `new AnthropicClient().AsIChatClient("model")` | SDK kaynağı: `AnthropicClientExtensions.cs`, `ChatResponseFormatJson.Schema` değerini `OutputConfig.Format = JsonOutputFormat` alanına eşliyor (doğrulandı) |
| **Ollama** (`OllamaSharp`) | Evet (`format` = JSON şema) | `OllamaApiClient` doğrudan `IChatClient` | OllamaSharp `AbstractionMapper.cs`: `ChatResponseFormatJson.Schema` → `format` (doğrulandı) |
| Google Gemini | Evet (`responseSchema`) | Google GenAI .NET SDK'sının `IChatClient` adaptörü **[DOĞRULANMADI]** | — |

### 4.2 Prompt tasarımı: "çıkar, seç, uydurma"

İlkeler:
1. **LLM'e serbest metin üretmesi için alan bırakılmaz.**
   - Span'ler girdideki **birebir alt dizgeler** (`text`) olarak istenir.
   - Kanonik varlıklar yalnızca **gazetteer'ın önerdiği aday ID listesinden** seçilir: şemada `enum` + `null`.
2. Aday listesi bizim beam search çıktımızdır: il, ilçe ve mahalle için top-k (k ≤ 10) ve `ambiguous_names` alternatifleri. LLM bunların dışında bir ID döndüremez, çünkü şema buna izin vermez.
3. Talimat: *"Emin değilsen null döndür. Girdide geçmeyen bir şey yazma."*
4. Kişisel veri en aza indirilir: göndermeden önce kurallarla TELEFON (ve ML ile KISI) span'leri `<TEL>` / `<KISI>` ile maskelenir.

```csharp
public sealed record LlmSpan(string Label, string Text);          // Label ∈ BIO label set (enum in schema)
public sealed record LlmParse(
    IReadOnlyList<LlmSpan> Spans,
    int? IlId, int? IlceId, long? MahalleId,                       // each restricted to candidate enum + null
    string? Note);                                                  // optional short reason, never shown as data

// system prompt (Turkish, short, cache-friendly; volatile content goes in the user message):
// "Türkçe bir adres metnini bileşenlerine ayırıyorsun. Kurallar:
//  1) spans[].text girdide AYNEN geçen bir alt dizge olmalı; düzeltme, tamamlama yapma.
//  2) il/ilçe/mahalle ID'lerini YALNIZCA verilen aday listesinden seç; emin değilsen null.
//  3) Girdide olmayan bilgi üretme. Telefon/kişi adı zaten maskelenmiştir."
// user message: {"input": "...", "candidates": {"il":[{id,name}], "ilce":[...], "mahalle":[...]}}
```

**Doğrulama (LLM sonrası, deterministik):**
1. Her `span.Text` için girdide katlanmış (folded) arama yapılır. Bulunamazsa span **atılır** ve `warnings += "llm_span_not_in_input"` eklenir.
2. Seçilen ID'ler aday kümesinde mi ve hiyerarşi tutarlı mı (mahalle ⊂ ilçe ⊂ il)? Değilse sonuç reddedilir.
3. LLM'in seçimi, gazetteer skorlayıcısına güçlü bir özellik (`llm_choice`) olarak eklenir. Nihai karar yine beam search'te verilir.
4. Sonuç `corrections[].kind = "llm_assist"` ile işaretlenir. Model kimliği ve tarih çıktıya yazılır.
5. Önbellek: katlanmış girdi + aday kümesinin hash'i ile HybridCache (yalnızca bellek içi, TTL kısa; ADR-009 gereği ham adres kalıcı depolanmaz).

### 4.3 Maliyet tahmini (1.000 adres)

**[DOĞRULANMADI: fiyatlar]** Kaynak, Claude API skill'inin 2026-10-06 tarihli fiyat tablosu. Yayın öncesi resmî fiyat sayfasından teyit edilmeli.

Varsayımlar **[TAHMİN]**:
- Sistem promptu + şema yaklaşık 700 token, kullanıcı mesajı (adres + adaylar) yaklaşık 350 token → **giriş ≈ 1.050 token**.
- **Çıktı ≈ 150 token** JSON.
- Haiku 5.5'te düşünme (thinking) varsayılan olarak açık ve `effort` varsayılanı `medium`. Çıkarım işi için `effort: "low"` ayarlanır. Düşünme token'ları çıktı olarak faturalanır. Tahmini ek 0–300 token.

| Model | Giriş $/1M | Çıkış $/1M | 1k adres (düşünme yok) | 1k adres (+300 düşünme) | Batch API (%50) |
|---|---|---|---|---|---|
| Claude Haiku 5.5 (`claude-haiku-5-5`) | 0,10 | 0,50 | **≈ $0,18** | ≈ $0,33 | ≈ $0,09–0,17 |
| Claude Haiku 4.5 (`claude-haiku-4-5`) | 1,00 | 5,00 | ≈ $1,80 | — | ≈ $0,90 |
| Self-host (Ollama, Qwen3.5-4B) | — | — | Elektrik + donanım. Tek GPU'da yaklaşık 50–200 adres/dk **[TAHMİN]** | — | — |

- LLM yalnızca kural ve ML sonrası hâlâ düşük güvenli kalan **%2–5** girdide çalışırsa, 1M adreslik bir toplu iş Haiku 5.5 ile yaklaşık $4–17 tutar **[TAHMİN]**.
- Prompt caching'in minimum önek uzunluğu modele göre 512–4096 token arasında. 700 token'lık sistem promptu büyük olasılıkla **önbelleğe girmez** **[DOĞRULANMADI]**.

### 4.4 KVKK ve self-host

- **Adres kişisel veridir** (ARASTIRMA §3). API'yi işleten taraf veri sorumlusudur.
- **Yurt dışına aktarım, KVKK m.9:** 7499 sayılı Kanun ile değişti, 1 Haziran 2024'te yürürlüğe girdi.
  - Sıra: yeterlilik kararı → uygun güvenceler (standart sözleşme, BCR, taahhütname) → arızi haller.
  - Standart sözleşme, imzadan itibaren **5 iş günü içinde** KVKK'ya bildirilmeli. Bildirim 25.10.2024'ten beri online modül üzerinden yapılıyor.
  - Henüz yayımlanmış bir yeterlilik kararı yok. Bu yüzden ABD merkezli bir LLM sağlayıcısına adres göndermek **standart sözleşme + bildirim** gerektirir **[DOĞRULANMADI: güncel durum, hukuki görüş alınmalı]**.
- **Bizim varsayılanlarımız:**
  1. `AdresTR` çekirdeği ve `AdresTR.Ml.Onnx` **hiç ağ çağrısı yapmaz.** Tek istisna model indirmedir ve o da kişisel veri taşımaz.
  2. LLM yolu yalnızca `AdresTR.Api`'de bulunur, varsayılan **kapalıdır** (`Llm:Enabled=false`, ADR-009). İstemci istek başına `"allowLlm": true` göndermediği sürece çalışmaz.
  3. Barındırılan demo API'de LLM **hiç açılmaz.**
  4. README'de "LLM'i açarsanız veri sorumlusu olarak yurt dışı aktarım yükümlülükleri sizdedir" uyarısı yer alır.
- **Self-host seçeneği (önerilen):** `OllamaSharp` ile `IChatClient`. Ollama kütüphanesinde mevcut modeller (2026-10-08):

| Model | Lisans | Boyut | Not |
|---|---|---|---|
| `qwen3.5:4b` / `qwen3.5:9b` | Apache-2.0 | 4,7B / 9,7B | 2026-02. Çok dilli. Türkçe kalitesi kendi benchmark'ımızda ölçülmeli. |
| `gemma4:e4b` / `gemma4:12b` | Apache-2.0 | ~8B (E4B) / 12B | 2026-03/05. Gemma 4 Apache-2.0'a geçti (HF metadata). |
| `vngrs/Kumru-2B` | Apache-2.0 | 2,4B | Türkçe'ye özel. Ollama resmî kütüphanesinde yok, topluluk GGUF'ları var. Talimat takibi ve JSON şema uyumu **[DOĞRULANMADI]**. |
| `ytu-ce-cosmos/Turkish-Gemma-9b-v0.1` | Gemma lisansı (OSI değil) | 9,2B | Türkçe ince ayarlı. Lisans kısıtları var. |
| `llama3.2:3b` | Llama 3.2 Community | 3,2B | Türkçe resmî destekli 8 dil arasında değil **[DOĞRULANMADI]**. Önerilmez. |

- Faz 3'teki "LLM zero-shot baseline" deneyi bu modellerle de koşulur: Qwen3.5-4B, Gemma4-E4B, Haiku 5.5. Hepsi aynı şema ve aynı aday listesiyle çalışır, böylece "self-host yeterli mi?" sorusu sayıyla cevaplanır.

---

## 5. Adres eşleştirme / tekilleştirme

### 5.1 TEKNOFEST 2025 kazanan tarifleri

Hepsiburada görevi: yaklaşık 1M gürültülü adresi yaklaşık **10.390** opak adres etiketine (kümeye) atamak. Bu bir sınıflandırma veya eşleştirme problemi, ayrıştırma değil. Veri özel (ARASTIRMA §1).

| Takım | Sonuç | Tarif |
|---|---|---|
| **GeoMind** (`huseyinardaarslan/teknofest-hepsiburada-address-matching`) | README: Kaggle'da en yüksek skor, final sunumdan sonra **1.** | 1. Türkçeye özgü normalizasyon: küçültme, `mah/cad/sk` açılımı, sayı–kelime ayırma. 2. **BGE-M3** yoğun (dense) arama, top-30. 3. **BM25** (`bm25s`), top-30. 4. İki listenin birleşimi (union) aday havuzu olur. 5. **`dbmdz/bert-base-turkish-cased` cross-encoder** reranker. (query, aday) çifti girer, eşleşme olasılığı çıkar. 6. Hard negative mining: yoğun aramada yakın ama etiketi farklı 20 komşu. 7. Eğitim 2 epoch, %15 doğrulama, model seçimi F1 ile. Colab GPU'da çalışıyor. |
| **Atom** (`whofeeddrogon/TEKNOFEST-2025-…`) | README: Kaggle aşamasında **1.** (256 takım), finalde **2.** | 1. Normalizasyon + 50'den fazla kısaltma açılımı + kapı/kat/posta kodu özellik etiketleri. 2. `dbmdz/bert-base-turkish-cased`, ~10k sınıflı **doğrudan sınıflandırıcı** olarak 1M örnekle ince ayar. 3. Doğrulama katmanı: her etiket için en güvenli 10 adres **Google Maps API** ile geocode edilir. 4. **İki geçişli DBSCAN** uygulanır: önce 25 km makro kümeleme ile şehirler arası aykırılar atılır, sonra mikro kümeleme ile etiket merkezi bulunur. 5. Görselleştirme paneli. |

> Not: İki README de Kaggle aşaması için "en yüksek skor / 1." diyor. Leaderboard (public/private) ayrımı **[DOĞRULANMADI]**. İki reponun da lisansı yok (GitHub API'de `license: null`). Kod alıntılanmaz, yalnızca fikirler aktarılır.

**Bizim için çıkarımlar:**
- İki tarif de **kapalı küme etiketi** varsayıyor: train setinde her kümenin örnekleri var. Bizim kütüphane senaryomuzda kümeler önceden bilinmez (açık dünya tekilleştirme). Bu yüzden doğrudan sınıflandırma (Atom) uygun değil.
- GeoMind'ın **aday üret → yeniden sırala** iskeleti uygun. Ancak bizde aday üretimi (blocking) **kanonik ID'ler** sayesinde çok daha ucuz ve açıklanabilir. BGE-M3 (568M parametre, MIT) + GPU gerektirmiyor.
- İki takımın da harcadığı emeğin büyük kısmı **normalizasyon ve kısaltma açılımı**. Bu bizim çekirdeğimizde zaten var.

### 5.2 Önerilen tasarım: `AdresTR.Matching`

```csharp
public enum MatchLevel { Different, Unknown, SameMahalle, SameStreet, SameBuilding, SameUnit }

public sealed record MatchResult(MatchLevel Level, double Score, IReadOnlyList<MatchEvidence> Evidence);

public interface IAddressMatcher
{
    MatchResult Compare(ParseResult a, ParseResult b);
    IReadOnlyList<AddressCluster> Deduplicate(IEnumerable<ParseResult> items, MatchLevel level = MatchLevel.SameBuilding);
}
```

**Adım 1. Blocking (aday çift üretimi):**
- Birincil anahtar: `(ilId, ilceId, mahalleId)`. Kanonik kimlikler sayesinde yazım varyantları (`kadikoy` / `Kadıköy`) aynı bloğa düşer.
- Belirsizlik varsa **çoklu anahtar** kullanılır: kayıt, skoru ≥ τ_alt olan **top-2 mahalle adayının** her birinin bloğuna eklenir. Recall korunur.
- Mahalle eksikse geri dönüş anahtarı `(ilceId, foldedStreetKey)` olur. O da yoksa `(ilceId, postaKodu)`.
- Çok büyük bloklarda (örneğin binlerce kayıtlı bir mahalle) ikinci düzey sıralı komşuluk uygulanır: `foldedStreetKey + disKapi` ile sıralanır, pencere w = 20.

**Adım 2. Cadde/sokak karşılaştırması:**
- Tür normalize edilir (`cad/cd/caddesi` → Cadde) ve ad katlanır (`FoldKey`). Tür sözcükleri addan çıkarılır.
- **Numaralı sokak** (`1203/5 Sk.`, `864 sokak`, `127 nolu sokak`): sayı ve alt-numara **tam eşit** olmalı. Bulanık karşılaştırma yapılmaz.
- **Adlı sokak:**
  - Katlanmış ad üzerinde **Jaro–Winkler** hesaplanır. Başlangıç eşiği 0,92, benchmark üzerinde ayarlanır.
  - Çok sözcüklü adlarda sıralanmış token kümesi üzerinden de karşılaştırılır ("Gazi Mustafa Kemal" / "Mustafa Kemal Gazi").
  - Yaygın önekler (`Şehit`, `Dr.`, `Av.`, `Prof.`) isteğe bağlı token sayılır.
- Tür uyuşmazlığı (Cadde ↔ Sokak) eşleşmeyi bozmaz ama skoru düşürür. Aynı mahallede aynı adlı hem cadde hem sokak olabilir.
- Site adı, sokak yokken sokağın yerine geçebilir. Sitelerde sokak çoğu zaman yazılmaz.

**Adım 3. Kapı ve iç kapı:**
- Dış kapı normalize edilir: `17/A`, `17-A`, `17 A`, `No:17A` → `17A`.
- `No:17/5` belirsizdir. Parser bunu `disKapi=17, icKapi=5` olarak ayırır, eşleştirici parser'ın kararını kullanır.
- **Tam eşitlik gerekir.** 17 ile 19 farklı binalardır. Bir tarafta kapı eksikse en fazla `SameStreet` seviyesine çıkılır.
- `SameUnit` için daire tam eşit, blok (varsa) eşit, kat (varsa) eşit olmalı. Kat ve daire tutarsızsa (`K:3 D:5` ↔ `K:5 D:3`) uyarı eklenir.

**Adım 4. Skor ve seviye:**
- Fellegi–Sunter tarzı log-olabilirlik ağırlıkları kullanılır: alan başına `log(m/u)` (uyuşma) ve `log((1−m)/(1−u))` (uyuşmama).
- m ve u olasılıkları sentetik eşleştirme benchmark'ı üzerinde **sayılarak** tahmin edilir. Eğitim gerektirmez, açıklanabilir, her alanın katkısı `Evidence` listesinde döner.
- Seviye, kural tablosu ile skor eşiğinin birleşimidir. Örnek: `SameBuilding` için mahalle eşit + sokak eşit (JW ≥ τ veya numara eşit) + dış kapı eşit + skor ≥ τ_b.

**Adım 5. (İsteğe bağlı, tetikleyiciye bağlı) embedding / cross-encoder:**
- Yalnızca kural adımları `Unknown` döndürdüğünde kullanılır: sokak yok, tarif ağırlıklı adres, işyeri adı.
- Artık metin (eşleşmeyen token'lar) için `intfloat/multilingual-e5-small` (118M, MIT, ONNX int8 ile ~120 MB) ile kosinüs benzerliği hesaplanır.
- Bu yeterli olmazsa: sentetik çiftlerle eğitilmiş **ELECTRA-small cross-encoder**. GeoMind'ın BERTurk reranker fikri, küçük ölçekte uygulanır.
- `AdresTR.Ml.Onnx` paketinin altyapısı yeniden kullanılır.

**Tekilleştirme (kümeleme):**
- Blok içinde `≥ level` kenarlarından bir graf kurulur. Bağlı bileşenler union-find ile bulunur.
- Zincirleme hatalara karşı her kümenin **merkezi** (en çok kenarı olan kayıt) seçilir. Merkezle doğrudan `≥ level` ilişkisi olmayan üyeler ayrılır ("center clustering"). Basit ve deterministik bir yöntem.
- Karmaşıklık: Blok içi O(b²). b > 500 olan bloklarda Adım 1'deki sıralı komşuluk penceresi devreye girer.

### 5.3 Değerlendirme

| Katman | Metrik |
|---|---|
| Blocking | **Pair completeness**: gerçek eşleşen çiftlerin aday kümesine düşme oranı (blocking recall). **Reduction ratio**: 1 − aday çift sayısı / tüm çift sayısı. Bu ikisinin harmonik ortalaması da raporlanır. |
| Çift sınıflandırma | Seviye bazında **pairwise P / R / F1**. `SameBuilding` için "eşleşir" = seviye ≥ SameBuilding. |
| Kümeleme | **B³ (B-cubed) P / R / F1** (Bagga & Baldwin 1998): eleman başına küme kesişimi ortalaması. Büyük kümelerin baskınlığını dengeler. Ek olarak küme kaynaklı pairwise F1 ve isteğe bağlı ARI. |
| Sağlamlık | Gürültü türüne göre kırılım (Faz 3 ile aynı katmanlar), il bazında kırılım, bootstrap %95 güven aralığı. |
| Hız | Saniyede çift, 1M kayıtta toplam tekilleştirme süresi. |

### 5.4 Açık eşleştirme benchmark'ı (BenchGen'den)

**Üretim:**
1. **Varlıklar:** `(mahalleId, csbm{tür, ad}, disKapi, [blok], [kat, daire])` demetleri.
   - Mahalleler gerçek gazetteer'dan alınır.
   - **Sokak adları sentetiktir.** Gerçekçi bir dağılımdan üretilir: yaygın ad havuzu (Atatürk, Cumhuriyet, İnönü…), numaralı sokak desenleri (`1203/5`), şehit/kişi adları. Gerçek NVİ sokak tablosu kullanılmaz (ADR-004).
2. **Render:** Her varlık N = 3–8 farklı biçimde metne dökülür. Faz 3 şablonları + gürültü katmanları + eksik bileşen varyantları kullanılır.
   - Örnekler: `Caferağa Mah. Moda Cad. No:12 D:3 Kadıköy/İstanbul` ↔ `kadikoy caferaga mh moda cd 12/3` ↔ `Moda Caddesi no 12 daire 3 Caferağa İST`.
3. **Zor negatifler** (hard negatives), varlık başına:
   - komşu kapı (12 ↔ 14, 12 ↔ 12A)
   - aynı sokak adı komşu mahallede
   - aynı mahalle adı başka ilçede (`ambiguous_names`)
   - aynı bina farklı daire (`SameBuilding` doğru, `SameUnit` yanlış)
   - aynı ad farklı tür (Moda Cad. ↔ Moda Sok.)
4. **Etiketler:** Her çift için gerçek `MatchLevel`. Ayrıca `clusters.jsonl` dosyasında her render'ın `entityId`, `buildingId` ve `unitId` değerleri.
5. **Bölümler:** **Varlık düzeyinde** bölünür, aynı bina iki bölüme düşmez. Ayrıca bir **coğrafi hold-out** bölümü olur: eğitimde hiç görülmeyen 5 il.
6. **Oranlar:** Pozitif : zor negatif : rastgele negatif (aynı blok) ≈ 1 : 2 : 1. Ayrıca blocking'i ölçmek için blok dışı rastgele negatifler.
7. **Yayın:** HF `AlperCna/turkish-address-benchmark` reposunda `matching` konfigürasyonu (CC-BY-4.0). İçerik: `pairs.jsonl` (`a`, `b`, `level`, `noise_a[]`, `noise_b[]`, `hard_negative_kind`) ve `clusters.jsonl`. Sabit seed ve üretici sürümü veri kartına yazılır.
8. **Sınır:** Sentetik veri gerçek dünyadaki "aynı yer, farklı tarif" vakalarını (işyeri adı, "… karşısı") eksik temsil eder. Bu, veri kartında **bilinen yanlılık** olarak belirtilir. Gerçek değerlendirme için kurum adreslerinden elle 200–300 çift hazırlanır (örneğin aynı okulun MEB ve belediye sitesindeki adresleri).

---

## 6. Aşamalı ML yol haritası ve tetikleyiciler

Varsayım: haftada 15–20 saat (PLAN §4). Ön koşul: Faz 3 benchmark'ı ve Faz 4 parser'ı tamamlanmış, `docs/error-analysis.md` yazılmış olmalı.

**"Platoya ulaştı" tanımı:** Kural tarafında art arda **iki iyileştirme döngüsü** (her biri yaklaşık 1 hafta) dev setinde makro F1'i **0,5 puandan az** artırdıysa plato var demektir.

| Aşama | İş | Efor | **Tetikleyici (yalnızca şu durumda başla)** |
|---|---|---|---|
| **M0. Spike: risk azaltma** | .NET'te ORT 1.30 + `BertTokenizer` ile hazır bir Türkçe NER modeli (`akdeniz27/bert-base-turkish-cased-ner`) çalıştırılır. Tokenizer paritesi (2k örnek, Türkçe tuzaklarla) ve gecikme ölçümü (BenchmarkDotNet). | ~15 saat | **Koşulsuz** (ucuz, belirsizliği giderir). Faz 4 bitince yapılabilir. |
| **M1. Veri** | BenchGen'den BIO dışa aktarma, zayıf denetimli kurum adresleri, 1k gold. `eval/labels.json` tek kaynak. | ~2 hafta | Plato tanımı sağlanmış **ve** test setinde (gerçek alt küme) şunlardan en az biri: **mahalle Acc@1 < 0,85**, **csbm_ad span F1 < 0,85**, **site/blok span F1 < 0,80**, veya **gerçek girdilerin > %15'i düşük güven kovasında** (güven < τ) ve bu kovada Acc@1 < 0,60. |
| **M2. Eğitim + model seçimi** | Öğretmen (BERTurk 32k, isteğe bağlı 128k) ve öğrenci (ELECTRA-small, damıtma) eğitilir. Karşılaştırma için cosmos-small ve TabiBERT satırları eklenir. ONNX + int8 + parite. | ~1,5 hafta | M1 bitti. |
| **M3. `AdresTR.Ml.Onnx` paketi** | `IAddressTagger`, indirici/önbellek/SHA-256, resolver'a özellik entegrasyonu, ablation tablosu (kural / kural+ML). | ~1,5 hafta | M2'de öğrenci model, **düşük güven kovasında** kurala göre ≥ **+5 puan** Acc@1 (veya span F1) sağladı **ve** genel test setinde hiçbir alanda > 0,5 puan gerileme yok. Sağlamadıysa ML yayımlanmaz, sonuç blog yazısı/ADR olarak raporlanır. |
| **M4. LLM geri dönüşü** | API'de opt-in, aday enum'lu şema, doğrulama, Ollama ve Anthropic/OpenAI adaptörleri, maliyet ve KVKK dokümanı. | ~1 hafta | Kural + ML sonrası **düşük güven kalan > %5** **ve** LLM zero-shot baseline'ı (Faz 3d) bu kovada ≥ +10 puan kazandırıyor **ve** en az bir kullanıcı veya issue talebi var. |
| **M5a. Kural tabanlı eşleştirici** | `AdresTR.Matching`: blocking, JW, kapı kuralları, Fellegi–Sunter ağırlıkları, kümeleme, `/v1/match` ve `/v1/dedup` uç noktaları. | ~1,5 hafta | **ML'den bağımsız.** Faz 4 parser hedefleri tuttuğunda başlanabilir (il/ilçe/mahalle kimliklerine güveniyor). |
| **M5b. Eşleştirme benchmark'ı** | BenchGen `matching` modu, zor negatifler, HF yayını. | ~1 hafta | M5a ile paralel yürür. |
| **M5c. Embedding / cross-encoder** | e5-small ile artık metin benzerliği, gerekirse ELECTRA-small cross-encoder. | ~1–1,5 hafta | Benchmark'ta `SameBuilding` için **pairwise F1 < 0,90**, **veya** `Unknown` oranı > %10 **ve** hata analizinde baskın nedenin "sokak/kapı yok, tarif/işyeri adı var" olması. |
| **M6. Aktif öğrenme döngüsü** | Playground katkı akışı, haftalık etiketleme, yeniden eğitim betiği (GitHub Actions + manuel onay). | ~1 hafta + sürekli | M3 yayımlandı **ve** ayda 100'den fazla katkı geliyor. |

**Durdurma kuralı:** Herhangi bir aşamada tetikleyici sağlanmazsa o aşama **yapılmaz**. "Neden ML kullanmadık" sayısal gerekçesiyle ADR olarak kaydedilir. Bu da bir sonuçtur ve README'deki "kurallar yeterli" iddiasını güçlendirir.

**Toplam:** ML (M0–M3) yaklaşık 6 hafta, LLM yaklaşık 1 hafta, eşleştirme yaklaşık 3,5–4 hafta. Hepsi tetiklenirse 15–20 saat/hafta ile **yaklaşık 11–12 hafta** **[TAHMİN]**.

---

## 7. Açık sorular / doğrulanacaklar

1. `optimum-onnx` 0.1.0 ↔ transformers 5.19 uyumu (M0'da denenir).
2. `Microsoft.ML.Tokenizers` 2.0.0 `BertTokenizer` ile HF `BertTokenizerFast` arasında `input_ids` paritesi, özellikle Türkçe tuzaklarda (M0).
3. `quantize_dynamic` içinde `Gather` quantization'ının BERT embedding'inde doğruluğa etkisi (M2).
4. ORT 1.30 managed katmanının NativeAOT/trim uyumu.
5. ODbL verisiyle eğitilmiş model ağırlıklarının lisans durumu (hukuki).
6. ABD merkezli LLM sağlayıcısına adres göndermenin KVKK m.9 kapsamındaki güncel yükümlülükleri (hukuki).
7. Haiku 5.5 fiyatı ve prompt caching'in minimum önek uzunluğu (resmî fiyat sayfası).
8. Google GenAI .NET SDK'sının `IChatClient` + JSON şema desteği.

---

## Kaynaklar

**Modeller ve sonuçlar**
- [stefan-it/turkish-bert: BERTurk/ConvBERTurk/ELECTRA/DistilBERTurk, WikiANN NER sonuçları](https://github.com/stefan-it/turkish-bert)
- [dbmdz/bert-base-turkish-128k-cased](https://huggingface.co/dbmdz/bert-base-turkish-128k-cased) · [dbmdz/bert-base-turkish-cased](https://huggingface.co/dbmdz/bert-base-turkish-cased) · [dbmdz/distilbert-base-turkish-cased](https://huggingface.co/dbmdz/distilbert-base-turkish-cased) · [dbmdz/electra-small-turkish-cased-discriminator](https://huggingface.co/dbmdz/electra-small-turkish-cased-discriminator) · [dbmdz/convbert-base-turkish-cased](https://huggingface.co/dbmdz/convbert-base-turkish-cased)
- [ytu-ce-cosmos/turkish-tiny-bert-uncased](https://huggingface.co/ytu-ce-cosmos/turkish-tiny-bert-uncased) · [turkish-small-bert-uncased](https://huggingface.co/ytu-ce-cosmos/turkish-small-bert-uncased) · [Kesgin vd., arXiv 2307.14134 (mimari tablosu)](https://arxiv.org/html/2307.14134v1)
- [TabiBERT, arXiv 2512.23065](https://arxiv.org/abs/2512.23065) · [boun-tabilab/TabiBERT](https://huggingface.co/boun-tabilab/TabiBERT)
- [jhu-clsp/mmBERT-base](https://huggingface.co/jhu-clsp/mmBERT-base) · [jhu-clsp/mmBERT-small](https://huggingface.co/jhu-clsp/mmBERT-small)
- [VRLLab/TurkishBERTweet](https://huggingface.co/VRLLab/TurkishBERTweet) · [FacebookAI/xlm-roberta-base](https://huggingface.co/FacebookAI/xlm-roberta-base)
- [deprem-ml/adres_ner_v2_bert_128k (model kartı, sınıf bazında F1)](https://huggingface.co/deprem-ml/adres_ner_v2_bert_128k)
- [Ünal, Aygün, Gerek: Comparison of Pre-trained Language Models for Turkish Address Parsing, arXiv 2306.13947](https://arxiv.org/abs/2306.13947)
- Kıyas dosya boyutları: [Xenova/bert-base-cased](https://huggingface.co/Xenova/bert-base-cased) · [Xenova/distilbert-base-uncased](https://huggingface.co/Xenova/distilbert-base-uncased) · [akdeniz27/bert-base-turkish-cased-ner-quantized](https://huggingface.co/akdeniz27/bert-base-turkish-cased-ner-quantized)

**Pipeline ve .NET**
- [huggingface/optimum-onnx](https://github.com/huggingface/optimum-onnx)
- [ONNX Runtime quantization dokümanı](https://onnxruntime.ai/docs/performance/model-optimizations/quantization.html)
- [philschmid: Static Quantization with Optimum (DistilBERT, 128 token, c6i.xlarge gecikme)](https://www.philschmid.de/static-quantization-optimum)
- [HF blog: Scaling up BERT-like model inference on modern CPU](https://huggingface.co/blog/bert-cpu-scaling-part-1)
- [dotnet/machinelearning: BertOptions.cs](https://github.com/dotnet/machinelearning/blob/main/src/Microsoft.ML.Tokenizers/Model/BertOptions.cs) · [BertNormalizer.cs](https://github.com/dotnet/machinelearning/blob/main/src/Microsoft.ML.Tokenizers/Normalizer/BertNormalizer.cs) · [BertTokenizer.cs](https://github.com/dotnet/machinelearning/blob/main/src/Microsoft.ML.Tokenizers/Model/BertTokenizer.cs)
- NuGet: [Microsoft.ML.OnnxRuntime](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime) · [Microsoft.ML.Tokenizers](https://www.nuget.org/packages/Microsoft.ML.Tokenizers) · [Microsoft.Extensions.AI](https://www.nuget.org/packages/Microsoft.Extensions.AI) · [OllamaSharp](https://www.nuget.org/packages/OllamaSharp) · [Anthropic](https://www.nuget.org/packages/Anthropic)

**LLM**
- [MS Learn: Structured output quickstart (`GetResponseAsync<T>`)](https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/structured-output)
- [dotnet/extensions: ChatClientStructuredOutputExtensions.cs](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/ChatClientStructuredOutputExtensions.cs) · [ChatResponseFormat.cs](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI.Abstractions/ChatCompletion/ChatResponseFormat.cs)
- [anthropics/anthropic-sdk-csharp: AnthropicClientExtensions.cs (`AsIChatClient`, ResponseFormat → output_config.format)](https://github.com/anthropics/anthropic-sdk-csharp/blob/main/src/Anthropic/AnthropicClientExtensions.cs) · [ChatClientExample](https://github.com/anthropics/anthropic-sdk-csharp/tree/main/examples/ChatClientExample)
- [OllamaSharp AbstractionMapper.cs (ResponseFormat → format)](https://github.com/awaescher/OllamaSharp/blob/main/src/OllamaSharp/MicrosoftAi/AbstractionMapper.cs)
- Ollama kütüphanesi: [qwen3.5](https://ollama.com/library/qwen3.5) · [gemma4](https://ollama.com/library/gemma4) · HF: [Qwen/Qwen3.5-4B](https://huggingface.co/Qwen/Qwen3.5-4B) · [google/gemma-4-E4B-it](https://huggingface.co/google/gemma-4-E4B-it) · [vngrs/Kumru-2B](https://huggingface.co/vngrs/Kumru-2B) · [ytu-ce-cosmos/Turkish-Gemma-9b-v0.1](https://huggingface.co/ytu-ce-cosmos/Turkish-Gemma-9b-v0.1)
- Fiyatlar: Claude API skill fiyat tablosu (önbellek tarihi 2026-10-06) **[DOĞRULANMADI]**

**KVKK**
- [KVKK: Standart Sözleşme Bildirim Modülü duyurusu](https://www.kvkk.gov.tr/Icerik/8043/Standart-Sozlesme-Bildirim-Modulu-Hakkinda-Kamuoyu-Duyurusu)
- [KVKK: Standart sözleşmeler ve BCR dokümanları](https://www.kvkk.gov.tr/Icerik/7938/Standart-Sozlesmeler-ve-Baglayici-Sirket-Kurallarina-Iliskin-Dokumanlar-Hakkinda-Kamuoyu-Duyurusu)
- [Erdem & Erdem: Yurt dışına aktarımda son gelişmeler](https://www.erdem-erdem.av.tr/bilgi-bankasi/kisisel-verilerin-yurt-disina-aktarilmasinda-yasanan-son-gelismeler)

**Eşleştirme**
- [GeoMind: huseyinardaarslan/teknofest-hepsiburada-address-matching](https://github.com/huseyinardaarslan/teknofest-hepsiburada-address-matching)
- [Atom: whofeeddrogon/TEKNOFEST-2025-Hepsiburada-AI-Powered-Address-Parsing-Hackathon](https://github.com/whofeeddrogon/TEKNOFEST-2025-Hepsiburada-AI-Powered-Address-Parsing-Hackathon)
- [BAAI/bge-m3](https://huggingface.co/BAAI/bge-m3) · [intfloat/multilingual-e5-small](https://huggingface.co/intfloat/multilingual-e5-small)
- Literatür (genel bilinen, bu çalışmada yeniden okunmadı): Fellegi & Sunter (1969), *A Theory for Record Linkage*, JASA · Bagga & Baldwin (1998), *Algorithms for Scoring Coreference Chains* (B³) · Christen (2012), *Data Matching*, Springer (pair completeness / reduction ratio) · Winkler (1990), Jaro–Winkler
