# Yayınlama: tüm sistemi Docker ile bir VPS'te çalıştırma

Bu belge demo / yayın ortamıdır, geliştirme değil. Geliştirme için `scripts/setup-dev.ps1` + `scripts/run-all.ps1` (README "Kurulum").
İkisini AYNI makinede aynı anda çalıştırmayın: portlar (5000, 5010, 1433, 27017...) çakışır.

## Ne çalışır?

```
Tarayıcı ──https──> Caddy (80/443) ──> webui:8080       (MVC sayfaları)
                          └─ /gateway/hubs/* ──> gateway:8080   (yalnız SignalR kuyruk ekranı, WebSocket)

webui ──> gateway:8080 ──> identity, branch, doctor, review, appointment, prescription, patient, queue,
                           pharmacy, market, order, payment, notification, chat   (hepsi :8080, yalnız Docker ağında)
servisler ──> sqlserver · mongo · mariadb · redis · mailhog   (extra profili: elasticsearch · kafka · analytics)
```

**Gateway'i dışarı açmak gerekmiyor.** Koddan doğrulandı: WebUI, API çağrılarını sunucu tarafında yapar
(`Hastane.WebUI/Infrastructure/ServiceCollectionExtensions.cs`: `Gateway:BaseUrl`, Docker'da `http://gateway:8080`).
Tarayıcının Gateway'e doğrudan gittiği tek yer SignalR kuyruk ekranıdır (`ViewModels/Queue/QueueViewModels.cs` `HubUrl`:
`<Gateway:PublicBaseUrl>/gateway/hubs/queue`; `wwwroot/js/queue-live.js` ve `queue-display.js` bağlanır). Bu yüzden Caddy
yalnız `/gateway/hubs/*` yolunu Gateway'e yollar; `/gateway/auth`, `/gateway/users` gibi diğer rotalar internetten ULAŞILAMAZ.
(`PUBLIC_GATEWAY_URL` bu yüzden alan adının kendisidir: `https://alanadi.com`.)

## Gereksinimler

| Kaynak | En az | Önerilen |
|---|---|---|
| RAM | 6 GB | 8 GB (`-Extra`: +2 GB) |
| CPU | 2 vCPU | 4 vCPU |
| Disk | 20 GB | 40 GB |

- SQL Server tek başına en az **2 GB** ister (Linux imajı bundan azıyla açılmaz). `SQL_MEMORY_LIMIT_MB` bunu sınırlar (varsayılan 2048).
- Rakamlar tahmindir (15 .NET işlemi x ~100 MB + SQL Server 2 GB + MongoDB + MariaDB + Redis); ölçülmüş değildir.
  4 GB'lık makinede 2-4 GB swap ile açılır ama yavaştır.
- Elasticsearch ve Kafka + Analytics ("extra") yalnız doktor tam metin araması ve etkinlik akışı içindir; yoksa sistem çalışır.

## 1. Sunucuyu hazırla (Ubuntu 22.04 / 24.04)

```bash
# Docker Engine + compose eklentisi (resmi depo)
sudo apt-get update && sudo apt-get install -y ca-certificates curl git openssl
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" \
  | sudo tee /etc/apt/sources.list.d/docker.list
sudo apt-get update && sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo usermod -aG docker $USER      # çıkıp tekrar girin

# Güvenlik duvarı: yalnız SSH + 80/443
sudo ufw default deny incoming
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw allow 443/udp             # HTTP/3
sudo ufw enable
```

> **Önemli:** Docker, yayımladığı portları `ufw` kurallarını ATLAYARAK iptables'a yazar. "ufw 1433'ü kapattı" demek yetmez.
> Bu yüzden `docker-compose.full.yml` altyapı portlarını, WebUI'yı ve Gateway'i yalnız `127.0.0.1`'e bağlar
> (`BIND_ADDRESS`, varsayılan 127.0.0.1). Servis portları hiç yayımlanmaz. `.env`'de `BIND_ADDRESS=0.0.0.0` YAPMAYIN.
> Dışarıya yalnız `caddy` servisinin 80/443'ü çıkar. Kontrol: `sudo ss -tlnp | grep -v 127.0.0.1` (yalnız 22, 80, 443 görünmeli).

DNS: alan adının A kaydını sunucunun IP'sine yönlendirin (Let's Encrypt sertifikası için 80/443 erişilebilir olmalı).

## 2. Depo ve ayarlar

```bash
git clone <depo-adresi> hastane && cd hastane
cp .env.example .env
nano .env
```

`.env` içinde en az şunlar:

```ini
MONGO_PASSWORD=...        # harf/rakam/_ -  (kaçışsız mongodb:// adresine giriyor; @ : / ? # % kullanmayın)
SQL_SA_PASSWORD=...       # 8+ karakter, büyük+küçük+rakam+sembol; ; ve $ kullanmayın
MARIADB_ROOT_PASSWORD=...
REDIS_PASSWORD=...
RABBITMQ_PASSWORD=...
ADMIN_EMAIL=siz@alanadi.com
ADMIN_PASSWORD=...        # ilk Admin hesabı (sistemde hiç Admin yoksa oluşur)
INTERNAL_API_KEY=...      # openssl rand -hex 32   (servisler arası iç anahtar)
DOMAIN=hastane.alanadi.com
PUBLIC_WEB_URL=https://hastane.alanadi.com
PUBLIC_GATEWAY_URL=https://hastane.alanadi.com
```

