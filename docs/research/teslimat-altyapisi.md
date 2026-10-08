# Teslimat altyapısı araştırması (Faz 5–8)

> Tarih: 2026-10-08 · Kapsam: NuGet yayını, sürümleme, container, Azure Container Apps, Blazor WASM + DocFX on GitHub Pages, repo hijyeni, rate limiting ve KVKK.
> Yöntem: Resmi dokümanlar (Microsoft Learn, GitHub Docs, NuGet, KVKK) ve action depolarının `gh api` ile çekilen güncel sürüm/etiketleri. Doğrulayamadığım her madde **[DOĞRULANMADI]** etiketiyle işaretli, sonda bir listede de toplu duruyor.
> Bu belge mevcut `src/`, `tests/`, `.github/` veya proje dosyalarını değiştirmez. Buradaki dosyalar kopyalanmaya hazır **önerilerdir**.

---

## 0. Özet ve kararlar

| Konu | Öneri | Not |
|---|---|---|
| NuGet yayını | **Trusted Publishing** (`NuGet/login@v1` → 1 saatlik anahtar), GitHub environment `nuget` | Uzun ömürlü anahtarlar **1 Kasım 2026'da** geçersiz oluyor. Doğrulandı. |
| Sürümleme | **MinVer 8** (tag `v` öneki) + **release-please v5**, `release-type: simple`, `include-component-in-tag: false` | release-please tag'i (`v1.2.3`) atar, MinVer bu tag'i okur. Çakışma yok. |
| release-please token'ı | Küçük bir **GitHub App** (`actions/create-github-app-token@v3`) | `GITHUB_TOKEN` ile açılan release PR'larında CI çalışmaz. |
| Container | `dotnet publish /t:PublishContainer`, `ContainerFamily=noble-chiseled`, `linux-x64;linux-arm64` tek OCI index, GHCR | ICU gerekmiyor, `-extra` gereksiz. QEMU da gerekmiyor. |
| API barındırma | **Azure Container Apps Consumption**, `min-replicas 0`, `max-replicas 1`, `--logs-destination none`, imaj GHCR'dan (ACR yok) | Aylık ücretsiz kota: 180.000 vCPU-sn, 360.000 GiB-sn, 2 M istek. Bütçe alarmı kur. |
| API yedeği | Render Free web service (GHCR imajı + deploy hook) | 15 dk sonra uyur, soğuk başlangıç yaklaşık 1 dk. |
| Playground | Blazor WASM standalone, **interpreter (AOT kapalı) ile başla**, `InvariantGlobalization=true`, `TrimMode=full` | `BrotliDecoder` tarayıcıda **desteklenmiyor**. Bkz. §5.6. |
| Pages yerleşimi | Tek Pages sitesi: playground `/AdresTR/`, DocFX `/AdresTR/docs/`, tek workflow ile tek artifact | Kökteki 404.html `/docs/` yolunu ayrı ele alır. |
| CodeQL | Advanced setup, `csharp` için `build-mode: manual` (regex source generator nedeniyle) ve `actions` dili | `none` daha basit ama üretilen kodu kaçırabilir. |
| Codecov | `codecov/codecov-action@v7` + `use_oidc` (fork PR'larında tokensız) | İstenen v5 eski. Güncel ana sürüm v7. |
| KVKK | Asıl demo **WASM playground** (veri cihazdan çıkmaz). API demosu durumsuz çalışır, gövde loglamaz, "gerçek kişisel veri göndermeyin" uyarısı taşır | Yurt dışı barındırma KVKK m.9 kapsamında aktarımdır. Bkz. §8. |

### 0.1 Action sürümleri (2026-10-08, `gh api repos/<r>/releases/latest` ile doğrulandı)

| Action | Son sürüm | Kullan | Not |
|---|---|---|---|
| `actions/checkout` | v7.0.1 (2026-07-20) | `@v7` | Mevcut `ci.yml` `@v5` kullanıyor, Dependabot yükseltir |
| `actions/setup-dotnet` | v6.0.0 (2026-07-16) | `@v6` | Mevcut `ci.yml` `@v5` kullanıyor |
| `actions/upload-artifact` | v7.0.2 (2026-10-07) | `@v7` | Mevcut `ci.yml` `@v4` kullanıyor |
| `actions/download-artifact` | v8.0.2 (2026-10-07) | `@v8` | |
| `actions/configure-pages` | v6.0.0 | `@v6` | |
| `actions/upload-pages-artifact` | v5.0.0 | `@v5` | v4 ve sonrasında nokta dosyaları **dahil edilmez**. v5 `include-hidden-files` girdisini ekledi |
| `actions/deploy-pages` | v5.0.1 | `@v5` | |
| `actions/create-github-app-token` | v3.2.0 | `@v3` | `app-id` artık deprecated, **`client-id`** kullan |
| `docker/login-action` | v4.6.0 | `@v4` | |
| `azure/login` | v3.1.0 | `@v3` | node24 |
| `azure/container-apps-deploy-action` | v2 (2023) | önerilmez | `action.yml` hâlâ `using: 'node16'` diyor. Yerine düz `az containerapp update` |
| `googleapis/release-please-action` | v5.0.0 (2026-04-22) | `@v5` | v5'teki tek kırıcı değişiklik node24. README hâlâ `@v4` gösteriyor |
| `github/codeql-action` | v4.38.2 | `@v4` | v3, Aralık 2026'da deprecated olacak |
| `amannn/action-semantic-pull-request` | v6.1.1 | `@v6` | |
| `codecov/codecov-action` | v7.1.1 (2026-09-17) | `@v7` | v6 node24 getirdi, v7 GPG anahtar hesabını değiştirdi |
| `NuGet/login` | v1.2.0 (2026-04-24) | `@v1` | |
| DocFX (`dotnet/docfx`) | v2.81.0 (2026-09-25) | `docfx 2.81.0` | .NET 10 TFM desteği 2.78.5'te geldi |
| MinVer | 8.0.0 (8.1.0-alpha.1 var) | `8.0.0` | `Directory.Packages.props` ile uyumlu |

SHA ile sabitleme isteyenler için (etiket → commit, 2026-10-08):

```text
actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0
actions/upload-artifact@cf430e030ddbb5b0abf93d22962f4752f3646cd9 # v7.0.2
actions/download-artifact@9000827ccba6bdab643e8b6fd33ac0654aef8333 # v8.0.2
actions/configure-pages@45bfe0192ca1faeb007ade9deae92b16b8254a0d # v6.0.0
actions/upload-pages-artifact@fc324d3547104276b827a68afc52ff2a11cc49c9 # v5.0.0
actions/deploy-pages@368f82528645a54fb793d4d04e342629a3f51346 # v5.0.1
actions/create-github-app-token@bcd2ba49218906704ab6c1aa796996da409d3eb1 # v3.2.0
docker/login-action@dbcb813823bdd20940b903addbd779551569679f # v4.6.0
azure/login@a641126d1b8aa4d1fa005f4f92df94a3a4c4c906 # v3.1.0
googleapis/release-please-action@45996ed1f6d02564a971a2fa1b5860e934307cf7 # v5.0.0
github/codeql-action@2892aa5e19bbd11bc0cff5427e3b750a04d9e3c2 # v4.38.2
amannn/action-semantic-pull-request@48f256284bd46cdaab1048c3721360e808335d50 # v6.1.1
codecov/codecov-action@303a32d7a59b442fa8d48b6a1cc6825c09c847a5 # v7.1.1
NuGet/login@8d196754b4036150537f80ac539e15c2f1028841 # v1.2.0
```

Öneri: Üçüncü taraf action'ları (release-please, amannn, codecov, NuGet/login) SHA ile sabitle. `actions/*` için major etiket yeterli. Dependabot'un `github-actions` ekosistemi SHA yorumlarını (`# v7.0.1`) da günceller.

---

## 1. NuGet Trusted Publishing

### 1.1 API anahtarı takvimi (iddia doğrulandı)

.NET Blog, 3 Ağustos 2026, "Strengthening NuGet supply chain security: reducing API key lifetime":
- **17 Ağustos 2026'dan itibaren** yeni API anahtarlarının ömrü en fazla **30 gün**. 365 günlük seçenek kaldırıldı.
- 17 Ağustos'tan önce oluşturulan **tüm anahtarlar 1 Kasım 2026'da geçersiz olacak** ("All existing API keys created before that date will expire on November 1, 2026").
- Trusted Publishing Eylül 2025'te çıktı. GitHub Actions ve GitLab destekleniyor.

Sonuç: AdresTR için hiçbir zaman uzun ömürlü anahtar oluşturma. İlk sürümden itibaren OIDC kullan. ADR-0010 ile uyumlu.

### 1.2 nuget.org tarafında politika oluşturma

1. nuget.org'a giriş yap → sağ üstte kullanıcı adı → **Trusted Publishing** (API Keys'in yanında).
2. **Add policy**. Alanlar büyük/küçük harfe duyarsız:
   - **Policy owner (Package owner):** `AlperCna` (kişisel hesap) veya ileride açılacak bir organizasyon. Politika, bu sahibin **tüm paketleri** için geçerli olur.
   - **Repository Owner:** `AlperCna`
   - **Repository:** `AdresTR`
   - **Workflow File:** `release.yml`. Yalnızca dosya adı yazılır, `.github/workflows/` öneki yazılmaz.
   - **Environment (opsiyonel):** `nuget`. Workflow'da `environment: nuget` kullanırsan doldur. Doldurursan politika yalnızca o environment'tan gelen token'ları kabul eder.
3. Kayıttan sonra politika **7 gün boyunca geçici olarak aktiftir**. İlk başarılı `NuGet/login` (token değişimi) GitHub'ın değişmez repo ve owner kimliklerini politikaya bağlar ve politika kalıcı hâle gelir. 7 gün içinde login olmazsa politika pasifleşir, sayfadan yeniden 7 gün başlatılabilir. Bu mekanizma "repo silinip aynı adla yeniden açılırsa" saldırısını önler.
4. "Select Scopes": politika kapsamı (yeni paket yayınlama mı, mevcut paketlere sürüm mü) ve glob deseni (`AdresTR*`) seçilebilir. İlk yayında paket ID'leri henüz yok. "Push new packages" yetkisinin açık olduğundan emin ol **[DOĞRULANMADI: ekran alanlarının tam adları]**.

### 1.3 `NuGet/login` girdileri ve çıktıları (action.yml'den)

| Girdi | Zorunlu | Varsayılan | Açıklama |
|---|---|---|---|
| `user` | evet | yok | nuget.org **profil adı** (e-posta değil). `secrets.NUGET_USER` olarak sakla |
| `token-service-url` | hayır | `https://www.nuget.org/api/v2/token` | |
| `audience` | hayır | `https://www.nuget.org` | |

| Çıktı | Açıklama |
|---|---|
| `NUGET_API_KEY` | 1 saat geçerli, tek kullanımlık token değişiminden gelen geçici anahtar |

Gereken izin: Job seviyesinde `id-token: write`. Yaygın hatalar: 403 → `id-token: write` eksik. "No matching policy" → dosya adı uyuşmuyor ya da environment adı farklı. "Push unauthorized" → paket, politikayı sahiplenen hesaba ait değil.

### 1.4 Paket meta verisi (öneri, `Directory.Build.props` ve csproj'larda)

