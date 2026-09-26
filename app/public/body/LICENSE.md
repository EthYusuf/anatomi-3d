# Model verisi lisansı / Model data license

Bu klasördeki dosyalar (`*.glb`, `parts.json`, `desc/*.txt`) **Z-Anatomy** projesinden türetilmiştir
ve **Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0)** lisansı ile paylaşılır.

The files in this folder (`*.glb`, `parts.json`, `desc/*.txt`) are derived from the **Z-Anatomy**
project and are distributed under **CC BY-SA 4.0**.

- Kaynak / Source: Z-Anatomy — Lluís Vinent Juanico et al., https://www.z-anatomy.com ,
  https://github.com/LluisV/Z-Anatomy
- Lisans / License: https://creativecommons.org/licenses/by-sa/4.0/
- Açıklama metinleri Wikipedia kaynaklıdır (CC BY-SA). / Description texts originate from Wikipedia (CC BY-SA).

Yapılan değişiklikler / Changes made (`tools/build_za.mjs`):
- Etiket ve kas yapışma işaret düğümleri çıkarıldı; her yapı dünya koordinatlarında tek mesh olarak birleştirildi.
- Geometri meshoptimizer ile sadeleştirildi (~8,2M → ~740K üçgen) ve meshopt ile sıkıştırıldı.
- Yapılar sistem/kategoriye göre sınıflandırıldı; Latince adlar ve hiyerarşi `parts.json`'a eklendi.

Bu türetilmiş veriyi yeniden paylaşırken aynı lisansı kullanmanız ve kaynak göstermeniz gerekir.
When redistributing this derived data you must keep the same license and give attribution.
