<#
.SYNOPSIS
    Anatomi 3D sürüm paketini üretir: tek exe + Data klasörü, zip ve SHA-256 dosyaları.

.DESCRIPTION
    Çıktılar artifacts/ altına yazılır:
      Anatomi3D-<sürüm>-win-x64.zip          indirilip çalıştırılan paket (.NET çalışma zamanı dahil)
      Anatomi3D-<sürüm>-win-x64.zip.sha256
      anatomy.pak, anatomy.pak.sha256          kaynak koddan derleyenler için model paketi (tools/veri-indir.ps1)

    Depo kökünde çalıştırın:

        powershell -ExecutionPolicy Bypass -File tools\paketle.ps1

.PARAMETER Surum
    Paket sürümü. Varsayılan: Directory.Build.props içindeki <Version>.
#>
param([string]$Surum)

$ErrorActionPreference = "Stop"
$kok = Split-Path -Parent $PSScriptRoot
if (-not $Surum) { $Surum = ([xml](Get-Content (Join-Path $kok "Directory.Build.props"))).Project.PropertyGroup.Version }

$pak = Join-Path $kok "data\anatomy.pak"
if (-not (Test-Path $pak)) {
    throw "data\anatomy.pak bulunamadı. Önce tools\veri-indir.ps1 ile indirin ya da tools\Anatomi3D.AssetBuilder ile üretin."
}

$ad = "Anatomi3D-$Surum-win-x64"
$artifacts = Join-Path $kok "artifacts"
$cikti = Join-Path $artifacts $ad
if (Test-Path $cikti) { Remove-Item -Recurse -Force $cikti }
New-Item -ItemType Directory -Force $artifacts | Out-Null

Write-Host "Yayınlanıyor: $ad"
dotnet publish (Join-Path $kok "src\Anatomi3D.Desktop") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -p:DebugSymbols=false -o $cikti
if ($LASTEXITCODE -ne 0) { throw "dotnet publish başarısız oldu" }
if (-not (Test-Path (Join-Path $cikti "Data\anatomy.pak"))) { throw "Data\anatomy.pak paket klasörüne kopyalanmadı" }

Copy-Item (Join-Path $kok "LICENSE"), (Join-Path $kok "THIRD-PARTY-NOTICES.md") $cikti
$okubeni = @"
Anatomi 3D $Surum — Windows için etkileşimli tam vücut 3D anatomi atlası
https://github.com/EthYusuf/anatomi-3d

ÇALIŞTIRMA
  Anatomi3D.exe dosyasına çift tıklayın. Kurulum gerekmez; .NET çalışma zamanı pakete dahildir.
  Data klasörü exe ile aynı yerde durmalıdır.

  Windows SmartScreen uyarı verirse: "Ek bilgi" > "Yine de çalıştır".

GEREKSİNİMLER
  Windows 10 veya 11 (64 bit), DirectX 11 destekli ekran kartı.

TEMEL KULLANIM
  Sol sürükle: döndür   Sağ sürükle: kaydır   Tekerlek: yakınlaştır
  Tıkla: seç ve bilgi kartını aç   Çift tıkla: yapıyı kaldır   Ctrl+K: ara
  Tüm kısayollar için uygulamadaki ? düğmesine bakın.

LİSANSLAR
  Kaynak kod ve Türkçe içerik: MIT (LICENSE).
  3B model verisi (Data\anatomy.pak): Z-Anatomy'den türetilmiştir, CC BY-SA 4.0 (Data\LICENSE.md).
  Üçüncü taraf bileşenler: THIRD-PARTY-NOTICES.md.

Bu uygulama eğitim amaçlıdır; tanı veya tedavi kararlarında kullanılmamalıdır.
"@
[System.IO.File]::WriteAllText((Join-Path $cikti "OKUBENI.txt"), $okubeni.Replace("`n", "`r`n"), (New-Object System.Text.UTF8Encoding $true))

$zip = Join-Path $artifacts "$ad.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Write-Host "Sıkıştırılıyor: $zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($cikti, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)

function Yaz-Ozet([string]$dosya) {
    $h = (Get-FileHash -Algorithm SHA256 $dosya).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText("$dosya.sha256", "$h  $(Split-Path -Leaf $dosya)`n", (New-Object System.Text.UTF8Encoding $false))
    return $h
}
$hZip = Yaz-Ozet $zip
Copy-Item -Force $pak (Join-Path $artifacts "anatomy.pak")
$hPak = Yaz-Ozet (Join-Path $artifacts "anatomy.pak")

Write-Host ""
Write-Host ("Paket : {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
Write-Host "SHA-256 zip : $hZip"
Write-Host "SHA-256 pak : $hPak"
