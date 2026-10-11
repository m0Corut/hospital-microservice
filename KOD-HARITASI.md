---
title: Hastane Mikroservis: Kod Haritası
created: 2026-10-09
modified: 2026-10-10
type: note
status: active
tags: [hastane-mikroservis, kod-haritasi, ogrenme]
---
# Kod Haritası

`OGRENME-NOTLARI.md` **neden** sorusunu cevaplar (kavramlar). Bu dosya **nasıl bağlandığını** gösterir: hangi dosya hangisini
çağırıyor, bir istek kodda hangi yoldan geçiyor, veritabanına tam olarak ne gidiyor.

Her yeni servis yazıldıkça bu dosyaya yeni bölüm eklenir. Satır numaraları
yazıldığı günkü commit'e aittir; kod değişince kayabilir, dosya adı ve metot adı kalıcıdır.

## 1. Büyük resim: bilgisayarında şu an ne çalışıyor?

```
Docker (docker compose up -d)
 ├─ hastane-mongo   :27017   MongoDB, veriler burada
 ├─ hastane-sqlserver :1433  SQL Server (Appointment, ileride Identity, Prescription)
 └─ hastane-seq     :5341    log ekranı (henüz servisler yazmıyor)

dotnet run ile açılan programlar (her biri ayrı)
 ├─ Hastane.Branch.Api   :5101  ──► MongoDB / hastane_branch / branches
 ├─ Hastane.Doctor.Api   :5102  ──► MongoDB / hastane_doctor / doctors
 ├─ Hastane.Review.Api   :5103  ──► MongoDB / hastane_review / reviews
 ├─ Hastane.Appointment.Api :5104 ──► SQL Server / hastane_appointment (EF Core)
 ├─ Hastane.Identity.Api    :5001 ──► SQL Server / hastane_identity (ASP.NET Core Identity)
 └─ Hastane.Prescription.Api :5105 ──► SQL Server / hastane_prescription (EF Core)

Servisler birbirini ÇAĞIRMAZ. Aralarındaki tek bağ metin kimlikler: doktorun içinde `BranchId`, yorumun içinde `DoctorId`,
randevunun içinde `DoctorId`, `PatientId`, `BranchId`, reçetenin içinde `AppointmentId`, `DoctorId`, `PatientId`.
```

## 2. Bir servisin içi (Branch örneği)

```
src/Services/Branch/Hastane.Branch.Api/
├─ Program.cs                 program açılırken her şeyi bağlar ("menüye yemekleri yazar")
├─ appsettings.json           ayar dosyası (bağlantı adresi BOŞ; gerçeği user-secrets'ta)
├─ Properties/launchSettings.json   port: 5101
├─ Controllers/BranchesController.cs   isteği karşılar (danışma)
├─ Services/IBranchService.cs         "servis ne yapabilir" listesi (arayüz)
├─ Services/BranchService.cs          asıl iş: veritabanına gider (kayıt memuru)
├─ Entities/Branch.cs                 veritabanındaki kaydın şekli (arşiv dosyası)
├─ Dtos/CreateBranchDto.cs            "yeni ekle" formu (Id yok)
├─ Dtos/UpdateBranchDto.cs            "güncelle" formu (Id var)
├─ Dtos/ResultBranchDto.cs            liste cevabı
├─ Dtos/GetByIdBranchDto.cs           tek kayıt cevabı
├─ Mapping/BranchMappingConfig.cs     Entity ↔ DTO çeviri kuralları
└─ Settings/DatabaseSettings.cs       ayarların C# karşılığı (+ IDatabaseSettings.cs)

tests/Hastane.Branch.Tests/
├─ BranchApiFactory.cs        test için programı açar + gerçek geçici MongoDB konteyneri kurar
├─ BranchesApiTests.cs        gerçek HTTP istekleri atar (7 test)
└─ BranchMappingConfigTests.cs   çeviri kurallarını denetler (2 test)
```

## 2b. Sıfırdan okuma rehberi: her klasör ne iş yapıyor?

Benzetme (hepsi aynı hastane): Servis = bir hastane birimi. Dışarıdan gelen istek = hastaneye gelen kişi.

```
        GELEN İSTEK ("Kardiyoloji'yi ekle")
                    │
                    ▼
   ┌───────────────────────────────────────────────────────────────┐
   │  Hastane.Branch.Api                                           │
   │                                                               │
   │  Controllers/   DANIŞMA    kapıdaki kişi: isteği karşılar,    │
   │       │                    işi içeri iletir, cevabı verir     │
   │       ▼                                                       │
   │  Services/      KAYIT      asıl işi yapar: arşive gider,      │
   │       │         MEMURU     kaydı koyar / okur / siler         │
   │       │                                                       │
   │       ├──► Mapping/   memurun ÇEVİRİ KURALI: arşiv dosyası ↔  │
   │       │               hastaya verilen kâğıt                    │
   │       │   Entities/   ARŞİV DOSYASI şablonu (kayıt nasıl durur)│
   │       │   Dtos/       HASTANIN GÖRDÜĞÜ KÂĞITLAR (formlar)     │
   │       ▼                                                       │
   │  Settings/      REHBER     "arşiv odası nerede?" bilgisi      │
   └───────┬───────────────────────────────────────────────────────┘
           ▼
     MongoDB  =  ARŞİV ODASI (Docker'da, kayıtlar burada durur)
```

İki ayrı zaman var, karıştırma:

```
PROGRAM AÇILIRKEN (bir kez)                  İSTEK GELİNCE (her istekte)
──────────────────────────                   ───────────────────────────
Program.cs      hastanenin sabah açılışı     Controllers/   karşılar
appsettings.json  ayar panosu                Services/      işi yapar
launchSettings.json  kapı numarası (port)    Mapping/       çevirir
Settings/       rehberin şablonu             Entities/ + Dtos/  veri taşır
```

| Parça | Ne işe yarıyor (tek cümle) |
|---|---|
| `Program.cs` | Program açılırken "kim kimi ister, kime ne verilir" listesini kurar |
| `appsettings.json` | Ayarların yazıldığı dosya (veritabanı adı, koleksiyon adı); şifre burada YOK |
| `Properties/launchSettings.json` | Programın hangi kapıdan (port 5101) dinleyeceğini söyler |
| `Controllers/` | İstekleri karşılar, işi servise iletir, cevabı geri verir; kendisi iş yapmaz |
| `Services/IBranchService.cs` | "Servis neler yapabilir" listesi (yalnız başlıklar, içi boş) |
| `Services/BranchService.cs` | O listedeki işlerin gerçekten yapıldığı yer; MongoDB'ye burası gider |
| `Entities/` | Veritabanındaki kaydın şekli (hangi alanlar, hangi tip) |
| `Dtos/` | Dışarıya verilen / dışarıdan alınan formlar (kayıtla aynı olması şart değil) |
| `Mapping/` | Entity ↔ DTO çevirisinin kuralları |
| `Settings/` | Ayarların C# karşılığı; `appsettings.json`'dan okunan değerler buraya dolar |
| `tests/.../BranchApiFactory.cs` | Test için programı açar ve geçici bir MongoDB kurar |
| `tests/.../BranchesApiTests.cs` | Programa gerçek istekler atar, cevapları kontrol eder |
| `tests/.../BranchMappingConfigTests.cs` | Çeviri kurallarında unutulan alan var mı bakar |

## 3. Program açılırken ne oluyor? (`Program.cs`)

Program açıldığında bir kez, yukarıdan aşağı çalışır. Amaç: "kim kimi ister, kim kime verilir" listesini kurmak.

| Satır | Ne yapıyor | Hangi sorunun cevabı |
|---|---|---|
| 10 | `AddControllers()` | "Controller dosyalarını bul ve devreye al" |
| 11 | `AddOpenApi()` | API'nin kendi dokümanını üret (`/openapi/v1.json`) |
| 14-15 | Mapster'a "kuralları `BranchMappingConfig`'te ara" der, `IMapper`'ı DI'ya koyar | `_mapper.Map<...>` çağrıları nereden gelecek? |
| 18 | `appsettings.json`'daki `DatabaseSettings` bölümünü `DatabaseSettings` sınıfına bağlar | `IOptions<DatabaseSettings>` isteyen kime ne verecek? |
| 21-22 | `IMongoClient` **tek kopya** (Singleton) olarak kurulur, adresi ayardan alır | Veritabanı bağlantısı nereden gelecek? |
| 23 | `IBranchService` istenince `BranchService` ver (her istek için ayrı kopya) | Controller'a servis nasıl gelecek? |
| 32 | `MapControllers()` | `api/branches` adresini controller metotlarına bağla |

## 4. Bir istek kodda hangi yoldan geçiyor?

### 4.1 `POST /api/branches` (yeni branş ekle)

Gönderilen:

```json
{ "name": "Kardiyoloji", "description": "Kalp ve damar hastalıkları", "icon": "fa-heart-pulse" }
```

```
1. Tarayıcı / Postman  ──►  http://localhost:5101/api/branches  (5101'i Branch programı dinliyor)
2. ASP.NET yolu eşler: POST + api/branches  →  BranchesController.Create        (BranchesController.cs:28)
3. Gelen JSON, CreateBranchDto'ya çevrilir. [Required] Name boşsa burada 400 döner, koda hiç girilmez
4. Controller:  _service.CreateAsync(dto, ct)                                      (BranchesController.cs:30)
5. BranchService.CreateAsync                                                       (BranchService.cs:38)
     a) _mapper.Map<BranchEntity>(dto)   CreateBranchDto → Branch, Id boş kalır   (satır 40; kural: BranchMappingConfig.cs:15)
     b) _branches.InsertOneAsync(entity)  ──►  MongoDB'ye YAZMA                    (satır 41)
        MongoDB kaydı ekler, ürettiği Id'yi entity.Id'nin içine yazar
     c) _mapper.Map<ResultBranchDto>(entity)  Branch → dışarı verilecek form       (satır 42)
6. Controller:  CreatedAtAction(...)  →  201 Created + Location başlığı            (BranchesController.cs:31)
7. Cevap JSON olarak döner:  { "id": "6712a...", "name": "Kardiyoloji", ... }
```

**MongoDB'ye giden komutun karşılığı** (MongoDB kabuğunda yazsaydın):

```javascript
db.branches.insertOne({ name: "Kardiyoloji", description: "Kalp ve damar hastalıkları", icon: "fa-heart-pulse" })
// MongoDB kaydı hastane_branch veritabanının "branches" koleksiyonuna ekler ve _id üretir
```

### 4.2 `GET /api/branches` (hepsini listele)

```
Controller.GetAll (BranchesController.cs:17)
  → BranchService.GetAllAsync (BranchService.cs:24)
      → _branches.Find(_ => true).ToListAsync(ct)  (satır 26)      MongoDB: db.branches.find({})
      → _mapper.Map<List<ResultBranchDto>>(list)   (satır 27)      her Branch → ResultBranchDto
  → Ok(...)  → 200 + JSON dizisi
```

### 4.3 `GET /api/branches/6712a...` (tek branş)

```
Controller.GetById (BranchesController.cs:21)
  → BranchService.GetByIdAsync (BranchService.cs:30)
      → ObjectId.TryParse(id)  (satır 33)   id biçimi bozuksa MongoDB'ye hiç gitmez, null döner  →  404
      → _branches.Find(b => b.Id == id).FirstOrDefaultAsync  (satır 34)   MongoDB: db.branches.findOne({_id: ObjectId("6712a...")})
      → kayıt yoksa null  →  Controller 404 (BranchesController.cs:24)
```

### 4.4 `PUT /api/branches/6712a...` (güncelle)

```
Controller.Update (BranchesController.cs:35)
  → yoldaki id ile gövdedeki id aynı mı?  değilse 400 (satır 37)
  → BranchService.UpdateAsync (BranchService.cs:45)
      → _branches.ReplaceOneAsync(b => b.Id == dto.Id, entity)  (satır 49)
        MongoDB: db.branches.replaceOne({_id: ...}, { name: ..., description: ..., icon: ... })
        Kaydın TAMAMI yeni değerle değişir (alan gönderilmezse boş kalır, "kısmi güncelleme" yok)
      → MatchedCount > 0 ise true  →  Controller 204 No Content, değilse 404
```

### 4.5 `DELETE /api/branches/6712a...`

```
Controller.Delete (BranchesController.cs:42)
  → BranchService.DeleteAsync (BranchService.cs:53)
      → _branches.DeleteOneAsync(b => b.Id == id)  (satır 56)    MongoDB: db.branches.deleteOne({_id: ...})
      → DeletedCount > 0 ise true  →  204, değilse 404
```

## 5. Kim kimi çağırıyor? (bağımlılık zinciri)

```
BranchesController ──ister──► IBranchService ──(Program.cs:23)──► BranchService
BranchService ──ister──► IMapper          (Mapster, Program.cs:15)
              ──ister──► IOptions<DatabaseSettings>   (appsettings.json, Program.cs:18)
              ──ister──► IMongoClient     (tek bağlantı, Program.cs:21)
```

Hiçbir yerde `new BranchService(...)` yok. Kurucu (constructor) parametreleri, "bana bunlar lazım" listesi: `BranchService.cs:16`.
ASP.NET bu listeye bakıp `Program.cs`'teki kayıtlardan her birini bulur ve verir. Bu mekanizmaya DI diyoruz.

## 6. Veritabanında ne duruyor?

```
MongoDB sunucusu (Docker, :27017)
 ├─ hastane_branch        ← Branch programının veritabanı
 │    └─ branches         ← koleksiyon (SQL'deki "tablo" karşılığı)
 │         { _id, name, description, icon }
 └─ hastane_doctor        ← Doctor programının veritabanı
      └─ doctors
           { _id, firstName, ..., branchId, educations: [...], awards: [...], clinicLocations: [...] }
```

Adlar ayardan gelir: `DatabaseSettings.cs:6-7` (`DatabaseName`, `BranchCollectionName`). Kodda `"branches"` diye bir metin yok.

## 7. Doctor: Branch'ten farkı nerede?

Aynı dosya düzeni (`src/Services/Doctor/Hastane.Doctor.Api/`), aynı istek yolu. Farklar:

