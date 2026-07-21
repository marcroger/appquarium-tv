// Sirve Tools/sender-rung1.html en localhost:3004 (RUNG 1 de la bisección).
// Separado de los senders de rung 0 (3003) y de ayer (3002).
// localhost = "secure context", requisito del Cast Web Sender SDK.
const http = require('http'), fs = require('fs'), path = require('path');
const FILE = path.join(__dirname, 'sender-rung1.html');

http.createServer((req, res) => {
  fs.readFile(FILE, (e, d) => {
    if (e) { res.writeHead(500); res.end('no se pudo leer sender-rung1.html'); return; }
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' });
    res.end(d);
  });
}).listen(3004, () => console.log('RUNG 1 (receiver mínimo) en http://localhost:3004'));
