// Prueba de LOGICA de las frases de la pantalla de carga (2026-08-28).
//   node Tools/test-frases.js
//
// POR QUE: `node --check` sólo dice que el JS parsea. Esto ejercita el reparto por tipo, la
// ausencia de repeticiones y la personalizacion, con INITs reales, con basura y con el reloj
// movido — que es donde se cazó el `_fraseT0 && …` que cortocircuitaba con reloj 0.
//
// ⚠ Lee el bloque del `webgl-output/index.html` DESPLEGABLE, no una copia: si el bloque cambia
//   de nombre o desaparece, el test falla en vez de probar aire.
const fs = require('fs');

const html = fs.readFileSync('D:/dev/appquarium-tv-unity/webgl-output/index.html', 'utf8');
const ini = html.indexOf('// ══ FRASES DE LA PANTALLA DE CARGA');
const fin = html.indexOf('function dbg(msg) {', ini);
if (ini < 0 || fin < 0) { console.error('no encuentro el bloque de frases'); process.exit(1); }
const bloque = html.slice(ini, fin);

// Stubs mínimos. `document` devuelve un elemento falso para poder ejercitar _siguienteFrase,
// y `setTimeout` ejecuta YA (el bloque lo usa sólo para el fundido).
let logs = [];
const sandbox = `
  var dbg = function (m) { logs.push(m); };
  var _el = { style: {}, textContent: '' };
  var document = { getElementById: function () { return _el; } };
  var setTimeout = function (f) { f(); };
  var setInterval = function () { return 1; };
  var clearInterval = function () {};
  var _ahora = 0; var mono = function () { return _ahora; };
`;
const api = new Function('logs', sandbox + bloque + `
  return { leer: _leerAcuarioParaFrases, personales: _personalizadas, arranca: _arrancarFrases,
           siguiente: _siguienteFrase, el: _el, FRASES: FRASES, PLANT: PLANTILLAS,
           avanza: function (ms) { _ahora += ms; },
           reset: function () { _colas = {}; } };
`)(logs);

let fallos = 0;
const ok = (n, c, extra) => { console.log((c ? '  OK   ' : '  FALLA') + ' ' + n + (extra ? '  → ' + extra : '')); if (!c) fallos++; };

const estado = {
  activeFish: [
    { speciesId: 'fish_banggai_cardinalfish', nickname: 'Dori' },
    { speciesId: 'fish_moorish_idol',         nickname: 'Gill' },
    { speciesId: 'fish_goby_firefish',        nickname: '' },
    { speciesId: 'fish_parrotfish',           nickname: '   ' },
    { speciesId: 'fish_boxfish_yellow',       nickname: 'fish_boxfish_yellow' },
    { speciesId: 'fish_black_durgon',         nickname: 'Puas' },
    { speciesId: 'fish_angelfish_queen',      nickname: 'Reina' }
  ]
};

// ── 1) Lectura del INIT ──────────────────────────────────────────────────────
logs.length = 0;
api.leer(JSON.stringify({ type: 'INIT', payload: JSON.stringify(estado) }));
ok('lee 7 peces y 4 motes válidos', /7 peces, 4 con mote/.test(logs.join('|')), logs.join(' | '));
let per = api.personales();
ok('NO cuela el mote vacío ni el speciesId', !per.some(f => /\{n\}/.test(f) || f.indexOf('fish_') >= 0));
ok('usa la cuenta real (7)', per.some(f => f.indexOf('7 peces') >= 0));
ok('máximo 3 peces distintos en las personalizadas',
   ['Dori', 'Gill', 'Puas', 'Reina'].filter(n => per.some(f => f.indexOf(n) >= 0)).length <= 3,
   per.length + ' personalizadas');

// ── 2) ⭐ EL REPARTO POR TIPO (la queja del user: «todas personalizadas») ─────
api.reset();
const salidas = [];
for (let i = 0; i < 200; i++) { api.siguiente(); salidas.push(api.el.textContent); }
const esPersonal = t => ['Dori', 'Gill', 'Puas', 'Reina'].some(n => t.indexOf(n) >= 0) || / \d+ peces/.test(t);
const pct = Math.round(100 * salidas.filter(esPersonal).length / salidas.length);
ok('las personalizadas NO dominan (entre 20 % y 50 %)', pct >= 20 && pct <= 50, pct + ' %');
const esInfo = t => api.FRASES.es.info.indexOf(t) >= 0;
ok('salen también frases de info', salidas.some(esInfo));
const esAmb = t => api.FRASES.es.ambiente.indexOf(t) >= 0;
ok('salen también frases de ambiente', salidas.some(esAmb));

