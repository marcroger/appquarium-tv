#!/usr/bin/env node
/**
 * Sender Cast SIN NAVEGADOR — habla el protocolo Cast v2 directamente (TCP+TLS 8009).
 *
 * Sustituye a Tools/sender-video.html + Chrome + el selector nativo de dispositivos,
 * que era el único paso de toda la investigación que exigía un humano delante.
 *
 * Uso:
 *   node Tools/cast-headless.js --rung 2
 *   node Tools/cast-headless.js --rung 23 --ip 192.168.1.33 --duration 600
 *   node Tools/cast-headless.js --stop            (cierra la app en el device)
 *
 * Salida: una línea por evento con el reloj de sesión, y al caer imprime la duración.
 * Código de salida 0 = la sesión cayó (lo normal en esta investigación), 1 = error.
 */

const Client = require('castv2').Client;

// ── args ──────────────────────────────────────────────────────────────────────
const argv = process.argv.slice(2);
function arg(name, def) {
  const i = argv.indexOf('--' + name);
  return i >= 0 && argv[i + 1] && !argv[i + 1].startsWith('--') ? argv[i + 1] : def;
}
const HOST     = arg('ip', '192.168.1.33');
const RUNG     = String(arg('rung', '2'));
const DURATION = parseInt(arg('duration', '600'), 10) * 1000;
const STOP     = argv.includes('--stop');
// --fish N → manda un INIT con un acuario REAL (N peces + fondo + decos).
// Sin este flag el receiver arranca Unity con la escena VACÍA: no carga ni un
// bundle remoto, así que la medida NO representa el coste de memoria real.
const FISH     = parseInt(arg('fish', '0'), 10);

// --update <tipo>=<valor>@<segundo>  (repetible) → manda un UPDATE en tiempo real.
//   --update ambient=night@60      cambia a modo noche al minuto
//   --update feed=@90              dispara comida (valor vacio) a los 90s
//   --update speed=1.8@120
// Sirve para verificar EN EL DEVICE los 11 tipos de UPDATE, que hasta el 2026-08-15
// solo estaban verificados en codigo y en el Chrome local con ?devtest=1.
// --diag → enciende los HUD de FPS/stats del receiver (que van APAGADOS en produccion
// desde el 2026-08-15). Sin esto no se puede leer el FPS por screencap.
const DIAG = argv.includes('--diag');

// --decos a,b,c  -> sustituye la lista fija de decos por las indicadas (itemIds).
// Se colocan repartidas en el suelo. Sirve para aislar UNA deco y compararla antes/despues
// de tocarla, que es como se valida un cambio de formato sin engañarse.
const DECOS_OVERRIDE = (function () {
  const i = argv.indexOf('--decos');
  if (i < 0 || !argv[i + 1]) return null;
  const ids = argv[i + 1].split(',').map(s => s.trim()).filter(Boolean);
  const n = ids.length;
  return ids.map((id, k) => ({
    instanceId: id + '_0',
    itemId: id,
    // repartidas simetricamente respecto al centro
    position: { x: n === 1 ? 0 : -2.6 + (5.2 * k) / (n - 1), y: -2.8, z: 2.0 },
    scaleFactor: 1.0, flipped: false, rotationY: 0,
  }));
})();

const UPDATES = argv.reduce((acc, a, i) => {
  if (a !== '--update' || !argv[i + 1]) return acc;
  const m = /^([a-z_]+)=(.*)@(\d+)$/.exec(argv[i + 1]);
  if (!m) { console.log(`[WARN] --update ignorado (formato tipo=valor@segundos): ${argv[i + 1]}`); return acc; }
  acc.push({ type: m[1], value: m[2], at: parseInt(m[3], 10) * 1000 });
  return acc;
}, []);

