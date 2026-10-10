<#
.SYNOPSIS
  Tüm servisleri, Gateway'i ve arayüzü arka planda başlatır (ya da -Stop ile durdurur).
.DESCRIPTION
  Her süreç kendi portunda açılır; çıktılar logs/ klasörüne yazılır (depoya girmez).
  Önce scripts/setup-dev.ps1 çalıştırılmış olmalı.
.EXAMPLE
  pwsh -File scripts/run-all.ps1          # başlat
  pwsh -File scripts/run-all.ps1 -Stop    # durdur
#>
[CmdletBinding()]
param([switch]$Stop, [switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$apps = [ordered]@{
    'Identity'     = @{ Path = 'src/Services/Identity/Hastane.Identity.Api'; Port = 5001 }
    'Branch'       = @{ Path = 'src/Services/Branch/Hastane.Branch.Api'; Port = 5101 }
    'Doctor'       = @{ Path = 'src/Services/Doctor/Hastane.Doctor.Api'; Port = 5102 }
    'Review'       = @{ Path = 'src/Services/Review/Hastane.Review.Api'; Port = 5103 }
    'Appointment'  = @{ Path = 'src/Services/Appointment/Hastane.Appointment.Api'; Port = 5104 }
    'Prescription' = @{ Path = 'src/Services/Prescription/Hastane.Prescription.Api'; Port = 5105 }
    'Patient'      = @{ Path = 'src/Services/Patient/Hastane.Patient.Api'; Port = 5106 }
    'Payment'      = @{ Path = 'src/Services/Payment/Hastane.Payment.Api'; Port = 5111 }
    'Notification' = @{ Path = 'src/Services/Notification/Hastane.Notification.Api'; Port = 5112 }
    'Chat'         = @{ Path = 'src/Services/Chat/Hastane.Chat.Api'; Port = 5114 }
    'Gateway'      = @{ Path = 'src/Gateway/Hastane.Gateway'; Port = 5000 }
    'WebUI'        = @{ Path = 'src/WebUI/Hastane.WebUI'; Port = 5010 }
}

function Get-Listener($port) { Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1 }

if ($Stop) {
    foreach ($name in $apps.Keys) {
        $l = Get-Listener $apps[$name].Port
        if ($l) { Stop-Process -Id $l.OwningProcess -Force -ErrorAction SilentlyContinue; Write-Host "durduruldu: $name" }
    }
    return
}

if (-not $NoBuild) {
    dotnet build HastaneMikroservis.slnx --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Derleme başarısız.' }
}

$logs = Join-Path $root 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null

foreach ($name in $apps.Keys) {
    $app = $apps[$name]
    if (Get-Listener $app.Port) { Write-Host "zaten açık: $name ($($app.Port))"; continue }
    Start-Process dotnet -ArgumentList @('run', '--no-build', '--launch-profile', 'http', '--project', $app.Path) `
        -WorkingDirectory $root -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $logs "$name.log") -RedirectStandardError (Join-Path $logs "$name.err.log")
}

Write-Host 'Servisler açılıyor...'
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 3
    $down = $apps.Keys | Where-Object { -not (Get-Listener $apps[$_].Port) }
    if (-not $down) { break }
}
if ($down) { Write-Warning "Açılmayanlar: $($down -join ', ') (logs/ klasörüne bakın)" }
else { Write-Host "Hepsi açık. Arayüz: http://localhost:5010" -ForegroundColor Green }
