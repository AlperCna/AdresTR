# AdresTR

**Türkçe serbest metin adresleri ayrıştıran, normalize eden ve doğrulayan .NET kütüphanesi.** → [English README](README.md)

> 🚧 **Erken geliştirme aşamasında.** Yol haritası ve arkasındaki araştırma açık: [plan](docs/plan/PLAN.md) · [araştırma](docs/plan/ARASTIRMA.md) · [kararlar (ADR)](docs/adr/).

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

## Hedef (v1.0)

- **Ayrıştırma:** il, ilçe, mahalle/köy, semt, cadde/sokak (tür + ad), site, blok, kapı no, kat, daire, posta kodu.
- **Normalizasyon:** resmi adlara ve kararlı kimliklere çevirme. Semt adlarını (Moda → Caferağa) ve 2014 öncesi köy adlarını anlar.
- **Doğrulama:** il ⊃ ilçe ⊃ mahalle hiyerarşisi ve posta kodu tutarlılığı.
- **Açıklama:** her düzeltmenin logu, alan bazında güven skoru ve alternatifler.
- **Çevrimdışı ve deterministik**, ICU'ya bağımlı değil: container, Native AOT ve tarayıcıda (Blazor WASM) aynı sonucu verir.

## Şu an kullanılabilir olan

`AdresTR.Text.TurkishText`: kültürden bağımsız Türkçe metin işleme. "Türkçe I problemi" için [ADR-0002](docs/adr/0002-icu-independent-turkish-text.md)'ye bakın.

```csharp
TurkishText.Fold("  KADIKÖY’de\u00A0Şişli ");   // "kadikoy'de sisli"
TurkishText.ToUpperTr("istanbul");               // "İSTANBUL"
TurkishText.ToTitleTr("ığdır");                  // "Iğdır"
```

## Lisans

Kod [MIT](LICENSE) lisanslı. Veri lisansları kaynak bazında [data/LICENSE-DATA.md](data/LICENSE-DATA.md) dosyasında listeleniyor.
