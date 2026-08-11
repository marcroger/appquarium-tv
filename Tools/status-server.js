#!/usr/bin/env node
/**
 * Panel de estado de la investigación Cast — http://localhost:3005
 * Se auto-refresca cada 5s. Lee _cast_runs/ESTADO.md (fase actual, lo escribe
 * cast-run.sh) y los resumen.txt de cada experimento terminado.
 *
 *   node Tools/status-server.js
 */
const http = require('http');
const fs = require('fs');
const path = require('path');

const ROOT = path.join(__dirname, '..', '_cast_runs');
const PORT = 3005;

const esc = s => String(s).replace(/[&<>]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));

function read(p, def = '') { try { return fs.readFileSync(p, 'utf8'); } catch (e) { return def; } }

function runs() {
  try {
    return fs.readdirSync(ROOT, { withFileTypes: true })
      .filter(d => d.isDirectory())
      .map(d => ({
        name: d.name,
        resumen: read(path.join(ROOT, d.name, 'resumen.txt')),
        mtime: fs.statSync(path.join(ROOT, d.name)).mtimeMs,
      }))
      .sort((a, b) => b.mtime - a.mtime);
  } catch (e) { return []; }
}

http.createServer((req, res) => {
  const estado = read(path.join(ROOT, 'ESTADO.md'), 'sin actividad todavía');
  const lines = estado.trim().split('\n');
  const ultima = lines[lines.length - 1] || '';
  const activo = /reiniciando|esperando|CASTEANDO|liberando|asentamiento/i.test(ultima);
  const done = runs();

  res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' });
  res.end(`<!doctype html><html><head><meta charset="utf-8">
<meta http-equiv="refresh" content="5">
<title>Cast — estado</title>
<style>
  body{background:#0e1116;color:#d7dde5;font:14px/1.55 ui-monospace,Menlo,Consolas,monospace;margin:0;padding:28px}
  h1{font-size:17px;margin:0 0 4px;color:#fff;letter-spacing:.3px}
  .sub{color:#7d8794;font-size:12px;margin-bottom:22px}
  .card{background:#161b22;border:1px solid #26303b;border-radius:10px;padding:16px 18px;margin-bottom:18px}
  .dot{display:inline-block;width:9px;height:9px;border-radius:50%;margin-right:8px;vertical-align:1px}
  .on{background:#3fb950;box-shadow:0 0 9px #3fb95099;animation:p 1.4s infinite}
  .off{background:#6e7681}
  @keyframes p{50%{opacity:.35}}
  .now{font-size:15px;color:#fff}
  pre{white-space:pre-wrap;margin:0;color:#9aa5b1;font-size:12.5px}
  .log{max-height:230px;overflow:auto;color:#6e7681;font-size:12px}
  h2{font-size:13px;color:#58a6ff;margin:0 0 8px;text-transform:uppercase;letter-spacing:.6px}
  .run{border-left:3px solid #2f81f7;padding-left:12px;margin-bottom:16px}
  .run b{color:#e6edf3}
</style></head><body>
<h1>Investigación Cast · panel de estado</h1>
<div class="sub">se refresca solo cada 5 s · ${new Date().toLocaleTimeString('es-ES')}</div>

<div class="card">
  <div class="now"><span class="dot ${activo ? 'on' : 'off'}"></span>${activo ? 'TRABAJANDO' : 'en espera'}</div>
  <div style="margin-top:10px;color:#d7dde5">${esc(ultima)}</div>
</div>

<div class="card">
  <h2>traza de la fase actual</h2>
  <pre class="log">${esc(lines.slice(-22).join('\n'))}</pre>
</div>

<div class="card">
  <h2>experimentos completados (${done.length})</h2>
  ${done.length === 0 ? '<pre>todavía ninguno</pre>' :
    done.map(r => `<div class="run"><b>${esc(r.name)}</b><pre>${esc(r.resumen || '(en curso)')}</pre></div>`).join('')}
</div>
</body></html>`);
}).listen(PORT, () => console.log('panel en http://localhost:' + PORT));
