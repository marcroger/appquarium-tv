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
  // ⚠⚠ 2026-08-27 — `waitForLog` mira TODO el log acumulado desde el arranque, asi que una
  // linea de un test ANTERIOR da el test por bueno sin que haya pasado nada. No es teorico:
  // el TEST 16 espera «remove_fish: fish_moorish_idol por especie» y el TEST 2 ya la imprime,
  // y el TEST 15 espera «0 cableadas», que el TEST 12 ya dejo escrita. Es la misma familia de
  // trampa que tuvo este fichero meses en verde con `bg_ocean`.
  //
  // `desde()` marca el punto del log y `waitForLog(pat, ms, marca)` solo mira de ahi en
  // adelante. Los tests viejos siguen llamando sin marca — su comportamiento no cambia.
  const desde = () => consoleLogs.length;

  async function waitForLog(pattern, timeoutMs = 10000, marca = 0) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      if (consoleLogs.slice(marca).some(l => l.includes(pattern))) return true;
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

  // ── remove_fish por uid (2026-08-27) ─────────────────────────────────────────
  // Llegados aqui el tanque tiene a UID_A (moorish idol) y UID_B (goby firefish), y el
  // TEST 12 dejo `activePairs` con una pareja rota a proposito. Los tests de abajo se
  // apoyan en ese estado — si se reordenan, revisar.

  console.log('TEST 13: remove_fish con un uid que NO existe -> no debe quitar nada...');
  const m13 = desde();
  await sendUpdate('remove_fish', JSON.stringify({ uid: 'uid-que-no-existe' }));
  const t13 = await waitForLog("ERR remove_fish: uid 'uid-que-no-existe' no esta en el tanque", 6000, m13);
  console.log(`  ${t13 ? '✅' : '❌'} remove_fish no cae al camino de la especie`
            + (t13 ? '' : '   ← si quita un pez cualquiera, es EL fallo que esto arregla'));
  results.push({ test: 'remove_fish_uid_inexistente', pass: t13 });
  await sleep(300);

  console.log('TEST 14: remove_fish por uid -> tiene que quitar ESE pez...');
  const m14 = desde();
  await sendUpdate('remove_fish', JSON.stringify({ uid: UID_B, speciesId: 'fish_goby_firefish' }));
  const t14 = await waitForLog(`remove_fish: fish_goby_firefish uid=${UID_B}`, 6000, m14);
  console.log(`  ${t14 ? '✅' : '❌'} remove_fish por uid`);
  results.push({ test: 'remove_fish_por_uid', pass: t14 });
  await sleep(300);

  // El pez de UID_B ya no esta, asi que su entrada del save tampoco deberia: si `pairs`
  // vuelve a mandar la pareja, tiene que reportar 0 cableadas y NO resucitar nada.
  console.log('TEST 15: el save olvido al pez -> pairs con su uid da 0 cableadas...');
  const m15 = desde();
  await sendUpdate('pairs', JSON.stringify({ items: [{ maleUid: UID_A, femaleUid: UID_B }] }));
  const t15 = await waitForLog('recibidas pero sólo 0 cableadas', 6000, m15);
  console.log(`  ${t15 ? '✅' : '❌'} el pez quitado ya no cablea`);
  results.push({ test: 'remove_fish_limpia_el_save', pass: t15 });
  await sleep(300);

  // Camino viejo: cadena suelta con la especie. Tiene que seguir funcionando (aditivo) pero
  // decir por donde fue, para que nadie lea el log y crea que quito el pez que pidio.
  console.log('TEST 16: remove_fish con una cadena suelta (cliente viejo)...');
  const m16 = desde();
  await sendUpdate('remove_fish', 'fish_moorish_idol');
  const t16 = await waitForLog('remove_fish: fish_moorish_idol por especie', 6000, m16);
  console.log(`  ${t16 ? '✅' : '❌'} remove_fish por especie sigue vivo y se identifica`);
  results.push({ test: 'remove_fish_por_especie_aun_vale', pass: t16 });
  await sleep(200);

  // ── add_deco con giro e inclinacion (2026-08-27) ─────────────────────────────
  // Hasta hoy `add_deco` sólo llevaba 6 campos y PERDIA el giro, la inclinacion y el montaje:
  // tras la primera rotacion la verdad vive en el cuaternion `DecoPlacement.userRot`, no en
  // `rotationY`. Lo encontro la sesion del repo movil. Aqui se comprueba que el receiver los
  // aplica Y que lo dice — el log reporta lo APLICADO, no lo recibido.

  console.log('TEST 17: add_deco con cuaternion + tilt -> el log tiene que decir que los aplico...');
  const m17 = desde();
  await sendUpdate('add_deco', JSON.stringify({
    instanceId: 'deco_anchor_test_rot', itemId: 'deco_anchor',
    position: { x: -1.2, y: -2.8, z: 2.0 }, scaleFactor: 1.0, flipped: false, rotationY: 0,
    tiltX: 12, hasUserRot: true, quatX: 0, quatY: 0.3826834, quatZ: 0, quatW: 0.9238795,
  }));
  const t17 = await waitForLog('+rot', 20000, m17);
  const t17b = await waitForLog('+tilt', 20000, m17);
  console.log(`  ${t17 && t17b ? '✅' : '❌'} add_deco aplica y reporta giro e inclinacion`
            + (t17 && t17b ? '' : '   <- si falta, el payload no lleva los campos nuevos'));
  results.push({ test: 'add_deco_rot_y_tilt', pass: t17 && t17b });
  await sleep(400);

  // Negativo: sin los campos nuevos, el log NO debe decir "+rot". Sin esto, un log que
  // imprimiera "+rot" siempre pasaria el test de arriba sin aplicar nada.
  console.log('TEST 18: add_deco SIN giro -> el log NO debe decir "+rot"...');
  const m18 = desde();
  await sendUpdate('add_deco', JSON.stringify({
    instanceId: 'deco_anchor_test_plano', itemId: 'deco_anchor',
    position: { x: 1.2, y: -2.8, z: 2.0 }, scaleFactor: 1.0, flipped: false, rotationY: 0,
  }));
  const colocada18 = await waitForLog('add_deco: deco_anchor', 20000, m18);
  const sinRot = colocada18 && !consoleLogs.slice(m18).some(l => l.includes('+rot'));
  console.log(`  ${sinRot ? '✅' : '❌'} el log distingue con giro de sin giro`);
  results.push({ test: 'add_deco_sin_rot_no_miente', pass: sinRot });
  await sleep(200);

  // ── dump: el volcado del estado montado (2026-08-27) ────────────────────────
  // Es la herramienta con la que se va a comparar la tele contra el movil, asi que tiene que
  // volcar de verdad: cabecera, una linea por pez, una por deco, y cierre con las cuentas.
  // ⚠ No basta con que responda: se exige que el numero de decos del cierre cuadre con las
  // lineas `DUMP deco` que ha impreso. Un volcado que diga "decos=4" y liste 2 seria peor que
  // ninguno, porque se usa para decidir si las dos pantallas coinciden.

  console.log('TEST 19: dump vuelca el estado montado y sus cuentas cuadran...');
  const m19 = desde();
  await sendUpdate('dump', '');
  const abre  = await waitForLog('DUMP ini', 8000, m19);
  const cierra = await waitForLog('DUMP fin', 8000, m19);
  const lineas = consoleLogs.slice(m19);
  const nDecos = lineas.filter(l => l.includes('DUMP deco ')).length;
  const nPeces = lineas.filter(l => l.includes('DUMP pez ')).length;
  const fin    = lineas.find(l => l.includes('DUMP fin')) || '';
  const mDecos = /decos=(\d+)/.exec(fin), mPeces = /peces=(\d+)/.exec(fin);
  const cuadra = !!(mDecos && mPeces
                    && parseInt(mDecos[1], 10) === nDecos
                    && parseInt(mPeces[1], 10) === nPeces);
  console.log(`  ${abre && cierra && cuadra ? '✅' : '❌'} dump vuelca y cuadra`
            + `  (lineas: ${nPeces} peces / ${nDecos} decos · cierre: ${fin.split('DUMP fin')[1] || '-'})`);
  results.push({ test: 'dump_vuelca_y_cuadra', pass: abre && cierra && cuadra });
  await sleep(200);

  // ⚠⚠ Y que sea PARSEABLE, que es distinto de que responda. La primera version formateaba
  // con la cultura del sistema y salia `pos=(4,12,-1,87,0,54)`: con coma decimal Y coma
  // separadora no hay forma de saber donde acaba un numero. El test de arriba pasaba en verde
  // igual, porque contaba lineas. Esto exige punto decimal y el numero exacto de campos.
  console.log('TEST 20: el volcado se puede parsear (punto decimal, no coma)...');
  const RE_PEZ  = /DUMP pez \S+ \S+ escala=-?\d+\.\d{3} pos=\(-?\d+\.\d{2},-?\d+\.\d{2},-?\d+\.\d{2}\) pareja=\S+/;
  const RE_DECO = /DUMP deco \S+ \S+ pos=\(-?\d+\.\d{2},-?\d+\.\d{2},-?\d+\.\d{2}\) escala=-?\d+\.\d{3} flip=[01] quat=\(-?\d+\.\d{3},-?\d+\.\d{3},-?\d+\.\d{3},-?\d+\.\d{3}\)/;
  const pezOk  = lineas.some(l => RE_PEZ.test(l));
  const decoOk = lineas.some(l => RE_DECO.test(l));
  console.log(`  ${pezOk && decoOk ? '✅' : '❌'} lineas parseables (pez:${pezOk} deco:${decoOk})`
            + (pezOk && decoOk ? '' : '   <- ¿coma decimal? falta InvariantCulture'));
  results.push({ test: 'dump_es_parseable', pass: pezOk && decoOk });
  await sleep(200);

  // ⚠⚠ Y —idea de la sesion del repo movil, que es mejor que la mia— el patron tiene que
  // RECHAZAR el formato viejo. Un regex que acepta las dos formas no fija nada: el dia que
  // alguien rompa el formato hacia algo que el patron tolere por casualidad, el test sigue en
  // verde. Aqui la comprobacion iba fuera del test (se verifico a mano que fallaba contra el
  // player anterior); asi queda dentro y no depende de que nadie se acuerde.
  console.log('TEST 21: el patron RECHAZA el formato con coma decimal...');
  const VIEJO_PEZ  = 'DUMP pez abc fish_x escala=0,750 pos=(4,12,-1,87,0,54) pareja=-';
  const VIEJO_DECO = 'DUMP deco d0 deco_x pos=(4,12,-1,87,0,54) escala=1,000 flip=0 quat=(0,000,0,383,0,000,0,924)';
  const rechaza = !RE_PEZ.test(VIEJO_PEZ) && !RE_DECO.test(VIEJO_DECO);
  console.log(`  ${rechaza ? '✅' : '❌'} el patron no traga el formato viejo`);
  results.push({ test: 'dump_regex_rechaza_el_viejo', pass: rechaza });
  await sleep(100);

  // ── Los cuatro que no estaban cubiertos EN NINGUN SITIO (2026-08-27) ─────────
  // Salio de contar, tipo a tipo, cuales tenian prueba: `speed`, `feed`, `startle` y
  // `remove_deco` no la tenian ni aqui ni en ninguna tanda contra la tele. Los handlers
  // llevaban meses en el player y nadie habia comprobado que hicieran algo.
  // 🧭 Todos comprueban el EFECTO (nº de peces afectados, ok=True/False), no el eco.

  console.log('TEST 22: speed aplica y reporta a cuantos peces...');
  const m22 = desde();
  await sendUpdate('speed', '1.8');
  const t22 = await waitForLog('speed: x1.80 aplicado a', 6000, m22);
  console.log(`  ${t22 ? '✅' : '❌'} speed`);
  results.push({ test: 'speed', pass: t22 });
  await sleep(300);

  console.log('TEST 23: speed con un valor ilegible -> ERR...');
  const m23 = desde();
  await sendUpdate('speed', 'no-soy-un-numero');
  const t23 = await waitForLog("ERR speed: valor ilegible 'no-soy-un-numero'", 6000, m23);
  console.log(`  ${t23 ? '✅' : '❌'} speed rechaza basura`);
  results.push({ test: 'speed_valor_ilegible', pass: t23 });
  await sleep(300);

  console.log('TEST 24: feed y startle confirman con el nº de peces...');
  const m24 = desde();
  await sendUpdate('feed', '');
  const t24a = await waitForLog('feed: comida soltada', 6000, m24);
  await sendUpdate('startle', '');
  const t24b = await waitForLog('peces espantados desde', 6000, m24);
  console.log(`  ${t24a && t24b ? '✅' : '❌'} feed:${t24a} startle:${t24b}`);
  results.push({ test: 'feed_y_startle', pass: t24a && t24b });
  await sleep(300);

  // remove_deco: el positivo usa la deco que planto el TEST 18, y el negativo exige ok=False.
  // Sin el negativo, un handler que dijera "ok=True" siempre pasaria el positivo.
  console.log('TEST 25: remove_deco distingue quitar de no encontrar...');
  const m25 = desde();
  await sendUpdate('remove_deco', 'deco_anchor_test_plano');
  const t25a = await waitForLog('remove_deco: deco_anchor_test_plano (ok=True)', 8000, m25);
  await sendUpdate('remove_deco', 'deco_que_no_existe_0');
  const t25b = await waitForLog('remove_deco: deco_que_no_existe_0 (ok=False)', 8000, m25);
  console.log(`  ${t25a && t25b ? '✅' : '❌'} remove_deco (quita:${t25a} no-existe:${t25b})`);
  results.push({ test: 'remove_deco', pass: t25a && t25b });
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
