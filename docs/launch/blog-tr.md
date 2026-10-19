# Türkçe adresleri neden kimse düzgün ayrıştıramıyor? (ve ben bir kütüphane yazdım)

`kadikoy caferaga mh moda cd no:12 d3 istanbul`

Bir insan için bu adres net. Bir bilgisayar için ise: "kadikoy" ilçe mi, mahalle mi? "mh" ne demek? "d3" daire mi, yön mü?
Hepsiburada'nın TEKNOFEST 2025'te bu konuda bir hackathon düzenlemesinin sebebi bu: yanlış adres, yanlış teslimat demek.

Bakınca şunu gördüm: **ücretsiz, çevrimdışı ve açıklanabilir** bir Türkçe adres ayrıştırıcı yok.

- Google'ın Address Validation API'si Türkiye'yi desteklemiyor.
- libpostal'da mahalle ve ilçe kavramı yok; Türkçe sözlükleri 2016'dan kalma.
- Hackathon çözümleri opak küme numaraları üretiyor, kütüphane değil. Veri setleri de kapalı.
- NuGet'te hiçbir şey yok.

## AdresTR

.NET için açık kaynak (MIT) bir kütüphane. Sunucu yok, yapay zekâ çağrısı yok: kurallar, resmi gazetteer ve bulanık eşleştirme.

```csharp
var r = TurkishGazetteer.Parser.Parse("kadikoy caferaga mh moda cd no:12 d3 istanbul");
r.Unit.Id;               // 34230005 (Caferağa Mahallesi)
r.ToCanonicalString();   // "Caferağa Mah. Moda Cad. No:12 D:3 34710 Kadıköy/İstanbul"
r.Corrections;           // kadikoy → Kadıköy, ...
```

- il, ilçe, mahalle/köy, sokak, kapı, daire, posta kodu ayrılıyor; resmi kimliklere bağlanıyor.
- Semt (Moda → Caferağa), 2014 öncesi köy adları, kısaltmalar, yapışık yazım (`147sok`), yazım hatası, ASCII yazım destekleniyor.
- **Tahmin etmiyor.** "Cumhuriyet Mah. No:5" yüzlerce yerde var; AdresTR birim döndürmüyor, adayları sıralıyor.
- Her düzeltmeyi açıklıyor ve kalibre edilmiş bir güven skoru veriyor.
- İki adresin aynı yer olup olmadığını ve mükerrer adresleri bulabiliyor.

Tarayıcıda denemek için: https://alpercna.github.io/AdresTR/ (her şey tarayıcında çalışır, adres hiçbir yere gitmez).

## "Türkçe I problemi" ve diğer tuzaklar

`"İSTANBUL".ToLower()` hangi kültüre göre çalıştığına bağlı. Alpine tabanlı container'larda ve Blazor WebAssembly'de .NET
"invariant" moda düşüyor, `tr-TR` sessizce Türkçe kurallarını kaybediyor. Ölçtüm: `Normalize()` bu modda hiçbir şey yapmıyor.
Bu yüzden AdresTR'ın çekirdeği ICU'ya ve kültüre hiç dokunmuyor; tablo tabanlı kendi Türkçe işleme kodunu kullanıyor. Aynı testler
ICU'lu ve ICU'suz çalışıyor, CI bunu her commit'te doğruluyor. Bir rastgele test üreticisi (CsCheck) beni `ı` + birleşik nokta
gibi iki gerçek uç duruma götürdü.

## Önce benchmark

Parser'dan önce değerlendirme setini yaptım, çünkü "iyi mi?" sorusu sayıyla cevaplanmalı:

| Test seti | AdresTR | libpostal | regex |
|---|---:|---:|---:|
| Sentetik, gürültülü (2.000) | **%98,0** | %10,0 | %14,0 |
| Gerçek kurum adresleri (1.200) | **%95,8** | %26,2 | %8,2 |
| Elle yazılmış zor vakalar (167) | **%86,8** | %24,6 | %21,0 |

"Tam eşleşme": her bileşen doğru. Gerçek adresler İBB ve İzmir açık verisinden, kişisel veri yok. Test setleri hiç ayar için kullanılmadı.
Zor vakalarda dev'de %96, test'te %86,8; aradaki fark bir uyarı: kurallarım dev'e fazla uymuş olabilir.

Veri seti ve değerlendirme aracı da açık: https://github.com/AlperCna/AdresTR/tree/main/eval

## Dürüst sınırlar

- Sokak adları için OpenStreetMap kapsamı eksik; ayrı (ODbL) pakette, "yok" demek "yok" demek değil.
- Mahalle sınırları olmadığı için semt eşlemeleri elle ve kaynakla ekleniyor; şu an az.
- NVİ'nin verisi CAPTCHA'lı ve lisanssız; paket PTT türevi (MIT) ve Wikidata (CC0) ile kurulu.

## Kurulum

```bash
dotnet add package AdresTR.Data --prerelease
```

REST API, Docker imajı, ASP.NET Core/FluentValidation paketleri, Python ve TypeScript istemcileri de var. Katkı, özellikle yeni
kısaltmalar, semt eşlemeleri ve zor örnekler (kişisel veri olmadan) çok değerli: https://github.com/AlperCna/AdresTR
