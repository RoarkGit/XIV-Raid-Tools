const { WebSocketServer, WebSocket } = require('ws');
const http = require('http');

const PORT = process.env.PORT || 3000;
const rooms = new Map(); // roomId -> Set<ws>

function genRoomId() {
  let id;
  do {
    id = Array.from({length: 4}, () => String.fromCharCode(65 + Math.floor(Math.random() * 26))).join('');
  } while (rooms.has(id));
  return id;
}

const server = http.createServer((_req, res) => { res.writeHead(200); res.end('OK'); });
const wss = new WebSocketServer({ server });

wss.on('connection', (ws) => {
  ws._room = null;

  ws.on('message', (raw) => {
    let msg;
    try { msg = JSON.parse(raw); } catch { return; }

    if (msg.type === 'create') {
      const id = genRoomId();
      rooms.set(id, new Set([ws]));
      ws._room = id;
      ws.send(JSON.stringify({ type: 'created', room: id }));

    } else if (msg.type === 'join') {
      const id = (msg.room || '').toUpperCase();
      if (!/^[A-Z]{4}$/.test(id)) {
        ws.send(JSON.stringify({ type: 'error', msg: 'Invalid room code. Must be 4 letters.' }));
        return;
      }
      if (!rooms.has(id)) rooms.set(id, new Set());
      rooms.get(id).add(ws);
      ws._room = id;
      ws.send(JSON.stringify({ type: 'joined', room: id }));

    } else if (msg.type === 'state') {
      if (!ws._room || !rooms.has(ws._room)) return;
      const payload = JSON.stringify(msg);
      for (const client of rooms.get(ws._room)) {
        if (client !== ws && client.readyState === WebSocket.OPEN) client.send(payload);
      }
    }
  });

  ws.on('close', () => {
    if (ws._room && rooms.has(ws._room)) {
      rooms.get(ws._room).delete(ws);
      if (rooms.get(ws._room).size === 0) rooms.delete(ws._room);
    }
  });
});

server.listen(PORT, () => console.log(`WS server listening on :${PORT}`));