// --raw <TIPO>=<payload JSON>@<segundo>  (repetible) → manda un CastMessage cualquiera.
//   --raw 'GRADE={"bgShader":"urp"}@40'
// Existe para poder conmutar cosas EN LA MISMA SESIÓN de la tele en vez de gastar un build por
// variante. `--update` sólo sirve para los UPDATE del protocolo; esto manda el mensaje crudo.
const RAWS = argv.reduce((acc, a, i) => {
  if (a !== '--raw' || !argv[i + 1]) return acc;
  const m = /^([A-Z_]+)=(.*)@(\d+)$/.exec(argv[i + 1]);
  if (!m) { console.log(`[WARN] --raw ignorado (formato TIPO=payload@segundos): ${argv[i + 1]}`); return acc; }
  acc.push({ type: m[1], payload: m[2], at: parseInt(m[3], 10) * 1000 });
  return acc;
}, []);

const APP_ID = '8F6C873F';

// ⚠ Guardas contra mediciones falsas. Las dos que van aqui YA han mordido:
//   · SPECIES.slice(0, FISH) capaba en silencio -> `--fish 25` medía 12 y el numero
//     parecia bueno. Ahora hay 25 especies, pero `--fish 40` volveria a capar callando.
//   · RUNG_CONFIG: el receiver LIMPIO desplegado el 2026-08-15 ya no tiene handler, asi
//     que todos los escalones miden lo mismo. `--rung 23` NO apaga el video keepalive y
//     `--rung 1` SI arranca Unity, pero el log sigue imprimiendo "RUNG_CONFIG enviado".
//     Solo los receivers de diagnostico (Tools/rcv-*.html) lo entienden.
function abortar(motivo) { console.error('ABORTA: ' + motivo); process.exit(1); }
const NS_APP = 'urn:x-cast:dev.unknownaerials.appquarium';   // canal de diag del receiver
const NS_CONN = 'urn:x-cast:com.google.cast.tp.connection';
const NS_HEART = 'urn:x-cast:com.google.cast.tp.heartbeat';
const NS_RECV = 'urn:x-cast:com.google.cast.receiver';

// ── mapa escalón → RUNG_CONFIG (espejo de Tools/sender-video.html) ────────────
function cfgForRung(rn) {
  return {
    type: 'RUNG_CONFIG',
    unity:        ['2','3','4','5','6','7','16','18','23'].includes(rn),
    kaBig:        rn === '3',
    killAudio:    rn === '4',
    verbose:      rn === '5',
    throttleRaf:  rn === '6',
    quitUnity:    rn === '7',
    stall:        rn === '8',
    delayedUnity: rn === '9',
    netLoad:      rn === '10',
    rawWebGL:     rn === '11',
    gpuHeavy:     rn === '12',
    coreSaturate: rn === '13',
    wasmCompile:  rn === '14',
    wasmMem:      rn === '15',
    spyInit:      rn === '16',
    unityCtx:     rn === '17',
    glSpy:        rn === '18',
    glFence:      rn === '19',
    glFbo:        rn === '20',
    glCombo:      rn === '21',
    noKa:         rn === '23',
  };
}

