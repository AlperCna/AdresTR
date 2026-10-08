# AdresTR

**Türkçe serbest metin adresleri ayrıştıran, normalize eden ve doğrulayan .NET kütüphanesi.** → [English README](README.md)

**▶ [Tarayıcıda deneyin](https://alpercna.github.io/AdresTR/)**: ayrıştırma tamamen tarayıcınızda (WebAssembly) çalışır, adres hiçbir yere gönderilmez.

> 🚧 **Önizleme sürümü.** 1.0'dan önce API'ler değişebilir. Yol haritası ve arkasındaki araştırma açık: [plan](docs/plan/PLAN.md) · [araştırma](docs/plan/ARASTIRMA.md) · [kararlar (ADR)](docs/adr/).

## Neden?

Türkiye'de adresler serbest metin olarak yazılıyor:

```
kadikoy caferaga mh moda cd no:12 d3 istanbul
atillamahallesi475sokibrahimapartno20kat4daire4
İSTANBUL ŞİŞLİ MECİDİYEKÖY MAH. 1203/5 SK. NO:17/A
```

Bu karmaşa e-ticaret ve kargo şirketlerine her gün maliyet çıkarıyor. Hepsiburada bu konuda TEKNOFEST 2025'te bir hackathon bile düzenledi. Buna rağmen **ücretsiz, çevrimdışı ve açıklanabilir** bir Türkçe adres ayrıştırıcı yok:

- Google Address Validation API Türkiye'yi desteklemiyor.
- libpostal'da mahalle ve ilçe etiketi yok.
- Açık, etiketli bir Türkçe adres benchmark'ı yok.

## Hızlı başlangıç

```bash
dotnet add package AdresTR.Data --prerelease
```

```csharp
using AdresTR;
using AdresTR.Data;

ParseResult r = TurkishGazetteer.Parser.Parse("kadikoy caferaga mh moda cd no:12 d3 istanbul");
r.Unit;                 // Caferağa Mahallesi (kimlik 34230005); metin belirsizse null
r.ToCanonicalString();  // "Caferağa Mah. Moda Cad. No:12 D:3 34710 Kadıköy/İstanbul"
r.Corrections;          // kadikoy → Kadıköy (aksan), …
r.Confidence;           // kalibre edilmiş güven
```

- **Ayrıştırma:** il, ilçe, mahalle/köy, semt, cadde/sokak, site, blok, kapı no, kat, daire, posta kodu, tarif.
- **Çözümleme:** resmi adlar ve kararlı kimlikler. Semt (Moda → Caferağa), 2014 öncesi köy adları, kısaltmalar,
  yapışık yazım (`147sok`), yazım hataları ve ASCII yazım desteklenir.
- **Tahmin etmez:** ilçesiz "Cumhuriyet Mah." için birim döndürmez, sıralı adaylar verir.
- **Açıklar:** her düzeltmeyi ve kalibre edilmiş güveni döndürür. Çevrimdışı, ICU'suz, adres başına ~0,2–0,5 ms.

## Benchmark (test setleri)

| Set | AdresTR | libpostal | regex |
|---|---:|---:|---:|
| Sentetik (2.000) — tam eşleşme | **%97,4** | %10,0 | %14,0 |
| Gerçek kurum adresleri (1.200) — tam eşleşme | **%95,8** | %26,2 | %8,2 |
| Elle yazılmış zor vakalar (167) — tam eşleşme | **%86,8** | %24,6 | %21,0 |

Ayrıntılar: [eval/results](eval/results/README.md) · Metodoloji: [eval/README.md](eval/README.md)

## Lisans

Kod [MIT](LICENSE) lisanslı. Veri lisansları kaynak bazında [data/LICENSE-DATA.md](data/LICENSE-DATA.md) dosyasında listeleniyor.
