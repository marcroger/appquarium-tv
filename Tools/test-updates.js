#!/usr/bin/env node
// Tests the new real-time Cast UPDATE handlers (add_fish, remove_fish, change_bg, change_sub, change_light).
// Injects fake UPDATE messages via JS eval and checks browser console logs for expected C# responses.

const puppeteer = require('puppeteer-core');

const CHROME = process.env.CHROME_PATH ||
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const URL = 'http://localhost:3001/?devtest=1';

function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }

(async () => {
  const consoleLogs = [];
  const browser = await puppeteer.launch({
    executablePath: CHROME,
    headless: true,
    args: ['--no-sandbox','--disable-setuid-sandbox','--use-gl=swiftshader',
           '--disable-features=VizDisplayCompositor'],
  });

  const page = await browser.newPage();
  page.on('console', m => {
    const text = m.text();
    consoleLogs.push(text);
    if (text.includes('[C#]') || text.includes('AQUARIUM') || text.includes('UPDATE'))
      console.log(`[browser] ${text}`);
  });

  await page.goto(URL, { waitUntil: 'networkidle0', timeout: 60000 });

  console.log('Waiting for AQUARIUM READY...');
  await page.waitForFunction(
    () => typeof dbg === 'function' || true, // ensure page loaded
    { timeout: 10000 }
  );

  // Wait until C# logs AQUARIUM READY
  const deadline = Date.now() + 40000;
  while (!consoleLogs.some(l => l.includes('AQUARIUM READY')) && Date.now() < deadline)
    await sleep(300);

  if (!consoleLogs.some(l => l.includes('AQUARIUM READY'))) {
    console.error('❌ AQUARIUM READY never received');
    await browser.close();
    process.exit(1);
  }
  console.log('✅ AQUARIUM READY\n');
  await sleep(500);

  // Send a Cast UPDATE message via JS eval
  async function sendUpdate(type, value) {
    await page.evaluate((_type, _value) => {
      var payload = JSON.stringify({ type: _type, value: _value });
      var msg = JSON.stringify({ type: 'UPDATE', payload: payload });
      if (window.unityInstance)
        window.unityInstance.SendMessage('CastReceiver', 'OnMessageReceived', msg);
    }, type, value);
  }

  // Wait for a string to appear in the collected console logs
  async function waitForLog(pattern, timeoutMs = 10000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      if (consoleLogs.some(l => l.includes(pattern))) return true;
      await sleep(200);
    }
    return false;
  }

  const results = [];

  // ── TEST 1: add_fish (not in INIT catalog — requires bundle download from R2) ─
  console.log('TEST 1: add_fish (moorish_idol, loads from R2)...');
  await sendUpdate('add_fish', JSON.stringify({ speciesId: 'fish_moorish_idol', nickname: 'TestFish' }));
  const t1 = await waitForLog('add_fish: fish_moorish_idol', 15000); // bundle download can take a few seconds
  console.log(`  ${t1 ? '✅' : '❌'} add_fish`);
  results.push({ test: 'add_fish', pass: t1 });
  await sleep(300);

  // ── TEST 2: remove_fish ──────────────────────────────────────────────────────
  console.log('TEST 2: remove_fish (moorish_idol)...');
  await sendUpdate('remove_fish', 'fish_moorish_idol');
  const t2 = await waitForLog('remove_fish: fish_moorish_idol');
  console.log(`  ${t2 ? '✅' : '❌'} remove_fish`);
  results.push({ test: 'remove_fish', pass: t2 });
  await sleep(200);

  // ── TEST 3: change_bg ────────────────────────────────────────────────────────
  //
  // ⚠⚠ 2026-08-26 — Este test llevaba MESES en verde sin cambiar el fondo: mandaba
  // `bg_ocean`, que NO existe (los válidos salen de `TankBackground.Presets`), y comprobaba
  // que el receiver hiciera ECO del id. El receiver confirmaba «change_bg: bg_ocean» pase lo
  // que pase, así que el test medía el eco, no el efecto.
  //
  // 🧭 Regla que sale de aquí: **no comprobar que el receiver repita lo que le mandaste**.
  // Aquí se comprueba contra `agua: … (<id>)`, que `PublicarAspectoDelAgua` imprime leyendo
  // `bg.CurrentPresetId` — el estado REAL del fondo, no la intención.
  console.log('TEST 3: change_bg (bg_cave, sync — no bundle)...');
  await sendUpdate('change_bg', 'bg_cave');
  const t3 = await waitForLog('(bg_cave)');
  console.log(`  ${t3 ? '✅' : '❌'} change_bg (verificado contra el estado real del fondo)`);
  results.push({ test: 'change_bg', pass: t3 });
  await sleep(200);

  // ── TEST 4: change_sub ───────────────────────────────────────────────────────
  console.log('TEST 4: change_sub (sub_gravel, sync — no bundle)...');
  await sendUpdate('change_sub', 'sub_gravel');
  const t4 = await waitForLog('change_sub: ') && !consoleLogs.some(l => l.includes('ERR change_sub'));
  console.log(`  ${t4 ? '✅' : '❌'} change_sub`);
  results.push({ test: 'change_sub', pass: t4 });
  await sleep(200);

  // ── TEST 5: change_light ─────────────────────────────────────────────────────
  console.log('TEST 5: change_light (light_blue, sync — no bundle)...');
  await sendUpdate('change_light', 'light_blue');
  const t5 = await waitForLog('change_light: ') && !consoleLogs.some(l => l.includes('ERR change_light'));
  console.log(`  ${t5 ? '✅' : '❌'} change_light`);
  results.push({ test: 'change_light', pass: t5 });
  await sleep(200);

  // ── TEST 6: re-add removed fish (tests reuse of existing catalog entry) ───────
  console.log('TEST 6: re-add banggai (already in INIT catalog, no bundle reload)...');
  await sendUpdate('add_fish', JSON.stringify({ speciesId: 'fish_banggai_cardinalfish', nickname: 'Banggai2' }));
  const t6 = await waitForLog('add_fish: fish_banggai_cardinalfish');
  console.log(`  ${t6 ? '✅' : '❌'} add_fish (catalog reuse)`);
  results.push({ test: 'add_fish_catalog_reuse', pass: t6 });
  await sleep(200);

  // ── TESTS 7-9: un id que NO existe tiene que CANTAR ──────────────────────────
  //
  // Estos tres son la red que faltaba: convierten en fallo lo que durante meses fue un
  // verde. ⚠ Requieren el player construido a partir del 2026-08-26 (el que valida el id
  // contra la lista y responde `ERR ...: id desconocido`). Contra un player anterior fallan
  // A PROPÓSITO: ese player confirma el id fantasma como si lo hubiera aplicado.
  const FANTASMAS = [
    { tipo: 'change_bg',    id: 'bg_ocean'    },   // id-fantasma a propósito
    { tipo: 'change_sub',   id: 'sub_black'   },   // id-fantasma a propósito
    { tipo: 'change_light', id: 'light_green' },   // id-fantasma a propósito
  ];
  for (const f of FANTASMAS) {
    console.log(`TEST ${7 + FANTASMAS.indexOf(f)}: ${f.tipo} con un id inexistente (${f.id}) → tiene que dar ERR...`);
    await sendUpdate(f.tipo, f.id);
    const ok = await waitForLog(`ERR ${f.tipo}: id desconocido '${f.id}'`, 4000);
    console.log(`  ${ok ? '✅' : '❌'} ${f.tipo} rechaza '${f.id}'`
              + (ok ? '' : '   ← ¿player anterior al 2026-08-26? Ese confirma el id fantasma.'));
    results.push({ test: `${f.tipo}_id_inexistente`, pass: ok });
    await sleep(200);
  }

  // ── TESTS 10-12: uid adoptado + emparejamiento ──────────────────────────────
  //
  // Se anaden DOS peces con uid explicito y despues se emparejan. Esto prueba de una vez:
  //   · que la TV ADOPTA el uid del movil en vez de generar el suyo (si no, el pairs no
  //     encontraria a nadie y saldrian 0 cableadas);
  //   · el handler `pairs` completo;
  //   · y de paso la carrera, porque el `pairs` va inmediatamente detras del `add_fish` que
  //     todavia puede estar bajando su bundle.
  const UID_A = 'uid-test-macho', UID_B = 'uid-test-hembra';
  console.log('TEST 10: add_fish x2 con uid explicito del movil...');
  await sendUpdate('add_fish', JSON.stringify({ speciesId: 'fish_moorish_idol', nickname: 'Macho', uid: UID_A }));
  await sleep(400);
  await sendUpdate('add_fish', JSON.stringify({ speciesId: 'fish_goby_firefish', nickname: 'Hembra', uid: UID_B }));
  const t10 = await waitForLog('add_fish: fish_goby_firefish', 15000);
  console.log(`  ${t10 ? '✅' : '❌'} add_fish con uid`);
  results.push({ test: 'add_fish_con_uid', pass: t10 });
  await sleep(300);

  console.log('TEST 11: pairs con los dos uid -> tiene que cablear 1...');
  await sendUpdate('pairs', JSON.stringify({ items: [{ maleUid: UID_A, femaleUid: UID_B }] }));
  const t11 = await waitForLog('pairs: 1 recibidas, 1 cableadas', 6000);
  console.log(`  ${t11 ? '✅' : '❌'} pairs empareja de verdad`
            + (t11 ? '' : '   ← si dice "0 cableadas", el uid del movil NO se esta adoptando'));
  results.push({ test: 'pairs_cablea', pass: t11 });
  await sleep(300);

  console.log('TEST 12: pairs con un uid que no existe -> tiene que DECIRLO...');
  await sendUpdate('pairs', JSON.stringify({ items: [{ maleUid: UID_A, femaleUid: 'uid-que-no-existe' }] }));
  const t12 = await waitForLog('recibidas pero sólo 0 cableadas', 6000);
  console.log(`  ${t12 ? '✅' : '❌'} pairs distingue recibidas de cableadas`);
  results.push({ test: 'pairs_reporta_no_cableadas', pass: t12 });
  await sleep(200);

  await page.screenshot({ path: 'test-updates-result.png' });
  console.log('\nScreenshot: test-updates-result.png');
  await browser.close();

  const passed = results.filter(r => r.pass).length;
  console.log(`\n${'─'.repeat(40)}`);
  console.log(`Result: ${passed}/${results.length} passed`);
  results.forEach(r => console.log(`  ${r.pass ? '✅' : '❌'} ${r.test}`));
  if (passed < results.length) process.exit(1);
})().catch(e => { console.error(e); process.exit(1); });
