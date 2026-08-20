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

/**
 * Fase 1: token constante (uno o varios, separados por coma, para poder rotar sin
 * dejar fuera al receiver viejo mientras se despliega el nuevo).
 * Fase 2: aqui entra la verificacion HS256 del JWT + los claims de propiedad.
 */
function authorize(req, env) {
  const auth = req.headers.get('Authorization') || '';
  if (!auth.startsWith('Bearer ')) return { ok: false, status: 401, msg: 'Missing auth' };

  const token = auth.slice(7).trim();
  if (!token) return { ok: false, status: 401, msg: 'Missing auth' };

  const allowed = (env.BUNDLE_TOKENS || '').split(',').map(s => s.trim()).filter(Boolean);
  if (allowed.length === 0) return { ok: false, status: 503, msg: 'Worker sin BUNDLE_TOKENS' };

  if (tokenMatches(token, allowed)) return { ok: true };
  return { ok: false, status: 403, msg: 'Invalid token' };
}

export default {
  async fetch(req, env, ctx) {
    const url = new URL(req.url);

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

    const auth = authorize(req, env);
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
    return res;
  },
};
