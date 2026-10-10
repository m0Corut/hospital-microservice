# syntax=docker/dockerfile:1
# Tüm .NET projeleri (servisler, Gateway, WebUI) için TEK, parametrik Dockerfile. Bağlam: depo kökü.
#   docker build -f Dockerfile --build-arg PROJECT_PATH=src/Services/Branch/Hastane.Branch.Api \
#                --build-arg PROJECT_NAME=Hastane.Branch.Api -t hastane/branch .
# docker-compose.full.yml bu iki argümanı her servis için doldurur.

ARG PROJECT_PATH
ARG PROJECT_NAME

# ---- 1. aşama: derleme (SDK imajı ~1 GB; son imaja GİRMEZ) ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT_PATH
ARG PROJECT_NAME
WORKDIR /src

# Katman önbelleği: önce yalnız "bağımlılık bildirimleri" (props + csproj). Kaynak kod değişince bu katmanlar
# yeniden kullanılır, NuGet paketleri tekrar indirilmez; paket sürümü (Directory.Packages.props) değişince yenilenir.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Shared/Hastane.Shared/Hastane.Shared.csproj src/Shared/Hastane.Shared/
# Olay sözleşmeleri (RabbitMQ): Appointment, Notification, Queue, Market, Order başvurur; diğerleri için zararsız.
COPY src/Shared/Hastane.Contracts/Hastane.Contracts.csproj src/Shared/Hastane.Contracts/
COPY ${PROJECT_PATH}/${PROJECT_NAME}.csproj ${PROJECT_PATH}/
RUN dotnet restore "${PROJECT_PATH}/${PROJECT_NAME}.csproj"

# Sonra kaynak (Shared + bu proje). Diğer servislerin kodu bağlama bile girmez (.dockerignore değil, bilinçli COPY).
COPY src/Shared/ src/Shared/
COPY ${PROJECT_PATH}/ ${PROJECT_PATH}/
RUN dotnet publish "${PROJECT_PATH}/${PROJECT_NAME}.csproj" -c Release -o /app/publish --no-restore -p:UseAppHost=false

# ---- 2. aşama: çalışma imajı (yalnız ASP.NET çalışma zamanı + yayınlanmış dosyalar) -----------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG PROJECT_NAME
WORKDIR /app

# Kalıcı veri klasörleri (Identity fotoğrafları: /data/photos, WebUI Data Protection anahtarları: /data/keys).
# Adlandırılmış volume ilk açılışta imajdaki klasörün sahibini devralır; bu yüzden root olmayan kullanıcıya verilir.
RUN mkdir -p /data/photos /data/keys && chown -R app:app /data

COPY --from=build --chown=app:app /app/publish .

# 8080: root olmayan kullanıcı 1024 altı porta bağlanamaz; .NET imajlarının varsayılanı da 8080.
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    APP_DLL=${PROJECT_NAME}.dll
EXPOSE 8080

# Root olmayan kullanıcı ("app", uid 1654, .NET imajlarında hazır): konteynerden kaçış/yazma saldırısının etkisini azaltır.
USER app

# Exec formunda ARG genişlemez; dll adı ortam değişkeninden okunur. "exec" ile dotnet PID 1 olur (docker stop SIGTERM'i alır).
# Sondaki "--" ve "$@": compose "command:" ile verilen argümanlar (DevSeed gibi konsol araçları için) uygulamaya geçer.
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet \"$APP_DLL\" \"$@\"", "--"]