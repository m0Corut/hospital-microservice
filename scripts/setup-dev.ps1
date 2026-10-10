<#
.SYNOPSIS
  Geliştirme ortamını tek komutla kurar: altyapı konteynerleri, tüm servislerin user-secrets'ı, JWT anahtarları, derleme.
.DESCRIPTION
  1. .env yoksa .env.example'dan oluşturur (yerel geliştirme için varsayılanlar yeterli).
  2. docker compose ile MongoDB, SQL Server, Seq ve MailHog'u açar.
  3. Her servisin user-secrets'ına bağlantı cümlesini, servisler arası iç anahtarı (Internal:ApiKey) ve ilk Admin
     bilgisini yazar. Var olan değerlere dokunmaz (-Force ile üzerine yazar). Değerler ekrana basılmaz.
  4. Identity'de JWT özel anahtarı yoksa yeni bir anahtar çifti üretir (scripts/new-jwt-keys.ps1).
  5. Çözümü derler.
.EXAMPLE
  pwsh -File scripts/setup-dev.ps1
  pwsh -File scripts/setup-dev.ps1 -DryRun     # yalnız ne yapacağını söyler, hiçbir şey yazmaz
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$DryRun,
    [switch]$SkipDocker,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

# --- 0. Araçlar
Step 'Araçlar kontrol ediliyor'
foreach ($tool in 'dotnet', 'docker') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool bulunamadı. README'deki 'Gereksinimler' bölümüne bakın." }
}
Write-Host "dotnet $(dotnet --version), docker bulundu."

# --- 1. .env
Step '.env okunuyor'
$envFile = Join-Path $root '.env'
if (-not (Test-Path $envFile)) {
    if ($DryRun) { Write-Host '.env yok; .env.example kopyalanacaktı.' }
    else { Copy-Item (Join-Path $root '.env.example') $envFile; Write-Host '.env, .env.example''dan oluşturuldu (yerel geliştirme için uygun).' }
}
$cfg = @{}
$source = if (Test-Path $envFile) { $envFile } else { Join-Path $root '.env.example' }
foreach ($line in Get-Content $source) {
    if ($line -match '^\s*([A-Z_]+)\s*=\s*(.*)\s*$') { $cfg[$Matches[1]] = $Matches[2] }
}
foreach ($k in 'MONGO_USER', 'MONGO_PASSWORD', 'SQL_SA_PASSWORD') {
    if (-not $cfg[$k]) { throw ".env içinde $k eksik." }
}
$adminEmail = if ($cfg['ADMIN_EMAIL']) { $cfg['ADMIN_EMAIL'] } else { 'admin@hastane.local' }
$adminPassword = if ($cfg['ADMIN_PASSWORD']) { $cfg['ADMIN_PASSWORD'] } else { 'Admin12345!' }

# --- 2. Altyapı
if (-not $SkipDocker) {
    Step 'Altyapı konteynerleri (MongoDB, SQL Server, Seq, MailHog)'
    if ($DryRun) { Write-Host 'docker compose up -d çalıştırılacaktı.' }
    else { docker compose up -d; if ($LASTEXITCODE -ne 0) { throw 'docker compose başarısız. Docker Desktop açık mı?' } }
}

# --- 3. user-secrets
$sql = { param($db) "Server=localhost,1433;Database=$db;User Id=sa;Password=$($cfg['SQL_SA_PASSWORD']);TrustServerCertificate=True" }
$mongo = "mongodb://$($cfg['MONGO_USER']):$($cfg['MONGO_PASSWORD'])@localhost:27017/?authSource=admin"

