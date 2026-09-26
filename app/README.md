# Anatomi 3D — web uygulaması

Proje açıklaması, ekran görüntüleri, mimari ve lisans bilgisi için depo kökündeki [README](../README.md) dosyasına bakın.

```bash
npm install
npm run dev       # geliştirme sunucusu
npm run build     # üretim derlemesi → dist/
npm run lint      # oxlint
```

Geliştirme sunucusunda (`import.meta.env.DEV`) hata ayıklama ve görsel testler için `window.__store`, `window.__model` ve `window.__r3f` açığa çıkarılır.
