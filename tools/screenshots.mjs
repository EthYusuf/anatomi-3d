// README için ekran görüntüleri ve animasyon kareleri üretir.
// Önce geliştirme sunucusunu başlatın: (app) npm run dev -- --port 4173
// Kullanım: node tools/screenshots.mjs  → docs/screenshots/*.jpg, docs/frames/<ad>/*.png
import { chromium } from 'playwright-core';
import { mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';

const ROOT = fileURLToPath(new URL('..', import.meta.url));
const OUT = join(ROOT, 'docs', 'screenshots');
const FRAMES = join(ROOT, 'docs', 'frames');
const URL_ = process.env.URL ?? 'http://localhost:4173/';
mkdirSync(OUT, { recursive: true });
rmSync(FRAMES, { recursive: true, force: true });

const browser = await chromium.launch({
  channel: 'msedge',
  headless: true,
  args: ['--use-angle=d3d11', '--ignore-gpu-blocklist', '--enable-gpu'],
});

async function open({ width = 1600, height = 960, mobile = false, lang = 'tr' } = {}) {
  const page = await browser.newPage({
    viewport: { width, height },
    deviceScaleFactor: mobile ? 2 : 1,
    isMobile: mobile,
    hasTouch: mobile,
  });
  await page.addInitScript((l) => {
    localStorage.setItem('anatomi-dig-hint-seen', '1');
    localStorage.setItem('anatomi-quality', 'high');
    localStorage.setItem('anatomi-lang', l);
  }, lang);
  page.on('pageerror', (e) => console.log('[pageerror]', e.message));
  await page.goto(URL_);
  await page.waitForSelector('.toolbar', { timeout: 120000, state: 'attached' });
  return page;
}

/** Shader ısınması ve açılış animasyonu bitene kadar bekle */
async function ready(page) {
  await page.waitForFunction(() => window.__store?.getState().sceneReady === true, null, { timeout: 180000 });
  await page.waitForTimeout(10000);
}

const cam = (page, pos, target) =>
  page.evaluate(
    ([p, t]) => {
      const s = window.__r3f.get();
      s.controls.target.set(...t);
      s.camera.position.set(...p);
      s.controls.update();
    },
    [pos, target],
  );

const shot = async (page, name, wait = 1800) => {
  await page.waitForTimeout(wait);
  await page.screenshot({ path: join(OUT, `${name}.jpg`), type: 'jpeg', quality: 86, timeout: 120000 });
  console.log('✓', name);
};

/**
 * Tarayıcının ekran yayını (CDP screencast) ile kareleri çizildikleri anda, zaman damgalı yakalar.
 * Ekran görüntüsü almaktan çok daha akıcıdır; GIF'ler bu karelerden sabit hızla üretilir.
 */
async function record(page, name, ms, action) {
  const dir = join(FRAMES, name);
  mkdirSync(dir, { recursive: true });
  const cdp = await page.context().newCDPSession(page);
  let n = 0;
  cdp.on('Page.screencastFrame', async (f) => {
    writeFileSync(join(dir, `${String(n++).padStart(4, '0')}_${Math.round(f.metadata.timestamp * 1000)}.jpg`), Buffer.from(f.data, 'base64'));
    await cdp.send('Page.screencastFrameAck', { sessionId: f.sessionId }).catch(() => {});
  });
  await cdp.send('Page.startScreencast', { format: 'jpeg', quality: 88, everyNthFrame: 1 });
  if (action) await action();
  await page.waitForTimeout(ms);
  await cdp.send('Page.stopScreencast');
  await cdp.detach();
  console.log('✓ frames', name, n);
}

const dbl = async (page, x, y) => {
  await page.mouse.click(x, y);
  await page.waitForTimeout(90);
  await page.mouse.click(x, y);
};

// ---------------------------------------------------------------- açılış animasyonu (GIF kareleri)
{
  const page = await open({ width: 960, height: 760 });
  await page.evaluate(() => {
    const s = window.__store.getState();
    s.togglePanel('left', false);
    s.togglePanel('right', false);
  });
  await page.waitForFunction(() => window.__store?.getState().sceneReady === true, null, { timeout: 180000 });
  await record(page, 'intro', 6000);
  await page.close();
}

// ---------------------------------------------------------------- masaüstü ekranları
{
  const page = await open();
  await ready(page);
  await shot(page, '01-tam-vucut');

  // Tek tıkla seçim + bilgi paneli
  await page.keyboard.press(']');
  await page.waitForTimeout(2200);
  await cam(page, [0.34, 1.36, 0.95], [-0.02, 1.28, 0.05]);
  await page.waitForTimeout(1200);
  await page.mouse.click(700, 520);
  await shot(page, '02-kas-secim-bilgi', 2200);
  await page.keyboard.press('Escape');

  // Kas lifleri yakın plan
  await cam(page, [0.2, 1.35, 0.5], [-0.08, 1.3, 0.08]);
  await page.mouse.move(40, 900);
  await shot(page, '03-kas-lifleri-yakin');

  // İç organlar (katman soyma)
  for (let i = 0; i < 5; i++) {
    await page.keyboard.press(']');
    await page.waitForTimeout(250);
  }
  await page.waitForTimeout(2200);
  await cam(page, [0.4, 1.18, 1.05], [0, 1.08, 0.05]);
  await shot(page, '04-ic-organlar');

  // X-ray + kalp (Türkçe arama)
  await page.evaluate(() => window.__store.getState().resetAll());
  await page.waitForTimeout(2500);
  await page.fill('.search input', 'kalp');
  await page.waitForTimeout(700);
  await shot(page, '05-turkce-arama', 400);
  await page.click('.results li:first-child button');
  await shot(page, '06-derin-yapi-vurgulama', 3500);
  await page.fill('.search input', '');
  await page.evaluate(() => window.__store.getState().resetAll());
  await page.waitForTimeout(2500);

  // Göz yakın plan
  await cam(page, [-0.02, 1.595, 0.17], [-0.031, 1.591, 0.07]);
  await shot(page, '07-goz-yakin-plan', 2500);
  await cam(page, [0.12, 1.62, 0.45], [0, 1.58, 0.05]);
  await shot(page, '08-yuz', 2500);

  // Sagittal kesit: beyin
  await page.evaluate(() => {
    const s = window.__store.getState();
    s.setClipAxis('sagittal');
  });
  await cam(page, [0.55, 1.62, 0.12], [0, 1.6, 0]);
  await shot(page, '09-sagittal-kesit', 2500);
  await page.evaluate(() => window.__store.getState().setClipAxis('none'));

  // X-ray
  await page.evaluate(() => {
    const s = window.__store.getState();
    s.requestCamera('home');
    s.setViewMode('xray');
  });
  await shot(page, '10-xray', 3000);
  await page.evaluate(() => window.__store.getState().setViewMode('solid'));

  // Kas fonksiyon renkleri
  await page.evaluate(() => {
    const s = window.__store.getState();
    s.setPeel(1);
    s.setColorMode('function');
  });
  await shot(page, '11-kas-fonksiyon-renkleri', 3000);
  await page.evaluate(() => window.__store.getState().resetAll());
  await page.waitForTimeout(2500);

  // Quiz
  await page.click('.pill');
  await shot(page, '12-quiz', 1500);
  await page.close();
}

// ---------------------------------------------------------------- çift tıklayarak kazı (GIF kareleri + ekran)
{
  const page = await open({ width: 960, height: 760 });
  await page.evaluate(() => {
    const s = window.__store.getState();
    s.togglePanel('left', false);
    s.togglePanel('right', false);
  });
  await ready(page);
  await cam(page, [0.2, 1.36, 0.95], [-0.06, 1.3, 0.08]);
  await page.waitForTimeout(1500);
  const [x, y] = [400, 420];
  // deri → göğüs kası → serratus: her çift tıklamada bir katman
  await record(page, 'dig', 7000, async () => {
    await page.waitForTimeout(400);
    await dbl(page, x, y);
    await page.waitForTimeout(2300);
    await dbl(page, x, y);
    await page.waitForTimeout(2300);
    await dbl(page, x, y);
  });
  await page.waitForTimeout(800);
  await page.screenshot({ path: join(OUT, '13-cift-tik-kazi.jpg'), type: 'jpeg', quality: 86 });
  console.log('✓ 13-cift-tik-kazi');
  await page.close();
}

// ---------------------------------------------------------------- mobil
{
  const page = await open({ width: 390, height: 844, mobile: true });
  await ready(page);
  await shot(page, '14-mobil', 500);
  await page.close();
}

await browser.close();
