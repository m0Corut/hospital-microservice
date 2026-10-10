---
title: Hocanın Doccure deposu ile karşılaştırma
created: 2026-10-10
modified: 2026-10-10
type: note
status: active
tags: [hastane-mikroservis, kurs, karsilastirma]
---
# Hocanın Doccure deposu ile karşılaştırma

Kaynak: [github.com/MuratYucedag/Doccure](https://github.com/MuratYucedag/Doccure), son commit `2f04a49` (2026-06-15).
Yöntem: depo yalnız okundu (kod çalıştırılmadı); paketler, varlık sınıfları, controller uçları ve anahtar kelimeler sayıldı.
Hocanın deposu kurs ilerledikçe güncelleniyor olabilir; bu tablo o commit'in anlık görüntüsü.

**Güncelleme (2026-10-10, Dalga Y ve Z sonrası):** Queue, Pharmacy, Market ve Order artık bizde de var; RabbitMQ, Kafka,
MediatR/CQRS ve Elasticsearch bizde var, hocada yok. Hocanın deposu yeniden okunmadı, hoca sütunu hâlâ `2f04a49` anlık görüntüsü.

## Rakamlar

Satır, dosya, uç ve test sayıları ilk karşılaştırma gününe (Dalga X sonrası) aittir; Dalga Y/Z sonrası yeniden sayılmadı.
Servis ve Gateway rotası satırları güncel.

| | Hoca | Biz |
|---|---|---|
| .NET sürümü | .NET 6 | .NET 10 |
| Servis | 11 + Gateway + WebUI | 15 + Gateway + WebUI |
| Uygulama kodu (C#) | 211 dosya, ~4.900 satır | 382 dosya, ~18.800 satır (Dalga X) |
| Arayüz görünümü (.cshtml) | 33 | 107 (Dalga X) |
| Test | **0** | 114 dosya, ~24.900 satır, **1.916 test** (Dalga X) |
| API uç noktası (servislerde) | ~50 | ~81 (Dalga X) |
| Gateway rotası | 4 (yalnız branş ve doktor) | 22 (tüm servisler + SignalR WebSocket) |
| `[Authorize]` kullanımı | 3 uç (Branch, Doctor) | her uçta açık etiket + yetki tablosu testleri |

## Veritabanları

| Servis | Hoca | Biz |
|---|---|---|
| Branch, Doctor, Review | MongoDB | MongoDB |
| Appointment, Identity, Patient | SQL Server | SQL Server |
| Prescription | **PostgreSQL** | SQL Server |
| Pharmacy, Queue | SQL Server | SQL Server |
| Market (ürün) | SQL Server + **Redis** | SQL Server + Redis (ürün önbelleği + sepet) |
| Order | **MySQL/MariaDB** | MariaDB |
| Doktor araması | — (yok) | Elasticsearch (MongoDB'ye yedek düşüş) |
| Payment, Notification, Chat, Analytics | — (yok) | SQL Server / MongoDB |

## Hocada olup bizde olmayanlar

- **PostgreSQL** (hocada reçete servisi): bizde reçete SQL Server'da. Veritabanı çeşitliliği dışında kazancı yok; şimdilik eklenmedi.
- **PatientVisit** (hasta ziyaret kaydı) varlığı ve Admin **Nurse (hemşire)** ekranı (hocada yalnız arayüzde).
- **Swagger** her serviste (bizde .NET 10'un yerleşik OpenAPI'si var, Swagger arayüzü yok).

Eskiden bu listede olan Queue + SignalR (B16), Pharmacy (B17), Market + Redis (B18, B20) ve Order + MariaDB (B19)
2026-10-10'da yazıldı (Dalga Y).

## Bizde olup hocada olmayanlar

- **Testler** (hocada hiç yok): birim + gerçek veritabanı konteynerleriyle entegrasyon testleri, yetki tablosu testleri, saldırı testleri.
- **Güvenlik**: JWT RS256 (hocada HS256 ortak anahtar), rol + sahiplik bazlı yetki her uçta (hocada 3 uç), token tarayıcıya verilmiyor (hocada `Session`'da tutulup her istekte `HttpClient.DefaultRequestHeaders`'a yazılıyor), antiforgery, hesap kilidi, kullanıcı numaralandırma koruması.
- **Yeni servisler**: Payment (idempotency, iade, tutar doğrulama), Notification (bildirim + e-posta), Chat (mesajlaşma), Analytics (Kafka tüketicisi).
- **Olay tabanlı iletişim**: RabbitMQ + MassTransit (transactional outbox, idempotent tüketiciler) ve Kafka (etkinlik akışı, yeniden oynatma). Hocada servisler arası iletişim tamamen senkron HTTP.
- **CQRS + MediatR**: Pharmacy servisinde komut/sorgu ayrımı ve pipeline behavior'lar.
- **Elasticsearch**: doktor araması gerçekten Elasticsearch ile (Türkçe analiz, yazım hatası toleransı); kursta yalnız teori (B21), hocanın deposunda yok.
- **Sıra ve sipariş derinliği**: anonim ama yalnız dinleme SignalR hub'ı ve maskeli ad, atomik stok rezervasyonu, idempotent checkout, fiyatın sunucudan yeniden okunması.
- **Randevu**: boş saat hesabı, program dışı saat ve çift randevu engeli, saat dilimi. (Hocada `DoctorSchedule` varlığı var, hesaplama mantığı görünmüyor.)
- **Kimlik**: şifremi unuttum, doktor başvurusu + onay, profil fotoğrafı, rol yönetimi, doktor hesabını doktor kaydına bağlama.
- **Arayüz**: hasta paneli ve doktor paneli (hocada arayüz büyük ölçüde Admin alanı), favoriler, bildirim zili, takvim, yazdırma, grafikli yönetim paneli.
- **Gateway** tüm servisleri kapsıyor; arayüz servislere yalnız Gateway'den gidiyor (hocada bazı çağrılar doğrudan servis portlarına).
- **Kurulum ve yayın**: tek docker-compose, tek komutla kurulum ve başlatma betikleri, örnek veri aracı, her servis için Dockerfile ve tek komutla Docker yayını (Caddy ile HTTPS).

## İkisinde de olmayanlar

İlk karşılaştırmada bu listede olan her kalem (RabbitMQ, Kafka, MediatR, CQRS, olay tabanlı mimari, Elasticsearch) artık
bizde var. Yeni bir "ikisinde de yok" listesi için hocanın deposu yeniden taranmadı. Hocadaki PostgreSQL ile
PatientVisit/Nurse bizde hâlâ yok (yukarıda).

## Sonuç ve öneri

Kapsam farkı kapandı: kursun B16–B21 bölümleri (Queue/SignalR, Pharmacy, Market/Redis, Order/MariaDB, Elasticsearch) bizde de
yazıldı. Biz ayrıca **derinlikte** (test, güvenlik, iş kuralları, arayüz) ve **mimaride** (RabbitMQ, Kafka, CQRS, Docker
yayını) öndeyiz. Hocada olup bizde olmayan küçük kalemler: PostgreSQL, PatientVisit, Nurse ekranı, Swagger arayüzü.