Rastgele parola üretmek için: `openssl rand -hex 16`. `SEED_PASSWORD` yalnız örnek veri yüklerseniz kullanılır; herkese açık sunucuda varsayılanı değiştirin.

JWT anahtar çifti (Identity imzalar, diğer servisler doğrular). `secrets/` gitignore'dadır, depoya girmez:

```bash
mkdir -p secrets && chmod 700 secrets
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out secrets/jwt-private.pem
openssl pkey -in secrets/jwt-private.pem -pubout -out secrets/jwt-public.pem
chmod 644 secrets/jwt-private.pem secrets/jwt-public.pem   # konteynerdeki root olmayan kullanıcı (uid 1654) okuyabilsin; klasör 700 olduğu için diğer yerel kullanıcılar giremez
```

Anahtarı kaybeder ya da yenilerseniz tüm oturumlar düşer (token'lar geçersiz olur); başka sonucu yok.
Depodaki `src/Shared/Hastane.Shared/jwt-public.pem` Docker'da KULLANILMAZ: açık anahtar `Jwt__PublicKeyPemFile` ile dosyadan gelir, yeniden derleme gerekmez.

> PowerShell (`pwsh`) kuruluysa yukarıdaki iki adımın (`.env` + anahtarlar) yerine
> `pwsh -File scripts/docker-up.ps1 -Proxy` hepsini yapar (rastgele parolalar dahil).

## 3. Başlat

```bash
docker compose -f docker-compose.yml -f docker-compose.full.yml --profile proxy up -d --build --wait --wait-timeout 900
```

İlk derleme 10-20 dakika sürebilir (15 imaj, NuGet indirmeleri); sonraki derlemeler katman önbelleğiyle hızlıdır.
Açılışta migration'lar kendiliğinden çalışır (`Database__MigrateOnStartup=true`); veritabanlarını elle oluşturmayın.

- Site: `https://hastane.alanadi.com` (Caddy sertifikayı kendisi alır). Caddy ayarı: `deploy/Caddyfile`.
- Örnek veri (isteğe bağlı, tekrar çalıştırmak güvenli): `docker compose -f docker-compose.yml -f docker-compose.full.yml --profile seed run --rm devseed`
- Elasticsearch + Kafka + Analytics: komuta `--profile extra` ekleyin ve `.env`'e `ELASTICSEARCH_URL=http://elasticsearch:9200` ile `KAFKA_BOOTSTRAP_SERVERS=kafka:9094` yazın
  (`docker-up.ps1 -Extra` bunu kendisi yapar).
- E-posta: demo ortamında giden postalar MailHog'a düşer (gerçek adrese GİTMEZ). Görmek için bilgisayarınızdan SSH tüneli:
  `ssh -L 8025:127.0.0.1:8025 -L 5341:127.0.0.1:5341 kullanici@sunucu` -> `http://localhost:8025` (MailHog), `http://localhost:5341` (Seq, parolasızdır: yalnız tünelle erişin).
  Gerçek SMTP için Notification'ın `SmtpOptions`'ı kimlik doğrulama/TLS taşımıyor; önce kodda eklenmeli.

## 4. Sağlık ve günlükler

```bash
docker compose -f docker-compose.yml -f docker-compose.full.yml ps          # hepsi healthy olmalı
docker compose -f docker-compose.yml -f docker-compose.full.yml logs -f --tail 50 identity
curl -sk https://hastane.alanadi.com/ -o /dev/null -w '%{http_code}\n'
```

## 5. Yedekleme

Kalıcı veri adlandırılmış volume'larda (`docker volume ls | grep hastane`): `hastane_sqlserver-data`, `hastane_mongo-data`,
`hastane_mariadb-data`, `hastane_redis-data`, `hastane_identity-photos` (profil fotoğrafları), `hastane_webui-keys`
(oturum çerezi anahtarları), `hastane_caddy-data` (HTTPS sertifikaları). Ayrıca `.env` ve `secrets/` (depoda yok; ayrı, güvenli yerde saklayın).

Tutarlı yedek için veritabanı araçlarıyla (konteynerler çalışırken):

```bash
mkdir -p ~/yedek && cd ~/hastane
C="docker compose -f docker-compose.yml -f docker-compose.full.yml"
# SQL Server: her veritabanı için (identity, appointment, prescription, patient, queue, pharmacy, market, payment)
$C exec sqlserver bash -c "/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P \"\$MSSQL_SA_PASSWORD\" -Q \"BACKUP DATABASE [hastane_identity] TO DISK='/var/opt/mssql/data/hastane_identity.bak' WITH INIT\""
docker cp "$($C ps -q sqlserver)":/var/opt/mssql/data/hastane_identity.bak ~/yedek/
# MongoDB
$C exec -T mongo bash -c 'mongodump -u "$MONGO_INITDB_ROOT_USERNAME" -p "$MONGO_INITDB_ROOT_PASSWORD" --authenticationDatabase admin --archive --gzip' > ~/yedek/mongo-$(date +%F).archive.gz
# MariaDB
$C exec -T mariadb bash -c 'mariadb-dump -uroot -p"$MARIADB_ROOT_PASSWORD" --databases hastane_order' > ~/yedek/order-$(date +%F).sql
# Fotoğraflar + anahtarlar (küçük, dosya kopyası yeter)
docker run --rm -v hastane_identity-photos:/d -v ~/yedek:/y alpine tar czf /y/photos-$(date +%F).tgz -C /d .
docker run --rm -v hastane_webui-keys:/d -v ~/yedek:/y alpine tar czf /y/webui-keys-$(date +%F).tgz -C /d .
```

(`mongodump` mongo:8.0 imajında yoksa MongoDB Database Tools'u kurun ya da `hastane_mongo-data` volume'unu konteyner durmuşken `tar` ile alın.)
Yedekleri sunucu dışına kopyalayın (`rsync`/`rclone`); aynı diskteki yedek disk ölünce gider. Geri yüklemeyi bir kez deneyin: denenmemiş yedek yedek değildir.

## 6. Güncelleme

```bash
cd ~/hastane
# 1) yedek al (yukarıda)  2) kodu çek  3) yeniden derle ve başlat
git pull
docker compose -f docker-compose.yml -f docker-compose.full.yml --profile proxy up -d --build --wait
docker image prune -f
```

- Yeni migration varsa servis açılırken uygular. Geri alınamaz değişiklikler (kolon silme gibi) için önce yedek alın.
- Geri dönüş: önceki commit'e `git checkout <eski>` + aynı komut. İmajları sürümlemek isterseniz `HASTANE_TAG=2026-10-10` ile derleyip aynı etiketle `up -d` yapın.
- Ocelot rotası değiştiyse (`ocelot.json`): `pwsh -File scripts/sync-ocelot-docker.ps1` çalıştırıp `ocelot.Docker.json`'u da commit'leyin
  (`Hastane.Gateway.Tests` ikisinin uyumunu denetler).

## 7. Sık sorunlar

| Belirti | Neden / çözüm |
|---|---|
| Servis `unhealthy`, günlükte `Login failed for user 'sa'` | `.env` parolası, volume'daki ilk parolayla uyuşmuyor. SQL Server parolası volume ilk oluşurken sabitlenir: eski parolayı geri yazın ya da `hastane_sqlserver-data` volume'unu silin (VERİYİ SİLER). |
| Identity açılmıyor: `Jwt:PrivateKeyPem yapılandırılmamış` / dosya bulunamadı | `secrets/jwt-private.pem` yok ya da boş; yukarıdaki `openssl` adımını çalıştırın. |
| Identity: `UnauthorizedAccessException ... jwt-private.pem` | Dosya izni. `chmod 644 secrets/*.pem` (klasör 700 kalsın). |
| `docker compose` `required variable ... is missing` | `.env`'de değişken eksik; mesaj hangisi olduğunu söyler. |
| Kuyruk ekranı canlı güncellenmiyor | Caddy `/gateway/hubs/*` yolunu Gateway'e yollamalı ve `PUBLIC_GATEWAY_URL` alan adı olmalı. 15 sn'de bir yedek yenileme vardır ama canlı değildir. |
| Her yeniden başlatmada herkes oturumdan düşüyor | `webui-keys` volume'u silinmiş. `docker compose down -v` KULLANMAYIN (tüm volume'ları siler). |
| Şifre sıfırlama bağlantıları `localhost` gösteriyor | `.env` `PUBLIC_WEB_URL` yanlış; düzeltip `up -d` yapın. |

## Render neden önerilmiyor?

Render tek bir `docker-compose` dosyasını çalıştırmaz: her servis (15 API + Gateway + WebUI) ayrı bir Render servisi olur
ve her biri ayrıca faturalanır. SQL Server, MongoDB ve MariaDB için yönetilen hizmeti yoktur (yalnız PostgreSQL ve Redis/Key-Value);
yani üçünü de kendiniz ayrı konteyner olarak, ücretli kalıcı diskle çalıştırmanız gerekir. Ücretsiz/küçük katmanlar boşta uyur
(SignalR bağlantıları kopar, ilk istek dakikalarca bekler) ve RAM sınırı SQL Server'ın 2 GB ihtiyacının altında kalabilir.
Bu proje tek makinede, tek ağda tasarlandı; bir VPS hem daha basit hem daha ucuzdur.
(Güncel fiyat ve limitleri Render'ın sayfasından doğrulayın; bu not mimari uyumsuzluğu anlatır, fiyat listesi değildir.)
