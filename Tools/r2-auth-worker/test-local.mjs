/**
 * Prueba la logica del Worker sin Cloudflare: mock del binding R2 y de caches.default.
 * No sustituye a smoke-test.sh (que prueba el Worker YA desplegado), pero coge los
 * fallos de routing y de auth antes de tocar nada.
 *   node test-local.mjs
 */
import worker, { itemIdFromKey } from './src/index.js';

const BODY = new Uint8Array([1, 2, 3, 4, 5]);
const KEY  = 'decos_remote_assets_deco_coral_acropora_205d35d0fac9df6e53157eb37878ac3b.bundle';
const ORIGIN = 'https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev';
const BASE = 'https://worker.test';

globalThis.caches = { default: { match: async () => null, put: async () => {} } };
const ctx = { waitUntil: () => {} };
const env = {
  BUNDLE_TOKENS: 'token-bueno,token-viejo',
  JWT_SECRET:    'secreto-de-pruebas',
  MINT_TOKENS:   'credencial-del-apk',
  ALLOWED_ORIGINS: `${ORIGIN},http://localhost:3001`,
  ASSETS: {
    async get(k) { return k === KEY ? { body: new Blob([BODY]).stream(), size: BODY.length, httpEtag: '"abc"' } : null; },
    async head(k) { return k === KEY ? { size: BODY.length, httpEtag: '"abc"' } : null; },
  },
};

const call = (path, init = {}) => worker.fetch(new Request(BASE + path, init), env, ctx);
const bearer = t => ({ Authorization: 'Bearer ' + t });
let pass = 0, fail = 0;
const chk = (desc, exp, got) => {
  if (exp === got) { console.log(`  OK    ${desc} (${got})`); pass++; }
  else { console.log(`  FALLO ${desc} -> esperado ${exp}, obtenido ${got}`); fail++; }
};

const r = {};
r.health   = await call('/health');
r.noAuth   = await call(`/bundle/${KEY}`);
r.badTok   = await call(`/bundle/${KEY}`, { headers: bearer('mal') });
r.ok       = await call(`/bundle/${KEY}`, { headers: { ...bearer('token-bueno'), Origin: ORIGIN } });
r.okViejo  = await call(`/bundle/${KEY}`, { headers: bearer('token-viejo') });
r.noExiste = await call('/bundle/no_existe.bundle', { headers: bearer('token-bueno') });
r.raiz     = await call('/', { headers: bearer('token-bueno') });
r.travers  = await call('/bundle/../index.html', { headers: bearer('token-bueno') });
r.post     = await call(`/bundle/${KEY}`, { method: 'POST', headers: bearer('token-bueno') });
r.head     = await call(`/bundle/${KEY}`, { method: 'HEAD', headers: bearer('token-bueno') });
r.pre      = await call(`/bundle/${KEY}`, { method: 'OPTIONS', headers: { Origin: ORIGIN } });
r.preMal   = await call(`/bundle/${KEY}`, { method: 'OPTIONS', headers: { Origin: 'https://evil.example' } });
r.doble    = await call(`/bundle//${KEY}`, { headers: bearer('token-bueno') });
r.sinTok   = await worker.fetch(new Request(`${BASE}/bundle/${KEY}`, { headers: bearer('token-bueno') }),
                                { ...env, BUNDLE_TOKENS: '' }, ctx);

chk('/health',                      200, r.health.status);
chk('sin Authorization',            401, r.noAuth.status);
chk('token invalido',               403, r.badTok.status);
chk('token valido',                 200, r.ok.status);
chk('segundo token (rotacion)',     200, r.okViejo.status);
chk('bundle inexistente',           404, r.noExiste.status);
chk('ruta que no es /bundle/',      404, r.raiz.status);
chk('path traversal',               404, r.travers.status);
chk('POST',                         405, r.post.status);
chk('HEAD con token',               200, r.head.status);
chk('preflight origen permitido',   204, r.pre.status);
chk('preflight origen ajeno',       403, r.preMal.status);
chk('Worker sin BUNDLE_TOKENS',     503, r.sinTok.status);
chk('doble barra (remote hash)',    200, r.doble.status);

chk('preflight permite Authorization', true,
    /authorization/i.test(r.pre.headers.get('access-control-allow-headers') || ''));
chk('CORS al origen del receiver',  ORIGIN, r.ok.headers.get('access-control-allow-origin'));
chk('Cache-Control inmutable',      'public, max-age=604800, immutable', r.ok.headers.get('cache-control'));
chk('Content-Length',               String(BODY.length), r.ok.headers.get('content-length'));
chk('los bytes salen intactos',     '1,2,3,4,5', new Uint8Array(await r.ok.arrayBuffer()).join(','));
chk('un 403 no filtra CORS a un ajeno', null,
    (await call(`/bundle/${KEY}`, { headers: { ...bearer('mal'), Origin: 'https://evil.example' } }))
      .headers.get('access-control-allow-origin'));
chk('itemId del nombre de bundle',  'deco_coral_acropora', itemIdFromKey(KEY));


