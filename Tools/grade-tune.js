// grade-tune.js — afina el grado de color sobre el PLAYER REAL, sin gastar un build por variante.
//
// Por qué existe: el barrido del Editor (`TvGradeSweep`) resultó no ser fiable para esto — sus
// capturas alternaban por índice al margen de los valores (ver CAST_PARIDAD_VISUAL.md §0.1).
// Esto usa el player que se despliega, cargado en Chrome, y le manda mensajes GRADE por el mismo
// camino que usaría el móvil. Lo que se ve aquí es lo que hace el build de verdad.
//
// Uso:
//   1) servir el player:  (cd webgl-output && python -m http.server 3001)
//   2) node Tools/grade-tune.js
//
// Salida: _gradetune/NN_nombre.png + un resumen de luminancia/saturación por variante.
//
// ⚠ Esto NO sustituye a la tele: dice cómo QUEDA, no cuánto CUESTA. El coste de GPU (el bloom es
// el efecto más caro en el Mali-G31 de la Xiaomi) sólo lo dice el device.

const puppeteer = require('puppeteer');
const fs = require('fs');
const path = require('path');

const PUERTO = process.env.PORT || 3001;
const SALIDA = path.join(process.cwd(), '_gradetune');

// A = lo que sale del build tal cual (la escena manda). B = el grado exacto del móvil.
// El resto separa qué aporta cada palanca. Z es un control deliberadamente extremo: si Z no se
// ve gris y oscura, el grado NO se está aplicando y el resto de capturas no valen nada.
const VARIANTES = [
  { nombre: 'A_build_tal_cual',   grade: null },
  { nombre: 'B_movil_exacto',     grade: { bloom: true,  bloomIntensity: 1.2,  tonemapping: false, saturation: -15, contrast: 0,  exposure: 0.10 } },
  { nombre: 'C_movil_con_tm',     grade: { bloom: true,  bloomIntensity: 1.2,  tonemapping: true,  saturation: -15, contrast: 0,  exposure: 0.10 } },
  { nombre: 'D_bloom_medio',      grade: { bloom: true,  bloomIntensity: 0.6,  tonemapping: true,  saturation: 0,   contrast: 10, exposure: 0.05 } },
  { nombre: 'E_bloom_bajo',       grade: { bloom: true,  bloomIntensity: 0.35, tonemapping: true,  saturation: 10,  contrast: 10, exposure: 0.05 } },
  { nombre: 'F_sin_bloom_sat18',  grade: { bloom: false, bloomIntensity: 0,    tonemapping: true,  saturation: 18,  contrast: 10, exposure: 0.05 } },
  { nombre: 'G_sin_tm_sat18',     grade: { bloom: false, bloomIntensity: 0,    tonemapping: false, saturation: 18,  contrast: 10, exposure: 0.05 } },
  { nombre: 'Z_control_extremo',  grade: { bloom: false, bloomIntensity: 0,    tonemapping: false, saturation: -100, contrast: 0, exposure: -1.0 } },
];

const esperar = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  fs.mkdirSync(SALIDA, { recursive: true });

  const browser = await puppeteer.launch({
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--enable-webgl', '--use-gl=angle',
           '--ignore-gpu-blacklist', '--window-size=1920,1080'],
  });

  const page = await browser.newPage();
  await page.setViewport({ width: 1920, height: 1080 });

  const logs = [];
  page.on('console', m => {
    const t = m.text();
    logs.push(t);
    if (/GRADE:|POSTFX:|ERR|renderScale|rp=/.test(t)) console.log('  [browser]', t);
  });

  console.log(`Abriendo el player en localhost:${PUERTO} …`);
  await page.goto(`http://localhost:${PUERTO}?devtest=1`, { waitUntil: 'networkidle0', timeout: 60000 });

  // Esperar al acuario por el log de C#, no por el DOM: el panel puede estar oculto en producción.
  console.log('Esperando AQUARIUM READY …');
  const limite = Date.now() + 120000;
  while (Date.now() < limite && !logs.some(l => l.includes('AQUARIUM READY'))) await esperar(1000);
  if (!logs.some(l => l.includes('AQUARIUM READY'))) {
    console.error('El acuario no llegó a estar listo. ¿Está servido webgl-output/ en ese puerto?');
    await browser.close();
    process.exit(1);
  }
  await esperar(3000);   // que se asienten peces y decos

  const medidas = [];
  for (let i = 0; i < VARIANTES.length; i++) {
    const v = VARIANTES[i];
    if (v.grade) {
      await page.evaluate((payload) => {
        window.unityInstance.SendMessage('CastReceiver', 'OnMessageReceived',
          JSON.stringify({ type: 'GRADE', payload }));
      }, JSON.stringify(v.grade));
      await esperar(1500);
    }

    // ⚠ El receiver tapa el canvas con el overlay "Sender desconectado" en cuanto el sender
    // del devtest se apaga, y ese velo (rgba(4,14,26,0.72)) domina la captura: la primera tanda
    // salió con las 8 variantes a 22 de luminancia y pareciendo idénticas. No tiene id, así que
    // se localiza por su z-index.
    await page.evaluate(() => {
      document.querySelectorAll('div').forEach(d => {
        const st = getComputedStyle(d);
        if (st.position === 'fixed' && st.zIndex === '400') d.style.display = 'none';
      });
    });

    const archivo = path.join(SALIDA, `${String(i).padStart(2, '0')}_${v.nombre}.png`);
    await page.screenshot({ path: archivo });
    medidas.push({ nombre: v.nombre, archivo });
    console.log(`${i + 1}/${VARIANTES.length} → ${path.basename(archivo)}`);
  }

  await browser.close();
  console.log(`\nListo. ${medidas.length} capturas en ${SALIDA}`);
  console.log('Ahora: python Tools/grade_contact_sheet.py --dir _gradetune');
  console.log('⚠ Mira Z_control_extremo: si NO sale gris y oscura, el grado no se aplica y el resto no vale.');
})();
