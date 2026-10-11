---
title: Hastane Mikroservis — Öğrenme Notları
created: 2026-10-08
modified: 2026-10-10
type: note
status: active
tags: [hastane-mikroservis, ogrenme, dotnet, mikroservis]
---
# Öğrenme Notları

Projede öğrenilen her kavram için kısa bir giriş. Biçim:

```
### Kavram adı
- **Ne:** tek cümle
- **Neden:** hangi sorunu çözüyor, olmasa ne olurdu
- **Projede:** hangi serviste / dosyada
- **Ders:** ilgili kurs dersi (izlemek istersen)
```

Bölüm sırasıyla eklenir.

## Giriş

### Neden mikroservis? (hastane benzetmesi)
- **Tek cümle:** Uygulamayı tek büyük program yerine, her biri kendi işini yapan küçük programlara böleriz.
- **Benzetme:** Gerçek hastane zaten böyle: poliklinik, laboratuvar, eczane, vezne ayrı birimler; her birinin kendi personeli ve
  kendi kayıt dolabı var, birbirlerine form / telefonla sorarlar. Laboratuvar cihazı bozulunca poliklinik muayeneye devam eder.
- **Olmasa ne olurdu (monolit):** Eczane kodundaki bir hata bütün programı çökertir, o sırada kimse randevu da alamaz.
  Pazartesi 08:00 randevu yoğunluğunda bütün programı büyütmek gerekir; mikroserviste yalnız Appointment'tan 5 kopya açılır.
- **Bedeli:** Daha karmaşık (çok servis, çok port, servisler arası iletişim). Küçük projede monolit daha mantıklı;
  kurs, büyük şirketlerin kullandığı yapıyı öğretmek için mikroservis kullanıyor.
- **Karar (Muhammet, 2026-10-09):** veritabanları kurstaki gibi (MongoDB, SQL Server, MariaDB, Redis, Elasticsearch: hoca "5 farklı veritabanı" diyor).

### Her servis kendi verisinin sahibi (database per service)
- **Ne:** Bir servisin tablolarına yalnız o servis dokunur; diğerleri veriyi onun API'sinden ister.
- **Neden:** Doctor servisi Appointment tablosunu doğrudan okursa, Appointment ekibi `AppointmentDate` kolonunu
  `StartTime` yapınca Doctor servisinin SQL'i "Invalid column name" hatası verir; Doctor'da tek satır değişmemiştir.
  API arkasında ise Appointment kolonu değiştirir, DTO'yu aynı bırakır, kimse etkilenmez. (Not: kolon *eklemek* genelde
  bozmaz; *ad değiştirmek, silmek, tip / anlam değiştirmek* bozar.)
- **Projede:** her servisin kendi bağlantı cümlesi; başka servisin verisi HTTP ile (ileride RabbitMQ).
- **Ders:** 1 (Giriş).

### Farklı veritabanı türleri (polyglot persistence)
- **Kısa cevap:** Zorunlu değil, hepsi tek tür olabilirdi. Kurs farklılarını öğretmek için kullanıyor; her türün iyi olduğu iş farklı.
- **Benzetme:** SQL = Excel tablosu (sabit sütun, sıkı kural) · MongoDB = her doktor için bir dosya (içine istediğin kadar liste) ·
  Redis = beyaz tahta (çok hızlı yaz-sil, kalıcı değil).
- **Örnek:** Doktor ödülleri Excel'de "Ödül1, Ödül2, Ödül3…" kaç sütun açacaksın? 10 ödüllü doktor gelince yetmez → SQL'de ikinci
  tablo açılır. MongoDB'de ödüller doktorun dosyasının içinde bir listedir, kaç tane olursa olsun.
- **Ne:** Her servis verisinin şekline uygun veritabanını seçer. Sahiplik kuralından **ayrı** bir karar; hepsi SQL de olabilirdi.
- **Neden:** Doktor + eğitim / deneyim / ödül / lokasyon listeleri MongoDB'de tek doküman, tek okuma (SQL'de 5 tablo + join).
  Randevu çakışma kuralı, ilişkiler, transaction → SQL. Sepet geçici ve çok sık yazılır → Redis (bellekte, süre sonunda silinir).
- **Dürüst not:** Gerçek projelerde çoğunlukla 1–2 tür kullanılır. Kurs 5 türü (MongoDB, SQL Server, MariaDB, Redis, Elasticsearch)
  öğretmek için kullanıyor; MariaDB'nin SQL Server'a karşı burada özel bir üstünlüğü yok.
- **Ders:** 2–3 (MongoDB), 23 (SQL Server), 134 (Redis), 139 (MariaDB).

### "Servis" nedir?
- **Tek cümle:** Ayrı çalışan küçük bir program; ekranı yok, kendi portundan gelen soruya JSON ile cevap verir.
- **Benzetme:** Telefondaki uygulamalar gibi; Spotify çökse WhatsApp çalışır.
- **Örnek:** Branch servisi = `src/Services/Branch/Hastane.Branch.Api/` klasörü → `dotnet run` → 5101 portunu dinler →
  `http://localhost:5101/api/branches` → `[{ "name": "Kardiyoloji" }]`. Proje tam çalışırken 13 program aynı anda açık
  (11 servis + Gateway 5000 + WebUI 5010).

