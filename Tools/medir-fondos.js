// medir-fondos.js — captura el player REAL con varios fondos, para poder comparar lo que
// se renderiza contra el PNG de origen.
//
// POR QUE (2026-08-27): al estudiar el «se ve apagado» hizo falta separar dos cosas que se
// confundian: que el FONDO este pintado oscuro (7 de los 11 lo estan) y que la TELE apague el
// color. Lo primero se mide en el PNG; lo segundo solo se puede medir renderizando.
//
// Uso:  node Tools/static-server.js    (en otra consola)
//       node Tools/medir-fondos.js
// Salida: _fondos/<id>.png  ->  luego  python Tools/analiza_grado_lab.py --dir _fondos
const puppeteer = require('puppeteer-core');
const fs = require('fs'); const path = require('path');
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const IDS = (process.env.BGS || 'bg_kelp,bg_classic,bg_tropical,bg_abyss').split(',');
const SALIDA = path.join(process.cwd(), '_fondos');
const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  fs.mkdirSync(SALIDA, { recursive: true });
  const logs = [];
  const b = await puppeteer.launch({ executablePath: CHROME, headless: true,
    args: ['--no-sandbox','--disable-setuid-sandbox','--use-gl=angle','--ignore-gpu-blacklist','--window-size=1920,1080'] });
  const p = await b.newPage();
  await p.setViewport({ width: 1920, height: 1080 });
  p.on('console', m => logs.push(m.text()));
  await p.goto('http://localhost:3001/?devtest=1', { waitUntil: 'networkidle0', timeout: 60000 });
  const dl = Date.now() + 90000;
  while (!logs.some(l => l.includes('AQUARIUM READY')) && Date.now() < dl) await sleep(300);
  if (!logs.some(l => l.includes('AQUARIUM READY'))) { console.error('el acuario no arranco'); process.exit(1); }
  await sleep(3000);

  for (const id of IDS) {
    const antes = logs.length;
    await p.evaluate((_id) => {
      const payload = JSON.stringify({ type: 'change_bg', value: _id });
      window.unityInstance.SendMessage('CastReceiver','OnMessageReceived',
        JSON.stringify({ type: 'UPDATE', payload }));
    }, id);
    await sleep(2000);
    // ⚠ Comprobar el EFECTO, no el eco: `agua: … (<id>)` sale de bg.CurrentPresetId.
    const ok = logs.slice(antes).some(l => l.includes(`(${id})`));
    // ⚠ El velo de "Sender desconectado" (position:fixed, z-index 400) domina la captura.
    await p.evaluate(() => document.querySelectorAll('div').forEach(d => {
      const s = getComputedStyle(d);
      if (s.position === 'fixed' && s.zIndex === '400') d.style.display = 'none';
    }));
    await p.screenshot({ path: path.join(SALIDA, `${id}.png`) });
    console.log(`${id} -> ${ok ? 'aplicado (leido del estado)' : '⚠ NO consta aplicado'}`);
  }
  await b.close();
})().catch(e => { console.error(e); process.exit(1); });