// Claves reales de los bundles en R2 (ServerData/WebGL/fish_remote_assets_*).
const SPECIES = [
  'fish_banggai_cardinalfish', 'fish_moorish_idol', 'fish_mandarinfish',
  'fish_angelfish_emperor', 'fish_goby_firefish', 'fish_parrotfish',
  'fish_boxfish_yellow', 'fish_filefish_orange', 'fish_angelfish_queen',
  'fish_klunzinger_wrasse', 'fish_black_durgon', 'fish_soldierfish_redcoat',
  // 2026-08-15 — las 13 que faltaban. Antes la lista tenia 12 y `SPECIES.slice(0, FISH)`
  // capaba EN SILENCIO: pedir --fish 25 medía 12 y el numero parecia bueno por error.
  'fish_angelfish_blueface', 'fish_angelfish_blueline', 'fish_angelfish_clarion',
  'fish_angelfish_flagfin', 'fish_angelfish_goldflake', 'fish_angelfish_majestic',
  'fish_angelfish_multibarred', 'fish_angelfish_queensland', 'fish_angelfish_scribbled',
  'fish_aweoweo', 'fish_filefish_scrawled', 'fish_napoleon_wrasse',
  'fish_sunfish_molamola',
];
// Decos repartidas por el tanque — el formato lo consume DecorationPlacer.
// ⚠ La clave Addressable NO sale de `itemId`: TvSceneBootstrap.ParseDecoItemIds()
//   la deriva del `instanceId` quitándole el sufijo `_N` (ver línea ~416). Un
//   instanceId sin guion bajo ⇒ InvalidKeyException y 0 decos colocadas.
// ⚠ Claves verificadas contra ServerData/WebGL/decos_remote_assets_*.bundle.
const DECOS = [
  { instanceId: 'deco_anchor_0',            itemId: 'deco_anchor',            position: { x: -2.6, y: -2.8, z: 2.0 }, scaleFactor: 1.0, flipped: false, rotationY:   0 },
  { instanceId: 'deco_coral_acropora_0',    itemId: 'deco_coral_acropora',    position: { x: -1.3, y: -2.8, z: 2.0 }, scaleFactor: 1.0, flipped: false, rotationY:  30 },
  { instanceId: 'deco_coral_pocillopora_0', itemId: 'deco_coral_pocillopora', position: { x:  0.0, y: -2.8, z: 2.0 }, scaleFactor: 0.9, flipped: true,  rotationY:  90 },
  { instanceId: 'deco_rock_hq_1_0',         itemId: 'deco_rock_hq_1',         position: { x:  1.3, y: -2.8, z: 2.0 }, scaleFactor: 1.1, flipped: false, rotationY: 150 },
  { instanceId: 'deco_shell_tridacna_0',    itemId: 'deco_shell_tridacna',    position: { x:  2.4, y: -2.8, z: 2.0 }, scaleFactor: 0.8, flipped: false, rotationY: 200 },
  { instanceId: 'deco_starfish_blue_0',     itemId: 'deco_starfish_blue',     position: { x:  3.2, y: -2.8, z: 2.0 }, scaleFactor: 0.7, flipped: false, rotationY: 260 },
];

if (!STOP && FISH > SPECIES.length) {
  abortar(`pediste --fish ${FISH} y solo hay ${SPECIES.length} especies; se mediria ${SPECIES.length} EN SILENCIO.`);
}
if (!STOP && RUNG !== '2' && !argv.includes('--allow-noop-rung')) {
  abortar(`--rung ${RUNG} no hace NADA contra el receiver de produccion (no tiene handler de ` +
          `RUNG_CONFIG desde el 2026-08-15). La tanda mediria lo mismo que --rung 2 pero con ` +
          `otra etiqueta. Usa --allow-noop-rung si estas casteando un receiver de diagnostico.`);
}

const t0 = Date.now();
const el = () => ((Date.now() - t0) / 1000).toFixed(1).padStart(6);
const log = (m) => console.log(`[${el()}s] ${m}`);

let requestId = 1;
let finished = false;
function finish(code, why) {
  if (finished) return;
  finished = true;
  const secs = ((Date.now() - t0) / 1000).toFixed(1);
  log(`══ FIN: ${why} ══`);
  log(`══ DURACIÓN DE SESIÓN: ${secs}s ══`);
  process.exit(code);
}

const client = new Client();
client.on('error', (err) => { log('ERROR de cliente: ' + err.message); finish(1, 'error de transporte'); });
client.on('close', () => finish(0, 'el device cerró la conexión'));

