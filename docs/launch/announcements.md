# Duyuru metinleri ve lansman kontrol listesi

Metinleri sen gönderirsin. Paylaşmadan önce linklerin açıldığını ve NuGet paketlerinin göründüğünü kontrol et.

## LinkedIn (TR)

Türkçe adresler neden bu kadar karışık? `kadikoy caferaga mh moda cd no:12 d3 istanbul` bir insan için net, bilgisayar için değil.

Bu soruna bir açık kaynak .NET kütüphanesi yazdım: **AdresTR**. Serbest metin adresi il/ilçe/mahalle/sokak/kapı/daire/posta kodu olarak
ayırıyor, resmi kayıtlara bağlıyor, her düzeltmeyi açıklıyor ve emin değilse tahmin etmiyor.

📊 Gerçek kurum adreslerinde %95,8 tam eşleşme (libpostal: %26). Test setleri hiç ayar için kullanılmadı.
🔒 Tarayıcıda çalışan demoda adresiniz cihazınızdan çıkmıyor.
🧪 Türkçe adresler için ilk açık benchmark: sentetik, gerçek ve elle yazılmış zor vakalar.

Demo: https://alpercna.github.io/AdresTR/
Kod (MIT): https://github.com/AlperCna/AdresTR

#dotnet #opensource #turkce #nlp #yazilim

## Show HN

**Show HN: AdresTR, an offline Turkish address parser with an open benchmark**

Turkish addresses are free text ("kadikoy caferaga mh moda cd no:12 d3 istanbul") and no free, offline, explainable parser existed
(Google's Address Validation does not support Turkey; libpostal has no neighbourhood concept for it). AdresTR is a rule +
gazetteer + fuzzy-matching library for .NET: it resolves il/ilçe/mahalle to official ids, never guesses ambiguous names, explains every
correction and returns a calibrated confidence. Exact match on held-out real public-institution addresses: 95.8% (libpostal 26.2%).
It also ships the first open Turkish address benchmark. The playground runs entirely in WebAssembly: https://alpercna.github.io/AdresTR/
Repo: https://github.com/AlperCna/AdresTR. Limits are listed honestly in the README (street coverage, hard-case overfit).

(Show HN kuralları: ilk yorumda kendini ve motivasyonunu kısaca anlat; arkadaşlardan oy isteme; yoruma ilk 24 saat hızlı yanıt ver.)

## X / Twitter (EN)

Turkish addresses are free text and no open parser existed, so I built one + the first open benchmark. AdresTR (.NET, MIT): resolves
il/ilçe/mahalle to official ids, never guesses, explains corrections. 95.8% exact match on real addresses vs libpostal 26%. Runs fully
in your browser: https://alpercna.github.io/AdresTR/

## Kontrol listesi

- [ ] GHCR imajı herkese açık (Packages → adrestr-api → Change visibility)
- [ ] NuGet'te `AdresTR.Data` sayfası görünüyor (README + ikon)
- [ ] PyPI ve npm istemcileri yayınlandı (docs/launch ve clients/ README'leri)
- [ ] Melih Bey'e izin e-postası gönderildi (Gmail taslağı), TBB yazışması (isteğe bağlı)
- [ ] Hugging Face veri seti yüklendi (`eval/huggingface/README.md` kartı)
- [ ] `docs/launch/blog-tr.md` Medium/dev.to'da yayımlandı; `blog-en.md` dev.to/Hashnode
- [ ] Profil README'sinde projeyi sabitle (pin) ve "Featured" tablosunun en üstüne ekle
- [ ] Gün 1: LinkedIn + X + Türkçe blog + r/CodingTR; Gün 2: Show HN (ABD sabahı), r/dotnet (kurallarına bak), r/csharp
- [ ] İlk hafta: her issue/yoruma 24 saat içinde yanıt
- [ ] Hafta 3–6: awesome-dotnet, awesome-turkish-language-models, awesome-tr'ye PR (listeye başına tek link)
