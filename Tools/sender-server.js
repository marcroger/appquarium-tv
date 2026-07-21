// Sirve Tools/sender-test.html en localhost:3002.
// localhost es "secure context", que es lo que exige el Cast Web Sender SDK
// (si no, el SDK no arranca y el icono de cast no aparece).
// Puerto 3002: el 3001 lo usa static-server.js y el 8080 lo tiene Docker.
const http = require('http'), fs = require('fs'), path = require('path');
const FILE = path.join(__dirname, 'sender-test.html');

http.createServer((req, res) => {
  fs.readFile(FILE, (e, d) => {
    if (e) { res.writeHead(500); res.end('no se pudo leer sender-test.html'); return; }
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' });
    res.end(d);
  });
}).listen(3002, () => console.log('sender de prueba en http://localhost:3002'));
