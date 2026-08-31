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
// 2026-08-31: EL SEXO YA VIAJA (movil 1.2.6 / build 41, campo `activeFish[].sex`).
// El aviso que habia aqui decia lo contrario y se ha quedado obsoleto; lo que NO cambia es
// que el banco NEUTRO (`unPez`) tiene que seguir siendo neutro, porque es el que se usa
// cuando el sexo no llega (`""`, cliente viejo), cuando el emisor dice que no lo sabe
// (`"Unknown"`) y cuando llega basura.
//
// Valores en el cable (confirmados por la sesion del repo movil con las lineas delante):
//   "Male" / "Female" / "Unknown"   <- lo unico que manda la build 41
//   ""                              <- cliente ANTERIOR a la 41, que no manda el campo
// Cualquier otra cosa -> desconocido, sin normalizar a ciegas.
const MARCAS = {
  es: /(que la veas|que lo veas|favorecid[oa]|junt[oa]s\b|guap[oa]\b|solit[oa]\b)/i,
  en: /\b(her|hers|she|his|him|he)\b/i,
};
for (const idioma of ['es', 'en']) {
  const P = api.PLANT[idioma];
  const todasP = P.unPez.concat(P.dosPeces, P.cuenta);
  const malas = todasP.filter(t => MARCAS[idioma].test(t));
  ok('banco NEUTRO ' + idioma + ' sin marca de genero'
     + (malas.length ? ' -> ' + JSON.stringify(malas) : ''), malas.length === 0);
}

// Y al reves: los bancos con genero tienen que estar REALMENTE marcados. Sin esto, alguien
// puede editar una frase y dejarla neutra, y el banco seguiria existiendo sin hacer nada
// -- que es el fallo silencioso de siempre: la infraestructura esta, el efecto no.
const MARCA_M = {
  es: /(que lo veas|guapo|orgulloso|emocionado|hambriento|listo|tímido|dormilón)/i,
  en: /\b(he|him|his)\b/i,
};
const MARCA_F = {
  es: /(que la veas|guapa|orgullosa|emocionada|hambrienta|lista|tímida|dormilona)/i,
  en: /\b(she|her|hers|herself)\b/i,
};
for (const idioma of ['es', 'en']) {
  const P = api.PLANT[idioma];
  ok('hay bancos con genero en ' + idioma, Array.isArray(P.unPezM) && Array.isArray(P.unPezF));
  // Misma longitud que el neutro: si no, la variedad de frases dependeria del sexo del pez.
  ok('unPezM/unPezF/' + 'unPez con el mismo nº en ' + idioma,
     P.unPezM.length === P.unPez.length && P.unPezF.length === P.unPez.length,
     P.unPez.length + ' / ' + P.unPezM.length + ' / ' + P.unPezF.length);
  // Cada frase con genero lleva su marca...
  const sinM = P.unPezM.filter(t => !MARCA_M[idioma].test(t));
  ok('todas las de macho en ' + idioma + ' llevan marca masculina'
     + (sinM.length ? ' -> ' + JSON.stringify(sinM) : ''), sinM.length === 0);
  const sinF = P.unPezF.filter(t => !MARCA_F[idioma].test(t));
  ok('todas las de hembra en ' + idioma + ' llevan marca femenina'
     + (sinF.length ? ' -> ' + JSON.stringify(sinF) : ''), sinF.length === 0);
  // ...y NO la del otro. Esto es lo que caza el copiar-pegar a medias.
  const cruceM = P.unPezM.filter(t => MARCA_F[idioma].test(t));
  ok('ninguna de macho en ' + idioma + ' lleva marca femenina'
     + (cruceM.length ? ' -> ' + JSON.stringify(cruceM) : ''), cruceM.length === 0);
  const cruceF = P.unPezF.filter(t => MARCA_M[idioma].test(t));
  ok('ninguna de hembra en ' + idioma + ' lleva marca masculina'
     + (cruceF.length ? ' -> ' + JSON.stringify(cruceF) : ''), cruceF.length === 0);
  // Y los dos bancos tienen que ser distintos frase a frase: un banco duplicado pasaria
  // todas las de arriba en ingles (donde la marca la pone un pronombre) sin cambiar nada.
  const iguales = P.unPezM.filter((t, i) => t === P.unPezF[i]);
  ok('los dos bancos de ' + idioma + ' difieren frase a frase', iguales.length === 0,
     iguales.length + ' identicas');
}

