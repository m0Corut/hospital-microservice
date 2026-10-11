---
title: JWT ve Yetkilendirme Tasarımı (Dalga 3)
created: 2026-10-09
modified: 2026-10-09
type: design
status: active
tags: [hastane-mikroservis, jwt, guvenlik, yetki, tasarim]
---
# JWT ve Yetkilendirme Tasarımı (Dalga 3)

Kurs bölümleri: B8 (JWT, ders 42-48) ve B9 (rol bazlı ve scope bazlı yetki, ders 49-51). Bu belge tüm servislerin uyacağı **sözleşme**dir.

## 1. Kavramlar (Muhammet için)

- **Kimlik doğrulama (authentication):** "Sen kimsin?" Giriş yapınca Identity servisi bir **token** (kimlik kartı) verir.
- **Yetkilendirme (authorization):** "Bunu yapmaya iznin var mı?" Diğer servisler kartı kontrol eder: kart gerçek mi, süresi dolmuş mu, rolü ne.
- **JWT:** İçinde kullanıcının bilgileri (kimlik, rol) yazılı, **imzalı** bir metin. İmza sahte kartı engeller.

## 2. Kararlar

| Konu | Karar | Neden |
|---|---|---|
| İmza algoritması | **RS256** (asimetrik: özel anahtar + açık anahtar) | Kurs büyük olasılıkla HS256 (ortak parola) kullanıyor. HS256'da 12 servisin hepsi imza parolasını bilir; biri ele geçirilirse sahte token üretilebilir. RS256'da yalnız Identity özel anahtarı tutar; diğer servisler yalnız açık anahtarla **doğrular**, üretemez. (Kurs sapması, README günlüğüne yazılır.) |
| Anahtar dağıtımı | Özel anahtar yalnız Identity'de (`user-secrets`: `Jwt:PrivateKeyPem`). Açık anahtar `src/Shared/Hastane.Shared/jwt-public.pem` dosyası **depoda**; her servis `Hastane.Shared` üzerinden okur | Açık anahtar sır değil. Çalışma anında Identity'ye bağımlılık yok (Identity çökse bile diğer servisler token doğrular). Anahtar değişimi (rotation) elle; üretimde JWKS ucu düşünülür (not) |
| Anahtar üretimi | Geliştirmede bir kez `scripts/new-jwt-keys.ps1` ile 2048 bit RSA çifti üretilir: özel → user-secrets (Identity), açık → `jwt-public.pem` | Betik özel anahtarı ekrana/dosyaya yazmaz, doğrudan user-secrets'a koyar |
| Token türü | **Yalnız access token**, ömrü **30 dakika** (`Jwt:AccessTokenMinutes`) | Refresh token bu dalgada YOK (ekstra özellik; ileride) |
| Yayıncı / hedef | `iss = "hastane-identity"`, `aud = "hastane-api"` (yapılandırılabilir `Jwt:Issuer`, `Jwt:Audience`) | Doğrulamada ikisi de zorunlu |
| Saat toleransı | `ClockSkew = 30 saniye` (varsayılan 5 dk DEĞİL) | Süresi dolan token gereksiz uzun yaşamasın |
| Başlık | `Authorization: Bearer <token>` | Standart |

## 3. Token içeriği (claims)

| Claim | Değer | Not |
|---|---|---|
| `sub` | kullanıcı kimliği (Identity `AspNetUsers.Id`) | Hasta için `PatientId` budur |
| `email` | e-posta | |
| `name` | "Ad Soyad" | |
| `role` | `Admin` / `Doctor` / `Patient` (birden çok olabilir; her biri ayrı `role` claim'i) | |
| `doctor_id` | MongoDB doktor kimliği (yalnız Doctor rolünde ve bağlanmışsa) | Admin, kullanıcıyı doktor kaydına bağlar (§6) |
| `jti` | rastgele token kimliği | |
| `iat`, `nbf`, `exp`, `iss`, `aud` | standart | |

Rol claim adı `role`, ad claim'i `name`: doğrulama ayarında `RoleClaimType = "role"`, `NameClaimType = "name"` (eski .NET'in uzun URI claim adlarına eşlenmesi KAPALI: `MapInboundClaims = false`).

## 4. Ortak kütüphane: `src/Shared/Hastane.Shared`

Bütün servisler `Hastane.Shared`'a `ProjectReference` verebilir (tek izinli ortak bağ). İçine:

