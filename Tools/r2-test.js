// r2-test.js — test contra el player deployado en R2, sin TV física.
// Uso: node Tools/r2-test.js
// Requiere: npm install puppeteer (en la carpeta del proyecto)

const puppeteer = require('puppeteer');

const R2_URL = 'https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev/?devtest=1';
const TIMEOUT = 90000; // 90s para Unity + carga bundles desde R2

(async () => {
  console.log('Lanzando Chrome con Unity en modo devtest contra R2...');
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

  const logs = [];

  // Capturar logs del dbg-panel y consola del browser
  page.on('console', msg => {
    const text = msg.text();
    logs.push({ type: msg.type(), text });
    console.log(`[browser ${msg.type()}] ${text}`);
  });

  page.on('pageerror', err => {
    logs.push({ type: 'pageerror', text: err.message });
    console.error(`[page error] ${err.message}`);
  });

  page.on('requestfailed', req => {
    console.log(`[net FAIL] ${req.url()} — ${req.failure()?.errorText}`);
  });

  console.log(`Abriendo: ${R2_URL}`);
  await page.goto(R2_URL, { waitUntil: 'domcontentloaded', timeout: 30000 });

  // Esperar hasta que aparezca algo útil en el dbg-panel (crash o éxito)
  const startTime = Date.now();
  let result = null;

  try {
    result = await page.waitForFunction(() => {
      const panel = document.getElementById('dbg-panel');
      if (!panel) return false;
      const text = panel.innerText;
      // Parar si hay crash, AQUARIUM READY, o si han pasado logs suficientes
      if (text.includes('JS ERR')) return 'CRASH';
      if (text.includes('AQUARIUM READY')) return 'OK';
      if (text.includes('ERR fish') || text.includes('ERR deco')) return 'LOAD_ERR';
      return false;
    }, { timeout: TIMEOUT, polling: 1000 });
  } catch (e) {
    console.log('Timeout esperando resultado (puede ser hang)');
  }

  // Leer el panel final
  const panelText = await page.evaluate(() => {
    const el = document.getElementById('dbg-panel');
    return el ? el.innerText : '(panel no encontrado)';
  }).catch(() => '(error leyendo panel)');

  const elapsed = ((Date.now() - startTime) / 1000).toFixed(1);
  console.log(`\n══════════════════════════════════════════`);
  console.log(`Tiempo transcurrido: ${elapsed}s`);
  console.log(`\nDBG PANEL (más reciente arriba):`);
  console.log(panelText);

  // Screenshot final
  await page.screenshot({ path: 'r2-test-screenshot.png', fullPage: false });
  console.log('\nScreenshot guardado: r2-test-screenshot.png');

  await browser.close();
})();