Mevcut dosyalarda zaten olanlar: `IncludeSymbols`, `SymbolPackageFormat=snupkg`, `PackageReadmeFile`, `PackageLicenseExpression=MIT`, `ContinuousIntegrationBuild` (GitHub Actions'ta), `MinVerTagPrefix=v`. Eklenmesi önerilenler:

```xml
<!-- Directory.Build.props (öneri) -->
<PropertyGroup>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>   <!-- SourceLink: repo URL'sini nuspec'e yazar -->
  <EmbedUntrackedSources>true</EmbedUntrackedSources> <!-- generator çıktıları snupkg'de görünsün -->
  <PackageIcon>icon.png</PackageIcon>
</PropertyGroup>
<ItemGroup Condition="'$(IsPackable)' == 'true'">
  <None Include="$(MSBuildThisFileDirectory)docs/images/icon.png" Pack="true" PackagePath="\" />
</ItemGroup>
```

.NET 8 ve sonrası SDK'lar GitHub için SourceLink'i yerleşik getirir, ayrı paket gerekmez. `dotnet nuget push *.nupkg`, aynı klasördeki eşleşen `.snupkg` dosyasını da otomatik iter.

### 1.5 Tam release workflow'u

Aşağıdaki workflow sürümleme (§2), NuGet, container (§3) ve ACA dağıtımını (§4) tek dosyada toplar. NuGet politikasındaki "Workflow File" değeri bu yüzden `release.yml` olur.

```yaml
# .github/workflows/release.yml
name: Release

on:
  push:
    branches: [main]
  workflow_dispatch:

permissions:
  contents: read

concurrency:
  group: release
  cancel-in-progress: false

env:
  DOTNET_NOLOGO: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true

jobs:
  release-please:
    runs-on: ubuntu-latest
    outputs:
      release_created: ${{ steps.rp.outputs.release_created }}
      tag_name: ${{ steps.rp.outputs.tag_name }}
      version: ${{ steps.rp.outputs.version }}
      sha: ${{ steps.rp.outputs.sha }}
    steps:
      # GitHub App token: release PR'ında CI çalışsın, tag/release olayları başka workflow'ları tetikleyebilsin.
      - id: app-token
        uses: actions/create-github-app-token@v3
        with:
          client-id: ${{ vars.RELEASE_APP_CLIENT_ID }}
          private-key: ${{ secrets.RELEASE_APP_PRIVATE_KEY }}

      - id: rp
        uses: googleapis/release-please-action@v5
        with:
          token: ${{ steps.app-token.outputs.token }}
          config-file: release-please-config.json
          manifest-file: .release-please-manifest.json

  build-test-pack:
    needs: release-please
    if: needs.release-please.outputs.release_created == 'true'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          ref: ${{ needs.release-please.outputs.sha }}
          fetch-depth: 0            # MinVer: tüm geçmiş ve tag'ler gerekli

      - uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json

      - run: dotnet restore AdresTR.slnx
      - run: dotnet build AdresTR.slnx -c Release --no-restore
      - run: dotnet test --solution AdresTR.slnx -c Release --no-build

      - name: Pack
        run: |
          dotnet pack src/AdresTR/AdresTR.csproj -c Release --no-build -o artifacts
          dotnet pack src/AdresTR.Data/AdresTR.Data.csproj -c Release --no-build -o artifacts

      - name: Verify MinVer == release-please version
        env:
          VERSION: ${{ needs.release-please.outputs.version }}
        run: |
          ls -la artifacts
          test -f "artifacts/AdresTR.${VERSION}.nupkg"
          test -f "artifacts/AdresTR.${VERSION}.snupkg"
          test -f "artifacts/AdresTR.Data.${VERSION}.nupkg"

      - uses: actions/upload-artifact@v7
        with:
          name: nuget
          path: artifacts/
          if-no-files-found: error

  publish-nuget:
    needs: [release-please, build-test-pack]
    runs-on: ubuntu-latest
    environment:
      name: nuget                    # nuget.org politikasındaki Environment alanıyla aynı
      url: https://www.nuget.org/packages/AdresTR/${{ needs.release-please.outputs.version }}
    permissions:
      id-token: write                # OIDC → NuGet geçici anahtarı
    steps:
      - uses: actions/download-artifact@v8
        with:
          name: nuget
          path: artifacts

      - uses: actions/setup-dotnet@v6
        with:
          dotnet-version: 10.0.x

      # Anahtar 1 saat geçerli olduğu için push'tan hemen önce al.
      - id: login
        uses: NuGet/login@v1
        with:
          user: ${{ secrets.NUGET_USER }}   # nuget.org profil adı

      - name: Push (.nupkg + eşleşen .snupkg)
        run: >
          dotnet nuget push "artifacts/*.nupkg"
          --api-key "${{ steps.login.outputs.NUGET_API_KEY }}"
          --source https://api.nuget.org/v3/index.json
          --skip-duplicate

  publish-container:
    needs: [release-please, build-test-pack]
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write
    outputs:
      image: ghcr.io/alpercna/adrestr-api:${{ needs.release-please.outputs.version }}
    steps:
      - uses: actions/checkout@v7
        with:
          ref: ${{ needs.release-please.outputs.sha }}
          fetch-depth: 0

      - uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json

      - uses: docker/login-action@v4      # ~/.docker/config.json yazar, .NET SDK bunu okur
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Publish multi-arch image to GHCR
        env:
          # Noktalı virgüllü liste; ortam değişkeni MSBuild özelliği olarak okunur (shell kaçış derdi yok).
          ContainerImageTags: ${{ needs.release-please.outputs.version }};latest
        run: >
          dotnet publish src/AdresTR.Api/AdresTR.Api.csproj
          -c Release
          /t:PublishContainer
          -p:ContainerRegistry=ghcr.io

  deploy-aca:
    needs: [release-please, publish-container]
    runs-on: ubuntu-latest
    environment: production
    permissions:
      id-token: write
      contents: read
    steps:
      - uses: azure/login@v3
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}

      - name: Roll new revision
        env:
          AZURE_CORE_OUTPUT: none
        run: >
          az containerapp update
          --name adrestr-api
          --resource-group rg-adrestr
          --image ghcr.io/alpercna/adrestr-api:${{ needs.release-please.outputs.version }}
```

Notlar:
- `nuget` environment'ına GitHub'da **required reviewer** (kendin) ekle. Böylece yanlış bir merge, onaysız NuGet'e çıkmaz. Public repolarda bu ücretsizdir.
- Sürüm doğrulama adımı, MinVer ile release-please'in aynı sürümü ürettiğini garanti eder. Tag fetch edilmezse MinVer `0.0.0-alpha.0.N` üretir ve job kırılır.
- İlk sürüm için paket ID'leri NuGet'te boş olmalı. `AdresTR` ve `AdresTR.Data` adlarını yayından önce nuget.org'da arayarak kontrol et.

### 1.6 Paket ID öneki rezervasyonu

Kaynak: learn.microsoft.com/nuget/nuget-org/id-prefix-reservation.
- **Başvuru:** `account@nuget.org` adresine e-posta. İçerik: nuget.org owner görünen adı (`AlperCna`) ve istenen önek(ler). Önerilen: `AdresTR.*`. Kimlik doğrulama için ek soru gelebilir. Kabul veya ret, gerekçesiyle bildirilir.
- **Kriterler** (hepsi şart değil):
  1. Önek sahibini açıkça tanımlıyor mu?
  2. Önek genel bir kelime mi? Dört karakterden kısa veya jenerik önekler reddedilir. `AdresTR` 7 karakter ve projeye özgü olduğu için uygun.
  3. Rezerve edilmemesi topluluk için karışıklık yaratır mı?
- **Yayın beklentileri:** Tutarlı yazar bilgisi, `license` meta verisi (`licenseUrl` değil), ikon için gömülü `icon` elemanı.
- **Etkisi:** Önekle eşleşen paketlerde nuget.org ve Visual Studio'da "doğrulanmış önek" işareti çıkar. Başkaları önekle yeni paket yükleyemez. "Public prefix" seçeneği işareti verir ama başkalarının yüklemesini engellemez. Çok katkıcılı OSS için uygundur ama burada gerekmiyor.
- **Zamanlama:** Bilinirlik kanıtı olarak ilk sürümden ve birkaç yüz indirmeden sonra başvurmak kabul şansını artırır. Bu bir tahmindir, takdir NuGet ekibindedir **[DOĞRULANMADI]**.

---

## 2. Sürümleme: MinVer 8 + release-please

### 2.1 Birlikte çalışıyorlar mı? Evet. İş bölümü şöyle:

| Araç | Görevi |
|---|---|
| **release-please** | Conventional commit'lerden bir sonraki semver'i hesaplar, `CHANGELOG.md` ve `version.txt` içeren bir "release PR" açar. PR merge edilince **`v1.2.3` tag'i ile GitHub Release** oluşturur |
| **MinVer** | Build sırasında en yakın `v*` tag'ini okur. Tag'li commit'te tam `1.2.3`, sonraki commit'lerde `1.2.4-alpha.0.N` üretir (`MinVerDefaultPreReleaseIdentifiers` varsayılanı `alpha.0`) |

`simple` release type, kök dizinde `version.txt` ile `CHANGELOG.md` tutan "dil bağımsız" stratejidir. .NET projesi için uygundur, çünkü sürüm dosyalara değil tag'e yazılır ve MinVer onu oradan okur. `version.txt` zararsızdır. İstenirse README rozeti için kullanılabilir.

**Tag formatı:** Manifest modunda varsayılan tag, bileşen adını içerebilir (`<component>-v1.2.3`). `include-component-in-tag: false` ile `v1.2.3` olur. MinVer'deki `MinVerTagPrefix=v` ile birebir eşleşir.

**Dikkat edilecek iki tuzak:**
1. **`GITHUB_TOKEN` kısıtı:** `GITHUB_TOKEN` ile oluşturulan PR, tag ve release yeni workflow tetiklemez. Sonuçları: (a) release PR'ında `ci.yml` çalışmaz, zorunlu status check'ler takılı kalır; (b) `on: push: tags` veya `on: release` dinleyen ayrı bir publish workflow'u hiç çalışmaz. Çözüm: §1.5'teki gibi yayını **aynı workflow'da** `release_created` koşuluyla yap **ve** release-please'e GitHub App token ver. App kurulumu: Settings → Developer settings → GitHub Apps → New. İzinler: Contents RW, Pull requests RW, Issues RW. Uygulamayı yalnızca bu repoya kur, `client-id` değerini repo variable'ına, private key'i secret'a koy.
2. **Squash merge:** release-please, `main` üzerindeki commit mesajlarını okur. Repo ayarında "Allow squash merging" + "Default to pull request title" seçeneklerini aç ve PR başlığını conventional commit olarak lint et (§7.5). Böylece her PR tek ve düzgün bir commit olur.

### 2.2 Konfigürasyon dosyaları

`release-please-config.json`:

```json
{
  "$schema": "https://raw.githubusercontent.com/googleapis/release-please/main/schemas/config.json",
  "release-type": "simple",
  "include-component-in-tag": false,
  "include-v-in-tag": true,
  "bump-minor-pre-major": true,
  "pull-request-title-pattern": "chore${scope}: release${component} ${version}",
  "changelog-sections": [
    { "type": "feat",     "section": "Yeni özellikler" },
    { "type": "fix",      "section": "Hata düzeltmeleri" },
    { "type": "perf",     "section": "Performans" },
    { "type": "data",     "section": "Veri güncellemeleri" },
    { "type": "deps",     "section": "Bağımlılıklar" },
    { "type": "revert",   "section": "Geri alınanlar" },
    { "type": "docs",     "section": "Dokümantasyon", "hidden": true },
    { "type": "refactor", "section": "Yeniden düzenleme", "hidden": true },
    { "type": "test",     "section": "Testler", "hidden": true },
    { "type": "build",    "section": "Build", "hidden": true },
    { "type": "ci",       "section": "CI", "hidden": true },
    { "type": "chore",    "section": "Diğer", "hidden": true }
  ],
  "packages": {
    ".": {
      "package-name": "AdresTR",
      "changelog-path": "CHANGELOG.md"
    }
  }
}
```

`.release-please-manifest.json`:

```json
{
  ".": "0.0.0"
}
```

- İlk sürüm: `"0.0.0"` ve `feat:` commit'leri varsa ilk PR `0.1.0` önerir. Kesinlik istenirse ilk sefer için pakete `"release-as": "0.1.0"` ekleyip yayından sonra kaldır.
- `bump-minor-pre-major: true`: 0.x'te `feat!:` / `BREAKING CHANGE` major yerine minor artırır. 1.0.0'a geçerken `release-as: "1.0.0"` kullan.
- **Önizleme sürümleri (opsiyonel):** PLAN'daki "`--prerelease`" ifadesi için `"versioning": "prerelease"`, `"prerelease": true`, `"prerelease-type": "preview"` ile `0.1.0-preview.1` benzeri tag'ler üretilebilir. MinVer bunları doğrudan okur (`v0.1.0-preview.1` → `0.1.0-preview.1`). Not: NuGet `0.x` sürümleri kararlı (stable) sayar, `--prerelease` olmadan da kurulur. Basitlik için düz `0.x` önerilir.
- `data` gibi özel bir commit tipinin release tetikleyip tetiklemediği net değil. Gizlenmemiş changelog bölümündeki tiplerin "releasable" sayıldığı varsayılıyor **[DOĞRULANMADI]**. Garanti için veri güncellemelerini `fix(data):` / `feat(data):` olarak yazmak daha güvenli.

Örnek üretilen `CHANGELOG.md` girdisi (release-please formatı):

```markdown
## [0.2.0](https://github.com/AlperCna/AdresTR/compare/v0.1.0...v0.2.0) (2026-11-03)


### Yeni özellikler

* **parser:** semt → mahalle alias çözümü ([#42](https://github.com/AlperCna/AdresTR/issues/42)) ([a1b2c3d](https://github.com/AlperCna/AdresTR/commit/a1b2c3d...))


### Hata düzeltmeleri

* **text:** "İ" katlamasında kombinasyon noktası ([#45](https://github.com/AlperCna/AdresTR/issues/45)) ([e4f5a6b](https://github.com/AlperCna/AdresTR/commit/e4f5a6b...))
```

### 2.3 MinVer tarafı (mevcut hâli doğru)

```xml
<!-- Mevcut: src/AdresTR/AdresTR.csproj ve src/AdresTR.Data/AdresTR.Data.csproj -->
<MinVerTagPrefix>v</MinVerTagPrefix>
<PackageReference Include="MinVer" PrivateAssets="all" />
```

İsteğe bağlı iyileştirmeler:
- `MinVerTagPrefix` değerini her csproj yerine `Directory.Build.props`'a taşı. API ve Playground da `InformationalVersion` alır, `/health` veya footer'da sürüm gösterilir. `MinVer` paket referansını yalnızca gereken projelere koy.
- MinVer 8 kırıcı değişikliği: Eski `MinVerDefaultPreReleasePhase` artık **hata verir**. Yerine `MinVerDefaultPreReleaseIdentifiers` (örn. `preview.0`) kullanılır.
- Checkout her yerde `fetch-depth: 0` ile yapılmalı (mevcut `ci.yml` öyle). Alternatif olarak `filter: tree:0` ile treeless clone daha hızlıdır (MinVer README).

### 2.4 Çakışırlarsa alternatifler

| Seçenek | Ne zaman |
|---|---|
| **release-please + MinVer** (önerilen) | Otomatik CHANGELOG ve otomatik tag isteniyorsa |
| **MinVer + elle tag + GitHub otomatik release notları** (`.github/release.yml`) | En sade yol: `git tag v0.1.0 && git push --tags`, `on: push: tags: ['v*']` ile yayın. Elle tag push'u kişisel token olduğu için workflow tetikler |
| **release-please tek başına** + `extra-files` ile `Directory.Build.props` `<Version>` güncelleme (`{"type":"xml","path":"Directory.Build.props","xpath":"//Project/PropertyGroup/Version"}`) | MinVer'i bırakmak istenirse. Ara build'ler önsürüm etiketi alamaz |
| **Versionize** (dotnet tool, conventional commits) / **Nerdbank.GitVersioning** / **GitVersion** | .NET'e özgü araç istenirse. NB.GV `version.json` ve yükseklik tabanlı sürüm üretir. GitVersion güçlü ama karmaşık |

---

## 3. Container: SDK ile multi-arch GHCR yayını

### 3.1 Taban imaj seçimi

| İmaj | İçerik | AdresTR için |
|---|---|---|
| `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` | Distroless Ubuntu 24.04. Kabuk ve paket yöneticisi yok, **varsayılan olarak root olmayan kullanıcı**. **ICU ve tzdata yok**, yalnızca invariant globalization ile çalışır | **Önerilen.** Çekirdek ICU'dan bağımsız (ADR-0002) ve Api csproj zaten `InvariantGlobalization=true` |
| `...:10.0-noble-chiseled-extra` | Yukarıdakinin aynısı, artı `icu` ve `tzdata` | Yalnızca `CultureInfo("tr-TR")` veya `TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")` gerekirse |
| `...:10.0-noble` (tam) | Kabuk, apt, ICU | Hata ayıklama imajı olarak |

- SDK'nın otomatik seçimi: ASP.NET projesinde varsayılan taban `mcr.microsoft.com/dotnet/aspnet:<TFM>`. Chiseled **otomatik seçilmez** (yalnızca AOT veya musl RID'lerinde özel çıkarım var). Bu yüzden `ContainerFamily=noble-chiseled` açıkça yazılmalı.
- tzdata yokken saat dilimi gerekirse sabit `+03:00` ofsetini kullan. Türkiye 2016'dan beri kalıcı olarak UTC+3.
- Root olmayan kullanıcı: .NET 8 ve sonrası Microsoft imajlarında `ContainerUser` varsayılanı `app` kullanıcısıdır (UID `APP_UID`, 1654). Varsayılan port `8080` (`ASPNETCORE_HTTP_PORTS=8080`).

