<#
.SYNOPSIS
  Tüm sistemi Docker'da tek komutla ayağa kaldırır (altyapı + 15 servis + Gateway + WebUI).
.DESCRIPTION
  1. .env yoksa .env.example'dan oluşturur ve parolaları RASTGELE üretir (yalnız yeni .env için; varsa dokunmaz).
  2. .env'de INTERNAL_API_KEY boşsa rastgele üretip yazar.
  3. secrets/ altında JWT anahtar çifti yoksa üretir (özel anahtar yalnız burada; secrets/ gitignore'da).
  4. docker compose -f docker-compose.yml -f docker-compose.full.yml up -d --build --wait
  5. -Seed: örnek veri aracını (DevSeed) konteyner olarak Gateway'e karşı çalıştırır.
  Geliştirme servislerini (scripts/run-all.ps1) AYNI ANDA çalıştırmayın: portlar (5000, 5010, altyapı) çakışır.
.PARAMETER Seed   Açıldıktan sonra örnek veriyi yükler (idempotent). Herkese açık sunucuda SEED_PASSWORD'ü değiştirin.
.PARAMETER Extra  Elasticsearch + Kafka + Analytics profilini de açar (RAM: +1.5 GB).
.PARAMETER Proxy  Caddy (80/443, otomatik HTTPS) profilini açar; .env'de DOMAIN gerekir.
.PARAMETER NoBuild  İmajları yeniden derlemez (mevcut imajlarla başlatır).
.PARAMETER DryRun  Hiçbir şey yazmaz/çalıştırmaz; ne yapacağını söyler.
.EXAMPLE
  pwsh -File scripts/docker-up.ps1
  pwsh -File scripts/docker-up.ps1 -Seed -Extra
  pwsh -File scripts/docker-up.ps1 -Proxy        # VPS: alan adı + HTTPS
#>
[CmdletBinding()]
param(
    [switch]$Seed,
    [switch]$Extra,
    [switch]$Proxy,
    [switch]$NoBuild,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

function New-RandomText([int]$length, [string]$alphabet = 'abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789') {
    $bytes = New-Object byte[] $length
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    -join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })
}

# .env içindeki KEY=... satırını değiştirir (yoksa sona ekler). Değer ekrana basılmaz.
function Set-EnvValue([string]$file, [string]$key, [string]$value) {
    $lines = [System.Collections.Generic.List[string]](Get-Content $file)
    $index = $lines.FindIndex({ param($l) $l -match "^\s*$key\s*=" })
    if ($index -ge 0) { $lines[$index] = "$key=$value" } else { $lines.Add("$key=$value") }
    Set-Content -Path $file -Value $lines -Encoding utf8
}

function Get-EnvValue([string]$file, [string]$key) {
    if (-not (Test-Path $file)) { return $null }
    foreach ($line in Get-Content $file) {
        if ($line -match "^\s*$key\s*=\s*(.*?)\s*$") { return $Matches[1] }
    }
    return $null
}

# --- 0. Araçlar
Step 'Araçlar kontrol ediliyor'
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'docker bulunamadı. Docker Desktop (Windows) ya da Docker Engine + compose eklentisi kurun.' }
docker compose version | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'docker compose eklentisi (v2.24+) gerekli.' }
docker info --format '{{.ServerVersion}}' 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Docker çalışmıyor. Docker Desktop açık mı / servis başlatıldı mı?' }

# --- 1. .env
Step '.env kontrol ediliyor'
$envFile = Join-Path $root '.env'
$freshEnv = $false
if (-not (Test-Path $envFile)) {
    $freshEnv = $true
    if ($DryRun) { Write-Host '.env yok; .env.example kopyalanacak, parolalar rastgele üretilecekti.' }
    else {
        Copy-Item (Join-Path $root '.env.example') $envFile
        # Örnek parolalar herkesin bildiği değerler. Yeni kurulumda hepsi rastgele olur (eski volume'u olan geliştirme
        # makinesinde mevcut .env'ye dokunulmaz; çünkü volume'daki parola değişmez ve bağlantı kopar).
        foreach ($k in 'MONGO_PASSWORD', 'REDIS_PASSWORD', 'RABBITMQ_PASSWORD') { Set-EnvValue $envFile $k (New-RandomText 24) }
        foreach ($k in 'SQL_SA_PASSWORD', 'MARIADB_ROOT_PASSWORD') { Set-EnvValue $envFile $k ('Aa1_' + (New-RandomText 20)) }
        Set-EnvValue $envFile 'ADMIN_PASSWORD' ('Aa1_' + (New-RandomText 14))
        Set-EnvValue $envFile 'SEED_PASSWORD' ('Aa1_' + (New-RandomText 14))
        Write-Host '.env oluşturuldu (parolalar rastgele; dosyada, ekrana basılmadı).'
    }
}
if (-not $DryRun) {
    if (-not (Get-EnvValue $envFile 'INTERNAL_API_KEY')) {
        $key = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        # Base64'te '+', '/', '=' var; compose ve bağlantı cümlelerinde sorunsuz ama kabuk kullananlar için güvenli alfabeye çevir.
        Set-EnvValue $envFile 'INTERNAL_API_KEY' ($key.Replace('+', 'A').Replace('/', 'B').Replace('=', ''))
        Write-Host 'INTERNAL_API_KEY üretildi.'
    }
    foreach ($k in 'MONGO_USER', 'MONGO_PASSWORD', 'SQL_SA_PASSWORD', 'MARIADB_ROOT_PASSWORD', 'REDIS_PASSWORD', 'RABBITMQ_USER', 'RABBITMQ_PASSWORD', 'ADMIN_EMAIL', 'ADMIN_PASSWORD') {
        if (-not (Get-EnvValue $envFile $k)) { throw ".env içinde $k eksik/boş." }
    }
    if ($Proxy -and -not (Get-EnvValue $envFile 'DOMAIN')) { throw '-Proxy için .env içinde DOMAIN=alanadi.com gerekli.' }
}

