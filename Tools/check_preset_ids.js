#!/usr/bin/env node
// Comprueba que TODO id de preset (bg_*, sub_*, light_*) que aparece en el receiver y en las
// herramientas exista de verdad en los arrays de C#. Sin navegador, sin Unity, sin tele.
//
// ⚠⚠ Por qué existe (2026-08-26): había SEIS ids fantasma repartidos por el proyecto —
// `bg_ocean`, `bg_reef`, `bg_sunset`, `sub_black`, `sub_coral` y `light_green`— y ninguno
// daba error: el receiver confirmaba el id que le mandaras aunque no existiera, así que la
// tecla B del devtest no hacía nada en 3 de cada 6 pulsaciones, la S en 2 de cada 4, y
// `test-updates.js` llevaba meses en VERDE mandando `bg_ocean`. Con eso se dio por buena una
// prueba entera el 25-ago.
//
//   node Tools/check_preset_ids.js        → informe, sale 1 si hay fantasmas
//
// Los ids se leen de la FUENTE (los arrays de C#), no de una lista copiada aquí: una lista
// copiada es justo el bug que esto persigue.

const fs = require('fs'), path = require('path');
const raiz = path.join(__dirname, '..');
const L = p => fs.readFileSync(path.join(raiz, p), 'utf8');

const FUENTES = [
  'Assets/Scripts/Tank/TankBackground.cs',
  'Assets/Scripts/Tank/DecorationPlacer.cs',
  'Assets/Scripts/Tank/TankLightingController.cs',
];
const validos = new Set();
for (const f of FUENTES)
  for (const m of L(f).matchAll(/id\s*=\s*"((?:bg|sub|light)_[a-z_]+)"/g)) validos.add(m[1]);

// Ids retirados que el código migra a propósito (un save viejo puede traerlos).
const RETIRADOS = new Set(['light_green']);   // AquariumManager.cs → light_white

const A_REVISAR = [
  'Assets/WebGLTemplates/CastReceiver/index.html',
  'webgl-output/index.html',
  'Tools/test-updates.js',
  'Tools/cast-headless.js',
  'Tools/local-test.js',
  'Tools/grade-tune.js',
];

let fantasmas = 0;
console.log(`ids válidos en C#: ${validos.size}  (${[...validos].sort().join(' ')})\n`);