### 3.2 Csproj ayarları (öneri, `src/AdresTR.Api/AdresTR.Api.csproj`)

```xml
<PropertyGroup>
  <InvariantGlobalization>true</InvariantGlobalization>            <!-- mevcut -->
  <ContainerRepository>alpercna/adrestr-api</ContainerRepository>  <!-- mevcut; GHCR adı küçük harf olmalı -->
  <ContainerFamily>noble-chiseled</ContainerFamily>
  <!-- Multi-arch: ContainerRuntimeIdentifiers, RuntimeIdentifiers'ın alt kümesi olmalı -->
  <RuntimeIdentifiers>linux-x64;linux-arm64</RuntimeIdentifiers>
  <ContainerRuntimeIdentifiers>linux-x64;linux-arm64</ContainerRuntimeIdentifiers>
  <ContainerDescription>AdresTR — Turkish address parser REST API</ContainerDescription>
  <ContainerLicenseExpression>MIT</ContainerLicenseExpression>
</PropertyGroup>

<ItemGroup>
  <!-- GHCR paketini repoya bağlar. Varsayılan etiket yalnızca PublishRepositoryUrl=true + SourceLink ile yazılır; açık yazmak garanti -->
  <ContainerLabel Include="org.opencontainers.image.source" Value="https://github.com/AlperCna/AdresTR" />
  <ContainerPort Include="8080" Type="tcp" />
</ItemGroup>
```