### API ve MVC: ikisini birlikte kullanıyoruz
- **Servisler = ASP.NET Core Web API** (yalnız veri verir / alır). **Ekranlar = ASP.NET Core MVC** (`Hastane.WebUI`, tek proje).
- **Benzetme:** MVC salondaki garson, API'ler mutfaktaki aşçılar.
- **Akış:** Tarayıcı → WebUI → Gateway (5000) → Branch API (5101) → MongoDB; geri dönüşte JSON → HTML.
- **Neden ayrı:** Yarın mobil uygulama yapılırsa aynı API'ler kullanılır, yalnız yeni ekran yazılır.
- "ASP.NET MVC" eski .NET Framework'ün adı; biz yeni **ASP.NET Core** kullanıyoruz.
- **Ön yüz teknolojileri:** Razor (.cshtml), Layout + View Component, Bootstrap tabanlı admin teması (öneri AdminLTE, karar B7'de),
  ikon kütüphanesi, SignalR JavaScript istemcisi (çağrı ekranı). React gibi ayrı uygulama yok; sayfayı sunucu pişirir.

### Gerçek hayattan: e-ticarette neden farklı veritabanları (Trendyol tipi yapı)
- Ürün bilgisi → asıl veritabanı (resmi kayıt defteri, kaybolmamalı).
- Arama → Elasticsearch (kitabın dizini): "ayakabı" yazım hatasını anlar, 100 milyon üründe milisaniyede bulur.
  Normal veritabanı birebir eşleşme arar → 0 sonuç. Elasticsearch'teki veri asıl kaydın **kopyası**.
- Sepet → Redis (kasadaki beyaz tahta): hızlı yaz-sil, geçici.
- **Sonuç:** Hastane kısmında (branş, doktor, randevu) farklı türe gerçek ihtiyaç yok, kurs göstermek için kullanıyor;
  ürün / arama / sepet kısmında (B18–B21) ihtiyaç gerçek.

### Redis
- **Bellek (RAM) ve disk:** Disk = arşiv dolabı (kalıcı, yavaş, büyük). RAM = çalışma masası (çok hızlı, küçük, pahalı,
  elektrik gidince silinir). Normal veritabanı diske yazar, Redis RAM'de tutar.
- **Nerelerde kullanılır:** önbellek (doktor listesi bir kez Redis'e, sonraki 9.999 kişiye oradan), SMS doğrulama kodu
  (3 dk sonra kendiliğinden silinir), şifre deneme sınırı, giriş oturumu, canlı sayaç ("25 kişi inceliyor"), oyun sıralaması.
- **Redis olmasaydı (sepet SQL'de):** her tıklama diske yazma → Efsane Cuma'da veritabanı tıkanır, aynı veritabanındaki
  sipariş / ödeme de yavaşlar (tek gişe benzetmesi); terk edilmiş sepetler birikir, temizlik programı gerekir
  (Redis'te `EX 259200` ile 3 gün sonra kendiliğinden silinir); daha pahalı sunucu gerekir.
  Küçük sitede (bizim eczane) fark görülmez; fark milyonlarca kullanıcıda çıkar.
- **React ≠ Redis:** React ekran kütüphanesi (tarayıcıda, bizdeki karşılığı MVC), Redis veri saklama (sunucuda).
  Sepet tarayıcıda da tutulabilir (`localStorage`) ama o zaman telefonda görünmez. Sunucuda Redis yerine: Memcached,
  Valkey, normal veritabanı.
- **Telefon + bilgisayarda aynı sepet nasıl?** Redis'in belleği **sitenin sunucusunun** RAM'i, senin cihazın değil.
  İki cihaz da aynı sunucuya `sepet:muhammet123` diye sorar (bir kafede aynı masaya bırakılan not). Sepet hesaba bağlı;
  giriş yapmadan eklenen sepet diğer cihazda görünmez.
- **Sunucu kapanırsa?** Redis birkaç saniyede bir diske ara kayıt yazar (açılınca geri yükler) ve başka makinede kopyası
  çalışır. RAM'in hızı + diskin güvencesi.
- **Ders:** 133–138 (Redis kurulumu, ürün), 146–153 (sepet, checkout).

## Dalga 0: İskelet

### Docker ve docker-compose
- **Tek cümle:** Veritabanlarını bilgisayara kurmak yerine, her birini kendi kutusunda (konteyner) çalıştırırız.
- **Benzetme:** Taşınabilir ocak gibi: mutfağa tesisat çekmeden fişe takarsın, işin bitince kaldırırsın.
- **Olmasa ne olurdu:** MongoDB + SQL Server + MariaDB + Redis'i Windows'a tek tek kurarsın; sürüm çakışır, kaldırması zor,
  arkadaşın aynı ortamı kuramaz. Compose'la `docker compose up -d` tek komut.
- **Projede:** `docker-compose.yml` (mongo, sqlserver, seq), şifreler `.env` (depoya girmez), şablonu `.env.example`.

### Solution ve global.json / Directory.Build.props
- **Solution (`HastaneMikroservis.slnx`):** bütün projeleri bir arada açan liste; `dotnet build` hepsini derler.
- **`global.json`:** proje hep .NET 10 SDK ile derlenir (bilgisayarda .NET 9 da var).
- **`Directory.Build.props`:** her projeye otomatik uygulanan ortak ayar; 15 projede aynı satırı tekrarlamayız.
- **`Directory.Packages.props`:** paket sürümleri tek yerde; iki servis aynı paketin farklı sürümünü kullanmaz.

## B8-B9 JWT ve yetkilendirme (Dalga 3A, çekirdek)

Kod: `src/Shared/Hastane.Shared/Security/`, Identity yeni uçlar. Yol haritası: `KOD-HARITASI.md` 8g. Sözleşme: `JWT-TASARIM.md`.

### JWT nedir
- **Tek cümle:** İçinde "ben kimim, rolüm ne, ne zamana kadar geçerliyim" yazan, sunucunun **imzaladığı** metin.
- **Benzetme:** Kapıdaki imzalı ve mühürlü yaka kartı: kartı okuyan veritabanına sormadan kimliği görür, mühür sahte kartı ele verir.
- **Olmasa ne olurdu:** İmzasız olsa kimse `role: Admin` yazıp kendine kart basardı. Testte `Me_BrokenSignature_Returns401` bunu kanıtlar.
- **Kodda:** `JwtTokenService.cs:32-50` kartı basar, `JwtAuthenticationExtensions.cs:33-34` mührü kontrol eder.

### RS256 ve HS256 (neden kurstan ayrıldık)
- **Tek cümle:** RS256'da imzayı yalnız **özel anahtar** atar, herkes **açık anahtarla** kontrol eder; HS256'da tek ortak parola hem atar hem kontrol eder.
- **Benzetme:** HS256: kasa anahtarı herkeste, kopyalayan kasadan para basar. RS256: yalnız banka mühür basar; şubeler mührün resmine bakıp doğrular.
- **Olmasa ne olurdu:** 12 servisten biri ele geçse o servis ortak parolayla sahte Admin token basardı. RS256'da çalınan servisten yalnız açık anahtar çıkar, token basılamaz. Üstelik doğrulama sırasında Identity'ye bağımlılık yok.
- **Kodda:** `JwtTokenService.cs:21` (özel anahtar yalnız Identity), `Hastane.Shared/jwt-public.pem`, `JwtKeys.cs:30` (`kid`).

### Token doğrulamada kontrol edilen 6 şey
- **Tek cümle:** İmza, süre, issuer, audience, algoritma ve saat toleransı; hepsi geçerse kart kabul edilir.
- **Benzetme:** Kapıda önce mühre, sonra son kullanma tarihine, hangi kurumdan çıktığına, hangi binaya yazıldığına ve kartın nasıl mühürlendiğine bakılır; saat farkına küçük tolerans tanınır.
- **Olmasa ne olurdu:** `alg: none`: "imzasızım" diyen token imza kontrolünü atlatırdı. **Alg confusion**: saldırgan herkesin bildiği AÇIK anahtarı HS256 parolası yapıp sahte kart basardı; `ValidAlgorithms` yalnız RS256 olduğu için reddedilir (`Me_Hs256Token...AlgConfusion_Returns401`). 5 dk varsayılan tolerans: süresi dolmuş token 5 dk daha yaşardı; biz 30 sn.
- **Kodda:** `JwtAuthenticationExtensions.cs:34-45`; saldırı üreticileri `TestJwt.cs`.

### Claim'ler ve `MapInboundClaims=false`
- **Tek cümle:** Claim kartın üstündeki tek bir bilgi satırı (`sub`, `role`); eski .NET bunların adını uzun adreslere çevirirdi, biz kapattık.
- **Olmasa ne olurdu:** `FindFirst("sub")` boş döner, rol claim adı bulunamadığı için `IsInRole("Admin")` hep false olur, herkes 403 alır.
- **Kodda:** `JwtAuthenticationExtensions.cs:28`, `:47-48`; okuyan `CurrentUser.cs:27-33`.

### Rol bazlı yetki ve politika
- **Tek cümle:** Politika "bu kapıdan kim geçer" kuralına verilen isim; ucun üstüne `[Authorize(Policy=...)]` yazılır.
- **Olmasa ne olurdu:** `Roles="Admin"` metni 12 serviste dağınık kalır; birinde "admin" yazılırsa o uç sessizce kimseye açılmaz.
- **Kodda:** `HastanePolicies.cs:16-25`; kullanım `RolesController.cs:11`, `UsersController.cs:41,59,64,69`.

### 401 ve 403
- **Tek cümle:** 401 "kimsin bilmiyorum" (kart yok ya da geçersiz); 403 "seni tanıyorum ama bu odaya iznin yok".
- **Olmasa ne olurdu:** İkisi karışırsa istemci süresi dolmuş token'ı "yetkin yok" sanıp kullanıcıyı yanlış yönlendirir; güvenlik logları ayırt edilemez.
- **Kodda:** `ProblemResponses.cs:11-23`, `JwtAuthenticationExtensions.cs:53-59`.

### Son Admin koruması
- **Tek cümle:** Sistemdeki son Admin'in Admin rolü alınamaz (say + sil tek SERIALIZABLE işlemde).
- **Benzetme:** Binanın tek yedek anahtarını kasaya kilitleyip kasa anahtarını içeride bırakmak.
- **Olmasa ne olurdu:** Tek Admin kendi rolünü kaldırırsa roller bir daha atanamaz; veritabanına elle girmek gerekir. İki Admin aynı anda birbirini düşürebileceği için sayma ve silme aynı işlemde.
- **Kodda:** `RoleService.cs:61-98` (sayım `:76-81`).

### İlk Admin tohumlama ve sırların yeri
- **Tek cümle:** Admin'i atayacak bir Admin yoksa tavuk-yumurta olur; o yüzden açılışta bir kez, sırlarla (`Seed:AdminEmail`, `Seed:AdminPassword`) verilen bilgilerle ilk Admin yaratılır. Mevcut kullanıcıyı Admin YAPMAZ (önceden kayıt olup yükselmeyi kapma saldırısı).
- **Sırlar:** özel anahtar yalnız user-secrets / ortam değişkeni; `appsettings.json`'da boş; açık anahtar depoda.
- **Kodda:** `AdminSeeder.cs:19-56`, `scripts/new-jwt-keys.ps1`, `IdentityJwtOptionsValidator.cs`.

### Anahtar sızıntısı olayı (ders)
- Anahtar betiği ilk çalışınca kontrol için `user-secrets list` çalıştırıldı ve çok satırlı özel anahtar terminal çıktısına düştü. Hemen yeni çift üretildi (eski anahtar artık geçersiz). **Ders:** sır içeren komutların çıktısı da "sızıntı yüzeyi"dir; sırları listeleyen komut çalıştırılmaz.

### Yetki etiketini unutmama: AuthorizationCoverage
- **Tek cümle:** Test, her denetleyici ucunun ya `[Authorize]` ya da bilerek `[AllowAnonymous]` taşıdığını otomatik denetler.
- **Olmasa ne olurdu:** Yeni bir uç eklenip etiket unutulursa sessizce herkese açık kalır; kimse fark etmez. Test kırılır ve uyarır. Hiç uç taranmadıysa test kendisi hata verir (sahte yeşil olmasın).
- **Kodda:** `tests/Hastane.TestSupport/AuthorizationCoverage.cs`, `tests/Hastane.Identity.Tests/AuthorizationCoverageTests.cs`.

## B1 Branch

Kod: `src/Services/Branch/Hastane.Branch.Api/` (port 5101, MongoDB `hastane_branch`), test 9/9. CRUD = Create, Read, Update, Delete.

### Entity ve DTO
- **Tek cümle:** Entity veritabanındaki kaydın şekli, DTO dışarıya verilen / dışarıdan alınan form.
- **Benzetme:** Hastane arşivindeki dosya (entity) ile hastaya verilen özet kâğıt (DTO). Dosyanın tamamı kimseye verilmez.
- **Olmasa ne olurdu:** Yarın `Branch`'e `InternalNote` (iç not) eklesek ve controller entity dönse, not `GET /api/branches` ile
  herkese açılır. Ayrıca `POST` ile istemci `Id` gönderip kaydın kimliğini kendisi seçmeye çalışabilir; `CreateBranchDto`'da
  `Id` alanı olmadığı için bu mümkün değil.
- **Kodda:** `Entities/Branch.cs:7`, `Dtos/CreateBranchDto.cs:5` (Id yok), `Dtos/ResultBranchDto.cs:3` (Id var),
  `Mapping/BranchMappingConfig.cs:15` (Create'te Id yok sayılır).
- **Ders:** 4 (Dto ve Mapping Sınıfı).
- **Branch'te neden gereksiz görünüyor:** Entity ile DTO'larda aynı alanlar var (fark yalnız `Id`). Değeri, alanlar
  farklılaşınca çıkar. Örnek (B5 Identity): `AppUser` entity'sinde `PasswordHash`, `TcKimlikNo`, `PhoneNumber` var.
  Entity dönülürse doktorun hasta listesi ekranına bunlar da gider (F12 ile görülür):
  ```json
  { "name": "Ali Yılmaz", "passwordHash": "AQAAAAIAAYag...", "tcKimlikNo": "12345678901" }
  ```
  DTO ile (`PatientSummaryDto { Name }`) yalnız `{ "name": "Ali Yılmaz" }` gider.
- **"Veritabanından yalnız ilgili alanları çeksek?"** Doğru fikir, zaten DTO bu: `Project(u => new { u.Name })` sonucunun
  bir tipi olmak zorunda; o tipin adı konmuş hali DTO (`class PatientSummaryDto { string Name }`).
- **Asıl fark içeri gelen veride (mass assignment):** Kayıt isteğine biri `"role": "Admin"` ekler. İstek entity'ye bağlanırsa
  `Role` dolar, kullanıcı kendini yönetici yapar. `RegisterDto`'da `Role` yok → görmezden gelinir.
  Dışarı giderken DTO = "ilgili alanları seç"; içeri gelirken DTO = "yalnız bu alanlar doldurulabilir" kapısı.
- **Next.js'te aynısı:** DTO'nun karşılığı Zod şeması.
  ```typescript
  // Tehlikeli: gelen her şey veritabanına
  await prisma.user.create({ data: await req.json() })
  // Doğru: yalnız şemadaki alanlar geçer
  const RegisterSchema = z.object({ name: z.string().min(2), email: z.string().email(), password: z.string().min(8) })
  const data = RegisterSchema.parse(await req.json())
  ```
  | İş | .NET | Next.js |
  |---|---|---|
  | Gelen veri kalıbı | `RegisterDto` | Zod şeması |
  | Doğrulama | `[Required]`, `[EmailAddress]` | `.min()`, `.email()` |
  | Veritabanı kaydı | Entity (`AppUser`) | Prisma modeli |
  | Dışarı giden | `ResultDto` | `select: { name: true }` / ayrı tip |

### Ayarların koddan ayrılması (IDatabaseSettings, IOptions, user-secrets)
- **Tek cümle:** Veritabanı adresi ve şifresi kodda değil, ayar dosyasında ve bilgisayardaki gizli bir kasada durur; kod oradan okur.
- **Benzetme:** Telefon rehberi: numarayı ezberden tuşlamazsın, rehberden seçersin; numara değişince rehberi güncellersin.
- **Olmasa ne olurdu:** (1) `new MongoClient("mongodb://hastane:Gizli123@...")` koda yazılsa proje GitHub'a çıkınca şifre
  herkese açılır, silinse bile git geçmişinde kalır. (2) Test, geliştirme, sunucu ortamları farklı adres ister; adres
  kodda olsaydı her ortam için kod değişirdi. Şimdi testler aynı kodu, ayarı ezerek kendi geçici MongoDB'sine bağlıyor.
- **Nerede duruyor:** `appsettings.json` → `ConnectionString` boş. Gerçek adres
  `%APPDATA%\Microsoft\UserSecrets\hastane-branch-api\secrets.json` (proje klasörünün dışı, git görmez).
  `.csproj` içindeki `<UserSecretsId>hastane-branch-api</UserSecretsId>` dolap numarası gibi: program o klasöre gider.
  Görmek için: `dotnet user-secrets list --project src/Services/Branch/Hastane.Branch.Api`.
- **Docker şifresi:** proje kökünde `.env`, `.gitignore`'da.
- **Sunucuda:** user-secrets yalnız geliştirme bilgisayarında; sunucuda aynı ayar ortam değişkeni (`DatabaseSettings__ConnectionString`).
- **Kodda:** `Settings/DatabaseSettings.cs`, `Program.cs:16`, `Services/BranchService.cs:16`. **Ders:** 3 (Entity Paketler ve IDatabaseSettings).

### Dependency Injection (DI)
- **Tek cümle:** Sınıf ihtiyacı olan şeyi kendisi üretmez, constructor'da ister; ASP.NET hazırlayıp verir.
- **Benzetme:** Garson mutfağa girip yemeği pişirmez, hazır tabağı alır; şef değişse garson aynı işi yapar.
- **Kodda:** `Program.cs:17` `AddScoped<IBranchService, BranchService>()` (menüye yaz), `BranchesController` constructor'ı
  (tabak ister), `BranchService.cs:16` (o da `IMapper` ve ayar ister). Hiçbir yerde `new BranchService(...)` yok.
- **Olmasa ne olurdu:** (1) Controller `new BranchService(mapper, ayarlar)` yazsa mapper ve ayarı da elle kurmak gerekir, her
  controller tekrar eder, servise yeni bağımlılık eklenince hepsi tek tek düzelir. (2) Test sahte servis veremez; her test
  gerçek veritabanına gitmek zorunda kalır. DI'da test "gerçek yerine şunu ver" der.
- `AddScoped` = her istek için ayrı bir kopya (iki kullanıcı aynı anda iki ayrı `BranchService` alır).

### Mapper (Mapster)
- **Tek cümle:** Entity ↔ DTO arasındaki alan kopyalamasını yapan araç: aynı isimli alanları kendisi eşler.
- **Benzetme:** Kayıt memuru: "hangi satır hangi kutuya" kuralını bir kez tarif edersin, her dosya aynı kuralla aktarılır.
- **Elle yazsaydık:** `new ResultBranchDto { Id = e.Id, Name = e.Name, ... }` her yerde; Mapper ile `_mapper.Map<ResultBranchDto>(e)`.
- **Olmasa ne olurdu:** `Branch`'e `OpeningHours` eklense elle kopyalamanın her yerine eklemek gerekir, birinde unutulursa o
  uç nokta alanı sessizce boş döner. Mapper alan adından görür; `BranchMappingConfigTests` sıkı modda unutulan alanı yakalar.
- **Özel kural:** `Mapping/BranchMappingConfig.cs`: Create→Branch'te Id yok sayılır (kimliği MongoDB üretir).
- **Dürüst not:** 4 alanlı sınıfta elle yazmak da sorun değil; fayda 20 alanlı sınıflar ve çok servis olunca çıkar.
  Mapster = AutoMapper'ın MIT ve açıksız alternatifi.

## B2 Doctor

Kod: `src/Services/Doctor/Hastane.Doctor.Api/` (port 5102, MongoDB `hastane_doctor`), test 11/11. Branch kalıbının aynısı + iç içe belgeler.

### MongoDB'de iç içe belge
- **Tek cümle:** Doktorun eğitim, deneyim, ödül ve klinik listeleri ayrı tablolara değil, doktorun kendi kaydının içine yazılır.
- **Benzetme:** Doktorun dosya klasörü: özgeçmiş, diplomalar, ödüller aynı klasörde; SQL'de 5 ayrı çekmece.
- **Örnek:** `{ "firstName": "Ayşe", "educations": [{ "school": "Hacettepe Tıp", ... }], "awards": [{ "title": "Yılın Hekimi", "year": 2020 }], "clinicLocations": [...] }`; `GET /api/doctors/{id}` tek okumada hepsini getirir.
- **Olmasa ne olurdu (SQL):** doktor sayfası için 5 tabloyu birleştiren (join) sorgu; sütun eklenince sorgu ve DTO değişir; doktor eklerken 5 tabloya yazım, biri patlarsa doktor eğitimsiz kalır.
- **Bedeli:** "2006'da mezun olan tüm doktorlar" gibi çapraz sorgular SQL'de kolay, MongoDB'de zor; doktoru tek tek göstermek için MongoDB mantıklı, istatistik için SQL.
- **Kodda:** `Entities/Doctor.cs` (listeler), `Entities/Nested.cs`, `Mapping/DoctorMappingConfig.cs`. **Ders:** 11, 12.

### Liste DTO'su ile detay DTO'su
- **Tek cümle:** Aynı doktor için iki form: listede kısa özet, tek doktor açılınca tam dosya.
- **Benzetme:** Panodaki isim + branş herkese açık; telefon yalnız o doktor seçilince verilir.
- **Kodda:** `Dtos/ResultDoctorDto.cs` (Phone/Email YOK), `Dtos/GetByIdDoctorDto.cs` (var). Testler: liste JSON'unda `phone`/`email` yok, reflection ile de doğrulanır.
- **Olmasa ne olurdu:** `GET /api/doctors` tek istekle bütün doktorların telefon ve e-postasını verir; bot toplayıp spam listesi yapar; liste ayrıca eğitim / ödül listeleriyle şişer.

### Singleton ve diğer kayıt türleri (tek MongoClient)
- **Tek cümle:** MongoDB bağlantısı bir kez kurulur, bütün istekler paylaşır.
- **Benzetme:** Hastane santrali: her arayan için yeni hat çekilmez, hazır hatlar paylaşılır.
- **Bulgu (B1):** `BranchService` Scoped olduğu için her istekte `new MongoClient` kuruyordu → düzeltildi (`54d3697`).
  500 eşzamanlı istek = 500 bağlantı + 500 el sıkışma → yavaşlama, "bağlantı reddedildi".
- | Kayıt | Kaç kopya | Ne zaman |
  |---|---|---|
  | `AddScoped` | her istek için bir tane | isteğe özel işler (servisler) |
  | `AddSingleton` | uygulama boyunca tek tane | pahalı ve paylaşılabilen şeyler (MongoDB bağlantısı) |
  | `AddTransient` | her ihtiyaçta yeni | çok hafif, durumsuz şeyler |
- **Kodda:** `Program.cs:19-22`, `DoctorService` / `BranchService` constructor'ı `IMongoClient` ister.

## B2 ek (Dalga 2.5, A1): Doctor arama ve genişletme

Kod: `DoctorService.cs` (arama), `Validation/DoctorRules.cs`. Yol haritası: `KOD-HARITASI.md` 7b.

### Sayfalama ve neden `TotalCount` ayrı sorgu
- **Tek cümle:** `skip/limit` yalnız bir sayfayı getirir; "toplam kaç eşleşme var" aynı filtreyle ayrı `CountDocuments` ile sorulur.
- **Benzetme:** Kütüphanede raftan 10 kitap alırsın; "rafta toplam kaç kitap var" o 10 kitapta yazmaz, ayrıca sayarsın.
- **Olmasa ne olurdu:** Arayüz "2245 sonuç bulundu" ve sayfa numaralarını gösteremez; `Items.Count` toplam sanılsa hep 10 çıkar. Sıralamada `_id` eşitlik bozucu olmasa eşit ücretli doktorlar sayfa kaydırınca tekrar eder ya da atlanır.
- **Kodda:** `DoctorService.cs:91-93`, `_id` eşitlik bozucu sıralama bloğunda.

### Regex enjeksiyonu: kullanıcı metnini kaçırmak
- **Tek cümle:** Kullanıcının yazdığı metni arama desenine olduğu gibi koymayıp `Regex.Escape` ile düz metne çeviririz.
- **Benzetme:** "Ara" kutusuna yazılan `.*` bir jokerdir; kaçırmazsan kasadaki kişiye "kasadaki her şeyi ver" demiş olursun.
- **Olmasa ne olurdu:** `text=.*` tüm doktorları döndürür, `text=(` geçersiz desen olarak 500 verir, `(a+)+$` gibi desen sunucuyu yorar.
- **Kodda:** `DoctorService.cs` (~:125); test `Search_Text_RegexMetaCharacters_AreLiteral...` (7 enjeksiyon dizesi).

### Regex aramasının dizin kullanamaması (Elasticsearch'e giden yol)
- **Tek cümle:** "İçerir" türü regex baştan sona her belgeyi tarar; dizin yalnız tam eşleşme ve ön ek (`^abc`) için işe yarar.
- **Benzetme:** Telefon rehberi soyada göre sıralı; "ahmet" ile başlayanı hızlı bulursun ama adında "met" geçen herkes için tüm rehberi okursun.
- **Olmasa / yapmasak ne olurdu:** 100 bin doktorda her harf yazışta tüm koleksiyon taranır, arama yavaşlar. B21'de Elasticsearch ters dizin (kelime → belge listesi) ile çözecek.
- **Kodda:** yorum `DoctorService.cs` regex bölümünde ve `DoctorIndexInitializer.cs`; dizinler yalnız eşitlik süzgeçleri içindir (`BranchId`, `Gender`, `ClinicLocations.City`).

### Türetilmiş alan neden saklanmaz (MinFee / MaxFee)
- **Tek cümle:** `MinFee` konumlardaki ücretlerden her istekte hesaplanır; kaynak bilgiyi ikinci bir yerde tutmak ayrışma doğurur.
- **Benzetme:** Fişin üstüne toplamı yazıp kalemleri sonradan değiştirirsen toplam yanlış kalır; toplamı her seferinde kalemlerden hesaplamak yanlış olamaz.
- **Olmasa ne olurdu:** Doktorun İstanbul ücreti 300→400 olunca kayıtlı `MinFee` güncellenmezse arama kartı 300, detay 400 gösterir.
- **Kodda:** `Mapping/DoctorDerived.cs`; testle kilitli (ham belgede `MinFee` yok).

### Toplu getirme (batch) N+1'i önler
- **Tek cümle:** 50 doktorun adı için 50 ayrı istek yerine tek `$in` sorgusu atılır.
- **Benzetme:** Markette 50 ürünü tek tek eve gidip getirmek yerine listeyi verip tek seferde sepeti doldurmak.
- **Olmasa ne olurdu:** Reçete listesinde 50 satır varsa 50 HTTP + 50 MongoDB sorgusu; yük 50 kat, gecikme birikir. Reçetedeki "Created by Dr. X" bu uçtan gelecek.
- **Kodda:** `DoctorsController.cs:34`, `DoctorService.cs` `GetBatchAsync` (tek `Find`, 100 sınırı, geçersiz id atılır).

### İç içe belgede çakışma doğrulaması
- **Tek cümle:** Konumun haftalık programı doktor belgesinin içinde durur, MongoDB aralık çakışmasını bilmez; kontrolü biz yazarız (gün bazında sırala, her başlangıç öncekinin bitişinden önce mi), hem DTO hem serviste.
- **Benzetme:** Toplantı odası takvimi: aynı odaya 09-12 ve 11-14 yazarsan iki kişi aynı saatte kapıda buluşur; veritabanı kural koymaz, takvimi yazan kontrol eder.
- **Olmasa ne olurdu:** Aynı gün çakışan aralıklar kaydolur, B1'deki boş-saat hesabı aynı slotu iki kez üretir. İki katman: DTO atlanırsa (başka çağıran, birim test) servis yine yakalar (B6 dersi).
- **Kodda:** `Validation/DoctorRules.cs:37-66` (çakışma :59-64), `Dtos/NestedDtos.cs` (DTO tarafı), `DoctorInputValidator.cs` (servis tarafı).

### Ücret için `decimal` ve `Decimal128` (`double` değil)
- **Tek cümle:** Para ondalık tabanda tutulur; `double` ikilik tabanda 0.1'i tam gösteremez (`0.1 + 0.2 = 0.30000000000000004`).
- **Benzetme:** 1/3'ü 0.333 yazıp üçle çarpınca 0.999 bulmak.
- **Olmasa ne olurdu:** 199.99 TL kaydedilip okununca 199.99000000000001 olabilir; `minFee <= ücret` karşılaştırması 1 kuruş kayabilir; MongoDB'de alan `double` kalsaydı filtre ve sıralama da bu kaymayı taşırdı.
- **Kodda:** `Entities/Nested.cs:38-42` (`[BsonRepresentation(Decimal128)]`); ham BSON tipi testte doğrulanır.

### Aynı elemanda sağlanması gereken koşullar tek `$elemMatch` içinde
- **Tek cümle:** Dizideki bir elemanın birkaç koşulu BİRLİKTE sağlaması gerekiyorsa koşullar aynı `$elemMatch` içine yazılır.
- **Benzetme:** "Mavi gömlek VE 500 TL altı" arayan müşteri aynı gömleği ister; iki ayrı koşul yazarsan "mavi bir gömlek var, 500 altında başka bir gömlek var" diye iki farklı gömlekle de onaylanır.
- **Olmasa ne olurdu:** İzmir konumu ucuz (100), Ankara konumu pahalı (900) olan doktor `city=Ankara&maxFee=500` aramasında çıkardı; hasta Ankara'da 900 TL ödeyeceği doktoru "500 altı" diye görürdü.
- **Süreç notu:** kod incelemesinde "filtreler birbirine bağlı değil" diye bulundu; birleştirmeden önce düzeltildi.
- **Kodda:** `DoctorService.cs:158-179`; test `Search_LocationFilters_MustBeSatisfiedByTheSameLocation`.

### Derin sayfalamanın maliyeti ve üst sınır
- **Tek cümle:** MongoDB `skip(N)` N belgeyi tek tek sayıp atlar; sayfa numarası büyüdükçe sorgu yavaşlar. Bu yüzden `page ≤ 10000`, skip hesabı `long + checked`.
- **Olmasa ne olurdu:** `page=2147483647` verilince `(page-1)*pageSize` int taşar, negatif skip 500 verirdi; şimdi 400.
- **Kodda:** `DoctorService.cs:104-108,118`.

### "Bilinen sınırlar" (kodda dokunulmadı)
- Eski belgelerde `Gender` yok (Male okunur); `feeAsc`'ta ücretsiz doktorlar başa gelir; ad sıralaması ikili; `city` tam eşleşme; karışık para birimi. Ayrıntı `KOD-HARITASI.md` 7b.

## B3 Review

Kod: `src/Services/Review/Hastane.Review.Api/` (port 5103, MongoDB `hastane_review`), test 18/18. Yol haritası: `KOD-HARITASI.md` 8b.

### Sunucunun atadığı alan (CreatedAt)
- **Tek cümle:** Yorumun zamanını istemciden almayıp sunucu kendi saatiyle damgalar.
- **Benzetme:** Postanedeki tarih damgası: mektubu yazan "dün yazdım" diyemez, damgayı memur vurur.
- **Olmasa ne olurdu:** `CreateReviewDto`'da `CreatedAt` olsaydı biri `"createdAt":"2000-01-01"` gönderip yorumunu listenin en altına
  gömer, `2099` yazıp hep en üstte tutardı; sıralama (`?doctorId=`) bozulurdu. Testte bu istek gönderilir, yok sayıldığı doğrulanır.
- **Bulgu (testler yakaladı):** MongoDB tarihi milisaniye hassasiyetinde saklar; `DateTime.UtcNow` daha ince. Create cevabındaki tarih ile sonra
  okunan fark çıkıyordu → servis tarihi milisaniyeye kırpıp atıyor (`ReviewService.cs:51-53`).
- **Kodda:** `ReviewService.cs:50-53`, `Dtos/CreateReviewDto.cs` (alan yok).

### DTO'da doğrulama kuralları ve 400
- **Tek cümle:** `[Range(1,5)]`, `[MaxLength(1000)]` gibi etiketler kural bozuk isteği controller'a girmeden 400 ile geri çevirir.
- **Benzetme:** Kapıdaki görevli: biletin yoksa içeri girmeden döndürülürsün.
- **Olmasa ne olurdu:** `rating: 6` ya da `0` veritabanına girerdi; ortalama anlamsız çıkardı (4.0 yerine 7.3 gibi). 5 MB'lık bir "yorum"
  koleksiyonu şişirirdi.
- **Kodda:** `Dtos/CreateReviewDto.cs` (etiketler), `ReviewsController.cs:7` (`[ApiController]` kontrolü yapar).

### Ortalamayı veritabanında hesaplamak
- **Tek cümle:** "Kaç yorum, ortalama kaç" sorusunu MongoDB cevaplar, uygulamaya tek satır sonuç gelir.
- **Benzetme:** Sınıfın not ortalamasını öğretmene sormak ile 500 kâğıdı eve taşıyıp kendin toplamak.
- **Olmasa ne olurdu:** Bir doktorun 50.000 yorumu olsa, bellekte hesaplamak için hepsi ağdan çekilip RAM'e dolardı; özet sayfası her
  açıldığında bu olur, 20 eşzamanlı istekte API yavaşlar. Aggregation'da ağdan tek `{Count, Average}` gelir.
- **Kodda:** `ReviewService.cs:81-83`.

### Güncellemede değişmeyen alanlar
- **Tek cümle:** PUT yalnız `ReviewerName`, `Rating`, `Comment`'i `Set` eder; `DoctorId` ve `CreatedAt` için DTO'da alan bile yok.
- **Benzetme:** Kimlik kartında adresi güncellersin ama doğum tarihini değiştiremezsin, çünkü o form alanı hiç yok.
- **Olmasa ne olurdu:** Belgeyi komple değiştirseydik (`ReplaceOne`), istemci `doctorId: "baska-doktor"` yazıp yorumu rakip doktorun
  puanına taşırdı; `createdAt` eksik gelse kayıt `0001-01-01` olurdu.
- **Karar (2026-10-09):** ilk sürümde `UpdateReviewDto`'ya `DoctorId`'yi "yok sayılacak" diye koymuştu; kabul etmediğimiz alanı formda
  tutmak yanıltıcı olduğu için kaldırıldı. Testte PUT gövdesine fazladan `doctorId` ve `createdAt` konur, ikisinin de değişmediği doğrulanır.
- **Kodda:** `ReviewService.cs:62-67`, `Dtos/UpdateReviewDto.cs`.

### Yuvarlama tuzağı (banker's rounding)
- **Tek cümle:** `Math.Round(4.25, 1)` varsayılan olarak 4.2 verir (yarımlar en yakın ÇİFT sayıya yuvarlanır), okulda öğrendiğin 4.3 değil.
- **Olmasa ne olurdu:** 4 yorumlu bir doktorun puanı (5,4,4,4) ekranda 4.2 görünür. Testimiz 5,4,4 → 4.3 ile geçiyordu ama tam yarım
  durumu yakalamıyordu; kod incelemesinde fark edildi.
- **Çözüm:** `Math.Round(x, 1, MidpointRounding.AwayFromZero)` (`ReviewService.cs:90`) + yeni test `Summary_RoundsExactHalfUp`.

### Servisler arası kimlik (tekrar)
- Review'da `DoctorId` yalnız metin; Review, doktorun var olup olmadığını **kontrol etmez** (Doctor'u çağırmaz). Sonuç: var olmayan
  bir doktora yorum yazılabilir. İleride Review, Doctor'a sorabilir ya da Doctor "doktor silindi" olayını yayınlar (RabbitMQ eklemesi).

## B2.5 Payment (para ve güvenlik dersleri)

Kod: `src/Services/Payment/Hastane.Payment.Api/`, test 127. Yol haritası: `KOD-HARITASI.md` 8f. Para işi olduğu için iki tur bağımsız doğrulama yapıldı; 1. turun bulguları main'e girmeden düzeltildi.

### Idempotency: çift tıklama çift ödeme olmasın
- **Tek cümle:** İstemci her ödeme niyeti için bir anahtar (`Idempotency-Key`) yollar; sunucu aynı anahtarı ikinci kez görürse işi tekrarlamaz, ilk sonucu geri verir.
- **Benzetme:** Kafede fiş numarası: aynı numarayla ikinci kez gelirsen kasiyer yeni kahve yapmaz, "bu sipariş zaten verildi" der.
- **Olmasa ne olurdu:** Hasta "Öde"ye iki kez basar ya da ağ koparken tarayıcı isteği tekrarlar, karttan iki kez çekilir. Testle kanıtlandı: 8 paralel POST, sağlayıcı yalnız 1 kez çağrıldı.
- **Anahtar entropisi:** 16-100 karakter, `[A-Za-z0-9_-]` (UUID). Kısa anahtar ("1") iki hastanın aynı anahtarı üretip birbirinin sonucunu görmesine yol açar.
- **Kodda:** `PaymentService.cs:62-98`, `IdempotencyKeyRules.cs`, tekil indeks `PaymentConfiguration.cs:35`.

### Parayı çektikten sonra hata penceresi ve telafi
- **Tek cümle:** Sağlayıcıdan para çekilip kayıt yazılamazsa sistem sağlayıcıdan telafi iadesi alır ve satırı Failed işaretler.
- **Benzetme:** Kasadan nakit verdin, fişi yazarken defter kapandı; parayı geri alıp "işlem olmadı" diye not düşersin.
- **Olmasa ne olurdu:** Hastanın kartından 110 TL çekilir, kayıtta ödeme ve fatura yoktur; kimse paranın nerede olduğunu bilmez. **Dış sistem (sağlayıcı) ile veritabanı tek transaction'da olamaz**, bu yüzden iki işlem arasında atomiklik yoktur; yerine telafi (compensation) vardır.
- **Kodda:** `PaymentService.cs:145-161`, `:168-199`; test `PaymentSafetyTests`.

### İade: claim-işaret deseni (yarışsız)
- **Tek cümle:** İade talebi `RefundRequestedAt` işaretiyle atomik alınır, Status ancak sağlayıcı iadeyi yapınca Refunded olur; arada Status Paid kalır.
- **Benzetme:** Banka havalesinde "talep alındı" damgası vurulur; para gerçekten gidene kadar hareket "tamamlandı" yazmaz.
- **Olmasa ne olurdu:** İlk tasarım önce Refunded yapıp sağlayıcı reddederse Paid'e geri alıyordu; arada aynı randevuya yeni Paid girerse geri alma `UX_Payments_Appointment_Paid` ihlaliyle patlar, ödeme yarı bozuk kalırdı. (Bu tasarım hatası ilk kod incelemesinde bulundu.)
- **Kodda:** `PaymentService.cs:399`, `:421`, `:432`.

### `CancellationToken.None`: iptali dış yan etkiye taşımamak
- **Tek cümle:** Para hareketi başladıktan sonra istemcinin vazgeçmesi dış yan etkiye taşınmaz; iptal yalnız para hareketinden ÖNCEKİ adımlarda geçerlidir.
- **Benzetme:** Taksiye bindikten sonra telefonu kapatman yolu yarıda bırakmaz.
- **Olmasa ne olurdu:** İstemci zaman aşımına uğrayıp bağlantıyı kesince ASP.NET `ct`'yi iptal eder; sağlayıcı çağrısı ya da sonraki `SaveChanges` yarıda kesilir, para çekilmiş ama kayıt yok / Pending kalır.
- **Kodda:** `PaymentService.cs:69` (son kontrol noktası), `:105`, `:151`, `:180`, `:408`.

### Bayat Pending ve uzlaştırma ihtiyacı
- **Tek cümle:** 2 dakikadan eski Pending satırı "sonuç belirsiz" sayılır; yeniden denemede beklemeden 409 döner. Gerçek çözüm sağlayıcıyla eşleştiren arka plan işidir (yok).
- **Benzetme:** "Hâlâ ütüleniyor" askısı üç gündür asılıysa kimse beklemez, tezgâha gidip sorar.
- **Kodda:** `PaymentService.cs:239-244`, `BillingOptions.cs`.

### Kart verisi neden saklanmaz (PCI) ve sızıntı testi
- **Tek cümle:** Tam kart numarası ve CVV hiçbir yere yazılmaz (veritabanı, log, hata mesajı, cevap), yalnız marka + son 4 hane saklanır.
- **Benzetme:** Otel resepsiyonu kartı geçirir ama fotokopisini çekip çekmeceye koymaz; elinde "Visa, sonu 4242" notu kalır.
- **Olmasa ne olurdu:** Yedek ya da log sızsa binlerce hastanın kartı çalınır; PCI-DSS CVV saklamayı yasaklar.
- **Test:** Tüm metin sütunları ham SQL ile taranır; taramanın kırık olmadığını kanıtlayan **pozitif kontrol** (bilerek yazılan değer bulunur) ve **log yakalama** testi var (başarı, red, hata, iade akışlarında log metinlerinde kart yok).
- **Kodda:** `Entities/Payment.cs`, `CardDto.cs` ve `CardDetails` (maskeli `ToString`), `CardValidator.cs`, `CardDataLeakTests.cs`.

### Para için `decimal`; KDV dahil fiyattan vergi ayrıştırma
- **Tek cümle:** Para `decimal(18,2)` ile tutulur; KDV dahil tutardan önce KDV'siz kısım yuvarlanıp bulunur, KDV bunun farkıdır (Total her zaman Amount).
- **Benzetme:** Hesap 110 TL, içinde %10 KDV: önce KDV'siz kısmı bul (100), vergi "110 − 100" = 10. İkisini ayrı yuvarlarsan toplam 1 kuruş şaşabilir.
- **Kodda:** `InvoiceCalculator.cs:9-13`, `PaymentConfiguration.cs:19` (`HasPrecision(18,2)`); testte 20.000 tutar × 5 KDV oranında Subtotal + Tax = Amount.

### Sahte sağlayıcı arayüz arkasında
- **Tek cümle:** Servis `IPaymentProvider` arayüzünü bilir, hangi sağlayıcı olduğunu bilmez.
- **Benzetme:** Prize takılan alet: şebeke de olsa jeneratör de olsa lamba aynı yanar.
- **Kodda:** `Providers/IPaymentProvider.cs`, `Program.cs:32` (değişen tek satır); testte aynı kapıdan çağrı sayan / hata enjekte eden sürüm takılır.

### Başarısız denemeyi de kaydetmek (denetim izi)
- Reddedilen ödeme `Failed` satırı olarak yazılır, fatura kesilmez. "Param çekildi ama ödeme yok" şikâyetinde kayıt bulunur; kart deneme saldırısı görünür olur. HTTP **402 Payment Required**: "istek geçerli, ödeme alınamadı" (400 = form bozuk, 502 = sağlayıcı / kayıt hatası).

### SQL SEQUENCE ile eşzamanlı güvenli fatura numarası
- **Tek cümle:** Numarayı veritabanı sayacı (`NEXT VALUE FOR`) verir; "en büyüğü bul, bir ekle" iki kişinin aynı numarayı almasına yol açar.
- **Kodda:** `PaymentDbContext.cs:22`, `PaymentService.cs:205-220` (düz `DbCommand`: EF `SqlQuery` SEQUENCE'i alt sorguya sardığı için SQL Server reddeder).

### Bir randevu için tek Paid ödeme (iki katmanlı savunma, B4 dersi tekrarı)
- Servis kontrolü güzel hata verir; filtreli tekil indeks `WHERE Status = 'Paid'` yarışta son duvardır. İade tamamlanınca Status Refunded olur ve randevu yeniden ödenebilir. Fatura yasal belge: iadede silinmez (`DeleteBehavior.Restrict`).

### Bilinen sınırlar
- Tutar istemciden gelir ve doğrulanmaz; kimlik doğrulama yok (Dalga 3); sağlayıcı zaman aşımı yok; uzlaştırma işi yok. Ayrıntı `KOD-HARITASI.md` 8f.

## B3 ek (Dalga 2.5, A2): Review toplu özet

Kod: `ReviewsController.cs:39-47`, `ReviewService.cs:84-118`. Yol haritası: `KOD-HARITASI.md` 8b-2.

### Toplu uç ve N+1
- **Tek cümle:** "10 doktorun puanı" tek istekte ve tek veritabanı sorgusunda gelir.
- **Benzetme:** 10 ürün için markete 10 kez gitmek yerine listeyi verip tek seferde torbayı doldurmak.
- **Olmasa ne olurdu:** Arama sayfası 10 doktor kartı çizerken `summary` ucuna 10 ayrı istek atar; her istek ağ gidiş-dönüşü, Review ayrıca 10 sorgu çalıştırır. 50 doktor gösterince 50 istek (N+1).
- **Kodda:** `ReviewsController.cs:39`, `ReviewService.cs:89`.

### `$cond` ile koşullu sayım
- **Tek cümle:** `$sum` içine "bu satır şartı sağlıyorsa 1, değilse 0" koyunca yalnız şartı sağlayanlar sayılır.
- **Benzetme:** Oy sayımında yalnız "evet" kâğıtlarına tık atmak.
- **Olmasa ne olurdu:** Tavsiye oranı için tüm yorumları servise çekip döngüyle sayardık; binlerce yorum ağdan gelir. Burada MongoDB tek sayı döndürür.
- **Kodda:** `ReviewService.cs:99-108` (`g.Sum(x => x.Rating >= 4 ? 1 : 0)`).

### "İstenen her kimlik için kayıt dön" sözleşmesi
- **Tek cümle:** Yorumsuz doktor da listede sıfırlarla gelir ve istenen sıra korunur.
- **Benzetme:** Sınıf yoklamasında gelmeyen öğrencinin adı da "yok" diye listede yer alır.
- **Olmasa ne olurdu:** Arayüz her doktor için "listede var mı?" diye aramak zorunda kalır, yorumsuz doktor kartında "undefined" çıkar.
- **Kodda:** `ReviewService.cs:94`.

### Yüzde yuvarlama tuzağı (B3'ün tekrarı)
- 8 yorumdan 5'i ≥ 4 ise 62.5: .NET'in varsayılanı (banker's) 62 verir, okul kuralı 63. `MidpointRounding.AwayFromZero` ile 63 (`ReviewService.cs:115-116`, kanıt testi `doc-rec-e`).

### XSS (kısa)
- `ReviewerName` ve `Comment` API'den ham döner; `<script>` gibi bir yorumun sayfada çalışmaması için HTML kaçırma ekran katmanının işidir (Razor varsayılan kaçırır; `Html.Raw` kullanılmamalı).

## B4 Appointment (SQL Server + EF Core)

Kod: `src/Services/Appointment/Hastane.Appointment.Api/` (port 5104, SQL Server `hastane_appointment`), test 69/69. Yol haritası: `KOD-HARITASI.md` 8c.
**İlk SQL servisi:** MongoDB servislerinden farkı veri katmanı (aşağıdaki ilk dört kavram bütün SQL servislerinde geçerli).

### DbContext (neden Scoped?)
- **Tek cümle:** `DbContext`, bir isteğin veritabanıyla konuşma oturumu: hangi satırları okuduğunu izler, `SaveChanges`'te değişenleri tek seferde yazar.
- **Benzetme:** Restoranda her masaya ayrı adisyon açılır, adisyon masalar arasında paylaşılmaz. `MongoClient` mutfağın kendisi gibi: tek örnek, herkese ortak.
- **Olmasa ne olurdu:** Singleton olsaydı iki eşzamanlı istek aynı izleyiciyi paylaşır, biri diğerinin yarım değişikliğini kaydeder ya da
  "second operation started" hatası alırdı. (MongoClient için tam tersi: tek olmalı. Kural: **pahalı ve iş parçacığı güvenli = Singleton, durum tutan = Scoped.**)
- **Kodda:** `Program.cs:20`, `AppointmentService.cs:16`.

### Migration (veritabanı için sürüm kontrolü)
- **Tek cümle:** Şema değişikliklerini kod olarak saklar ve veritabanına sırayla uygular.
- **Benzetme:** git commit'leri gibi; her biri "tabloyu şöyle değiştir" adımı.
- **Olmasa ne olurdu:** Başka makinede tabloları elle kurman gerekir; bir yerde kolon adı farklı olunca "Invalid column name" çıkar.
- **Kodda:** `Migrations/20261009181953_InitialCreate.cs`, `Program.cs:35` (`Migrate()`). Komut: `dotnet dotnet-ef migrations add <Ad> --project <Api csproj>`.
- **Development'ta otomatik `Migrate()`:** artısı `dotnet run` yeterli. Eksisi: üretimde açılışta şema değiştirmek riskli (iki kopya aynı anda açılır,
  yetkisiz hesapla şema değişir) → yalnız Development'ta.

### Guid v7 kimlik
- **Tek cümle:** Zaman damgası içeren kimlik; üretildikçe büyür.
- **Benzetme:** Seri numaralı biletler (sıralı dizilir) ile rastgele numaralı biletler (araya karışır).
- **Olmasa ne olurdu:** Rastgele Guid (v4) kümelenmiş indekste her kaydı ortaya sokar, sayfa bölünmesi yapar. Sıralı `int` olsaydı randevu sayısı ve
  komşu kayıtlar tahmin edilirdi (`/appointments/1`, `/2`...).
- **Kodda:** `AppointmentService.cs:56` (`Guid.CreateVersion7()`), `AppointmentConfiguration.cs` (`ValueGeneratedNever`).

### Enum'u string saklamak
- **Tek cümle:** Veritabanında 0,1,2 yerine "Pending" yazılır.
- **Benzetme:** Klasör etiketine "3" yerine "Tamamlandı" yazmak.
- **Olmasa ne olurdu:** Biri enum'a araya yeni değer koyunca eski kayıtların anlamı sessizce kayar (eski `2` başka bir durum olur).
- **Kodda:** `AppointmentConfiguration.cs` (`HasConversion<string>`), test `Status_IsStoredAsStringInDatabase`.

### Bire-bir ilişki, yabancı anahtar, cascade
- **Tek cümle:** Detay satırı randevunun Id'sine bağlıdır; veritabanı yetim detayı reddeder, randevu silinince detayı da siler.
- **Benzetme:** Hasta dosyasının içindeki tek sayfalık muayene formu: dosya imha edilince form da gider; sahibi olmayan form kabul edilmez.
- **Olmasa ne olurdu:** Randevu silinince detay yetim kalır (şikâyetler ortada, sahibi yok). Tekil indeks olmasa aynı randevuya iki detay yazılır,
  `GET /detail` hangisini döneceğini bilemez.
- **Kodda:** `AppointmentConfiguration.cs` (`HasOne...WithOne...OnDelete(Cascade)`), `AppointmentDetailConfiguration.cs` (`HasIndex(...).IsUnique()`), `AppointmentService.cs:101-104`.

### Durum makinesi (geçersiz geçişe 409)
- **Tek cümle:** Hangi durumdan hangisine geçilebileceği tek bir tabloda; dışındaki istek reddedilir.
- **Benzetme:** Tren bileti: "iptal edilmiş" bilet "binildi"ye dönmez.
- **Kural:** Pending→Confirmed|Cancelled, Confirmed→Completed|Cancelled; Completed ve Cancelled son durum (yeniden planlanamaz).
- **Olmasa ne olurdu:** `PATCH status=Pending` ile tamamlanmış muayene yeniden bekleyene döner; raporlar ve faturalar tutarsızlaşır.
- **Kodda:** `Services/AppointmentStatusTransitions.cs:7-20`, `AppointmentService.cs:86`. Test: 16 çiftin hepsi Theory ile.
- **409 nedir:** "isteğin biçimi doğru ama şu anki durumla çelişiyor" (400 biçim hatası, 404 yok, 409 çelişki).

### Sunucunun atadığı alanlar (tekrar: Status, CreatedAt, Id)
- DTO'da alan yoksa istemci gönderemez. `CreateAppointmentDto`'da `Status` olmadığı için `status: "Completed"` ile randevu açıp akışı atlamak yok sayılır.
- **Kodda:** `Dtos/CreateAppointmentDto.cs`, `AppointmentService.cs:56-64`.

### Filtreli sorgu SQL'e nasıl dönüşür (IQueryable)
- **Tek cümle:** `IQueryable` bir tarif defteri: her `Where` tarife bir koşul satırı ekler, SQL `ToListAsync`'te tek seferde gider.
- **Benzetme:** Sipariş formu: yalnız işaretlediğin kutular mutfağa gider.
- **Olmasa ne olurdu:** Tüm randevuları çekip C#'ta süzmek gerekir; yüz bin satırda ağ ve bellek israfı.
- **Kodda:** `AppointmentService.cs:27-44`.

### Tarih ve saat dilimi
- Veritabanında hep UTC (`datetime2`). Offsetsiz gelen tarih ("2026-12-01T10:00:00") UTC varsayılır; istemci yerel saatle offsetsiz yollarsa saat kayar:
  bu bir API sözleşmesi konusu (ileride ekran yerel saati UTC'ye çevirip yollayacak). Okurken `Kind=Utc` işaretlenir, JSON'da `Z` ile çıkar.
- **Kodda:** `Data/UtcDateTimeConverter.cs`, `Validation/FutureDateAttribute.cs`.

### Bilinçli bırakılanlar
- **Doktor çakışma engeli** ilk sürümde yoktu; senin onayınla sonradan eklendi (aşağıdaki "B4 ek" bölümü).
- **`rowversion` yok:** iki eşzamanlı durum değişikliğinde son yazan kazanır (ileride "iyimser eşzamanlılık" olarak eklenebilir).

## B4 ek: Randevu çakışma engeli

Kod: `AppointmentService.cs:57-125`, `AppointmentConfiguration.cs:33-35`. Yol haritası: `KOD-HARITASI.md` 8c-2.

### "Kontrol et, sonra yaz" yarışı (check-then-act race)
- **Tek cümle:** Önce "boş mu?" diye bakıp sonra yazmak yetmez; bakma ile yazma arasında başka bir istek araya girebilir.
- **Benzetme:** İki kişi aynı anda tek kalan sinema koltuğuna bakıyor, ikisi de "boş" görüyor, ikisi de bilet alıyor.
- **Olmasa ne olurdu:** Aynı doktor ve saat için aynı milisaniyede gelen iki POST da `AnyAsync` ile "boş" görür, ikisi de 201 alır, doktora iki hasta aynı saate yazılır.
- **Kodda:** kontrol `AppointmentService.cs:82`, yazma `:88`; aradaki boşluk. Test: `Create_ConcurrentRequestsForSameSlot_ExactlyOneWins` (8 paralel POST → 1×201, 7×409, veritabanında 1 satır).

### Filtreli tekil indeks: veritabanı son savunma hattı
- **Tek cümle:** Veritabanına "bu iki kolonun birleşimi tekil olsun, ama yalnız `Cancelled` olmayan satırlarda" kuralını koyar.
- **Benzetme:** Kapıdaki görevli: iptal edilmiş biletler sayılmaz, diğerlerinden aynı koltuğa ikincisi giremez.
- **Olmasa ne olurdu:** Servis kodundaki `if` atlanırsa (yarış, başka bir servis, elle `INSERT`) tabloya mükerrer aktif randevu girer.
- **Kodda:** `AppointmentConfiguration.cs:33-35`, migration `20261009183549_AddDoctorSlotUniqueIndex.cs:12-16`.

### İki katmanlı savunma
- **Tek cümle:** Kod güzel hata mesajını verir, veritabanı bütünlüğü garanti eder.
- **Benzetme:** Otelde resepsiyonist "bu oda dolu" der, ama kapı kilidi yine de ikinci kişiyi içeri almaz.
- **Olmasa ne olurdu:** Yalnız kod olursa yarışta mükerrer kayıt girer. Yalnız indeks olursa kullanıcı 500 ya da anlamsız SQL hatası görür.
- **Kodda:** katman 1 `AppointmentService.cs:82-83`, katman 2 `:90-93` (`IsUniqueViolation`, `:60`). Aynı fikir Prescription'da kalem sayısı için de kullanıldı (B6).

### Dakika normalizasyonu
- **Tek cümle:** Randevu saati dakika birimiyle düşünüldüğü için saniye ve altı atılır.
- **Benzetme:** Doktorun ajandasında "10:00" satırı var, saniyesi yok; 10:00:30'da gelen de 10:00 satırına yazılır.
- **Olmasa ne olurdu:** İstemci 10:00:00 ve 10:00:30 göndererek aynı doktora aynı 10:00 slotunu iki kez aldırır; veritabanı bunları farklı değer sayar, tekil indeks işe yaramaz.
- **Kodda:** `UtcDateTimeConverter.cs:22-26`, çağrıları `AppointmentService.cs:79` ve `:108`.

### Mevcut veride yeni tekil indeks
- Tabloda zaten mükerrer kayıt varsa `CREATE UNIQUE INDEX` patlar (migration uygulanamaz). Üretimde önce veri temizlenir; geliştirme veritabanı boştu.

## B5 Identity (ASP.NET Core Identity)

Kod: `src/Services/Identity/Hastane.Identity.Api/` (port 5001, SQL Server `hastane_identity`), test 29/29. Yol haritası: `KOD-HARITASI.md` 8d.
SQL ortak kavramları (DbContext Scoped, migration, Guid, ilişki) B4'te anlatıldı; burada servise özel olanlar.

### Şifre asla düz saklanmaz (hash + salt)
- **Tek cümle:** Identity şifreyi PBKDF2 ile, her kullanıcı için rastgele tuzla (salt), geri döndürülemez bir özete çevirip `PasswordHash` sütununa yazar.
  Girişte girilen şifre aynı yöntemle özetlenip karşılaştırılır.
- **Benzetme:** Meyveyi suya çevirmek: suyu içersin ama meyveyi geri çıkaramazsın. Salt, her meyveye ayrı baharat katmak: aynı şifreyi kullanan iki kişinin suyu farklı çıkar.
- **Olmasa ne olurdu:** Veritabanı yedeği sızarsa ya da bir geliştirici `SELECT *` çekerse herkesin şifresi okunur; insanlar şifreyi başka sitelerde de
  kullandığı için zarar bu servisle sınırlı kalmaz. Hash'liyse saldırgan her şifreyi tek tek denemek zorunda.
- **Kodda:** `AuthService.cs:43` (`CreateAsync` hash'ler), `AuthService.cs:84` (doğrulama).

### Mass assignment ve rol tohumlama
- **Tek cümle:** İstemcinin gönderebileceği alanları DTO belirler; `RegisterDto`'da rol alanı yok, rolü sunucu verir (`Patient`).
- **Benzetme:** Kayıt formunda "yetki: yönetici" kutusu yok; forma el yazısıyla yazsan da işleme alınmaz.
- **Olmasa ne olurdu:** `AppUser`'ı doğrudan alsaydık `{"role":"Admin"}` gönderen herkes kendini yönetici yapardı. Testte bu saldırı (rol, roller, id) denenir; kullanıcı
  yalnız Patient olur. Roller referans veridir: kod "Patient" rolünü bekler; tohumlanmasa `AddToRoleAsync` hata verir, kimse kayıt olamazdı. Tohumlama idempotent (tekrar çalışınca zarar vermez).
- **Kodda:** `Dtos/RegisterDto.cs:5-9`, `Services/AuthService.cs:62-63`, `Data/RoleSeeder.cs:15-24`.

### Aynı giriş hatası (kullanıcı numaralandırma) ve hesap kilidi (kaba kuvvet)
- **Tek cümle:** "E-posta yok" ile "şifre yanlış" birebir aynı 401 gövdesini döner; 5 yanlış denemede hesap 5 dakika kilitlenir.
- **Benzetme:** Danışma "bu isimde hasta yok" yerine hep "bilgileriniz uyuşmadı" der; yoksa dışarıdan biri hangi isimlerin yatan hasta olduğunu öğrenir.
  Kilit: kodlu panelin 5 yanlış denemeden sonra bir süre kapanması.
- **Olmasa ne olurdu:** Ayrı mesajla saldırgan binlerce e-postayı deneyip hangilerinin sistemde olduğunu çıkarır, sonra yalnız onlara şifre dener. Kilit olmasa
  saniyede yüzlerce şifre denenir, zayıf şifre dakikalar içinde bulunur.
- **Kodda:** `AuthController.cs:44-48` (aynı gövde), `AuthService.cs:84` (`lockoutOnFailure: true`), `Program.cs:32-34` (5 deneme, 5 dk).
- **Dürüst not:** 423 yalnız var olan hesaplarda çıktığı için numaralandırma tam kapanmıyor; rate limit ile birlikte düşünülecek (JWT dalgası).

### `IdentityUser`'ı genişletmek (`AppUser`)
- Hazır şifre, kilit, rol altyapısını bozmadan kendi alanlarımızı (ad, soyad, kayıt tarihi) eklemenin yolu. `Entities/AppUser.cs:7`.

### `AddIdentityCore` seçimi (kurstan olası sapma)
- Kurs büyük olasılıkla `AddIdentity` kullanıyor (cookie, giriş sayfası gibi MVC parçalarını da getirir). Biz API olduğumuz için `AddIdentityCore`
  (yalnız kullanıcı ve rol yönetimi) + `AddSignInManager` aldık; şema kaydı yok. Bu doğrulanmadı (videoyu görmedik).

### Çoktan çoğa ilişki (`AspNetUserRoles`)
- Bir kullanıcının birden çok rolü, bir rolün birden çok kullanıcısı olabilir; arada ara tablo durur (`UserId`, `RoleId`). Roller JOIN ile okunur.

### Servisler arası çoklu kullanıcı sorgusu (`users?ids=`)
- Patient ve Appointment ileride kullanıcı adlarını bu uçtan alacak. Tek tek sormak 100 kullanıcıda 100 istek demek; `?ids=a,b,c` ile tek istek, tek SQL (`WHERE Id IN (...)`).
  Guid olmayanlar atılır, en çok 100 id (aksi 400): sonsuz uzun sorgu ile servisi yormayı engeller.

## B6 Prescription (1—N ilişki, transaction)

Kod: `src/Services/Prescription/Hastane.Prescription.Api/` (port 5105, SQL Server `hastane_prescription`), test 32/32. Yol haritası: `KOD-HARITASI.md` 8e.
SQL ortak kavramları (DbContext Scoped, migration, Guid v7) B4'te anlatıldı; burada servise özel olanlar.

### Bire-çok (1—N) ilişki: reçete → kalemler
- **Tek cümle:** Tek bir kaydın yanında ona bağlı çok sayıda satırı yabancı anahtarla tutmak.
- **Benzetme:** Bir reçete kâğıdı ve üzerine alt alta yazılmış ilaç satırları; satırlar o kâğıda aittir, kâğıt atılırsa satırlar da gider.
- **Olmasa ne olurdu:** Kalemleri reçetenin içine tek metin olarak koysak "parasetamol içeren kalemleri bul" gibi sorgular yazılamaz, kalem başına doz sınırı veritabanında zorlanamaz.
  Cascade olmasa reçete silinince kalemler sahipsiz kalırdı.
- **MongoDB ile fark:** Doktor'da eğitim/deneyim listeleri doktorun belgesinin İÇİNDE durmuştu (tek kayıt). Burada ayrı tablo + yabancı anahtar: kalemler tek başına sorgulanabilir, kısıtlanabilir.
- **Kodda:** `Data/PrescriptionConfiguration.cs:30-34` (`HasMany/WithOne/Cascade`), `Data/PrescriptionItemConfiguration.cs:23`.

### Transaction: "ya hep ya hiç"
- **Tek cümle:** Birden çok satırı tek bir bütün olarak yazmak; biri bozulursa hiçbiri yazılmaz. EF'te tek `SaveChanges` bunu otomatik yapar.
- **Benzetme:** Market kasasında 5 ürünü tek fişte okutmak; kart reddedilirse hiçbiri satılmış sayılmaz.
- **Olmasa ne olurdu:** Önce reçeteyi, sonra kalemleri ayrı ayrı kaydetsek, 2. kalemde hata olunca kalemsiz ya da yarım kalemli reçete kalırdı; hasta eksik ilaç listesi görürdü.
  Testle gösterildi: 2. kalemin adı 201 karakter olunca reçete satırı da yazılmıyor.
- **Kodda:** `PrescriptionService.cs:64-65` (`Add` + tek `SaveChanges`), `:80-88` (güncellemede sil + ekle tek `SaveChanges`); test `Create_WhenDatabaseRejectsAnyRow_NothingIsPersisted_AllOrNothing`.

### N+1 sorunu, `Include` ve `Count`'u veritabanında hesaplamak
- **Tek cümle:** 20 reçeteyi listeleyip her biri için ayrı "kalemleri ver" sorgusu atmak 1+20 sorgu eder (N+1); `Include` kalemleri tek JOIN'li sorguda getirir; liste ucunda kalem lazım değil, yalnız sayı lazım, o yüzden SQL `COUNT` hesaplar.
- **Benzetme:** Kitabın bölümlerini sormak için her bölüm için kütüphaneciye ayrı gidip gelmek yerine tek seferde "kitabı bölümleriyle ver" demek.
- **Olmasa ne olurdu:** 1000 reçetelik listede 1001 sorgu ya da 1000 reçetenin tüm kalemlerini çekip C#'ta saymak; ağ ve bellek israfı.
- **Kodda:** `PrescriptionService.cs:32-37` (liste, `ProjectToType`), `Mapping/PrescriptionMappingConfig.cs:13-14` (`s.Items.Count`), `PrescriptionService.cs:45-46` (`Include`).

### Liste sırasını tesadüfe bırakmamak (`LineNo`)
- **Tek cümle:** Sıranın önemli olduğu bir listede sırayı açık bir kolona yaz ve okurken o kolona göre sırala.
- **Benzetme:** Reçetedeki ilaçlar madde numarasıyla (1., 2., 3.) yazılır; "alt alta yazıldığı için sıralı olur" denmez.
- **Olmasa ne olurdu:** İlk sürümde kalemler `Id`'ye göre sıralanıyordu; Guid v7 aynı milisaniyede kesin artan değil (duman testinde görüldü). Doktor "önce antibiyotik, sonra ağrı kesici" yazsa bile
  hasta ekranında sıra bazen ters çıkardı. `ORDER BY` yoksa SQL Server sırayı zaten garanti etmez. `LineNo` + tekil indeks hem sırayı sabitler hem aynı numaranın iki kez verilmesini engeller.
- **Süreç notu:** kod incelemesinde bulgu olarak çıktı, Prescription henüz main'e girmediği için ikinci migration yerine `InitialCreate` yeniden üretildi.
- **Kodda:** `PrescriptionService.cs:105-112` (`Number`), `:46` (`OrderBy(LineNo)`), `Data/PrescriptionItemConfiguration.cs` (tekil indeks).

### Servis katmanında da doğrulama (iki katmanlı savunma, tekrar)
- **Tek cümle:** Kuralı hem kapıda (DTO / controller) hem içeride (servis) kontrol et.
- **Benzetme:** Binanın hem giriş kapısında hem kasa odasının kapısında görevli olması.
- **Olmasa ne olurdu:** Bugün tek giriş HTTP, DTO doğrulaması yeter. Yarın başka bir servis, arka plan işi ya da test doğrudan servisi çağırsa 0 ya da 21 kalemli reçete kayda geçerdi.
- **Kodda:** `PrescriptionService.cs:55` ve `:72`, `Controllers/PrescriptionsController.cs` (`Validation` → `BadRequest`). Test: `Service_RejectsInvalidItemCount_EvenWhenDtoValidationIsBypassed`.

### Güncellemede "kalemleri tamamen değiştir"
- **Artısı:** basitlik; istemci tam listeyi gönderir, tek tek diff yok, sonuç her zaman gönderilen liste.
- **Eksisi:** değişmeyen kalemler de silinip yeniden eklenir, kalem Id'leri her güncellemede değişir (ileride eczane teslim kaydı kalem Id'sine bağlanırsa kırılır), iki eşzamanlı güncellemede sonuncusu kazanır.
- **Benzetme:** Tabloyu kısmen düzeltmek yerine sayfayı yırtıp yenisini yazmak.
- **Kodda:** `PrescriptionService.cs:80-84`; test `Update_ReplacesItemsCompletely_OldItemIdsGone`.

### Servisler arası kimlik (tekrar)
- `AppointmentId`, `DoctorId`, `PatientId` yalnız saklanan değerler. Reçeteyi yazan doktor gerçekten o randevunun doktoru mu, ilaç gerçekten var mı: kontrol edilmez (Prescription başka servise sormaz). İleride ya
  servisler arası HTTP sorgusu ya da Pharmacy ile ilaç kimliği bağı.

### "Branch" ne demek?
- **Branş:** hastanenin tıp bölümleri (Kardiyoloji, Dahiliye, Ortopedi…). Branch servisi = hastane girişindeki bölüm tabelasının
  dijital hali. Kayıt: `{ "name": "Kardiyoloji", "description": "Kalp ve damar hastalıkları", "icon": "fa-heart-pulse" }`.
- **Neden ilk servis:** doktor bir branşa bağlı, randevuda önce branş seçilir, çağrı ekranı branşa göre sıra gösterir.
- **Karıştırma:** Git'teki `feat/branch` "dal" demek (kodun ayrı kopyası); isim benzerliği tesadüf.

## B8-B9 Yetkilendirme (Dalga 3B, altı servise koruma)

Kod haritası: `KOD-HARITASI.md` 8h.

### Rol kontrolü ve sahiplik kontrolü aynı şey değil
- **Tek cümle:** Rol "hangi kapıdan girebilirsin", sahiplik "o odadaki hangi dosyaya dokunabilirsin".
- **Benzetme:** Hastane girişinde "hasta" yaka kartı seni hastaya ait katlara alır; ama başkasının dosyasını çekmecesinden alamazsın.
- **Olmasa ne olurdu:** `GET /api/prescriptions/{id}` yalnız `Patient` rolü ister ve sahiplik bakmazsa, Ayşe Hanım Id'yi tahmin edip Ali Bey'in reçetesini okur (IDOR).
- **Kodda:** rol controller etiketinde, sahiplik serviste: `PrescriptionService.cs:29-34`, `AppointmentAccess.cs:11-22`.

### Başkasının kaydı 403 değil 404
- Başkasının kaydına 403 dersek "bu Id var ama senin değil" demiş oluruz; Id'leri deneyen biri hangilerinin var olduğunu öğrenir. 404 hiçbir şey sızdırmaz. Rolü hiç uygun olmayan 403 alır.

### Sınıf düzeyi [Authorize] tuzağı
- Sınıfa `[Authorize(Roles="Admin")]`, action'a `[Authorize(Roles="Patient")]` koyarsan ikisi **VE**'lenir: kimse giremez (ya da beklenmedik biri girer). Bu yüzden her action kendi etiketini taşır ve `AuthorizationCoverage` testi etiketsiz ucu yakalar. Branch'te sınıf etiketi + GET'lerde `[AllowAnonymous]` bilinçli kullanıldı.

### Sunucu kimliği belirler
- Yorumu kimin yazdığı (`ReviewerId`), randevunun hastası: istekten değil token'dan atanır. Aksi halde istemci başkası adına yorum yazardı (mass assignment).

## B10 Gateway (Ocelot) (Dalga 4A)

Kod haritası: `KOD-HARITASI.md` 8i.

### Gateway nedir, neden tek kapı
- **Tek cümle:** İstemcinin tek adresi (5000) olup yolun önekine bakarak isteği doğru servise ileten kapı.
- **Benzetme:** Hastane danışması: ziyaretçi servis odalarının yerini bilmez, danışmaya söyler, danışma yönlendirir.
- **Olmasa ne olurdu:** Arayüz 5001, 5101, 5102... adreslerini koda gömerdi; bir servis taşınınca ya da portu değişince arayüz bozulurdu. Şimdi yalnız `ocelot.json` değişir.
- **Kodda:** `Program.cs` (`AddOcelot`), `ocelot.json` rota satırları.

### Authorization iletilir ama doğrulanmaz
- Gateway başlığı değiştirmeden geçirir, doğrulamayı her servis kendi JWT ayarıyla yapar. Servis Gateway'e güvenmediği için biri servisin portuna doğrudan gelse de korumasız kalmaz. Merkezileştirme ileride bilinçli bir karar (README Dalga B #12).

## B7, B11-B14 WebUI (MVC arayüz) (Dalga 5)

Kod haritası: `KOD-HARITASI.md` 8j.

### Çerez + JWT neden birlikte
- **Tek cümle:** Servisler JWT ister, tarayıcı çerezle konuşur; WebUI ikisini birleştirir.
- **Benzetme:** Otelde kolundaki bant (çerez) resepsiyonu geçirir; resepsiyon arka ofise kendi kimlik kartıyla (JWT) gider.
- **Kodda:** girişte token doğrulanıp şifreli çereze konur (`AccountController.cs`), API'ye giderken `BearerTokenHandler.cs` çerezden alıp başlığa ekler.

### Token neden tarayıcıya verilmez
- Sayfaya bir XSS kaçarsa JavaScript `localStorage`'daki token'ı okuyup başka yerde kullanır. HttpOnly çerezi JavaScript okuyamaz. Test: yanıt HTML'inde token geçmiyor.

### Antiforgery ve "silme neden GET değil"
- **Tek cümle:** Çerez her isteğe tarayıcı tarafından kendiliğinden eklenir; başka bir site senin adına istek gönderebilir (CSRF).
- **Olmasa ne olurdu:** İade GET olsaydı kötü bir sayfaya `<img src="https://hastane/Admin/Payments/Refund/123">` yazmak yeterdi: admin sayfayı açınca iade yapılırdı. POST + antiforgery belirteci (başka sitenin bilemeyeceği değer) bunu keser; onay sayfası da yanlış tıklamayı.
- **Kodda:** global `AutoValidateAntiforgeryToken`; test `Every_post_requires_antiforgery_token`.

### Arayüzde gizlemek yetki değildir
- "Onayla" düğmesini yalnız bekleyen randevuda göstermek kullanıcı deneyimi; biri POST'u elle gönderebilir. Gerçek kapı servisteki `AppointmentAccess.CanSetStatus`. Doktor profilinde id formdan değil token'dan okunur (`Profile_update_always_uses_doctor_id_from_token_not_from_request`).

### Idempotency anahtarı arayüzde
- Ödeme formu açılırken bir anahtar üretilir, gizli alanda taşınır; çift tıklamada aynı anahtar gider, servis kartı ikinci kez çekmez. Tutar formdan değil doktor kaydından okunur (ama asıl doğrulama servise ait, bkz. açık maddeler).

## Ödeme tutarı sunucuda (2026-10-10)

### İstemciden gelen fiyata güvenilmez
- **Tek cümle:** İstemci "ne yapmak istediğini" söyler; "ne kadar" ve "kimin" bilgisini sunucu kendi kaynağından alır.
- **Benzetme:** Kasiyer etiketi müşterinin söylediğinden değil, barkoddan okur.
- **Olmasa ne olurdu:** Eskiden tarayıcıda gövdedeki `amount: 450` → `0.01` yapmak yetiyordu; üstelik başkasının randevusuna da fatura kesilebiliyordu.
- **Kodda:** `PaymentService.cs` `VerifyAsync`: Appointment'a (sahiplik, durum) ve Doctor'a (ücret) sorar; istemci tutarı yalnız "ekranda gördüğüm fiyat bu" diye karşılaştırılır.

### Kural tek yerde olmalı
- Ücret hesabı iki yerde yazılınca (arayüz Doctor'ın `MinFee`'si, Payment kendi hesabı) karışık para birimli doktorda ikisi farklı tutar buldu ve hasta hiç ödeyemedi. Çözüm: hesap yalnız Doctor'da (`DoctorDerived.cs`), diğerleri sonucu okur.

### Servisten servise token iletmek
- **Artısı:** sahiplik kuralı tek yerde (Appointment) kalır, Payment kopyalamaz.
- **Eksisi:** Payment, Appointment ve Doctor'a bağımlı: biri kapalıysa ödeme alınamaz (503, kart çekilmez). Kullanıcının token'ı iç servisler arasında dolaşır; yalnız güvenilen iç adreslere gider (adres ayardan, istekten değil).

## Gece dalgası (2026-10-10)

### Boş saat neden Appointment'ta hesaplanıyor
- **Tek cümle:** Boş saat = doktorun programı − dolu randevular; dolu randevuları bilen Appointment, programı Doctor'dan HTTP ile sorar.
- **Benzetme:** Otelde oda planını yönetim, rezervasyon defterini resepsiyon tutar; "hangi oda boş?" sorusunu resepsiyon planı yönetime sorarak cevaplar, kopyasını tutmaz.
- **Olmasa ne olurdu:** Hasta doktorun çalışmadığı 09:15'e ya da Pazar gününe randevu alabiliyordu. Artık POST/PUT program dışı saati 400 ile reddediyor.
- **Kodda:** `SlotCalculator.cs`, `AppointmentService.cs` (`GetAvailabilityAsync`, `CheckScheduleAsync`).

### Saat dilimi
- Program "09:00" yerel saat (İstanbul), veritabanı UTC (06:00Z). Tek bir saat dilimi ayarı ve ekranda tek bir dönüşüm yardımcısı (`AppointmentTime`) olmazsa iki ekran farklı saat gösterir.

### Ayrı "public" DTO ve `isMine`
- Gizli alanı tek DTO'dan çıkarmak yerine gösterilecek alanları ayrı sınıfta tek tek yazmak: yarın eklenen hassas bir alan kendiliğinden anonim yanıta sızmaz.
- Ekran "bu yorum benim mi?" bilgisine ihtiyaç duyar, başkasının kullanıcı kimliğine değil: sunucu karşılaştırır, istemciye yalnız true/false gider.

### Testlerde konteyner bellek dersi
- Her test sınıfı kendi SQL Server konteynerini açıyordu; sınıf sayısı 9'a çıkınca hepsi aynı anda açılıp Docker belleğini (7,4 GB) doldurdu ve testler "kod hatası" gibi görünen yüzlerce hata verdi. Çözüm: o projede sınıfları sırayla koşmak. Ders: toplu hata görünce önce altyapıya bak.

## Dalga X (2026-10-10): eksiklerin tamamlanması

### Servisler arası iç anahtar
- **Tek cümle:** Bazı uçlar kullanıcıya değil yalnız diğer servislere açıktır; onları ortak bir sır (`X-Internal-Key`) korur.
- **Olmasa ne olurdu:** Dışarıdan biri `/api/internal/notifications/email` ile herkese sahte e-posta attırabilirdi. Ayar boşsa uç "herkese açık" değil "herkese kapalı" olmalı.
- **Kodda:** `Notification/.../Security/InternalApiKeyAttribute.cs`.

### Şifremi unuttum ve hesap numaralandırma
- Kullanıcı var da yok da aynı yanıt (202) ve yaklaşık aynı süre: yoksa da sahte token üretilir, e-posta isteğin dışında (kuyrukta) gönderilir. Hız sınırı var olmayan e-postayı da sayar, yoksa sınırın kendisi "bu e-posta kayıtlı" diye ele verirdi.

### Dosya yüklemede güvenin kaynağı içerik
- Türü uzantıdan ya da Content-Type'tan değil dosyanın ilk baytlarından (magic bytes) belirle, adı sunucu üretsin, SVG kabul etme (içinde script olabilir). Canlı denendi: PNG kılığındaki SVG 400.

### En iyi çaba bildirim
- Randevu asıl iş, bildirim ek iş: bildirim servisi çökse de randevu alınır; çağrı kayıttan sonra, kısa zaman aşımıyla, hatası yutularak.

### Sorgulama mı, anlık bağlantı mı
- SignalR tarayıcının token'ı bilmesini ister; bizim tasarımda token şifreli çerezde, tarayıcı göremez. Bu yüzden mesajlar 5 saniyede bir "yeni var mı" diye sorulur; (konuşma, gönderim zamanı) indeksi bunu ucuz yapar.

## B16 Queue + SignalR (Dalga Y)

### SignalR: hub, grup, yayın
- **Tek cümle:** Sunucunun, tarayıcı sormadan ona "yeni bir şey oldu" diye haber verebildiği kalıcı bağlantı (çoğunlukla WebSocket).
- **Benzetme:** Sorgulama, her 5 dakikada bir eczaneyi arayıp "ilacım geldi mi?" demek. SignalR, eczacının "gelince seni ararım" demesi.
  **Hub** santral, **grup** santraldeki dahili hat: "Dr. Ayşe'nin bekleme salonu" grubuna katılan ekranlar yalnız onun çağrılarını duyar.
- **Olmasa ne olurdu:** Çağrı ekranı her 2 saniyede bir sunucuya sorardı; 30 ekran × günde 10 saat = 540.000 gereksiz istek, yine de
  "Numara 14" ekrana 2 saniye geç düşerdi.
- **Kodda:** `Queue/.../Hubs/QueueHub.cs` (hub: `JoinDoctor`, `JoinAll`; yayın `QueueNotifier`: gruplar `doctor:{id}` ve `all`, metot `QueueUpdated`),
  `Program.cs:60-66` (`AddSignalR`), `:88` (`MapHub`), tarayıcı `WebUI/wwwroot/js/queue-display.js`. Gateway'de WebSocket rotası `/gateway/hubs/queue`.
- **Tuzak (yaşandı):** hub JSON'u enum'u varsayılan olarak **sayı** yazar (`status: 1`), REST ise metin (`"Called"`). JS iki kaynağı
  karşılaştırınca uyuşmuyordu; `AddJsonProtocol(... JsonStringEnumConverter)` ile hub da metin yazıyor.
- **Ders:** 110–121.

### Neden anonim ve yalnız dinleme
- **Tek cümle:** Bekleme salonundaki TV oturum açamaz; bu yüzden hub token istemez, ama karşılığında hiçbir şey **kabul etmez ve gizli bir şey yaymaz**.
- **Benzetme:** Salondaki hoparlör. Herkes duyar, ama mikrofon yalnız hemşirenin elinde; hoparlörden "Ahmet Yılmaz, kan grubu A" değil "14 numara, Ahmet Y." duyulur.
- **Olmasa ne olurdu:** Hub metot üzerinden yayın yapılabilseydi biri tarayıcı konsolundan sahte "14 numara çağrıldı" basabilirdi. Yayında hasta
  kimliği olsaydı anonim herkes bir hastanın hangi doktora geldiğini öğrenirdi (sağlık verisi). Ayrıca tarayıcıya token vermemek (bkz. Dalga X) için
  oturum gerektirmeyen bir yol gerekiyordu.
- **Kodda:** `QueueHub.cs` (yalnız grup katılımı, 64 karakter sınırı), herkese açık biçim tek yerde `QueuePublic` (`QueueBroadcastDto`: doctorId, number,
  displayName, status, calledAt), ad maskesi `Services/DisplayNameMasker.cs` ("Can Y."; tam ad hiç saklanmaz), `Program.cs:62` gelen ileti en çok 4 KB.
  Yayını yalnız sunucu yapar, o da yetkili REST ucundan sonra.

### Günlük sıra numarası ve "MAX+1" yarışı
- Numara = o doktorun bugünkü en büyük numarası + 1. İki hasta aynı anda girerse ikisi de 7 okur, ikisi de 7 almaya çalışır. Son savunma
  `(DoctorId, QueueDate, Number)` tekil indeksi: kaybeden kısa bir rastgele beklemeyle yeniden dener (`QueueService.cs:61-96`). B4'teki
  "kontrol et, sonra yaz" yarışının tekrarı; burada çözüm kilit değil, "çakışırsa yeniden dene".
- "Bugün" UTC'ye göre değil **İstanbul gününe** göre: gece 01:00'de (UTC 22:00) giren hasta dünkü sıraya yazılmasın.

## B17 Pharmacy: CQRS + MediatR (Dalga Y)

### CQRS: komut ve sorgu ayrımı
- **Tek cümle:** Sistemi **değiştiren** istekler (komut: "ilaç ekle", "reçeteyi karşıla") ile yalnız **soran** istekler (sorgu: "ilaçları listele")
  ayrı sınıflarda yazılır.
- **Benzetme:** Bankada para yatırma gişesi ile bakiye sorma makinesi ayrıdır. Makine bozulsa da yatırma yapılır; makineye "para çek" düğmesi konmaz.
- **Olmasa ne olurdu:** Tek `MedicineService` sınıfında 12 metot birikir; "liste" metoduna yanlışlıkla bir `SaveChanges` girer ve okumada veri
  değişir. Ayrımda sorgu handler'ı hiç `SaveChanges` çağırmaz, kural dosya adından okunur.
- **Kodda:** `Pharmacy/.../Features/Medicines/Commands/` ve `Queries/`; her işlem bir klasör: `XCommand` (ne istendiği, `record`) + `XHandler` (nasıl
  yapıldığı) + `XValidator`. Controller yalnız `ISender.Send` çağırır (`Controllers/PharmacyApiController.cs`).
- **Dürüst not:** Burada okuma ve yazma aynı veritabanını kullanıyor; CQRS'in "ayrı okuma veritabanı" biçimi değil, yalnız **kod düzeyinde** ayrım.
  Küçük bir serviste bile fayda: her işlem kendi küçük dosyasında. Bedeli: daha çok dosya.

### MediatR ve pipeline behavior
- **Tek cümle:** MediatR "bu komutu kim işler?" sorusunu çözen aracıdır; pipeline behavior her isteğin handler'a varmadan önce geçtiği ortak halkalardır.
- **Benzetme:** Hastane girişi: önce danışma (kaydı tut, süreyi yaz: **Logging**), sonra triyaj (eksik bilgiyle doktora gönderme: **Validation**),
  en son doktor (**Handler**). Her hasta aynı kapılardan geçer; doktorlar triyajı tekrar yapmaz.
- **Olmasa ne olurdu:** Her handler'ın başına aynı "doğrula, logla" kodu kopyalanır; biri unutulunca geçersiz istek veritabanına kadar iner.
- **Kodda:** `Program.cs:30-35` (sıra = kayıt sırası, **ilk kayıt en dışta**: Logging → Validation → Handler), `Behaviors/ValidationBehavior.cs`
  (hata varsa istisna, handler hiç çalışmaz), `Behaviors/ValidationExceptionHandler.cs` (istisna → 400 + alan hataları), `Behaviors/LoggingBehavior.cs`
  (istek adı + süre; gövde loglanmaz, sağlık verisi olabilir). Doğrulayıcı arayüzü kendi yazımımız (`Validation/IValidator.cs`), FluentValidation yok.
- **Lisans notu:** MediatR **12.5.0** son Apache-2.0 sürüm; 13 ve sonrası ticari lisans anahtarı ister. Sürüm bu yüzden sabit (`Directory.Packages.props`).

### Reçete karşılama: hepsi ya hiç
- Reçetede 3 ilaç var, biri stokta yok: 2'sini düşüp hastayı yarım ilaçla göndermek yerine **hiçbiri** düşülmez, 409 ile hangi kalemin neden
  karşılanamadığı listelenir. Önizleme (sorgu) ve karşılama (komut) **aynı planı** kullanır (`DispensePlanner.cs`): ekranda "karşılanabilir"
  gören yönetici, basınca aynı kuralı çalıştırır. Reçetedeki serbest metin ilaç adı katalogla Türkçe katlamayla eşleşir (`Common/TurkishText.cs`:
  `ToLower` yetmez, "I" Türkçede "ı" olur).
- **Kodda:** `DispensePrescriptionHandler.cs` (transaction, ilaçları Id sırasıyla kilitleme, `UX_Dispensations_PrescriptionId` ile çift karşılama engeli).

## B18–B20 Market, Redis, Order (Dalga Y)

### Redis cache-aside (ürün önbelleği)
- **Tek cümle:** Önce önbelleğe bak, yoksa veritabanından oku ve önbelleğe koy; veri değişince önbellekteki kopyayı sil.
- **Benzetme:** Eczacının tezgâhındaki en çok sorulan 20 ilacın fiyat listesi. Fiyat değişince eski listeyi çöpe atar; bir sonraki soruda depodaki
  defterden yenisini yazar. Liste kaybolsa da defter (SQL Server) yerinde, sadece biraz yavaş.
- **Olmasa ne olurdu:** Mağaza vitrini her ziyaretçide SQL Server'a gider; 10.000 kişinin baktığı kampanya gününde aynı 10 ürün 10.000 kez okunur,
  aynı veritabanındaki stok düşümü (sipariş) yavaşlar.
- **Kodda:** `Market/.../Caching/ProductCache.cs` (`product:{id}`, liste anahtarı sorgunun SHA-256 özeti, TTL 5 dk), `Services/ProductService.cs`
  (oku: `:62-75`, yaz sonrası `InvalidateAsync`). **Kural:** önbellek yalnız hızlandırıcıdır; Redis kapalıysa sessizce atlanır, cevap SQL'den gelir
  (`Caching/RedisGate.cs`, `AbortOnConnectFail=false`: Redis kapalıyken de servis açılır).
- **Neden silme, güncelleme değil:** stok düşünce önbellekteki JSON'u düzeltmeye çalışmak iki kaynağı senkron tutma derdi açar; silmek basit ve
  bir sonraki okuma doğruyu yazar. Liste anahtarları bir Redis kümesinde tutulur ki `KEYS *` taramadan hepsi silinebilsin.

### Redis sepeti: HASH + 7 gün TTL
- **Tek cümle:** Sepet `cart:{userId}` adlı bir HASH: alan = ürün, değer = adet; her yazmada ömür 7 güne yeniden kurulur.
- **Benzetme:** Kafede masaya bırakılan not kâğıdı: 7 gün kimse dokunmazsa garson atar; dokunan olursa süre baştan başlar.
- **Olmasa ne olurdu:** Sepet SQL'de olsaydı terk edilmiş binlerce sepet için gece temizlik işi yazmak gerekirdi; JSON blok olarak saklansaydı iki
  sekmeden aynı anda ürün eklemek "oku, değiştir, yaz" yarışında birini kaybederdi. HASH'te `HSET` tek alanı atomik yazar.
- **Kodda:** `Caching/RedisCartStore.cs` (`HSET` + `EXPIRE` aynı MULTI/EXEC işleminde), `Settings/MarketOptions.cs` (`CartDays = 7`). Sepette
  **fiyat tutulmaz**: okurken ürün kaydından gelir. Sepet anahtarı token'daki `sub`'dan türer, başkasının sepetine yol yok. Redis kapalıysa sepet 503.
- **Ders:** 133–138, 146–153.

### Atomik stok UPDATE ve idempotency (rezervasyon)
- **Tek cümle:** "Stok yeterliyse düş" kontrolü ve düşüm **tek bir SQL cümlesi**: `UPDATE Products SET Stock = Stock - 2 WHERE Id = X AND Stock >= 2`.
- **Benzetme:** Son biletin satışı. Gişe memuru önce "var mı?" diye bakıp sonra satarsa, iki gişe aynı son bileti iki kişiye satar. Tek hamlede
  "varsa sat" denirse ikinci gişe "kalmadı" cevabını alır.
- **Olmasa ne olurdu:** Stok 1; iki sipariş aynı anda "1 ≥ 1, tamam" der, ikisi de düşer, stok -1 olur, olmayan ürün satılır (kayıp güncelleme).
  Koşullu UPDATE'te veritabanı satırı kilitler, ikinci istek güncel stokla (0) yeniden değerlendirilir ve 0 satır etkiler.
- **Idempotency:** Order aynı `orderId` ile ikinci kez rezerve isterse (ağ cevabı kaybetti, yeniden denedi) stok **ikinci kez düşmez**:
  `StockReservations.OrderId` tekil indeksi "bu sipariş daha önce geldi mi"yi veritabanına sordurur, ikinci çağrı ilk sonucu alır (`replayed: true`).
  Aynı mantık checkout'ta `Idempotency-Key` (çift tıklama tek sipariş) ve iadede (`Reserved → Released` geçişini yapan tek çağrı iade eder).
- **Kodda:** `Market/.../Services/StockService.cs` (`ReserveAsync`, `ReleaseAsync`), ürün Id sırasıyla güncelleme (deadlock önlemi), veritabanında
  `CHECK (Stock >= 0)`. Eczanede aynı desen: `Pharmacy/.../StockLedger.cs`. Payment'taki idempotency notunun (B2.5) tekrarı.

### Fiyat sunucudan yeniden okunur
- Checkout sepetteki fiyatı hiç kullanmaz; her ürün için Market'e "bu ürün şu an satışta mı, fiyatı ne?" diye sorar (`OrderService.cs:75-95`).
  Sepete 100 TL'den eklenip fiyatı 150 TL'ye çıkan ürün 150 TL'den satılır; istemcinin gönderdiği fiyata hiç güvenilmez (Payment tutarı notunun tekrarı).

### MariaDB ve EF sağlayıcı seçimi
- **Tek cümle:** MariaDB, MySQL'in açık kaynak kardeşi; EF Core'un onunla konuşması için bir **sağlayıcı** paketi gerekir, ve EF Core 10 için kararlı tek seçenek Oracle'ın paketiydi.
- **Benzetme:** Aynı prize farklı ülkenin fişini takmak için adaptör gerekir. Bilinen iyi adaptör (Pomelo) yeni prize (EF Core 10) henüz yalnız
  "deneme" sürümüyle uyuyor; resmi adaptör (Oracle) uyuyor ama kutusundan iki sürpriz çıktı.
- **Olmasa ne olurdu (yanlış seçim):** Önizleme paketiyle yazılan servis bir sonraki sürümde kırılabilir; projeyi EF Core 9'a indirmek diğer 14 servisi de etkilerdi.
- **Sürpriz 1, migration kilidi:** EF Core 10 migration uygularken iki örnek aynı anda uygulamasın diye `GET_LOCK` alır. Oracle sağlayıcısı sonucu
  sayıya çeviriyor, MariaDB 11.4 NULL döndürünce `InvalidCastException` ile servis açılmıyordu. Sağlayıcının sınıfı `internal` (miras alınamaz),
  bu yüzden **dekoratör** yazıldı: her işi içteki nesneye devreder, yalnız kilidi NULL-güvenli yapar (`Order/.../Data/MariaDbHistoryRepository.cs`,
  takıldığı yer `Program.cs:31`).
- **Sürpriz 2, karakter kümesi:** MariaDB varsayılanı latin1 olunca "Ateş" kayboluyordu; compose sunucuyu utf8mb4 açar, bağlantı cümlesi utf8mb4 zorlar
  (`Program.cs:83-90`), `EnsureUtf8mb4` migration'ı eski tabloları çevirir.
- **Lisans:** `MySql.EntityFrameworkCore` GPLv2 + Universal FOSS Exception (açık kaynak projede serbest). Dürüst not: MariaDB'nin burada SQL Server'a
  karşı bir üstünlüğü yok; kurs farklı veritabanlarını göstermek için kullanıyor.
- **Ders:** 139–145.

### Sipariş numarası sayacı
- `MAX+1` yerine yıl başına tek satırlık sayaç: `UPDATE OrderCounters SET LastValue = LastValue + 1` satırı kilitler, ikinci işlem birincinin COMMIT'ini
  bekler ve artmış değeri görür (`Services/OrderNumberGenerator.cs`). Payment'taki SQL `SEQUENCE`'in MariaDB karşılığı; sonuç `SIP-2026-000123`.

## B21 Elasticsearch (Dalga Y)

### Ters indeks
- **Tek cümle:** Elasticsearch her belgeyi sözcüklerine ayırır ve "bu sözcük hangi belgelerde geçiyor" listesini tutar; arama bu listeye bakar, belgeleri tek tek okumaz.
- **Benzetme:** Kitabın sonundaki dizin. "Kardiyoloji" kelimesini bulmak için 500 sayfayı okumazsın, dizinde "Kardiyoloji: s. 12, 87, 301" yazar.
- **Olmasa ne olurdu:** MongoDB'deki regex araması (`/yilm/i`) dizin kullanamaz, her doktor belgesini tarar (B2 ek notu: "Regex aramasının dizin
  kullanamaması"). 12 doktorda fark yok; 100.000 doktorda her arama saniyeler sürer.
- **Kodda:** `Doctor/.../Search/ElasticDoctorSearchIndex.cs` (dizin tanımı `:114-161`, belge `ToDocument`).

### Analyzer: Türkçe katlama
- **Tek cümle:** Analyzer, metni dizine yazmadan (ve aramadan) önce aynı biçime getiren zincirdir: küçük harf, aksan kaldırma, kök bulma.
- **Örnek:** `tr_fold` = Türkçe küçük harf (I → ı, İ → i) + asciifolding: "YILMAZ", "Yılmaz", "yilmaz" aynı terime iner; "cocuk" yazan "Çocuk"u bulur.
  `tr_text` ek olarak Türkçe etkisiz sözcükleri atar ve kök bulur (ekleri kırpar), yalnız biyografide; ad/soyadda kök bulma yok ki bulanık eşleşme öngörülebilir kalsın.
- **Olmasa ne olurdu:** Klavyesinde Türkçe karakter olmayan kullanıcı "Gunes" yazınca "Güneş" adlı doktoru bulamaz.

### Fuzziness ve yazdıkça öneri
- `fuzziness: AUTO`: sözcük uzunluğuna göre 1-2 harf hatayı tolere eder ("kardiyolji" → "Kardiyoloji"); `prefix_length: 1` ilk harfi sabit tutar
  (yoksa "Ali" ile "Veli" birbirine karışır). `bool_prefix` son sözcüğü önek sayar ("yilm" → "Yılmaz"); `suggest` ucu bununla yazdıkça öneri verir.
- **Kodda:** `ElasticDoctorSearchIndex.cs:386-412` (sorgu), `:421` (öneri).

### Yedek yol (Mongo'ya düşüş) ve "kaynak gerçek" kuralı
- **Tek cümle:** Elasticsearch asıl veri değil, Mongo'nun aranabilir kopyası; kapalıysa arama eski Mongo yoluyla çalışır, kullanıcı fark etmez.
- **Benzetme:** Kütüphanenin dijital kataloğu çöktü; görevli raflara bakarak yine kitabı bulur, sadece daha yavaş.
- **Olmasa ne olurdu:** Elasticsearch konteyneri düşünce doktor arama sayfası 500 verirdi, randevu almak isteyen kimse doktor bulamazdı.
- **Kodda:** `DoctorService.cs:105-124` (dizin yalnız kimlik + sıra verir, belgeler Mongo'dan), hata sonrası devre kesici (`ElasticDoctorSearchIndex.cs`
  `Trip`), yanıt başlığı `X-Search-Engine` hangi yolun cevap verdiğini söyler. Yazmalar önce Mongo'ya, sonra dizine "en iyi çaba"; tutarsızlık olursa
  Admin **Arama Dizini** ekranından yeniden indeksle (dizin silinmeden, üstüne yazarak).
- **Ders:** 154–155 (kursta teori).

## Dalga B: olaylar (RabbitMQ, Kafka) (Dalga Z)

### Transactional outbox
- **Tek cümle:** Olayı RabbitMQ'ya doğrudan göndermek yerine iş verisiyle **aynı veritabanı işleminde** bir "giden kutusu" tablosuna yazarsın; arka plandaki
  bir görevli onu sonra RabbitMQ'ya taşır.
- **Benzetme:** Hastane sekreteri sevk mektubunu postaneye koşarak götürmez; hastanın dosyasına işlerken aynı anda "giden evrak" sepetine koyar.
  Postacı gelince sepeti boşaltır. Postane kapalıysa mektup sepette bekler, kaybolmaz.
- **Olmasa ne olurdu:** "Önce siparişi kaydet, sonra RabbitMQ'ya yayınla": arada süreç çökerse sipariş iptal kaydedilmiş ama stok iadesi olayı hiç
  gönderilmemiş olur, ürün sonsuza dek "rezerve" kalır. Ters sırada (önce yayınla) olay gider ama kayıt yazılamaz, Market olmayan bir iptali iade eder.
- **Kodda:** `Order/.../Messaging/OrderMessaging.cs` (`AddEntityFrameworkOutbox` + `UseBusOutbox`), `OrderService.cs:266-286` (`MarkCancelledAsync`:
  durum + `Publish` + **tek** `SaveChanges`), `Appointment/.../Messaging/AppointmentMessaging.cs` (SQL Server). Outbox tabloları migration'la eklendi
  (`*_AddMassTransitOutbox`). Eski senkron yolda gereken "iade bekliyor" işareti (`StockReleasePending`) bu yüzden artık hep false.

### En az bir kez teslim ve idempotent tüketici
- **Tek cümle:** RabbitMQ (ve Kafka) bir olayı **en az bir kez** teslim eder; ağ onayı kaybolursa aynı olay ikinci kez gelebilir, tüketici bunu zararsız kılmalıdır.
- **Benzetme:** Kargo "teslim edildi" imzası kaybolursa kargocu paketi bir daha getirir; kapıdaki kişi "bu paket zaten geldi" diye takip numarasından anlar.
- **Olmasa ne olurdu:** `OrderCancelled` iki kez gelir, Market stoğu iki kez iade eder: 2 adet satılmış ürün 4 adet geri döner. Hasta aynı bildirimi iki kez görür.
- **Kodda (üç farklı yöntem):** Notification `SourceKey = "{EventId}:{alıcı}"` filtreli tekil indeks (ikinci ekleme reddedilir); Market iadeyi
  `Reserved → Released` koşullu UPDATE'le sahiplenir (ikinci çağrı 0 satır); Queue `WHERE Status = 'Waiting'` koşullu UPDATE. Kafka tarafında Analytics
  `processed_events` koleksiyonuna `_id = eventId` kuponu. Hata olursa yeniden deneme 1, 5, 15 sn, sonra `<kuyruk>_error` (`Queue/.../QueueMessaging.cs`).
- **Ders:** kursta yok (kurs tamamen senkron HTTP).

### "Her şey olay olmamalı": rezervasyon neden senkron
- **Tek cümle:** Kullanıcının **cevabını beklediği** iş senkron HTTP kalır; kullanıcının beklemediği yan işler olay olur.
- **Örnek:** Checkout'ta "stok var mı?" sorusunun cevabı ekrana o an yazılmalı ("Stok yetersiz, miktarı azaltın"). Rezervasyon olay olsaydı sipariş
  "alındı" denip 3 saniye sonra "aslında stok yokmuş" bildirimi gelirdi. Buna karşılık iptalde stok iadesi, "Siparişiniz alındı" bildirimi, sıra kaydını
  kapatma kullanıcının beklemediği işler: olayla yapılır, Market o an kapalı olsa da iş kaybolmaz.
- **Kodda:** senkron `OrderService.cs:127` (`ReserveAsync`, `X-Internal-Key`), olay `:149` ve `:275`. Market cevap vermezse bile güvenli yön seçildi:
  sipariş Cancelled + `OrderCancelled` olayı (stok belki düştü, belki düşmedi; iade idempotent olduğu için ikisi de doğru sonuç verir).

### Kafka ve RabbitMQ farkı
- **Tek cümle:** RabbitMQ bir **kuyruk**tur (mesaj işlenince silinir), Kafka bir **kayıt defteri**dir (log; mesaj okunsa da saklama süresince durur,
  her okuyucu kendi konumunu tutar).
- **Benzetme:** RabbitMQ postane: mektup alıcıya verilince postanede kalmaz. Kafka gazete arşivi: herkes istediği sayfadan okumaya başlar, okunan
  gazete rafta kalır; dün başlayan yeni bir okuyucu geçen ayın sayılarını da okuyabilir.
- **Ne zaman hangisi:** "Şunu yap" (bildirim gönder, stoğu iade et) → RabbitMQ: iş bir kez yapılsın, hata kuyruğu, yeniden deneme. "Şu oldu" akışı,
  sonradan farklı amaçlarla yeniden okunacak veri (arama ve görüntüleme istatistiği) → Kafka.
- **Kavramlar:** **topic** (konu, `hastane.doctor-activity`), **partition** (konunun paralel parçaları; anahtar = doctorId, aynı doktorun olayları aynı
  parçada sıralı kalır), **offset** (okuyucunun defterdeki sayfa numarası), **consumer group** (`analytics`: aynı gruptaki örnekler parçaları paylaşır;
  ikinci bir grup aynı olayları baştan, bağımsız okuyabilir).
- **Replay (yeniden oynatma):** Admin "yeniden hesapla" der → toplamlar silinir, offset en eski mesaja sarılır (`Seek`), olaylar baştan okunup toplamlar
  sıfırdan kurulur (`Analytics/.../Kafka/ActivityConsumerService.cs` `RewindAsync`). RabbitMQ'da bu mümkün değil: işlenen mesaj gitti.
- **Elle commit:** offset **işledikten sonra** commit edilir (`EnableAutoCommit = false`). Önce commit sonra işleme olsaydı çökmede olay kaybolurdu;
  bu sırayla en kötü durumda olay tekrar gelir ve eventId kuponu yakalar.
- **Üretici tarafı:** istek Kafka'yı beklemez; olay sınırlı bir bellek kanalına bırakılır, arka plan servisi yazar (`Doctor/.../Activity/`). Kanal dolarsa
  yeni olay düşer: bu bir istatistik, kayıt defteri değil; birkaç olay kaybı doktor aramasını yavaşlatmaktan ucuzdur.
- **Kodda:** üretici `DoctorActivityPublisher.cs`, `DoctorActivityProducerService.cs`; tüketici `ActivityConsumerService.cs`, `ActivityProcessing.cs`.

## Docker ile yayın (Dalga Z)

### Multi-stage Dockerfile
- **Tek cümle:** İmaj iki aşamada kurulur: büyük SDK imajında derlenir, sonuç yalnız küçük çalışma zamanı imajına kopyalanır.
- **Benzetme:** Mobilya atölyede (testere, zımpara, tutkal) yapılır, müşterinin evine yalnız bitmiş dolap gider; atölye eve taşınmaz.
- **Olmasa ne olurdu:** Her servis imajı ~1 GB SDK ve kaynak kodu taşırdı; 17 imaj diski doldurur, imajın içinde derleyici bulunması saldırgana da araç verir.
- **Kodda:** kökteki `Dockerfile` (tek, parametrik: `PROJECT_PATH`, `PROJECT_NAME`). Önce yalnız `.csproj` + `*.props` kopyalanıp `restore`: kod değişince
  paket katmanı önbellekten gelir, NuGet tekrar inmez.

### Root olmayan kullanıcı
- Konteyner içindeki süreç `root` değil `app` kullanıcısıyla çalışır (`USER app`, port 8080: root olmayan 1024 altı porta bağlanamaz).
  Uygulamada bir açık bulunsa bile saldırgan konteyner içinde sistem dosyalarını değiştiremez. Kalıcı klasörler (`/data/photos`, `/data/keys`) bu
  yüzden önceden `app`'e verilir.

### Compose: tek komutla tüm sistem
- **Tek cümle:** `docker-compose.full.yml`, 17 uygulamanın ve altyapının birbirine nasıl bağlanacağını tek dosyada tarif eder.
- **Dikkat edilecekler (dosyada yorumlu):** konteyner içinde `localhost` konteynerin **kendisidir**, servisler birbirine adla gider (`http://identity:8080`),
  bu yüzden Gateway için ayrı rota dosyası var (`ocelot.Docker.json`, `scripts/sync-ocelot-docker.ps1` üretir). Servis portları dışarı açılmaz; Docker
  yayımlanan portu güvenlik duvarını (ufw) atlayarak açtığı için varsayılan bağlanma adresi 127.0.0.1. Sırlar `.env` ve `secrets/` dosyalarından gelir,
  imaja girmez. Development dışında migration'ı `Database__MigrateOnStartup=true` açar.
- **Kodda:** `docker-compose.full.yml`, `scripts/docker-up.ps1`, `deploy/Caddyfile` (otomatik HTTPS, yalnız WebUI + SignalR yolu dışarıda), `docs/YAYINLAMA.md`.

