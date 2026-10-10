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

## Rakamlar

| | Hoca | Biz |
|---|---|---|
| .NET sürümü | .NET 6 | .NET 10 |
| Servis | 11 + Gateway + WebUI | 10 + Gateway + WebUI |
| Uygulama kodu (C#) | 211 dosya, ~4.900 satır | 382 dosya, ~18.800 satır |
| Arayüz görünümü (.cshtml) | 33 | 107 |
| Test | **0** | 114 dosya, ~24.900 satır, **1.916 test** |
| API uç noktası (servislerde) | ~50 | ~81 |
| Gateway rotası | 4 (yalnız branş ve doktor) | 14 (tüm servisler) |
| `[Authorize]` kullanımı | 3 uç (Branch, Doctor) | her uçta açık etiket (83) + yetki tablosu testleri |

## Veritabanları

| Servis | Hoca | Biz |
|---|---|---|
| Branch, Doctor, Review | MongoDB | MongoDB |
| Appointment, Identity, Patient | SQL Server | SQL Server |
| Prescription | **PostgreSQL** | SQL Server |
| Pharmacy, Queue | SQL Server | — (yok) |
| Market (ürün) | SQL Server + **Redis** | — (yok) |
| Order | **MySQL/MariaDB** | — (yok) |
| Payment, Notification, Chat | — (yok) | SQL Server / MongoDB |

## Hocada olup bizde olmayanlar

- **Queue servisi + SignalR**: `QueueHub`, hasta çağırma (`POST call/{id}`) ve "şu anki hasta" ucu; arayüzde çağrı ekranı. (Kurs B16)
- **Pharmacy servisi**: ilaç (`Medicine`) yönetimi + Admin "İlaçlar" ekranı. (B17)
- **Market servisi + Redis**: ürünler ve Redis'te sepet. (B18, B20)
- **Order servisi + MariaDB**: sipariş ve sipariş kalemleri. (B19)
- **PostgreSQL** (reçete) ve **MySQL/MariaDB** (sipariş): veritabanı çeşitliliği; bizde yalnız MongoDB + SQL Server.
- **PatientVisit** (hasta ziyaret kaydı) varlığı ve Admin **Nurse (hemşire)** ekranı (yalnız arayüzde).
- **Swagger** her serviste (bizde .NET 10'un yerleşik OpenAPI'si var, Swagger arayüzü yok).

## Bizde olup hocada olmayanlar

- **Testler** (hocada hiç yok): birim + gerçek veritabanı konteynerleriyle entegrasyon testleri, yetki tablosu testleri, saldırı testleri.
- **Güvenlik**: JWT RS256 (hocada HS256 ortak anahtar), rol + sahiplik bazlı yetki her uçta (hocada 3 uç), token tarayıcıya verilmiyor (hocada `Session`'da tutulup her istekte `HttpClient.DefaultRequestHeaders`'a yazılıyor), antiforgery, hesap kilidi, kullanıcı numaralandırma koruması.
- **Yeni servisler**: Payment (idempotency, iade, tutar doğrulama), Notification (bildirim + e-posta), Chat (mesajlaşma).
- **Randevu**: boş saat hesabı, program dışı saat ve çift randevu engeli, saat dilimi. (Hocada `DoctorSchedule` varlığı var, hesaplama mantığı görünmüyor.)
- **Kimlik**: şifremi unuttum, doktor başvurusu + onay, profil fotoğrafı, rol yönetimi, doktor hesabını doktor kaydına bağlama.
- **Arayüz**: hasta paneli ve doktor paneli (hocada arayüz büyük ölçüde Admin alanı), favoriler, bildirim zili, takvim, yazdırma, grafikli yönetim paneli.
- **Gateway** tüm servisleri kapsıyor; arayüz servislere yalnız Gateway'den gidiyor (hocada bazı çağrılar doğrudan servis portlarına).
- **Kurulum**: tek docker-compose, tek komutla kurulum ve başlatma betikleri, örnek veri aracı.

## İkisinde de olmayanlar

RabbitMQ, Kafka, MediatR, CQRS, event-driven mimari, Elasticsearch (kurs B21; hocanın deposunda henüz yok).

## Sonuç ve öneri

Hoca **kapsamda** önde: kursun son bölümleri (Queue/SignalR, Pharmacy, Market/Redis, Order/MariaDB) yazılmış.
Biz **derinlikte** öndeyiz: test, güvenlik, iş kuralları ve arayüz. Eksik dört servis kursun B16–B20 bölümleri;
sıradaki iş bunları yazmak (hocanın veritabanı seçimlerine sadık kalarak: Redis, MariaDB, isteğe bağlı PostgreSQL).
