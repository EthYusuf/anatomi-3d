"""
README animasyonlarını üretir: tools/screenshots.mjs'in docs/frames/ altına kaydettiği zaman damgalı
karelerden sabit hızlı GIF'ler oluşturur.

Kullanım: python tools/make_gifs.py   (Pillow gerekir)
Çıktı   : docs/acilis-animasyonu.gif, docs/cift-tik-kazi.gif
"""
import glob
import os
from pathlib import Path

from PIL import Image

DOCS = Path(__file__).resolve().parent.parent / "docs"


def make_gif(name: str, out: str, box: tuple[int, int, int, int], width: int, fps: int = 12, hold: float = 1.2):
    files = sorted(glob.glob(str(DOCS / "frames" / name / "*.jpg")))
    if not files:
        raise SystemExit(f"kare bulunamadı: docs/frames/{name}")
    # dosya adı: <sıra>_<zaman damgası ms>.jpg
    stamps = [int(Path(f).stem.split("_")[1]) for f in files]
    step = 1000 / fps
    frames, durations, last = [], [], None
    t = stamps[0]
    while t <= stamps[-1]:
        idx = max(i for i, s in enumerate(stamps) if s <= t)
        if idx == last:
            durations[-1] += step
        else:
            im = Image.open(files[idx]).convert("RGB").crop(box)
            frames.append(im.resize((width, round(im.height * width / im.width)), Image.LANCZOS))
            durations.append(step)
            last = idx
        t += step
    durations[-1] += hold * 1000
    pal = [f.quantize(colors=128, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE) for f in frames]
    target = DOCS / out
    pal[0].save(target, save_all=True, append_images=pal[1:], duration=[int(d) for d in durations], loop=0, optimize=True)
    print(f"{out}: {len(pal)} kare, {os.path.getsize(target) // 1024} KB")


if __name__ == "__main__":
    # kareler 960x760; üst bar ve alt araç çubuğu kırpılır
    make_gif("intro", "acilis-animasyonu.gif", box=(230, 60, 730, 690), width=380)
    make_gif("dig", "cift-tik-kazi.gif", box=(120, 60, 840, 690), width=460)
