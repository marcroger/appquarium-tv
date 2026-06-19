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

  // Screenshot completo — estado inicial
  const screenshotPath = path.join(__dirname, '..', 'local-test-screenshot.png');
  await page.screenshot({ path: screenshotPath, fullPage: false });
  console.log(`Screenshot guardado en: ${screenshotPath}`);

  // Screenshot zoom — recorte 640x360 centrado en la zona inferior del tank
  const zoomPath = path.join(__dirname, '..', 'local-test-zoom.png');
  await page.screenshot({
    path: zoomPath,
    fullPage: false,
    clip: { x: 1100, y: 700, width: 640, height: 320 }
  });
  console.log(`Zoom guardado en: ${zoomPath}`);

  // ── Verificar device input (startle + feed) ──────────────────────────────
  // Simula pulsaciones de teclado para verificar que el handler JS funciona.
  // NOTE: con el build actual (C# viejo) SendMessage no tiene OnDeviceInput —
  // los logs "Input: startle" / "Input: feed" solo aparecen en builds nuevos.
  // Los logs "Remote: startle" / "Remote: feed" del panel debug SÍ deben aparecer
  // porque son JS puro (sendDeviceInput → dbg() → panel).

  console.log('Probando tecla Enter (startle)...');
  await page.keyboard.press('Enter');
  await new Promise(r => setTimeout(r, 1000));

  const panelAfterStartle = await page.$eval('#dbg-panel', el => el.innerText).catch(() => '');
  const startleOk = panelAfterStartle.includes('Input: startle') || panelAfterStartle.includes('Remote: startle');
  console.log(`  Startle en panel: ${startleOk ? '✅' : '⚠️ no detectado (esperado con build viejo)'}`);

  console.log('Probando tecla F (feed)...');
  await page.keyboard.press('f');
  await new Promise(r => setTimeout(r, 1000));

  const panelAfterFeed = await page.$eval('#dbg-panel', el => el.innerText).catch(() => '');
  const feedOk = panelAfterFeed.includes('Input: feed') || panelAfterFeed.includes('Remote: feed');
  console.log(`  Feed en panel: ${feedOk ? '✅' : '⚠️ no detectado (esperado con build viejo)'}`);

  // Screenshot post-input para ver si algo cambió visualmente
  const postInputPath = path.join(__dirname, '..', 'local-test-postinput.png');
  await page.screenshot({ path: postInputPath, fullPage: false });
  console.log(`Screenshot post-input: ${postInputPath}`);

  await browser.close();
})();