# --- 2. JWT anahtar çifti (yalnız yoksa)
Step 'JWT anahtar çifti (secrets/)'
$secretsDir = Join-Path $root 'secrets'
$privatePath = Join-Path $secretsDir 'jwt-private.pem'
$publicPath = Join-Path $secretsDir 'jwt-public.pem'
if ((Test-Path $privatePath) -and (Test-Path $publicPath)) {
    Write-Host 'Var, atlandı. (Yenisini üretmek eski tokenları geçersiz kılar: iki dosyayı silip betiği yeniden çalıştırın.)'
}
elseif ($DryRun) { Write-Host 'Yeni RSA-2048 çifti secrets/ altına yazılacaktı.' }
else {
    New-Item -ItemType Directory -Force $secretsDir | Out-Null
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    try {
        $utf8 = [System.Text.UTF8Encoding]::new($false)
        [System.IO.File]::WriteAllText($privatePath, $rsa.ExportPkcs8PrivateKeyPem() + "`n", $utf8)
        [System.IO.File]::WriteAllText($publicPath, $rsa.ExportSubjectPublicKeyInfoPem() + "`n", $utf8)
    }
    finally { $rsa.Dispose() }
    if (-not $IsWindows) {
        # Konteynerdeki root olmayan kullanıcı (uid 1654) dosyayı okuyabilmeli; klasör 700: diğer yerel kullanıcılar giremez.
        chmod 700 $secretsDir
        chmod 644 $privatePath $publicPath
    }
    Write-Host 'secrets/jwt-private.pem ve jwt-public.pem yazıldı (depoya girmez).'
}

# --- 3. Compose
$compose = @('compose', '-f', 'docker-compose.yml', '-f', 'docker-compose.full.yml')
if ($Extra) {
    $compose += @('--profile', 'extra')
    # Süreç ortamı .env'den önce gelir; kullanıcı .env'de başka değer verdiyse ona dokunma.
    if (-not $env:ELASTICSEARCH_URL -and -not (Get-EnvValue $envFile 'ELASTICSEARCH_URL')) { $env:ELASTICSEARCH_URL = 'http://elasticsearch:9200' }
    if (-not $env:KAFKA_BOOTSTRAP_SERVERS -and -not (Get-EnvValue $envFile 'KAFKA_BOOTSTRAP_SERVERS')) { $env:KAFKA_BOOTSTRAP_SERVERS = 'kafka:9094' }
}
if ($Proxy) { $compose += @('--profile', 'proxy') }

$upArgs = @('up', '-d', '--wait', '--wait-timeout', '900')
if (-not $NoBuild) { $upArgs += '--build' }

Step "docker $($compose -join ' ') $($upArgs -join ' ')"
if ($DryRun) { Write-Host 'Çalıştırılmayacak (DryRun).' }
else {
    & docker @compose @upArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "`nİpucu: ilk açılışta SQL Server hazır olana dek servisler bekler; hata için:  docker compose -f docker-compose.yml -f docker-compose.full.yml logs --tail 50 <servis>" -ForegroundColor Yellow
        throw 'docker compose up başarısız.'
    }
}

# --- 4. Örnek veri
if ($Seed) {
    Step 'Örnek veri (DevSeed) Gateway üzerinden yükleniyor'
    if ($DryRun) { Write-Host 'docker compose --profile seed run --rm devseed çalıştırılacaktı.' }
    else {
        & docker @compose '--profile' 'seed' 'run' '--rm' 'devseed'
        if ($LASTEXITCODE -ne 0) { throw 'Örnek veri yüklenemedi (çıktıya bakın).' }
    }
}

Step 'Hazır'
$webUrl = if (-not $DryRun) { Get-EnvValue $envFile 'PUBLIC_WEB_URL' } else { $null }
if (-not $webUrl) { $webUrl = 'http://localhost:5010' }
Write-Host "Arayüz:         $webUrl"
Write-Host 'E-postalar:     http://127.0.0.1:8025 (MailHog)   Loglar: http://127.0.0.1:5341 (Seq)'
Write-Host 'Admin girişi:   .env içindeki ADMIN_EMAIL / ADMIN_PASSWORD'
Write-Host 'Durdur:         docker compose -f docker-compose.yml -f docker-compose.full.yml down   (veri volume''larda kalır)'
if ($freshEnv -and -not $DryRun) { Write-Host '.env yeni üretildi: parolaları bir parola yöneticisine kaydedin.' -ForegroundColor Yellow }