$secretsRoot = Join-Path $env:APPDATA 'Microsoft/UserSecrets'
function Get-SecretsId($project) {
    $csproj = Get-ChildItem (Join-Path $root $project) -Filter *.csproj | Select-Object -First 1
    ([regex]::Match((Get-Content $csproj.FullName -Raw), '<UserSecretsId>([^<]+)')).Groups[1].Value
}
function Get-Secret($project, $key) {
    $file = Join-Path $secretsRoot "$(Get-SecretsId $project)/secrets.json"
    if (-not (Test-Path $file)) { return $null }
    $json = Get-Content $file -Raw | ConvertFrom-Json
    return $json.$key
}
function Set-Secret($project, $key, $value) {
    $name = Split-Path $project -Leaf
    if ((Get-Secret $project $key) -and -not $Force) { Write-Host "  $name  $key : var, atlandı"; return }
    if ($DryRun) { Write-Host "  $name  $key : yazılacaktı"; return }
    dotnet user-secrets set $key $value --project (Join-Path $root $project) | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "$name için $key yazılamadı." }
    Write-Host "  $name  $key : yazıldı"
}

Step 'user-secrets (değerler gösterilmez)'
$svc = { param($n) "src/Services/$n/Hastane.$n.Api" }

# SQL Server servisleri
Set-Secret (& $svc 'Identity')     'ConnectionStrings:IdentityDb'         (& $sql 'hastane_identity')
Set-Secret (& $svc 'Appointment')  'ConnectionStrings:DefaultConnection'  (& $sql 'hastane_appointment')
Set-Secret (& $svc 'Prescription') 'ConnectionStrings:DefaultConnection'  (& $sql 'hastane_prescription')
Set-Secret (& $svc 'Payment')      'ConnectionStrings:DefaultConnection'  (& $sql 'hastane_payment')
Set-Secret (& $svc 'Patient')      'ConnectionStrings:DefaultConnection'  (& $sql 'hastane_patient')

# MongoDB servisleri (veritabanı adları appsettings.json'da)
foreach ($n in 'Branch', 'Doctor', 'Review', 'Notification', 'Chat') {
    Set-Secret (& $svc $n) 'DatabaseSettings:ConnectionString' $mongo
}

# Servisler arası iç anahtar: Identity, Appointment ve Notification AYNI değeri kullanır
$internalKey = @('Identity', 'Appointment', 'Notification') | ForEach-Object { Get-Secret (& $svc $_) 'Internal:ApiKey' } | Where-Object { $_ } | Select-Object -First 1
if (-not $internalKey -or $Force) {
    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $internalKey = [Convert]::ToBase64String($bytes)
}
foreach ($n in 'Identity', 'Appointment', 'Notification') { Set-Secret (& $svc $n) 'Internal:ApiKey' $internalKey }

# İlk Admin: Identity açılışta, sistemde hiç Admin yoksa bu hesabı oluşturur
Set-Secret (& $svc 'Identity') 'Seed:AdminEmail' $adminEmail
Set-Secret (& $svc 'Identity') 'Seed:AdminPassword' $adminPassword

# --- 4. JWT anahtarları
Step 'JWT anahtar çifti'
if ((Get-Secret (& $svc 'Identity') 'Jwt:PrivateKeyPem') -and -not $Force) {
    Write-Host '  Identity özel anahtarı var, atlandı.'
}
elseif ($DryRun) { Write-Host '  Yeni anahtar çifti üretilecekti (src/Shared/Hastane.Shared/jwt-public.pem değişir).' }
else {
    & (Join-Path $PSScriptRoot 'new-jwt-keys.ps1') -Force
    Write-Host '  Not: jwt-public.pem değişti; bu SİZİN anahtarınız, depoya commit etmeniz gerekmez.'
}

# --- 5. Derleme
if (-not $SkipBuild -and -not $DryRun) {
    Step 'Derleme'
    dotnet build HastaneMikroservis.slnx --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Derleme başarısız.' }
}

Step 'Hazır'
Write-Host "Servisleri başlatmak için:  pwsh -File scripts/run-all.ps1"
Write-Host "Örnek veri için:            dotnet run --project tools/Hastane.DevSeed -- --admin-email $adminEmail --admin-password <.env ADMIN_PASSWORD>"
Write-Host "Arayüz: http://localhost:5010   E-postalar (MailHog): http://localhost:8025   Loglar (Seq): http://localhost:5341"
