# Değişiklik günlüğü

Bu dosyadaki biçim [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/) önerilerine, sürüm numaraları
[Anlamsal Sürümleme](https://semver.org/lang/tr/) kurallarına uyar.

## [Yayımlanmamış]

### Düzeltildi

- `tools/veri-indir.ps1`: Windows PowerShell 5.1'de SHA-256 özet dosyası metin yerine bayt dizisi olarak okunduğu için
  doğrulama başarısız oluyordu; özet dosyası artık diske indirilip okunuyor.

## [2.0.0] — 2026-09-27

Anatomi 3D, C# (.NET 8) ve Direct3D 11 ile Windows masaüstü uygulaması olarak yeniden yazıldı.

### Eklendi

- **Masaüstü uygulaması** (`src/`): yerel Win32 pencere, Dear ImGui tabanlı Türkçe/İngilizce arayüz, harici ekran kartı seçimi,
  ekran kartına göre otomatik kalite ayarı.
- **Görüntüleme hattı**: PBR malzemeler (GGX, clearcoat, sheen, deri saçılımı yaklaşımı), ortam ışığı, gölge haritası, SSAO,
  4× MSAA, uyarlamalı teselasyon, prosedürel yüzey ayrıntısı, bloom ve ACES ton eşleme, sıra bağımsız saydamlıkla X-ray,
  dolu kesit yüzeyleri, GPU'da seçim, ekran uzayı hatasına göre ayrıntı düzeyi.
- **Model paketi derleyici** (`tools/Anatomi3D.AssetBuilder`): Z-Anatomy modelinden `anatomy.pak` üretimi — yönelim
  düzeltme, dikişsiz ve alt bölümlenmiş deri (~1,83 milyon üçgen), dört ayrıntı düzeyi, kas lif yönleri, ortam kapatması,
  kas origo/insersiyo alanları, Brotli + meshopt sıkıştırma.
- **Türkçe bilgi bankası** (`data/content/`): ~1.670 yapı ve ~360 grup için Türkçe adlar; kemiklerin ve kasların tamamı,
  kalp, solunum, sindirim, üriner, genital, endokrin sistem, periton, duyu organları ve beyin zarları için 379 bilgi girdisi
  (özet, origo, insersiyo, innervasyon, kanlanma, fonksiyon, klinik not).
- Kas yapışma yerlerinin kemik üzerinde gösterimi, 23 hazır bölge, Türkçe duyarlı üç dilli arama, quiz modu, ekran
  görüntüsü (F12), yön göstergesi.
- `--content-report` ile içerik kapsama raporu; `--script` ile görsel test ve belge görseli otomasyonu.
- `tools/veri-indir.ps1`: model paketini sürümlerden indirip SHA-256 ile doğrulayan betik.
- `tools/paketle.ps1`: tek exe + `Data` klasöründen oluşan, .NET çalışma zamanını içeren sürüm paketini (zip + SHA-256) üreten betik.

### Değişti

- README, masaüstü sürümünü ana ürün olarak tanıtacak şekilde yeniden düzenlendi; lisans notları ve üçüncü taraf
  bildirimleri (`THIRD-PARTY-NOTICES.md`, `data/LICENSE.md`) eklendi.
- Model paketi boyutu nedeniyle Git deposu yerine sürüm dosyası olarak yayınlanıyor.

### Korundu

- Tarayıcıda çalışan ilk sürüm (`app/`, React + Three.js) değiştirilmeden depoda kalıyor.

## [1.0.0] — 2026-09-26

- Tarayıcıda çalışan tam vücut 3D anatomi atlası (React 19, React Three Fiber, Three.js): ~2.950 yapı, çift tıklayarak
  diseksiyon, prosedürel dokular, canlı fizyoloji, X-ray, kesitler, Türkçe/Latince/İngilizce adlar, quiz ve mobil uyum.

[Yayımlanmamış]: https://github.com/EthYusuf/anatomi-3d/compare/v2.0.0...main
[2.0.0]: https://github.com/EthYusuf/anatomi-3d/releases/tag/v2.0.0
[1.0.0]: https://github.com/EthYusuf/anatomi-3d/commit/cc27861
