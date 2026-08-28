/**
 * Appquarium TV — portero de los bundles de Addressables.
 *
 * Los bundles viven en un bucket R2 PRIVADO (sin dominio publico). Este Worker es la
 * unica puerta: sin token no salen bytes.
 *
 *   Fase 1  el receiver manda un token constante horneado en el .wasm.
 *   Fase 2  el mismo header traera el JWT por usuario que emite el movil. El receiver
 *           NO hay que rebuildearlo: el hook de TV ya prefiere state.castJwt si viene.
 *
 * Decision de diseno importante: el Worker SIRVE LOS BYTES, no redirige a R2.
 * Un 302 no vale: el receiver pide los bundles desde otro origen y con un header
 * Authorization, o sea request con preflight, y el spec de Fetch prohibe seguir un
 * redirect cross-origin en ese caso. Con proxy ademas el bucket puede ser privado
 * de verdad, que es justo lo que queremos.
 */

// Solo un nombre de fichero: sin barras, sin "..". Corta el path traversal de raiz.
const BUNDLE_RE = /^\/bundle\/([A-Za-z0-9._-]+)$/;

// decos_remote_assets_deco_coral_acropora_<md5>.bundle  ->  deco_coral_acropora
// Sin uso en la Fase 1; es lo que la Fase 2 necesita para los claims de propiedad.
const ITEM_RE = /^(?:decos|fish|audio|environments)_remote_assets_(.+)_[0-9a-f]{32}\.bundle$/;

export function itemIdFromKey(key) {
  const m = ITEM_RE.exec(key);
  return m ? m[1] : null;
}

/** Comparacion en tiempo constante: no filtra el token por temporizacion. */
function tokenMatches(given, allowed) {
  let ok = 0;
  for (const cand of allowed) {
    if (cand.length !== given.length) { ok |= 1; continue; }
    let diff = 0;
    for (let i = 0; i < given.length; i++) diff |= given.charCodeAt(i) ^ cand.charCodeAt(i);
    if (diff === 0) return true;
  }
  return false;
}

function corsHeaders(req, env) {
  const origin = req.headers.get('Origin');
  if (!origin) return {};                       // curl y compania: no hacen falta
  const allowed = (env.ALLOWED_ORIGINS || '').split(',').map(s => s.trim()).filter(Boolean);
  if (!allowed.includes(origin)) return {};     // no se bloquea: simplemente no se concede
  return {
    'Access-Control-Allow-Origin': origin,
    'Vary': 'Origin',
  };
}

function deny(status, msg, req, env) {
  return new Response(msg + '\n', {
    status,
    headers: { 'Content-Type': 'text/plain; charset=utf-8', ...corsHeaders(req, env) },
  });
}

// ── Fase 2: JWT por usuario ──────────────────────────────────────────────────
//
// HS256 con `JWT_SECRET` (Workers secret). Claims segun CAST_R2_AUTH_MOVIL.md §1.3:
//   userId, isPremium, ownedSpecies[], ownedDecoIds[], ownedPackIds[], iat, exp
//
// ⚠⚠ DOS DECISIONES QUE NO ESTABAN EN EL SPEC Y QUE HAY QUE CONOCER:
//
// 1. **`/mint-token` NO es abierto.** El spec decia "el Worker se fia de lo que le manda el
//    APK", y eso vale para el CONTENIDO de los claims (no se valida la compra contra Google
//    Play, es el trade-off conocido del MVP). Pero un endpoint de emision SIN NINGUNA
//    credencial es otra cosa: cualquiera pide un token con isPremium y se baja el catalogo
//    entero, y entonces la Fase 2 protegeria MENOS que la Fase 1. Asi que para emitir hay que
//    presentar uno de `MINT_TOKENS` (secret propio, el que hornea el APK). El liston sigue
//    siendo "hay que atacar el producto", que es lo que las licencias esperan.
//
// 2. **La propiedad NO se aplica todavia: se registra.** `OWNERSHIP_MODE` vale `log` (por
//    defecto) o `enforce`. En `log` se verifica la firma y la caducidad de verdad, pero si el
//    usuario pide un bundle que no consta como suyo se le sirve igual y se anota la cabecera
//    `X-Aq-Ownership: would-deny`. Motivo: si los ids de los claims llegan con otro formato
//    -sufijos de instancia, prefijos distintos- el usuario se queda sin SU acuario, y eso se
//    ve como una tele vacia, que es exactamente el sintoma mas caro de diagnosticar de este
//    proyecto. Primero se mide con trafico real, y se pasa a `enforce` cuando el contador de
//    `would-deny` sea 0.

