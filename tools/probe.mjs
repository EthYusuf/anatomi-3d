// Geliştirme yardımcısı: açılışta ana iş parçacığını bloke eden uzun görevleri ölçer.
import { chromium } from 'playwright-core';
const browser = await chromium.launch({ channel: 'msedge', headless: true, args: ['--use-angle=d3d11', '--ignore-gpu-blocklist', '--enable-gpu'] });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
page.on('pageerror', (e) => console.log('[pageerror]', e.message));
page.on('console', (m) => { if (m.type() === 'error' || m.text().startsWith('[dbg]')) console.log('[console]', m.text().slice(0, 300)); });
await page.addInitScript((q) => {
  if (q) localStorage.setItem('anatomi-quality', q);
  window.__long = [];
  new PerformanceObserver((l) => l.getEntries().forEach((e) => window.__long.push([Math.round(e.startTime), Math.round(e.duration)]))).observe({ entryTypes: ['longtask'] });
}, process.env.Q ?? '');
const t0 = Date.now();
await page.goto('http://localhost:4173/');
await page.waitForSelector('.toolbar', { timeout: 120000 });
console.log('toolbar at', Date.now() - t0, 'ms');
await page.waitForTimeout(Number(process.env.WAIT ?? 15000));
const long = await page.evaluate(() => window.__long);
console.log('long tasks (start ms, duration ms):', JSON.stringify(long.filter((l) => l[1] > 200)));
console.log('total blocked ms:', long.reduce((a, l) => a + l[1], 0));
await browser.close();
