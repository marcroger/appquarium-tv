// local-test.js — screenshot automatico del receiver WebGL en modo devtest
// Uso: node Tools/local-test.js
// Requiere: npm install puppeteer (una sola vez)

const puppeteer = require('puppeteer');
const path = require('path');

(async () => {
  const browser = await puppeteer.launch({
    headless: true,
    args: [
      '--no-sandbox',
      '--disable-setuid-sandbox',
      '--enable-webgl',
      '--use-gl=angle',
      '--ignore-gpu-blacklist',
      '--enable-accelerated-2d-canvas',
      '--window-size=1920,1080',
    ],
  });

  const page = await browser.newPage();
  await page.setViewport({ width: 1920, height: 1080 });

  // Capturar logs de consola de Unity/JS
  page.on('console', msg => console.log('[browser]', msg.text()));
  page.on('pageerror', err => console.error('[page error]', err.message));

  console.log('Abriendo receiver en modo devtest...');
  const port = process.env.PORT || 3001;
  await page.goto(`http://localhost:${port}?devtest=1`, { waitUntil: 'networkidle0', timeout: 30000 });

  // Esperar a que Unity cargue y procese INIT (aprox 30s para WebGL)
  console.log('Esperando que Unity cargue...');
  await page.waitForFunction(() => {
    const panel = document.getElementById('dbg-panel');
    return panel && panel.innerText.includes('AQUARIUM READY');
  }, { timeout: 90000, polling: 1000 });

  console.log('AQUARIUM READY detectado — tomando screenshot...');
  await new Promise(r => setTimeout(r, 6000)); // 6s extra para decos async desde R2

  // Screenshot completo
  const screenshotPath = path.join(__dirname, '..', 'local-test-screenshot.png');
  await page.screenshot({ path: screenshotPath, fullPage: false });
  console.log(`Screenshot guardado en: ${screenshotPath}`);

  // Screenshot zoom — recorte 640x360 centrado en la zona inferior del tank
  // (donde suelen spawnear los peces en el devtest)
  const zoomPath = path.join(__dirname, '..', 'local-test-zoom.png');
  await page.screenshot({
    path: zoomPath,
    fullPage: false,
    clip: { x: 1100, y: 700, width: 640, height: 320 }
  });
  console.log(`Zoom guardado en: ${zoomPath}`);

  await browser.close();
})();