log(`conectando a ${HOST}:8009 …`);
client.connect(HOST, () => {
  log('conectado (TLS)');

  const connection = client.createChannel('sender-0', 'receiver-0', NS_CONN, 'JSON');
  const heartbeat  = client.createChannel('sender-0', 'receiver-0', NS_HEART, 'JSON');
  const receiver   = client.createChannel('sender-0', 'receiver-0', NS_RECV, 'JSON');

  connection.send({ type: 'CONNECT' });
  const hb = setInterval(() => heartbeat.send({ type: 'PING' }), 5000);
  hb.unref && hb.unref();

  if (STOP) {
    receiver.send({ type: 'GET_STATUS', requestId: requestId++ });
    receiver.on('message', (data) => {
      const app = (data.status && data.status.applications || [])[0];
      if (app) {
        receiver.send({ type: 'STOP', sessionId: app.sessionId, requestId: requestId++ });
        log('STOP enviado a ' + app.displayName);
        setTimeout(() => finish(0, 'app detenida'), 1500);
      } else { finish(0, 'no había ninguna app corriendo'); }
    });
    return;
  }

  log(`lanzando app ${APP_ID} (escalón ${RUNG}) …`);
  receiver.send({ type: 'LAUNCH', appId: APP_ID, requestId: requestId++ });

  let joined = false;
  receiver.on('message', (data) => {
    if (data.type === 'LAUNCH_ERROR') { log('LAUNCH_ERROR: ' + JSON.stringify(data)); return finish(1, 'el device rechazó el lanzamiento'); }
    if (!data.status || !data.status.applications) return;
    const app = data.status.applications.find(a => a.appId === APP_ID);
    if (!app || joined) return;
    joined = true;

    log(`app viva: ${app.displayName} · session=${app.sessionId.slice(0, 8)} · transport=${app.transportId.slice(0, 8)}`);

    // conexión virtual con la app + canal de diagnóstico del receiver
    const appConn = client.createChannel('sender-0', app.transportId, NS_CONN, 'JSON');
    const appChan = client.createChannel('sender-0', app.transportId, NS_APP, 'JSON');
    appConn.send({ type: 'CONNECT' });

    appChan.on('message', (msg) => {
      const d = (typeof msg === 'string') ? safeParse(msg) : msg;
      if (d && d.type === 'LOG') log('RCV ' + d.line);
      else log('RCV(?) ' + JSON.stringify(msg).slice(0, 200));
    });

    // ⚠ El cliente TLS NO siempre emite 'close' cuando el renderer se estrella: la
    //   conexión de transporte sigue viva y el sender se quedaba colgado hasta el
    //   límite (bug del primer run autónomo). Sondeamos el estado del receiver: si
    //   nuestra app desaparece de la lista de aplicaciones, la sesión ha muerto.
    const poll = setInterval(() => {
      try { receiver.send({ type: 'GET_STATUS', requestId: requestId++ }); } catch (e) {}
    }, 4000);
    poll.unref && poll.unref();
    let ausencias = 0;
    receiver.on('message', (data) => {
      if (!data.status) return;
      // ⚠ Hacen falta DOS lecturas seguidas sin la app antes de dar la sesion por caida.
      // Un RECEIVER_STATUS transitorio (cambios de volumen, estados intermedios) puede
      // llegar sin la clave `applications` y cortaba la tanda al instante, quedando
      // registrada como una caida real: generador silencioso de duraciones falsas.
      const apps = data.status.applications || [];
      if (joined && !apps.find(a => a.appId === APP_ID)) {
        ausencias++;
        if (ausencias >= 2) {
          clearInterval(poll);
          finish(0, 'la app ha desaparecido del device (sesión caída)');
        }
      } else {
        ausencias = 0;
      }
    });
    appConn.on('message', (msg) => {
      if (msg && msg.type === 'CLOSE') finish(0, 'el receiver cerró la conexión virtual');
    });

    // El receiver difiere Unity hasta recibir esto. Se manda 3× (mismo criterio que
    // el sender HTML: el canal puede no estar listo en el primer intento).
    const cfg = cfgForRung(RUNG);
    log('RUNG_CONFIG → ' + Object.keys(cfg).filter(k => cfg[k] === true).join(' ') + (RUNG === '1' ? ' (ninguno)' : ''));
    let sent = 0;
    const send = () => {
      try { appChan.send(cfg); log(`RUNG_CONFIG enviado (#${++sent})`); }
      catch (e) { log('fallo al enviar RUNG_CONFIG: ' + e.message); }
    };
    send();
    setTimeout(send, 700);
    setTimeout(send, 1800);

    // ── estado del acuario (INIT) ────────────────────────────────────────────
    // Formato = CastMessage { type:'INIT', payload:<TvAquariumState en JSON> }
    // (ver Assets/Scripts/Core/CastDataTypes.cs y CastReceiver.cs:42).
    // Se manda tras el RUNG_CONFIG para que Unity ya esté arrancando.
    if (FISH > 0) {
      setTimeout(() => {
        const state = {
          activeFish: SPECIES.slice(0, FISH).map((s, i) => ({
            speciesId: s, nickname: 'test' + i, ageScale: 1.0,
          })),
          decoJson: JSON.stringify({ items: DECOS_OVERRIDE || DECOS }),
          bgId: 'bg_tropical',
          subId: 'sub_sand',
          lightId: 'light_white',
          ambientMode: 'day',
          fishSpeed: 1.0,
          // ⚠ tank_l + y:-2.8 van EN PAREJA. La Y del suelo sale de _tankBounds
          //   (DecorationPlacer.BuildFloorVisual), asi que depende del tanque: cambiar
          //   uno sin el otro vuelve a dejar las decos flotando. Este par es el unico
          //   verificado (es el que usa el ?devtest=1 del receiver, visto apoyando bien).
          // ⚠ Las tandas de estabilidad 4/4 del 2026-08-11 se midieron con tank ''.
          //   Si se compara con ellas, tenerlo en cuenta.
          selectedTankId: 'tank_l',
          tankHalfWidth: 0.0,
        };
        try {
          appChan.send({ type: 'INIT', payload: JSON.stringify(state) });
          log(`INIT enviado → ${state.activeFish.length} peces · ${(DECOS_OVERRIDE || DECOS).length} decos${DECOS_OVERRIDE ? ' (override: ' + DECOS_OVERRIDE.map(d => d.itemId).join(',') + ')' : ''} · ${state.bgId}`);
        } catch (e) { log('fallo al enviar INIT: ' + e.message); }
      }, 3000);
    } else {
      log('⚠ sin --fish: la escena quedará VACÍA (no se carga ningún bundle remoto)');
    }

    if (DIAG) {
      setTimeout(() => {
        try {
          appChan.send({ type: 'DIAG', value: 'on' });
          log('DIAG enviado → HUD de FPS/stats encendidos en el receiver');
        } catch (e) { log('fallo al enviar DIAG: ' + e.message); }
      }, 3500);
    }

    // ── UPDATEs programados ──────────────────────────────────────────────────
    // Formato = CastMessage { type:'UPDATE', payload:<TvUpdateMessage en JSON> }
    // (ver CAST_UPDATES.md y TvSceneBootstrap.ApplyUpdate).
    for (const r of RAWS) {
      setTimeout(() => {
        try {
          appChan.send({ type: r.type, payload: r.payload });
          log(`RAW ${r.type} enviado → ${r.payload}`);
        } catch (e) { log(`[WARN] RAW ${r.type} fallo: ${e.message}`); }
      }, r.at);
    }
    if (RAWS.length) log(`${RAWS.length} RAW(s) programados: ` + RAWS.map(r => `${r.type}@${r.at / 1000}s`).join(' '));

    for (const u of UPDATES) {
      setTimeout(() => {
        try {
          appChan.send({ type: 'UPDATE', payload: JSON.stringify({ type: u.type, value: u.value }) });
          log(`UPDATE enviado → ${u.type}${u.value ? '=' + u.value : ''}`);
        } catch (e) { log(`fallo al enviar UPDATE ${u.type}: ` + e.message); }
      }, u.at);
    }
    if (UPDATES.length) log(`${UPDATES.length} UPDATE(s) programados: ` +
      UPDATES.map(u => `${u.type}@${u.at / 1000}s`).join(' '));
  });

  // ⚠ Al acabar por LÍMITE la sesión sigue VIVA en el device: hay que pararla
  //   explícitamente o el receiver se queda en pantalla indefinidamente (pasó el
  //   2026-07-27: 35 min de cubo verde en la TV tras un run de 11 minutos).
  setTimeout(() => {
    try { receiver.send({ type: 'STOP', requestId: requestId++ }); log('STOP enviado al device (fin por límite)'); } catch (e) {}
    setTimeout(() => finish(0, `límite de ${DURATION / 1000}s alcanzado sin caída`), 1500);
  }, DURATION);
});

function safeParse(s) { try { return JSON.parse(s); } catch (e) { return null; } }
