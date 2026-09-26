# Üçüncü taraf bildirimleri / Third-party notices

Anatomi 3D aşağıdaki üçüncü taraf bileşenleri ve verileri kullanır. Her bileşen kendi lisansına tabidir.
Anatomi 3D uses the third-party components and data listed below; each is governed by its own license.

## Veri / Data

| Bileşen | Kullanım | Lisans |
|---|---|---|
| [Z-Anatomy](https://www.z-anatomy.com) — Lluís Vinent Juanico ve katkıda bulunanlar ([GitHub](https://github.com/LluisV/Z-Anatomy)) | 3B anatomi modeli, yapı adları, hiyerarşi (`data/anatomy.pak`, `app/public/body/`) | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) |
| Wikipedia katkıda bulunanları | İngilizce yapı açıklamaları (Z-Anatomy üzerinden) | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) |

Ayrıntılar ve yapılan değişiklikler: [`data/LICENSE.md`](data/LICENSE.md), [`app/public/body/LICENSE.md`](app/public/body/LICENSE.md).

## Masaüstü uygulaması / Desktop application

Aşağıdaki bileşenlerin tümü MIT lisanslıdır; lisans metni bu belgenin sonundadır.
All components below are licensed under the MIT License; the license text is reproduced at the end of this document.

| Bileşen | Telif hakkı | Kaynak |
|---|---|---|
| Dear ImGui | Copyright (c) 2014-2024 Omar Cornut | https://github.com/ocornut/imgui |
| cimgui | Copyright (c) 2015 Stephan Dilly | https://github.com/cimgui/cimgui |
| ImGui.NET | Copyright (c) 2017 Eric Mellino and ImGui.NET contributors | https://github.com/ImGuiNET/ImGui.NET |
| Vortice.Windows (Direct3D11, DXGI, D3DCompiler, DirectX), Vortice.Mathematics | Copyright (c) Amer Koleci and Contributors | https://github.com/amerkoleci/Vortice.Windows |
| SharpGen.Runtime | Copyright (c) 2010-2017 Alexandre Mutel, 2017 Jeremy Koritzinsky | https://github.com/SharpGenTools/SharpGenTools |
| meshoptimizer | Copyright (c) 2016-2026 Arseny Kapoulkine | https://github.com/zeux/meshoptimizer |
| Meshoptimizer.NET | Copyright (c) 2024 Julian | https://github.com/BoyBaykiller/Meshoptimizer.NET |
| SharpGLTF (yalnız model derleyici / asset builder only) | Copyright (c) 2019 Vicente Penades | https://github.com/vpenades/SharpGLTF |
| .NET çalışma zamanı (bağımsız sürüm paketinde / in the self-contained release) | Copyright (c) .NET Foundation and Contributors | https://github.com/dotnet/runtime |

Arayüz yazı tipleri (Segoe UI, Segoe MDL2 Assets) çalışma sırasında Windows'tan yüklenir; uygulamayla dağıtılmaz.
UI fonts (Segoe UI, Segoe MDL2 Assets) are loaded from Windows at run time and are not redistributed.

## Web sürümü / Web version

`app/` altındaki web sürümünün bağımlılıkları (React, Three.js, React Three Fiber, drei, Zustand, three-mesh-bvh vb.)
kendi lisanslarıyla npm üzerinden kurulur; ayrıntılar için `app/package.json` ve ilgili paketlere bakın.

## MIT License

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
