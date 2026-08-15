const puppeteer = require('puppeteer');
(async () => {
  const browser = await puppeteer.launch({ headless: true,
    args: ['--no-sandbox','--disable-setuid-sandbox','--enable-webgl','--use-gl=angle',
           '--ignore-gpu-blacklist','--window-size=1280,720'] });
  const page = await browser.newPage();
  await page.setViewport({ width: 1280, height: 720 });
  const errors = [];
  page.on('pageerror', e => errors.push('PAGEERR: ' + e.message));
  page.on('console', m => { const t = m.text(); if (/JS ERR|banner ERR|catch:/.test(t)) errors.push('CONSOLE: ' + t); });
  await page.goto('http://localhost:3001/?devtest=1', { waitUntil: 'domcontentloaded', timeout: 30000 });

  // Esperar a que Unity instancie (no hace falta READY completo para validar el meter)
  let unityUp = false;
  try {
    await page.waitForFunction(() => !!window.unityInstance, { timeout: 90000, polling: 500 });
    unityUp = true;
  } catch (e) { /* sigue: reportamos abajo */ }

  // Dejar correr el meter ~4s y leer el valor
  await new Promise(r => setTimeout(r, 4500));
  const meter = await page.$eval('#fps-meter', el => el.textContent).catch(() => '(no element)');
  const dbg = await page.$eval('#dbg-panel', el => el.innerText).catch(() => '');

  await page.screenshot({ path: 'fps-check.png', fullPage: false });
  console.log('UNITY_INSTANCE:', unityUp);
  console.log('FPS_METER_TEXT:', JSON.stringify(meter));
  console.log('JS_ERRORS:', errors.length ? errors.join(' || ') : 'none');
  console.log('DBG_TAIL:', dbg.split('\n').slice(-6).join(' / '));
  await browser.close();
})();
