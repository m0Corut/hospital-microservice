# WebUI Kuralları (Hastane.WebUI, port 5010)

Yeni ekran yazarken yalnız bu belge yeterli olmalı. Ortak katmana (`Infrastructure/`, `Program.cs`, `_Layout`, `site.css`) dokunma; eksik varsa ayrıca ele al. Yeni NuGet paketi ekleme.

## 1. Klasör ve alan düzeni

```
Controllers/            herkese açık sayfalar (kök alan): Home, Account, Error, + Branches, Doctors...
Areas/{Admin,Doctor,Patient}/Controllers|Views   role özel paneller (/Admin, /Doctor, /Patient)
Clients/                I<Ad>Client + <Ad>Client: API yolları YALNIZ burada
Models/Api/             API'den gelen/giden JSON sınıfları (servis DTO'sunun WebUI kopyası, kopyala-yapıştır değil elle)
ViewModels/<Alan>/      görünümün modeli + form modeli (DataAnnotations, Türkçe hata mesajı)
Views/<Controller>/     kök alan görünümleri; Areas/<Alan>/Views/<Controller>/ alan görünümleri
Infrastructure/         ortak altyapı (DOKUNMA)
```

- Her ekran KENDİ dosyalarını ekler; paylaşılan dosyayı düzenlemez. Yeni controller/client/model/view eklemek çakışma yaratmaz: `Program.cs` değişmez (client'lar otomatik kaydedilir, bkz. 2).
- Dosya adı çakışması: istemciyi alan adıyla adlandır (`DoctorClient`, `AppointmentClient`); ViewModel'leri `ViewModels/<Alan>/` altına koy.
- Alan görünümünde `_ViewImports`/`_ViewStart` hazır. Kök alanın görünümü `Views/<Controller>/<Action>.cshtml`.

## 2. Yeni ekran: controller + client + model + view

1. **Model (`Models/Api/XApiModel.cs`)**: API JSON'unun alanları (camelCase gelir, büyük/küçük fark etmez; enum metin gelir).
2. **Client (`Clients/XClient.cs`)**: `IXClient` + `XClient(IGatewayClient gateway)`. Aynı dosyada. Ad kuralı: `Hastane.WebUI.Clients` ad alanı, sınıf `...Client`, arayüz `I...Client`: otomatik `AddScoped` kaydı bu kurala bağlı.
   Yollar `"/gateway/..."` (rota tablosu: `src/Gateway/Hastane.Gateway/ocelot.json`). Servis adresi/portu WebUI'da asla yazılmaz.
3. **ViewModel (`ViewModels/<Alan>/`)**: görünüm için gereken şey; API modelini doğrudan forma bağlama (form alanları = ViewModel).
4. **Controller**: ince. Client çağırır, ViewModel kurar, `View(model)` döner. İş kuralı API'dedir.
5. **View**: Türkçe metin, Bootstrap 5, `_Layout` otomatik.

```csharp
public sealed class DoctorClient(IGatewayClient gateway) : IDoctorClient
{
    public Task<ApiResult<DoctorApiModel>> GetAsync(string id, CancellationToken ct)
        => gateway.GetAsync<DoctorApiModel>($"/gateway/doctors/{Uri.EscapeDataString(id)}", ct: ct);
}
```
Yol parçası kullanıcı girdisiyse `Uri.EscapeDataString`.

## 3. API çağrısı ve hata işleme (tek desen)

`IGatewayClient` metotları `ApiResult<T>` döner: `Kind`, `StatusCode`, `Value`, `Problem`. İşleyiş:

| API yanıtı | Varsayılan davranış (sen bir şey yazmazsın) |
|---|---|
| 2xx | `result.IsSuccess`, `result.Value` dolu (`TryGetValue(out var v)`) |
| 400 / 422 | `Kind=ValidationFailed`, `result.Problem.Errors`: formda `ModelState.AddApiProblem(result)` |
| 409 | `Kind=Conflict`, `Problem.Detail`: `AddApiProblem` ile göster |
| 423, diğer 4xx | `Kind=Locked / OtherError`, sonuç olarak döner |
| **401** | istemci `GatewayException` fırlatır → `GatewayExceptionFilter`: oturum kapanır, `/Account/Login?returnUrl=...` (yalnız GET'te returnUrl) |
| **403** | → 403 "Yetkiniz yok" sayfası |
| **404** | → 404 "Sayfa bulunamadı" sayfası |
| bağlantı hatası, zaman aşımı, 5xx | → 503 "Servis şu an yanıt vermiyor" sayfası (yığın izi yok) |

- try/catch YAZMA. 401/403/404/503'ü ekran içinde kendin ele almak istersen çağrıya `ApiFailures` ver: `gateway.GetAsync<T>(path, ApiFailures.NotFound, ct)`; o zaman istemci fırlatmaz, `result.Kind == NotFound` döner (örn. ana sayfa `ApiFailures.All` ile servis kapalıyken uyarıyla açılır).
- Başarıyı her zaman kontrol et: `if (!result.IsSuccess) ...` ya da `TryGetValue`. `result.Value` başarısızlıkta `null`.
- Tüm async metotlar `CancellationToken ct` alır ve iletir.
- Bearer token'ı sen eklemezsin: `BearerTokenHandler` oturum açıksa çerezden ekler. Token'ı HTML'e, ViewData'ya, günlüğe YAZMA.

## 4. Form + antiforgery + ModelState

```cshtml
<form asp-action="Create" asp-controller="Appointments" asp-area="Patient" method="post" novalidate>
    <div asp-validation-summary="All" class="alert alert-danger py-2 small"></div>
    <input asp-for="Note" class="form-control" />
    <span asp-validation-for="Note" class="text-danger small"></span>
    <button type="submit" class="btn btn-primary">Kaydet</button>
</form>
```
- `method="post"` + `<form asp-...>` etiket yardımcısı antiforgery gizli alanını OTOMATİK ekler. Elle `<form action=...>` yazma. Global `AutoValidateAntiforgeryToken` var: tüm POST/PUT/DELETE belirteç ister, `[ValidateAntiForgeryToken]` yazma.
- Silme/durum değiştirme gibi işlemler de POST formudur; GET ile veri değiştirme YOK.
- Post-Redirect-Get: başarıdan sonra `RedirectToAction`, mesaj için `SetSuccess("...")` (layout gösterir).

```csharp
[HttpPost]
public async Task<IActionResult> Create(CreateAppointmentViewModel model, CancellationToken ct)
{
    if (!ModelState.IsValid) return View(model);
    var result = await client.CreateAsync(model, ct);
    if (!result.IsSuccess) { ModelState.AddApiProblem(result); return View(model); }
    SetSuccess("Randevunuz oluşturuldu.");
    return RedirectToAction(nameof(Index));
}
```
- `AddApiProblem`: API'nin alan hatalarını (`errors: { "Email": [...] }`) aynı adlı alana, eşleşmeyenleri model düzeyine koyar. Görünümde `asp-validation-summary="All"` bulunsun.
- `returnUrl` kullanıyorsan yalnız `RedirectToLocal(returnUrl)` ile (`AppController`): yerel değilse ana sayfa. Kendi `Redirect(returnUrl)` YAZMA (open redirect).
- Kimlik alanlarını (PatientId, DoctorId) formdan/gövdeden gönderme; API bunları token'dan alır.

## 5. Yetki etiketleri

- **Her action'da açık yetki etiketi zorunlu:** `[AllowAnonymous]` ya da `[Authorize]` (sınıf ya da action düzeyi). Eksik olan action'ı test yakalar (`Every_controller_action_declares_authorization_explicitly`). Varsayılan "açık" bırakma.
- **Alan controller'ları**: `Areas/<Alan>/Controllers/<Alan>AreaController` tabanından türet (zaten `[Area]` + `[Authorize(Roles=...)]` taşır). Alan controller'ına rol etiketi tekrar yazma.
- **TUZAK:** sınıf düzeyi `[Authorize(Roles="Admin")]` + action düzeyi `[Authorize(Roles="Doctor")]` birbirini AÇMAZ, VE'lenir: kullanıcı hem Admin hem Doctor olmalı, yoksa kimse giremez. Bir alan içinde farklı rol gerekiyorsa o ekran o rolün alanına gitsin. Sınıfta rolsüz `[Authorize]`, action'da `[AllowAnonymous]` ise action'ı açar (kasıtlı kullan, alan controller'ında kullanma).
- Rol sabitleri `HastaneRoles.Admin/Doctor/Patient` (`Hastane.Shared`), string yazma. Menüde `User.IsInRole(...)`.
- Giriş gerektiren kök-alan sayfası: `[Authorize]`. Rol yanlışsa 403 sayfası, anonimse girişe yönlendirme otomatik.
- Görünümde bir şeyi gizlemek güvenlik değildir: asıl kapı controller etiketi ve API'nin kendi yetkisidir.

## 6. Adlandırma ve metin

- Kod, sınıf, dosya, route İngilizce (`DoctorsController`, `SearchDoctorsViewModel`); arayüz metni, hata mesajı, `[Display(Name=...)]` Türkçe.
- Controller çoğul isim (`Doctors`, `Branches`, `Appointments`), alan başına `HomeController` (hazır) = panel ana sayfası.
- Menüdeki hedefler: `/Branches`, `/Doctors`, `/Admin`, `/Doctor`, `/Patient`. Bu yolları kullanan controller'lar yazılınca bağlantılar çalışır.
- Tarih/saat: API UTC döner, ekranda yerel saate çevir (`ToLocalTime()`), biçim `dd.MM.yyyy HH:mm`. Para: `decimal` + para birimi kodu.
- Kullanıcı girdisini `@` ile yaz (otomatik HTML kodlama); `@Html.Raw` YASAK. Sunucu hata ayrıntısını (özel durum, yığın) kullanıcıya gösterme.
- Stil: Bootstrap 5 sınıfları + `site.css` tasarım sistemi (bkz. 9). Doccure'un HTML/CSS/görsellerini kopyalama (lisanslı); düzen ve his kendi kodumuzla yeniden üretilir.

## 7. Test deseni

Proje: `tests/Hastane.WebUI.Tests`. Yeni ekran için kendi dosyanı ekle (`<Alan>Tests.cs`); `WebUiTestHost.cs`'ye ekleme gerekirse yalnız yeni yardımcı ekle, var olanı değiştirme.

```csharp
await using var host = await WebUiTestHost.StartAsync();          // sahte gateway (gerçek Kestrel, rastgele port) + WebUI
host.Gateway.MapJson("GET", "/gateway/doctors/d1", 200, new { id = "d1", firstName = "Ali" });
var client = host.CreateClient();                                  // yönlendirme otomatik DEĞİL, çerez var
await host.LoginAsync(client, WebUiTestHost.NewToken("Patient"), roles: ["Patient"]);   // gerçek giriş akışı
var html = await (await client.GetAsync("/Patient/Appointments")).Content.ReadHtmlAsync();
Assert.Contains("Ali", html);
var sent = host.Gateway.Requests.Last(r => r.Path == "/gateway/doctors/d1");   // Authorization: Bearer ... kontrolü
```
- Her ekran için en az: başarılı sayfa, yanlış rol 403, anonim girişe yönlendirme, API hata durumu (400 form hatası ya da 404/503 sayfası), ve formlarda antiforgery'li başarılı POST (`host.PostFormAsync`).
- Gerçek servis, Docker, veritabanı gerekmez. Gerçek JWT anahtarı kullanılmaz (`UseTestJwt` fabrikada hazır).
- Razor modelden gelen Türkçe harfleri HTML varlığına çevirir: yanıtı `ReadHtmlAsync()` ile oku (kodu çözer).
- Test kapsamında sahte denetleyici gerekirse test projesine koy (örnek: `ProbeController`); üretim koduna test ucu ekleme.

## 8. Güvenlik özeti (değiştirme)

Çerez: HttpOnly, SameSite=Lax, SameAsRequest, süre = token ömrü (kayan yok). Token yalnız şifreli çerezin içinde; tarayıcıya JS ile okunur token verilmez. Giriş sırasında gelen JWT, Shared'daki açık anahtarla (RS256, issuer, audience, ömür) doğrulanır. Data Protection anahtarları geliştirmede `%LOCALAPPDATA%\hastane-webui\keys`. Geliştirici hata sayfası kapalı; hatalar `/Error/{kod}` sayfasına gider.

## 9. Tasarım sistemi ve ortak görünüm parçaları (Gece Dalgası N2)

- **Renk/tipografi/bileşen:** `wwwroot/css/site.css` (`--h-*` değişkenleri; mavi + turkuaz). Sistem yazı tipi, dış CDN yok. İkonlar yerel Bootstrap Icons 1.11.3 (`wwwroot/lib/bootstrap-icons`, `<i class="bi bi-...">`). Durum rozetleri Bootstrap `text-bg-*` sınıflarıyla kalır (CSS yumuşak hap biçimine çevirir).
- **Görseller:** `wwwroot/img/` altında kendi çizdiğimiz SVG'ler (logo, hero, giriş, boş durum, `doctors/avatar-01..12.svg`, `clinics/clinic-01..06.svg`, `branches/*.svg`). Dış/telifli görsel yok. Seed verisi `/img/doctors/avatar-NN.svg` ve `/img/clinics/clinic-NN.svg` yollarını kullanır.
- **Ortak parçalar (`Views/Shared`):** `_Avatar` (fotoğraf yoksa baş harfli SVG; görsel adresi yalnız yerel yol ya da http/https, bkz. `UiKit.SafeImageUrl`), `_Stars`, `_EmptyState`, `_DoctorCard` (arama ve ana sayfa), `_PanelSide` (profil kartlı kenar menü), `_PrintLayout` (menüsüz yazdırma sayfası, `window.print()`), `_PrescriptionForm` (reçete formu: doktor ve yönetici ortak). Yardımcılar `ViewModels/Shared/UiKit.cs`.
- **Panel iskeleti:** her alanın `_ViewStart.cshtml`'i `_PatientLayout` / `_DoctorLayout` / `_AdminLayout` seçer; menü öğeleri bu üç dosyada. Yeni panel ekranı eklerken menüye oradan eklenir; ekranlara ayrıca nav partial'ı konmaz.
- **Yazdırma sayfası:** `Layout = "~/Views/Shared/_PrintLayout.cshtml"`, `ViewData["DocTitle"]`, `ViewData["BackUrl"]`. Yetki ve sahiplik ilgili Details ile aynıdır.
- **Yeniden planlama:** `IAppointmentRescheduleClient` (PUT appointments/{id}); hasta, doktor ve yönetici aynı uç noktayı kullanır, şube/doktor/durum formdan alınmaz.