for (const f of A_REVISAR) {
  let txt; try { txt = L(f); } catch { continue; }
  const malos = new Map();
  txt.split('\n').forEach((linea, i) => {
    // Se saltan los comentarios (ahí los ids se CITAN, no se usan) y las líneas marcadas
    // con `id-fantasma`, que es como se declara un id inexistente A PROPÓSITO: los tests
    // negativos de test-updates.js mandan uno adrede para ver que el receiver lo rechaza.
    const t = linea.trim();
    if (t.startsWith('//') || t.startsWith('*') || linea.includes('id-fantasma')) return;
    // Sólo ids entre comillas: así no saltan los que aparecen en prosa.
    for (const m of linea.matchAll(/['"]((?:bg|sub|light)_[a-z_]+)['"]/g))
      if (!validos.has(m[1]) && !RETIRADOS.has(m[1]))
        malos.set(m[1], (malos.get(m[1]) || []).concat(i + 1));
  });
  if (malos.size === 0) { console.log(`✅ ${f}`); continue; }
  console.log(`❌ ${f}`);
  for (const [id, lineas] of malos) { console.log(`     '${id}' NO existe — línea ${lineas.join(', ')}`); fantasmas++; }
}

// ── Las cifras del contrato, que estan escritas A MANO ───────────────────────
//
// CAST_CONTRACT_TV.md §3 declara "11 fondos / 12 sustratos / 7 luces" a mano, y el repo MOVIL
// depende de esas cifras. Una lista escrita a mano que nadie comprueba es justo el bug que
// persigue este script: si alguien anade un preset y no toca el doc, el contrato queda
// mintiendo EN SILENCIO a otro repo.
// AVISO 2026-08-28: partir por el salto de linea A SECAS deja un retorno de carro al final
// de cada linea si el fichero es CRLF, y entonces la comparacion con el cierre del metodo
// NUNCA corta: el escaner se comia el switch de ApplyAmbientMode y reportaba `sunset` y
// `night` como tipos de UPDATE sin documentar. Contaba 17 en vez de 12.
// Una guarda que grita en falso acaba ignorandose.
const NL = new RegExp(String.fromCharCode(13) + "?" + String.fromCharCode(10));
const CONTRATO = 'CAST_CONTRACT_TV.md';
let desfases = 0;
try {
  const doc = L(CONTRATO);
  const cuenta = pre => [...validos].filter(v => v.startsWith(pre)).length;
  for (const [fila, pre] of [['Fondos', 'bg_'], ['Sustratos', 'sub_'], ['Luces', 'light_']]) {
    // La fila es: | Fondos | **11** | ... — se parte por '|' y se limpia a digitos.
    const row = doc.split(NL).find(l => l.startsWith('| ' + fila + ' '));
    if (!row) { console.log('   ! ' + CONTRATO + ': no encuentro la fila "' + fila + '"'); desfases++; continue; }
    const declarado = Number(row.split('|')[2].replace(/[^0-9]/g, ''));
    const real = cuenta(pre);
    if (declarado !== real) {
      console.log('   ' + CONTRATO + ' dice ' + declarado + ' ' + fila.toLowerCase() + ' y en C# hay ' + real);
      desfases++;
    }
  }
  if (desfases === 0) console.log('OK ' + CONTRATO + ': las cifras de la tabla cuadran con los arrays de C#');
} catch (e) { console.log('   ! ' + CONTRATO + ': no se pudo comprobar (' + e.message + ')'); }

// -- Que ningun tipo de UPDATE se escape del contrato ------------------------
//
// Idea portada de la sesion del repo MOVIL (26-ago), que hizo el test simetrico y le cazo
// un tipo sin documentar A LA PRIMERA -- el mismo dia que lo escribio. Aqui el riesgo es el
// espejo: anadir un `case` al switch de ApplyUpdate y no contarlo en el contrato, con lo que
// el otro repo se queda sin saber que existe.
let sinDocumentar = 0;
try {
  const src = L('Assets/Scripts/Core/TvSceneBootstrap.cs').split(NL);
  const iSwitch = src.findIndex(l => l.includes('switch (upd.type)'));
  if (iSwitch < 0) throw new Error('no encuentro el switch de ApplyUpdate');
  const tipos = [];
  for (let i = iSwitch; i < src.length; i++) {
    if (src[i] === '    }') break;              // fin del metodo ApplyUpdate
    const m = src[i].match(/case "([a-z_]+)":/);
    if (m) tipos.push(m[1]);
  }
  const doc = L(CONTRATO);
  // Se busca una FILA de tabla que mencione el tipo entre backticks, no una cadena exacta:
  // la fila puede llevar negrita o un marcador y seguir siendo documentacion valida. Exigir
  // el formato exacto convierte la guarda en un corrector de estilo y acaba ignorandose.
  const filas = doc.split(NL).filter(l => l.startsWith('|'));
  const faltan = tipos.filter(t => !filas.some(f => f.includes(String.fromCharCode(96) + t + String.fromCharCode(96))));
  if (faltan.length) {
    console.log('   ' + CONTRATO + ' no documenta ' + faltan.length + ' tipo(s) de UPDATE: ' + faltan.join(', '));
    sinDocumentar = faltan.length;
  } else {
    console.log('OK ' + CONTRATO + ': los ' + tipos.length + ' tipos de UPDATE estan documentados');
  }
} catch (e) { console.log('   ! tipos de UPDATE: no se pudo comprobar (' + e.message + ')'); }

console.log(fantasmas === 0
  ? '\nSin ids fantasma.'
  : `\n${fantasmas} id(s) fantasma. Un id que no existe NO da error en runtime: el preset simplemente no cambia.`);
process.exit(fantasmas === 0 && desfases === 0 && sinDocumentar === 0 ? 0 : 1);
