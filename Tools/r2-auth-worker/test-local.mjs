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

console.log(`\n== ${pass} OK, ${fail} fallos`);
process.exit(fail === 0 ? 0 : 1);