// ── 3) ⭐ SIN REPETIR (la otra queja) ────────────────────────────────────────
// En una carga real caben ~6 frases (40 s / 7,5 s). Ninguna debe repetirse.
api.reset();
const seis = [];
for (let i = 0; i < 6; i++) { api.siguiente(); seis.push(api.el.textContent); }
ok('en 6 frases seguidas no se repite ninguna', new Set(seis).size === 6, seis.length + ' → ' + new Set(seis).size + ' distintas');
api.reset();
const doce = [];
for (let i = 0; i < 12; i++) { api.siguiente(); doce.push(api.el.textContent); }
ok('en 12 frases seguidas tampoco', new Set(doce).size === 12, new Set(doce).size + ' distintas');

// ── 4) Basura y acuario vacío: no revienta y lo DICE ─────────────────────────
logs.length = 0;
api.leer('esto no es json');
ok('con basura avisa en el log', /no pude leer/.test(logs.join('|')));
api.reset(); api.siguiente();
ok('con basura sigue saliendo una frase genérica', api.el.textContent.length > 3, api.el.textContent);

logs.length = 0;
api.leer(JSON.stringify({ type: 'INIT', payload: JSON.stringify({ activeFish: [] }) }));
api.reset();
const vacio = [];
for (let i = 0; i < 20; i++) { api.siguiente(); vacio.push(api.el.textContent); }
ok('acuario vacío: ninguna frase personalizada', !vacio.some(esPersonal));
ok('acuario vacío: sin marcadores sueltos', !vacio.some(t => /\{[nac]\}/.test(t)));

// ── 5) Las de «espera» sólo tras 25 s ────────────────────────────────────────
api.leer(JSON.stringify({ type: 'INIT', payload: JSON.stringify(estado) }));
api.arranca(); api.reset();
const pronto = [];
for (let i = 0; i < 40; i++) { api.siguiente(); pronto.push(api.el.textContent); }
ok('a los 0 s NO salen las de espera', !pronto.some(t => api.FRASES.es.espera.indexOf(t) >= 0));
api.avanza(30000); api.reset();
const tarde = [];
for (let i = 0; i < 60; i++) { api.siguiente(); tarde.push(api.el.textContent); }
ok('a los 30 s SÍ salen las de espera', tarde.some(t => api.FRASES.es.espera.indexOf(t) >= 0));

// ── 7) ⭐ EL IDIOMA (se validó `lang=es -> es` en device el 28-ago) ──────────
// ⚠ Estos tests existen porque durante un rato el idioma se LEÍA, se LOGUEABA y se IGNORABA:
// `_fuentes()` usaba `FRASES.es` a pelo. Un usuario en inglés habría visto castellano, y el
// log habría dicho `lang=en -> en` tan tranquilo. El síntoma perfecto: parece que funciona.
function conIdioma(lang) {
  api.leer(JSON.stringify({ type: 'INIT', payload: JSON.stringify(Object.assign({ lang: lang }, estado)) }));
  api.reset();
  const out = [];
  for (let i = 0; i < 60; i++) { api.siguiente(); out.push(api.el.textContent); }
  return out;
}
let sal = conIdioma('en');
ok('lang=en → salen frases en INGLÉS', sal.some(t => api.FRASES.en.ambiente.indexOf(t) >= 0), sal[0]);
ok('lang=en → NINGUNA en castellano', !sal.some(t => api.FRASES.es.ambiente.indexOf(t) >= 0));
ok('lang=en → las personalizadas también', sal.some(t => /cannot wait|showing off|brought food/.test(t)));

sal = conIdioma('es');
ok('lang=es → salen en CASTELLANO', sal.some(t => api.FRASES.es.ambiente.indexOf(t) >= 0));
ok('lang=es → ninguna en inglés', !sal.some(t => api.FRASES.en.ambiente.indexOf(t) >= 0));