// ── 6ter) EL SEXO, DE PUNTA A PUNTA ─────────────────────────────────────────
// ⚠⚠ Esto prueba que el index.html PARSEA el campo, NO que el movil lo mande. El fixture lo
//    escribo yo. La paridad de verdad solo la cierra un volcado de la APK real -- aviso de la
//    sesion del repo movil, y es el mismo verde fabricado a mano que ya costo una tanda.
// Los CUATRO caminos, no solo el que espero: Male, Female, Unknown y campo ausente.
const conSexo = {
  activeFish: [
    { speciesId: 'fish_a', nickname: 'Macho',  sex: 'Male'    },
    { speciesId: 'fish_b', nickname: 'Hembra', sex: 'Female'  },
    { speciesId: 'fish_c', nickname: 'Nosabe', sex: 'Unknown' },
    { speciesId: 'fish_d', nickname: 'Viejo'                  }
  ]
};
logs.length = 0;
api.leer(JSON.stringify({ type: 'INIT', payload: JSON.stringify(conSexo) }));
ok('el log separa Unknown de ausente', /sexo M1\/F1\/Unknown1\/ausente1/.test(logs.join('|')),
   logs.join(' | '));

// El banco de un pez concreto: se mira SU nombre dentro de las frases producidas.
// (`_personalizadas` corta a 3 peces barajados, asi que se repite hasta ver a los cuatro.)
// ⚠ Se SALTAN las frases con dos nombres: las de pareja (`dosPeces`) son neutras a
//   proposito, asi que contarlas hundiria la comprobacion de abajo con un fallo que no lo es.
//   (Lo cazo el propio test en su primera ejecucion: decia FALLA enseñando una frase correcta.)
const NOMBRES = ['Macho', 'Hembra', 'Nosabe', 'Viejo'];
const vistas = {};
for (let i = 0; i < 60; i++) for (const f of api.personales()) {
  const dentro = NOMBRES.filter(n => f.indexOf(n) >= 0);
  if (dentro.length !== 1) continue;
  (vistas[dentro[0]] = vistas[dentro[0]] || []).push(f);
}
const soloDe = (n, re) => (vistas[n] || []).length > 0 && (vistas[n] || []).every(f => re.test(f));
const neutro = n => (vistas[n] || []).length > 0 &&
  (vistas[n] || []).every(f => !MARCA_M.es.test(f) && !MARCA_F.es.test(f));
ok('el macho usa el banco masculino', soloDe('Macho', MARCA_M.es),
   ((vistas.Macho || [])[0] || 'NINGUNA'));
ok('la hembra usa el banco femenino', soloDe('Hembra', MARCA_F.es),
   ((vistas.Hembra || [])[0] || 'NINGUNA'));
// ⚠ Los dos casos de abajo son los que HOY salen en produccion: mientras el 99 % de los
//   usuarios no actualice, el camino neutro es EL camino. Si se rompe, no se nota.
ok('"Unknown" cae al banco NEUTRO', neutro('Nosabe'),
   ((vistas.Nosabe || [])[0] || 'NINGUNA'));
ok('el campo AUSENTE (cliente viejo) cae al banco NEUTRO', neutro('Viejo'),
   ((vistas.Viejo || [])[0] || 'NINGUNA'));

// Basura en el campo: ni peta ni se normaliza a ciegas -> neutro.
logs.length = 0;
api.leer(JSON.stringify({ type: 'INIT', payload: JSON.stringify({
  activeFish: [{ speciesId: 'fish_x', nickname: 'Raro', sex: 'MALE' },
               { speciesId: 'fish_y', nickname: 'Raro2', sex: 42 }] }) }));
ok('un valor no reconocido se cuenta como RARO', /RAROS2/.test(logs.join('|')), logs.join(' | '));
const rarasP = api.personales().filter(f => f.indexOf('Raro') >= 0);
ok('un valor no reconocido cae al banco NEUTRO',
   rarasP.length > 0 && rarasP.every(f => !MARCA_M.es.test(f) && !MARCA_F.es.test(f)));

// Volver al estado de 7 peces sin sexo para lo que venga detras.
api.leer(JSON.stringify({ type: 'INIT', payload: JSON.stringify(estado) }));

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
