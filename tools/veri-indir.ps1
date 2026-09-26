<#
.SYNOPSIS
    Anatomi 3D model paketini (data/anatomy.pak) GitHub sürümünden indirir ve SHA-256 ile doğrular.

.DESCRIPTION
    Model paketi ~63 MB olduğu için Git deposunda değil, GitHub sürümlerinde (Releases) yayınlanır.
    Kaynak koddan derlemeden önce depo kökünde bir kez çalıştırın:

        powershell -ExecutionPolicy Bypass -File tools\veri-indir.ps1

.PARAMETER Surum
    İndirilecek sürüm etiketi (ör. v2.0.0). Varsayılan: en son sürüm.

.PARAMETER Zorla
    Paket zaten varsa bile yeniden indirir.
#>
param(
    [string]$Surum = "latest",
    [switch]$Zorla
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Windows PowerShell 5.1'de ilerleme çubuğu indirmeyi çok yavaşlatır
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$depo  = "EthYusuf/anatomi-3d"
$kok   = Split-Path -Parent $PSScriptRoot
$hedef = Join-Path $kok "data\anatomy.pak"
$taban = if ($Surum -eq "latest") { "https://github.com/$depo/releases/latest/download" }
         else { "https://github.com/$depo/releases/download/$Surum" }

if ((Test-Path $hedef) -and -not $Zorla) {
    Write-Host "Model paketi zaten var: $hedef (yeniden indirmek için -Zorla kullanın)"
    exit 0
}

New-Item -ItemType Directory -Force (Split-Path $hedef) | Out-Null
$gecici = "$hedef.part"

Write-Host "İndiriliyor: $taban/anatomy.pak"
Invoke-WebRequest -Uri "$taban/anatomy.pak" -OutFile $gecici -UseBasicParsing

Write-Host "Doğrulanıyor (SHA-256)..."
# Özet dosyası diske indirilip okunur: Windows PowerShell 5.1, ikili içerik türündeki yanıtları metin yerine bayt dizisi döndürür
$ozetDosyasi = "$hedef.sha256.part"
Invoke-WebRequest -Uri "$taban/anatomy.pak.sha256" -OutFile $ozetDosyasi -UseBasicParsing
$beklenen = ((Get-Content $ozetDosyasi -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
Remove-Item $ozetDosyasi -Force
$gercek = (Get-FileHash -Algorithm SHA256 $gecici).Hash.ToLowerInvariant()
if ($beklenen -ne $gercek) {
    Remove-Item $gecici -Force
    throw "SHA-256 uyuşmuyor (beklenen $beklenen, inen $gercek). İndirme bozuk olabilir; tekrar deneyin."
}

Move-Item -Force $gecici $hedef
Write-Host ("Tamam: {0} ({1:N1} MB). Şimdi derleyip çalıştırabilirsiniz:" -f $hedef, ((Get-Item $hedef).Length / 1MB))
Write-Host "  dotnet run --project src/Anatomi3D.Desktop -c Release"