Komut (CI'da, bkz. §1.5):

```bash
# ContainerImageTags ortam değişkeni olarak: "0.2.0;latest"
dotnet publish src/AdresTR.Api/AdresTR.Api.csproj -c Release /t:PublishContainer -p:ContainerRegistry=ghcr.io
```

- Tek `-r` verilmez. Birden çok `ContainerRuntimeIdentifiers` olunca SDK her RID için ayrı yayın yapar ve bunları **tek bir OCI Image Index**'te birleştirir. Bu özellik SDK 8.0.405 / 9.0.102'den beri var. Multi-arch'ta format her zaman OCI'dir.
- **QEMU veya buildx gerekmez.** SDK `RUN` çalıştırmaz, arm64 imajı x64 runner'da çapraz derlenir.
- Kimlik doğrulama: `docker/login-action` `~/.docker/config.json` dosyasını yazar ve SDK bu dosyayı okur. Alternatif olarak `DOTNET_CONTAINER_REGISTRY_UNAME` / `DOTNET_CONTAINER_REGISTRY_PWORD` ortam değişkenleri kullanılabilir (SDK 8.0.400'den beri `SDK_CONTAINER_*` yerine geçti). GHCR, public paketler için bile push sırasında kimlik doğrulama ister.
- **Paket görünürlüğü:** İlk push'ta GHCR paketi **private** olur. ACA ve Render'ın kimlik bilgisi olmadan çekebilmesi için: GitHub → Packages → `adrestr-api` → Package settings → **Change visibility → Public**.
- **Repo bağlantısı:** `GITHUB_TOKEN` ile workflow'dan yayınlanan paket, workflow'un bulunduğu repoya **otomatik bağlanır** ve repo izinlerini devralır. `org.opencontainers.image.source` etiketi ek güvencedir.
- **Açıklama:** GHCR, multi-arch imajlarda açıklamayı index manifest'inin `annotations` alanından (`org.opencontainers.image.description`) okur. SDK'nın label'ları index'e annotation olarak yazıp yazmadığı belirsiz **[DOĞRULANMADI]**. Açıklama görünmezse paket sayfasından elle girilebilir.
- **Taban imaj yamaları:** `10.0-noble-chiseled` gezici (floating) bir etikettir. Güvenlik yamalarını almak için yeniden build gerekir. Öneri: Haftalık `schedule:` ile `latest` etiketini yeniden yayınlayan bir job. Dependabot `docker` ekosistemi Dockerfile tarar ve csproj içindeki taban imajı **güncellemez**.
- Yerel deneme: `dotnet publish src/AdresTR.Api -c Release /t:PublishContainer -p:ContainerRuntimeIdentifiers=linux-x64` imajı yerel Docker'a yükler. `docker run -p 8080:8080 alpercna/adrestr-api:latest`.

---

## 4. Azure Container Apps (OIDC ile), $0 hedefi ve yedekler

### 4.1 Ücretsiz kota ve maliyet aritmetiği

Azure Container Apps billing dokümanı (Consumption plan), **abonelik başına, her takvim ayı**:
- İlk **180.000 vCPU-saniye**
- İlk **360.000 GiB-saniye**
- İlk **2 milyon HTTP isteği** (health probe istekleri faturalanmaz)

Kurallar:
- Revizyon **0 replikaya** inince hiç kaynak ücreti yoktur.
- `minReplicas = 0` iken çalışan replika **aktif** tarifeden faturalanır. "Idle" indirimli tarife yalnızca `minReplicas > 0` ve minimumda bekleyen replikalar için geçerlidir.
- Son istekten sonra **300 sn cooldown** vardır (varsayılan). Her "uyanış" en az yaklaşık 5 dakikalık aktif süre demektir.

0,25 vCPU / 0,5 GiB ile hesap:
- vCPU: 180.000 / 0,25 = 720.000 sn ≈ **200 saat/ay** aktif replika
- Bellek: 360.000 / 0,5 = 720.000 sn ≈ **200 saat/ay**
- Tek uyanış (istek + 300 sn cooldown) yaklaşık 75 vCPU-sn tüketir. Ayda yaklaşık 2.400 ayrık uyanış ücretsizdir.
- En kötü durum (bir bot uygulamayı 7/24 uyanık tutarsa), `max-replicas 1` ile: 744 sa × 3600 × 0,25 ≈ 669.600 vCPU-sn. Kotayı yaklaşık 490.000 vCPU-sn aşar. Tarihsel fiyatlar (aktif vCPU yaklaşık $0,000024/sn, bellek yaklaşık $0,000003/GiB-sn) ile ayda **yaklaşık $12–15** eder **[DOĞRULANMADI: güncel bölgesel fiyatlar; fiyat sayfası fetch'inde değerler boş geldi]**.

**$0'da kalmak için:**
1. **Consumption** plan (workload profile environment'ın varsayılan "Consumption" profili). Dedicated profil ekleme, çünkü sabit yönetim ücreti var.
2. `--min-replicas 0 --max-replicas 1`. Max 1, kötüye kullanımda faturayı sınırlar.
3. **Log Analytics yok:** `--logs-destination none`. `az containerapp up` gibi kısayollar otomatik **Log Analytics workspace** oluşturur. Bu workspace GB başına ingestion ücreti doğurabilir (gizli maliyet #1). `none` ile loglar saklanmaz. Canlı izleme için portalda **Log stream** hâlâ çalışır.
4. **ACR yok:** Imaj public GHCR'dan çekilir. ACR Basic'in sabit aylık ücreti vardır (gizli maliyet #2).
5. VNet, private endpoint, planned maintenance kullanma. Bunlar "Dedicated Plan Management" ücreti tetikler (gizli maliyet #3).
6. **Bütçe alarmı:** Cost Management → Budgets → $1, %50/%100 e-posta. Pay-As-You-Go'da harcama limiti yoktur. Free account ve Azure for Students'ta varsayılan olarak harcama limiti vardır.
7. Egress: Azure'da internet çıkışının ilk yaklaşık 100 GB/ayı ücretsizdir. Demo için yeterli **[DOĞRULANMADI: güncel bandwidth kotası]**.
8. Uygulama içi rate limiting (§8.1) ve küçük istek gövdesi limiti. Bunlar "fatura DoS"una karşı ilk savunmadır.

### 4.2 Tek seferlik kurulum (Azure CLI)

```bash
# Değişkenler
RG=rg-adrestr
LOC=westeurope            # veya germanywestcentral / italynorth — Türkiye'ye yakın AB bölgesi
ENV=cae-adrestr
APP=adrestr-api
GH_REPO=AlperCna/AdresTR

az login
az extension add --name containerapp --upgrade
az provider register --namespace Microsoft.App --wait

az group create -n $RG -l $LOC

# Log Analytics OLMADAN environment
az containerapp env create -n $ENV -g $RG -l $LOC --logs-destination none

# Uygulama: public GHCR imajı, scale-to-zero, en fazla 1 replika
az containerapp create -n $APP -g $RG --environment $ENV \
  --image ghcr.io/alpercna/adrestr-api:latest \
  --ingress external --target-port 8080 \
  --cpu 0.25 --memory 0.5Gi \
  --min-replicas 0 --max-replicas 1 \
  --scale-rule-name http --scale-rule-type http --scale-rule-http-concurrency 50 \
  --env-vars ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

# GitHub OIDC için user-assigned managed identity + federated credential (secret yok)
az identity create -g $RG -n id-adrestr-gh
PRINCIPAL_ID=$(az identity show -g $RG -n id-adrestr-gh --query principalId -o tsv)
CLIENT_ID=$(az identity show -g $RG -n id-adrestr-gh --query clientId -o tsv)

az identity federated-credential create -g $RG --identity-name id-adrestr-gh \
  --name gh-production \
  --issuer https://token.actions.githubusercontent.com \
  --subject "repo:${GH_REPO}:environment:production" \
  --audiences api://AzureADTokenExchange

# En az yetki: yalnızca bu resource group'ta Contributor
az role assignment create --assignee-object-id $PRINCIPAL_ID --assignee-principal-type ServicePrincipal \
  --role Contributor --scope $(az group show -n $RG --query id -o tsv)

# GitHub'a secret olarak yazılacaklar
echo "AZURE_CLIENT_ID=$CLIENT_ID"
echo "AZURE_TENANT_ID=$(az account show --query tenantId -o tsv)"
echo "AZURE_SUBSCRIPTION_ID=$(az account show --query id -o tsv)"
```

- Federated credential **subject**'i workflow job'unun `environment: production` alanıyla birebir eşleşmeli. Environment kullanılmazsa `repo:AlperCna/AdresTR:ref:refs/heads/main` olur. Eşleşmezse login `AADSTS700213` / `AADSTS7002138` hatası verir.
- `azure/login@v3` OIDC için `client-id`, `tenant-id` ve `subscription-id` ister. Job'da `id-token: write` olmalı. v3, `client-id`'yi loglarda varsayılan olarak maskeler (`mask-client-id: true`).
- **Public GHCR imajı:** MS dokümanı, GHCR'ı deploy action ile kullanırken kimlik bilgisi olmasa da `az containerapp registry set --server ghcr.io` ile registry tanımlamayı öneriyor. `az containerapp update --image ghcr.io/...` ile anonim çekme pratikte çalışır **[DOĞRULANMADI: ilk kurulumda deneyin; hata alırsanız registry set komutunu çalıştırın]**.
- **Neden `azure/container-apps-deploy-action` değil:** v2 etiketi 2023 tarihli ve `action.yml` hâlâ `node16` diyor. Düz `az containerapp update` aynı işi yapar, daha şeffaftır ve node runtime riski taşımaz.
- **Soğuk başlangıç:** imaj çekme, uygulama başlangıcı ve gazetteer yükleme birkaç saniyeden onlarca saniyeye kadar sürebilir **[DOĞRULANMADI: ölçün]**. Startup'ta gazetteer'ı eager yükle, `/health/ready` ancak yükleme bitince 200 dönsün.
- **Özel alan adı (opsiyonel):** `az containerapp hostname add` + `az containerapp hostname bind --validation-method CNAME` ile ücretsiz managed sertifika. CNAME ve TXT (`asuid`) kayıtları gerekir. Varsayılan `*.azurecontainerapps.io` adresi demo için yeterli.
- **Bölge ve KVKK:** ACA için Türkiye bölgesi bulunamadı **[DOĞRULANMADI]**. Her durumda yurt dışı aktarım konusu §8'de.

### 4.3 Yedek: Render Free web service (GHCR imajından)

Render Free sınırları (render.com/docs/free):
- 15 dakika gelen trafik yoksa servis **uyur**. Uyanış yaklaşık 1 dakika sürer.
- Çalışma alanı başına ayda **750 serbest instance saati**. Bitince ay sonuna kadar askıya alınır.
- Dosya sistemi geçicidir. SMTP portları kapalıdır.
- Instance yaklaşık 512 MB RAM / 0,1 CPU **[DOĞRULANMADI: güncel plan özellikleri]**. Gazetteer yüklemesi yavaş olabilir.

Kurulum:
1. Dashboard → New → Web Service → **Existing image** → `ghcr.io/alpercna/adrestr-api:latest` (public), Instance Type **Free**. İmaj tabanlı servislerin Free tipte açılabildiği belgelerde açıkça yazmıyor **[DOĞRULANMADI: dashboard'da teyit et]**.
2. Env: `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`. Port: Render `PORT` değişkeni veriyor. Ya `ASPNETCORE_HTTP_PORTS=8080` ile Render'da port 8080 seç ya da Render'ın tespitine bırak.
3. Settings → **Deploy Hook** URL'sini GitHub secret `RENDER_DEPLOY_HOOK_URL` olarak sakla.

```yaml
  deploy-render:
    needs: [release-please, publish-container]
    runs-on: ubuntu-latest
    steps:
      - name: Trigger Render deploy for exact image tag
        env:
          HOOK: ${{ secrets.RENDER_DEPLOY_HOOK_URL }}   # https://api.render.com/deploy/srv-...?key=...
          IMG: ghcr.io/alpercna/adrestr-api:${{ needs.release-please.outputs.version }}
        run: |
          ENC=$(jq -rn --arg v "$IMG" '$v|@uri')
          curl -fsS -X POST "${HOOK}&imgURL=${ENC}"
```

`imgURL` içinde tag/digest dışındaki her şey servisin varsayılan imaj URL'siyle aynı olmalı, aksi hâlde istek 400 döner. URL-encode zorunluluğu belgelerden tam okunamadı **[DOĞRULANMADI]**.

### 4.4 Öğrenci ve yeni mezun teklifleri

| Teklif | Ne veriyor | Yeni mezun için durum |
|---|---|---|
| **Azure for Students** | 12 ay **$100 kredi**, kredi kartı yok, 25+ ücretsiz servis. Okul e-postasıyla doğrulama. Yenilenebilir ama aktif öğrencilik gerekir | Koşul "tam zamanlı kayıtlı öğrenci" beyanı. **Mezunsan yeni başvuru veya yenileme muhtemelen uygun değil.** Hâlâ aktif bir aboneliğin varsa 12 ay dolana kadar kullanılabilir |
| **GitHub Student Developer Pack** | Partner kredileri, Copilot Student planı (12 Mart 2026'dan itibaren ayrı plan) vb. | Resmi koşul "currently enrolled". Mezunlar genelde uygun değil. Paket zaten aktifse süresince devam edebilir. Periyodik yeniden doğrulama istenebilir |
| **Azure Free Account** | 30 gün $200 kredi + 12 ay bazı ücretsiz servisler. Kredi kartı gerekir | Herkes, tek sefer |
| **ACA aylık ücretsiz kotası** | §4.1 | **Tüm aboneliklerde** (Pay-As-You-Go dahil) her ay. Demo için asıl dayanak bu |

Öneri: Kişisel bir **Pay-As-You-Go** aboneliği aç, $1 bütçe alarmı kur ve ACA kotasına güven. Azure for Students hâlâ aktifse aynı kurulum orada da çalışır ($100 kredi tampon olur). Kredi bitince abonelik devre dışı kalır, sürpriz fatura çıkmaz.

---

## 5. Blazor WebAssembly playground (GitHub Pages)

### 5.1 Proje kurulumu (.NET 10, standalone)

```bash
dotnet new blazorwasm -o web/playground/AdresTR.Playground --empty
dotnet sln AdresTR.slnx add web/playground/AdresTR.Playground/AdresTR.Playground.csproj
```

```xml
<!-- web/playground/AdresTR.Playground/AdresTR.Playground.csproj (öneri) -->
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">

  <PropertyGroup>
    <!-- TargetFramework, Nullable vb. Directory.Build.props'tan gelir -->
    <InvariantGlobalization>true</InvariantGlobalization>   <!-- icudt*.dat indirilmez -->
    <TrimMode>full</TrimMode>                               <!-- Blazor WASM varsayılanı 'partial' -->
    <OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders> <!-- index.html'deki #[.{fingerprint}] yer tutucuları -->
    <!-- AOT yalnızca istenirse: dotnet publish -p:RunAOTCompilation=true -->
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\src\AdresTR\AdresTR.csproj" />
    <ProjectReference Include="..\..\..\src\AdresTR.Data\AdresTR.Data.csproj" />
  </ItemGroup>

  <!-- Veri derlemesini UI çizildikten sonra yükle (bkz. 5.7) -->
  <ItemGroup>
    <BlazorWebAssemblyLazyLoad Include="AdresTR.Data.wasm" />
  </ItemGroup>

</Project>
```

`Directory.Packages.props` dosyasına eklenmeli (mevcut ASP.NET sürümüyle hizalı):

```xml
<PackageVersion Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.12" />
<PackageVersion Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" Version="10.0.12" />
```

- Blazor WASM SDK varsayılanları (`Microsoft.NET.Sdk.BlazorWebAssembly.Current.props`, release/10.0.1xx): `PublishTrimmed=true`, `TrimMode=partial`. `full`, çekirdek `IsTrimmable=true` olduğu için güvenli olmalı. `TreatWarningsAsErrors` trim uyarılarını build hatasına çevirir, bu iyi bir güvence.
- `InvariantGlobalization=true`: Runtime ICU verisini (`icudt_*.dat` dilimleri) hiç indirmez (`WasmApp.Common.targets`: "Whether to disable ICU"). Çekirdek ICU'suz olduğu için kayıpsızdır.

### 5.2 Base href (`/AdresTR/`)

Kaynakta `<base href="/" />` kalsın, böylece yerel `dotnet run` çalışır. CI'da yayın çıktısında değiştir. Microsoft'un örneği `SteveSandersonMS/ghaction-rewrite-base-href` kullanıyor. Üçüncü taraf action yerine tek satır `sed` yeterli:

```bash
sed -i 's|<base href="/" />|<base href="/AdresTR/" />|' "$PUBLISH_DIR/index.html"
```

Önemli: `sed`'i fingerprint ve compression **sonrası** `index.html` üzerinde çalıştır. `index.html`'in kendisi integrity listesinde olmadığı için güvenlidir **[DOĞRULANMADI: .NET 10'da index.html'in .br/.gz kopyaları da üretiliyorsa onları silin ya da yeniden üretin. GitHub Pages bunları zaten kullanmaz]**.

### 5.3 SPA geri dönüşü: `404.html` ve `index.html` betiği

GitHub Pages derin bağlantıda (`/AdresTR/toplu`) 404 döner. Çözüm, rafrex/spa-github-pages yöntemi (MS dokümanı ve `dotnet/blazor-samples` örneği). Pages yalnızca **kökteki** `404.html`'i kullanır. §6'daki birleşik düzende docs yolu ayrıca ele alındığı için `wwwroot/404.html` şöyle olmalı:

```html
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <title>AdresTR</title>
  <script>
    // Single Page Apps for GitHub Pages — https://github.com/rafrex/spa-github-pages (MIT)
    // Project Pages (alpercna.github.io/AdresTR) → segmentCount = 1
    var segmentCount = 1;
    var l = window.location;
    if (l.pathname.toLowerCase().indexOf('/adrestr/docs/') === 0) {
      // DocFX statik sitesi: SPA'ya yönlendirme, docs ana sayfasına git
      l.replace(l.protocol + '//' + l.host + '/AdresTR/docs/');
    } else {
      l.replace(
        l.protocol + '//' + l.hostname + (l.port ? ':' + l.port : '') +
        l.pathname.split('/').slice(0, 1 + segmentCount).join('/') + '/?p=/' +
        l.pathname.slice(1).split('/').slice(segmentCount).join('/').replace(/&/g, '~and~') +
        (l.search ? '&q=' + l.search.slice(1).replace(/&/g, '~and~') : '') +
        l.hash
      );
    }
  </script>
</head>
<body></body>
</html>
```

`wwwroot/index.html` `<head>` içine, Blazor betiğinden önce:

```html
<script>
  // spa-github-pages: ?p=/yol&q=... → gerçek URL'yi history'ye geri yaz
  (function (l) {
    if (l.search) {
      var q = {};
      l.search.slice(1).split('&').forEach(function (v) {
        var a = v.split('='); q[a[0]] = a.slice(1).join('=').replace(/~and~/g, '&');
      });
      if (q.p !== undefined) {
        window.history.replaceState(null, null,
          l.pathname.slice(0, -1) + (q.p || '') + (q.q ? ('?' + q.q) : '') + l.hash);
      }
    }
  }(window.location));
</script>
```

### 5.4 `.nojekyll` gerekiyor mu?

- Pages kaynağı **"GitHub Actions"** ise (artifact + `deploy-pages`) Jekyll çalışmaz ve `_framework/` klasörü sorunsuz yayınlanır. `.nojekyll` gerekmez.
- `.nojekyll` yalnızca branch tabanlı (gh-pages) yayında gereklidir. MS dokümanındaki uyarı ve `*.js binary` `.gitattributes` önerisi bu senaryo içindir. `.gitattributes` CRLF dönüşümünü önler, Actions artifact yolunda da zararsızdır.
- Dikkat: `actions/upload-pages-artifact@v4` ve sonrası **nokta dosyalarını artifact'a koymaz**. `.nojekyll` (veya `.well-known/`) gerekirse v5'te `include-hidden-files: true` kullan.

### 5.5 Pages workflow'u

Birleşik (playground + DocFX) workflow §6.3'te. Yalnızca playground için:

```yaml
# .github/workflows/pages.yml (yalnız playground sürümü)
name: Pages
on:
  push:
    branches: [main]
    paths: ['src/**', 'web/playground/**', 'docs/**', '.github/workflows/pages.yml']
  workflow_dispatch:

permissions:
  contents: read
  pages: write
  id-token: write

concurrency:
  group: pages
  cancel-in-progress: false

jobs:
  build:
    runs-on: ubuntu-latest
    env:
      PUBLISH_DIR: web/playground/AdresTR.Playground/bin/Release/net10.0/publish/wwwroot
    steps:
      - uses: actions/checkout@v7
        with: { fetch-depth: 0 }
      - uses: actions/setup-dotnet@v6
        with: { global-json-file: global.json }
      # AOT seçilirse: - run: dotnet workload install wasm-tools
      - run: dotnet publish web/playground/AdresTR.Playground -c Release
      - run: sed -i 's|<base href="/" />|<base href="/AdresTR/" />|' "$PUBLISH_DIR/index.html"
      - uses: actions/configure-pages@v6
      - uses: actions/upload-pages-artifact@v5
        with:
          path: ${{ env.PUBLISH_DIR }}

  deploy:
    needs: build
    runs-on: ubuntu-latest
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    steps:
      - id: deployment
        uses: actions/deploy-pages@v5
```

Repo ayarı: Settings → Pages → Source: **GitHub Actions**.

### 5.6 Gazetteer verisi tarayıcıda: Brotli sorunu (kritik)

**Bulgu:** `System.IO.Compression.Brotli` .NET 10'da **browser ve wasi için desteklenmiyor**. dotnet/runtime `release/10.0` dalındaki `src/libraries/System.IO.Compression.Brotli/Directory.Build.props` dosyasında `<UnsupportedOSPlatforms>browser;wasi</UnsupportedOSPlatforms>` var. Platformsuz TFM'de assembly `PlatformNotSupportedException` fırlatan bir stub olarak üretiliyor. `main` dalında da durum aynı. Yani `BrotliDecoder` / `BrotliStream` playground'da çalışmaz.

Buna karşılık `System.IO.Compression` (Deflate/GZip/ZLib) için `$(NetCoreAppCurrent)-browser` TFM'i var. Bu yollar tarayıcıda çalışır, zlib runtime'a gömülü.

Bir ek kısıt: GitHub Pages `.br` içerik müzakeresi yapmaz. `dotnet/blazor-samples` yorumuna göre .dll dosyaları için gzip bile uygulamıyor. Bu yüzden MS dokümanı framework dosyalarını `decode.js` (Google'ın JS Brotli çözücüsü) ve `loadBootResource` ile `.br` olarak indirmeyi öneriyor.

**Seçenekler (önerilen sırayla):**

| # | Yaklaşım | Artı | Eksi |
|---|---|---|---|
| A | **Tek veri kaynağı, JS ile çözme.** `AdresTR.Data` gömülü kaynağı .NET'te okunur (`GetManifestResourceStream` tarayıcıda çalışır). Brotli gövdesi JS'e verilir ve tarayıcının `DecompressionStream('brotli')` API'siyle çözülür. Destek yoksa `decode.js` içindeki `BrotliDecode` kullanılır | NuGet paketiyle aynı bayt dizisi, sürüm kayması yok. `decode.js` zaten framework için yükleniyor | Interop üzerinden iki kopya. JS çözme süresi ölçülmeli |
| B | **Deflate/ZLib kodek varyantı.** DataBuilder başlıkta kodek baytı olan ikinci bir çıktı üretir (`codec=zlib`). Playground bu dosyayı `HttpClient` ile `wwwroot/data/` altından çeker ve `ZLibStream` ile çözer | Saf .NET, JS yok | Brotli'ye göre %10–25 daha büyük olabilir **[DOĞRULANMADI: kendi verinizle ölçün]**. İki artefakt |
| C | **Sıkıştırılmamış dosya + HTTP sıkıştırması** | En basit kod | GitHub Pages `application/octet-stream` için sıkıştırma garanti etmiyor **[DOĞRULANMADI]**. Kötü durumda 20–40 MB ham indirme |

**Öneri: A.** Gerekli API tasarımı (çekirdekte):
- `AdresTR.Data` içindeki Brotli yükleyiciyi `[UnsupportedOSPlatform("browser")]` ile işaretle. Playground (`<SupportedPlatform Include="browser" />`) bunu yanlışlıkla çağırırsa CA1416 uyarısı çıkar ve `TreatWarningsAsErrors` yüzünden derleme hatası olur.
- Ham baytları (başlık + sıkıştırılmış gövde) ve "zaten açılmış gövde"yi kabul eden platform bağımsız bir yükleme noktası ekle. Örnek: `Gazetteer.Load(ReadOnlySpan<byte> header, Stream decompressedPayload)` veya `GazetteerLoader.Load(Stream raw, Func<Stream, Stream> decompress)`. ADR-0006'daki "dış dosyadan yükleme" de bunu gerektiriyor.

```js
// wwwroot/js/adrestr.js — DecompressionStream('brotli') varsa onu, yoksa decode.js'i kullanır
window.adrestr = {
  brotliDecompress: async function (bytes /* Uint8Array */) {
    let native = false;
    try { new DecompressionStream('brotli'); native = true; } catch { /* desteklenmiyor */ }
    if (native) {
      const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream('brotli'));
      return new Uint8Array(await new Response(stream).arrayBuffer());
    }
    const out = BrotliDecode(new Int8Array(bytes.buffer, bytes.byteOffset, bytes.byteLength)); // decode.js
    return new Uint8Array(out.buffer, out.byteOffset, out.byteLength);
  }
};
```

```csharp
// Playground tarafı (IJSRuntime, WASM'da byte[] ↔ Uint8Array ikili aktarılır, base64 yok)
byte[] compressed = ReadEmbeddedPayload();                 // AdresTR.Data'daki gömülü kaynağın gövde kısmı
byte[] raw = await js.InvokeAsync<byte[]>("adrestr.brotliDecompress", compressed);
var gazetteer = Gazetteer.Load(header, new MemoryStream(raw, writable: false));
```

- `DecompressionStream('brotli')` desteği: Firefox 147 ve sonrası (Ocak 2026). Chrome ve Safari durumu doğrulanamadı **[DOĞRULANMADI]**. Bu yüzden `decode.js` geri dönüşü şart. Özellik tespiti, constructor'ın `TypeError` fırlatmasıyla yapılır.
- `decode.js` için `dotnet/blazor-samples/BlazorWebAssemblyXrefGenerator/wwwroot/decode.js` dosyasını kopyala (Google brotli JS, MIT).
- Performans: Yaklaşık 5 MB → 30–40 MB JS çözmesi birkaç yüz ms sürebilir **[DOĞRULANMADI: ölçün]**. Yükleme ekranında ilerleme göster.

### 5.7 Lazy loading

- `.NET 8+`: Derleme dosyaları Webcil formatında `.wasm` uzantılı. Lazy load öğesi `AdresTR.Data.wasm` olmalı (`.dll` değil).
- `LazyAssemblyLoader.LoadAssembliesAsync(["AdresTR.Data.wasm"])` ile, ilk boyamadan sonra veya kullanıcı yazmaya başlayınca yükle. Ardından §5.6'daki çözme yapılır.
- Lazy yüklenen derlemeye ait türlere, yükleme öncesi derlenen kodda doğrudan statik referans olmamalı (router `AdditionalAssemblies` veya ara yüz kullan). Aksi hâlde derleme açılışta yüklenir.

### 5.8 AOT mu, interpreter mı? (CPU yoğun parser)

| | Interpreter (+ Jiterpreter, varsayılan) | WASM AOT (`RunAOTCompilation=true`) |
|---|---|---|
| İndirme | Küçük | Kod kısmı "genelde yaklaşık 2 kat" (MS dokümanı). AOT, IL dll'lerini de taşır. .NET 10'da `WasmStripILAfterAOT` **varsayılan true** (targets'ta doğrulandı), bu da artışı azaltır |
| CPU yoğun iş | Yavaş (IL yorumlanır, sıcak yollar kısmen Jiterpreter ile) | "Dramatik" hızlanma (MS). Fuzzy eşleme, trie ve beam search tam bu sınıf |
| Build | Hızlı | `wasm-tools` workload gerekir, publish dakikalar sürer. Linux'ta Python gerekir (ubuntu runner'da var) |

**Öneri:** Interpreter ile başla. Playground'a gizli bir `/bench` sayfası koy: challenge setinden 1.000 adres, p50/p95 ms. Tek adres p95 değeri yaklaşık 30–50 ms'yi aşar ya da toplu CSV modu yavaş kalırsa AOT'ye geç. Bu eşik bir kullanıcı deneyimi tahminidir. Ölçüm `[DOĞRULANMADI]`. AOT'yi yalnızca Pages workflow'unda `-p:RunAOTCompilation=true` ile aç ve `dotnet workload install wasm-tools` adımını ekle.

### 5.9 İndirme boyutu beklentisi

- .NET 10 boş standalone uygulama, Brotli ile yaklaşık 1,5–2,5 MB **[DOĞRULANMADI: kendi publish çıktınızda `du -ch *.br` ile ölçün]**.
- `InvariantGlobalization=true`: ICU dilimi (yüzlerce KB) düşer.
- Gazetteer: yaklaşık 5 MB (zaten Brotli, Blazor'ın ek `.br`'si kazanç sağlamaz).
- Toplam ilk yükleme yaklaşık 7–8 MB (interpreter). AOT ile birkaç MB daha. `decode.js` yoksa framework dosyaları sıkıştırılmamış iner (birkaç kat büyük). Bu yüzden `loadBootResource` ile `.br` çekme önerilir (§5.3'teki MS örneği).
- Sonraki ziyaretler: Blazor kaynakları fingerprint'li olarak tarayıcı önbelleğinde tutulur.

### 5.10 Çoklu iş parçacığı (.NET 10)

- `WasmEnableThreads=true` runtime düzeyinde hâlâ **deneysel ve varsayılan kapalı** (`src/mono/wasm/features.md`, release/10.0). Blazor için "Make Blazor WebAssembly work on multithreaded runtime" (dotnet/aspnetcore#54365) **açık ve Backlog**'da.
- `SharedArrayBuffer` için `Cross-Origin-Embedder-Policy: require-corp` ve `Cross-Origin-Opener-Policy: same-origin` başlıkları gerekir. **GitHub Pages özel başlık ayarlatmaz**, yani thread'ler orada pratikte kullanılamaz. Service-worker hileleri (coi-serviceworker) mümkün ama önerilmez.
- Pratik: Tek iş parçacığı. Toplu CSV modunda her N satırda bir `await Task.Yield()` ile UI'ın boyanmasına izin ver, ilerleme çubuğu göster. Gerekirse ileride Web Worker.

---

## 6. DocFX + playground: tek Pages sitesi

### 6.1 Kısıt

Repo başına **tek** Pages sitesi vardır (`alpercna.github.io/AdresTR`). Actions tabanlı yayın tek bir artifact'ı **atomik** olarak değiştirir. İki ayrı workflow ayrı artifact yüklerse son yükleyen öncekini siler. Bu yüzden iki çıktı **tek job'da birleştirilip** tek artifact olarak yüklenmeli. Pages sınırları: yayınlanan site en fazla 1 GB, yaklaşık 100 GB/ay "soft" bant genişliği, dağıtım zaman aşımı 10 dakika (GitHub Pages limits).

### 6.2 Önerilen düzen

```
https://alpercna.github.io/AdresTR/          → Blazor playground (README GIF'inin kaynağı, "vay" etkisi)
https://alpercna.github.io/AdresTR/docs/     → DocFX: kavramlar, API referansı, "Türkçe I", veri rehberi
https://alpercna.github.io/AdresTR/404.html  → SPA yönlendirici, /docs/ yolunu ayrı ele alır (§5.3)
```

Gerekçe: PLAN Faz 7 "Bitti" tanımı `alpercna.github.io/AdresTR` adresinin canlı demo olmasını istiyor. DocFX göreli bağlantı ürettiği için alt klasörde ayar gerektirmeden çalışır. Pages yalnızca kökteki 404.html'i kullandığı için SPA'nın kökte olması en az sürtünmeli seçenek. Alternatifler: (a) docs kökte, playground `/playground/`. 404.html bu durumda `/playground/` önekini ayıklar (`segmentCount = 2`). (b) Ayrı bir `AlperCna/AdresTR-docs` reposu. Gereksiz karmaşıklık, önerilmez.

**Playground'dan docs'a link:** Blazor router, base href altındaki `<a href="docs/">` tıklamasını yakalar ve kendi NotFound sayfasını gösterebilir. Tam sayfa gezinme için `target="_top"` kullan veya `NavigationManager.NavigateTo("docs/", forceLoad: true)` çağır. Blazor `_self` dışındaki `target` değerlerini yakalamıyor **[DOĞRULANMADI: deneyin]**.

### 6.3 DocFX konfigürasyonu

`docs/docfx.json`:

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/docfx/main/schemas/docfx.schema.json",
  "metadata": [
    {
      "src": [
        { "src": "../src", "files": ["AdresTR/AdresTR.csproj", "AdresTR.Data/AdresTR.Data.csproj"] }
      ],
      "dest": "api",
      "properties": { "TargetFramework": "net10.0" }
    }
  ],
  "build": {
    "content": [
      { "files": ["**/*.{md,yml}"], "exclude": ["_site/**", "plan/**", "research/**", "obj/**"] }
    ],
    "resource": [ { "files": ["images/**"] } ],
    "output": "_site",
    "template": ["default", "modern"],
    "globalMetadata": {
      "_appName": "AdresTR",
      "_appTitle": "AdresTR",
      "_appFooter": "MIT · Veri lisansları: LICENSE-DATA.md",
      "_enableSearch": true
    },
    "sitemap": { "baseUrl": "https://alpercna.github.io/AdresTR/docs/" }
  }
}
```

DocFX'i bir yerel araç olarak sabitle (CI ile yerel ortam aynı sürümü kullansın):

```bash
dotnet new tool-manifest            # .config/dotnet-tools.json
dotnet tool install docfx --version 2.81.0
dotnet docfx docs/docfx.json --serve
```

DocFX 2.78.5 ve sonrası .NET 10 TFM'i destekliyor. `$schema` URL'si doğrulanmadı **[DOĞRULANMADI]**, kaldırılabilir.

### 6.4 Birleşik Pages workflow'u

```yaml
# .github/workflows/pages.yml
name: Pages
on:
  push:
    branches: [main]
    paths: ['src/**', 'web/playground/**', 'docs/**', '.config/dotnet-tools.json', '.github/workflows/pages.yml']
  workflow_dispatch:

permissions:
  contents: read
  pages: write
  id-token: write

concurrency:
  group: pages
  cancel-in-progress: false

env:
  DOTNET_NOLOGO: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true
  PLAYGROUND_OUT: web/playground/AdresTR.Playground/bin/Release/net10.0/publish/wwwroot

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          fetch-depth: 0          # MinVer → playground footer'ında sürüm

      - uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json

      - name: Publish playground
        run: dotnet publish web/playground/AdresTR.Playground -c Release

      - name: Rewrite base href
        run: sed -i 's|<base href="/" />|<base href="/AdresTR/" />|' "$PLAYGROUND_OUT/index.html"

      - name: Build docs
        run: |
          dotnet tool restore
          dotnet docfx docs/docfx.json

      - name: Assemble site
        run: |
          mkdir -p site
          cp -r "$PLAYGROUND_OUT"/. site/
          mkdir -p site/docs
          cp -r docs/_site/. site/docs/

      - uses: actions/configure-pages@v6

      - uses: actions/upload-pages-artifact@v5
        with:
          path: site

  deploy:
    needs: build
    runs-on: ubuntu-latest
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    steps:
      - id: deployment
        uses: actions/deploy-pages@v5
```

Docs'un yalnızca sürümlerde güncellenmesi istenirse `on: release: types: [published]` eklenebilir. Bu tetikleyici yalnızca release-please GitHub App token'ıyla çalışır (§2.1). Demo amaçlı bir repo için `main`'den yayın yeterli.

---

## 7. Repo hijyeni şablonları

Not: `.github/ISSUE_TEMPLATE/` altında üç form ve `pull_request_template.md` zaten var. Aşağıdakiler onları genişletmek için öneridir: bilgilendirme kutusu, onay kutuları, `config.yml`.

### 7.1 Issue formları

```yaml
# .github/ISSUE_TEMPLATE/config.yml
blank_issues_enabled: false
contact_links:
  - name: Güvenlik açığı bildir / Report a vulnerability
    url: https://github.com/AlperCna/AdresTR/security/advisories/new
    about: Lütfen güvenlik açıklarını herkese açık issue olarak açmayın.
  - name: Soru / Question
    url: https://github.com/AlperCna/AdresTR/discussions
    about: Kullanım soruları için Discussions.
```

```yaml
# .github/ISSUE_TEMPLATE/bug.yml
name: Hata / Bug
description: Kütüphane, API veya playground'da beklenmeyen davranış. / Unexpected behavior.
labels: [bug, triage]
body:
  - type: textarea
    id: what
    attributes:
      label: Ne oldu? / What happened?
    validations:
      required: true
  - type: textarea
    id: repro
    attributes:
      label: Yeniden üretme / Repro
      description: Mümkünse en küçük kod örneği. / Minimal code sample.
      render: csharp
    validations:
      required: true
  - type: textarea
    id: logs
    attributes:
      label: Hata çıktısı / Stack trace
      render: text
  - type: dropdown
    id: surface
    attributes:
      label: Bileşen / Component
      options: [NuGet kütüphanesi, REST API, Playground (WASM), Docker imajı, Dokümantasyon]
    validations:
      required: true
  - type: input
    id: version
    attributes:
      label: Sürüm / Version
      placeholder: "AdresTR 0.2.0, AdresTR.Data 0.2.0 (veri 2026.10), .NET 10.0.x, OS"
    validations:
      required: true
```

```yaml
# .github/ISSUE_TEMPLATE/wrong-parse.yml
name: Yanlış ayrıştırma / Wrong parse
description: Bir adres yanlış ayrıştırıldı veya çözümlenemedi. / An address was parsed or resolved incorrectly.
labels: [wrong-parse, triage]
body:
  - type: markdown
    attributes:
      value: |
        ### KVKK uyarısı / Privacy warning
        **Gerçek bir kişinin adresini paylaşmayın.** Issue'lar herkese açıktır ve kalıcıdır.
        Kapı/daire numarasını, site/blok adını değiştirin ya da bir kurum/işyeri adresi kullanın. İsim, telefon, T.C. kimlik no **yazmayın**.
        **Do not post a real person's address.** Issues are public and permanent. Alter door/flat numbers or use a business address.
  - type: checkboxes
    id: consent
    attributes:
      label: Onay / Confirmation
      options:
        - label: Bu adres gerçek bir kişiye ait değil ya da kişiyi tanımlayamayacak şekilde değiştirildi. / This address is not a real person's or has been altered so no one can be identified.
          required: true
  - type: textarea
    id: input
    attributes:
      label: Girdi adresi / Input address
      description: Ayrıştırılan metin, olduğu gibi (yazım hatalarıyla birlikte). / The exact text you parsed.
      render: text
    validations:
      required: true
  - type: textarea
    id: actual
    attributes:
      label: AdresTR çıktısı / Actual output
      description: Playground'daki "JSON kopyala" çıktısı. / The JSON from the playground "Copy JSON" button.
      render: json
  - type: textarea
    id: expected
    attributes:
      label: Beklenen / Expected
      description: Hangi bileşen ne olmalıydı? (il, ilçe, mahalle, cadde/sokak, no, daire, posta kodu) / Which component should be what?
    validations:
      required: true
  - type: dropdown
    id: where
    attributes:
      label: Nerede denediniz? / Where?
      options: [Playground, NuGet kütüphanesi, REST API]
  - type: input
    id: version
    attributes:
      label: Sürüm / Version
      placeholder: "AdresTR 0.2.0, veri 2026.10"
```

```yaml
# .github/ISSUE_TEMPLATE/alias-or-abbreviation.yml
name: Kısaltma veya semt adı ekle / New abbreviation or semt alias
description: Sözlüğe yeni bir kısaltma, yazım varyantı veya semt → mahalle eşlemesi önerin.
labels: [data, curated, good first issue]
body:
  - type: dropdown
    id: kind
    attributes:
      label: Tür / Kind
      options:
        - Kısaltma / yazım hatası (abbreviations.csv)
        - Semt → mahalle (semt_alias.csv)
        - Eski ad → güncel mahalle (historic_alias.csv)
    validations:
      required: true
  - type: input
    id: variant
    attributes:
      label: Görülen yazım / Seen form
      placeholder: "mh., mahlesi, cd, sk., Moda"
    validations:
      required: true
  - type: input
    id: canonical
    attributes:
      label: Kanonik karşılık / Canonical form
      placeholder: "Mahallesi · Caddesi · Caferağa Mahallesi (Kadıköy, İstanbul)"
    validations:
      required: true
  - type: input
    id: scope
    attributes:
      label: Geçerli olduğu yer / Scope (il/ilçe)
      description: Semt eşlemeleri için zorunlu; aynı semt adı birden çok ilde olabilir.
      placeholder: "İstanbul / Kadıköy"
  - type: textarea
    id: evidence
    attributes:
      label: Kaynak / Evidence
      description: Belediye sayfası, PTT, Wikipedia, NVİ vb. bağlantı. Kişisel adres paylaşmayın.
    validations:
      required: true
  - type: checkboxes
    id: pr
    attributes:
      label: Katkı / Contribution
      options:
        - label: Bunu `data/curated/*.csv` dosyasına PR olarak eklemek istiyorum. / I'd like to open a PR myself.
```

### 7.2 PR şablonu

```markdown
<!-- .github/pull_request_template.md -->
## Ne değişti? / What changed?

<!-- PR başlığı conventional commit olmalı: feat(parser): ..., fix(text): ..., fix(data): ... -->

## Neden? / Why?

Closes #

## Kontrol listesi / Checklist

- [ ] Testler eklendi veya güncellendi (`dotnet test` yeşil)
- [ ] Invariant globalization modunda da çalışıyor (CI `linux-invariant-globalization` job'u)
- [ ] Doğruluk benchmark'ı gerilemedi (parser değişikliklerinde)
- [ ] Public API değiştiyse XML doc ve `docs/` güncellendi
- [ ] Gerçek kişisel adres içeren test verisi **yok** (KVKK)
- [ ] `data/curated/*.csv` değişikliklerinde kaynak belirtildi
```

### 7.3 CODEOWNERS

```text
# .github/CODEOWNERS
*                       @AlperCna
/data/                  @AlperCna
/data/curated/          @AlperCna
/.github/workflows/     @AlperCna
/src/AdresTR/Text/      @AlperCna
```

Tek bakımcı için anlamı: Branch protection'da "Require review from Code Owners" açılırsa dış katkılar sizden onaysız merge olamaz. Kendi PR'larınız için admin bypass gerekir.

### 7.4 dependabot.yml

```yaml
# .github/dependabot.yml
version: 2
updates:
  - package-ecosystem: nuget
    directory: /
    schedule:
      interval: weekly
      day: monday
    cooldown:
      default-days: 3
      semver-major-days: 14
    commit-message:
      prefix: "chore(deps)"
    groups:
      aspnetcore:
        patterns: ["Microsoft.AspNetCore.*", "Microsoft.Extensions.*"]
      test:
        patterns: ["xunit*", "Microsoft.Testing.*", "CsCheck", "Microsoft.AspNetCore.Mvc.Testing", "Verify*"]
      build:
        patterns: ["MinVer", "Microsoft.SourceLink.*"]

  - package-ecosystem: dotnet-sdk          # global.json içindeki SDK sürümü
    directory: /
    schedule:
      interval: weekly
    commit-message:
      prefix: "chore(deps)"

  - package-ecosystem: github-actions
    directory: /
    schedule:
      interval: weekly
    commit-message:
      prefix: "ci"
    groups:
      actions:
        patterns: ["*"]

  # Dockerfile / compose varsa (PLAN: docker-compose.yml). SDK container'ı Dockerfile kullanmadığı için
  # taban imaj (ContainerFamily) Dependabot ile güncellenmez → haftalık yeniden build (§3.2).
  - package-ecosystem: docker-compose
    directory: /
    schedule:
      interval: monthly
    commit-message:
      prefix: "chore(deps)"
```

- `prefix: "chore(deps)"` → başlık `chore(deps): bump ...` olur, semantic PR lint'ten geçer. release-please `chore`'u gizler. Bağımlılıkların changelog'da görünmesi istenirse prefix `deps` olmalı ve `deps` tipi lint listesine eklenmeli.
- Ekosistem değerleri GitHub dokümanından doğrulandı: `nuget`, `dotnet-sdk`, `github-actions`, `docker`, `docker-compose`. `cooldown` belirtilmezse varsayılan 3 gündür.
- Kendi workflow'unun Codecov'a yükleme yapması gerekiyorsa OIDC kullanılırken Dependabot secret'ı gerekmez (§7.7).

### 7.5 Conventional commit PR başlığı lint'i

```yaml
# .github/workflows/pr-title.yml
name: PR title
on:
  pull_request_target:
    types: [opened, reopened, edited, synchronize]

permissions:
  pull-requests: read

jobs:
  lint:
    runs-on: ubuntu-slim
    steps:
      - uses: amannn/action-semantic-pull-request@v6
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        with:
          types: |
            feat
            fix
            perf
            data
            deps
            docs
            refactor
            test
            build
            ci
            chore
            revert
          requireScope: false
          subjectPattern: ^(?![A-Z]).+$
          subjectPatternError: |
            Konu küçük harfle başlamalı. / Subject must start with a lowercase letter: "{subject}"
```

- `pull_request_target` workflow'u base repo bağlamında çalışır, fork PR'larında da etiket okuyabilir. Bu action PR kodunu **checkout etmez**, bu yüzden güvenlidir. Bu workflow'a asla `actions/checkout` + PR head eklemeyin.
- `ubuntu-slim` etiketi action README'sinde kullanılıyor (tek vCPU'lu hafif runner). Bulunamazsa `ubuntu-latest` kullanın.
- Branch protection'da bu check'i zorunlu yap. Repo ayarı: "Allow squash merging" + "Default commit message: Pull request title".

### 7.6 CodeQL (C#)

2026 durumu (GitHub Docs):
- C# için build mode `none`, `autobuild` ve `manual` destekleniyor. **Default setup C# için otomatik olarak `none` seçer.**
- `none`: Kurulumu basit ve hızlı. Bağımlılıkları geri yükler, birkaç kaynak dosya üretir. Ancak **derleme sırasında kod üretimi** veya erişilemeyen registry'ler varsa uyarı kaçırabilir. AdresTR'ın regex source generator (`[GeneratedRegex]`) kullanacağı planlanıyor, bu tam o risk.
- `autobuild` başarılı olursa sonuç `manual` ile aynıdır. Solution yapısı basitse (`AdresTR.slnx`) genelde çalışır.
- **Öneri:** Advanced setup ve `manual` (`dotnet build`). Build ucuz ve en kesin sonucu verir. İstenirse ek olarak `actions` dili (workflow YAML taraması) eklenir.
- CodeQL Action **v4** kullanılmalı. v3, Aralık 2026'da deprecated olacak (GitHub changelog 2025-10-28).

```yaml
# .github/workflows/codeql.yml
name: CodeQL
on:
  push:
    branches: [main]
  pull_request:
    branches: [main]
  schedule:
    - cron: '27 3 * * 1'

permissions:
  contents: read

jobs:
  analyze:
    name: Analyze (${{ matrix.language }})
    runs-on: ubuntu-latest
    permissions:
      security-events: write
      contents: read
      actions: read
    strategy:
      fail-fast: false
      matrix:
        include:
          - language: csharp
            build-mode: manual
          - language: actions
            build-mode: none
    steps:
      - uses: actions/checkout@v7
        with:
          fetch-depth: 0

      - if: matrix.language == 'csharp'
        uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json

      - uses: github/codeql-action/init@v4
        with:
          languages: ${{ matrix.language }}
          build-mode: ${{ matrix.build-mode }}
          queries: security-and-quality

      - if: matrix.build-mode == 'manual'
        run: dotnet build AdresTR.slnx -c Release

      - uses: github/codeql-action/analyze@v4
        with:
          category: "/language:${{ matrix.language }}"
```

Not: Default setup açıkken advanced workflow eklenirse ikisi çakışır. Biri seçilmeli (Settings → Code security → CodeQL).

### 7.7 Codecov (v7, OIDC)

Mevcut `ci.yml`, Microsoft.Testing.Platform ile `--coverage --coverage-output-format cobertura` kullanıyor ve çıktıyı `coverage/` altına yazıyor. Codecov adımı (yalnız `linux` matris girdisinde):

```yaml
# ci.yml → build-test job'una eklenecekler
    permissions:
      contents: read
      id-token: write          # Codecov OIDC

      # ... test adımından sonra:
      - name: Upload coverage to Codecov
        if: matrix.name == 'linux'
        uses: codecov/codecov-action@v7
        with:
          files: ./coverage/**/*.cobertura.xml
          flags: unittests
          # Aynı repodan gelen push/PR → OIDC; fork PR'ında id-token verilmez → tokensız (public repo)
          use_oidc: ${{ !(github.event_name == 'pull_request' && github.event.pull_request.head.repo.fork) }}
          fail_ci_if_error: false
```

- `use_oidc: true` iken verilen token yok sayılır. Job'da `id-token: write` olmalı (Codecov README).
- Public repolarda fork PR'ları tokensız yükleyebilir (v4+). Organizasyon ayarında "Global Upload Token" opsiyonel kılınabilir.
- v5'te `file` → `files`, `plugin` → `plugins` olarak değişti. v6 node24 getirdi. v7 yalnızca GPG doğrulama anahtarının alındığı Keybase hesabını (`codecovsecops`) değiştirdi. Kırıcı girdi değişikliği yok.
- `files` girdisinde glob desteği belirsiz **[DOĞRULANMADI]**. Çalışmazsa `directory: ./coverage` verip otomatik aramaya bırakın.

İsteğe bağlı `codecov.yml`:

```yaml
coverage:
  status:
    project:
      default:
        target: auto
        threshold: 1%
    patch:
      default:
        target: 80%
comment:
  layout: "diff, flags, files"
```

---

## 8. Rate limiting ve KVKK (yurt dışında barındırılan herkese açık demo API)

> Bu bölüm hukuki görüş değildir. KVKK'ya uyum için ücretsiz kaynaklar: KVKK'nın kendi rehberleri, baro hukuk klinikleri. Ticari bir hizmete dönüşürse bir veri koruma avukatına danışın.

### 8.1 Rate limiting ve kötüye kullanım önlemleri (ASP.NET Core 10, yerleşik)

```csharp
// Program.cs (öneri)
using System.Threading.RateLimiting;

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (ctx, _) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        return ValueTask.CompletedTask;
    };

    // IP başına: 60 istek/dk (tek), batch için ayrı ve daha sıkı politika
    o.AddPolicy("per-ip", http => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));

    o.AddPolicy("per-ip-batch", http => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));

    // Küresel tavan: tek replikayı (0,25 vCPU) korur
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
        RateLimitPartition.GetConcurrencyLimiter("global",
            _ => new ConcurrencyLimiterOptions { PermitLimit = 32, QueueLimit = 64 }));
});

builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 1 * 1024 * 1024); // 1 MB
builder.Services.AddRequestTimeouts(o => o.DefaultPolicy = new() { Timeout = TimeSpan.FromSeconds(10) });

var app = builder.Build();
// ASPNETCORE_FORWARDEDHEADERS_ENABLED=true → X-Forwarded-For/Proto middleware'i otomatik eklenir
// (bilinen proxy listesini temizler; yalnız ingress arkasında, yani ACA veya Render'da güvenli).
app.UseRateLimiter();
app.UseRequestTimeouts();

var v1 = app.MapGroup("/v1").RequireRateLimiting("per-ip");
v1.MapPost("/parse/batch", /* ... */).RequireRateLimiting("per-ip-batch");
```

- **Gerçek istemci IP'si:** ACA ve Render ingress'i `X-Forwarded-For` ekler. `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` ortam değişkeni ForwardedHeaders middleware'ini açar ve bilinen ağ/proxy listesini temizler. Bu, uygulamaya yalnızca ingress üzerinden erişilebildiği sürece güvenlidir. Kodla yapılandırılacaksa .NET 10'da `KnownNetworks` yerine `KnownIPNetworks` kullanılmalı **[DOĞRULANMADI: obsolete uyarısı kodu]**.
- IPv6'da tek kullanıcı bir /64 bloğuna sahiptir. Partition anahtarı olarak /64 öneki kullanmak daha adil olur (opsiyonel).
- Bellek içi limiter, replika başına çalışır. `max-replicas 1` ile tutarlı. Yeniden başlatmada sıfırlanır. Demo için kabul edilebilir.
- PLAN'daki "1.000 adres/batch" ile 1 MB gövde limiti uyumlu. Gerekirse batch uç noktasına özel limit verilebilir.

### 8.2 Loglama: kişisel veri sızıntısı noktaları

- **Gövde loglama yok:** `AddHttpLogging` kullanılıyorsa `RequestBody`/`ResponseBody` alanları **kapalı** olmalı. En iyisi hiç eklememek.
- **Query string:** `GET /v1/autocomplete?q=...` kullanıcı girdisi taşır. `Microsoft.AspNetCore.Hosting.Diagnostics` Information seviyesinde "Request starting ... ?q=..." satırı yazar. Varsayılan şablondaki `"Microsoft.AspNetCore": "Warning"` ayarı bunu bastırır. **Bu ayar korunmalı.**
- **İstisnalar:** ProblemDetails ve exception mesajları girdi metnini içermemeli. Parser istisnalarında girdiyi mesajda tekrarlama.
- **OpenTelemetry:** Yalnızca özel metrikler (süre, güven, uzunluk). Trace'lerde `url.query` ve gövde attribute'u ekleme. ASP.NET Core instrumentation'ın query değerlerini varsayılan olarak maskeleyip maskelemediği sürüme bağlı **[DOĞRULANMADI]**. Gerekirse `EnrichWithHttpRequest` ile `url.query` silinir.
- **Platform logları:** ACA'da `--logs-destination none` ile stdout saklanmaz. Azure ve Render'ın kendi altyapı logları (IP, zaman damgası) sağlayıcının sorumluluğundadır. Aydınlatma metninde belirtilmeli.
- **OutputCache / HybridCache:** PLAN, POST parse için HybridCache öngörüyor. Önbellek anahtarı **adres metninin kendisi** olur, yani kişisel veri bellekte tutulur. Kısa TTL (örn. 5 dk), yalnızca bellek içi ve dağıtık önbellek yok. Demo API'de **POST parse önbelleğini kapatmak** en düşük riskli seçim. GET referans uç noktalarında (`/reference/il` vb.) OutputCache sorunsuz.

### 8.3 KVKK çerçevesi: yurt dışına aktarım (7499 sayılı Kanun sonrası)

- **Değişiklik:** 7499 sayılı Kanun'un 34. maddesi KVKK m.9'u değiştirdi, **1 Haziran 2024**'te yürürlüğe girdi. Usul ve esaslar: "Kişisel Verilerin Yurt Dışına Aktarılmasına İlişkin Usul ve Esaslar Hakkında Yönetmelik" (RG 10.07.2024, sayı 32598).
- **Aktarım yolları (sırayla):**
  1. **Yeterlilik kararı** (ülke veya sektör). Ekim 2026 itibarıyla ilan edilmiş bir yeterlilik kararı bilmiyorum **[DOĞRULANMADI: kvkk.gov.tr'den kontrol edin]**.
  2. **Uygun güvenceler:** (a) Kurul'un ilan ettiği **standart sözleşme**. İmzadan itibaren **5 iş günü içinde Kurum'a bildirilir**. 25 Ekim 2024'ten beri "Standart Sözleşme Bildirim Modülü" ile çevrim içi yapılabiliyor, alternatifler KEP veya fiziki teslim. Bildirime imza yetkisini gösteren belgeler ve yabancı dildeki belgelerin noter onaylı çevirisi eklenir. (b) Bağlayıcı şirket kuralları. (c) Kurul izniyle yazılı taahhütname.
  3. **Arızi aktarım istisnaları** (m.9/6). Yalnızca **düzenli olmayan**, tekil durumlar için. Sürekli çalışan bir API barındırması "arızi" sayılmaz.
- **Açık rıza artık genel bir aktarım dayanağı değil.** Açık rızaya dayalı aktarımlar 1 Eylül 2024'e kadar geçerli sayıldı. Arızi istisnalardan biri olarak, bilgilendirilmiş açık rıza dar kapsamda kalıyor.
- **Bildirim yapmamanın yaptırımı:** 2026 idari para cezası aralığı **90.308 TL – 1.806.377 TL** (2025 aralığı × 1,2549 yeniden değerleme. Kaynak: CottGroup tablosu, Resmî Gazete ile teyit edin).
- **Bulut sağlayıcı:** Azure veya Render sunucusu yurt dışındaysa ve sunucuya kişisel veri giderse bu aktarımdır. Sağlayıcı "veri işleyen" olur. Microsoft'un KVKK standart sözleşmesini nasıl sunduğuna dair resmi bir kaynak bulamadım. Azure'un "KVK Soru-Cevap" PDF'i var ama içeriği doğrulanamadı **[DOĞRULANMADI]**. Render için de bilgi yok.
- **KVKK uygulanır mı?** m.28/1-a istisnası yalnızca gerçek kişinin "kendisiyle veya aynı konutta yaşayan aile fertleriyle ilgili" faaliyetlerini kapsar. Herkese açık bir API bu kapsamın dışındadır. Bir adres, belirli bir kişiyle ilişkilendirilebiliyorsa (konut adresi + kapı/daire no) kişisel veridir (ADR-0009). Hizmete gönderilen IP adresi de kişisel veri sayılır. Kurul kararı düzeyinde doğrulanamadı, genel kabul bu yönde **[DOĞRULANMADI]**.
- **VERBİS:** Küçük ölçekli veri sorumluları (çalışan ve bilanço eşiklerinin altında, ana faaliyeti özel nitelikli veri işlemek olmayanlar) kayıttan muaf olabilir. Güncel eşik için Kurul kararını kontrol edin **[DOĞRULANMADI: güncel eşik]**.

### 8.4 Önerilen en düşük riskli tasarım

1. **Varsayılan demo = WASM playground.** Adres cihazdan çıkmaz, sunucuya veri gitmez, aktarım yoktur. README ve playground'da "Adresiniz tarayıcınızdan çıkmaz" mesajı. PLAN Faz 7 ve ADR-0009 ile uyumlu.
2. **Barındırılan API = geliştirici demosu.** Scalar UI'da ve `/privacy` sayfasında açık uyarı: "**Gerçek kişisel veri göndermeyin; örnek veya kurumsal adresler kullanın. Üretim kullanımı için Docker imajını kendi altyapınızda çalıştırın.**" Bu uyarı riski azaltır ama tek başına KVKK uyumu sağlamaz.
3. **Durumsuz:** Kalıcı depolama yok, POST parse önbelleği kapalı, gövde ve query string loglanmaz, `--logs-destination none`. Rate limiter IP'yi yalnızca pencere süresince (≤ 1 dk) bellekte tutar.
4. **Çerez yok, analitik yok.** Playground'da da üçüncü taraf analitik ekleme. GitHub Pages'in kendi sunucu loglarını aydınlatmada belirt.
5. **Bölge:** Türkiye'ye en yakın AB bölgesi. Bu bir gecikme tercihidir. AB'de olmak KVKK açısından ayrıcalık **sağlamaz**, çünkü ayrı bir yeterlilik kararı yoktur.
6. **Hukuken en temiz seçenek:** API'yi herkese açık barındırmayıp yalnızca imaj ve self-host rehberi sunmak, ya da Türkiye'de barındırmak. Barındırma sürdürülecekse sağlayıcıyla KVKK standart sözleşmesi ve 5 iş günü içinde bildirim seçeneğini bir hukukçuyla değerlendir. Bu karar size ait. Teknik tasarım her durumda yukarıdaki 1–5. maddeler.

### 8.5 Aydınlatma metni taslağı (`/privacy`, Türkçe; EN çevirisi ayrıca)

```markdown
# Gizlilik ve Aydınlatma Metni — AdresTR Demo API

**Veri sorumlusu:** Alper Can (bireysel açık kaynak geliştirici) · İletişim: <iletişim e-postası> · github.com/AlperCna/AdresTR

**Bu hizmet bir demodur.** Lütfen **gerçek kişilere ait adres göndermeyin**; örnek veya kurumsal adresler kullanın.
Tarayıcıda çalışan playground (alpercna.github.io/AdresTR) adresinizi hiçbir sunucuya göndermez — gerçek veriler için onu
veya kendi altyapınızda çalıştırabileceğiniz Docker imajını kullanın.

**İşlenen veriler ve süreleri**
- Gönderdiğiniz adres metni: yalnızca isteğin işlenmesi süresince bellekte tutulur; **kaydedilmez, loglanmaz, önbelleğe alınmaz**.
- IP adresi: kötüye kullanımı önlemek için istek sınırlama amacıyla en fazla 1 dakika bellekte tutulur; kaydedilmez.
- Teknik ölçümler: yalnızca anonim istatistikler (istek süresi, metin uzunluğu, güven skoru); adres veya IP içermez.
- Çerez ve analitik kullanılmaz.

**Amaç ve hukuki sebep:** Adres ayrıştırma hizmetinin sunulması (KVKK m.5/2-c, hizmetin ifası) ve hizmet güvenliği
(m.5/2-f, meşru menfaat).

**Aktarım:** Hizmet <Microsoft Azure, Batı Avrupa bölgesi / Render, Frankfurt> üzerinde barındırılır; bu nedenle istek
verileri barındırma sağlayıcısının yurt dışındaki sunucularında işlenir (KVKK m.9). Sağlayıcının altyapı logları
(ör. bağlantı IP'si, zaman damgası) sağlayıcının kendi politikalarına tabidir. <Standart sözleşme imzalandıysa: "Aktarım,
Kurul tarafından ilan edilen standart sözleşmeye dayanır ve Kurum'a bildirilmiştir.">

**Haklarınız (KVKK m.11):** Verilerinizin işlenip işlenmediğini öğrenme, bilgi talep etme, düzeltme, silme, itiraz ve zararın
giderilmesini talep etme. Veri saklanmadığı için silinecek kayıt bulunmaz. Başvurular: <iletişim e-postası>.

Son güncelleme: <tarih>
```

Not: Hukuki sebep seçimi (m.5/2-c ve m.5/2-f) bir öneridir. Aydınlatma Yükümlülüğü Tebliği'nin asgari unsurları (sorumlunun kimliği, amaç, aktarım alıcıları ve amacı, toplama yöntemi ve hukuki sebep, m.11 hakları) yukarıda karşılanıyor. İçeriği bir hukukçuya gözden geçirtmek iyi olur.

---

## 9. Doğrulanmamış maddeler (toplu liste)

1. nuget.org Trusted Publishing "Select Scopes" ekranındaki alanların tam adları ve yeni paket yayınlama izninin varsayılanı.
2. ID önek rezervasyonunun bireysel ve yeni bir proje için kabul olasılığı.
3. release-please'de özel `data` tipinin release tetikleyip tetiklemediği.
4. .NET SDK'nın multi-arch OCI index'e `org.opencontainers.image.description` annotation'ı yazıp yazmadığı (GHCR açıklaması).
5. ACA güncel bölgesel fiyatları (en kötü durum yaklaşık $12–15/ay tahmini), ücretsiz egress kotası, ACA için Türkiye bölgesi olup olmadığı.
6. Public GHCR imajının ACA'da `registry set` olmadan `az containerapp update` ile çekilebilmesi.
7. Render Free'de imaj tabanlı servis desteği, instance özellikleri, deploy hook `imgURL` encode kuralı.
8. .NET 10 publish'te `index.html` için `.br/.gz` kopyası üretilip üretilmediği (base href `sed` sonrası).
9. `DecompressionStream('brotli')` desteğinin Chrome ve Safari'deki durumu (Firefox 147+ doğrulandı).
10. Gazetteer için Deflate ve Brotli boyut farkı. JS Brotli çözme süresi. Blazor boş uygulama indirme boyutu. AOT eşiği.
11. GitHub Pages'in `application/wasm` ve `application/octet-stream` için gzip uygulayıp uygulamadığı.
12. Blazor router'ın `target="_top"` bağlantılarını yakalamadığı.
13. DocFX `$schema` URL'si.
14. Codecov `files` girdisinde glob desteği.
15. .NET 10'da `ForwardedHeadersOptions.KnownNetworks` → `KnownIPNetworks` geçişi. OTel ASP.NET Core instrumentation'ın `url.query` maskelemesi.
16. KVKK yeterlilik kararı listesi, IP adresinin Kurul nezdindeki niteliği, VERBİS eşiği, Microsoft'un KVKK standart sözleşme süreci.

---

## 10. Kaynaklar

**NuGet**
- Trusted Publishing — https://learn.microsoft.com/nuget/nuget-org/trusted-publishing
- API anahtarı ömrü (3 Ağu 2026) — https://devblogs.microsoft.com/dotnet/strengthening-nuget-supply-chain-security-reducing-api-key-lifetime/
- NuGet/login — https://github.com/NuGet/login
- ID prefix reservation — https://learn.microsoft.com/nuget/nuget-org/id-prefix-reservation
- Trusted Publishing duyurusu — https://devblogs.microsoft.com/dotnet/enhanced-security-is-here-with-the-new-trust-publishing-on-nuget-org/

**Sürümleme**
- MinVer README ve CHANGELOG — https://github.com/adamralph/minver
- release-please-action — https://github.com/googleapis/release-please-action
- release-please manifest config — https://github.com/googleapis/release-please/blob/main/docs/manifest-releaser.md
- release-please customizing — https://github.com/googleapis/release-please/blob/main/docs/customizing.md

**Container**
- Containerize a .NET app reference — https://learn.microsoft.com/dotnet/core/containers/publish-configuration
- Registry authentication — https://github.com/dotnet/sdk-container-builds/blob/main/docs/RegistryAuthentication.md
- Ubuntu Chiseled + .NET — https://github.com/dotnet/dotnet-docker/blob/main/documentation/ubuntu-chiseled.md
- Image variants (extra = icu + tzdata) — https://github.com/dotnet/dotnet-docker/blob/main/documentation/image-variants.md
- GHCR — https://docs.github.com/packages/working-with-a-github-packages-registry/working-with-the-container-registry

**Azure / Render / teklifler**
- ACA billing — https://learn.microsoft.com/azure/container-apps/billing
- ACA pricing — https://azure.microsoft.com/pricing/details/container-apps/
- ACA scaling — https://learn.microsoft.com/azure/container-apps/scale-app
- ACA log options — https://learn.microsoft.com/azure/container-apps/log-options
- ACA GitHub Actions — https://learn.microsoft.com/azure/container-apps/github-actions
- azure/login — https://github.com/Azure/login
- azure/container-apps-deploy-action — https://github.com/Azure/container-apps-deploy-action
- Render Free — https://render.com/docs/free · Deploy an image — https://render.com/docs/deploy-an-image · Deploy hooks — https://render.com/docs/deploy-hooks
- Azure for Students — https://learn.microsoft.com/azure/education-hub/about-azure-for-students · FAQ — https://learn.microsoft.com/azure/education-hub/faq
- GitHub Student Developer Pack — https://docs.github.com/education/explore-the-benefits-of-teaching-and-learning-with-github-education/github-education-for-students/apply-to-github-education-as-a-student

**Blazor WASM / Pages / DocFX**
- Blazor WASM on GitHub Pages — https://learn.microsoft.com/aspnet/core/blazor/host-and-deploy/webassembly/github-pages?view=aspnetcore-10.0
- Örnek workflow, 404.html, index.html, decode.js — https://github.com/dotnet/blazor-samples/tree/main/BlazorWebAssemblyXrefGenerator
- Build tools & AOT — https://learn.microsoft.com/aspnet/core/blazor/webassembly-build-tools-and-aot?view=aspnetcore-10.0
- WASM features (threads, ICU) — https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/features.md
- Brotli browser desteği yok — https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.IO.Compression.Brotli/Directory.Build.props
- Blazor threading issue — https://github.com/dotnet/aspnetcore/issues/54365
- DecompressionStream — https://developer.mozilla.org/docs/Web/API/DecompressionStream/DecompressionStream
- upload-pages-artifact (nokta dosyaları) — https://github.com/actions/upload-pages-artifact/releases
- GitHub Pages limits — https://docs.github.com/pages/getting-started-with-github-pages/github-pages-limits
- DocFX — https://dotnet.github.io/docfx/ · releases — https://github.com/dotnet/docfx/releases

**Repo hijyeni**
- Dependabot options — https://docs.github.com/code-security/dependabot/working-with-dependabot/dependabot-options-reference
- CodeQL build options — https://docs.github.com/code-security/reference/code-scanning/codeql/build-options-for-compiled-languages
- CodeQL Action v3 deprecation — https://github.blog/changelog/2025-10-28-upcoming-deprecation-of-codeql-action-v3/
- action-semantic-pull-request — https://github.com/amannn/action-semantic-pull-request
- codecov-action — https://github.com/codecov/codecov-action
- Issue forms syntax — https://docs.github.com/communities/using-templates-to-encourage-useful-issues-and-pull-requests/syntax-for-issue-forms

**KVKK**
- Standart Sözleşme Bildirim Modülü duyurusu — https://www.kvkk.gov.tr/Icerik/8043/Standart-Sozlesme-Bildirim-Modulu-Hakkinda-Kamuoyu-Duyurusu
- Yönetmelik m.14 (bildirim) — https://prighter.com/resources/laws/turkish-kvkk/by-laws/the-procedures-and-principles-for-the-transfer-of-personal-data-abroad/articles/article-14
- Yurt dışı aktarım rehberi özeti — https://www.erdem-erdem.av.tr/bilgi-bankasi/yurt-disina-kisisel-veri-aktarimi-rehberi-neleri-duzenliyor
- 2026 idari para cezaları — https://cottgroup.com/en/legislation/item/administrative-fine-amounts-in-kvkk-for-2026
- Standart sözleşme bildirim yükümlülüğü — https://cottgroup.com/en/blog/kvkk-gdpr/item/standard-contract-notification-obligation-and-penalties
