# AdresTR — Ayrıştırıcı (Parser) Tasarım Raporu

> Durum: araştırma + tasarım önerisi (Faz 4 için girdi). Kodda hiçbir değişiklik yapılmadı.
> Tarih: 2026-10-08
> Not: Görevde istenen `docs/plan/PLAN.md` ve `ARASTIRMA.md` dosyaları depoda **yok** (yalnızca `docs/research/` var). Bu rapor görev tanımındaki MVP yaklaşımını temel alır: normalizasyon + ASCII katlama → tokenizer → pattern tagger → keyword sözlüğü → aday üretimi (token trie + kapsamlı fuzzy) → hiyerarşik beam (il ⊃ ilçe ⊃ mahalle) → skorlama → kalibre güven + düzeltme günlüğü.

Etiketler: **[DOĞRULANDI]** = kaynak kodu bu oturumda okundu / veriyle sayıldı. **[DOĞRULANMADI]** = genel bilgi, arama özeti veya varsayım; uygulamadan önce kontrol edilmeli.

---

## 0. Özet (TL;DR)

1. **En yakın örnek pelias/parser.** Mimarisi (Span grafı → sınıflandırıcılar → çözücü zinciri → skor = karakter-ağırlıklı güven × kapsama × (1 − ceza)) Türkçe'ye doğrudan taşınabilir. Ancak pelias'ın `ExclusiveCartesianSolver`'ı gazetteer varlık kimlikleri taşıyan hipotezlerde patlar. Biz onun yerine **soldan sağa, durum birleştirmeli (Viterbi benzeri) beam search** kullanacağız. Bu, Nominatim'in `_TokenSequence.advance/appendable` yaklaşımına benzer.
2. **İki katmanlı çözücü:** (a) *Yapısal katman*: span'lere rol atanır (mahalle, cadde, sokak, no, kat, daire, ilçe, il, posta kodu, landmark…). (b) *İdari çözüm katmanı*: seçilen idari span'ler için gazetteer'de hiyerarşik beam yapılır (il → ilçe → mahalle). Her seviyede bir **NULL adayı** bulunur (Mordecai3'teki "doğru cevap yok" satırı gibi). Pelias Placeholder'daki sağdan sola alt/üst kesişim fikri de bu katmanda kullanılır.
3. **Skor log-lineer (ağırlıklı özellik toplamı).** Özellik listesi ve başlangıç ağırlıkları §5'te. Ağırlıklar elle başlatılır, sonra etiketli veriyle (koordinat-yükselişi veya libpostal tarzı averaged perceptron) öğrenilir.
4. **Güven:** N-best listesinden softmax (sıcaklık ölçekli) ile alan bazında marjinal olasılık hesaplanır. Ardından alan bazında **izotonik regresyon (PAV)** veya küçük veri için **Platt** uygulanır. İkisi de bağımlılıksız C#'ta yaklaşık 60 satır. Pelias'ın güveni sezgisel ve kalibre değil. Nominatim güven döndürmüyor, yalnızca ceza ve importance var.
5. **Fuzzy:** katlanmış (ASCII) metin üzerinde **OSA** kullanılır. Eşik uzunluğa göre belirlenir: ≤3 karakter → 0, 4–5 → 1, 6–10 → 1 (kapsam içindeyse 2), ≥11 → 2. Global aramada SymSpell kullanılır (C# kaynak varsayılanları: maxEditDistance=2, prefixLength=7, DamerauOSA). Kapsamlı aramada brute-force OSA yeterli (bir ilçede en fazla 218 mahalle/köy). **Klavye-komşuluğu ağırlıklı mesafe şimdilik gereksiz.** Türkçe yazım hatalarının en büyük kısmı diakritik hatası ve bunlar katlamayla zaten sıfırlanıyor. Ağırlıklı mesafe sonra yalnızca eşitlik bozucu olarak denenebilir.
6. **Veri gerçekleri (bizim gazetteer):** 973 ilçe, 73.398 mahalle-seviyesi kayıt. Bunun içinde yalnızca 32.471'i gerçek "mahalle". Geri kalanı köy (12.294), mevkii (22.290), mezra (5.093) ve yayla/küme evleri (1.250). 51 ilde "Merkez" ilçesi var. Ad belirsizliği ciddi: "Cumhuriyet" adı 402 mahallede geçiyor. 3.892 mahalle bir ilçeyle aynı adı taşıyor, 51'i kendi ilçesiyle aynı adda. Ad uzunluğu ≤7 token, %99,9'u ≤3 token. Bu yüzden **MaxSpan = 7** yeterli (§2).
7. Rapor sonunda 58 snapshot test vakası var (§10). Sözde kod §9'da.

---

## 1. Önceki çalışmaların incelemesi ve devşirilecek fikirler

### 1.1 pelias/parser (JS) — en yakın örnek [DOĞRULANDI]

Depo: <https://github.com/pelias/parser>

**Tokenizasyon** — `tokenization/Tokenizer.js`, `Span.js`, `split.js`, `split_funcs.js`, `permutate.js`
- Girdi metni bir kök `Span` olur. `segment()` metni **bölümlere (section)** ayırır. Ayraçlar `\n`, `\t`, `,` ve tırnak karakterleridir (`fieldsFuncBoundary`).
- Her bölüm önce boşluğa göre bölünür, **sonra ayrıca** `-` ve `/` dahil olacak şekilde bir kez daha bölünür (`fieldsFuncHyphenOrWhiteSpace`). İki parçalama da aynı bölümün `child` kenarlarına eklenir. Örneğin `1203/5` için hem `1203/5` hem de `1203` ve `5` token'ları bulunur. **Çok-çözünürlüklü token** fikri bizim için çok önemli (`1203/5 Sk.` ile `No:17/5` ayrımı).
- Her Span `start/end` karakter ofsetini taşır. `norm` küçük harfe çevrilmiş halidir. `contains.numerals` ve `contains.final.period` gibi önceden hesaplanmış bayraklar var. `MAX_SPAN_LENGTH = 140` karakter.
- `permute(0, 10)`: bölüm içinde ardışık token'lardan 1–10 uzunluğunda tüm "phrase" span'leri üretilir (`permutate.js`). Bölüm sınırını (virgül) aşan phrase üretilmez.
- `computeCoverage()`: ayraçlar hariç toplam karakter sayısı. Skorun paydasıdır.

**Sınıflandırma** — `classifier/*`, `classification/*`
- Taban sınıflar: `WordClassifier` (tek token'lar), `PhraseClassifier` (tüm permütasyonlar), `SectionClassifier` (bölüm bütünü).
- `Classification`: `{label, confidence, public, meta}`. Yalnızca `public` sınıflandırmalar çözüme girer (ör. `HouseNumberClassification.public = true`, `AreaClassification` ise private).
- Span bir sınıfı ancak daha yüksek güvenle tekrar alabilir (`Span.classify`). Yani bir span aynı anda **birden çok hipotez** taşır.
- `HouseNumberClassifier`: kalıplar `^\d{1,5}[a-z]?$`, `10-19a`, `1/135` (`/` içeren). 5 haneli sayıya 0.2, 4 haneliye 0.9 güven verir. Önceki token birim tipiyse (apt, unit) sınıflandırmaz.
- `PostcodeClassifier`: chromium-i18n kalıpları. Bölümde başka token varsa **başlangıç konumunda posta kodu kabul edilmez**.
- `StreetSuffixClassifier`: libpostal `street_types.txt`. Bölümün ilk token'ı sokak eki olamaz. Sonu noktalı kısaltmalar (`str.`) da kabul edilir.
- `WhosOnFirstClassifier`: gazetteer'den yüklenen `Set`'lerde tam (normalize) eşleşme yapar. Bir kara liste var (`north`, `street`, `city`…). Önceki token stopword ise güven yarıya iner. Sonraki token sokak ekiyse sınıflandırma yapılmaz ("X Street" içindeki X yer adı değildir).
- `CompositeClassifier` + `classifier/scheme/street.js`: **şema tabanlı bileşik sınıflandırma**. Örnek: `[Alpha][StreetSuffix]` → `StreetClassification(0.82)`, `[Numeric][StreetSuffix]` → 0.86, `[StreetPrefix][Alpha]` → 0.88. Komşuluk zorunludur (`child:last.next === next.child:first`). Bu, Türkçe "`<ad> Sokak`", "`<sayı> Sokak`", "`<ad> Mahallesi`" kalıpları için doğrudan kullanılabilecek bir model.
- `UnitClassifier`/`UnitTypeClassifier`: birim tipi (apt/daire) + birim değeri. `OrphanedUnitTypeDeclassifier` değersiz birim tipini düşürür.

**Çözücüler (solver zinciri)** — `parser/AddressParser.js` sırası:
1. `ExclusiveCartesianSolver` (`solver/ExclusiveCartesianSolver.js`): her `label` için en fazla `MAX_PAIRS_PER_LABEL = 8` aday (`HashMapSolver`) ve bir "boş" seçenek alır, bunların kartezyen çarpımını üretir. Sınırlar: `MAX_RECURSION = 10`, `MAX_SOLUTIONS = 50000`. Çakışan span içeren çözümler sonradan filtrelenir. **Sorun:** üretim sırasına bağlı kesme var, budama yok, çakışma kontrolü sonradan yapılıyor.
2. `LeadingAreaDeclassifier`: idari (locality/region/country) etiketleri, idari olmayan son etiketten **önce** geliyorsa çözümden atılır. Dosyadaki yorum: "Batı yarımkürede iyi çalışır". Türkçe'de "İstanbul Kadıköy Caferağa Mah." gibi ters sıralı girdiler yaygın, bu yüzden **katı filtre değil ceza olmalı**.
3. `MultiStreetSolver`: kavşak için iki sokak etiketine izin verir. Türkçe'de **cadde + sokak** birlikteliği ("Bağdat Cad. Kazım Özalp Sok.") için benzer bir istisna gerekir.
4. `SubsetFilter`: başka bir çözümün alt kümesi olan çözümleri siler.
5. `InvalidSolutionFilter`: tam eşleşen anlamsız kombinasyonları (ör. yalnız `housenumber+locality`) siler.
6. `MustNotFollowFilter`/`MustNotPreceedFilter`: ikili sıra kısıtları. Ör. posta kodu kapı numarasından önce gelemez, ülke bölgeden önce gelemez. Kural ihlal edilirse ilgili etiket çözümden atılır.
7. `HouseNumberPositionPenalty`: `tr` "numara sonda" dilleri listesinde. Kapı no sokaktan önceyse ceza `0.05`.
8. `PostcodePositionPenalty`: kapı no yokken posta kodu + tek sokak varsa ceza `0.1`.
9. `TokenDistanceFilter`: kapı no ile sokak arası **karakter** mesafesi > 2 ise çözüm silinir. Sokaksız kapı no da düşürülür.

**Skor** — `solver/Solution.js#computeScore`:
```
score = (Σ conf_i · chars_i / Σ chars_i) · (Σ chars_i / tokenizer.coverage) · (1 − penalty)
      = Σ conf_i · chars_i / coverage · (1 − penalty)
```
Yani karakter-ağırlıklı ortalama güven ile kapsama oranının çarpımı. `Parser.comparitor` eşitlikte locality > region > country tercihi yapar. Sonuçlar `max_solutions = 10` ile kesilir. HTTP çıktısında (`server/routes/parse.js`) her çözüm `score` ve `{label, value, start, end}` listesi döner. Sınıflandırma güveni dışarı verilmiyor (yorum satırı).

**Bizim için çıkarımlar**
- Span + karakter ofseti + çok-çözünürlüklü token + permütasyon penceresi aynen alınabilir.
- Şema tabanlı bileşik sınıflandırma (ad + tip anahtar kelimesi) Türkçe'nin **son-ek tipli** yapısına çok uygun.
- Skor fikri (kapsanan karakter × güven) iyi bir **ana özellik**. Ancak tek başına yetmez: hiyerarşi tutarlılığı, posta kodu uyumu ve belirsizlik özellikleri eklenmeli.
- Kartezyen çözücü yerine **budamalı beam** kullanılmalı. Katı filtreler (LeadingArea, MustNot…) Türkçe'de **yumuşak cezalara** çevrilmeli.

### 1.2 pelias/placeholder — hiyerarşik idari çözüm [DOĞRULANDI]

Depo: <https://github.com/pelias/placeholder>
- `lib/permutations.js`: `GROUP_MIN = 1`, `GROUP_MAX = 6`. Ardışık token grupları **uzun olandan kısaya** üretilir.
- `prototype/tokenize.js#_groups`: indekste bulunan phrase'ler arasından soldan sağa **açgözlü en uzun eşleşme** seçilir.
- `prototype/query.js#reduce` + `lib/Result.js#intersect`: gruplar **sağdan sola** işlenir. En sağdaki "object" (ör. ülke/il) ile solundaki "subject" (ör. şehir) arasında `matchSubjectObject` ile "subject, object'in torunu mu?" sorgusu yapılır. Geçerli çocuk bulunursa kimlik kümesi daraltılır ve bir sola kayılır. Bulunamazsa subject atlanır. Hiç eşleşme yoksa en sağdaki token'ın kimlikleri döner.
- **Çıkarım:** Türkçe'de idari kuyruk sonda ("… Kadıköy İstanbul") olduğundan sağdan sola kesişim doğal bir uyum. İdari çözüm katmanımız bu mantığın **skorlanan** bir versiyonu olacak. Placeholder ikili (var/yok) çalışıyor, biz ise beam + skor kullanacağız.

### 1.3 pelias/api — güvenin dışarıya sunulması [DOĞRULANDI]

- `middleware/confidenceScore.js`: Elasticsearch skorlarının ortalama ve standart sapmasından `checkDistanceFromMean` ve `computeZScore` hesaplanır. Bunlara `checkName`, `checkQueryType` (kapı no istendi ama sonuçta yok → 0) ve `checkAddress` (`propMatch`: eşit 1, farklı 0.7, eksik 0/0.5) eklenip ortalaması alınır. **Deal-breaker** (eyalet/posta kodu çelişkisi) durumunda güven doğrudan 0.5 olur.
- `middleware/confidenceScoreFallback.js`: fallback olduysa katmana göre sabit çarpan uygulanır (address 0.8, locality 0.6, county 0.4, region 0.3, country 0.1) ve `match_type` = `exact|fallback` set edilir.
- **Çıkarım:** pelias güveni **kalibre değil, sezgisel**. Biz de `match_type` benzeri bir alan sunacağız (`Exact | Normalized | Fuzzy | Alias | Inferred`). Sayısal güveni ise etiketli veriyle kalibre edeceğiz. "Deal-breaker" fikri bizde posta kodu–il çelişkisi olarak karşılık bulur.

### 1.4 Nominatim (Python frontend) — token ataması ve cezalar [DOĞRULANDI]

Kaynak: <https://github.com/osm-search/Nominatim/tree/master/src/nominatim_api/search>
- `query.py`: sorgu bir **düğüm grafıdır**. Düğümler kelime aralarıdır ve her düğümün bir kesme tipi var: `BREAK_START '<'`, `BREAK_END '>'`, `BREAK_PHRASE ','`, `BREAK_SOFT_PHRASE ':'`, `BREAK_WORD ' '`, `BREAK_PART '-'`, `BREAK_TOKEN '`'`. Kesme cezaları `PENALTY_BREAK` tablosunda: start/end/phrase/soft = −0.5, word = 0.1, part = 0.2, token = 0.4. `word_break_penalty = max(0, p)`, `word_continuation_penalty = max(0, −p)`. Yani virgülde bir kelimeyi bitirmek **ödüllendirilir**, virgülü aşan çok-kelimeli ad cezalandırılır. Token'lar düğümler arası kenarlardır (DAG). `num_token_slots() > 50` ise sorgu reddedilir (`icu_tokenizer.py`).
- `Token.match_penalty`: sorgu biçimi ile sözlük biçimi arasında difflib opcode'larına dayalı normalize mesafe.
- `icu_tokenizer.py#from_db_row`: tip bazlı taban cezalar (kısmi kelime 0.3, tek harfli tam kelime 0.2/0.3, harf içeren kapı no her harf için 0.1). `add_extra_tokens`: ≤4 haneli sayı her zaman 0.2 cezalı bir kapı no adayı olur. `rerank_tokens`: aynı kenarda posta kodu adayı varsa diğer tiplere +0.39 ceza, ≤3 karakterli kapı no adayı varsa diğerlerine (0.5 − p) ceza. Ayrıca `match_penalty` (tam ad/kısmi için ×1, diğer tipler için ×2) eklenir.
- `query.py#compute_direction_penalty`: her düğümdeki kısmi token'ın *ad / adres* sayım oranından konuma göre doğrusal regresyon eğimi hesaplanır. Bu sorgunun soldan sağa mı sağdan sola mı okunduğunu tahmin eder. **Bize uygun fikir:** Türkçe'de idari blok başta mı sonda mı, yön bayrağıyla modellenebilir.
- `token_assignment.py`: `_TokenSequence` bir durum (`seq`, `direction`, `penalty`) taşır. `appendable(ttype)` gramer kurallarını uygular: kapı no/posta kodu/ülke en fazla bir kez, yön kilitleme gibi. `advance` aynı tipte uzatmada 0, yeni aralık açarken `break_penalty` ekler. `recheck_sequence`: kapı no öncesinde 2 isim aralığı varsa +0.8 ceza, daha fazlası red. `get_assignments` ilk/son aralığı "name" yapan varyantları üretir. Virgüllü sorguda bölünmüş varyantlara +0.25, kapı no ad sonrasındaysa +0.4 ceza eklenir. `yield_token_assignments` yığın tabanlı (DFS) **tam numaralandırma** yapar, budama yalnızca gramer kurallarıyla.
- `geocoder.py#execute_searches`: aramalar cezaya göre sıralanır. `min_ranking = ilk ceza + 1.5`'i aşan ve 15'ten sonraki aramalar atlanır. `rerank_by_query` sonuca göre sorgu kelimesi mesafesi ekler. `results.py#ranking = accuracy − importance`. Dışarıya **güven verilmez**, `importance` ve `place_rank` döner [DOĞRULANMADI: JSON çıktı alanları kaynak kodundan değil genel bilgiden].
- **Çıkarım:** (1) düğüm/kesme tipli graf ve kesme cezaları, (2) gramer durum makinesi (`appendable`), (3) yön cezası, (4) sayının bağlama göre kapı no / posta kodu tercihi, (5) en iyi cezaya göre erken kesme (`best + 1.5`).

### 1.5 libpostal — CRF, sözlükler, normalizasyon ve near-dupe [DOĞRULANDI]

Kaynak: <https://github.com/openvenues/libpostal>
- `src/address_parser.c#address_parser_features`: CRF/averaged perceptron özellikleri şunlar:
  - Adres sözlüğü phrase üyeliği. "unambiguous phrase type" (tek bileşen tipine ait) ile "phrase type+phrase" (çok tipli) ayrımı yapılır.
  - Gazetteer bileşen phrase'leri ("phrase", "commonly city", "commonly suburb"…).
  - **Posta kodu bağlamı**: `postal_code_context_exists(postal_code_id, admin_id)` ile solda/sağdaki idari phrase'in posta koduyla uyumlu olup olmadığı → "postcode have context" / "postcode no context".
  - Kelime frekansı, bilinmeyen kelime ve sayı, ön/son ek n-gram, `prev word`, `next word`, `first word`.
  - Sağ bağlamdaki sokak/mekan phrase'i ile "first word unknown+street phrase right" gibi birleşik özellikler.
- `src/libpostal.h`: normalizasyon seçenekleri bir kontrol listesi olarak işe yarar. String düzeyinde `LATIN_ASCII`, `TRANSLITERATE`, `STRIP_ACCENTS`, `DECOMPOSE`, `LOWERCASE`, `TRIM`, `REPLACE_HYPHENS`, `COMPOSE`, `REPLACE_NUMEX`. Token düzeyinde `DELETE_HYPHENS`, `DELETE_FINAL_PERIOD`, `DELETE_ACRONYM_PERIODS`, `DELETE_OTHER_APOSTROPHE`, `SPLIT_ALPHA_FROM_NUMERIC`, `REPLACE_DIGITS`, `REPLACE_NUMERIC_TOKEN_LETTERS`, `REPLACE_NUMERIC_HYPHENS`. Parser API (`libpostal_address_parser_response_t`) yalnızca `components`/`labels` döndürür, **skor/güven yok**.
- `src/expand.c`: sözlük kategorisine göre "yok sayılabilir" bileşenler (`gazetteer_ignorable_components`), "kök olabilir" bileşenler (`gazetteer_possible_root_components`) ve `expand_address_root`. Örneğin sokak tipi sözcüğü ad karşılaştırmasında atılabilir ("Atatürk Cad." ≈ "Atatürk"). Kök formu fikri karşılaştırmada kullanılacak.
- `src/near_dupe.c`: ad, adres, birim, şehir/kapsayan sınır, posta kodu ve geohash kombinasyonlarından anahtar (hash) üretir. Double metaphone ve quadgram ile çağrışım anahtarları da var. **Bizde:** gazetteer'de aynı yerin farklı yazımlarını (ör. `100.YIL`/`100 YIL`, `GAZİOSMANPAŞAOSB`/`GAZİOSMANPAŞA OSB`) aynı anahtara indirmek için "boşluksuz anahtar + boşluklu anahtar" çift indeksi.
- `resources/dictionaries/tr/*` (Türkçe sözlükler, okundu): `street_types.txt` (bulvar|bl|blv, cadde|cd|cad, sokak|sk|sok, sokağı|sokagi, yolu, çevreyolu, köprüsü), `unit_types_numbered.txt` (apartman|apt|ap, daire|d, oda, ofis), `level_types_numbered.txt` (kat|k), `level_types_standalone.txt` (zemin kat|zk|z.k), `level_types_basement.txt` (bodrum), `level_types_mezzanine.txt` (asma kat), `number.txt` (numara|no|no:, numarala|nolu), `qualifiers.txt` (il, ilçe, köyü, mahalle|mah, mahallesi|mah), `post_office.txt` (posta kutusu|pk), `cross_streets.txt` (arasında, köşe, köşesinde, ve), `entrances.txt` (giriş), `unit_directions.txt` (sağ, sol), `personal_titles.txt` (bey, hanım, mimar, sultan), `place_names.txt` (cami, köprüsü). **Önemli:** `directionals.txt` = `batı|b`, `doğu|d`, `güney|g`, `kuzey|k` ve `ambiguous_expansions.txt` = `& b d g k`. Yani libpostal'da **D ve K yön kısaltması olarak da kayıtlı**. Bu, görevdeki "D/K yön belirsizliği"nin kaynağı. Bizim sözlüğümüzde tek harfli yön kısaltmaları **olmamalı** (§6.7).
- **Çıkarım:** sözlük formatı (`kanonik|varyant|varyant`) aynen benimsenebilir ve tr sözlükleri genişletilerek başlangıç sözlüğümüz olur (lisans MIT [DOĞRULANMADI: libpostal LICENSE dosyası okunmadı]). Özellik fikirleri (posta kodu–idari bağlam, belirsiz/belirsiz-olmayan phrase, sağ-bağlam) skor özelliklerimize dönüştürüldü (§5).

### 1.6 Photon — OpenSearch + yeniden sıralama [DOĞRULANDI]

Kaynak: <https://github.com/komoot/photon>
- `opensearch/SearchQueryBuilder.java`: ayrıştırma yok, tam metin sorgu yapılır. Kısa sorguda `fuzziness(qlen < 4 ? "0" : "AUTO")` ve `prefixLength(qlen <= 6 ? 1 : 2)` kullanılır. Kapı no alt sorgusu `.parent` alanı ile eşleşme gerektirir.
- `searcher/QueryReranker.java`: sonuç adıyla sorgu karşılaştırılır. Tam eşitlik 1.0, önek + boşluk 0.9, önek 0.8. Aksi halde sorgu metninden eşleşen terimler silinir ve **karakter uzunluğu ağırlıklı** puan toplanır (tam terim 0.8·len, önek 0.4·len, ülke 0.85), sonuç `0.8·matches/len(query)` olur.
- **Çıkarım:** (1) fuzzy için **ilk 1–2 karakterin sabit tutulması** (prefixLength) hem doğruluk hem hız sağlar. (2) Kalan (eşleşmemiş) karakter oranı iyi bir güven özelliği. Pelias'ın coverage'ı ile aynı fikir.

### 1.7 CLAVIN / CLIFF — bağlamsal birlikte-seçim [DOĞRULANDI (CLAVIN), kısmen (CLIFF)]

- CLAVIN `ClavinLocationResolver.java` (fork: <https://github.com/bigconnect/clavin>, `src/main/java/com/bericotech/clavin/resolver/ClavinLocationResolver.java`): her mention için `maxHitDepth = 5` aday alınır, `maxContextWindow = 5` mention'luk parçalarda işlem yapılır. `pickBestCandidates` derinlik 3'ten başlayarak tüm kombinasyonları dener. Skor `(#mention / (#farklı ülke + #farklı admin1)) / derinlik`. Skor arttığı sürece derinlik artırılır. Yani **ortak ebeveyni paylaşan aday kombinasyonu** tercih ediliyor. Bu, bizim "hiyerarşi tutarlılığı" özelliğimizin genelleştirilmiş hali.
- CLIFF (`mediacloud/cliff-annotator`, `places/disambiguation/`): `ExactAdmin1MatchPass`, `ExactColocationsPass`, `TopColocationsPass`, `LargeAreasPass`, `TopAdminPopulatedPass` gibi **sıralı geçişler** var [Dosya adları okundu, içerikleri okunmadı — DOĞRULANMADI]. Fikir şu: önce tam ve birlikte-yerleşik (co-located) eşleşmeler kabul edilir, sonra nüfus önceliği uygulanır.

### 1.8 Mordecai3 — aday sıralama özellikleri [DOĞRULANDI]

Kaynak: <https://github.com/ahalterman/mordecai3/blob/main/mordecai3/candidate_features.py>
- Özellik grupları:
  - *prom*: `log_population`, `is_max_pop`, `log_pop_rel`.
  - *name*: `exact_name_match`, `exact_altname_match`.
  - *cue*: `mention_admin_cue` ("county", "province"…), `is_admin_class`.
  - *sib*: aynı belgede adayın ebeveyni geçiyor mu (`sib_adm1`, `sib_adm2`).
  - *shape*: `log_n_same_name`, `is_unique_exact_match`.
  - *cf*: küçük harfli edit mesafeleri.
- Her aday listesine nötr değerli bir **"NULL / doğru cevap yok" satırı** eklenir ve softmax'ta diğerleriyle yarışır.
- **Çıkarım:** (1) `mention_admin_cue` = bizim "Mah./İlçesi/Köyü" anahtar kelime desteği. (2) `log_n_same_name` = ad belirsizliği cezası. (3) `sib_*` = hiyerarşi tutarlılığı. (4) **NULL aday**: her idari seviyede "bu seviye girdide yok/çözülemedi" seçeneği olmalı. Zorla yanlış mahalle atamaktansa NULL dönmek tercih edilmeli.

### 1.9 Türkçe'ye özgü önceki çalışmalar [DOĞRULANMADI — yalnızca arama özetleri]

- Ünal, Aygün, Gerek, "Türkçe adres ayrıştırma için önceden eğitilmiş dil modelleri karşılaştırması", arXiv:2306.13947. Orta boy bir Türkçe adres korpusu kullanılmış. Veri açıklığı belirsiz. <https://arxiv.org/abs/2306.13947>
- `deprem-ml/adres_ner_v2_bert_128k` (HuggingFace): BERT tabanlı Türkçe adres NER modeli. Etiketler şehir, ilçe, mahalle, sokak, bina, telefon… Model kartına göre makro-F1 ≈ 0.84, ilçe F1 0.92, mahalle 0.79. Eğitim verisi özel depoda. <https://huggingface.co/deprem-ml/adres_ner_v2_bert_128k>. **Not:** Etiket kümesinde "telefon" olması, deprem dönemi gerçek verilerinde telefon gürültüsünün yaygın olduğunu doğruluyor.
- Kürklü & Akagündüz (ODTÜ, SIU 2024): UAVT'den türetilmiş veriye ChatGPT ile sentetik hata eklenmiş, T5/TowerInstruct ile adres standardizasyonu yapılmış. <https://open.metu.edu.tr/handle/11511/110721>
- Oflazer & Güzey (1994), "Spelling Correction in Agglutinative Languages", arXiv:cmp-lg/9410004 <https://arxiv.org/abs/cmp-lg/9410004>. Bulgular: yanlış yazılmış dizilerin %23,1'inde değiştirme, %22,2'sinde silme, %17,3'ünde ekleme, %3,3'ünde yer değiştirme hatası var. Değiştirmelerin %34'ü ş-s, ç-c, ı-i, ö-o, ü-u, a-e çiftlerinden oluşuyor. Bir tweet çalışmasında diakritik hatası tüm yazım hatalarının %44,94'ü olarak raporlanmış. (Rakamlar arama özetinden alındı.)

---

## 2. Bizim gazetteer'in tasarımı etkileyen gerçekleri [DOĞRULANDI — `data/raw/melihozkara` üzerinde sayıldı]

| Gerçek | Değer | Tasarım sonucu |
|---|---|---|
| İlçe sayısı / farklı katlanmış ad | 973 / 898 | İlçe adı tek başına il'i belirlemez |
| "Merkez" adlı ilçe | 51 il (büyükşehir olmayanlar) | "Merkez" il bağlamı olmadan anlamsız |
| Başka ilde aynı adlı ilçe | Yenişehir ×3 (Bursa, Diyarbakır, Mersin); Edremit, Gönen, Yenipazar, Altınyayla, Kemer, Ayvacık, Yenice, Bayat, Ortaköy, Gölbaşı ×2 | İlçe → il çıkarımı belirsiz olabilir |
| Kısa ilçe adları (≤3 harf, katlanmış) | bor, çal, çan, çat, çay, han, kaş, mut, of, sur, tut, ula | **Fuzzy yasak.** Yalnız tam eşleşme ve bağlam şartı (çay/han/çan gibi adlar gündelik kelimelerle çakışıyor) |
| Mahalle-seviyesi kayıt toplamı | 73.398 | — |
| `mahalleTur` dağılımı | 1=mahalle 32.471; 0=köy 12.294; 4/6=mevkii 21.684+606; 3=mezra 5.093; 5=yayla/küme evleri 1.250 | Tür önceliği (mahalle > köy > küme > mezra > mevkii) |
| Köy kayıtlarında ad alanı | `adi = null`, ad `koyAdi` alanında | Ad anahtarı = `adi ?? koyAdi` |
| Gerçek mahalle (tur 1): farklı ad | 16.227. Bu adların %76'sı Türkiye'de tek | Tekil ad güçlü kanıt |
| En sık mahalle adları | Cumhuriyet 402, Yeni 381, Fatih 246, Bahçelievler 164, Merkez 156, Atatürk 144, Hürriyet 144, Karşıyaka 129 | Belirsizlik cezası gerekli: −log(n) |
| Bir ilçeyle aynı adı taşıyan mahalle (tur 1) | 3.892. Kendi ilçesiyle aynı ad: 51 (ör. Çankaya/Çankaya, Konak/Konak, Bahçelievler/Bahçelievler, Yenişehir/Yenişehir-Diyarbakır) | "X X İl" girdisinde ilki mahalle, ikincisi ilçe |
| Aynı ilçede aynı adlı iki mahalle (tur 1) | 241 | Belde/köy ön ekiyle ayrışıyor (`koyAdi`) |
| `koyAdi ≠ MERKEZ` olan mahalle (tur 1) | 1.671 (belde mahalleleri) | `koyAdi` = semt/belde takma adı kaynağı |
| Ad token uzunluğu (tüm seviyeler) | 1: 67.038 · 2: 5.647 · 3: 639 · 4: 57 · 5: 13 · 6: 3 · 7: 1 | **MaxSpan = 7** (ad) + 1 (tip anahtar kelimesi). Pratikte 4 ile %99,96 kapsama |
| Rakam içeren adlar | 391 (ör. `19 MAYIS`, `100.YIL`, `2000 EVLER`, `7EYLÜL`, `1.GÜNDOĞAN`) | Sayı ≠ her zaman kapı no; gazetteer tarafında da sayı/harf bölme normalizasyonu |
| Bitişik/özel biçimli adlar | `GAZİOSMANPAŞAOSB`, `DEMİRCİLEROSB`, `HÜRRİYET 1.OSB`, `KURTKÖY FATİH` | Boşluksuz + boşluklu çift anahtar; OSB eşanlamı |
| Parantezli adlar | 223 (ör. `DERE (MERKEZ)`, `YENİ(ABANT SAPAĞI)`) | Parantez içi = takma ad / ek bilgi; iki anahtar indeksle |
| Bir ilçedeki mahalle+köy sayısı | max 218, medyan 37. (Tüm tipler dahil max 675) | Kapsam içi brute-force fuzzy ucuz |
| Bir ildeki mahalle+köy sayısı | max ≈1.468 (Şanlıurfa) | İl kapsamında da brute-force olur |
| Yeniden adlandırılmış ilçeler | Gazetteer'de `EYÜPSULTAN`, `KAHRAMANKAZAN` var; `EYÜP`, `KAZAN` yok | Eski ad → yeni ad takma ad tablosu gerekli |
| Semt ≠ mahalle | Kadıköy'de "Moda" ve Şişli'de "Nişantaşı" mahalle olarak **yok** | Ayrı semt tablosu gerekir (gazetteer'de yok) |
| Sokak verisi | Kaynak depoda 1.276.922 sokak/cadde var (`sokaklar.jsonl`, `melih_tree.tsv`), yerelde indirilmemiş | MVP sokakları gazetteer'e bağlamaz. Faz sonrası opsiyon |

---

## 3. Mimari genel bakış

```
Girdi
 └─ [N] Normalize: Unicode NFC, mojibake onarımı, İ/ı kuralları, U+0307 temizliği
        → norm (Türkçe küçük harf) + fold (ASCII) + ofset haritası
 └─ [T] Tokenize: tarayıcı → Token[] (Kind, BreakBefore, ofsetler)
        + ön-bölme kuralları (alfa|sayı, bitişik anahtar kelime, kesme işareti)
        → Lattice (alternatif kenarlar: birleştirme "i smet", bölme "atillamahallesi")
 └─ [C] Classify: span numaralandırma (≤7 token) ×
        { PatternTagger (regex), KeywordLexicon, GazetteerTrie (exact), Fuzzy (scoped), Composite şemalar }
        → her kenar için Hyp[] (rol, değer, varlık kimlikleri, özellikler)
 └─ [S] Solve-1 (yapısal): soldan sağa beam (durum birleştirmeli) → top-K rol ataması
     Solve-2 (idari): her atama için il→ilçe→mahalle hiyerarşik beam + NULL adaylar + çıkarım
 └─ [R] Score: log-lineer özellik toplamı (yapısal + idari) → N-best
 └─ [K] Calibrate: softmax(T) marjinal → alan bazında izotonik/Platt → güven
 └─ Çıktı: bileşenler + ofsetler + alan güvenleri + match_type + alternatifler + düzeltme günlüğü
```

Temel veri tipleri (C#):

```csharp
enum BreakKind : byte { Start, Section /* , ; \n */, Soft /* : / ( */, Space, Part /* - / . */, Glued /* yapay bölme */, End }
enum TokKind  : byte { Alpha, Num, AlphaNum, Ordinal /* "5." */, Punct, Sym }

readonly record struct Token(
    int Idx, int Start, int End,          // orijinal metindeki [Start,End)
    string Norm, string Fold,             // tr-küçük harf, ASCII katlanmış
    TokKind Kind, BreakKind BreakBefore,
    bool FinalPeriod, bool HadApostropheSuffix);

enum Role : byte {
    None, Noise, Keyword, Landmark,
    Semt, Mahalle, Koy, Mevkii, KumeEvler,
    Cadde, Sokak, Bulvar, Yol, Meydan,
    Site, Bina, Blok, KapiNo, Kat, Daire,
    PostaKodu, Ilce, Il, Ulke }

sealed record Hyp(
    int From, int To,                 // token aralığı [From,To) — lattice kenarı
    Role Role,
    string Value,                     // kanonik değer (ör. "1203/5", "A", "Caferağa")
    int[] EntityIds,                  // gazetteer kimlikleri (idari roller için), aksi halde boş
    float Local,                      // yerel sınıflandırma güveni [0,1]
    HypFeat F,                        // exact/fuzzy dist, keyword desteği, ...
    Correction[] Corr);               // bu hipotez için uygulanan düzeltmeler
```

---

## 4. Algoritma ayrıntıları

### 4.1 Normalizasyon (N)

Sıra önemlidir. Her adım **ofset haritasını** korumalıdır. `int[] map`, normalize metindeki her karakterin orijinal indeksini tutar. Silme ve ekleme bu haritayı güncelleyerek yapılır.

1. **Unicode NFC** (`string.Normalize(NormalizationForm.FormC)`). Ayrışmış "I + U+0307" → "İ" ve "i + U+0307" → "i" olur. Kalan bağımsız U+0307 (JS'te `"İ".toLowerCase()` = `"i\u0307"`) silinir ve `Correction(Kind=CombiningDot)` yazılır.
2. **Mojibake onarımı** (yalnızca tetikleyici örüntü görülürse):
   - cp1254 metnin cp1252 olarak okunması: `Ý→İ, ý→ı, Þ→Ş, þ→ş, Ð→Ğ, ð→ğ`.
   - UTF-8 metnin 1252/1254 olarak okunması: `Ã¼→ü, Ã§→ç, Ã¶→ö, Ä±→ı, Ä°→İ, ÅŸ→ş, Å→Ş, Ä→ğ…`.
   - [DOĞRULANMADI: tablo genel bilgiden; gerçek veriyle test edilmeli.]
3. **Türkçe küçük harf** (`norm`). .NET'te `ToLowerInvariant` `I→i` yapar ve bu Türkçe için yanlış. `CultureInfo("tr-TR")` kullanılabilir ama **açık bir tablo** daha deterministik: `I→ı`, `İ→i`, diğerleri invariant. Karakter sayısı 1:1 korunur.
   - Not: Yalnızca ASCII büyük harfli girdide ("ISTANBUL") `I→ı` "ıstanbul" üretir. Bu yüzden `fold` adımı `ı→i` yapar ve eşleştirme her zaman `fold` üzerinden yürür.
4. **ASCII katlama** (`fold`): `ç→c, ğ→g, ı→i, ö→o, ş→s, ü→u, â→a, î→i, û→u`. 1:1 eşleme olduğu için ofset bozulmaz. **Tüm sözlük ve gazetteer anahtarları da aynı fonksiyonla katlanır** (simetri).
5. **Gürültü maskeleme (tokenizasyondan önce, regex ile)**: telefon, e-posta, URL ve "Tel:/GSM:/Cep:" etiketleri. Bu aralıklar `Role.Noise` olarak **önceden atanmış** sabit hipotezlerdir. Böylece telefonun "532" parçası kapı no adayı olamaz (§6.10).
6. Noktalama sadeleştirme: tekrarlı noktalama (`,,`, `..`) tek karaktere indirilir. `\` → `/` yapılır. Unicode tireler (– —) `-` olur.

### 4.2 Tokenizasyon ve kafes (T)

**Tarayıcı:** karakter sınıfları harf, rakam, boşluk ve noktalama olarak ayrılır. Token'lar harf/rakam dizileridir. Noktalama `BreakBefore`'u belirler:

| Ayraç | BreakKind | Kesme cezası (Nominatim'den uyarlama) |
|---|---|---|
| metin başı/sonu | Start/End | −0.5 (bitirme ödülü) |
| `,` `;` `\n` `\t` | Section | −0.5 |
| `:` `(` `)` | Soft | −0.3 |
| boşluk | Space | +0.1 |
| `-` `/` `.` (token içi) | Part | +0.2 |
| yapay bölme (ön-bölme kuralı) | Glued | +0.4 |

Kurallar:
- **Virgül pelias'taki gibi katı bölüm sınırı olmamalı.** Türk kullanıcılar virgülü tutarsız kullanır ("Caferağa Mah., Moda Cad.,No:12"). Virgülü aşan çok-token'lı span'e *ceza* verilir (`word_continuation_penalty`), yasak konmaz.
- `Part` ayraçlı token'lar **iki çözünürlükte** tutulur (pelias `split` fikri): `1203/5` hem tek token (Kind = Num, `"/"` içerir) hem de `1203`, `5` alt token'ları olarak. Lattice'te birleşik token, alt token'ları kapsayan bir kenardır.
- **Nokta:** `Mah.`, `Cad.`, `Sk.` gibi sondaki nokta silinir ve `FinalPeriod = true` olur. `5.` (rakam + nokta) `Kind = Ordinal` olur. `100.YIL` → `100.` + `YIL` (Ordinal + Alpha).
- **Kesme işareti:** `Kadıköy'de`, `Migros'un` → kök token alınır, ek atılır, `HadApostropheSuffix = true` olur. Türkçe özel ad + ek kuralı (libpostal `DELETE_OTHER_APOSTROPHE` benzeri).

**Ön-bölme kuralları (alternatif kenar üretir, orijinali silmez):**

| Kural | Örnek | Uygulama |
|---|---|---|
| Rakam→harf(≥2) sınırı | `147sok` → `147` `sok` | Kesin bölme (Glued) |
| Harf(≥2)→rakam sınırı | `no5` → `no` `5`; `sk12` → `sk` `12` | Kesin bölme |
| Rakam + tek harf | `17a`, `b2` | **Bölme yok** (kapı/blok değeri) |
| Bitişik uzun anahtar kelime soneki | `atillamahallesi` → `atilla` + `mahallesi`; `bagdatcaddesi` → `bagdat` + `caddesi` | Ters (suffix) trie ile. Önek ≥3 harf olmalı. **Alternatif kenar** olur, bölünmemiş biçim de kalır |
| Bitişik kısa anahtar kelime soneki (`mah`, `cad`, `sok`, `sk`, `cd`) | `atillamah` → `atilla mah` | Yalnızca önek tek başına gazetteer/sözlükte **tam** eşleşiyorsa |
| Kırık İ | `i smet paşa` → `ismet paşa` | Tek harfli `i`/`ı` token'ı ve ardından Space ile Alpha token geliyorsa birleştirilmiş kenar eklenir (ceza 0.2) |
| Boşluk-kaldırma birleştirme | `ismet pasa` ~ `ismetpasa`, `demirciler osb` ~ `demircilerosb` | Gazetteer'de boşluksuz anahtar ile eşleşir. Ayrı kenar gerekmez: span'in boşluksuz anahtarı da sorgulanır |

Örnek: `atillamahallesi475sok` için:
1. Rakam sınırından bölme yapılır → `atillamahallesi | 475 | sok`.
2. Suffix trie `atilla|mahallesi` alternatif kenarını verir.

"Yeniköy" gibi adlar `koy` soneki içerir. Ancak `koy`/`koyu` kısa kalıplar listesinde ve önek `yeni` tek başına bir yer adıyla eşleşse bile, bölünmemiş `yenikoy` gazetteer'de tam eşleştiği için beam onu tercih eder. Bu nedenle bölme **yıkıcı değil**, yalnızca alternatif kenardır.

**Kafes (lattice):** düğümler 0..n token sınırlarıdır. Her kenar `[i,j)` bir span'dir. Alternatif tokenizasyon kenarları (birleşik/bölünmüş) da aynı düğüm uzayına yerleşir. Bölünen token için ara düğümler eklenir: `atillamahallesi` tek token iken bir "ara düğüm" oluşur ve alt-token kenarları ona bağlanır. Basitlik için **önce ön-bölme uygulanmış token dizisi üretilip**, bölünmemiş biçim "birleştirme kenarı" olarak eklenir. Böylece tüm alternatifler tek düz düğüm dizisi üzerinde ifade edilir.

### 4.3 Span numaralandırma

- `MaxSpan = 7` token (gazetteer adı) + gerekirse tip anahtar kelimesi. Bileşik şemalar ayrıca birleştirir.
- Naif yöntem tüm `[i, j)` aralıklarını üretir: `O(n · MaxSpan)` ≤ 64·7 = 448 span. Bu zaten küçük.
- **Trie yürüyüşü ile budama:** gazetteer adları katlanmış token dizileri olarak bir **token trie**'ye konur (düğüm = token, kenar = sonraki token). Her `i` için trie'de yürünür. Çocuk yoksa durulur ve terminal düğümlerde hipotez yazılır. Maliyet `O(n · ortalama_derinlik)`. Placeholder'ın "indekste var mı?" filtresinin O(1)-adımlı versiyonu.
- Boşluksuz anahtar: her `[i, j)` span'i (j − i ≤ 3) için `concat(fold tokens)` ayrıca `Dictionary<string, int[]>`'de aranır. Bu `ismet pasa`→`ismetpasa`, `demirciler osb`→`demircilerosb` ve `100 yil`→`100yil` eşleşmelerini sağlar.
- Fuzzy yalnızca (a) tam eşleşmesi olmayan ve (b) uzunluğu ≥4 olan span'ler için çalışır. Mümkünse kapsamlı yapılır (§7).

### 4.4 Sınıflandırma (C)

Her sınıflandırıcı kenarlara `Hyp` ekler. Bir kenarda birden fazla hipotez olabilir ve her rol için en iyi `H = 8` tanesi tutulur (pelias `MAX_PAIRS_PER_LABEL = 8`).

**A. PatternTagger** (katlanmış metin üzerinde, sıra önemli; ilk ikisi gürültü):

```text
PHONE   (?<!\d)(?:\+?90[\s.\-]?|0)?\(?[2-5]\d{2}\)?[\s.\-]?\d{3}[\s.\-]?\d{2}[\s.\-]?\d{2}(?!\d)
EMAIL   \S+@\S+\.\w+
POSTA   (?<!\d)(?:0[1-9]|[1-7]\d|8[01])\d{3}(?!\d)                      → PostaKodu (il = ilk 2 hane)
KAPI    \b(?:no|nu|numara|n)\s*[:.]?\s*(\d{1,5})(?:\s*[/\-]\s*(\d{1,4}|[a-z]))?(?:\s*([a-z]))?\b
          → KapiNo=$1[$3]; $2 sayı ise Daire adayı (No:17/5 → kapı 17, daire 5); $2 harf ise kapı=17/A
KAPI2   \b(\d{1,5})\s*/\s*([a-z])\b                                     → KapiNo "17/A"
KAT     \b(?:kat|k)\s*[:.]?\s*(-?\d{1,2}|zemin|z|bodrum|b|asma|giris|cati)\b
KAT2    \b(\d{1,2})\s*\.?\s*kat\b   |   \b(zemin|bodrum|asma|giris|cati|teras)\s*kat\b
DAIRE   \b(?:daire|dai|dr|d|ic\s*kapi(?:\s*no)?)\s*[:.]?\s*(\d{1,4}[a-z]?)\b
DAIRE2  \b(\d{1,4})\s*(?:nolu|numarali|no\.?lu)\s*daire\b
BLOK    \b([a-z]\d{0,2}|\d{1,2}[a-z]?)\s*[-.]?\s*(?:blok|blk|bl)\b  |  \b(?:blok|blk|bl)\s*[:.]?\s*([a-z]\d{0,2}|\d{1,2}[a-z]?)\b
NUMSTR  \b(\d{1,5}(?:\s*/\s*\d{1,3})?)\s*(?:\.|nolu|numarali|no\.?lu)?\s*(sokak|sokagi|sok|sk|cadde|caddesi|cad|cd|bulvar|bulvari|blv|bulv|yolu?)\b
          → Sokak/Cadde/Bulvar, Value="1203/5" | "864" | "127"
ORDSTR  \b(\d{1,3})\s*\.\s*(sokak|sk|sok|cadde|cad|cd)\b                → "5. Sokak" (Value="5")
```

- Regex'ler *aday üretir*, kararı beam verir. Örneğin `KAT` ve `DAIRE` tek harfli formları (`k 3`, `d 5`) yalnızca rakam takip ediyorsa tetiklenir. `d blok` BLOK kuralına gider.
- Her regex eşleşmesi token sınırlarına "yapıştırılır". Eşleşme token ortasında bitiyorsa (ör. `no:5/3`) Part düğümleri kullanılır.

**B. KeywordLexicon** (libpostal tr sözlükleri + genişletme; `kanonik|varyant…`, katlanmış):

| Kanonik | Varyantlar | Rol / konum |
|---|---|---|
| mahallesi | mahalle, mah, mh, mahalesi, mahhallesi, mahallasi | Mahalle tipi, **adın sonunda** |
| köyü | koy, koyu, ky | Köy tipi, sonda |
| mevkii | mevki, mevkı, mvk | Mevkii, sonda |
| küme evleri | kume evler, kume evleri, k.evler | KumeEvler, sonda veya tek başına |
| caddesi | cadde, cad, cd, cadesi, cddesi | Cadde, sonda |
| sokağı | sokak, sok, sk, skk, sokagi, sokk | Sokak, sonda |
| bulvarı | bulvar, bulv, blv, bl(!) | Bulvar, sonda (`bl` blok ile çakışır, bağlama göre seçilir) |
| yolu | yol | Yol, sonda |
| meydanı | meydan, mey, myd | Meydan |
| sitesi | site, sit, st(!) | Site, sonda |
| apartmanı | apartman, apt, ap | Bina, sonda |
| konutları / evleri / rezidans / residence / plaza / iş merkezi / işhanı / pasajı | — | Bina/Site, sonda (`evleri` mahalle adı içinde de var: "2000 Evler") |
| blok | blk, bl | Blok, önde veya sonda |
| numara | no, nu, numarası, nolu, numaralı | KapiNo öneki / "N nolu" son eki |
| kat | k | Kat |
| daire | d, dr, dai, iç kapı | Daire |
| ilçesi | ilce, ilçe | İlçe sonda |
| ili | il | İl sonda (`il` kısa, yalnız sonda ve büyük harfli/ayrı token ise) |
| organize sanayi bölgesi | osb, o.s.b, org san bol | OSB eşanlamı (gazetteer'de "...OSB") |
| landmark tetikleyicileri | karşısı, yanı, arkası, bitişiği, üstü, altı, önü, civarı, yakını, köşesi, girişi, içi, arası | Landmark, **sonda** (postposition) |
| posta kutusu | pk, p.k | Not/PO box |

Türkçe'nin yapısal özelliği tip sözcüğünün **ad sonrasında** gelmesidir ("Atatürk Caddesi"). İstisnalar `No`, `Kat`, `Daire`, `Blok`, `Tel` gibi sayı öncesi etiketlerdir. Bileşik şemalar buna göre yazılır.

**C. GazetteerTrie (tam eşleşme):** İl (81 ad + plakalar), ilçe (973 + takma adlar: eski adlar, "Eyüp→Eyüpsultan", "Kazan→Kahramankazan"), mahalle-seviyesi (adi ?? koyAdi; parantez içi ve dışı ayrı anahtarlar; boşluklu/boşluksuz), semt (ayrı tablo; §6.1), belde (`koyAdi ≠ MERKEZ` olanlar). Her terminal düğüm `(Level, EntityId[], NameFreq)` taşır.

**D. Bileşik şemalar** (pelias `CompositeClassifier` uyarlaması, komşuluk zorunlu):

| Şema | Örnek | Rol | Taban güven |
|---|---|---|---|
| [GazMahalle] [kw:mahallesi] | Caferağa Mah. | Mahalle (keyword-supported) | 0.95 |
| [Alpha+] [kw:mahallesi] | Bilinmeyenadı Mah. | Mahalle (gazetteer'de yok) | 0.55 |
| [GazKoy] [kw:köyü] | Karaburun Köyü | Köy (veya 6360 sonrası mahalle) | 0.9 |
| [Alpha/Person+] [kw:caddesi] | Abdi İpekçi Cad. | Cadde | 0.85 |
| [Num(/Num)] [kw:sokağı] | 1203/5 Sk. | Sokak (numaralı) | 0.9 |
| [Ordinal] [kw:cadde/sokak] | 7. Cad. | Cadde | 0.88 |
| [Num] [nolu] [kw:sokak] | 127 nolu sokak | Sokak | 0.9 |
| [Alpha+] [kw:sitesi] | Gül Sitesi | Site | 0.85 |
| [Letter/AlphaNum] [kw:blok] / [kw:blok] [Letter] | A Blok / Blok B2 | Blok | 0.9 |
| [Any+] [kw:landmark] | Migros karşısı | Landmark | 0.8 |
| [GazIlce] "/" [GazIl] | Kadıköy/İstanbul | İlçe+İl (Soft ayraç destekli) | 0.95 |
| [Alpha] [kw:küme evleri] | Kuruca Küme Evleri | KumeEvler/Mevkii | 0.8 |

### 4.5 Çözücü (S): yapısal beam

Problem: kafes üzerinde, çakışmayan ve tüm token'ları "rol" veya "atla (None)" ile kapsayan bir yol bulmak. Kısıtlar:
- Her rol en fazla bir kez kullanılır. İstisnalar: `Landmark` (çok), `Noise` (çok), `Keyword` (yalnız şema içinde), `Cadde` + `Sokak` (her ikisi de bir kez). Aynı varlığa işaret eden **tekrar span**'i ise `Repeat` olarak cezasız alınır (§6.12).
- Hiyerarşi tutarlılığı: seçilen il/ilçe/mahalle hipotezlerinin `EntityIds` kümelerinin **uyumlu bir kesişimi** bulunmalıdır (gazetteer'de en az bir (il, ilçe, mahalle) üçlüsü). Bu beam sırasında artımlı olarak denetlenir.

**Neden kartezyen değil?** Pelias çözümü `Π_label (pairs_label + 1)`'dir. 12 rol ve 8 aday için 9¹² ≈ 2,8·10¹¹ eder. Pelias bunu `MAX_RECURSION = 10` ve `MAX_SOLUTIONS = 50000` ile keser, çakışmaları da *sonradan* filtreler. Bizde idari hipotezler varlık-kimlik kümeleri taşıdığı için kombinasyon sayısı daha da büyük. Nominatim'in DFS'i tam numaralandırma yapıyor ve yalnız gramerle budanıyor (kendi sorgu boyutunda kabul edilebilir). Biz **soldan sağa beam + durum birleştirme** kullanacağız:

- Durum `σ = (pos, usedMask:uint32, lastRank:byte, dir:sbyte, adminSig:ulong)`.
  - `adminSig` = o ana kadar seçilen idari hipotezlerin kimlik-kümesi parmak izi.
  - Aynı imzalı durumlar birleştirilir ve en iyi skorlu olan tutulur (Viterbi recombination).
- Geçiş: `pos = i` durumundan
  - (a) token `i`'yi `None` (sınıflandırılmamış) olarak atla → `i+1`, ceza `w_unk`.
  - (b) `[i, j)` kenarındaki her `Hyp h` için, rol izinliyse ve idari uyum varsa → `j`. Skor artışı `Δ = φ(h) + geçiş_cezası(lastRank → rank(h)) + kesme_cezaları`.
- Her `pos` için beam genişliği `B = 32`. Global erken kesme: `best_final − 1.5`'ten kötü kısmi durumlar atılır (Nominatim `min_ranking` fikri).
- Sonuç: `pos = n`'deki en iyi `K = 16` atama → idari çözüm katmanına gider.

**Karmaşıklık:**
- Kenar sayısı ≤ n·7. Kenar başına ≤ 8 hipotez + atla. Durum sayısı ≤ B. Toplam genişletme `O(n · 7 · 9 · B) = O(2016·n)`. n = 40 için ≈ 80k genişletme.
- Her genişletme O(1) bit-maske işlemi + idari uyum kontrolü. İdari uyum, önceden hesaplanmış `HashSet<(int ilceId, int ilId)>` ve `ilçe → mahalle` haritalarıyla O(min(|A|, |B|)) küme kesişimidir. Kimlik listeleri ≤ 402 ve tipik olarak <10 eleman.
- Beklenen süre: tek adres için < 1 ms [DOĞRULANMADI: benchmark yapılmadı].
- Kötü durum sınırı: n = 64 token üst sınır (Nominatim 50 token'ı reddediyor). Fazlası kesilir ve `Correction(Truncated)` yazılır.

**Sıra modeli (geçiş cezası):** Türkçe kanonik sıra **iki bloktan** oluşur. "Küçükten büyüğe" ifadesi tam doğru değil: yerel blok büyükten küçüğe, idari kuyruk küçükten büyüğe gider.

```
Yerel blok (büyük→küçük):  Semt(1) Mahalle/Köy(2) Mevkii/KümeEvler(3) Cadde/Bulvar(4) Sokak(5) Site/Bina(6) Blok(7) KapıNo(8) Kat(9) Daire(10)
İdari kuyruk (küçük→büyük): PostaKodu(11, serbest) İlçe(12) İl(13) Ülke(14)
Landmark: herhangi bir yerde, sıra cezası yok
```

- `rank(h) < lastRank` ise inversiyon cezası `w_inv = −0.4`. Ayrıca şu yaygın varyasyonlar cezasız veya düşük cezalı:
  - **Ters idari blok (dir = −1):** ilk idari hipotez Mahalle'den önce İl/İlçe ise ("İstanbul Kadıköy Caferağa Mah. …") yön `dir = −1` kilitlenir. Tek seferlik ceza `−0.2` uygulanır ve İl(13)→İlçe(12)→Mahalle(2) geçişlerine inversiyon cezası verilmez (Nominatim `direction` fikri).
  - `Kat/Daire` ↔ `Blok` yer değiştirmesi: ceza −0.1.
  - `PostaKodu` her yerde: ceza 0. İl/İlçe'ye bitişikse `+0.2` bonus.
  - Mahalle cadde/sokaktan *sonra* ("Bağdat Cad. … Bostancı Kadıköy"): −0.3.

### 4.6 İdari çözüm (S-2): hiyerarşik beam

Yapısal atamadan gelen idari kanıtlar şunlar: `ilSpan?`, `ilceSpan?`, `mahSpan?`, `semtSpan?`, `postaKodu?`, landmark içindeki zayıf idari kanıtlar.

```
Seviye 1 — İl adayları (≤5):
   il span hipotezleri (exact/fuzzy) ∪ posta kodu öneki ∪ ilçe span'inin ebeveynleri ∪ {NULL}
Seviye 2 — İlçe adayları | il (≤8):
   ilçe span hipotezleri ∩ children(il) ∪ "Merkez" kuralı ∪ semt'in ilçesi ∪ mahalle ebeveynleri ∪ {NULL}
Seviye 3 — Mahalle adayları | ilçe (≤10):
   mahalle span hipotezleri ∩ children(ilçe) ∪ (kapsamlı fuzzy, yalnız ilçe biliniyorsa) ∪ semt→mahalle ∪ {NULL}
```

- Her seviye kendi özellik skoruyla genişletilir. Beam genişliği `B_admin = 16` üçlüdür.
- Eksik seviyeler **çıkarılır** (`Inferred`): mahalle tekil olarak bir ilçeye aitse ilçe ve il ondan çıkar. İlçe adı tekilse il ondan çıkar.
- Çıkarım skoru `+w_inferred` (stated'den düşük) ve belirsizlik cezası `−0.15·log₂(aday sayısı)`.
- NULL adayının skoru 0'dır. Herhangi bir gerçek aday negatif net skordaysa NULL kazanır (zorla atama yapılmaz).
- Karmaşıklık: 5 × 8 × 10 = 400 üçlü üst sınır. Bu her yapısal aday için ve K = 16 aday için ≤ 6.400 değerlendirme eder. İhmal edilebilir.

Yapısal skor ile idari skor toplanarak **N-best** (N ≤ 16 × 16, sonra tekilleştirilerek en iyi 10) elde edilir.

---

## 5. Skor fonksiyonu (R)

`S(A) = Σ_f w_f · φ_f(A)` (log-lineer). Başlangıç ağırlıkları elle konuldu. Etiketli dev seti hazır olunca **koordinat yükselişi / grid search**, daha sonra da libpostal'daki gibi **averaged (structured) perceptron** ile öğrenilmeli. Skor özelliklerde doğrusal olduğu için beam'in N-best'i üzerinden perceptron güncellemesi doğrudan uygulanabilir.

| # | Özellik φ | Tanım | Başlangıç w | Kaynak fikri |
|---|---|---|---|---|
| 1 | `cov_conf` | Σ local_h · chars_h / chars_toplam (Noise hariç) | **+4.0** | pelias `Solution.computeScore` |
| 2 | `unk_alpha` | Sınıflandırılmamış harf token sayısı | −0.6 / token | pelias kapsama, Photon kalan karakter |
| 3 | `unk_num` | Sınıflandırılmamış sayı token sayısı | −0.8 / token | — |
| 4 | `order_inv` | Kanonik sıra inversiyonu (izinli istisnalar hariç) | −0.4 / çift | pelias MustNot*, Nominatim yön |
| 5 | `admin_rev` | Ters idari blok kullanıldı | −0.2 | Nominatim `dir_penalty` |
| 6 | `kw_support` | Rolü anahtar kelimeyle desteklenen span (Mah./Cad./Köyü/İlçesi…) | +0.5 / span | Mordecai `mention_admin_cue` |
| 7 | `kw_conflict` | Anahtar kelime başka rolü işaret ederken farklı rol (ör. "X Cad." → Mahalle) | −1.5 / span | — |
| 8 | `cross_section` | Çok-token'lı span virgül/Section'ı aşıyor | −0.8 | Nominatim `word_continuation_penalty` |
| 9 | `break_pen` | Kenar sınırlarındaki kesme cezaları toplamı | ×1.0 | Nominatim `PENALTY_BREAK` |
| 10 | `hn_orphan` | KapıNo var; cadde/sokak/mahalle/köy yok | −0.5 | pelias `TokenDistanceFilter` |
| 11 | `hn_far` | KapıNo, son sokak/caddeden >2 token uzakta | −0.3 | pelias `TokenDistanceFilter` (mesafe 2) |
| 12 | `il_match` | İl stated: exact +1.0, fuzzy +1.0 − 0.5·d | +1.0 | — |
| 13 | `ilce_cons` | İlçe stated ve il ile tutarlı | +1.2 | CLAVIN ortak-ebeveyn |
| 14 | `mah_cons` | Mahalle stated ve ilçe ile tutarlı | +1.2 | Placeholder kesişim |
| 15 | `inconsistent` | Stated iki seviye gazetteer'de çelişkili | **sert red** (ikisi de fuzzy ise −3.0) | — |
| 16 | `inferred` | Çıkarılmış seviye sayısı | +0.3 / seviye | — |
| 17 | `pk_agree` | Posta kodu ilk 2 hane = il plakası | +0.6 | libpostal "postcode have context" |
| 18 | `pk_conflict` | Posta kodu ≠ il (il metinden geliyorsa) | −1.0 + düzeltme günlüğü | pelias deal-breaker |
| 19 | `ambig` | −log₂(kapsamdaki aynı adlı aday sayısı) | ×0.15 | Mordecai `log_n_same_name` |
| 20 | `mah_type` | mahalle 0 · köy −0.1 · küme/yayla −0.3 · mezra −0.4 · mevkii −0.6 | tablo | gazetteer `mahalleTur` |
| 21 | `fuzzy_cost` | Σ edit / len (fuzzy eşleşmeler) | −2.0 | Nominatim `match_penalty` |
| 22 | `alias_used` | Semt/belde/eski ad/takma ad kullanıldı | −0.1 / adet | — |
| 23 | `short_name_noctx` | ≤3 harfli ilçe adı, `/`, il veya "ilçesi" bağlamı olmadan | −1.5 | — |
| 24 | `pop_prior` | log₁₀(nüfus)/7 (ADNKS mahalle nüfusu varsa) | +0.2 | Mordecai prom, CLIFF TopAdminPopulated |
| 25 | `landmark_admin` | Landmark içindeki idari ad (zayıf kanıt), seçilen adayla aynı | +0.3 | — |
| 26 | `street_name_is_admin` | Cadde/sokak adı bir mahalle/ilçe adıyla aynı (ör. "Fatih Cad.") | 0 (bilgi) | — |
| 27 | `repeat_ok` | Aynı varlığa ikinci referans | 0 (ceza yok) | — |

Notlar:
- 15 numaralı özellik sert kısıttır. İlçe ve mahalle ikisi de *stated* ve gazetteer'de uyumsuzsa birinin yanlış okunduğu varsayılır ve beam diğer hipotezlere geçer.
- 24 numaralı özellik için nüfus verisi depoda yok. TÜİK ADNKS mahalle nüfusu ileride eklenebilir [DOĞRULANMADI: lisans ve erişim incelenmedi]. Veri gelene kadar `w = 0`.
- Eşitlikte tercih sırası (pelias `comparitor` benzeri): daha çok stated idari seviye > daha az fuzzy > daha kısa toplam span.

---

## 6. Zor fenomenler ve ele alınışları

### 6.1 Semt takma adları (Moda, Nişantaşı, Bahçeli…)
- Gazetteer'de semt yok. Doğrulandı: Kadıköy'de "Moda", Şişli'de "Nişantaşı" mahalle değil.
- Ayrı **semt tablosu** gerekir: `semt(ad, ilce_id, mahalle_id[]?)`. Olası kaynaklar: (1) OSM `place=suburb|quarter|neighbourhood` (TR) [lisans ODbL, DOĞRULANMADI]. (2) Wikidata (`data/raw/wikidata` klasörü şu an boş). (3) Gazetteer `koyAdi ≠ MERKEZ` belde adları (1.671 mahalle; ör. Sapanca'da `KURTKÖY FATİH`).
- Kural: semt eşleşmesi **ilçe kanıtı** olarak çalışır (ilçeyi kısıtlar). Mahalle yalnızca semt tek mahalleye eşleniyorsa `Inferred` ve düşük güvenle doldurulur. Aksi halde `semt` alanı doldurulur, `mahalle = null` bırakılır.

### 6.2 Eski köy adları ve 6360 dönüşümü
- 6360 sayılı Kanun (2012, yürürlük 2014) ile büyükşehir illerindeki köyler mahalleye dönüştü [DOĞRULANMADI: tarih genel bilgi]. Doğrulandı: "Karaburun" Arnavutköy'de **mahalle** olarak kayıtlı.
- Kural: `[Ad] köyü` → hem `Koy` (tur 0) hem de aynı adlı `Mahalle` (tur 1, büyükşehir ilinde) hipotezi üretilir. İkincisine `alias_used` (−0.1) uygulanır ve `Correction(KoyToMahalle)` yazılır.
- Yeniden adlandırılmış köyler (1960'lar ad değişiklikleri): eski→yeni ad tablosu gerekir. Olası kaynak Nişanyan "Index Anatolicus" [lisans DOĞRULANMADI]. MVP dışında.

### 6.3 "Merkez" ilçe
- 51 ilde Merkez ilçesi var (büyükşehir olmayan iller). Kural: "Merkez" ancak `/`, il adı veya "Merkez ilçesi" bağlamıyla ilçe olur. İl ile birlikte ise o ilin Merkez'idir.
- **Büyükşehir ilinde "Merkez"** (ör. "Merkez/Kayseri" — Kayseri'de Merkez ilçesi yok, doğrulandı): ilçe = NULL olur ve `Correction(MerkezIlceYok)` yazılır. Mahalle verildiyse ilçe ondan çıkarılır. Kayseri "Cumhuriyet" mahallesi 7 ilçede var, bu yüzden belirsiz ve düşük güvenli kalır.
- "Merkez" mahalle adı (156 adet tur 1 + 3.045 tüm tipler) ve "şehir merkezi" ifadesi landmark olarak geçebilir. Bu yüzden `ambig` cezası güçlü çalışır.

### 6.4 İlçe adıyla aynı mahalle adı
- 51 mahalle kendi ilçesiyle aynı adı taşıyor (Çankaya/Çankaya, Konak/Konak, Bahçelievler/Bahçelievler…). Kural: aynı katlanmış ad iki kez geçiyorsa ilki Mahalle, ikincisi İlçe olur (sıra modeli zaten bunu verir).
- Tek geçişte: anahtar kelime varsa ("Mah.") o kazanır. Yoksa ve ad idari kuyruktaysa (il'e bitişik) İlçe tercih edilir. Mahalle alternatifi N-best'te kalır. 3.892 mahallenin *herhangi bir* ilçeyle aynı ada sahip olması nedeniyle (ör. "Fatih Mah., Arifiye/Sakarya"), il bağlamı ilçe hipotezini tutarsızlıkla eler.

### 6.5 Numaralı sokaklar
- `1203/5 Sk.` → NUMSTR. Değer "1203/5" olur. Kural: `/` içeren sayı + sokak anahtar kelimesi → **sokak adı**, kapı/daire değil. Çok-çözünürlüklü token sayesinde `1203/5` bütün halinde değerlendirilir.
- `864 sokak`, `5. Sokak`, `7.Cad.`, `127 nolu sokak`, `1820sk` (bitişik) → aynı kural ailesi.
- `No: 1203/5` (öncesinde `no`) ise KAPI kuralı kazanır: kapı 1203, daire 5. Belirleyici olan tip anahtar kelimesinin konumudur.
- Ad içinde sayı olan mahalleler (`19 Mayıs`, `100. Yıl`, `2000 Evler`) önce gazetteer trie'de tam eşleşir. `kw_support` (Mah.) + `cov_conf` sayesinde kapı no hipotezini yener.

### 6.6 `No:17/5`, `17/A`, `No:5-7`, `17 A Blok`
- `No:17/5` → kapı 17, daire 5. Türk kullanımı dış kapı / iç kapı şeklindedir. Alternatif hipotez kapı = "17/5" (`Local` 0.3) olarak tutulur. Ayrıca `D:` açıkça verilmişse alternatif seçilir (daire iki kez atanamaz).
- `17/A` ve `17A` → kapı "17/A". Harf daire değildir.
- `No:5-7` → kapı "5-7" (aralık).
- `17 A Blok` → kapı 17, blok A (BLOK kuralı harfi tüketir). `No:17 A` + sonda blok yoksa → kapı "17A".

### 6.7 `K:3 D:5` ve yön kısaltmaları ile D/K belirsizliği
- libpostal tr sözlüğü D=doğu, K=kuzey kısaltmalarını içeriyor ve `ambiguous_expansions` listesinde b/d/g/k var. Bizim sözlükte **tek harf yön kısaltması yok**.
- Tek harf `k`/`d` yalnızca hemen ardından (`:`/`.`/boşluk sonrası) rakam geliyorsa Kat/Daire adayı olur. Ardında veya önünde "blok" varsa Blok harfi olur. Tam yazılmış "Kuzey Sok." / "Doğu Mah." ise ad olarak kalır.
- `b` → "bodrum" (yalnız `kat` bağlamında) veya blok harfi. `z` → zemin (yalnız `kat`/`k` sonrası).

### 6.8 Bitişik token'lar
- §4.2 ön-bölme kuralları. Her bölme `Correction(Split)` ile ofsetli kaydedilir. Bölünmemiş biçim de kafeste kaldığı için yanlış bölme maliyetsiz geri alınır.

### 6.9 Kırık İ
- Üç kaynak var: (a) U+0307 birleşik nokta, (b) PDF/OCR kaynaklı "i smet", (c) ASCII "ISMET" → `ı`.
- (a) Normalizasyonda temizlenir. (b) Birleştirme kenarı eklenir. (c) Katlamada `ı→i` yapılır.
- Örnek: `i smet paşa` → `ismet` + `paşa` → boşluksuz anahtar `ismetpasa` = İSMETPAŞA (Kuluncak/Malatya'da doğrulandı).

### 6.10 Telefon ve diğer gürültüler
- Telefon, e-posta ve "Tel:/Cep:/GSM:" etiketleri normalizasyonda Noise olarak işaretlenir ve `cov_conf` paydasından çıkarılır.
- 10–11 haneli sayılar asla kapı no/posta kodu olmaz.
- Kişi adları ("Ali Veli") sınıflandırılmamış kalır (`unk_alpha` cezası). İleride bir "alıcı adı" rolü eklenebilir. Adres başında 2–3 token'lık, gazetteer'de olmayan dizi bir sinyal olabilir.

### 6.11 Landmark ("… karşısı/yanı/arkası")
- Tetikleyici postposition'lar için bkz. §4.4-B. Landmark span'i tetikleyiciden **geriye doğru** en yakın güçlü sınıra kadar uzar: Section ayracı, tipli bir rolün sonu (Mah./Cad./No), sayı veya metin başı.
- Landmark içindeki idari adlar ("Kadıköy Belediyesi arkası") ayrı rol olmaz. Sadece `landmark_admin` zayıf kanıtı (+0.3) olarak kullanılır.
- Parantez içi açıklamalar ("(Eczane yanı)") Soft ayraçlarla aynı mantıkla landmark/not olur.

### 6.12 Tekrarlanan token'lar
- Aynı katlanmış span bitişik veya yakın tekrar ediyorsa ("Caferağa Mah. Caferağa Mah.", "Kadıköy Kadıköy İstanbul"):
  - Hiyerarşik yorum mümkünse (mahalle = ilçe adı, §6.4) o yorum önce denenir.
  - Değilse ikinci geçiş `Repeat` olarak aynı varlığa bağlanır (ceza 0) ve `Correction(Duplicate)` yazılır.
- Anahtar kelime tekrarı ("Mah. Mah.") Keyword olarak yutulur.

### 6.13 Site/blok/bina
- `X Sitesi`, `X Apt.`, `X Konutları`, `X Rezidans`, `X Plaza`, `X İş Merkezi` → Site/Bina rolü (adla birlikte). Blok her iki yönde de olabilir (A Blok / Blok A / A-1 Blok / B2 Blok).
- `X Evleri` çakışması: "2000 Evler" (mahalle), "Yayla Evleri" (mevkii) gazetteer'de varsa onlar kazanır. Yoksa Site olur.

### 6.14 OSB
- Eşanlam: `organize sanayi bölgesi` ↔ `osb` ↔ `o.s.b.`. Gazetteer'de OSB'ler mahalle olarak kayıtlı ve çoğu bitişik yazılmış (`DEMİRCİLEROSB` Gerede/Bolu, `GAZİOSMANPAŞAOSB` Altıeylül/Balıkesir, `HÜRRİYET 1.OSB` Merkez/Bilecik; doğrulandı).
- Gazetteer anahtar üretiminde: (1) `osb` sonekini ayır, (2) boşluksuz anahtar ekle, (3) "organize sanayi bölgesi" açılımını ekle.
- OSB içi caddeler numaralı veya renklidir ("3. Cad.", "Sarı Cad."). Normal kurallar uygulanır.

### 6.15 Küme evler
- Gazetteer'de iki biçim var: mevkii olarak "KURUCA KÜME EVLERİ MEVKİİ" (Kepçeli köyü, Genç/Bingöl; doğrulandı) ve "1.KÜME MEVKİİ". UAVT köy adreslerinde ise sokak seviyesinde "Küme Evler" yaygın [DOĞRULANMADI: sokak verisi yerelde yok].
- Kural: `[Ad]? Küme Evler(i)` → önce gazetteer mevkii araması yapılır. Yoksa `KumeEvler` sokak-seviyesi rolü olur (değer ad veya boş). Ardından gelen `No:` kapı numarası olur.

### 6.16 Kısa adlar ve gündelik kelime çakışması
- Çay, Han, Çan, Kaş, Of, Sur, Tut, Mut, Bor, Ula, Çal, Çat: fuzzy kapalı ve `short_name_noctx` cezası var. "Han" ayrıca "İşhanı/Han" bina tipleriyle çakışıyor.

### 6.17 Posta kodu
- 5 hane ve ilk 2 hane ∈ 01..81 = il plakası. Uyum ve çelişki özellikleri 17 ve 18 numaralı özelliklerdir.
- Çelişkide il metindeki adla belirlenir (iki bağımsız kanıt: ilçe + il). Posta kodu `PostaKodu` alanında kalır, ama `Correction(PostcodeConflict)` yazılır ve güven düşer.
- İl adı yoksa posta kodu il'i **çıkarır** (`Inferred`).
- Posta kodu → ilçe/mahalle eşlemesi (PTT verisi) MVP dışında [DOĞRULANMADI: kaynak/lisans].

---

## 7. Fuzzy eşleştirme

**Algoritma:** **OSA** (Optimal String Alignment; kısıtlı Damerau). SymSpell C# kaynağının varsayılanı da `DamerauOSA` (`SymSpell/SymSpell.cs`: `defaultMaxEditDistance = 2`, `defaultPrefixLength = 7`, `distanceAlgorithm = DamerauOSA`) [DOĞRULANDI].
- Gerçek (kısıtsız) Damerau-Levenshtein'ın OSA'dan farkı yalnızca "aynı alt dizgiyi iki kez düzenleme" durumlarında ortaya çıkar (ör. `ca → abc`). Adres adlarında pratik bir fark beklenmez.
- **OSA metrik değildir** (üçgen eşitsizliğini sağlamaz). BK-tree gibi metrik indeks kullanılacaksa Levenshtein veya tam DL seçilmeli. SymSpell bunu gerektirmez.

**Eşikler (katlanmış ve boşluksuz uzunluk L'ye göre):**

| L | Kapsam yok (global) | Kapsamlı (ebeveyn biliniyor) | Ek kural |
|---|---|---|---|
| ≤ 3 | 0 | 0 | Kısa adlar asla fuzzy değil |
| 4–5 | 1 | 1 | İlk harf eşit olmalı (prefix = 1) |
| 6–10 | 1 | 2 | İlk 2 harften en az 1'i eşit |
| ≥ 11 | 2 | 2 | — |

Karşılaştırma için Elasticsearch `fuzziness: AUTO` 0–2 karakter → 0, 3–5 → 1, >5 → 2 kullanır [DOĞRULANMADI: ES dokümanı bu oturumda açılmadı]. Photon bunu `prefixLength` 1–2 ile kullanıyor [DOĞRULANDI]. Bizim tablo Türkçe adlarda ilk harf hatalarının nadir olduğu varsayımıyla biraz daha muhafazakâr [DOĞRULANMADI: hata korpusuyla ölçülmeli].

**İndeks stratejisi:**
- İl (81) ve ilçe (973): brute-force OSA. Erken sonlandırmalı (bant genişliği = maxDist) ve mikro-saniyeler sürer.
- Mahalle — kapsamlı: ilçe biliniyorsa en fazla 218 aday (tüm tipler dahil 675), il biliniyorsa en fazla ≈1.468. Brute-force bantlı OSA yeterli.
- Mahalle — global (ebeveyn yok): SymSpell. Yaklaşık 32k farklı katlanmış ad, maxEdit 2, prefixLength 7. Bellek kullanımı birkaç on MB mertebesinde olabilir [DOĞRULANMADI: ölçülmeli]. Alternatif olarak global fuzzy yalnızca maxEdit 1 ile yapılır, 2'ye yalnızca kapsamlı aramada izin verilir.
- Çok-token'lı adlar: önce token token trie ile denenir. Tek token'da fuzzy olursa toplam mesafe birikimli hesaplanır. Ayrıca boşluksuz anahtarla tek dizgi olarak denenir ("ismetpasa").

**Türkçe klavye-komşuluğu ağırlıklı mesafe — değer mi?**
- Mevcut kanıt: Türkçe yazım hatalarının en büyük sınıfı diakritik hataları (Oflazer & Güzey: değiştirmelerin %34'ü ş-s, ç-c, ı-i, ö-o, ü-u çiftleri; tweet çalışmasında hataların %44,94'ü). Bunlar **katlama ile sıfır maliyetli**. Kalan hatalar ekleme, silme ve yer değiştirme. Klavye-komşuluğu konusunda Türkçe'ye özgü bir çalışma bulunamadı.
- Öneri: **MVP'de yok.** İkinci aşamada gerçek hata günlüğü (düzeltme günlüğündeki fuzzy kayıtları) toplanınca şunlar denenir: (1) klavye-komşu değiştirme maliyeti 0.5, (2) çift harf ekleme/silme maliyeti 0.5 ("mahhallesi", "sokk"), (3) sesli harf silme ("mhalle") 0.7. Bunlar yalnızca **aynı tam-sayı mesafedeki adaylar arasında eşitlik bozucu** olarak kullanılır, eşiği değiştirmez. Kabul kriteri dev setinde fuzzy top-1 doğruluğunun en az +1 puan artmasıdır. Türkçe-Q düzeninde `ı` ve `i` tuşlarının farklı yerde olduğu da hesaba katılmalı [DOĞRULANMADI].

---

## 8. Güven: türetme ve kalibrasyon (K)

### 8.1 Başka sistemler nasıl veriyor?
- **pelias/parser:** çözüm başına `score` döner. Bu kapsama × güven × (1 − ceza) ile hesaplanır ve kalibre değildir. Sınıflandırma bazında güven çıktıda yok.
- **pelias/api:** `confidence` (z-skor + ad/adres eşleşmesi ortalaması, deal-breaker → 0.5) ve `match_type` (exact/fallback) döner. Sabit katman çarpanları var. Kalibre değil [DOĞRULANDI].
- **Nominatim:** dahili olarak ceza/`accuracy`, `ranking = accuracy − importance` kullanır. Dışarıya güven değil `importance` verir [çıktı alanları DOĞRULANMADI].
- **libpostal:** parser yanıtında skor yok [DOĞRULANDI: `libpostal.h`].
- **Photon:** yeniden sıralama skoru dahili. Çıktıda güven alanı olduğu doğrulanmadı.
- Sonuç: hazır bir "kalibre güven" örneği yok. Bunu biz tanımlayacağız.

### 8.2 Ham güvenin türetilmesi
1. **N-best softmax:** `p(A_k) = exp(S_k / T) / Σ_j exp(S_j / T)`. Bu en iyi 10 aday ve NULL-dolu bir "hiçbiri" adayı üzerinden hesaplanır (NULL adayının skoru = sabit `S_null`, öğrenilir).
2. **Alan bazında marjinal:** `p(alan = v) = Σ_{k: A_k.alan = v} p(A_k)`. Örneğin top-3 atamanın üçü de mahalle = Caferağa diyorsa mahalle güveni yüksek olur, kapı no ayrışıyorsa kapı no güveni düşük olur.
3. **Genel güven:** `p(A_1)` (tüm alanların birlikte doğru olma olasılığı). Ayrıca kullanıcıya "en zayıf kritik alan"ın marjinali de verilebilir.
4. Sıcaklık `T` dev setinde NLL minimize edilerek tek parametre olarak öğrenilir (temperature scaling; Guo vd., 2017) [DOĞRULANMADI: makale bu oturumda açılmadı].

### 8.3 Kalibrasyon
Etiket: alan bazında "doğru mu" (0/1). Kalibratör ham marjinal `q`'yu `P(doğru | q)`'ya eşler.

| Yöntem | Ne zaman | Not |
|---|---|---|
| **Histogram binning** | İlk prototip, <300 örnek | 10–15 eşit-frekanslı kutu |
| **Platt (lojistik, 2 parametre)** | 300–1.000 örnek | Monoton sigmoid. Hedef yumuşatma `(N₊+1)/(N₊+2)`, `1/(N₋+2)` (Platt 1999) |
| **İzotonik (PAV)** | ≥ ~1.000 örnek / alan | Monoton parça-sabit. Az veride aşırı uyum riski var (scikit-learn kılavuzu) [DOĞRULANMADI: sayı eşiği kaba kural] |

Değerlendirme metrikleri: **ECE** (10 kutu), **Brier skoru**, güvenilirlik diyagramı. Ayrıca "güven ≥ 0.9 olanlarda doğruluk" ve kapsama oranı raporlanır. Bu, eşik tabanlı otomatik kabul için kritik.

Veri: (1) gazetteer'den **sentetik** üretilmiş adresler. Bunlara gürültü eklenir: katlama, büyük harf, kısaltma, bitişik yazım, yer değiştirme, typo, telefon, landmark. (2) **Gerçek** anonim adreslerden 300–1.000 adet elle etiketlenmiş küme. Kalibratör **mutlaka gerçek küme üzerinde** fit edilmeli. Sentetik veri ağırlık öğrenimi içindir.

Kalibratör JSON olarak paketlenir: alan başına `[(eşik, olasılık)]` kırılma noktaları. Çalışma zamanında ikili arama ile uygulanır. Bağımlılık gerekmez.

---

## 9. Sözde kod (C# tadında)

```csharp
// ---------- 0. Giriş noktası ----------
public ParseResult Parse(string input)
{
    var nz   = Normalizer.Run(input);                // Norm, Fold, OffsetMap, NoiseRanges, Corrections
    var toks = Tokenizer.Run(nz);                     // Token[] (ön-bölmeler uygulanmış)
    var lat  = Lattice.Build(toks, nz);               // kenarlar: tekil, birleşik, bölünmüş alternatifler
    Classifier.Run(lat, toks, _gaz, _lex);            // her kenara Hyp[] ekler
    var structural = StructuralBeam.Solve(lat, toks.Length, beam: 32, topK: 16);
    var nbest = new List<Scored>();
    foreach (var a in structural)
        foreach (var r in AdminResolver.Resolve(a, _gaz, beam: 16, topK: 4))
            nbest.Add(Scorer.Score(a, r, toks));
    nbest = Dedup(nbest).OrderByDescending(x => x.S).Take(10).ToList();
    return Confidence.Build(nbest, _calib);           // alan güvenleri, match_type, alternatifler, düzeltmeler
}

// ---------- 1. Normalizasyon ----------
static class Normalizer
{
    public static Normalized Run(string s)
    {
        s = s.Normalize(NormalizationForm.FormC);
        var sb = new StringBuilder(s.Length); var map = new List<int>(s.Length); var corr = new List<Correction>();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\u0307') { corr.Add(Correction.CombiningDot(i)); continue; }  // birleşik nokta
            c = Mojibake.TryFix(c, s, i, corr);                                       // Ý→İ, þ→ş ...
            c = c switch { '\\' => '/', '–' or '—' => '-', _ => c };
            sb.Append(TrLower(c)); map.Add(i);
        }
        string norm = sb.ToString();
        string fold = Fold(norm);                    // 1:1, aynı uzunluk
        var noise = NoisePatterns.Find(fold);        // telefon, e-posta, "tel:" (aralıklar)
        return new(input: s, norm, fold, map.ToArray(), noise, corr);
    }
    static char TrLower(char c) => c switch { 'I' => 'ı', 'İ' => 'i', _ => char.ToLowerInvariant(c) };
    public static string Fold(string t) => string.Create(t.Length, t, (span, src) => {
        for (int i = 0; i < src.Length; i++) span[i] = src[i] switch {
            'ç'=>'c','ğ'=>'g','ı'=>'i','ö'=>'o','ş'=>'s','ü'=>'u','â'=>'a','î'=>'i','û'=>'u', var x => x };
    });
}

// ---------- 2. Tokenizasyon + kafes ----------
static class Tokenizer
{
    public static Token[] Run(Normalized nz)
    {
        var raw = Scan(nz);                          // harf/rakam dizileri + BreakBefore + FinalPeriod/Ordinal/Apostrophe
        var outp = new List<Token>();
        foreach (var t in raw)
        {
            if (nz.IsNoise(t)) { outp.Add(t with { Kind = TokKind.Sym }); continue; }
            foreach (var piece in SplitAlphaNumeric(t))  // "147sok"→147|sok ; "17a" bölünmez
                outp.Add(piece);                         // bölme parçası BreakBefore = Glued
        }
        return Reindex(outp);
    }
}

static class Lattice
{
    public static Lat Build(Token[] t, Normalized nz)
    {
        var lat = new Lat(t.Length);
        for (int i = 0; i < t.Length; i++) lat.AddEdge(i, i + 1, t[i].Fold);           // tekil kenarlar
        for (int i = 0; i + 1 < t.Length; i++)                                          // kırık İ
            if ((t[i].Fold is "i") && t[i + 1].Kind == TokKind.Alpha && t[i + 1].BreakBefore == BreakKind.Space)
                lat.AddEdge(i, i + 2, "i" + t[i + 1].Fold, penalty: 0.2f, Correction.MergeI(t[i], t[i + 1]));
        for (int i = 0; i < t.Length; i++)                                              // bitişik anahtar kelime soneki
            foreach (var (stem, kw) in KeywordSuffixTrie.Splits(t[i].Fold, minStem: 3))
                lat.AddSplitEdge(i, stem, kw, penalty: 0.4f);                           // ara düğümle iki alt kenar
        return lat;
    }
}

// ---------- 3. Sınıflandırma ----------
static class Classifier
{
    const int MaxSpan = 7, MaxHypPerRole = 8;

    public static void Run(Lat lat, Token[] t, Gazetteer gaz, Lexicon lex)
    {
        PatternTagger.Run(lat, t);                   // KAPI, KAT, DAIRE, BLOK, POSTA, NUMSTR, ORDSTR, NOISE
        lex.TagKeywords(lat, t);                     // mah/cad/sk/köyü/ilçesi/landmark... (Keyword hipotezleri)

        for (int i = 0; i < lat.NodeCount - 1; i++)  // trie yürüyüşü: O(n * derinlik)
        {
            var node = gaz.TokenTrie.Root;
            for (int j = i; j < Math.Min(lat.NodeCount - 1, i + MaxSpan); j++)
            {
                if (!lat.TryGetUnitEdge(j, out var e) || !node.TryChild(e.Fold, out node)) break;
                foreach (var entry in node.Terminals)  // (Level, ids, freq)
                    lat.Add(i, j + 1, Hyp.FromGaz(entry, exact: true));
            }
            for (int j = i + 2; j <= Math.Min(lat.NodeCount - 1, i + 3); j++)   // boşluksuz anahtar
                if (gaz.Despaced.TryGetValue(lat.Concat(i, j), out var ents))
                    foreach (var en in ents) lat.Add(i, j, Hyp.FromGaz(en, exact: true, Correction.Despace(i, j)));
        }

        foreach (var (i, j) in lat.SpansWithoutExactAdmin(maxSpan: 3))   // fuzzy: yalnız eşleşmeyen ve L >= 4
        {
            var key = lat.Concat(i, j); int L = key.Length;
            int d = Fuzzy.MaxDist(L, scoped: false);
            if (d == 0) continue;
            foreach (var m in gaz.GlobalFuzzy(key, d, prefix: L <= 5 ? 1 : 0))   // il/ilçe brute, mahalle SymSpell
                lat.Add(i, j, Hyp.FromGaz(m.Entry, exact: false, dist: m.Dist));
        }

        Composite.Run(lat, t, Schemes.Turkish);      // [Ad][kw:mahallesi] → Mahalle(kw_support) vb.
        lat.PruneTopKPerRole(MaxHypPerRole);
    }
}

// ---------- 4a. Yapısal beam ----------
readonly record struct State(int Pos, uint Used, byte LastRank, sbyte Dir, ulong AdminSig, float S, Back? Back);

static class StructuralBeam
{
    public static List<Assignment> Solve(Lat lat, int n, int beam, int topK)
    {
        var frontier = new Dictionary<int, BeamBucket> { [0] = BeamBucket.Single(State.Initial) };
        float bestFinal = float.NegativeInfinity;

        for (int pos = 0; pos < n; pos++)
        {
            if (!frontier.TryGetValue(pos, out var bucket)) continue;
            foreach (var st in bucket.Top(beam))
            {
                if (st.S < bestFinal - 1.5f) continue;                       // Nominatim tarzı erken kesme

                Push(frontier, Skip(st, lat.Token(pos)));                     // (a) atla: unk_alpha/unk_num cezası

                foreach (var e in lat.EdgesFrom(pos))                         // (b) hipotez al
                foreach (var h in e.Hyps)
                {
                    if (!Grammar.Allows(st, h)) continue;                     // rol tekilliği, cadde+sokak istisnası, Repeat
                    var sig = AdminSig.Combine(st.AdminSig, h);               // il/ilçe/mahalle kimlik kesişimi
                    if (sig == AdminSig.Empty) continue;                      // stated seviyeler tutarsız → budama
                    float delta = Features.Local(h)                           // cov_conf payı, kw_support/conflict, fuzzy_cost, mah_type, ambig
                                + Order.Transition(st.LastRank, st.Dir, h.Role, out var newDir, out var newRank)
                                + Breaks.Penalty(lat, e);
                    Push(frontier, st with { Pos = e.To, Used = st.Used | h.Role.Bit(), LastRank = newRank,
                                             Dir = newDir, AdminSig = sig, S = st.S + delta, Back = new(st, h) });
                }
            }
            if (frontier.TryGetValue(n, out var fin)) bestFinal = fin.BestScore;
        }
        return frontier[n].Top(topK).Select(Backtrack).ToList();
    }

    // Push: (Pos, Used, LastRank, Dir, AdminSig) anahtarıyla birleştirir; aynı anahtarda en iyi S kalır.
}

// ---------- 4b. İdari hiyerarşik beam ----------
static class AdminResolver
{
    public static IEnumerable<AdminResolution> Resolve(Assignment a, Gazetteer g, int beam, int topK)
    {
        var ilC = new List<Cand<Il>> { Cand<Il>.Null };
        ilC.AddRange(a.IlHyps.SelectMany(h => h.EntityIds.Select(id => Cand.Stated(g.Il[id], h))));
        if (a.PostaKodu is { } pk) ilC.Add(Cand.FromPostcode(g.IlByPlate(pk[..2])));
        ilC.AddRange(a.IlceHyps.SelectMany(h => h.EntityIds.Select(id => Cand.Inferred(g.Ilce[id].Il))));
        ilC = MergeSameEntity(ilC).OrderByDescending(c => c.S).Take(5).ToList();

        var level2 = new List<(Cand<Il>, Cand<Ilce>)>();
        foreach (var il in ilC)
        {
            var ilceC = new List<Cand<Ilce>> { Cand<Ilce>.Null };
            ilceC.AddRange(a.IlceHyps.SelectMany(h => h.EntityIds)
                .Where(id => il.IsNull || g.Ilce[id].IlId == il.Id)
                .Select(id => Cand.Stated(g.Ilce[id])));
            if (a.HasMerkezToken && !il.IsNull) ilceC.Add(Cand.MerkezRule(g, il));     // yoksa Correction(MerkezIlceYok)
            if (a.Semt is { } s) ilceC.AddRange(g.SemtIlceler(s, il));
            ilceC.AddRange(a.MahHyps.SelectMany(h => h.EntityIds)
                .Select(id => g.Mah[id].IlceId)
                .Where(id => il.IsNull || g.Ilce[id].IlId == il.Id)
                .Distinct()
                .Select(id => Cand.Inferred(g.Ilce[id])));
            foreach (var ic in MergeSameEntity(ilceC).OrderByDescending(c => c.S).Take(8))
                level2.Add((il, ic));
        }
        level2 = level2.OrderByDescending(p => p.Item1.S + p.Item2.S + Consistency(p)).Take(beam).ToList();

        var triples = new List<AdminResolution>();
        foreach (var (il, ic) in level2)
        {
            var mahC = new List<Cand<Mah>> { Cand<Mah>.Null };
            mahC.AddRange(a.MahHyps.SelectMany(h => h.EntityIds)
                .Where(id => ic.IsNull ? (il.IsNull || g.Mah[id].IlId == il.Id) : g.Mah[id].IlceId == ic.Id)
                .Select(id => Cand.Stated(g.Mah[id])));
            if (!ic.IsNull && a.UnresolvedMahSpan is { } span)                         // kapsamlı fuzzy (≤218 aday)
                mahC.AddRange(g.ScopedFuzzyMah(ic.Id, span.Key, Fuzzy.MaxDist(span.Key.Length, scoped: true)));
            foreach (var m in mahC.OrderByDescending(c => c.S).Take(10))
                triples.Add(AdminResolution.Of(il, ic, m, a));                         // Inferred doldurma + ambig + pk_agree
        }
        return triples.OrderByDescending(t => t.S).Take(topK);
    }
}

// ---------- 5. Skor ----------
static class Scorer
{
    public static Scored Score(Assignment a, AdminResolution r, Token[] t)
    {
        var f = new FeatureVector();
        f[F.CovConf]      = a.Spans.Sum(s => s.Local * s.Chars) / t.Where(x => x.Kind != TokKind.Sym).Sum(x => x.Len);
        f[F.UnkAlpha]     = a.Unclassified.Count(x => x.Kind == TokKind.Alpha);
        f[F.UnkNum]       = a.Unclassified.Count(x => x.Kind is TokKind.Num or TokKind.AlphaNum);
        f[F.OrderInv]     = a.Inversions;
        f[F.AdminRev]     = a.Dir < 0 ? 1 : 0;
        f[F.KwSupport]    = a.Spans.Count(s => s.F.KeywordSupported);
        f[F.KwConflict]   = a.Spans.Count(s => s.F.KeywordConflict);
        f[F.HnOrphan]     = a.Has(Role.KapiNo) && !a.HasAny(Role.Cadde, Role.Sokak, Role.Mahalle, Role.Koy) ? 1 : 0;
        f[F.IlMatch]      = r.Il.Stated ? 1 - 0.5f * r.Il.Dist : 0;
        f[F.IlceCons]     = r.Ilce.Stated && r.Consistent(Level.Il, Level.Ilce) ? 1 : 0;
        f[F.MahCons]      = r.Mah.Stated && r.Consistent(Level.Ilce, Level.Mah) ? 1 : 0;
        f[F.Inferred]     = r.InferredLevels;
        f[F.PkAgree]      = r.PostcodeAgrees ? 1 : 0;
        f[F.PkConflict]   = r.PostcodeConflicts ? 1 : 0;
        f[F.Ambig]        = -r.Log2CandidatesInScope;
        f[F.MahType]      = MahTypePrior(r.Mah);
        f[F.FuzzyCost]    = a.Spans.Sum(s => s.F.EditDist / (float)Math.Max(1, s.F.Len)) + r.FuzzyCost;
        f[F.AliasUsed]    = a.Corrections.Count(c => c.IsAlias) + r.AliasCount;
        f[F.ShortNoCtx]   = r.ShortNameWithoutContext ? 1 : 0;
        return new Scored(a, r, S: Weights.Dot(f) + a.BreakPenalty, f);
    }
}

// ---------- 6. Güven + kalibrasyon ----------
static class Confidence
{
    public static ParseResult Build(List<Scored> nbest, Calibrators cal)
    {
        float T = cal.Temperature, sNull = cal.NullScore;
        var z = nbest.Select(x => MathF.Exp(x.S / T)).Append(MathF.Exp(sNull / T)).ToArray();
        float Z = z.Sum();
        var best = nbest[0]; var fields = new Dictionary<Field, FieldOut>();
        foreach (var fld in Fields.All)
        {
            var v = best.Value(fld);
            float q = 0; for (int k = 0; k < nbest.Count; k++) if (Equals(nbest[k].Value(fld), v)) q += z[k] / Z;
            fields[fld] = new(v, Conf: cal[fld].Apply(q), MatchType: best.MatchType(fld), Span: best.Offsets(fld));
        }
        return new ParseResult(fields, Overall: cal[Field.Overall].Apply(z[0] / Z),
                               Alternatives: nbest.Skip(1).Take(3), Corrections: best.AllCorrections());
    }
}

// İzotonik regresyon — Pool Adjacent Violators (PAV), bağımlılıksız
public sealed class Isotonic
{
    double[] _x = [], _y = [];                       // kırılma noktaları (artan x, azalmayan y)
    public void Fit(double[] score, int[] label)
    {
        var idx = Enumerable.Range(0, score.Length).OrderBy(i => score[i]).ToArray();
        var vals = new List<double>(); var wts = new List<double>(); var xs = new List<double>();
        foreach (var i in idx)
        {
            vals.Add(label[i]); wts.Add(1); xs.Add(score[i]);
            while (vals.Count > 1 && vals[^2] > vals[^1])            // ihlal: birleştir
            {
                double w = wts[^1] + wts[^2];
                double v = (vals[^1] * wts[^1] + vals[^2] * wts[^2]) / w;
                vals.RemoveAt(vals.Count - 1); wts.RemoveAt(wts.Count - 1); xs.RemoveAt(xs.Count - 1);
                vals[^1] = v; wts[^1] = w;                            // blok başlangıç x'i korunur
            }
        }
        _x = xs.ToArray(); _y = vals.ToArray();
    }
    public double Apply(double s)                                     // blok basamak fonksiyonu
    {
        if (_x.Length == 0) return s;
        int i = Array.BinarySearch(_x, s); if (i < 0) i = ~i - 1;
        return _y[Math.Clamp(i, 0, _y.Length - 1)];
    }
}

// Platt ölçekleme — 2 parametreli lojistik, Newton yöntemi (Platt 1999, yumuşatılmış hedefler)
public sealed class Platt
{
    public double A = -1, B = 0;                                       // p = 1 / (1 + exp(A*s + B))
    public void Fit(double[] s, int[] y, int iters = 100)
    {
        double nPos = y.Count(v => v == 1), nNeg = y.Length - nPos;
        double tPos = (nPos + 1) / (nPos + 2), tNeg = 1 / (nNeg + 2);
        for (int it = 0; it < iters; it++)
        {
            double gA = 0, gB = 0, hAA = 1e-12, hAB = 0, hBB = 1e-12;
            for (int i = 0; i < s.Length; i++)
            {
                double p = 1 / (1 + Math.Exp(A * s[i] + B)), t = y[i] == 1 ? tPos : tNeg;
                double d = t - p, w = p * (1 - p);                       // dL/dz = t - p ; z = A*s + B
                gA += d * s[i]; gB += d; hAA += w * s[i] * s[i]; hAB += w * s[i]; hBB += w;
            }
            double det = hAA * hBB - hAB * hAB; if (Math.Abs(det) < 1e-12) break;
            double dA = (hBB * gA - hAB * gB) / det, dB = (-hAB * gA + hAA * gB) / det;
            A -= dA; B -= dB; if (Math.Abs(dA) + Math.Abs(dB) < 1e-9) break;
        }
    }
    public double Apply(double s) => 1 / (1 + Math.Exp(A * s + B));
}
```

> Not: Sözde kod tasarımı aktarmak içindir. Tipler, imzalar ve işaret kuralları (ör. Platt Newton adımının işareti) birim testlerle doğrulanmalı.

---

## 10. Snapshot test vakaları

Gösterim: `il / ilçe / mah` (gazetteer'e çözülen), `semt`, `cad` (cadde/bulvar), `sk` (sokak), `site`, `blok`, `no` (dış kapı), `kat`, `d` (daire), `pk` (posta kodu), `lm` (landmark). Ayrıca `corr` (beklenen düzeltme kayıtları), `conf` (beklenen güven seviyesi: Y = yüksek ≥0.85, O = orta, D = düşük <0.5).
✓ = idari üçlü bu oturumda `data/raw/melihozkara` ile doğrulandı. ✗ = bilinçli olarak gazetteer'de olmayan ad (semt vb.). Sokak/cadde adları gazetteer'e bağlanmadığı için doğrulanmadı.

| # | Girdi | Beklenen | Fenomen |
|---|---|---|---|
| 1 | `Caferağa Mah. Moda Cad. No:12 D:3 Kadıköy/İstanbul` | il=İstanbul, ilçe=Kadıköy, mah=Caferağa ✓, cad=Moda, no=12, d=3; conf=Y | Temel, kanonik sıra |
| 2 | `CAFERAGA MAH MODA CAD NO 12 KADIKOY ISTANBUL` | #1 ile aynı (d yok); corr=[fold] | ASCII + büyük harf (`I→ı→i`) |
| 3 | `İstanbul Kadıköy Caferağa Mahallesi Moda Caddesi No 12` | #2 ile aynı; dir=−1 | Ters idari blok |
| 4 | `Caferağa Mah.,Moda Cad.,No:12,Kadıköy,İstanbul` | #2 ile aynı | Boşluksuz virgüller |
| 5 | `Moda Bahariye Cad. No:25/4 Kadıköy İstanbul` | il=İstanbul, ilçe=Kadıköy, semt=Moda ✗, mah=null (semt tablosu varsa Inferred, conf=D), cad=Bahariye, no=25, d=4 | Semt (gazetteer'de mahalle değil) |
| 6 | `Nişantaşı Abdi İpekçi Cad. No:40 Şişli İstanbul` | il=İstanbul, ilçe=Şişli, semt=Nişantaşı ✗, mah=null, cad=Abdi İpekçi, no=40 | Semt + çok-kelimeli cadde |
| 7 | `19 Mayıs Mah. Şemsettin Günaltay Cad. No:5 Kadıköy İstanbul` | mah=19 Mayıs ✓ (Kadıköy), cad=Şemsettin Günaltay, no=5 (19 ≠ kapı) | Sayıyla başlayan mahalle |
| 8 | `100. Yıl Mah. 1234. Sok. No:3 Bağcılar İstanbul` | mah=100. Yıl ✓ (Bağcılar), sk=1234, no=3 | Ordinal mahalle + ordinal sokak |
| 9 | `2000 Evler Mah. Seyhan Adana` | il=Adana, ilçe=Seyhan, mah=2000 Evler ✓ | Rakamlı ad, "Evler" çakışması |
| 10 | `Kazımdirik Mah. 372 Sok. No:5 K:2 D:4 Bornova İzmir` | mah=Kazımdirik ✓, sk=372, no=5, kat=2, d=4 | Numaralı sokak, K:/D: |
| 11 | `Atatürk Mah. 1203/5 Sk. No:7 Bornova/İzmir` | mah=Atatürk ✓ (Bornova), sk=1203/5, no=7 | Slash'lı sokak adı |
| 12 | `Alsancak Mah. 1453 sokak no 12 Konak İzmir` | mah=Alsancak ✓ (Konak), sk=1453, no=12 | Kısaltmasız numaralı sokak |
| 13 | `Atilla Mah. 127 nolu sokak no:3 Konak İzmir` | mah=Atilla ✓ (Konak), sk=127, no=3 | "nolu sokak" |
| 14 | `atillamahallesi475sok no:3 konak izmir` | mah=Atilla ✓, sk=475, no=3; corr=[split(num), split(kw)] | Çift bitişik |
| 15 | `Bostanlı mah. 1820sk. no5 karşıyaka izmir` | mah=Bostanlı ✓ (Karşıyaka), sk=1820, no=5; corr=[split×2] | `1820sk`, `no5` |
| 16 | `147sok no:5 d:2 Mavişehir Karşıyaka İzmir` | mah=Mavişehir ✓, sk=147, no=5, d=2; order_inv>0 | Mahalle sokaktan sonra |
| 17 | `Kızılay Mah. Atatürk Bulvarı No:125/7 Çankaya/Ankara` | mah=Kızılay ✓ (Çankaya), cad(bulvar)=Atatürk, no=125, d=7 | Bulvar, No:x/y |
| 18 | `Çankaya Mah. Çankaya Ankara` | il=Ankara, ilçe=Çankaya, mah=Çankaya ✓ | Mahalle = kendi ilçe adı |
| 19 | `Konak Konak İzmir` | il=İzmir, ilçe=Konak, mah=Konak ✓; conf=O | Anahtar kelimesiz tekrar → hiyerarşik yorum |
| 20 | `Yenişehir Mah. Yenişehir Diyarbakır` | il=Diyarbakır, ilçe=Yenişehir, mah=Yenişehir ✓ | Mahalle = ilçe |
| 21 | `Bahçelievler İstanbul` | il=İstanbul, ilçe=Bahçelievler, mah=null (alt: mah=Bahçelievler) | Tek geçiş → idari kuyrukta ilçe |
| 22 | `Bahçelievler Mah. 34180` | il=İstanbul (pk, Inferred), mah=Bahçelievler ✓ (İstanbul'da 4 ilçede var), ilçe=null/çok aday; conf(ilçe)=D | Posta koduyla il çıkarımı, ilçe belirsiz |
| 23 | `Yenişehir` | ilçe aday ×3 (Bursa, Diyarbakır, Mersin) + mahalle adayları; il=null; conf=D | Saf belirsizlik |
| 24 | `Yenişehir Bursa` | il=Bursa, ilçe=Yenişehir | İl ile belirginleşme |
| 25 | `Fatih Mah. Arifiye Sakarya` | il=Sakarya, ilçe=Arifiye, mah=Fatih ✓ (Fatih ilçesi değil) | Yaygın mahalle = başka ildeki ilçe adı |
| 26 | `Fatih İstanbul` | il=İstanbul, ilçe=Fatih | Aynı ad, ilçe yorumu |
| 27 | `Kurtköy Fatih Mah. Sapanca Sakarya` | mah=Kurtköy Fatih ✓ (Sapanca) | Belde önekli mahalle adı |
| 28 | `Fatih Mah. Sapanca` | il=Sakarya (Inferred), ilçe=Sapanca, mah=Kurtköy Fatih (kısmi, conf=D) veya null | Kısmi ad eşleşmesi |
| 29 | `Tabaklar Mah. Merkez/Bolu` | il=Bolu, ilçe=Merkez, mah=Tabaklar ✓ | "Merkez" ilçe |
| 30 | `Cumhuriyet Mah. Merkez Kayseri` | il=Kayseri, ilçe=null (7 aday), mah=Cumhuriyet (ilçe belirsiz); corr=[merkez_ilce_yok]; conf=D | Büyükşehirde "Merkez" yok |
| 31 | `Karaburun Köyü Arnavutköy İstanbul` | il=İstanbul, ilçe=Arnavutköy, mah=Karaburun ✓; corr=[koy_to_mahalle] | Eski köy → mahalle |
| 32 | `Akçaalan Köyü Küme Evler No:12 Merkez Bolu` | il=Bolu, ilçe=Merkez, köy=Akçaalan ✓, sk(küme)=Küme Evler, no=12 | Köy + küme evler |
| 33 | `Kepçeli Köyü Kuruca Küme Evleri No:4 Bingöl` | il=Bingöl, ilçe=Genç (Inferred) ✓, köy=Kepçeli, mevkii=Kuruca Küme Evleri, no=4 | Mevkii seviyesinde küme evleri, ilçe çıkarımı |
| 34 | `i smet paşa mah. kuluncak malatya` | il=Malatya, ilçe=Kuluncak, mah=İsmetpaşa ✓; corr=[merge_i, despace] | Kırık İ |
| 35 | `İSMETİYE MAH. BATTALGAZİ / MALATYA` | il=Malatya, ilçe=Battalgazi, mah=İsmetiye ✓ | Büyük harf İ, `/` ayraç |
| 36 | `Kadikoy Istnbul` | il=İstanbul (fuzzy d=1), ilçe=Kadıköy; corr=[fuzzy] | Fuzzy il |
| 37 | `Kadıköy'de Fenerbahçe Mah.` | il=İstanbul (Inferred), ilçe=Kadıköy, mah=Fenerbahçe ✓; corr=[apostrophe] | Kesme işareti eki |
| 38 | `Barbaros Mah. Begonya Sok. Gül Sitesi A Blok K:12 D:48 Ataşehir İstanbul` | mah=Barbaros ✓ (Ataşehir), sk=Begonya, site=Gül Sitesi, blok=A, kat=12, d=48 | Site + blok |
| 39 | `Etiler Mah. Nispetiye Cad. Blok B2 Daire 5 Beşiktaş` | il=İstanbul (Inferred), ilçe=Beşiktaş, mah=Etiler ✓, cad=Nispetiye, blok=B2, d=5 | Önde "Blok" |
| 40 | `Bağdat Cad. Kazım Özalp Sok. No:7 Bostancı Kadıköy İstanbul` | cad=Bağdat, sk=Kazım Özalp, no=7, mah=Bostancı ✓ (Kadıköy) | Cadde + sokak; mahalle sonda |
| 41 | `Mecidiyeköy Mah. Büyükdere Cad. No:10 Kat:3 Şişli İstanbul Tel: 0532 123 45 67` | mah=Mecidiyeköy ✓, cad=Büyükdere, no=10, kat=3; telefon=Noise | Telefon gürültüsü |
| 42 | `05321234567 Emek Mah. 7. Cad. No:3 Çankaya Ankara` | mah=Emek ✓ (Çankaya), cad=7, no=3; telefon=Noise | Bitişik telefon, ordinal cadde |
| 43 | `Migros karşısı, Kocatepe Mah. Mithatpaşa Cad. No:20 Çankaya/Ankara` | lm="Migros karşısı", mah=Kocatepe ✓, cad=Mithatpaşa, no=20 | Landmark başta |
| 44 | `Güzelyalı Mah. Mithatpaşa Cad. No:5 Kat 2 Konak İzmir (Eczane yanı)` | mah=Güzelyalı ✓ (Konak), cad=Mithatpaşa, no=5, kat=2, lm="Eczane yanı" | Parantez içi landmark |
| 45 | `Kadıköy Belediyesi arkası Caferağa Mah.` | lm="Kadıköy Belediyesi arkası", mah=Caferağa ✓, ilçe=Kadıköy (Inferred), il=İstanbul (Inferred) | Landmark içi idari ad (zayıf kanıt) |
| 46 | `Hürriyet 1. OSB Mah. 3. Cad. No:12 Merkez Bilecik` | il=Bilecik, ilçe=Merkez, mah=Hürriyet 1.OSB ✓, cad=3, no=12 | OSB + ordinal |
| 47 | `Demirciler Organize Sanayi Bölgesi Gerede Bolu` | il=Bolu, ilçe=Gerede, mah=DemircilerOSB ✓; corr=[osb_expansion, despace] | OSB eşanlamı + bitişik gazetteer adı |
| 48 | `Caferağa Mah. Caferağa Mah. Moda Cad. No 12 Kadıköy` | mah=Caferağa ✓, cad=Moda, no=12, ilçe=Kadıköy, il=İstanbul (Inferred); corr=[duplicate] | Tekrar eden span |
| 49 | `No:17/A Kat:Zemin Caferağa Mah. Kadıköy İstanbul` | no=17/A, kat=0 (zemin), mah=Caferağa ✓; order_inv>0 | Harfli kapı, zemin kat, ters sıra |
| 50 | `Bostancı mah. 17 A blok kat 3 daire 5 Kadıköy` | mah=Bostancı ✓, no=17, blok=A, kat=3, d=5 | "17 A blok" (17A değil) |
| 51 | `Kuzey Sok. No:3 K.2 D.6 Fenerbahçe Mah. Kadıköy` | sk=Kuzey (yön değil ad), no=3, kat=2, d=6, mah=Fenerbahçe ✓ | D/K vs yön |
| 52 | `D Blok K 2 D 5 Emek Mah. Çankaya Ankara` | blok=D, kat=2, d=5, mah=Emek ✓ | Aynı harf üç rolde |
| 53 | `Atatürk Cad. No: 5-7 Kızılay Mah. Çankaya Ankara` | cad=Atatürk, no=5-7, mah=Kızılay ✓ | Kapı aralığı |
| 54 | `34710 Kadıköy İstanbul` | pk=34710, ilçe=Kadıköy, il=İstanbul; pk_agree | Posta kodu uyumu (değer–ilçe eşleşmesi DOĞRULANMADI, yalnız 34 öneki) |
| 55 | `06100 Kadıköy İstanbul` | il=İstanbul, ilçe=Kadıköy, pk=06100; corr=[postcode_conflict]; conf=O | Posta kodu çelişkisi |
| 56 | `Eyüp İstanbul` | il=İstanbul, ilçe=Eyüpsultan ✓; corr=[renamed_ilce] | Yeniden adlandırılmış ilçe |
| 57 | `Cumhuriyet` | mah adayları ≈402, il/ilçe=null; conf=D (çok düşük) | Aşırı belirsiz tek ad |
| 58 | `Lütfen kapıcıya bırakın. Etiler Mah. Beşiktaş` | mah=Etiler ✓, ilçe=Beşiktaş, il=İstanbul (Inferred); "Lütfen kapıcıya bırakın" = sınıflandırılmamış/not | Serbest metin gürültüsü |

Eklenmesi önerilen negatif/sınır vakaları (tablo dışı):
- Boş girdi.
- Yalnız telefon.
- 200+ karakter girdi (64 token kesmesi).
- Emoji ve kontrol karakterleri.
- Tamamı rakam (`34710`).
- Tamamı ASCII-katlanmış ve boşluksuz (`caferagamahmodacadno12kadikoyistanbul`). Beklenti: kısmi başarı, conf=D. Bu, sözlük-tabanlı kelime bölmenin (SymSpell `WordSegmentation` benzeri) gerekip gerekmediğini ölçmek için konur.

---

## 11. Faz 4 için uygulama sırası (öneri)

1. `Normalizer` + ofset haritası + gürültü maskeleri. Test 2, 34, 41, 42.
2. `Tokenizer` + ön-bölme + kafes. Test 14, 15, 16, 37.
3. `PatternTagger` + `KeywordLexicon` (libpostal tr sözlüklerinden üretilir ve genişletilir). Test 10–13, 49–53.
4. Gazetteer derleme. DataBuilder tarafında token trie, boşluksuz anahtarlar, parantez/OSB takma adları ve eski-ad tablosu üretilir. Test 7–9, 46, 47, 56.
5. `StructuralBeam` (önce idari uyum kontrolü olmadan, sonra eklenerek).
6. `AdminResolver` + NULL adaylar + çıkarım. Test 18–31.
7. `Scorer` (elle ağırlıklar) → sentetik dev seti üretici → ağırlık ayarı.
8. Fuzzy (kapsamlı, sonra global SymSpell).
9. Güven: softmax marjinali → histogram binning → gerçek veriyle Platt/izotonik. ECE raporu.
10. Semt tablosu ve posta kodu–ilçe tablosu (veri kaynağı ve lisans kararı sonrası).

---

## 12. Açık sorular / doğrulanmamış maddeler

- `docs/plan/PLAN.md` ve `ARASTIRMA.md` depoda bulunamadı. Bu tasarım, görev metnindeki MVP tanımına dayanıyor.
- Semt verisi (OSM/Wikidata) ve köy eski adları (Index Anatolicus) için lisans uygunluğu doğrulanmadı.
- TÜİK ADNKS mahalle nüfusu (pop_prior) için erişim ve lisans doğrulanmadı.
- PTT posta kodu → ilçe/mahalle eşlemesi için kaynak ve lisans doğrulanmadı.
- Mojibake tablosu ve telefon regex'i gerçek veriyle doğrulanmalı.
- Fuzzy eşik tablosu ve klavye-ağırlıklı maliyetin değeri gerçek hata korpusuyla ölçülmeli.
- İzotonik için "~1.000 örnek" eşiği kaba kural. scikit-learn kılavuzundaki ifade bu oturumda açılmadı.
- Performans tahminleri (< 1 ms/adres, SymSpell belleği) ölçülmedi.
- Nominatim ve Photon'un dış API çıktı alanları kaynak koddan değil genel bilgiden.
- libpostal sözlüklerinin lisansı (MIT olduğu düşünülüyor) LICENSE dosyasından doğrulanmadı.

## 13. Kaynaklar

Kaynak kodu (bu oturumda okundu):
- pelias/parser: `tokenization/Tokenizer.js`, `tokenization/Span.js`, `tokenization/permutate.js`, `tokenization/split.js`, `tokenization/split_funcs.js`, `tokenization/normalizer.js`, `parser/Parser.js`, `parser/AddressParser.js`, `solver/*.js` (Solution, ExclusiveCartesianSolver, HashMapSolver, LeadingAreaDeclassifier, MultiStreetSolver, InvalidSolutionFilter, MustNotFollowFilter, MustNotPreceedFilter, SubsetFilter, HouseNumberPositionPenalty, PostcodePositionPenalty, TokenDistanceFilter, OrphanedUnitTypeDeclassifier), `classifier/` (WhosOnFirst, HouseNumber, Postcode, StreetSuffix, Composite, scheme/street, Unit, UnitType, Intersection, TokenPosition, StopWord), `server/routes/parse.js` — <https://github.com/pelias/parser>
- pelias/api: `middleware/confidenceScore.js`, `middleware/confidenceScoreFallback.js` — <https://github.com/pelias/api>
- pelias/placeholder: `lib/permutations.js`, `lib/Result.js`, `prototype/tokenize.js`, `prototype/query.js` — <https://github.com/pelias/placeholder>
- Nominatim: `src/nominatim_api/search/token_assignment.py`, `query.py`, `icu_tokenizer.py`, `geocoder.py`, `db_search_builder.py` (grep), `src/nominatim_api/results.py` (grep) — <https://github.com/osm-search/Nominatim>
- libpostal: `src/address_parser.c` (özellik fonksiyonu), `src/libpostal.h`, `src/expand.c`, `src/near_dupe.c` (grep), `resources/dictionaries/tr/*.txt` — <https://github.com/openvenues/libpostal>
- Photon: `src/main/java/de/komoot/photon/opensearch/SearchQueryBuilder.java`, `searcher/QueryReranker.java` — <https://github.com/komoot/photon>
- CLAVIN (fork): `src/main/java/com/bericotech/clavin/resolver/ClavinLocationResolver.java` — <https://github.com/bigconnect/clavin>
- CLIFF: `common/src/main/java/org/mediacloud/cliff/places/disambiguation/` (yalnız dosya listesi) — <https://github.com/mediacloud/cliff-annotator>
- Mordecai3: `mordecai3/candidate_features.py` — <https://github.com/ahalterman/mordecai3>
- SymSpell (C#, MIT): `SymSpell/SymSpell.cs` (varsayılanlar) — <https://github.com/wolfgarbe/SymSpell>

Veri (bu oturumda sayıldı): `data/raw/melihozkara/il-*/ilceler.jsonl`, `mahalleler.jsonl`, `README.md` (kaynak: NVİ adres sorgu; 81 il / 973 ilçe / 73.398 mahalle-seviyesi kayıt / 1.276.922 sokak).

Makaleler ve dokümanlar (arama özeti; tam metin açılmadı):
- Oflazer & Güzey (1994), Spelling Correction in Agglutinative Languages — <https://arxiv.org/abs/cmp-lg/9410004>
- Ünal, Aygün, Gerek (2023), Türkçe adres ayrıştırma — <https://arxiv.org/abs/2306.13947>
- deprem-ml adres NER modeli — <https://huggingface.co/deprem-ml/adres_ner_v2_bert_128k>
- Kürklü & Akagündüz (2024), adres standardizasyonu — <https://open.metu.edu.tr/handle/11511/110721>
- Türkçe yazım düzeltme (Wikipedia düzenleme geçmişi), UW tezi — <https://digital.lib.washington.edu/researchworks/items/d927553a-9a4b-4881-8b03-f00bbe27f02f>
- Kalibrasyon (genel bilgi; bu oturumda açılmadı): Platt (1999) "Probabilistic Outputs for SVMs"; Zadrozny & Elkan (2002) "Transforming classifier scores into accurate multiclass probability estimates" (izotonik); Niculescu-Mizil & Caruana (2005) "Predicting Good Probabilities with Supervised Learning"; Guo vd. (2017) "On Calibration of Modern Neural Networks" (temperature scaling, ECE); scikit-learn Probability calibration kılavuzu — <https://scikit-learn.org/stable/modules/calibration.html>
- Elasticsearch `fuzziness: AUTO` (genel bilgi) — <https://www.elastic.co/guide/en/elasticsearch/reference/current/common-options.html#fuzziness>