- `Security/HastaneRoles.cs`: `Admin`, `Doctor`, `Patient` sabitleri.
- `Security/HastaneClaims.cs`: `Subject = "sub"`, `DoctorId = "doctor_id"` vb. sabitler.
- `Security/HastanePolicies.cs`: ilke adları + kayıt: `AdminOnly`, `DoctorOnly`, `PatientOnly`, `DoctorOrAdmin`, `AuthenticatedUser` (`RequireAuthenticatedUser`).
- `Security/JwtOptions.cs` (`Issuer`, `Audience`, `AccessTokenMinutes`, `PrivateKeyPem` yalnız Identity'de dolu).
- `Security/JwtAuthenticationExtensions.cs`: `AddHastaneJwtAuthentication(this IServiceCollection, IConfiguration)`: JwtBearer'ı açık anahtar, issuer, audience, `ValidateLifetime`, `ClockSkew=30s`, `RequireSignedTokens`, `ValidAlgorithms = RS256` ile kurar; `AddHastaneAuthorization()` ilkeleri kaydeder.
- `Security/CurrentUser.cs` (`ICurrentUser`: `UserId`, `Email`, `Roles`, `DoctorId`, `IsInRole`, `IsAdmin`): `HttpContext.User`'dan okur, `AddHastaneCurrentUser()` ile kayıt.
- `Security/ProblemResponses.cs`: 401 ve 403 için `ProblemDetails` JSON gövdesi (başlık, durum, `traceId`); 401'de `WWW-Authenticate: Bearer`.
- `jwt-public.pem` gömülü kaynak ya da çıktıya kopyalanan dosya; `Program.cs` yalnız `builder.Services.AddHastaneJwtAuthentication(builder.Configuration)` + `app.UseAuthentication(); app.UseAuthorization();` çağırır.

Test desteği: `tests/Hastane.TestSupport` (yeni sınıf kütüphanesi, yalnız testler kullanır): test için RSA çifti üretir, `TestJwt.Create(userId, roles, doctorId?)` ile imzalı token üretir, `JwtTestAuthExtensions.WithBearer(HttpClient, token)`, ve `WebApplicationFactory` için `UseTestJwt()` (servisin doğrulama anahtarını test açık anahtarıyla DEĞİŞTİRİR; üretim anahtarı testte kullanılmaz).

## 5. Yetki matrisi (hedef; Dalga 3B'de her servise uygulanır)

`A` = anonim, `Auth` = giriş yapmış herkes, rol adları, `Sahip` = kaynağın sahibi (token `sub` / `doctor_id`).

| Servis | Uç | Kural |
|---|---|---|
| **Branch** | GET (liste, tek) | A |
| | POST, PUT, DELETE | Admin |
| **Doctor** | GET liste, `search`, `batch` | A (telefon / e-posta zaten yok) |
| | GET `{id}` (telefon + e-posta var) | Auth |
| | POST, DELETE | Admin |
| | PUT `{id}` | Admin **ya da** Doctor ve `doctor_id == {id}` (Sahip) |
| **Review** | GET (liste, tek, özetler) | A |
| | POST | Patient (ReviewerId = `sub`, istemci veremez) |
| | PUT, DELETE | Admin **ya da** Sahip (ReviewerId == `sub`) |
| **Appointment** | POST | Patient (PatientId = `sub`; istemcinin gövdesindeki PatientId yok sayılır), Admin (herhangi bir hasta için) |
| | GET liste / tek / detay | Patient: yalnız kendi randevuları (filtre zorlanır, başkasının → 404); Doctor: yalnız `doctor_id` ile eşleşenler; Admin: hepsi |
| | PUT (yeniden planlama) | Randevunun hastası, randevunun doktoru ya da Admin |
| | PATCH status | Doktor (kendi randevusu): Confirmed / Completed / Cancelled; Hasta (kendi randevusu): yalnız Cancelled; Admin: hepsi |
| | DELETE | Admin |
| | `{id}/detail` GET | Randevunun hastası, doktoru, Admin |
| | `{id}/detail` PUT | Randevunun doktoru, Admin |
| **Prescription** | POST, PUT, DELETE | Doctor (DoctorId = `doctor_id` claim'i; gövdedeki yok sayılır) ya da Admin |
| | GET liste / tek | Patient: yalnız kendi (PatientId == `sub`); Doctor: yalnız kendi yazdıkları; Admin: hepsi |
| **Payment** | POST | Patient (PatientId = `sub`) ya da Admin |
| | GET payments / invoices | Patient: yalnız kendi; Doctor: yalnız kendisine ait (DoctorId == `doctor_id`); Admin: hepsi |
| | POST `{id}/refund` | Admin |
| **Identity** | POST `auth/register`, `auth/login` | A |
| | GET `auth/me` | Auth |
| | GET `users/{id}` | Sahip (kendisi), Doctor, Admin |
| | GET `users?ids=` | Doctor, Admin |
| | `roles` (liste / oluştur), `users/{id}/roles` (ata / kaldır), `users/{id}/doctor-link` (bağla) | Admin |

Sahip olmayan kaynak için: **404** (varlığı sızdırmamak; Dalga 2'deki "bulunamadı" kuralıyla aynı), rol yetmiyorsa **403**, token yoksa / geçersizse **401**.

## 6. Identity değişiklikleri (3A)

- `POST api/auth/login` cevabı: `{ accessToken, tokenType: "Bearer", expiresAt (UTC), user: UserSummaryDto }`. Yanlış şifre / olmayan e-posta hâlâ AYNI 401 (kullanıcı numaralandırma kuralı), kilitli hesap 423.
- `GET api/auth/me` ([Authorize]): token'daki kullanıcının özeti.
- **Rol yönetimi (Admin):** `GET api/roles`, `POST api/roles` (yeni rol oluştur; ad doğrulaması: harf/rakam, ≤50, mevcut ise 409), `POST api/users/{id}/roles` ({role}), `DELETE api/users/{id}/roles/{role}` (sistemin son Admin'ini silmeye karşı koruma: son Admin'den Admin rolü alınamaz → 409). Admin kendini son Admin iken düşüremez.
- **Doktor bağlama:** `PUT api/users/{id}/doctor-link` ({doctorId}) (Admin): `AppUser.DoctorId` (string?, ≤64) alanı, kullanıcının `Doctor` rolü olmalı (değilse 409), aynı doktor kimliği iki kullanıcıya bağlanamaz (tekil indeks, 409). Token'a `doctor_id` claim'i bu alandan girer. **Doctor servisine soru SORULMAZ** (doktor var mı doğrulaması kapsam dışı; not).
- **İlk Admin (bootstrap):** açılışta, `Seed:AdminEmail` ve `Seed:AdminPassword` user-secrets / ortam değişkeninde VARSA ve sistemde hiç Admin yoksa o kullanıcı oluşturulur (şifre kuralları geçerli). Değerler appsettings'te / depoda YOK. Production'da yalnız ortam değişkeniyle.
- `Register` yine yalnız `Patient` verir (mass assignment testi aynen geçerli).
- Token imzalama: `JsonWebTokenHandler` + `RsaSecurityKey` + `SecurityAlgorithms.RsaSha256`, kimlik `kid` başlığı (anahtar parmak izi) token başlığına yazılır.
- Test: token'ın doğrulanması (imza, süre, issuer, audience, algoritma), `alg: none` ve HS256 ile yeniden imzalanmış token'ların REDDEDİLMESİ (klasik JWT saldırıları: "alg confusion"), süresi dolmuş token 401, rol claim'leri, `doctor_id` claim'i, son Admin koruması.

## 7. Güvenlik testleri (her servis, 3B)

Her korunan uç için en az: token yok → 401; geçersiz / süresi dolmuş token → 401; yanlış rol → 403; doğru rol → beklenen sonuç; Sahip kuralı olan uçlarda başkasının kaynağı → 404; **gövdedeki kimlik alanları (PatientId, DoctorId, ReviewerId) token'dakini ezmez** (mass assignment: istemci başkası adına işlem yapamaz).

## 8. Kapsam dışı (bilerek)

Refresh token ve çıkış (token iptali), JWKS ucu ve anahtar rotasyonu, servisten servise kimlik (client credentials), hız sınırı (rate limit), CORS (ekran dalgasında), e-posta doğrulama, şifre sıfırlama, `Doctor register` başvuru akışı.

## 9. Kurstan sapma kaydı

Kurs (tahmin): HS256 + ortak gizli anahtar. Biz: RS256 + açık anahtar. Hoca B8-B9'da `AddJwtBearer` ve `[Authorize(Roles=...)]` öğretecek; mantık aynı (token doğrulama + rol kontrolü), yalnız imza anahtarı türü ve ilkeler farklı.
