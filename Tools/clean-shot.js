const puppeteer = require('puppeteer');
(async () => {
  const browser = await puppeteer.launch({ headless: true,
    args: ['--no-sandbox','--disable-setuid-sandbox','--enable-webgl','--use-gl=angle','--ignore-gpu-blacklist','--window-size=1920,1080'] });
  const page = await browser.newPage();
  await page.setViewport({ width: 1920, height: 1080 });
  page.on('console', m => { const t=m.text(); if (/Decos placed|Loaded: fish/.test(t)) console.log('[c]', t); });
  await page.goto('http://localhost:3001/?devtest=1', { waitUntil: 'domcontentloaded', timeout: 30000 });
  await page.waitForFunction(() => {
    var p = document.getElementById('dbg-panel');
    return p && /Decos placed: \d/.test(p.innerText);
  }, { timeout: 90000, polling: 500 }).catch(()=>console.log('(timeout esperando Decos placed)'));
  await new Promise(r => setTimeout(r, 3000)); // asentar render
  // Ocultar overlays para ver la escena limpia
  await page.evaluate(() => {
    var p = document.getElementById('dbg-panel'); if (p) p.style.display='none';
    var f = document.getElementById('fps-meter'); if (f) f.style.display='none';
  });
  await new Promise(r => setTimeout(r, 500));
  await page.screenshot({ path: 'deco-clean.png' });
  await page.screenshot({ path: 'deco-floor.png', clip: { x: 380, y: 760, width: 1180, height: 300 } });
  console.log('shots: deco-clean.png + deco-floor.png');
  await browser.close();
})();