// ── Fase 2: JWT por usuario ─────────────────────────────────────────────────
const AUDIO = 'audio_remote_assets_ambient_music_205d35d0fac9df6e53157eb37878ac3b.bundle';
env.ASSETS = {
  async get(k)  { return (k === KEY || k === AUDIO) ? { body: new Blob([BODY]).stream(), size: BODY.length, httpEtag: '"abc"' } : null; },
  async head(k) { return (k === KEY || k === AUDIO) ? { size: BODY.length, httpEtag: '"abc"' } : null; },
};

// Firma un JWT con el secreto de pruebas, para fabricar casos (caducados, etc.)
const b64u = str => btoa(String.fromCharCode(...new TextEncoder().encode(str)))
  .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
async function signJwtDePrueba(claims, alg = 'HS256') {
  const data = b64u(JSON.stringify({ alg, typ: 'JWT' })) + '.' + b64u(JSON.stringify(claims));
  const key = await crypto.subtle.importKey('raw', new TextEncoder().encode(env.JWT_SECRET),
    { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  const sig = new Uint8Array(await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(data)));
  let bin = ''; for (const b of sig) bin += String.fromCharCode(b);
  return data + '.' + btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

const mint = (body, cred = 'credencial-del-apk') => call('/mint-token', {
  method: 'POST', headers: { ...bearer(cred), 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
});

const j = {};
j.sinCred = await call('/mint-token', { method: 'POST', body: '{}' });
j.credMal = await mint({ userId: 'u0' }, 'no-soy-el-apk');
j.getMint = await call('/mint-token', { headers: bearer('credencial-del-apk') });
j.sinUser = await mint({});
j.dueno   = await mint({ userId: 'u1', ownedDecoIds: ['deco_coral_acropora'] });
j.pobre   = await mint({ userId: 'u2', ownedDecoIds: ['deco_otra_cosa'] });
j.premium = await mint({ userId: 'u3', isPremium: true });

chk('mint sin credencial',            401, j.sinCred.status);
chk('mint con credencial ajena',      401, j.credMal.status);
chk('mint por GET',                   405, j.getMint.status);
chk('mint sin userId',                400, j.sinUser.status);
chk('mint valido',                    200, j.dueno.status);

const tokDueno   = (await j.dueno.json()).token;
const tokPobre   = (await j.pobre.json()).token;
const tokPremium = (await j.premium.json()).token;

chk('el JWT tiene tres partes',       3, tokDueno.split('.').length);

const conJwt = t => call(`/bundle/${KEY}`, { headers: bearer(t) });
chk('JWT de quien SI posee la deco',  200, (await conJwt(tokDueno)).status);
chk('JWT premium: todo vale',         200, (await conJwt(tokPremium)).status);

// Modo `log` (por defecto): se sirve igual, pero marcado.
const rPobre = await conJwt(tokPobre);
chk('JWT de quien NO la posee (log)', 200, rPobre.status);
chk('  ...marcado would-deny',        'would-deny', rPobre.headers.get('x-aq-ownership'));
chk('al dueno no se le marca',        null, (await conJwt(tokDueno)).headers.get('x-aq-ownership'));

// Modo `enforce`: el dia que se active, la misma peticion cae.
env.OWNERSHIP_MODE = 'enforce';
chk('quien NO la posee (enforce)',    403, (await conJwt(tokPobre)).status);
chk('el audio no es de nadie',        200,
    (await call(`/bundle/${AUDIO}`, { headers: bearer(tokPobre) })).status);
chk('en enforce el dueno entra',      200, (await conJwt(tokDueno)).status);
delete env.OWNERSHIP_MODE;

// Falsificaciones
const partes = tokDueno.split('.');
chk('firma manipulada',               401,
    (await conJwt(partes[0] + '.' + partes[1] + '.' + partes[2].slice(0, -2) + 'AA')).status);
chk('alg: none rechazado',            401,
    (await conJwt(b64u(JSON.stringify({ alg: 'none', typ: 'JWT' })) + '.'
                + b64u(JSON.stringify({ userId: 'x', exp: 4102444800 })) + '.')).status);
chk('alg HS256 exigido en la cabecera', 401,
    (await conJwt(await signJwtDePrueba({ userId: 'x', exp: 4102444800 }, 'HS512'))).status);
chk('JWT caducado',                   401,
    (await conJwt(await signJwtDePrueba({ userId: 'x', exp: Math.floor(Date.now() / 1000) - 10 }))).status);
chk('JWT sin exp',                    401,
    (await conJwt(await signJwtDePrueba({ userId: 'x' }))).status);

chk('el token constante SIGUE valiendo', 200, (await conJwt('token-bueno')).status);

env.JWT_SECRET = '';
chk('Worker sin JWT_SECRET',          503, (await conJwt(tokDueno)).status);
env.JWT_SECRET = 'secreto-de-pruebas';

console.log(`\n== ${pass} OK, ${fail} fallos`);
process.exit(fail === 0 ? 0 : 1);
