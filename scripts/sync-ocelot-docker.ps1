<#
.SYNOPSIS
  ocelot.json'dan ocelot.Docker.json üretir (rotalar aynı, yalnız ana makine adı + port konteynere göre).
.DESCRIPTION
  Konteynerde "localhost" konteynerin KENDİSİDİR; Gateway'in Identity'ye ulaşması için adres "identity:8080" olmalı.
  ocelot.json geliştirme içindir (localhost:5001...). Bu betik her rotadaki Host/Port'u aşağıdaki tabloya göre çevirir:
  port tablosu geliştirme portlarıdır, hedef ad docker-compose.full.yml'deki servis adıdır, port hep 8080.
  Yeni servis/rota ekleyince: önce ocelot.json'u düzenle, sonra bu betiği çalıştır, iki dosyayı birlikte commit et.
  Gateway.Tests iki dosyanın aynı rota setine sahip olduğunu doğrular.
.PARAMETER Check
  Dosyayı yazmaz; ocelot.Docker.json güncel değilse 1 ile çıkar (CI / commit öncesi).
.EXAMPLE
  pwsh -File scripts/sync-ocelot-docker.ps1
  pwsh -File scripts/sync-ocelot-docker.ps1 -Check
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $root 'src/Gateway/Hastane.Gateway'
$source = Join-Path $dir 'ocelot.json'
$target = Join-Path $dir 'ocelot.Docker.json'

# geliştirme portu -> compose servis adı (geliştirme port tablosu)
$services = @{
    5001 = 'identity';     5101 = 'branch';       5102 = 'doctor';       5103 = 'review'
    5104 = 'appointment';  5105 = 'prescription'; 5106 = 'patient';      5107 = 'queue'
    5108 = 'pharmacy';     5109 = 'market';       5110 = 'order';        5111 = 'payment'
    5112 = 'notification'; 5113 = 'medicalrecord'; 5114 = 'chat';        5115 = 'analytics'
}
$containerPort = 8080

$config = Get-Content $source -Raw | ConvertFrom-Json -Depth 20
foreach ($route in $config.Routes) {
    foreach ($hp in $route.DownstreamHostAndPorts) {
        if (-not $services.ContainsKey([int]$hp.Port)) {
            throw "ocelot.json'da bilinmeyen port $($hp.Port) ($($route.UpstreamPathTemplate)). Betikteki tabloya ekleyin."
        }
        $hp.Host = $services[[int]$hp.Port]
        $hp.Port = $containerPort
    }
}
# Gateway'in kendi adresi: konteyner içinde 8080
$config.GlobalConfiguration.BaseUrl = "http://gateway:$containerPort"

$json = ($config | ConvertTo-Json -Depth 20).Replace("`r`n", "`n").Replace("`n", "`r`n") + "`r`n"

if ($Check) {
    $current = if (Test-Path $target) { Get-Content $target -Raw } else { '' }
    if ($current -ne $json) { Write-Error 'ocelot.Docker.json güncel değil. Çalıştırın: pwsh -File scripts/sync-ocelot-docker.ps1'; exit 1 }
    Write-Host 'ocelot.Docker.json güncel.'
    return
}

[System.IO.File]::WriteAllText($target, $json, [System.Text.UTF8Encoding]::new($false))
Write-Host "Yazıldı: $target ($($config.Routes.Count) rota)"