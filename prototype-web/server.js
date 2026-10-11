// Chaos Admin 0.0.1
// Browser <-> (WebSocket /ws) <-> this server <-> (WebRCON) <-> Rust server
const http = require('http');
const fs = require('fs');
const path = require('path');
const WebSocket = require('ws');

const PORT = process.env.PORT || 3000;
const HOST = process.env.HOST || '127.0.0.1';
const PUBLIC = path.join(__dirname, 'public');
const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css' };

const server = http.createServer((req, res) => {
  const name = req.url === '/' ? 'index.html' : path.basename(req.url.split('?')[0]);
  const file = path.join(PUBLIC, name);
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404); return res.end('Not found'); }
    res.writeHead(200, { 'Content-Type': TYPES[path.extname(file)] || 'application/octet-stream' });
    res.end(data);
  });
});

const wss = new WebSocket.Server({ server, path: '/ws' });

wss.on('connection', (client) => {
  let rcon = null;
  const send = (obj) => client.readyState === WebSocket.OPEN && client.send(JSON.stringify(obj));
  const closeRcon = () => { if (rcon) { rcon.removeAllListeners(); try { rcon.close(); } catch {} rcon = null; } };

  client.on('message', (raw) => {
    let msg;
    try { msg = JSON.parse(raw); } catch { return; }

    if (msg.type === 'connect') {
      closeRcon();
      const { host, port, password } = msg;
      if (!host || !port) return send({ type: 'status', state: 'error', text: 'Host and port required' });
      send({ type: 'status', state: 'connecting', text: `Connecting to ${host}:${port}...` });
      const sock = new WebSocket(`ws://${host}:${port}/${encodeURIComponent(password || '')}`);
      rcon = sock;
      sock.on('open', () => {
        send({ type: 'status', state: 'connected', text: `Connected to ${host}:${port}` });
        sock.send(JSON.stringify({ Identifier: 1, Message: 'serverinfo', Name: 'WebRcon' }));
      });
      sock.on('message', (data) => {
        try {
          const m = JSON.parse(data);
          send({ type: 'console', id: m.Identifier, kind: m.Type, text: m.Message, time: Date.now() });
        } catch {}
      });
      sock.on('error', (e) => send({ type: 'status', state: 'error', text: `Error: ${e.message}` }));
      sock.on('close', () => {
        if (rcon === sock) rcon = null;
        send({ type: 'status', state: 'disconnected', text: 'Disconnected' });
      });
    } else if (msg.type === 'command') {
      if (!rcon || rcon.readyState !== WebSocket.OPEN) return send({ type: 'status', state: 'error', text: 'Not connected' });
      rcon.send(JSON.stringify({ Identifier: 2, Message: String(msg.command), Name: 'WebRcon' }));
    } else if (msg.type === 'disconnect') {
      closeRcon();
      send({ type: 'status', state: 'disconnected', text: 'Disconnected' });
    }
  });

  client.on('close', closeRcon);
});

server.listen(PORT, HOST, () => console.log(`Chaos Admin 0.0.1 running at http://${HOST}:${PORT}`));
