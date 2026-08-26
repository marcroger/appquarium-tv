// Servidor estático del receiver para pruebas locales (Chrome + ?devtest=1), puerto 3001.
//
// ⚠⚠ 2026-08-26 — Por qué esto NO sirve `StreamingAssets/aa/` del disco:
//
// El catálogo local y el de R2 dejaron de cuadrar (ver CLAUDE.md, «El catálogo local YA NO
// cuadra con R2»): un build de player regenera los bundles con hashes nuevos, pero al
// desplegar sólo se sube `Build/`. Servir el catálogo del disco hacía que el player pidiera
// al Worker bundles que NO existen en el bucket → **404 en los 7**, acuario vacío. El rig
// local llevaba así desde el último build de player sin que nadie lo notara, porque todo se
// validaba en la tele.
//
// 🧭 La trampa es que los dos catálogos pesan EXACTAMENTE lo mismo (44.826 bytes) y sólo
// cambian los hashes de dentro: comparar por tamaño —o dar por bueno «suele ser idéntico»—
// no lo detecta.
//
// Por eso `StreamingAssets/aa/` se sirve desde R2, que es lo que ve la tele. El resto
// (`Build/`, `index.html`) sí sale del disco: es justo lo que se quiere probar.
// Con `--local-catalog` se vuelve al comportamiento de antes.

const http = require('http'), fs = require('fs'), path = require('path');
const root = path.join(__dirname, '..', 'webgl-output');
const R2   = 'https://pub-2b11cc17bdef4f75bd4d34eeabbd6042.r2.dev';
const CATALOGO_LOCAL = process.argv.includes('--local-catalog');

const MIME = { '.html':'text/html', '.js':'application/javascript', '.wasm':'application/wasm',
  '.data':'application/octet-stream', '.json':'application/json', '.png':'image/png',
  '.css':'text/css', '.ico':'image/x-icon', '.bin':'application/octet-stream', '.hash':'text/plain' };

const cache = new Map();   // ruta -> Buffer, para no repetir la descarga en cada recarga

async function deR2(p, res) {
  try {
    let buf = cache.get(p);
    if (!buf) {
      const r = await fetch(R2 + p, { headers: { 'User-Agent': 'Mozilla/5.0 Chrome/140.0.0.0' } });
      if (!r.ok) { res.writeHead(r.status); res.end(String(r.status)); return; }
      buf = Buffer.from(await r.arrayBuffer());
      cache.set(p, buf);
      console.log(`  <- R2 ${p} (${buf.length} B)`);
    }
    res.writeHead(200, { 'Content-Type': MIME[path.extname(p)] || 'application/octet-stream',
      'Access-Control-Allow-Origin':'*' });
    res.end(buf);
  } catch (e) { res.writeHead(502); res.end('502 ' + e.message); }
}

http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html';

  if (!CATALOGO_LOCAL && p.startsWith('/StreamingAssets/aa/')) return deR2(p, res);

  const fp = path.join(root, p);
  fs.readFile(fp, (e, d) => {
    if (e) { res.writeHead(404); res.end('404'); return; }
    res.writeHead(200, { 'Content-Type': MIME[path.extname(fp)] || 'application/octet-stream',
      'Access-Control-Allow-Origin':'*' });
    res.end(d);
  });
}).listen(3001, () => console.log('static server on 3001 - catalogo: '
  + (CATALOGO_LOCAL ? 'LOCAL (--local-catalog)' : 'R2 (el mismo que ve la tele)')));
