// Geliştirme yardımcısı: uygulamanın ekran görüntülerini alır.  node tools/shot.mjs <out-dir> [steps.json]
import { readFileSync } from 'node:fs';
import { chromium } from 'playwright-core';
const out = process.argv[2];
const browser = await chromium.launch({ channel: 'msedge', headless: true, args: process.env.SWIFT ? ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'] : ['--use-angle=d3d11', '--ignore-gpu-blocklist', '--enable-gpu'] });
const page = await browser.newPage({ viewport: process.env.MOBILE ? { width: 390, height: 844 } : { width: 1440, height: 900 }, deviceScaleFactor: process.env.MOBILE ? 2 : 1, hasTouch: !!process.env.MOBILE, isMobile: !!process.env.MOBILE });
page.on('console', (m) => console.log('[console]', m.type(), m.text().slice(0, 300)));
page.on('pageerror', (e) => console.log('[pageerror]', e.message));
const t0 = Date.now();
await page.goto('http://localhost:4173/');
await page.waitForSelector('.toolbar', { timeout: 60000 });
if (process.env.NOSHOT) { for (const s of JSON.parse(process.argv[3] ?? '[]')) { if (s.eval) console.log('eval', await page.evaluate(s.eval)); if (s.evalFile) console.log('eval', await page.evaluate(readFileSync(s.evalFile, 'utf8'))); if (s.key) await page.keyboard.press(s.key); await page.waitForTimeout(s.wait ?? 1000); } await browser.close(); process.exit(0); }
console.log('loaded in', Date.now() - t0, 'ms');
if (!process.env.INTRO) {
  await page.waitForTimeout(1500);
  await page.screenshot({ path: `${out}/01-home.png`, timeout: 120000 });
}
const steps = JSON.parse(process.argv[3] ?? '[]');
let i = 2;
for (const s of steps) {
  if (s.click) await page.click(s.click);
  if (s.mouse) await page.mouse.click(s.mouse[0], s.mouse[1]);
  if (s.dbl) { await page.mouse.click(s.dbl[0], s.dbl[1]); await page.waitForTimeout(90); await page.mouse.click(s.dbl[0], s.dbl[1]); }
  if (s.key) await page.keyboard.press(s.key);
  if (s.type) await page.fill(s.type[0], s.type[1]);
  if (s.eval) console.log('eval', await page.evaluate(s.eval));
  if (s.evalFile) console.log('eval', await page.evaluate(readFileSync(s.evalFile, 'utf8')));
  await page.waitForTimeout(s.wait ?? 1500);
  if (s.shot) await page.screenshot({ path: `${out}/${String(i++).padStart(2, '0')}-${s.shot}.png`, timeout: 120000 });
}
const fps = await page.evaluate(() => new Promise((r) => { let n = 0; const t = performance.now(); const f = () => { n++; if (performance.now() - t < 2000) requestAnimationFrame(f); else r(n / 2); }; requestAnimationFrame(f); }));
console.log('fps', fps);
await browser.close();
