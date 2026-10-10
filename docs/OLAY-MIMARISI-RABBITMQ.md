# Olay tabanlı mimari (RabbitMQ + MassTransit)

Servisler arası "kullanıcı beklemeyen" işler artık senkron HTTP ile değil, RabbitMQ üzerinden olayla yapılır.
Paket: MassTransit **8.5.11** (Apache-2.0; 9.x ticari lisansa geçti, kullanılmaz) + `MassTransit.RabbitMQ` + `MassTransit.EntityFrameworkCore` (outbox).

## Olay akışı

| Olay | Yayınlayan | Tüketen | Ne olur |
|---|---|---|---|
| `AppointmentCreated` | Appointment | Notification | Hastaya "Randevunuz alındı", doktora "Yeni randevu talebi" bildirimi |
| `AppointmentStatusChanged` | Appointment | Notification, Queue | Notification: karşı tarafa bildirim (hasta değiştirdiyse doktora, doktor/Admin değiştirdiyse hastaya). Queue: `NewStatus=Cancelled` ise Waiting sıra kaydı `Cancelled` + SignalR `QueueUpdated` |
| `AppointmentRescheduled` | Appointment | Notification | Hastaya ve doktora "yeniden planlandı" bildirimi (yeni tarih, klinik saati) |
| `OrderPlaced` | Order | Notification | Hastaya "Siparişiniz alındı: SIP-..." |
| `OrderCancelled` | Order | Notification, Market | Notification: "Siparişiniz iptal edildi". Market: stok iadesi (idempotent) |

Sözleşmeler: `src/Shared/Hastane.Contracts` (yalnız olay kayıtları, kişisel veri minimum). Stok **rezervasyonu** (checkout) bilerek senkron HTTP kalır.

## Güvenilirlik

- **Outbox (Appointment, Order):** olay iş verisiyle AYNI veritabanı işleminde `OutboxMessage` tablosuna yazılır; arka plan servisi RabbitMQ'ya taşır. RabbitMQ kapalıyken de randevu/sipariş işlemi başarılı olur, olay kaybolmaz.
- **En az bir kez teslim:** aynı olay iki kez gelebilir. Tüketiciler idempotent: Notification `SourceKey` (`{EventId}:{alıcı}`) tekil indeksi, Market `release` koşullu UPDATE, Queue koşullu UPDATE.
- **Yeniden deneme:** tüketicilerde 3 kez artan aralık (1 sn, 5 sn, 15 sn). Yine başarısızsa mesaj `<kuyruk>_error` kuyruğuna düşer.

## RabbitMQ yerelde

```
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:4-management
```

Yönetim arayüzü: http://localhost:15672 (varsayılan kullanıcı `guest` / `guest`, yalnız yerelde).

- **Queues and Streams** sekmesi: `notification-appointment-created`, `notification-appointment-status-changed`, `notification-appointment-rescheduled`, `notification-order-placed`, `notification-order-cancelled`, `queue-appointment-status-changed`, `market-order-cancelled`.
- Hata kuyrukları aynı sekmede `<kuyruk>_error` adıyla görünür. Kuyruğa tıklayıp **Get messages** ile mesajı ve hata başlıklarını (`MT-Fault-Message`, `MT-Fault-StackTrace`) okuyabilirsiniz; düzeltme sonrası **Move messages** ile asıl kuyruğa geri taşıyabilirsiniz.
- **Exchanges** sekmesi: her olay türü için bir exchange (`Hastane.Contracts:AppointmentCreated` gibi); tüketici kuyrukları buna bağlanır.

## Yapılandırma (user-secrets)

Her olay yayınlayan/tüketen servis (Appointment, Notification, Order, Market, Queue):

```
dotnet user-secrets set "RabbitMq:Host" "localhost"
dotnet user-secrets set "RabbitMq:Username" "guest"
dotnet user-secrets set "RabbitMq:Password" "guest"
```

`RabbitMq:Host` boşsa (testler, RabbitMQ'suz çalışma) MassTransit **bellek içi** taşıma kullanır: olaylar yalnız aynı süreç içinde teslim edilir, servisler arası bildirim akmaz.
Notification servisinde ek olarak `Scheduling:TimeZone` (varsayılan `Europe/Istanbul`) bildirim saatini klinik saatine çevirir.