function b64urlToBytes(s) {
  const b64 = s.replace(/-/g, '+').replace(/_/g, '/') + '=='.slice(0, (4 - s.length % 4) % 4);
  const bin = atob(b64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

function bytesToB64url(bytes) {
  let bin = '';
  for (const b of bytes) bin += String.fromCharCode(b);
  return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

const b64urlFromString = str => bytesToB64url(new TextEncoder().encode(str));

async function hmacKey(secret) {
  return crypto.subtle.importKey('raw', new TextEncoder().encode(secret),
    { name: 'HMAC', hash: 'SHA-256' }, false, ['sign', 'verify']);
}

/** Firma un JWT HS256. `claims.exp`/`iat` los pone quien llama. */
async function signJwt(claims, secret) {
  const head = b64urlFromString(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
  const body = b64urlFromString(JSON.stringify(claims));
  const data = head + '.' + body;
  const sig  = await crypto.subtle.sign('HMAC', await hmacKey(secret), new TextEncoder().encode(data));
  return data + '.' + bytesToB64url(new Uint8Array(sig));
}

/**
 * Verifica firma y caducidad. Devuelve los claims o null.
 * ⚠ `crypto.subtle.verify` ya compara en tiempo constante; no hay que hacerlo a mano.
 */
async function verifyJwt(token, secret) {
  const partes = token.split('.');
  if (partes.length !== 3) return null;
  const [head, body, sig] = partes;

  let cabecera;
  try { cabecera = JSON.parse(new TextDecoder().decode(b64urlToBytes(head))); } catch { return null; }
  // Se exige HS256 explicitamente: aceptar `alg` del token es como se cuelan los "alg: none".
  if (!cabecera || cabecera.alg !== 'HS256') return null;

  let ok;
  try {
    ok = await crypto.subtle.verify('HMAC', await hmacKey(secret),
      b64urlToBytes(sig), new TextEncoder().encode(head + '.' + body));
  } catch { return null; }
  if (!ok) return null;

  let claims;
  try { claims = JSON.parse(new TextDecoder().decode(b64urlToBytes(body))); } catch { return null; }
  if (!claims || typeof claims.exp !== 'number') return null;
  if (Math.floor(Date.now() / 1000) >= claims.exp) return null;   // caducado
  return claims;
}

/**
 * ¿Este bundle es suyo? Los bundles de `audio` y `environments` no son de nadie: no se
 * comprueban. Los de `fish` y `decos` tienen que constar en los claims.
 *
 * ⚠ El movil manda en `ownedSpecies`/`ownedDecoIds` TODO lo que el usuario tiene, incluidos
 * los regalos de inicio. No hace falta una lista de "gratis" en el Worker: si esta en su
 * acuario, esta en su save.
 */
function ownsBundle(key, claims) {
  if (/^(?:audio|environments)_remote_assets_/.test(key)) return true;
  if (claims.isPremium === true) return true;

  const itemId = itemIdFromKey(key);
  if (!itemId) return true;                       // no sabemos de que es: no se bloquea a ciegas

  const especies = Array.isArray(claims.ownedSpecies) ? claims.ownedSpecies : [];
  const decos    = Array.isArray(claims.ownedDecoIds) ? claims.ownedDecoIds : [];
  return especies.includes(itemId) || decos.includes(itemId);
}

/**
 * Fase 1: token constante (uno o varios, separados por coma, para poder rotar sin
 * dejar fuera al receiver viejo mientras se despliega el nuevo).
 * Fase 2: aqui entra la verificacion HS256 del JWT + los claims de propiedad.
 */
async function authorize(req, env, key) {
  const auth = req.headers.get('Authorization') || '';
  if (!auth.startsWith('Bearer ')) return { ok: false, status: 401, msg: 'Missing auth' };

  const token = auth.slice(7).trim();
  if (!token) return { ok: false, status: 401, msg: 'Missing auth' };

  // Fase 2 primero: si TIENE FORMA de JWT se trata como JWT y no cae al camino del token
  // constante. Si no, un JWT caducado se compararia contra la lista y daria 403 "Invalid
  // token", que es un diagnostico enganoso para algo que solo necesita re-emitirse.
  if (token.split('.').length === 3) {
    if (!env.JWT_SECRET) return { ok: false, status: 503, msg: 'Worker sin JWT_SECRET' };
    const claims = await verifyJwt(token, env.JWT_SECRET);
    if (!claims) return { ok: false, status: 401, msg: 'JWT invalido o caducado' };

    const suyo = ownsBundle(key, claims);
    if (suyo) return { ok: true, claims };

    // Ver la nota de arriba: en `log` se sirve igual y se marca. El dia que se pase a
    // `enforce`, esta misma linea empieza a devolver 403 sin tocar nada mas.
    if ((env.OWNERSHIP_MODE || 'log') === 'enforce') {
      return { ok: false, status: 403, msg: 'No consta como tuyo' };
    }
    return { ok: true, claims, wouldDeny: true };
  }

  // Fase 1: token constante. Sigue vivo durante toda la migracion (ver §3 del spec): una app
  // ya instalada no manda JWT, y quitarlo la dejaria sin acuario.
  const allowed = (env.BUNDLE_TOKENS || '').split(',').map(s => s.trim()).filter(Boolean);
  if (allowed.length === 0) return { ok: false, status: 503, msg: 'Worker sin BUNDLE_TOKENS' };

  if (tokenMatches(token, allowed)) return { ok: true };
  return { ok: false, status: 403, msg: 'Invalid token' };
}

/**
 * POST /mint-token — emite el JWT de un usuario. TTL 24 h (CAST_R2_AUTH_MOVIL.md §1.3).
 * Requiere `Authorization: Bearer <uno de MINT_TOKENS>`: ver la nota (1) de arriba.
 */
async function mintToken(req, env) {
  if (req.method !== 'POST') return deny(405, 'Method not allowed', req, env);
  if (!env.JWT_SECRET)  return deny(503, 'Worker sin JWT_SECRET', req, env);

  const auth = req.headers.get('Authorization') || '';
  const cred = auth.startsWith('Bearer ') ? auth.slice(7).trim() : '';
  const permitidos = (env.MINT_TOKENS || '').split(',').map(s => s.trim()).filter(Boolean);
  if (permitidos.length === 0) return deny(503, 'Worker sin MINT_TOKENS', req, env);
  if (!cred || !tokenMatches(cred, permitidos)) return deny(401, 'Missing auth', req, env);

  let cuerpo;
  try { cuerpo = await req.json(); } catch { return deny(400, 'Body no es JSON', req, env); }
  if (!cuerpo || typeof cuerpo.userId !== 'string' || !cuerpo.userId) {
    return deny(400, 'Falta userId', req, env);
  }

  const lista = v => (Array.isArray(v) ? v.filter(x => typeof x === 'string') : []);
  const ahora = Math.floor(Date.now() / 1000);
  const claims = {
    userId:       cuerpo.userId,
    isPremium:    cuerpo.isPremium === true,
    ownedSpecies: lista(cuerpo.ownedSpecies),
    ownedDecoIds: lista(cuerpo.ownedDecoIds),
    ownedPackIds: lista(cuerpo.ownedPackIds),
    iat:          ahora,
    exp:          ahora + 24 * 60 * 60,
  };

  const token = await signJwt(claims, env.JWT_SECRET);
  return new Response(JSON.stringify({ token, exp: claims.exp }), {
    headers: { 'Content-Type': 'application/json; charset=utf-8', ...corsHeaders(req, env) },
  });
}

export default {
  async fetch(req, env, ctx) {
    const url = new URL(req.url);

    if (url.pathname === '/mint-token') return mintToken(req, env);

    if (url.pathname === '/health') {
      return new Response('ok\n', { headers: { 'Content-Type': 'text/plain; charset=utf-8' } });
    }

    // Barras repetidas colapsadas antes de casar. Motivo real: Addressables genera la URL
    // del remote hash como ".../bundle//catalog_1.2.1.hash". Hoy esa URL no se pide nunca
    // (m_DisableCatalogUpdateOnStart = true), pero si alguien cambia ese flag el 404 seria
    // dificil de diagnosticar. El regex de la clave sigue prohibiendo barras, asi que
    // colapsarlas no abre ningun camino nuevo.
    const path = url.pathname.replace(/\/{2,}/g, '/');
    const match = BUNDLE_RE.exec(path);
    if (!match) return deny(404, 'Not found', req, env);
    const key = match[1];

    // Preflight. Sin esto el receiver no puede mandar el header Authorization.
    if (req.method === 'OPTIONS') {
      const cors = corsHeaders(req, env);
      if (!cors['Access-Control-Allow-Origin']) return deny(403, 'Origin not allowed', req, env);
      return new Response(null, {
        status: 204,
        headers: {
          ...cors,
          'Access-Control-Allow-Methods': 'GET, HEAD, OPTIONS',
          'Access-Control-Allow-Headers': 'Authorization, Range',
          'Access-Control-Max-Age': '86400',
        },
      });
    }

    if (req.method !== 'GET' && req.method !== 'HEAD') {
      return deny(405, 'Method not allowed', req, env);
    }

    const auth = await authorize(req, env, key);
    if (!auth.ok) return deny(auth.status, auth.msg, req, env);

    const cors = corsHeaders(req, env);

    // La cache del edge se consulta DESPUES de autorizar, y con una clave sintetica
    // sin el header Authorization (Cloudflare no cachea respuestas a peticiones que
    // lo llevan). Los nombres de bundle llevan el hash del contenido -> inmutables.
    const cacheKey = new Request(url.toString(), { method: 'GET' });
    const cache = caches.default;

    if (req.method === 'GET') {
      const hit = await cache.match(cacheKey);
      if (hit) {
        const res = new Response(hit.body, hit);
        for (const [k, v] of Object.entries(cors)) res.headers.set(k, v);
        res.headers.set('X-Aq-Cache', 'HIT');
        if (auth.wouldDeny) res.headers.set('X-Aq-Ownership', 'would-deny');
        return res;
      }
    }

    if (req.method === 'HEAD') {
      const head = await env.ASSETS.head(key);
      if (!head) return deny(404, 'Not found', req, env);
      return new Response(null, {
        headers: {
          'Content-Type': 'application/octet-stream',
          'Content-Length': String(head.size),
          'ETag': head.httpEtag,
          'Cache-Control': 'public, max-age=604800, immutable',
          ...cors,
        },
      });
    }

    const obj = await env.ASSETS.get(key);
    if (!obj) return deny(404, 'Not found', req, env);

    const headers = new Headers({
      'Content-Type': 'application/octet-stream',
      'Content-Length': String(obj.size),
      'ETag': obj.httpEtag,
      'Cache-Control': 'public, max-age=604800, immutable',
    });

    // Se cachea el cuerpo sin los headers de CORS: el Vary: Origin los haria inutiles
    // en la cache compartida. Se anaden despues, por respuesta.
    const cacheable = new Response(obj.body, { headers });
    ctx.waitUntil(cache.put(cacheKey, cacheable.clone()));

    const res = new Response(cacheable.body, { headers });
    for (const [k, v] of Object.entries(cors)) res.headers.set(k, v);
    res.headers.set('X-Aq-Cache', 'MISS');
    if (auth.wouldDeny) res.headers.set('X-Aq-Ownership', 'would-deny');
    return res;
  },
};
