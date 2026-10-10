# Hastane.DevSeed

Geliştirme için örnek veri yükleyen konsol aracı. Tüm çağrılar Gateway üzerinden gider; servislerin çalışıyor olması gerekir.

```
dotnet run --project tools/Hastane.DevSeed -- --admin-email <e> --admin-password <p> [--gateway http://localhost:5000] [--password Hastane123!] [--web-base http://localhost:5010]
```

- Admin hesabı Identity'de `Seed:AdminEmail` / `Seed:AdminPassword` ile önceden var olmalı; araç onu oluşturmaz, yalnız giriş yapar.
- `--password`: doktor (`dr.<ad>.<soyad>@hastane.local`) ve hasta (`hasta1..6@hastane.local`) hesaplarının ortak şifresi.
- `--web-base`: doktor galerisi mutlak http(s) adresi ister; görseller `<web-base>/img/clinics/clinic-0X.svg` olarak yazılır.
- Idempotent: tekrar çalıştırınca branş/doktor adı, e-posta, hastanın randevu sayısı, reçete (randevu başına), yorum (metin) ve ödeme (randevudan türetilen Idempotency-Key) kopyalanmaz.
- Bir adım hata verirse durum kodu ve ProblemDetails `detail` yazılır, araç devam eder; sonda hata sayısı gösterilir ve hata varsa çıkış kodu 1 olur.

Adımlar: branşlar (8), doktorlar (12), doktor hesapları (rol + doktor bağı), hastalar (6), randevular, durumlar (Confirmed/Completed/Cancelled), ödemeler, reçeteler, yorumlar.
