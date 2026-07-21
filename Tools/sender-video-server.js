// Sirve Tools/sender-video.html en localhost:3003 (RUNG 0 de la bisección).
// Separado del harness de ayer (sender-server.js / 3002) para no romperlo.
// localhost = "secure context", requisito del Cast Web Sender SDK.
// Puerto 3003: el 3001 lo usa static-server, el 3002 el harness de ayer, el 8080 Docker.
const http = require('http'), fs = require('fs'), path = require('path');
const FILE = path.join(__dirname, 'sender-video.html');

http.createServer((req, res) => {
  fs.readFile(FILE, (e, d) => {
    if (e) { res.writeHead(500); res.end('no se pudo leer sender-video.html'); return; }
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' });
    res.end(d);
  });
}).listen(3003, () => console.log('RUNG 0 (vídeo en loop) en http://localhost:3003'));