sal = conIdioma('fr');
ok('idioma desconocido → cae a castellano', sal.some(t => api.FRASES.es.ambiente.indexOf(t) >= 0));
ok('idioma desconocido → sin marcadores sueltos', !sal.some(t => /\{[nac]\}/.test(t)));

// El locale completo debe recortarse a dos letras (lo pedimos así al repo móvil a propósito).
sal = conIdioma('en-GB');
ok('locale completo (en-GB) → inglés', sal.some(t => api.FRASES.en.ambiente.indexOf(t) >= 0));

// Los dos bancos deben tener la MISMA estructura, o un idioma tendrá menos variedad sin avisar.
for (const k of ['ambiente', 'info', 'espera']) {
  ok('banco es/en con el mismo nº de "' + k + '"',
     api.FRASES.es[k].length === api.FRASES.en[k].length,
     api.FRASES.es[k].length + ' vs ' + api.FRASES.en[k].length);
}
for (const k of ['unPez', 'dosPeces', 'cuenta']) {
  ok('plantillas es/en con el mismo nº de "' + k + '"',
     api.PLANT.es[k].length === api.PLANT.en[k].length,
     api.PLANT.es[k].length + ' vs ' + api.PLANT.en[k].length);
}
// Y las plantillas inglesas deben llevar sus marcadores, o no se sustituiría nada.
ok('las plantillas en inglés llevan {n}', api.PLANT.en.unPez.every(t => t.indexOf('{n}') >= 0));
ok('las de pareja llevan {n} y {a}', api.PLANT.en.dosPeces.every(t => t.indexOf('{n}') >= 0 && t.indexOf('{a}') >= 0));
ok('las de cuenta llevan {c}', api.PLANT.en.cuenta.every(t => t.indexOf('{c}') >= 0));

// Volver a castellano para los tests de contenido de abajo.
api.leer(JSON.stringify({ type: 'INIT', payload: JSON.stringify(estado) }));

// ── 6bis) GENERO ─────────────────────────────────────────────────────────────
// AVISO: el sexo del pez NO viaja por el canal Cast. `TvFishEntry` trae speciesId,
// nickname, uid y ageScale, y nada mas. El movil lo tiene en su save (OwnedFishSave.sex)
// pero no lo manda, y `pairs` (maleUid/femaleUid) solo cubre peces EMPAREJADOS y ademas
// llega DESPUES del INIT, o sea cuando la splash ya lleva rato rotando frases.
// => Mientras no llegue el campo, NINGUNA plantilla personalizada puede marcar genero, o
//    un pez macho sale como "Nemo esta deseando que LA veas".
const MARCAS = {
  es: /(que la veas|que lo veas|favorecid[oa]|junt[oa]s\b|guap[oa]\b|solit[oa]\b)/i,
  en: /\b(her|hers|she|his|him|he)\b/i,
};
for (const idioma of ['es', 'en']) {
  const P = api.PLANT[idioma];
  const todasP = P.unPez.concat(P.dosPeces, P.cuenta);
  const malas = todasP.filter(t => MARCAS[idioma].test(t));
  ok('plantillas ' + idioma + ' sin marca de genero'
     + (malas.length ? ' -> ' + JSON.stringify(malas) : ''), malas.length === 0);
}

// ── 6) Contenido ─────────────────────────────────────────────────────────────
const todas = api.FRASES.es.ambiente.concat(api.FRASES.es.info, api.FRASES.es.espera);
ok('ninguna frase vacía', todas.every(f => f && f.trim().length > 3));
ok('sin marcadores sueltos en las fijas', !todas.some(f => /\{[nac]\}/.test(f)));
ok('sin frases duplicadas en el banco', new Set(todas).size === todas.length);

console.log('\nbanco: ' + todas.length + ' fijas (' + api.FRASES.es.ambiente.length + ' ambiente + '
  + api.FRASES.es.info.length + ' info + ' + api.FRASES.es.espera.length + ' espera) · '
  + per.length + ' personalizadas con 7 peces');
console.log(fallos === 0 ? '\n✅ TODO OK' : '\n⚠ ' + fallos + ' FALLOS');
process.exit(fallos === 0 ? 0 : 1);