| Fark | Nerede | Ne yapıyor |
|---|---|---|
| İç içe listeler | `Entities/Doctor.cs:28-31` | `Educations`, `Experiences`, `Awards`, `ClinicLocations` doktorun kaydının içinde |
| Çeviri kuralları çoğaldı | `Mapping/DoctorMappingConfig.cs:13-20` | İç içe her sınıf için ayrı çeviri çifti (Mapster'ın sıkı modu açıkça ister) |
| Liste ve detay DTO'su ayrı | `Dtos/ResultDoctorDto.cs` (Phone/Email yok), `Dtos/GetByIdDoctorDto.cs` | Listede telefon ve e-posta görünmez |
| Branşa göre süzme | `DoctorsController.cs:15-20` → `DoctorService.cs:31-35` | `GET api/doctors?branchId=6711f...` → MongoDB: `db.doctors.find({ branchId: "6711f..." })` |
| Branşa bağlantı | `Entities/Doctor.cs:20-21` | `BranchId` yalnız metin; Doctor, Branch'i çağırmıyor |

```
GET /api/doctors?branchId=6711f...
Controller.GetAll (DoctorsController.cs:17)   branchId doluysa → GetByBranchIdAsync, boşsa → GetAllAsync
  → DoctorService.GetByBranchIdAsync (DoctorService.cs:31)
      → _doctors.Find(d => d.BranchId == branchId)   MongoDB: db.doctors.find({ branchId: "6711f..." })
      → ResultDoctorDto listesi (telefon ve e-posta olmadan)
```

Doctor, "bu BranchId hangi branş?" sorusunu **şu an cevaplayamaz** (adı Branch'te). İleride Doctor, Branch'in `GET /api/branches/{id}`
adresini `HttpClient` ile çağıracak; ilk servisler arası istek o olacak.

## 8. Testler nasıl çalışıyor? (`tests/Hastane.Branch.Tests`)

```
1. BranchApiFactory açılır: gerçek bir MongoDB konteyneri başlatır (BranchApiFactory.cs:10-12)
2. Ayarları testin konteynerine çevirir (BranchApiFactory.cs:16-18):
     adres = konteynerin adresi, veritabanı = hastane_branch_test
3. Branch programını AYNI Program.cs ile bellekte açar (port açmadan)
4. Test, HTTP isteği atar (POST /api/branches ...), cevabı kontrol eder
5. Test bitince konteyner silinir; senin MongoDB'ne ve verine hiç dokunulmaz
```

Üretim kodu ile test kodu aynı. Tek fark, ayarın nereden geldiği. Bu yüzden ayarı kodun dışında tutmuştuk.

## 7b. Doctor: arayüz alanları, arama, toplu getirme (Dalga 2.5, A1)

Doccure arama kartı (kurs ekran görüntüsü, depoda yok) için Doctor genişledi. Satır numaraları **yaklaşık**: dosya sonradan düzeltmelerle kaydı; güncel dosyadan okuyun.

```
Entities/Doctor.cs          + Gender (DoctorGender), Degrees, Services, Gallery, SocialLinks
Entities/Nested.cs          ClinicLocation + HourlyFee (Decimal128), Currency, Schedule;  ScheduleSlot;  SocialLinks
Dtos/DoctorWriteDto.cs      Create/Update ORTAK alanlar + doğrulama öznitelikleri (CreateDoctorDto / UpdateDoctorDto bundan türer)
Dtos/NestedDtos.cs          ClinicLocationDto (IValidatableObject), ScheduleSlotDto, SocialLinksDto
Dtos/SearchDtos.cs          PagedResult<T>, DoctorSearchItemDto, DoctorSummaryDto, DoctorSearchQuery (Page [Range 1..10000])
Validation/DoctorRules.cs   TEK KAYNAK: sınırlar, saat regex'i, URL, program doğrulaması + çakışma kuralı
Validation/ListAttributes.cs, DoctorInputValidator.cs   liste öğesi öznitelikleri; SERVİS katmanı doğrulaması (DoctorValidationException)
Mapping/DoctorDerived.cs    MinFee / MaxFee / Currency / PrimaryCity HESAPLANIR (saklanmaz)
Services/DoctorService.cs   SearchAsync, GetBatchAsync, BuildSearchFilter;  Services/DoctorIndexInitializer.cs  açılışta 3 dizin
Controllers/DoctorsController.cs   search ve batch uçları; DoctorValidationException → 400
```

`Program.cs`: `:12-13` controllers + `JsonStringEnumConverter` (enum = metin, sayı kabul edilmez); `:30` `AddHostedService<DoctorIndexInitializer>`.

**`GET /api/doctors/search`** — `DoctorsController.cs:26` → `DoctorService.cs:85` `SearchAsync` → `BuildSearchFilter` (~:111) → `Find().Project().Sort().Skip().Limit()` (~:91) ve `CountDocumentsAsync` (~:93).
Filtreler: metin regex (`Regex.Escape`, ~:125), cinsiyet, çoklu branş (`$in`), **konum filtreleri tek `$elemMatch` içinde** (`DoctorService.cs:158-179`: şehir, ücret aralığı, tarih→haftanın günü; verilmeyen filtre girmez),
sıralama (ad / `feeAsc` / `feeDesc`, her sıralamanın sonunda `_id` eşitlik bozucu), sayfa: `MaxPage = 10000` (~:106), `SkipCount` long + checked (~:108), kontrol (~:118).

```javascript
// MongoDB karşılığı (örnek: Ankara'da, 200-600 aralığında, salı çalışan, kadın, 2 branştan biri, "diş" geçen)
db.doctors.find({ $and: [
  { $or: [ {FirstName:/diş/i}, {LastName:/diş/i}, {Degrees:/diş/i}, {Services:/diş/i}, {"ClinicLocations.ClinicName":/diş/i} ] },
  { Gender: "Female" },
  { BranchId: { $in: ["a","b"] } },
  { ClinicLocations: { $elemMatch: {                       // AYNI konum hepsini sağlamalı
      City: "Ankara",
      HourlyFee: { $gte: NumberDecimal("200"), $lte: NumberDecimal("600") },
      Schedule: { $elemMatch: { DayOfWeek: "Tuesday" } } } } }
] }, { FirstName:1, LastName:1, Title:1, Degrees:1, ... /* Phone, Email, Biography YOK */ })
  .sort({ LastName:1, FirstName:1, _id:1 }).skip((page-1)*pageSize).limit(pageSize)
db.doctors.countDocuments(<aynı filtre>)                   // TotalCount ayrı sorgu
```

**`GET /api/doctors/batch?ids=a,b,c`** — `DoctorsController.cs:34` → `DoctorService.cs` `GetBatchAsync` (ayrıştırma ve 100 sınırı, tek sorgu, istenen sırayı koru):
`db.doctors.find({ _id: { $in: [ObjectId("a"), ...] } }, { FirstName:1, LastName:1, Title:1, Degrees:1, BranchId:1, ImageUrl:1 })`. Geçersiz kimlikler sessizce atılır, tekrarlar silinir, en çok 100 (aşarsa 400), boş → `[]`.

**POST / PUT**: `DoctorsController.cs:49/60` → `DoctorService.cs:54/62` önce `ValidateOrThrow` (DTO doğrulaması atlansa bile servis yakalar) sonra `insertOne` / `replaceOne`.
Rotalar: `search` ve `batch` sabit, `{id}` parametreli; sabit rota kazanır (testle kilitli).

Dizinler (açılışta, idempotent): `ix_branch`, `ix_gender`, `ix_city` (`ClinicLocations.City`). Hata olursa loglanır, uygulama düşmez (10 sn zaman aşımı). Konuşma: yalnız MongoDB.

Bilinen sınırlar (bilerek bırakıldı): `Gender` alanı olmayan eski belgeler `Male` okunur ama `gender=Male` filtresi onları eşlemez; `feeAsc`'ta ücretsiz doktorlar başa gelir (alan yok = en küçük); (2026-10-10 düzeltildi: ücret filtresi/sıralaması `HourlyFee > 0` ile MinFee'ye tutarlı, ücretsizler sona, bkz. 8k);
ad sıralaması ikili (Türkçe Ç, Ş, İ sona; büyük harf küçükten önce; Collation eklenirse eşitlik dizinleri kullanılamaz); `city` tam eşleşme ve büyük-küçük harf duyarlı; karışık para biriminde MinFee/MaxFee
ücreti olan İLK konumun birimine göredir; derin sayfa pahalıdır (üst sınır 10000); metin araması regex olduğu için dizin kullanamaz (B21'de Elasticsearch).

## 8b. Review: yorum ve puan servisi

`src/Services/Review/Hastane.Review.Api/`, port 5103, MongoDB `hastane_review` / `reviews`. Dosya düzeni Doctor ile aynı;
fark: `Dtos/DoctorRatingSummaryDto.cs` (özet) ve `Services/ReviewService.cs` içindeki aggregation.

```
Entities/Review.cs               Id, DoctorId (yalnız metin), ReviewerName, Rating, Comment?, CreatedAt (UTC, sunucu atar)
Dtos/CreateReviewDto.cs          DoctorId, ReviewerName, Rating [Range 1-5], Comment? [MaxLength 1000]   (Id ve CreatedAt YOK)
Dtos/UpdateReviewDto.cs          Id, ReviewerName, Rating, Comment?   (DoctorId ve CreatedAt YOK: gövdede gelse bile yok sayılır)
Dtos/ResultReviewDto.cs          çıkış: tüm alanlar
Dtos/DoctorRatingSummaryDto.cs   çıkış: DoctorId, ReviewCount, AverageRating
Mapping/ReviewMappingConfig.cs   Entity→Result, Create→Entity (Id, CreatedAt yok sayılır)
Services/ReviewService.cs        Mongo işlemleri + mantık
Controllers/ReviewsController.cs api/reviews uçları
```

`Program.cs` Doctor'dakiyle aynı kayıtlar (`:10` controllers, `:13-14` Mapster, `:17` ayarlar, `:21-22` tek `IMongoClient`,
`:24` `IReviewService`). Başka servisi çağırmıyor; altyapı olarak yalnız MongoDB.

Endpoint istek yolları (C = `Controllers/ReviewsController.cs`, S = `Services/ReviewService.cs`):

| Uç | Controller | Service | MongoDB komutunun karşılığı |
|---|---|---|---|
| GET `/api/reviews` | C:17 | S:25 `GetAllAsync` (S:27) | `db.reviews.find({})` |
| GET `/api/reviews?doctorId=X` | C:17 | S:31 `GetByDoctorIdAsync` (S:33-34) | `find({DoctorId:"X"}).sort({CreatedAt:-1})` (yeniden eskiye) |
| GET `/api/reviews/{id}` | C:23 (yoksa 404) | S:39 `GetByIdAsync` (S:42 bozuk id → null, S:43 Find) | `findOne({_id: ObjectId(id)})` |
| GET `/api/reviews/doctors/{doctorId}/summary` | C:31 | S:78 `GetDoctorSummaryAsync` (S:81-83 aggregation, S:85-90 yuvarlama + boş durum) | `aggregate([{$match:{DoctorId:"X"}},{$group:{_id:"$DoctorId",Count:{$sum:1},Average:{$avg:"$Rating"}}}])` |
| POST `/api/reviews` | C:35 (201: C:38) | S:47 `CreateAsync` (S:52-53 CreatedAt ataması, S:54 Insert) | `insertOne({DoctorId, ReviewerName, Rating, Comment, CreatedAt: new Date()})` |
| PUT `/api/reviews/{id}` | C:42 (Id uyuşmazsa 400: C:44, bulunamazsa 404: C:45) | S:58 `UpdateAsync` (S:62-67 `UpdateOne` + `Set`) | `updateOne({_id: ObjectId(id)}, {$set: {ReviewerName, Rating, Comment}})` |
| DELETE `/api/reviews/{id}` | C:49-50 | S:71 `DeleteAsync` (S:74) | `deleteOne({_id: ObjectId(id)})` |

İstek yolu örneği (özet): `GET /api/reviews/doctors/6712a.../summary` → Controller (C:31) → `GetDoctorSummaryAsync` (S:78) →
MongoDB gruplama yapar, tek satır `{Count, Average}` döner → servis yuvarlar (S:90) → `DoctorRatingSummaryDto` → 200.
Yorumu olmayan doktorda grup boş gelir; servis `ReviewCount=0, AverageRating=0` yazar ve yine 200 döner.

Doğrulama hataları (`Rating` 0 ya da 6, boş `ReviewerName`, 1001 karakterlik yorum): `[ApiController]` (C:7) modeli kontrol eder,
hata varsa controller metoduna girilmeden 400 döner.

## 8b-2. Review: toplu özet ve tavsiye oranı (Dalga 2.5, A2)

`28d84d4`. Arama kartında "puan, yorum sayısı, %tavsiye" gerekiyordu (Doccure `ekran-2`). Review iki şey kazandı:
`DoctorRatingSummaryDto.RecommendationPercent` ve **toplu** uç `GET /api/reviews/summaries?doctorIds=a,b,c`.

```
Dtos/DoctorRatingSummaryDto.cs     DoctorId, ReviewCount, AverageRating, RecommendationPercent (satır 9-10)
Services/IReviewService.cs         GetDoctorSummariesAsync (satır 21)
Services/ReviewService.cs          AggregateByDoctorAsync (ortak aggregation), ToSummary (yuvarlama), GetDoctorSummariesAsync
Controllers/ReviewsController.cs   GetSummaries (C:39-47)
```

İstek yolu (örnek): `GET /api/reviews/summaries?doctorIds=d1,d2,d1`
1. Controller C:39 → `Split(',', RemoveEmpty | Trim)` + `Distinct()` (ilk sıra korunur, C:43-44) → d1, d2. Yüzden fazlaysa 400 (C:46).
2. Servis S:84 `GetDoctorSummariesAsync` → boş liste kısayolu (S:86), sonra **tek** aggregation (S:89).
3. `AggregateByDoctorAsync` (S:99-108) MongoDB'ye gider; gelen gruplar `DoctorId`'ye göre sözlüğe alınır.
4. İstenen her kimlik için kayıt (S:94): yorumu olmayan doktor için `ReviewCount=0, AverageRating=0, RecommendationPercent=0`. Cevap istenen sırayla döner.

MongoDB karşılığı:

```javascript
db.reviews.aggregate([
  { $match: { DoctorId: { $in: ["d1","d2"] } } },
  { $group: { _id: "$DoctorId",
              Count: { $sum: 1 },
              Average: { $avg: "$Rating" },
              Recommended: { $sum: { $cond: [ { $gte: ["$Rating", 4] }, 1, 0 ] } } } }
])
// kod tarafı: RecommendationPercent = round(100 * Recommended / Count, AwayFromZero)
```

Tek doktor özet ucu (`GET /api/reviews/doctors/{doctorId}/summary`, C:31 → S:78) aynı `AggregateByDoctorAsync` + `ToSummary` yolunu kullanır (`$in` yerine `DoctorId: "X"`).
`ToSummary` yuvarlaması `S:110-118`. `summaries` rotası sabittir; `{id}` gerçek ObjectId (24 karakter) olduğundan karışmaz (testle doğrulandı).

Bilinen sınırlar: 100 sınırı tekrarlar silindikten SONRA sayılır (aynı kimlik 150 kez gelirse 400 yerine tek kayıt döner, bilinçli); kimlik eşleşmesi büyük-küçük harfe duyarlı;
`GetDoctorSummariesAsync` kimliklerin temizlenmiş olmasını çağırana bırakır (interface yorumunda belirtildi).

## 8f. Payment: ödeme ve fatura (SQL Server + EF Core, sahte sağlayıcı) (Dalga 2.5, B2)

`67ce20f`. `src/Services/Payment/Hastane.Payment.Api/`, port 5111, SQL Server `hastane_payment`. **Gerçek para yok:** `MockPaymentProvider` (test kartı `4242424242424242` başarı, `4000000000000002` red, `4000000000009995` yetersiz bakiye; Luhn doğrulaması).

```
Controllers/PaymentsController.cs, InvoicesController.cs    api/payments, api/invoices
Services/PaymentService.cs        ödeme akışı, idempotency, doğrulama, iade, telafi (HTTP bilmez; OperationResult döner)
Services/InvoiceService.cs        fatura listesi/detayı;  Services/InvoiceCalculator.cs  KDV dahil tutardan Subtotal/Tax ayrıştırma + INV-yıl-sıra biçimi
Services/IdempotencyKeyRules.cs   anahtar biçimi ^[A-Za-z0-9_-]{16,100}$;  Services/RequestFingerprint.cs  gövde özeti (SHA-256, JSON dizisi, kart verisi hariç)
Providers/IPaymentProvider.cs     ChargeAsync / RefundAsync sözleşmesi + CardDetails (ToString maskeli)
Providers/MockPaymentProvider.cs, CardValidator.cs   sahte sağlayıcı; Luhn, SKT, CVV, marka
Entities/Payment.cs, Invoice.cs, InvoiceLine.cs, PaymentStatus.cs, PaymentFailureKind.cs
Data/PaymentDbContext.cs (SEQUENCE), PaymentConfiguration.cs, InvoiceConfiguration.cs, InvoiceLineConfiguration.cs
Dtos/ (CreatePaymentDto, CardDto, PaymentResultDto, fatura DTO'ları, PagedResult)   Settings/BillingOptions.cs   Migrations/*_InitialCreate.*
```

`Program.cs`: `:12-14` controllers + JSON string enum; `:21-24` `Billing` ayarı (TaxPercent, AllowedCurrencies, PendingStaleSeconds, PendingWaitSeconds); `:28-29` `AddDbContext`; `:32` `AddSingleton<IPaymentProvider, MockPaymentProvider>` (gerçek sağlayıcıya geçişte değişen TEK satır); `:34-35` servisler; `:46` Development'ta `Migrate()`.

Tablolar:

```
Payments                                      Invoices                                       InvoiceLines
 Id (PK)                                       Id (PK)                                        Id (PK)
 AppointmentId (Guid), PatientId, DoctorId     InvoiceNumber varchar(20) UNIQUE               InvoiceId → Invoices (CASCADE)
 Amount decimal(18,2), Currency char(3)        PaymentId → Payments (UNIQUE, RESTRICT)        Description, Quantity, UnitPrice, LineTotal
 Status (Pending/Paid/Failed/Refunded, metin)  Subtotal, TaxPercent, TaxAmount, Total decimal(18,2)
 CardBrand, CardLast4 char(4)                  IssuedAt, Currency, PatientId, DoctorId
 ProviderReference, FailureReason, FailureKind
 IdempotencyKey (UNIQUE, BIN2), RequestHash
 RefundRequestedAt?, RefundedAt?, CreatedAt, PaidAt?
```

```sql
CREATE SEQUENCE InvoiceNumberSeq AS int START WITH 1 INCREMENT BY 1;     -- fatura numarası: NEXT VALUE FOR InvoiceNumberSeq
CREATE UNIQUE INDEX UX_Payments_IdempotencyKey   ON Payments(IdempotencyKey);
CREATE UNIQUE INDEX UX_Payments_Appointment_Paid ON Payments(AppointmentId) WHERE [Status] = N'Paid';   -- bir randevu için TEK Paid
CREATE UNIQUE INDEX UX_Invoices_InvoiceNumber    ON Invoices(InvoiceNumber);
```

**POST `/api/payments`** (başlık `Idempotency-Key` zorunlu): `PaymentsController.cs:31-38` → `PaymentService.CreateAsync` (`:50`):
1. `:62` aynı anahtar var mı → **replay** (gövde özeti aynıysa ilk sonuç, değilse 409). `:69` iptal kontrolü: para hareketinden önceki SON nokta.
2. `:89` **Pending rezervasyonu** (`INSERT Payments ... Status='Pending'`, anahtar tekil indeksle korunur; çakışırsa `:98` replay).
3. `:105` `ChargeAsync(..., CancellationToken.None)`. İstisna → `MarkFailed(ProviderError)` → 502. `:132` red → Failed + `Declined` → 402.
4. `:145` `NEXT VALUE FOR`; `:151` **tek `SaveChanges`**: `UPDATE Payments (Paid)` + `INSERT Invoices` + `INSERT InvoiceLines`.
5. `:153-157` Paid indeksi ihlali → `CompensateAsync(Duplicate)` → 409; `:158-161` başka herhangi hata → `CompensateAsync(RecordingFailed)` → 502. `CompensateAsync` (`:168`): sağlayıcıdan telafi iadesi (`:180`), satır Failed (`MarkFailedAsync` `:207`, yeni scope + `ExecuteUpdate`); iade de başarısızsa LogCritical (PaymentId + ProviderReference).
6. Bayat Pending (`ReplayAsync` `:233-244`): yaş > 120 sn → beklemeden 409 + LogWarning; taze Pending için en çok 15 sn bekle.

**POST `/api/payments/{id}/refund`**: `PaymentsController.cs:41-43` → `RefundAsync` (`:385`): (1) `:399` işaret `UPDATE Payments SET RefundRequestedAt=@now WHERE Id AND Status='Paid' AND RefundRequestedAt IS NULL` (0 satır → 409);
(2) `:408` sağlayıcı iadesi (None; istisna = red); red/istisnada `:421` işaret geri alınır (`UPDATE ... SET RefundRequestedAt=NULL WHERE Id AND Status='Paid'`); (3) başarıda `:432` `UPDATE ... SET Status='Refunded', RefundedAt=@now WHERE Id AND Status='Paid' AND RefundRequestedAt IS NOT NULL`. **İade süresince Status Paid kalır** (indeks yeni ödemeyi engellemeye devam eder). Fatura silinmez.

**GET**: `/api/payments` (`:18-20` → `PaymentService.cs:286`): `COUNT(*)` + sayfalı `SELECT ... ORDER BY CreatedAt DESC, Id DESC OFFSET ... FETCH NEXT ...` (`?patientId&doctorId&appointmentId&status&page&pageSize`, 1..50); `/api/payments/{id}` (`:23-28` → `:314`); `/api/invoices` (`InvoicesController.cs:18-25` → `InvoiceService.cs:19`), `/api/invoices/{id}` (kalemlerle, `:27-32` → `:43`).
Cevaplarda yalnız marka + son 4 hane; `ProviderReference`, `RequestHash`, `RefundRequestedAt` dışarı verilmez.

Başka servisle konuşma (2026-10-10'dan beri): kart çekilmeden ÖNCE `PaymentService.CreateAsync` → `VerifyAsync`: Appointment `GET /api/appointments/{id}` (kullanıcının `Authorization` başlığı aynen iletilir, sahipliği Appointment karar verir; başkasının randevusu 404) ve Doctor `GET /api/doctors/{id}` (`minFee` + `currency`). Kod: `Integration/` (`AppointmentLookup`, `DoctorFeeLookup`, `DownstreamClient`), ayar `Services:AppointmentBaseUrl` / `DoctorBaseUrl` / `TimeoutSeconds` (5 sn). DoctorId ve PatientId randevudan gelir; istemcinin Amount/Currency'si yalnız karşılaştırılır (uyuşmazsa 409 "Tutar güncel değil"). Ret kodları: randevu yok 404, iptal 409, ücret yok 409, tutar uyuşmaz 409, aşağı akış kapalı/zaman aşımı 503; hiçbirinde kart çekilmez, satır yazılmaz. **Ücret kuralı tek yerde:** Doctor `Mapping/DoctorDerived.cs` (ücreti > 0 olan ilk konumun birimi, o birimdeki en düşük ücret); WebUI de aynı alanları okur.

**Bilinen sınırlar** (bilerek bırakıldı; gerçek sağlayıcıya geçerken şart olanlar işaretli):
- ~~İstemci tutarı doğrulanmıyor~~ **Kapandı (2026-10-10, `3f22f67`)**: yukarıdaki sunucu doğrulaması; iki tur kod incelemesi, gerçek servislerle duman testi (olmayan randevu 404, kart çekilmedi).
- ~~Kimlik doğrulama yok~~ Dalga 3B'de eklendi (8h).
- **Sağlayıcı çağrısına zaman aşımı yok (gerçek sağlayıcı için ŞART)**: `CancellationToken.None` istemci iptalinden korur ama sağlayıcı asılırsa istek ve Pending satır sonsuza dek bekler; sınırlı bir CTS (ör. 30 sn) ve "belirsiz → uzlaştırmaya aday" işareti gerekir.
- **Uzlaştırma (reconciliation) işi yok**: süreç sağlayıcı çağrısı sırasında çökerse satır Pending kalır (aynı anahtar 120 sn sonra anında 409 verir); iade işareti alındıktan sonra çökerse `RefundRequestedAt` dolu kalır ve o ödeme "iade zaten talep edildi" 409'unda takılır (güvenli yönde hata). Çözüm: sağlayıcı ile eşleştiren arka plan işi.
- Sağlayıcı istisnası paranın çekilmiş olabileceği durumu da kapsar; satır `ProviderError` Failed işaretlenir, iade denenmez (belirsizlik uzlaştırmaya aittir).
- Kapanış UPDATE'i 0 satır etkilerse (iade başarılı, durum güncellenemedi) LogCritical atılır ama 200 + Paid gövde döner (502 daha tutarlı olurdu).
- `ClearRefundMarker` koşulu işaretin değerini doğrulamaz (fiilen yalnız işareti alan istek siler; `RefundRequestedAt == now` eklenebilir).
- Ham `ex.Message` loglanır: gerçek sağlayıcı SDK'sı mesaja istek parçası koyarsa kart verisi sızabilir; geçişte gözden geçirilmeli.
- Fatura SEQUENCE'i yıl başında sıfırlanmaz, başarısız / telafi edilen denemelerde numara boşluğu olabilir; mali mevzuat gerekirse ayrıca ele alınmalı.
- Kart marka tespiti yalnız ilk haneden; CVV uzunluğu markaya bağlı değil; `Status` sütununda CHECK kısıtı yok.
- Aynı anahtar + aynı tutar/randevu ama FARKLI kart → ilk sonuç döner (kasıtlı; kart özetini bile saklamamak için).

## 8c. Appointment: randevu servisi (SQL Server + EF Core)

`src/Services/Appointment/Hastane.Appointment.Api/`, port 5104, SQL Server `hastane_appointment`. MongoDB servislerinden farkı veri katmanı:
`MongoClient` yerine `DbContext`, kayıt tarifi `Fluent API`, şema `Migrations/`.

```
Entities/Appointment.cs, AppointmentDetail.cs, AppointmentStatus.cs   kayıtlar + durum enum'u
Data/AppointmentDbContext.cs            iki DbSet; ayar sınıflarını assembly'den yükler
Data/AppointmentConfiguration.cs        tablo, ilişki (1—0..1), indeksler, enum→string, uzunluklar
Data/AppointmentDetailConfiguration.cs  uzunluklar, AppointmentId üzerinde TEKİL indeks
Data/UtcDateTimeConverter.cs            tarihleri UTC'ye normalize eder (okurken Kind=Utc → JSON'da "Z")
Validation/FutureDateAttribute.cs       geçmiş tarih → 400
Dtos/ ...                               Create, Update (yeniden planlama), Result, GetById(+Detail), UpdateStatus, detay DTO'ları
Mapping/AppointmentMappingConfig.cs     Mapster; sunucu alanları Ignore
Services/AppointmentService.cs          CRUD, filtre, durum, yeniden planlama
Services/AppointmentDetailService.cs    detay get / upsert
Services/AppointmentStatusTransitions.cs  durum geçiş tablosu (tek yerde)
Services/OperationResult.cs             servis HTTP bilmez; 404/409'u bu zarfla bildirir
Controllers/AppointmentsController.cs   HTTP katmanı
Migrations/20261009181953_InitialCreate.*  şema sürümü
```

`Program.cs`: `:10-12` controllers (+ enum'u metin okuyan JSON dönüştürücü), `:13` OpenAPI, `:15-16` Mapster, `:20-21` `AddDbContext`
(**Scoped**; bağlantı cümlesi tembel okunur), `:23-24` iki servis Scoped, `:28-36` yalnız Development'ta `MapOpenApi` ve açılışta
`Database.Migrate()` (`:35`), `:38` `MapControllers`.

Tablolar:

```
Appointments                              AppointmentDetails
 Id (uniqueidentifier, PK)   1 ───── 0..1  Id (PK)
 DoctorId   nvarchar(64)                   AppointmentId (FK, UNIQUE, ON DELETE CASCADE)
 PatientId  nvarchar(64)                   Complaint  nvarchar(1000) NOT NULL
 BranchId   nvarchar(64)                   Diagnosis  nvarchar(1000) NULL
 AppointmentDate datetime2                 DoctorNotes nvarchar(2000) NULL
 Status nvarchar(20) ("Pending"...)        UpdatedAt  datetime2 NULL
 CreatedAt  datetime2
 IX(DoctorId, AppointmentDate), IX(PatientId)    (hız indeksi; çakışma engeli ayrı bir filtreli tekil indeks, bkz. 8c-2)
```

Endpoint istek yolları (C = `Controllers/AppointmentsController.cs`, S = `Services/AppointmentService.cs`, D = `Services/AppointmentDetailService.cs`):

| Uç | Controller → Service | EF'in ürettiği SQL'in karşılığı |
|---|---|---|
| GET `/api/appointments` (+ `?doctorId&patientId&status&from&to`) | C:22 → S:22 (sorgu zinciri S:27-44) | `SELECT ... FROM Appointments WHERE DoctorId=@d AND PatientId=@p AND Status=N'Pending' AND AppointmentDate>=@from AND AppointmentDate<=@to ORDER BY AppointmentDate, Id` (gelmeyen filtrenin `AND`'i hiç yazılmaz) |
| GET `/{id}` | C:29 → S:47 | `SELECT TOP(1) a.*, d.* FROM Appointments a LEFT JOIN AppointmentDetails d ON a.Id=d.AppointmentId WHERE a.Id=@id` |
| POST | C:36 → S:56 (Id/Status/CreatedAt burada atanır) | `INSERT INTO Appointments (Id,DoctorId,PatientId,BranchId,AppointmentDate,Status,CreatedAt) VALUES (...)` |
| PUT `/{id}` (yeniden planlama) | C:44 → S:70 | `SELECT TOP(1) ... WHERE Id=@id` sonra `UPDATE Appointments SET BranchId=@b, AppointmentDate=@d WHERE Id=@id` |
| PATCH `/{id}/status` | C:51 → S:86 | `SELECT ...` sonra `UPDATE Appointments SET Status=N'Confirmed' WHERE Id=@id` |
| DELETE `/{id}` | C:55 → S:101-104 | `DELETE FROM Appointments WHERE Id=@id` (detayı veritabanındaki `ON DELETE CASCADE` siler) |
| GET `/{id}/detail` | C:59 → D:20 | `SELECT TOP(1) ... FROM AppointmentDetails WHERE AppointmentId=@id` |
| PUT `/{id}/detail` (upsert) | C:67 → D:27 | `SELECT 1 FROM Appointments WHERE Id=@id`, detay varsa `UPDATE AppointmentDetails ...`, yoksa `INSERT INTO AppointmentDetails ...` (yeni: 201, güncelleme: 200) |

Id rotaları `{id:guid}` kısıtlıdır; bozuk Guid controller'a girmeden 404 döner. Reddedilen iş kuralları `ToActionResult` (C:76) ile 409'a çevrilir.

Başka servisle konuşma: yok. `DoctorId`, `PatientId`, `BranchId` yalnız metin.

Bilinen sınırlar (bilerek bırakıldı): iki istek aynı anda ilk detayı eklerse tekil indeks ikincisini reddeder (409'a çevrildi, testi yok; `catch (DbUpdateException)`
geniş, SQL hata numarası 2601/2627 ile daraltılabilir); eşzamanlı durum değişikliğinde "son yazan kazanır" (`rowversion` yok); doktor çakışma engeli ilk sürümde yoktu, sonradan eklendi (8c-2).

## 8c-2. Appointment: aynı doktor, aynı saat çakışma engeli

`1829f6f`. Aynı `DoctorId` ve aynı dakikada iki **aktif** randevu verilemez. İptal edilmiş (`Cancelled`) randevu slotu işgal etmez;
`Pending`, `Confirmed`, `Completed` eder. İki katman var:

```
İstek ──► Controller ──► AppointmentService
                           1) tarihi dakikaya indir (saniye/ms = 0)                   UtcDateTimeConverter.cs:22-26
                           2) KATMAN 1: AnyAsync "bu slot boş mu?" → doluysa 409       AppointmentService.cs:82-83 (Create), :109-110 (Reschedule)
                           3) SaveChanges ──► SQL Server
                                               KATMAN 2: filtreli tekil indeks → ikinci aynı anda gelen istek reddedilir
                           4) DbUpdateException (SQL 2601/2627) → 409                  AppointmentService.cs:90-93 (Create), :118-120 (Reschedule)
```

Dosyalar: `Services/AppointmentService.cs` (`:57-72` mesaj + `IsUniqueViolation` + `IsSlotTakenAsync`, `:74-97` `CreateAsync`,
`:99-125` `RescheduleAsync`), `Data/UtcDateTimeConverter.cs:22-26` (`NormalizeToMinute`), `Data/AppointmentConfiguration.cs:33-35` (indeks),
`Controllers/AppointmentsController.cs:37-42` (`Create` artık 201 ya da 409), `Migrations/20261009183549_AddDoctorSlotUniqueIndex.*`.

Çakışma kontrolünün SQL karşılığı (`AppointmentService.cs:64-70`):

```sql
SELECT CASE WHEN EXISTS (
  SELECT 1 FROM Appointments
  WHERE DoctorId = @d AND AppointmentDate = @t AND Status <> N'Cancelled' [AND Id <> @self]
) THEN 1 ELSE 0 END
-- "AND Id <> @self" yalnız yeniden planlamada (randevunun kendi slotuna tekrar taşınması sahte çakışma vermesin)
```

Veritabanındaki indeks:

```sql
CREATE UNIQUE INDEX UX_Appointments_Doctor_Slot_Active
ON Appointments (DoctorId, AppointmentDate)
WHERE [Status] <> N'Cancelled';
```

409 verebilen uçlar: `POST /api/appointments`, `PUT /api/appointments/{id}` (yeniden planlama), `PATCH {id}/status` (yalnız geçersiz durum geçişi, eski kural).
`PATCH status` slot kuralını uygulamaz: `Cancelled` son durum olduğu için iptalden geri dönüp slotu yeniden işgal etmek mümkün değil.

Bilinen sınırlar: `[FutureDate]` doğrulaması dakikaya indirmeden ÖNCE çalışır; şimdi 10:00:40 iken 10:00:50 gönderilirse geçer, 10:00:00'a düşer (kural en çok 59 sn gevşer,
veri tutarlılığı bozulmaz). `IsUniqueViolation` yalnız doğrudan iç istisnaya bakar (EF'te bugün yeterli). Mükerrer kayıt içeren bir veritabanında migration patlar
(üretimde önce veri temizlenir).

## 8d. Identity: kayıt, giriş, kullanıcı özeti (ASP.NET Core Identity + SQL Server)

`src/Services/Identity/Hastane.Identity.Api/`, port 5001, SQL Server `hastane_identity` (anahtar `ConnectionStrings:IdentityDb`).
**JWT henüz yok** (Dalga 3): giriş şimdilik doğrulanmış kullanıcı özetini döner, token vermez.

```
Constants/AppRoles.cs               Admin, Doctor, Patient adları + All listesi
Entities/AppUser.cs                 IdentityUser + FirstName, LastName, CreatedAt   (Identity'nin hazır şifre/kilit/rol altyapısı kalır)
Data/HastaneIdentityDbContext.cs    IdentityDbContext<AppUser, IdentityRole, string>: Identity tablolarını kurar, sonra yapılandırmaları uygular
Data/AppUserConfiguration.cs        FirstName/LastName en çok 100, CreatedAt datetime2
Data/RoleSeeder.cs                  açılışta rolleri garanti eder (idempotent)
Dtos/RegisterDto.cs                 FirstName, LastName, Email, Password   (rol alanı YOK)
Dtos/LoginDto.cs, UserSummaryDto.cs, LoginResultDto.cs   (UserSummaryDto'da hash/securityStamp/telefon/kilit YOK)
Mapping/UserMappingConfig.cs        AppUser → UserSummaryDto (roller serviste eklenir)
Services/AuthService.cs             kayıt ve giriş iş kuralları
Services/UserService.cs             kullanıcı özeti okuma (tek ve çoklu)
Controllers/AuthController.cs       api/auth/register, api/auth/login
Controllers/UsersController.cs      api/users/{id}, api/users?ids=
Migrations/20261009181950_InitialCreate.*   Identity tabloları + FirstName/LastName/CreatedAt
```

`Program.cs`: `:14` Mapster, `:18` `AddDbContext` (Scoped), `:22-34` `AddIdentityCore<AppUser>` + şifre kuralları (`:24-28`: ≥8 karakter, rakam,
büyük + küçük harf), tekil e-posta (`:30`), kilit 5 deneme / 5 dk (`:32-34`), `:36-38` `AddRoles` + EF deposu + `AddSignInManager`,
`:40` `AddAuthenticationCore()` (şema kaydetmez; `SignInManager` bağımlılığı için), `:42-43` iki servis Scoped, `:52` yalnız Development'ta
`MigrateAsync`, `:56` `RoleSeeder` (her ortamda), `:58` `MapControllers`.

İstek yolları:

1. **POST `/api/auth/register`**: `AuthController.cs:16` → `AuthService.cs:26` (`CreateAsync` :43 şifreyi hash'ler, `AddToRoleAsync` :63 rolü SABİT `Patient` verir,
   özet :72) → `UserService.cs:19/27`. SQL: tekillik için `SELECT TOP(1) ... FROM AspNetUsers WHERE NormalizedUserName=@n` ve `NormalizedEmail=@e`;
   `INSERT INTO AspNetUsers (Id, Email, FirstName, LastName, CreatedAt, PasswordHash, SecurityStamp, ...)`; `SELECT FROM AspNetRoles WHERE NormalizedName='PATIENT'`;
   `INSERT INTO AspNetUserRoles (RoleId, UserId)`. Cevap: 201 + Location (`AuthController.cs:22`), e-posta kayıtlıysa 409 (`:26`), zayıf şifre 400 (`:33`).
2. **POST `/api/auth/login`**: `AuthController.cs:38` → `AuthService.cs:77` (`FindByEmailAsync` :79: `SELECT TOP(2) ... WHERE NormalizedEmail=@e`;
   `CheckPasswordSignInAsync(lockoutOnFailure: true)` :84: hash karşılaştırır, yanlışsa `UPDATE AspNetUsers SET AccessFailedCount/LockoutEnd`,
   doğruysa sayacı sıfırlar). Cevap: 200 (`:43`), kilitliyse 423 (`:44`), yanlış şifre VE olmayan e-posta için AYNI 401 gövdesi (`:47`).
3. **GET `/api/users/{id}`**: `UsersController.cs:17` → `UserService.cs:19` (Guid değilse veritabanına gitmeden null → 404) → `:27`:
   `SELECT ... FROM AspNetUsers WHERE Id IN (@id)` + `SELECT ur.UserId, r.Name FROM AspNetUserRoles ur JOIN AspNetRoles r ON ur.RoleId=r.Id WHERE ur.UserId IN (...)`.
4. **GET `/api/users?ids=a,b,c`**: `UsersController.cs:25` (Guid olmayanlar atılır, tekrarlar silinir, en çok 100, aksi 400; boşsa boş liste) → `UserService.cs:27` aynı iki sorgu.
   Patient ve Appointment servisleri ileride bu ucu çağırıp kullanıcı adlarını alacak.

Tablolar:

```
AspNetUsers (Id PK [string-guid], UserName, NormalizedUserName UNIQUE, Email, NormalizedEmail, PasswordHash, SecurityStamp,
             LockoutEnd, AccessFailedCount, FirstName, LastName, CreatedAt, ...)
   1 ─── N  AspNetUserRoles (UserId, RoleId)  PK(UserId, RoleId)     ← çoktan çoğa ara tablo
   N ─── 1  AspNetRoles (Id, Name, NormalizedName UNIQUE)             ← Admin, Doctor, Patient
AspNetUserClaims / UserLogins / UserTokens / RoleClaims: Identity'nin varsayılan tabloları (şimdilik kullanılmıyor)
```

Başka servisle konuşma: yok.

Bilinen sınırlar (bilerek bırakıldı): 423 yalnız var olan hesaplarda çıktığı için "bu e-posta kayıtlı" ipucu verir; olmayan e-postada hash hesaplanmadığı için yanıt
hafifçe hızlıdır (zamanlama farkı); rate limit ve sahte hash JWT dalgasında ele alınabilir. `UserManager` çağrılarına `CancellationToken` geçmez (API'de yok).

## 8e. Prescription: reçete ve ilaç kalemleri (SQL Server + EF Core)

`src/Services/Prescription/Hastane.Prescription.Api/`, port 5105, SQL Server `hastane_prescription` (`ConnectionStrings:DefaultConnection`). Appointment ile aynı veri katmanı düzeni;
farkı **bire-çok (1—N)** ilişki ve "ya hep ya hiç" yazma.

```
Entities/Prescription.cs          Id (Guid v7), AppointmentId (Guid, yalnız DEĞER: FK/çağrı yok), DoctorId, PatientId, IssuedAt (UTC, sunucu), Notes?
Entities/PrescriptionItem.cs      Id, PrescriptionId (FK), LineNo (sunucu atar), MedicineName, Dosage, Frequency, DurationDays (1-365), Instructions?
Data/PrescriptionDbContext.cs     iki DbSet; yapılandırmaları assembly'den yükler
Data/PrescriptionConfiguration.cs        tablo, uzunluklar, 3 indeks (PatientId, DoctorId, AppointmentId), 1—N + cascade
Data/PrescriptionItemConfiguration.cs    uzunluklar, LineNo zorunlu, (PrescriptionId, LineNo) TEKİL indeks
Data/UtcDateTimeConverter.cs      okurken Kind=Utc
Validation/NotEmptyGuidAttribute.cs      Guid.Empty → 400
Dtos/                             CreatePrescriptionDto (+ Items), CreatePrescriptionItemDto, UpdatePrescriptionDto, ResultPrescriptionDto (ItemCount),
                                  GetByIdPrescriptionDto (+ Items), PrescriptionItemDto (LineNo burada döner; Create/Update'te YOK)
Mapping/PrescriptionMappingConfig.cs     Mapster; ItemCount = s.Items.Count (SQL COUNT alt sorgusu); kalemde Id/PrescriptionId/LineNo Ignore
Services/PrescriptionService.cs   iş mantığı + EF sorguları;  Services/OperationResult.cs (Ok / NotFound / Validation)
Controllers/PrescriptionsController.cs   api/prescriptions
Migrations/20261009184736_InitialCreate.*
```

`Program.cs`: `:9` controllers, `:12-13` Mapster, `:17` `AddDbContext` (Scoped), `:20` servis, `:31` yalnız Development'ta `Migrate()`, `:34` `MapControllers`.

Tablolar:

```
Prescriptions                                 PrescriptionItems
 Id (uniqueidentifier PK)           1 ───< N   Id (PK)
 AppointmentId (Guid, FK DEĞİL)                PrescriptionId (FK, ON DELETE CASCADE)
 DoctorId  nvarchar(64)                        LineNo int NOT NULL        ← sıra, sunucu atar (1..n)
 PatientId nvarchar(64)                        MedicineName nvarchar(200)  Dosage nvarchar(100)  Frequency nvarchar(100)
 IssuedAt  datetime2                           DurationDays int            Instructions nvarchar(500) NULL
 Notes     nvarchar(2000) NULL                 TEKİL indeks: (PrescriptionId, LineNo)
 indeksler: PatientId, DoctorId, AppointmentId
```

İstek yolları (C = `Controllers/PrescriptionsController.cs`, S = `Services/PrescriptionService.cs`):

| Uç | Controller → Service | EF'in ürettiği SQL'in karşılığı |
|---|---|---|
| GET `/api/prescriptions` (+ `?patientId&doctorId&appointmentId`) | C:16 → S:23 (sorgu S:26-37) | `SELECT p.Id, p.AppointmentId, p.DoctorId, p.PatientId, p.IssuedAt, (SELECT COUNT(*) FROM PrescriptionItems i WHERE i.PrescriptionId = p.Id) AS ItemCount FROM Prescriptions p WHERE ... ORDER BY p.IssuedAt DESC, p.Id DESC` (kalem YÜKLENMEZ) |
| GET `/{id}` | C:22 → S:41 (`Include` + `OrderBy(LineNo)` S:45-46) | `SELECT TOP(1) ... FROM Prescriptions p LEFT JOIN PrescriptionItems i ON i.PrescriptionId = p.Id WHERE p.Id=@id ORDER BY p.Id, i.LineNo` (tek sorgu) |
| POST | C:29 → S:51 (kalem sayısı kontrolü S:55, `Number` S:60, `Add` S:64) | tek transaction: `INSERT Prescriptions`, her kalem için `INSERT PrescriptionItems (..., LineNo, ...)` |
| PUT `/{id}` | C:37 → S:69 (kontrol S:72, `RemoveRange` S:80, `Number` S:82, `AddRange` S:84) | `SELECT ... JOIN` sonra tek transaction: `UPDATE Prescriptions SET Notes`, `DELETE PrescriptionItems WHERE Id IN (eskiler)`, yeni kalemler için `INSERT` |
| DELETE `/{id}` | C:50 → S:115 (`ExecuteDelete` S:118) | `DELETE FROM Prescriptions WHERE Id=@id` (kalemleri `ON DELETE CASCADE` siler) |

`Number()` (`S:105-112`): her kaleme `Guid.CreateVersion7()` Id, `PrescriptionId` ve gönderilen sırayla `LineNo = 1..n` verir. Update'te eski kalemler silinip yenileri eklenirken aynı
(PrescriptionId, LineNo) çiftleri yeniden kullanılır: EF silmeyi eklemeden önce çalıştırdığı için tekil indeks çakışmaz (testle doğrulandı).

Doğrulama hataları (kalem sayısı 0 ya da 21+, `DurationDays` 0 ya da 366, boş `MedicineName`, `Guid.Empty` Id): DTO katmanında `[ApiController]` 400 verir; kalem sayısı **servis katmanında da** kontrol edilir
(`S:55`, `S:72`) ve `OperationResult.Validation` olarak döner, controller 400'e çevirir (iki katmanlı savunma).

Başka servisle konuşma: yok. Reçeteyi yazan doktorun o randevunun doktoru olup olmadığı, ilacın var olup olmadığı, hasta doğrulaması kapsam dışı (ileride servisler arası sorgu).

Bilinen sınırlar: güncellemede son yazan kazanır (`rowversion` yok); "kalemleri tamamen değiştir" yaklaşımı değişmeyen kalemleri de silip yeniden ekler ve kalem Id'lerini değiştirir
(ileride eczane teslim kaydı kalem Id'sine bağlanırsa kırılır).

## 8g. Güvenlik: JWT, ortak kütüphane, Identity genişlemesi (Dalga 3A)

`8050d93`. Sözleşme: `JWT-TASARIM.md`. Bu bölüm 3A'yı (çekirdek) anlatır; 3B (altı servise uygulama) ayrı bölümde.

**`src/Shared/Hastane.Shared/`** (bütün servislerin ortak bağı, tek izinli `ProjectReference`):

```
Hastane.Shared.csproj        web çerçevesi + JwtBearer paketi; jwt-public.pem derlemenin İÇİNE gömülü (EmbeddedResource)
jwt-public.pem               AÇIK anahtar (sır değil, depoda). Özel anahtar yalnız Identity'de (user-secrets)
Security/HastaneRoles.cs     Admin, Doctor, Patient sabitleri
Security/HastaneClaims.cs    sub, email, name, role, doctor_id, jti sabitleri
Security/HastanePolicies.cs  ilkeler: AdminOnly, DoctorOnly, PatientOnly, DoctorOrAdmin, AuthenticatedUser (AddHastaneAuthorization, :16-25)
Security/JwtOptions.cs       Issuer, Audience, AccessTokenMinutes, PrivateKeyPem (yalnız Identity), PublicKeyPem
Security/JwtKeys.cs          PEM → RsaSecurityKey, kid = açık anahtarın SHA-256'sının ilk 16 baytı
Security/JwtAuthenticationExtensions.cs   AddHastaneJwtAuthentication (aşağıda)
Security/CurrentUser.cs      ICurrentUser: UserId, Email, Roles, DoctorId, IsInRole  (HttpContext.User'dan)
Security/ProblemResponses.cs 401 ve 403 için ProblemDetails gövdesi (iç hata metni sızmaz)
```

**`AddHastaneJwtAuthentication`** (`JwtAuthenticationExtensions.cs`), satır satır:
`:13` `Jwt` bölümünü `JwtOptions`'a bağlar · `:15` "Bearer" şemasını ekler · `:19-20` ayarlar ilk kullanımda okunur (test ezmeleri çalışır) · `:23-25` açık anahtar `Jwt:PublicKeyPem` doluysa o, yoksa gömülü dosya ·
`:28` `MapInboundClaims=false` (kısa claim adları olduğu gibi kalır) · `:33-34` imza anahtarı + `RequireSignedTokens` · `:36` **yalnız RS256** (`alg: none` ve HS256 imzaya bakılmadan düşer) · `:38-41` issuer ve audience zorunlu ·
`:43-45` süre kontrolü, `exp` zorunlu, **ClockSkew 30 sn** · `:47-48` `RoleClaimType="role"`, `NameClaimType="name"` · `:53-58` OnChallenge → 401 ProblemDetails + `WWW-Authenticate: Bearer` · `:59` OnForbidden → 403.

**Token üretimi (Identity)**: login doğrulanır → kullanıcı ve roller veritabanından TAZE okunur → `JwtTokenService.CreateAccessToken` (`:32-50`) claim'leri kurar (sub, email, name, çoklu `role`, `doctor_id` yalnız bağlı Doctor'da, jti, iat/nbf/exp, iss, aud) →
`JsonWebTokenHandler` + `RsaSecurityKey(özel)` + `kid` → `{ accessToken, tokenType: "Bearer", expiresAt, user }`. Diğer servisler yalnız açık anahtarla doğrular; Identity'ye bağlanmaz (Identity çökse bile doğrulama sürer).

**Identity yeni uçlar** (`src/Services/Identity/Hastane.Identity.Api/`):

| Uç | Politika | Controller → Service | SQL karşılığı |
|---|---|---|---|
| POST `auth/login` | anonim | `AuthController.cs:51` → `AuthService.cs:76` → `JwtTokenService.cs:32` | `SELECT ... FROM AspNetUsers WHERE NormalizedEmail=@e`; hash doğrula (yanlışsa `UPDATE ... AccessFailedCount`); `SELECT roller JOIN`; token imzası (veritabanı yok) |
| GET `auth/me` | giriş yapmış | `AuthController.cs:67` → `UserService.cs:19` | iki SELECT (kullanıcı + roller) |
| GET `roles` / POST `roles` | Admin | `RolesController.cs:19/23` → `RoleService.cs:27/31` | `SELECT Name FROM AspNetRoles` / `INSERT INTO AspNetRoles` |
| POST `users/{id}/roles` | Admin | `UsersController.cs:60` → `RoleService.cs:43` | kullanıcı, rol, `IsInRole` SELECT'leri; `INSERT INTO AspNetUserRoles` |
| DELETE `users/{id}/roles/{role}` | Admin | `UsersController.cs:65` → `RoleService.cs:61` | `BEGIN TRAN` **SERIALIZABLE**; Admin ise `SELECT COUNT(*) FROM AspNetUserRoles WHERE RoleId=@admin` (1 ise 409); `DELETE`; Doctor rolüyse `UPDATE AspNetUsers SET DoctorId=NULL`; `COMMIT` |
| PUT `users/{id}/doctor-link` | Admin | `UsersController.cs:70` → `RoleService.cs:100` | Doctor rolü kontrolü; `SELECT ... WHERE DoctorId=@d AND Id<>@id`; `UPDATE AspNetUsers SET DoctorId=@d` (filtreli tekil indeks çakışırsa 409) |
| GET `users/{id}` | kendisi / Doctor / Admin (diğeri 404) | `UsersController.cs:28` → `UserService.cs:19` | iki SELECT |
| GET `users?ids=` | Doctor, Admin | `UsersController.cs:42` → `UserService.cs:27` | `WHERE Id IN (...)` |
| Açılış | | `AdminSeeder.cs:19-56` | `GetUsersInRole("Admin")` yoksa `INSERT AspNetUsers` + `INSERT AspNetUserRoles` |

`Program.cs` (Identity): `:44` `AddHastaneJwtAuthentication`, `:45` `AddHastaneCurrentUser`, `:48` özel anahtar doğrulayıcısı, `:50` `JwtTokenService` (Singleton), `:53` `IRoleService`, `:59` açılışta anahtar hatası veritabanından ÖNCE yakalanır, `:70/72` `RoleSeeder` / `AdminSeeder`, `:75-76` `UseAuthentication` → `UseAuthorization`.
Şema: `AspNetUsers` + `DoctorId nvarchar(64) NULL` (filtreli tekil indeks `WHERE DoctorId IS NOT NULL`); migration `20261009200017_AddDoctorLinkAndRoleManagement`.

**`tests/Hastane.TestSupport`** (yalnız test projeleri başvurur): `TestKeys` (süreç başına RSA çifti), `TestJwt.Create(...)` + saldırı üreticileri (`CreateExpired`, `CreateWithBrokenSignature`, `CreateAlgNone`, `CreateHs256WithPublicKeyAsSecret`), `WithBearer`, `UseTestJwt()` (doğrulama anahtarını test açık anahtarıyla değiştirir), `UseTestJwtSigningKey()` (yalnız Identity),
`AuthorizationCoverage.FindEndpointsWithoutAuthorizationMetadata` (yetki etiketi unutulmuş uçları bulur; hiç uç taranmadıysa kendisi hata verir). `tests/Hastane.Shared.Tests`: güvenlik hattı testleri (politika matrisi, çoklu rol, alg confusion, issuer/audience).

Bilinen sınırlar (bilerek): token iptali / çıkış yok (rolü alınan kullanıcının eski token'ı en çok 30 dk geçerli kalır); refresh token yok; anahtar rotasyonu / JWKS yok; rate limit yok; `kid` eşleşmezliği için özel test yok; özel anahtar `new-jwt-keys.ps1` içinde komut satırı argümanıyla `user-secrets set`'e geçer (süreç listesinde kısa süre görünür; stdin daha iyi olurdu); eşzamanlı son-Admin ve doctor-link yarışı için test yok (işlem SERIALIZABLE; deadlock olursa 500 dönebilir ama Admin'siz kalınmaz).

## 8h. Yetkilendirme: altı servise koruma (Dalga 3B)

Kural: her action kendi `[Authorize]` / `[AllowAnonymous]` etiketini taşır (sınıf düzeyindeki `[Authorize]` action'daki kuralla VE'lenir, tuzak; bu yüzden tek tek). `AuthorizationCoverageTests` etiketi unutulan ucu bulur.
İki katman: **rol** controller'da (etiket), **sahiplik** serviste (`ICurrentUser`); başkasının kaydı 404 döner (varlığı sızdırmaz), rol hiç uygun değilse 403.

**Branch** (`BranchesController.cs`, sınıfta AdminOnly): GET liste `:21` ve GET `{id}` `:26` anonim; POST `:33`, PUT `:40`, DELETE `:47` yalnız Admin.

**Doctor** (`DoctorsController.cs`): GET liste `:28`, `search` `:38`, `batch` `:47` anonim; GET `{id}` `:56` giriş yapmış herkes; POST `:64` Admin; PUT `:78` Doctor veya Admin; DELETE `:89` Admin.

**Review** (`ReviewsController.cs`): GET liste/`{id}`/`doctors/{id}/summary`/`summaries` anonim; POST `:69` yalnız Patient; PUT `:81` ve DELETE `:89` Patient veya Admin. `ReviewerId` istekten değil token'dan atanır.

**Appointment** (`AppointmentsController.cs`): GET liste `:29`, GET `{id}` `:37`, PUT `:63`, PATCH `status` `:71`, GET `detail` `:81` Patient/Doctor/Admin (sahiplik `AppointmentAccess.cs:11-22`: hasta kendi `PatientId`'si, doktor kendi `DoctorId`'si); POST `:45` Patient veya Admin; DELETE `:76` Admin; PUT `detail` `:90` Doctor veya Admin. Durum geçişini kimin yapabileceği `AppointmentAccess.CanSetStatus`.

**Prescription** (`PrescriptionsController.cs`): GET liste `:22` ve GET `{id}` `:29` Patient/Doctor/Admin (hasta kendi reçetesini, doktor yazdığını görür: `PrescriptionService.cs:29-34`); POST `:37`, PUT `:54`, DELETE `:69` Doctor veya Admin; doktor hesabı doktor kaydına bağlı değilse 403 (`LacksDoctorIdentity`).

**Payment** (`PaymentsController.cs`, `InvoicesController.cs`): GET liste/`{id}` Patient/Doctor/Admin; POST `:40` Patient veya Admin; `POST {id}/refund` `:54` yalnız Admin. Fatura servisi `ICurrentUser` alır (sahiplik süzmesi ayrıca doğrulanmadı).

Testler: her serviste `<Servis>AuthorizationTests` (rol x uç tablosu). Son koşu sayıları: Branch 35, Doctor 133, Review 53, Appointment 118, Prescription 56, Payment 154, Identity 90, Shared 26.
Kapandı (2026-10-10): Identity testlerinde nadir, sıraya bağlı 2 hata (401 / kid uyuşmazlığı) görülüyordu; kök neden kanıtlanamadı. `TestKeys` değiştirildi (PEM ve kid tip başlatıcısında bir kez üretilir, her çağrı taze RSA kurar), düzeltmeden sonra Identity 8 koşuda 90/90, diğer 7 proje temiz.

## 8i. Gateway: tek kapı (Ocelot, port 5000) (Dalga 4A)

`src/Gateway/Hastane.Gateway`: `Program.cs` ocelot.json'u yükler (`AddOcelot`), `/health` Gateway'in kendisine aittir (Ocelot `UseWhen` ile /health dışındaki yollara bağlı). Rotalar `ocelot.json`'da, hepsi `{everything}` ile biter, 5 metod açık, `localhost` host:

| İstemci yolu | Servis | Aşağı akış yolu |
|---|---|---|
| `/gateway/auth/*`, `/users/*`, `/roles/*` | Identity 5001 | `/api/auth/*`, `/api/users/*`, `/api/roles/*` |
| `/gateway/branches/*` | Branch 5101 | `/api/branches/*` |
| `/gateway/doctors/*` | Doctor 5102 | `/api/doctors/*` |
| `/gateway/reviews/*` | Review 5103 | `/api/reviews/*` |
| `/gateway/appointments/*` | Appointment 5104 | `/api/appointments/*` |
| `/gateway/prescriptions/*` | Prescription 5105 | `/api/prescriptions/*` |
| `/gateway/payments/*`, `/invoices/*` | Payment 5111 | `/api/payments/*`, `/api/invoices/*` |

Karar: Gateway token DOĞRULAMAZ, `Authorization` başlığını aynen iletir; 401/403 servisten aynen döner. Doğrulamanın merkezileşmesi sonraki dalga. Kapalı servis 502 verir. Docker'da host adı ortam değişkeniyle ezilir (`Routes__0__DownstreamHostAndPorts__0__Host`). CORS yok (arayüz MVC, sunucudan çağıracak). Testler: `tests/Hastane.Gateway.Tests` (53; sahte aşağı akış, rota tablosu ocelot.json ile karşılaştırılır).
Duman testi: Gateway ve Branch çalıştı, `GET /gateway/branches` ile `GET :5101/api/branches` aynı yanıtı verdi (ilk denemede ikisi de 500 verdi: Branch Development dışında açılmıştı, user-secrets yüklenmedi, Mongo'ya şifresiz gitti. Normal `dotnet run` ile Branch 200. Gateway üzerinden uçtan uca 200 henüz denenmedi).

## 8j. WebUI: MVC arayüz (port 5010) (Dalga 5)

Kurallar ve yeni ekran ekleme deseni: `src/WebUI/Hastane.WebUI/UI-KURALLARI.md`.

**Bir sayfanın istek yolu (örnek: hasta randevusunu iptal eder):**
1. Tarayıcı `POST /Patient/Appointments/Cancel/{id}` (formda antiforgery belirteci).
2. `Areas/Patient/Controllers/AppointmentsController.cs` `Cancel` (önce antiforgery + `Patient` rol kapısı).
3. `Clients/PatientAppointmentClient.cs` yolu kurar: `/gateway/appointments/{id}/status`.
4. `Infrastructure/Gateway/GatewayClient.cs` gönderir; `BearerTokenHandler.cs` çerezdeki JWT'yi `Authorization: Bearer` olarak ekler.
5. Gateway (5000) → Appointment (5104) `/api/appointments/{id}/status`; sahiplik ve durum makinesi orada.
6. Dönüş: 409 → `ApiResult.Kind=Conflict` → sayfada uyarı; 401 → `GatewayExceptionFilter` çıkış + girişe yönlendirme.

**Klasörler:**
- `Infrastructure/`: `Gateway/` (istemci, `ApiResult`, hata filtresi, `ModelState.AddApiProblem`), `Security/` (`AccessTokenValidator`), `ServiceCollectionExtensions` (`Clients/` altındaki `XClient`'lar otomatik kaydolur).
- `Controllers/`: herkese açık + hesap: `Home`, `Account` (giriş/kayıt/çıkış), `Error`, `Branches`, `Doctors` (sabit rotalı: `/Doctors`, `/Doctors/{id}`).
- `Areas/Patient`: Randevular (al, iptal), Reçeteler, Ödemeler (Idempotency-Key), Faturalar.
- `Areas/Doctor`: Randevular (durum, muayene notu), Reçeteler (yaz/düzenle/sil), Profil (id token'dan), Yorumlar.
- `Areas/Admin`: Branş, Doktor (iç içe form), Kullanıcı/rol, doctor-link, Randevular, Ödemeler (iade), Faturalar, Reçeteler.

**Kimlik:** giriş → Identity JWT → WebUI kendi açık anahtarıyla doğrular (`AccessTokenValidator.cs`) → şifreli HttpOnly çerez (`.Hastane.Auth`, ömür = token, kayan süre yok); token tarayıcıya verilmez.
**Tuzak (düzeltildi):** herkese açık `DoctorsController` sabit rotalı olduğu için Admin içinde alansız üretilen `Index` adresleri `/Doctors`'a kayıyordu → Admin doktor sayfalarında `area = "Admin"` açık yazılır (test: `List_links_stay_in_admin_area_not_public_doctors_page`).
**Testler:** `tests/Hastane.WebUI.Tests` 252 (sahte gateway, Docker yok).
**Duman testi (2026-10-10):** 9 süreç gerçek veritabanlarıyla: kayıt → giriş → ana sayfa, Branşlar, Doktorlar, hasta paneli sayfaları 200; hasta için /Admin ve /Doctor 403; servis loglarında hata yok. Veri boş olduğu için sayfalar "boş durum" gösterdi; Admin ve Doktor panelleri gerçek servislerle denenmedi (admin hesabı gerekiyor).

## 8k. Gece dalgası (2026-10-10): boş saat, hesap işlemleri, tasarım, örnek veri

**Appointment (5104):** `GET /api/appointments/availability?doctorId=&date=` (giriş gerekli) → Doctor `GET /api/doctors/{id}` (token iletilir) programını `Services/SlotCalculator.cs` slotlara böler, o günün iptal olmayan randevuları ve geçmiş saatler çıkar. POST ve PUT aynı hesapla "slot başlangıcı mı" kontrol eder (`CheckScheduleAsync`; değilse 400, Doctor kapalıysa 503). Saat dilimi `Scheduling:TimeZone` (varsayılan Europe/Istanbul): program yerel, veritabanı UTC. `Note` kolonu (≤500, migration `AddAppointmentNote`). Kod: `Integration/` (DoctorScheduleLookup, DownstreamClient).
**Identity (5001):** `PUT api/auth/me`, `POST api/auth/change-password` (login ile aynı kilit sayacı; 5. hatada 423), `GET api/users?page&pageSize&search&role` (Admin, sayfalı; `ids=` verilirse eski davranış), `DELETE api/users/{id}/doctor-link` (idempotent 204).
**Doctor (5102):** `GET api/doctors/{id}/public` anonim, ayrı `PublicDoctorDto` (telefon/e-posta yok). Ücret filtresi ve `feeAsc/feeDesc` artık `HourlyFee > 0` (aggregation `SearchByFeeAsync`; ücretsizler sona).
**Review (5103):** yanıtta `isMine` (token'daki sub == ReviewerId), `ReviewerId` yanıta konmaz; geçersiz token anonim gibi işlenir.
**WebUI:** tasarım sistemi (`site.css`, `Views/Shared/_Avatar|_DoctorCard|_PanelSide|_PrintLayout|_SlotPicker`, panel layout'ları), saat seçici (`ViewModels/Shared/SlotPickerViewModel.cs` → `AppointmentTime`: tüm saat gösterimi İstanbul), `/Account/Profile`, `/Account/ChangePassword`, onay/yazdırma/takvim/Hastalarım/yeniden planlama ekranları; çoklu branş filtresi `Controllers/DoctorsController.cs` Index başında `Request.Query["branchId"]` birleştirilir. Görseller `wwwroot/img/`.
**Örnek veri:** `tools/Hastane.DevSeed` (Gateway üzerinden, idempotent).
**Test düzeni:** `tests/Hastane.Identity.Tests/xunit.runner.json` sınıfları sırayla koşar (her sınıf bir SQL konteyneri; paralelde Docker belleği taşıyordu).

## 8l. Ek arayüz dalgası (2026-10-10 sabah)

- Doktor: `Areas/Doctor/Controllers/{Schedule,Social,Invoices}Controller.cs`, `PatientsController` `Details` (öznitelikli `/Doctor/Patients/{patientId}`). Program ve sosyal bağlantı kaydı: kaydı yeniden oku → yalnız ilgili alanı değiştir → tam `PUT doctors/{id}` (id token'dan).
- Admin: `HomeController` grafik verisi (`ViewModels/Admin/AdminDashboardData.cs` toplama; `<script type="application/json">` + `wwwroot/lib/chartjs`), `ReviewsController`, `PatientsController`.
- Genel: `HomeController` `About/Contact/Faq`; `Views/Error/Index` 404 tasarımı.
- **Tuzak (düzeltildi):** iç içe düzen (`_AdminLayout` → `_Layout`) sayfanın `@section Scripts`'ini iletmezse sayfa 500 verir; üç panel düzeni `RenderSectionAsync("Styles"/"Scripts")` ile iletir.

## 8m. Dalga X (2026-10-10): Patient, Notification, Chat, Identity eklemeleri

| Servis | Port / veri | Ne yapar | Kim çağırır |
|---|---|---|---|
| Patient | 5106, SQL `hastane_patient` | `GET/PUT /api/patients/me` (doğum tarihi, yaş hesaplanır, kan grubu, adres, acil kişi), favoriler `PUT/DELETE /api/patients/me/favorites/{doctorId}`, `GET /api/patients/{userId}` (Doctor yalnız randevulu hasta: Appointment'a sorar) | WebUI |
| Notification | 5112, Mongo `hastane_notification` + MailHog SMTP 1025 | İç uçlar `/api/internal/notifications[/email]` yalnız `X-Internal-Key` ile (sabit zamanlı karşılaştırma, ayar boşsa hep 401); kullanıcı uçları `/api/notifications/me`, `{id}/read`, `read-all` (alıcı = `sub` ya da `doctor_id`) | Identity (e-posta), Appointment (bildirim) |
| Chat | 5114, Mongo `hastane_chat` | Hasta-doktor tek konuşma (tekil indeks), mesajlar, okunmamış sayaç (`$inc`), konuşma açmak için randevu ilişkisi Appointment'tan; Admin okuyamaz | WebUI (5 sn sorgulama) |
| Identity | 5001 | `forgot-password` (her zaman 202, arka plan kuyruğu, e-posta başına hız sınırı), `reset-password` (aynı 400), `register-doctor` + Admin `doctor-applications` onay/red (koşullu UPDATE), `me/photo` (magic bytes, 2 MB, sunucu dosya adı) + `GET users/{id}/photo` | WebUI |

- Appointment oluştur / durum / yeniden planla sonrası `Integration/NotificationClient.cs` karşı tarafa bildirim yollar (3 sn, hata yutulur, randevu sonucu değişmez).
- Gateway 14 rota; `/api/internal/...` dışarı açılmaz (canlı denendi: `%2e%2e` ve `..` hileleri 404).
- WebUI: `/Patient/Profile`, `/Patient/Favorites` (kalp `_FavoriteButton`), `/Notifications` + zil (`ViewComponents/NavBadgeComponents.cs`), `/Messages` (sorgulama JS `Views/Messages/Index.cshtml`), `/Account/ForgotPassword|ResetPassword|RegisterDoctor|DoctorApplication`, `/Admin/DoctorApplications`, fotoğraf `/Account/Profile` + vekil `/media/users/{id}/photo` (`Controllers/MediaController.cs`).
- **Sapma:** mesajlar SignalR yerine sorgulamayla, çünkü token tarayıcıya verilmiyor. Kurs B16 SignalR'ı sıra ekranı için ayrıca işlenecek.
- **Tuzak (düzeltildi):** Razor `Mesajlar@await ...` yazımını e-posta adresi sanıp kodu düz metin basıyordu; `<span>Mesajlar</span>@await ...` (test: sayfada `@await` yok).
- Yerel sırlar: Patient/Notification/Chat bağlantıları ve `Internal:ApiKey` (Identity, Appointment, Notification aynı) user-secrets'ta.
  (Sonradan: Appointment bildirimleri RabbitMQ olayına geçti, iç anahtar artık Identity, Notification, Market, Order'da; bkz. 8s.)

---

> **Dalga Y ve Z (2026-10-10):** aşağıdaki bölümler kursun B16–B21 bölümleri ve Dalga B eklemeleridir. Satır numaraları
> entegrasyon commit'i `3929d43`'e aittir. Bugünkü tam tablo: 15 servis + Gateway + WebUI (17 uygulama, `scripts/run-all.ps1`).
> Gateway'de 22 rota (`ocelot.json`): eskilere ek olarak `/gateway/queue`, `/medicines`, `/pharmacy`, `/products`, `/cart`,
> `/orders`, `/analytics` ve WebSocket rotası `/gateway/hubs/queue` → `ws://localhost:5107/hubs/queue`.

## 8n. Queue: bekleme salonu sırası + SignalR (B16, port 5107)

`src/Services/Queue/Hastane.Queue.Api/`, SQL Server `hastane_queue`, tek tablo `QueueEntries`.

```
Controllers/QueueController.cs     api/queue: check-in, me/today, doctor/today, doctor/call-next, {id}/call|complete|no-show, display
Hubs/QueueHub.cs                   QueueHub (anonim, yalnız JoinDoctor / JoinAll), QueuePublic (herkese açık biçim TEK yerde), QueueNotifier (yayın)
Services/QueueService.cs           iş kuralları: check-in, günlük numara, çağırma, durum geçişleri
Services/DisplayNameMasker.cs      "Ahmet Can Yılmaz" -> "Ahmet Y." (tam ad hiç saklanmaz)
Integration/AppointmentLookup.cs   check-in'de randevuyu Appointment'tan doğrular (kullanıcının token'ı iletilir)
Messaging/QueueMessaging.cs, AppointmentStatusChangedConsumer.cs   RabbitMQ tüketicisi (randevu iptali -> sıra kaydı iptal)
Entities/QueueEntry.cs, QueueStatus.cs (Waiting, Called, Completed, NoShow, Cancelled)
Data/QueueEntryConfiguration.cs    tekil indeksler (aşağıda)
```

`Program.cs`: `:17-19` JSON enum metin; `:27-28` DbContext; `:45-50` Appointment HttpClient (5 sn); `:57` `AddQueueMessaging`;
`:60-66` `AddSignalR` + **hub JSON'unda da enum metin** (`AddJsonProtocol`; yoksa hub `status: 1` yazar, REST `"Called"` yazar,
WebUI JS ikisini karşılaştıramaz); `:67` `IQueueNotifier` Singleton; `:77-82` açılışta migration; `:88` `MapHub<QueueHub>("/hubs/queue").AllowAnonymous()`.

Tablo ve indeksler (`QueueEntryConfiguration.cs`):

```sql
CREATE UNIQUE INDEX UX_QueueEntries_Appointment       ON QueueEntries(AppointmentId);                 -- randevu başına tek kayıt
CREATE UNIQUE INDEX UX_QueueEntries_Doctor_Day_Number ON QueueEntries(DoctorId, QueueDate, Number);   -- günlük numara çakışmaz
CREATE UNIQUE INDEX UX_QueueEntries_Doctor_Day_OneCalled ON QueueEntries(DoctorId, QueueDate) WHERE [Status] = N'Called';  -- doktorda aynı anda tek çağrılan
```

| Uç | Yetki | Ne yapar |
|---|---|---|
| `POST /api/queue/check-in` `{appointmentId}` | Patient | Sıraya gir (201 yeni, 200 zaten vardı) |
| `GET /api/queue/me/today` | Patient | Bugünkü kayıtlarım + `AheadCount` (önümde kaç kişi) |
| `GET /api/queue/doctor/today[?doctorId=]` | Doctor / Admin | Doktor kendi sırasını görür (sorgudaki doctorId yok sayılır), Admin doctorId verir |
| `POST /api/queue/doctor/call-next` | Doctor | Sıradaki bekleyeni çağır |
| `POST /api/queue/{id}/call` · `complete` · `no-show` | Doctor / Admin | Durum geçişi |
| `GET /api/queue/display?doctorId=` | **Anonim** | Çağrı ekranı: şu an çağrılan + sıradaki 5 (yalnız numara, maskeli ad, durum) |

**Check-in istek yolu** (`QueueController.cs:15-24` → `QueueService.CheckInAsync` `:36`):
1. `:42` aynı randevu zaten sıradaysa mevcut kaydı döndür (idempotent).
2. `:46` Appointment `GET /api/appointments/{id}` (token iletilir; başkasının randevusu orada 404, burada da 404). Kapalıysa 503.
3. `:52` randevu `Confirmed` değilse 409; `:56-57` randevu günü İstanbul'a göre bugün değilse 409.
4. `:59` ad token'daki `name` claim'inden maskelenir (istemci ad gönderemez).
5. `:61-96` numara: `SELECT MAX(Number) ... WHERE DoctorId AND QueueDate` + 1 → `INSERT`. Tekil indeks çakışırsa kısa rastgele
   bekleme, yeni MAX+1 (en çok 15 deneme). Aynı randevunun eşzamanlı ikinci isteği kazananın kaydını alır.
6. `:98` `QueueNotifier.PublishAsync` → hub grupları `doctor:{id}` ve `all`'a `QueueUpdated` yayını.

**Çağırma** (`CallNextAsync` `:140`): durum geçişleri koşullu `ExecuteUpdate` ile (`:222-226`, ör. `UPDATE ... SET Status='Called' WHERE Id AND Status='Waiting'`),
iki eşzamanlı çağrıdan birini "tek Called" filtreli indeksi durdurur (409). Başarıda `:238` yayın.

**Canlı ekran akışı:** tarayıcı `/Queue/Display?doctorId=` (WebUI `Controllers/QueueController.cs`, girişsiz, `Cache-Control: no-store`) →
sayfa `wwwroot/js/queue-display.js` ile **Gateway'deki** `/gateway/hubs/queue`'ya SignalR bağlantısı açar (`JoinDoctor`) →
`QueueUpdated` gelince WebUI'nin `/Queue/DisplayData` ucundan güncel listeyi çeker; ayrıca 15 sn'de bir yedek yenileme.
Sunucudan gelen her metin `textContent` ile yazılır (innerHTML yok). Hasta tarafı: `Areas/Patient/Controllers/QueueController.cs`
("Sıram", sıraya gir), doktor tarafı: `Areas/Doctor/Controllers/QueueController.cs` (Sıra; çağır / tamamla / gelmedi), canlı güncelleme `wwwroot/js/queue-live.js`.

**Kim çağırır:** WebUI (Gateway üzerinden); Queue → Appointment (HTTP, check-in); RabbitMQ → Queue (`AppointmentStatusChanged`, bkz. 8s).

## 8o. Pharmacy: ilaç kataloğu, stok defteri, reçete karşılama, CQRS + MediatR (B17, port 5108)

`src/Services/Pharmacy/Hastane.Pharmacy.Api/`, SQL Server `hastane_pharmacy`. Diğer servislerden farkı: iş mantığı
`Services/` altında değil, her işlem için ayrı bir **komut/sorgu + handler** sınıfında (`Features/`).

```
Controllers/PharmacyApiController.cs   taban: yalnız "isteği komuta çevir, ISender.Send, sonucu HTTP koduna çevir"
Controllers/MedicinesController.cs     api/medicines (katalog + stok)
Controllers/PharmacyController.cs      api/pharmacy (karşılama)
Features/Medicines/Commands/{CreateMedicine,UpdateMedicine,DeleteMedicine,AdjustStock}/   XCommand + XHandler + XValidator
Features/Medicines/Queries/{GetMedicines,GetMedicineById,GetLowStock,GetMovements}/        XQuery + XHandler (+ XValidator)
Features/Medicines/StockLedger.cs      stok değiştirmenin TEK yolu (koşullu UPDATE + hareket kaydı)
Features/Dispensing/DispensePlanner.cs önizleme ve karşılamanın ortak planı (salt okur)
Features/Dispensing/Commands/DispensePrescription/, Queries/{PreviewDispense,GetDispensations,GetDispensationByPrescription}/
Behaviors/LoggingBehavior.cs, ValidationBehavior.cs, ValidationExceptionHandler.cs   MediatR pipeline halkaları + 400'e çeviri
Validation/IValidator.cs, Rules.cs     basit doğrulayıcı arayüzü (FluentValidation yok, bilerek) + derlemeden otomatik kayıt
Common/TurkishText.cs                  "PARASETAMOL" == "parasetamol", "AĞRI" == "agri" (kültürden bağımsız katlama)
Integration/PrescriptionLookup.cs      karşılamada reçeteyi Prescription servisinden okur (token iletilir)
Entities/Medicine.cs, StockMovement.cs, Dispensation.cs (+ DispensationLine), MedicineForm.cs
```

`Program.cs`: `:30-35` `AddMediatR` (handler'lar derlemeden taranır) + `AddOpenBehavior(LoggingBehavior)` + `AddOpenBehavior(ValidationBehavior)`
(**ilk kayıt en dışta**: istek → Logging → Validation → Handler); `:36` doğrulayıcıları tara; `:39-40` `ValidationExceptionHandler` (400);
`:43-44` `StockLedger`, `DispensePlanner`; `:57-62` Prescription HttpClient; `:67` `UseExceptionHandler`; `:73-77` migration.

| Uç | Yetki | Komut / sorgu |
|---|---|---|
| `GET /api/medicines?search&form&...` | Giriş yapmış | `GetMedicinesQuery` |
| `GET /api/medicines/{id}` | Giriş yapmış | `GetMedicineByIdQuery` |
| `GET /api/medicines/low-stock` | Admin | `GetLowStockQuery` |
| `POST` / `PUT {id}` / `DELETE {id}` `/api/medicines` | Admin | `Create/Update/DeleteMedicineCommand` |
| `POST /api/medicines/{id}/stock` `{delta, reason}` | Admin | `AdjustStockCommand` (+ giriş, - çıkış) |
| `GET /api/medicines/{id}/movements` | Admin | `GetMovementsQuery` (stok defteri) |
| `GET /api/pharmacy/dispense/preview/{prescriptionId}` | Admin | `PreviewDispenseQuery` (stoğa dokunmaz) |
| `POST /api/pharmacy/dispense` `{prescriptionId}` | Admin | `DispensePrescriptionCommand` |
| `GET /api/pharmacy/dispensations`, `.../by-prescription/{id}` | Admin | karşılama kayıtları |

Eczacı rolü yok: yazma ve karşılama uçları Admin.

**Stok düzeltme yolu** (`MedicinesController.cs:60-63` → `ISender.Send` → LoggingBehavior → ValidationBehavior (`AdjustStockValidator`) → `AdjustStockHandler.cs:11`):
transaction aç → `StockLedger.ApplyAsync` (`StockLedger.cs:28`):

```sql
UPDATE Medicines SET Stock = Stock + @delta, UpdatedAt = @now
WHERE Id = @id AND IsActive = 1 AND Stock + @delta >= 0;      -- 0 satır: yok (404) ya da yetersiz (409)
-- sonra INSERT StockMovements (Delta, StockAfter, Reason, CreatedBy) ; COMMIT
```
Ayrıca veritabanında `CHECK (Stock >= 0)` kısıtı var (`MedicineConfiguration.cs:14`): kod yanlış yazılsa bile eksi stok yazılamaz.

**Reçete karşılama yolu** (`PharmacyController.cs:18-26` → `DispensePrescriptionHandler.cs:19`):
1. `:22` bu reçete zaten karşılanmış mı (ucuz kontrol, Prescription'a gitmeden) → 409.
2. `:25` `DispensePlanner.PlanAsync`: Prescription `GET /api/prescriptions/{id}` (kapalıysa 503), her kalemin ilaç adı katlanır
   (`TurkishText.Fold`) ve katalogda `NormalizedName` ile eşleşir; durum: Matched / NotFound / Ambiguous / InsufficientStock. Her kalem 1 ambalaj.
3. `:29-34` tek kalem bile uygun değilse **hiçbir şey düşülmez**, 409 + uygunsuz kalemler listesi.
4. `:39` transaction; `:49` ilaçlar **Id sırasıyla** kilitlenir (iki karşılama ters sırayla kilit isteyip kilitlenmesin); her biri için koşullu UPDATE;
   arada stok değiştiyse `:56` geri al + 409.
5. `:62-81` `Dispensation` + satırları (fiyat anlık görüntüsü), `:85` `SaveChanges`; aynı reçete eşzamanlı ikinci kez gelirse
   `UX_Dispensations_PrescriptionId` tekil indeksi durdurur, `:91` geri al. `:95` COMMIT → 201.

**Kim çağırır:** WebUI Admin (`Areas/Admin/Controllers/PharmacyMedicinesController.cs`, `PharmacyDispensingController.cs`); Pharmacy → Prescription (HTTP).

## 8p. Market: ürünler, Redis önbelleği ve sepet, stok rezervasyonu (B18 + B20, port 5109)

`src/Services/Market/Hastane.Market.Api/`, SQL Server `hastane_market` (ürün, stok, rezervasyon: **doğruluk kaynağı**) + Redis (önbellek + sepet).

```
Controllers/ProductsController.cs        api/products (liste/tek ürün anonim; yazma Admin)
Controllers/CartController.cs            api/cart (Patient; anahtar token'dan)
Controllers/InternalStockController.cs   api/internal/stock/reserve|release (yalnız X-Internal-Key; Gateway'de YOK)
Caching/RedisGate.cs                     Redis'e tek kapı; bağlı değilse Database = null (servis Redis'siz de açılır)
Caching/ProductCache.cs                  cache-aside: product:{id}, products:list:{özet}, products:list-index (silmek için küme)
Caching/RedisCartStore.cs                sepet: cart:{userId} HASH (alan = productId, değer = adet), 7 gün TTL
Services/ProductService.cs, CartService.cs, StockService.cs
Messaging/MarketMessaging.cs, OrderCancelledConsumer.cs   RabbitMQ: sipariş iptali -> stok iadesi
Data/DemoDataSeeder.cs                   Development'ta (Market__SeedDemoData=true) tablo boşsa 10 örnek ürün
Entities/Product.cs, ProductCategory.cs, StockReservation.cs (+ StockReservationItem)
Security/InternalApiKeyAttribute.cs
```

`Program.cs`: `:18` enum metin, sayı kabul edilmez; `:32-33` DbContext; `:36-38` `RedisGate`, `ProductCache`, `RedisCartStore` Singleton (tek bağlantı);
`:40-42` servisler; `:45` `AddMarketMessaging`; `:57-64` migration + demo ürünler.

**Ürün okuma (cache-aside)** `GET /api/products/{id}` → `ProductService.GetByIdAsync` (`:62`): `:66` Redis `GET product:{id}`; varsa döner.
Yoksa SQL'den oku, `:75` Redis'e `SET product:{id} <json> EX 300` (Admin değilse; Admin pasif ürünleri de görür, onun cevabı önbelleğe girmez).
Liste de aynı (`:26-58`, anahtar sorgu parametrelerinin SHA-256 özeti). Yazmada (`:91`, `:111`, `:130`) ve stok değişince
`ProductCache.InvalidateAsync`: ürün anahtarı + **tüm** liste anahtarları silinir (liste stok ve ad taşır). Redis hata verirse önbellek
sessizce atlanır, cevap SQL'den gelir.

**Sepet** (`RedisCartStore.cs`): `PUT /api/cart/items/{productId}` `{quantity}` → `CartService.SetItemAsync` (`:38`; `:51` stoktan fazlası 409) →
Redis işlemi (MULTI/EXEC): `HSET cart:{userId} {productId} {adet}` (0 ise `HDEL`) + `EXPIRE cart:{userId} 604800` (her yazmada 7 gün yeniden başlar).
Sepette fiyat/ad saklanmaz; `GET /api/cart` okurken ürün kaydından doldurur. Redis kapalıysa sepet uçları **503**, ürün uçları çalışır.

**Stok rezervasyonu** `POST /api/internal/stock/reserve` `{orderId, items}` (`InternalStockController.cs:17-38` → `StockService.ReserveAsync` `:34`):
1. `:42-46` aynı ürün satırları toplanır, **ürün Id sırasına** dizilir (deadlock önlemi).
2. `:48` bu orderId daha önce işlendiyse 200 `replayed: true` (stok tekrar düşmez).
3. `:51-76` transaction; önce `INSERT StockReservations` (`OrderId` tekil: eşzamanlı aynı orderId ikinci istek burada durur, kazananın sonucunu alır).
4. `:80-90` her kalem: `UPDATE Products SET Stock = Stock - @q WHERE Id = @id AND IsActive = 1 AND Stock >= @q`; 0 satır → tümü geri alınır,
   409 `{productId, reason: InsufficientStock|ProductUnavailable|ProductNotFound, requested, available}`.
5. `:92-93` COMMIT + önbellek temizliği.

**İade** (`ReleaseAsync` `:97`): `UPDATE StockReservations SET Status='Released' WHERE OrderId AND Status='Reserved'` (sahiplenme; 1 satır
etkileyen TEK çağrı iade eder) → kalemler için `UPDATE Products SET Stock = Stock + @q`. Rezervasyon yoksa ya da zaten iade edildiyse 200, stok değişmez.
Bugün iade yalnız RabbitMQ olayıyla tetiklenir (`OrderCancelledConsumer`); HTTP `release` ucu duruyor.

**Kim çağırır:** WebUI (`/Shop`, `/Shop/{id}`, `/Cart`, sepet rozeti `ViewComponents/CartBadgeViewComponent.cs`, Admin `ProductsController`);
Order → Market (sepet, ürün, reserve; bkz. 8q); RabbitMQ → Market (`OrderCancelled`).

## 8q. Order: sipariş ve checkout, MariaDB (B19 + B20, port 5110)

`src/Services/Order/Hastane.Order.Api/`, **MariaDB** `hastane_order` (Docker `mariadb:11.4`, bilgisayarda port **3307**).
EF sağlayıcısı **Oracle `MySql.EntityFrameworkCore` 10.0.9** (`UseMySQL`, büyük harf); Pomelo'nun EF Core 10 kararlı sürümü yok.

```
Controllers/OrdersController.cs         api/orders: checkout, liste, tek, status (Admin), cancel
Services/OrderService.cs                checkout akışı, iptal, durum
Services/OrderStatusTransitions.cs      Received -> Preparing -> Shipped -> Delivered; Received/Preparing -> Cancelled
Services/OrderNumberGenerator.cs        SIP-2026-000123 (yıl başına sayaç satırı)
Services/IdempotencyKeyRules.cs
Integration/MarketClient.cs             Market'e typed HttpClient: sepet + ürün (kullanıcı token'ı), reserve (X-Internal-Key), sepeti boşalt
Messaging/OrderMessaging.cs             MassTransit + EF outbox (MySQL kilidi), OrderPlaced / OrderCancelled
Data/MariaDbHistoryRepository.cs        migration kilidi dekoratörü (aşağıda)
Data/OrderDbContext.cs, OrderConfiguration.cs, OrderItemConfiguration.cs, OrderCounterConfiguration.cs, OrderDbContextFactory.cs (dotnet ef için)
Entities/Order.cs, OrderItem.cs, OrderCounter.cs
Migrations/ InitialCreate, AddMassTransitOutbox, EnsureUtf8mb4
```

`Program.cs`: `:28-31` `UseMySQL(Utf8(...))` + `ReplaceService<IHistoryRepository, MariaDbHistoryRepository>()`; `:45-50` Market HttpClient;
`:55` `AddOrderMessaging`; `:65-71` migration; `:83-90` `Utf8`: bağlantı cümlesinde karakter kümesi yoksa `utf8mb4` zorlanır
(yoksa "Ateş" sessizce "Ates" olabiliyordu).

**MariaDB tuzakları (düzeltildi):**
- **Migration kilidi:** sağlayıcı açılıştaki `Migrate()` sırasında `GET_LOCK` sonucunu `Int64`'e çeviriyor; MariaDB 11.4'te sonuç NULL gelince
  `InvalidCastException` ile servis açılmıyordu. Sağlayıcının `MySQLHistoryRepository` sınıfı `internal` olduğu için alt sınıf yazılamadı:
  `Data/MariaDbHistoryRepository.cs` her şeyi içteki nesneye devreden bir **dekoratör**, yalnız kilidi NULL-güvenli yazıyor.
- **Karakter kümesi:** `docker-compose.yml` MariaDB'yi `--character-set-server=utf8mb4` ile açar; `20261010150000_EnsureUtf8mb4` migration'ı
  mevcut tabloları (outbox tabloları dahil) utf8mb4'e çevirir.

Tablolar ve indeksler (`OrderConfiguration.cs`):

```sql
CREATE UNIQUE INDEX UX_Orders_OrderNumber              ON Orders(OrderNumber);
CREATE UNIQUE INDEX UX_Orders_Patient_IdempotencyKey   ON Orders(PatientUserId, IdempotencyKey);   -- aynı hasta + aynı anahtar = tek sipariş
-- Orders.Version: iyimser eşzamanlılık belirteci (iki yönetici aynı anda durum değiştirirse biri 409)
-- OrderCounters(Year PK, LastValue): sipariş numarası sayacı
```

**Checkout akışı** `POST /api/orders/checkout` (başlık `Idempotency-Key` isteğe bağlı, gövde `{shippingAddress, note}`; Patient):
`OrdersController.cs:25-38` → `OrderService.CheckoutAsync` (`:40`)

1. `:46-50` anahtar verildiyse ve bu hasta + anahtarla sipariş varsa **aynı sipariş** döner (yeni sipariş, yeni stok düşümü yok).
2. `:53` Market `GET /api/cart` (hastanın token'ı iletilir). Market kapalı → 503, hiçbir şey yazılmaz. Sepet boşsa `:64-68` anahtarı bir daha ara
   (çift tıklamanın yavaş ikinci isteği: birincisi sepeti boşaltmış olabilir), yoksa 400 "Sepetiniz boş".
3. `:75-95` her ürün için Market `GET /api/products/{id}`: **fiyat ve aktiflik sunucudan**; sepetteki fiyat hiç kullanılmaz. Satışta değilse 409,
   farklı para birimi 409. Tutarlar 2 haneye `AwayFromZero` yuvarlanır.
4. `:114` `InsertWithNumberAsync` (`:287`): `INSERT IGNORE INTO OrderCounters` (işlem dışında) → transaction → `UPDATE OrderCounters SET LastValue = LastValue + 1`
   → `SELECT LastValue` → `INSERT Orders (Status='Received', OrderNumber='SIP-2026-000123', PaymentMethod=kapıda ödeme)` → COMMIT.
   Aynı anahtarla yarışan ikinci istek tekil indekste durur (`:116-123`), birincinin siparişini alır.
5. `:127` Market `POST /api/internal/stock/reserve` `{orderId, items}` (`X-Internal-Key`; `CancellationToken.None`: sipariş artık kayıtlı, yarım kalmasın).
   - Stok yetmedi (409) → `:134` sipariş **Cancelled** ("Stok yetersiz"), olay yok (Market hiçbir şey düşmedi) → istemciye 409.
   - Market yanıt vermedi → `:141` sipariş Cancelled + **`OrderCancelled` olayı** (cevap kaybolmuş, stok düşmüş olabilir; Market tüketicisi
     idempotent iade eder) → 503.
6. `:149-158` `OrderPlaced` olayı outbox'a yazılır (Notification hastaya "Siparişiniz alındı" der). Yazılamazsa sipariş yine geçerli, loglanır.
7. `:166` Market `DELETE /api/cart`; başarısızsa sipariş yine başarılı (loglanır).
8. 201 + sipariş (numara, kalemler, toplam).

**İptal** `POST /api/orders/{id}/cancel` → `CancelAsync` (`:229`): hasta yalnız kendi siparişini ve yalnız `Received` iken, Admin `Received`/`Preparing` iken.
`MarkCancelledAsync` (`:266`): durum + `OrderCancelled` olayı **tek `SaveChanges`** (outbox) → Market stoğu iade eder, Notification bildirim yazar.
**Durum** `PATCH /api/orders/{id}/status` (Admin) → `OrderStatusTransitions.IsAllowed`; geçersiz geçiş 409.
**Liste/tek** `GET /api/orders`, `/{id}`: hasta yalnız kendininkini görür (başkasınınki 404), Admin hepsini.

**Kim çağırır:** WebUI (`/Cart` → checkout, `Areas/Patient/Controllers/OrdersController.cs` Siparişlerim, Admin `OrdersController.cs`);
Order → Market (HTTP); Order → RabbitMQ (outbox).

## 8r. Elasticsearch: doktor araması (B21, Doctor 5102 içinde)

Ayrı servis değil: Doctor servisine eklenmiş bir **arama dizini**. Kaynak gerçek MongoDB; Elasticsearch ondan türetilmiş, silinse yeniden kurulabilen bir kopya.
Ayar `Elasticsearch:Url` (boşsa kapalı), dizin adı `hastane-doctors`. Paket `Elastic.Clients.Elasticsearch` 8.19.27, sunucu 8.15 (`extra` profili, port 9200).

```
Search/IDoctorSearchIndex.cs            sözleşme: Search, Suggest, Upsert, Delete, BulkUpsert, DeleteStale, Count
Search/ElasticDoctorSearchIndex.cs      gerçek uygulama: dizin tanımı (analyzer), belge, sorgu, devre kesici
Search/NullDoctorSearchIndex.cs         Url boşken: hiçbir şey yapmaz, arama Mongo'ya düşer
Search/SearchServiceCollectionExtensions.cs   hangisinin kaydedileceği ÇÖZÜMLEME anında seçilir + açılışta dizini kur (best-effort)
Services/DoctorService.cs               SearchWithEngineAsync (:105), RebuildSearchIndexAsync (:391)
```

**Dizin tanımı** (`ElasticDoctorSearchIndex.cs:114-161`): `tr_fold` analyzer = apostrof + Türkçe küçük harf (I→ı, İ→i) + asciifolding
("Yilmaz" = "Yılmaz", "cocuk" = "Çocuk"); `tr_text` = tr_fold + Türkçe etkisiz sözcükler + Türkçe kök bulucu (yalnız biyografide).
`fullName` ve `services` `search_as_you_type` (yazdıkça önek eşleşmesi). Konumlar `nested`: şehir + ücret + gün aynı konumda sağlanmalı
(Mongo'daki `$elemMatch` ile aynı anlam).

**Arama istek yolu** `GET /api/doctors/search?text=kardiyolji&...` (anonim): `DoctorsController.cs:41-55` → `DoctorService.SearchWithEngineAsync` (`:105`):
1. Doğrulama ve filtre her iki yolda da aynı (`BuildSearchFilter`).
2. `:109-111` metin doluysa ve dizin açıksa `ElasticDoctorSearchIndex.SearchAsync` (`:299`): `multi_match` + `fuzziness: AUTO` (`prefix_length: 1`,
   "kardiyolji" → "Kardiyoloji") ve `bool_prefix` (son sözcük önek: "yilm" → "Yılmaz") birlikte.
3. `:115` Elasticsearch yalnız **kimlikleri ve sırayı** verir; belgeler Mongo'dan tek `$in` sorgusuyla, aynı sırada (`FetchInOrderAsync` `:147`).
4. Elasticsearch kapalı / hata / zaman aşımı / 10.000'den derin sayfa → `null` → `:126` sonrası eski Mongo araması (regex). Hata sonrası
   devre kesici bir süre Elasticsearch'ü hiç denemez (her istek zaman aşımını beklemesin).
5. `DoctorsController.cs:48` yanıt başlığı **`X-Search-Engine: elasticsearch | mongo`** (gövde değişmez; teşhis için).

**Öneri** `GET /api/doctors/suggest?text=` (anonim): `bool_prefix` ile yazdıkça aday doktorlar (`SuggestAsync` `:421`).
**Yazma:** doktor ekle/güncelle/sil → önce Mongo, sonra `UpsertAsync` / `DeleteAsync` best-effort (`DoctorService.cs:66-78`); dizin hatası asıl işlemi bozmaz.
**Yeniden indeksle** `POST /api/doctors/search-index/rebuild` (Admin): dizin **silinmez** (silinse dolana dek aramalar boş dönerdi); Mongo'dan 200'lük
gruplarla `BulkUpsertAsync`, sonra bu çalıştırmadan eski `indexedAt`'li yetim belgeler silinir. `GET .../search-index/status`: ulaşılabilir mi, kaç belge.
**Kim çağırır:** WebUI `/Doctors` arama, Admin **Arama Dizini** ekranı (`Areas/Admin/Controllers/SearchIndexController.cs`).

## 8s. RabbitMQ + MassTransit: olaylar ve outbox (Dalga B)

Ayrıntılı tablo: [`docs/OLAY-MIMARISI-RABBITMQ.md`](./docs/OLAY-MIMARISI-RABBITMQ.md). MassTransit 8.5.11 (Apache-2.0).

```
src/Shared/Hastane.Contracts/Events.cs    olay sözleşmeleri (record, geçmiş zaman adı, EventId, kişisel veri yok, durumlar metin)
Appointment/.../Messaging/AppointmentMessaging.cs   yayıncı + EF outbox (SQL Server); Migrations/*_AddMassTransitOutbox
Order/.../Messaging/OrderMessaging.cs               yayıncı + EF outbox (MySQL kilit SQL'i); Migrations/*_AddMassTransitOutbox
Notification/.../Messaging/Consumers.cs, EventNotificationFactory.cs, NotificationMessaging.cs   5 tüketici
Queue/.../Messaging/AppointmentStatusChangedConsumer.cs
Market/.../Messaging/OrderCancelledConsumer.cs
```

| Olay | Yayınlayan (nerede) | Tüketen |
|---|---|---|
| `AppointmentCreated` | Appointment `AppointmentService.cs:183` | Notification |
| `AppointmentRescheduled` | Appointment `AppointmentService.cs:222` | Notification |
| `AppointmentStatusChanged` | Appointment `AppointmentService.cs:258` | Notification, Queue (`NewStatus=Cancelled`) |
| `OrderPlaced` | Order `OrderService.cs:149` | Notification |
| `OrderCancelled` | Order `OrderService.cs:275` | Notification, Market (stok iadesi) |

**Bir olayın yolu (örnek: hasta randevusunu iptal eder):**
1. Appointment durum değişikliğini kaydederken `IPublishEndpoint.Publish(AppointmentStatusChanged)` çağırır. Bus outbox sayesinde mesaj RabbitMQ'ya
   gitmez, `OutboxMessage` tablosuna **randevu güncellemesiyle aynı `SaveChanges`'te** yazılır.
2. MassTransit'in arka plan teslim servisi outbox'taki satırı RabbitMQ'ya gönderir (RabbitMQ kapalıysa bekler, sonra gönderir).
3. RabbitMQ exchange'i (`Hastane.Contracts:AppointmentStatusChanged`) mesajı bağlı kuyruklara kopyalar: `notification-appointment-status-changed`,
   `queue-appointment-status-changed` (kuyruk adları servis öneki + kebab-case).
4. Notification tüketicisi bildirim yazar; `SourceKey = "{EventId}:{alıcı}"` filtreli tekil indeksi aynı olayın ikinci gelişini atlar.
5. Queue tüketicisi `UPDATE QueueEntries SET Status='Cancelled' WHERE AppointmentId AND Status='Waiting'` (ikinci kez 0 satır) + SignalR yayını.
6. Tüketici hata verirse 1 sn, 5 sn, 15 sn sonra yeniden denenir; yine olmazsa mesaj `<kuyruk>_error` kuyruğuna düşer (RabbitMQ arayüzü 15672).

`RabbitMq:Host` boşsa her servis **bellek içi taşıma** kullanır (testler ve RabbitMQ'suz geliştirme): olaylar yalnız aynı süreç içinde kalır.
**Bilinçli senkron kalan:** checkout'taki stok rezervasyonu (kullanıcı cevabı bekliyor, "stok yetersiz" anında söylenmeli).

## 8t. Kafka: doktor etkinlik akışı + Analytics servisi (Dalga B, port 5115)

Confluent.Kafka 2.16.0. Konu `hastane.doctor-activity`, Kafka `extra` profilinde (port 9092). İki taraf kod paylaşmaz, **JSON** paylaşır.

**Üretici (Doctor 5102):**
```
Activity/DoctorActivityEvent.cs              olay: eventId, type (search | profile_view), occurredAt, doctorId, text, filters, resultCount, engine; kişisel veri yok
Activity/DoctorActivityPublisher.cs          sınırlı bellek kanalı (Channel, 10.000): istek yalnız TryWrite yapar, beklemez, dolunca olay düşer
Activity/DoctorActivityProducerService.cs    BackgroundService: kanaldan okur, Kafka'ya yazar (EnableIdempotence, Acks=All, anahtar = doctorId)
```
Arama sonrası `DoctorsController.cs:50`, profil görüntüleme `:104` / `:115` → `TryPublish`. `Kafka:BootstrapServers` boşsa akış kapalı.

**Tüketici (Analytics 5115, MongoDB `hastane_analytics`):**
```
Kafka/ActivityConsumerService.cs   BackgroundService: grup "analytics", elle commit, Mongo hatasında Seek ile aynı olaya dön
Kafka/ReplayAndState.cs            ReplayCoordinator (HTTP isteği ile tüketici döngüsü arasındaki köprü), KafkaConnectionState
Services/ActivityProcessing.cs     ActivityProcessor (idempotent işleme), TermNormalizer (Türkçe katlama, e-posta/uzun sayı atlanır), AnalyticsDay (İstanbul günü)
Services/AnalyticsQueryService.cs  okuma uçlarının sorguları
Controllers/AnalyticsController.cs api/analytics (hepsi Admin)
```
Koleksiyonlar: `doctor_views_daily`, `search_terms_daily`, `processed_events` (işlenmiş eventId kuponları, TTL 8 gün).

**Bir olayın yolu:** `ActivityConsumerService.cs:106` `Consume` → `:130` JSON çöz (bozuksa atla) → `ActivityProcessor.ProcessAsync` (`ActivityProcessing.cs:121`):
`:145` `processed_events`'e `_id = eventId` ekle (yinelenen anahtar = zaten işlendi, atla) → `:161`/`:171` günlük toplamı `$inc` (upsert) →
`ActivityConsumerService.cs:113` **işledikten sonra** `Commit`. Commit'ten önce çökerse olay yeniden gelir, kupon yakalar.

| Uç | Ne |
|---|---|
| `GET /api/analytics/doctors/top-viewed` | En çok görüntülenen doktorlar |
| `GET /api/analytics/search/top-terms` | En çok aranan terimler |
| `GET /api/analytics/status` | Kafka açık mı, bağlı mı |
| `POST /api/analytics/rebuild` | **Yeniden oynatma:** toplamları sil, her partition'ı en eski offset'e sar (`RewindAsync` `:157`), olaylar baştan okunur |

**Kim çağırır:** WebUI Admin **Analitik** ekranı (`Areas/Admin/Controllers/AnalyticsController.cs`, Gateway `/gateway/analytics`).

## 8u. Docker: tüm sistemi konteynerde çalıştırma (Dalga Z)

```
Dockerfile                        TEK, parametrik (PROJECT_PATH, PROJECT_NAME); çok aşamalı (sdk:10.0 derler, aspnet:10.0 çalıştırır); USER app; port 8080
.dockerignore                     bin/obj, testler, sırlar, depo geçmişi bağlama girmez
docker-compose.full.yml           docker-compose.yml'in ÜSTÜNE biner: 15 servis + gateway + webui (+ devseed, caddy profilleri)
src/Gateway/Hastane.Gateway/ocelot.Docker.json   konteyner adresli rotalar (identity:8080 ...); scripts/sync-ocelot-docker.ps1 üretir
src/Shared/Hastane.Shared/Hosting/DatabaseStartupExtensions.cs   ShouldMigrateOnStartup: Development ya da Database:MigrateOnStartup=true
deploy/Caddyfile                  {$DOMAIN}: /gateway/hubs/* -> gateway:8080 (SignalR), geri kalanı -> webui:8080; otomatik HTTPS
scripts/docker-up.ps1             .env + INTERNAL_API_KEY + secrets/ (JWT çifti) üretir, compose up --build --wait; -Seed, -Extra, -Proxy
docs/YAYINLAMA.md                 VPS adımları, yedek, güncelleme
```

**Compose kuralları** (`docker-compose.full.yml` başı): sır yok, hepsi `.env`'den; servisler birbirine konteyner adıyla gider (`http://identity:8080`;
konteyner içinde `localhost` konteynerin kendisidir); servis portları dışarı açılmaz, yalnız Gateway (5000) ve WebUI (5010) `BIND_ADDRESS`'e
(varsayılan 127.0.0.1) yayımlanır; altyapı portları da 127.0.0.1'e daraltılır (Docker yayımlanan portu ufw'yi atlayarak açar).
Ortak ortam (`x-app-env`): `ASPNETCORE_ENVIRONMENT=Docker`, `Database__MigrateOnStartup=true`, `Jwt__PublicKeyPemFile=/run/secrets/jwt_public_key`,
`Internal__ApiKey`, `Kafka__BootstrapServers`. Sağlık denetimi `bash /dev/tcp` ile 8080 portu (aspnet imajında curl yok).
Profiller: `extra` (Elasticsearch, Kafka, Analytics), `proxy` (Caddy), `seed` (DevSeed konteyneri).

**Neden tek Dockerfile:** 17 uygulamanın derleme adımı aynı; yalnız proje yolu değişiyor. Önce yalnız `*.props` + `.csproj` kopyalanıp `restore`
edilir (katman önbelleği: kod değişince paketler yeniden inmez), sonra kaynak kopyalanıp `publish`. Son imajda SDK yok (~1 GB kazanç).

## 9. Yazılan kodlar kaydı (yeni servis geldikçe satır eklenir)

| Bölüm | Servis | Port | Veritabanı | Commit | Notlar |
|---|---|---|---|---|---|
| B1 | Branch | 5101 | MongoDB `hastane_branch` | `58b6e6d`, `54d3697` | CRUD, Mapster, tek MongoClient |
| B2 | Doctor | 5102 | MongoDB `hastane_doctor` | `dee28ac` | iç içe belgeler, liste/detay DTO, `?branchId=` |
| B3 | Review | 5103 | MongoDB `hastane_review` | `ede5fc2` | sunucu atamalı alan, doğrulama, aggregation özeti, yuvarlama |
| B4 | Appointment | 5104 | SQL Server `hastane_appointment` | `078532b` | EF Core, 1—0..1 ilişki, durum makinesi, filtreli sorgu, Guid v7 |
| B4+ | Appointment çakışma engeli | 5104 | SQL Server | `1829f6f` | check-then-act yarışı, filtreli tekil indeks, iki katmanlı savunma, dakika normalizasyonu |
| B5 | Identity | 5001 | SQL Server `hastane_identity` | `994adf1` | ASP.NET Core Identity, şifre hash, mass assignment, kilit, rol tohumlama |
| B6 | Prescription | 5105 | SQL Server `hastane_prescription` | `4c4d655` | 1—N ilişki, transaction (ya hep ya hiç), N+1 / Include, LineNo, iki katmanlı doğrulama |
| B2.5 | Payment | 5111 | SQL Server `hastane_payment` | `67ce20f` | idempotency, telafi, iade işareti, kart verisi saklanmaz, SEQUENCE, KDV dahil ayrıştırma |
| B8-9 (3A) | Güvenlik çekirdeği | (Shared, Identity 5001) | SQL Server `hastane_identity` | `8050d93` | RS256 JWT, politikalar, rol yönetimi, ilk Admin, alg confusion testleri |
| B8-9 (3B) | Yetkilendirme (6 servis) | Branch, Doctor, Review, Appointment, Prescription, Payment | | `81ce393`, `59877e3` (birleşme `6a8d8b8`) | uç başına açık etiket, sahiplik serviste, 404/403 ayrımı, AuthorizationCoverage |
| B10 (4A) | Gateway | 5000 | — | merge `Dalga 4A` | Ocelot, 10 rota, Authorization iletilir, /health |
| B7, B11-B14 (5) | WebUI | 5010 | — | `f791cec` | MVC, çerez + JWT, gateway istemcisi, 3 panel (hasta, doktor, admin), 252 test |
| B16 (Y) | Queue | 5107 | SQL Server `hastane_queue` | `4827c30` | SignalR hub (anonim, yalnız dinleme), günlük numara, filtreli tekil indeks, maskeli ad |
| B17 (Y) | Pharmacy | 5108 | SQL Server `hastane_pharmacy` | `96eb64a` | CQRS + MediatR 12.5, pipeline behavior, stok defteri, hepsi-ya-hiç karşılama |
| B18, B20 (Y) | Market | 5109 | SQL Server `hastane_market` + Redis | `9c1b72c` | cache-aside, sepet HASH + TTL, atomik koşullu stok UPDATE, idempotent orderId |
| B19, B20 (Y) | Order | 5110 | MariaDB `hastane_order` | `19d3141` | checkout, fiyat sunucudan, idempotency key, sipariş numarası sayacı, migration kilidi dekoratörü |
| B21 (Y) | Elasticsearch (Doctor içinde) | 5102 | ES dizini `hastane-doctors` | `c05bbe6` | Türkçe analyzer, fuzziness, bool_prefix, Mongo'ya yedek düşüş, yeniden indeksleme |
| Dalga B (Z) | Kafka + Analytics | 5115 | MongoDB `hastane_analytics` | `377da09` | bounded Channel üretici, elle commit, eventId ile idempotent, yeniden oynatma |
| Dalga B (Z) | RabbitMQ olayları | Appointment, Order, Notification, Queue, Market | outbox tabloları | `1900d7d` (birleşme `51a53b9`) | MassTransit 8.5.11, transactional outbox, en az bir kez, idempotent tüketici |
| Dalga Z | Docker | — | — | `bc58573` | tek parametrik Dockerfile, compose.full, ocelot.Docker.json, Caddy, docker-up.ps1 |
