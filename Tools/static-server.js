const http = require('http'), fs = require('fs'), path = require('path');
const root = path.join(__dirname, '..', 'webgl-output');
const MIME = { '.html':'text/html', '.js':'application/javascript', '.wasm':'application/wasm',
  '.data':'application/octet-stream', '.json':'application/json', '.png':'image/png',
  '.css':'text/css', '.ico':'image/x-icon', '.bin':'application/octet-stream', '.hash':'text/plain' };
http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html';
  const fp = path.join(root, p);
  fs.readFile(fp, (e, d) => {
    if (e) { res.writeHead(404); res.end('404'); return; }
    res.writeHead(200, { 'Content-Type': MIME[path.extname(fp)] || 'application/octet-stream',
      'Access-Control-Allow-Origin':'*' });
    res.end(d);
  });
}).listen(3001, () => console.log('static server on 3001'));
