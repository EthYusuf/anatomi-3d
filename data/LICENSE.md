# Veri lisansı / Data license

## `anatomy.pak` — 3B model paketi / 3D model package

Model paketi (GitHub sürümlerinde yayınlanır; `tools/veri-indir.ps1` ile indirilir) **Z-Anatomy** projesinden
türetilmiştir ve **Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0)** lisansı ile paylaşılır.

The model package (published with GitHub releases; downloaded by `tools/veri-indir.ps1`) is derived from the
**Z-Anatomy** project and is distributed under **CC BY-SA 4.0**.

- Kaynak / Source: Z-Anatomy — Lluís Vinent Juanico et al., https://www.z-anatomy.com ,
  https://github.com/LluisV/Z-Anatomy
- Lisans / License: https://creativecommons.org/licenses/by-sa/4.0/
- İngilizce açıklama metinleri Wikipedia kaynaklıdır (CC BY-SA). / English description texts originate from Wikipedia (CC BY-SA).

Yapılan değişiklikler / Changes made (`tools/Anatomi3D.AssetBuilder`):

- Etiket ve kas yapışma işaret düğümleri ayıklandı; her yapı dünya koordinatlarında tek mesh olarak birleştirildi,
  aynalanmış dönüşümlerde yüzey yönleri düzeltildi ve yönelim tutarlı hale getirildi.
- Deri tek parça olarak birleştirildi, sınırları eşlendi, küçük delikler kapatıldı ve interpolasyonlu Loop
  alt bölümlemesiyle inceltildi (~114 bin → ~1,83 milyon üçgen).
- Her yapı için dört ayrıntı düzeyi (LOD) üretildi; kas lif yönleri, ortam kapatması ve girinti (cavity) verisi hesaplandı;
  kas origo/insersiyo alanları ayrı parçalar olarak eklendi.
- Köşe verisi 16 bayta nicemlendi; meshoptimizer ve Brotli ile sıkıştırıldı.
- Yapılar sistem/kategoriye göre sınıflandırıldı; Latince adlar, hiyerarşi ve açıklama eşleşmeleri eklendi.

Bu türetilmiş veriyi yeniden paylaşırken aynı lisansı kullanmanız ve kaynak göstermeniz gerekir.
When redistributing this derived data you must keep the same license and give attribution.

## `content/` — Türkçe adlar ve bilgi bankası / Turkish names and knowledge base

`content/` klasöründeki Türkçe ad sözlükleri ve bilgi bankası (`content/tr/*.json`) bu projenin kendi içeriğidir ve
kaynak kodla birlikte [MIT](../LICENSE) lisansı altındadır. Bilgi girdileri **taslak** durumundadır ve klinik
kullanımdan önce uzman gözden geçirmesi gerektirir.

The Turkish name dictionaries and knowledge base in `content/` are this project's own content and are covered by the
[MIT](../LICENSE) license together with the source code. Entries are **drafts** and require expert review.
