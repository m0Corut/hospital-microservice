<#
.SYNOPSIS
  Geliştirme için bir RSA-2048 JWT anahtar çifti üretir.
.DESCRIPTION
  - ÖZEL anahtar: ekrana ya da dosyaya yazılmaz; doğrudan Identity'nin user-secrets'ına (Jwt:PrivateKeyPem) konur.
  - AÇIK anahtar: src/Shared/Hastane.Shared/jwt-public.pem (depoya girer; sır değil).
  Çalıştırınca eski anahtarla imzalanmış tüm token'lar geçersiz olur. Üzerine yazmak için -Force gerekir.
.EXAMPLE
  pwsh -File scripts/new-jwt-keys.ps1
#>
[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publicPath = Join-Path $root 'src/Shared/Hastane.Shared/jwt-public.pem'
$identityProject = Join-Path $root 'src/Services/Identity/Hastane.Identity.Api'

if ((Test-Path $publicPath) -and -not $Force) {
    throw "jwt-public.pem zaten var. Yeni anahtar üretmek eski token'ları geçersiz kılar; devam etmek için -Force verin."
}

$rsa = [System.Security.Cryptography.RSA]::Create(2048)
try {
    $privatePem = $rsa.ExportPkcs8PrivateKeyPem()
    $publicPem = $rsa.ExportSubjectPublicKeyInfoPem()

    # Özel anahtar yalnız user-secrets'a gider (komut çıktısı da gizlenir).
    dotnet user-secrets set 'Jwt:PrivateKeyPem' $privatePem --project $identityProject | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'user-secrets yazılamadı.' }

    [System.IO.File]::WriteAllText($publicPath, $publicPem.Replace("`r`n", "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
}
finally {
    $rsa.Dispose()
}

Write-Host "Özel anahtar Identity user-secrets'a yazıldı (gösterilmez)."
Write-Host "Açık anahtar: $publicPath"
Write-Host "Not: açık anahtar değiştiyse Hastane.Shared'ı kullanan servisleri yeniden derleyin."
