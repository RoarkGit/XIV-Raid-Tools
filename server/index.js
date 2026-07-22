const { WebSocketServer, WebSocket } = require('ws');
const http = require('http');

const PORT = process.env.PORT || 3000;
const READY_CHECK_TIMEOUT_MS = 30000;

const rooms = new Map(); // roomId -> Set<ws>
const roomPasswords = new Map(); // roomId -> string, only present when the room was created with one
const readyChecks = new Map(); // roomId -> { ready: Set<ws>, timer }, only present while a check is active
// roomId -> the most recent `state` payload relayed in that room. Replayed
// (see `join`) to whoever joins/rejoins next so they don't start blank —
// stored and replayed completely opaquely, same as the live broadcast, so
// this needs no per-tool knowledge (works identically for the webapp's
// mechanic fields and the plugin's, or any future tool's own shape).
const lastState = new Map();

function genRoomId() {
  let id;
  do {
    id = Array.from({length: 4}, () => String.fromCharCode(65 + Math.floor(Math.random() * 26))).join('');
  } while (rooms.has(id));
  return id;
}

// Ready check is webapp-only — the game already has its own ready check, so
// the Dalamud plugin never sends readyCheck/readyAck (see SessionClient.cs)
// and would never be able to complete a check counted against the WHOLE
// room. `client` is set from create/join's payload (defaults to 'webapp'
// for anything that doesn't specify it, e.g. a manual/raw connection) and
// only 'plugin' is ever excluded here.
function webappCount(id) {
  if (!rooms.has(id)) return 0;
  let n = 0;
  for (const client of rooms.get(id)) if (client._client !== 'plugin') n++;
  return n;
}

// Broadcast (not just to the client that just joined/left) since everyone
// already in the room needs their own "N connected" display to update too.
function broadcastCount(id) {
  if (!rooms.has(id)) return;
  const payload = JSON.stringify({ type: 'count', count: rooms.get(id).size });
  for (const client of rooms.get(id)) {
    if (client.readyState === WebSocket.OPEN) client.send(payload);
  }
}

// Same broadcast-to-everyone shape as broadcastCount, reporting the current
// ready-check state (or the inactive state, if readyChecks has no entry for
// this room) so every client's own banner stays in sync. `extra` lets the
// timeout path report `timedOut` plus the ready count as it stood at the
// moment of timeout, since by the time this is called that entry's already
// gone (active/ready would otherwise read back as false/0).
//
// expiresAt is an absolute epoch-ms deadline rather than a "seconds
// remaining" number — clients derive their own live countdown from it
// against their own clock each render tick, instead of the server having to
// push a new message every second just to tick a number down.
function broadcastReadyCheck(id, extra = {}) {
  if (!rooms.has(id)) return;
  const total = webappCount(id);
  const active = readyChecks.has(id);
  const ready = active ? readyChecks.get(id).ready.size : 0;
  const expiresAt = active ? readyChecks.get(id).expiresAt : undefined;
  const payload = JSON.stringify({ type: 'readyCheck', active, ready, total, expiresAt, ...extra });
  for (const client of rooms.get(id)) {
    if (client.readyState === WebSocket.OPEN) client.send(payload);
  }
}

// Central teardown for a room's active check — always goes through here
// (rather than a bare `readyChecks.delete`) so the timeout timer never
// outlives the check it belongs to and fires again for a since-completed
// or since-cancelled room.
function clearReadyCheck(id) {
  if (!readyChecks.has(id)) return;
  clearTimeout(readyChecks.get(id).timer);
  readyChecks.delete(id);
}

const server = http.createServer((_req, res) => { res.writeHead(200); res.end('OK'); });
const wss = new WebSocketServer({ server });

wss.on('connection', (ws) => {
  ws._room = null;
  ws._client = 'webapp'; // overwritten below if create/join's payload says otherwise

  ws.on('message', (raw) => {
    let msg;
    try { msg = JSON.parse(raw); } catch { return; }

    if (msg.type === 'create') {
      let id;
      const requested = typeof msg.room === 'string' ? msg.room.trim().toUpperCase() : '';
      if (requested) {
        if (!/^[A-Z]{4}$/.test(requested)) {
          ws.send(JSON.stringify({ type: 'error', msg: 'Invalid room code. Must be 4 letters.' }));
          return;
        }
        if (rooms.has(requested)) {
          ws.send(JSON.stringify({ type: 'error', msg: 'That room code is already in use.' }));
          return;
        }
        id = requested;
      } else {
        id = genRoomId();
      }
      rooms.set(id, new Set([ws]));
      const password = typeof msg.password === 'string' ? msg.password.trim() : '';
      if (password) roomPasswords.set(id, password);
      ws._room = id;
      if (msg.client === 'plugin') ws._client = 'plugin';
      ws.send(JSON.stringify({ type: 'created', room: id, hasPassword: !!password }));
      broadcastCount(id);

    } else if (msg.type === 'join') {
      const id = (msg.room || '').toUpperCase();
      if (!/^[A-Z]{4}$/.test(id)) {
        ws.send(JSON.stringify({ type: 'error', msg: 'Invalid room code. Must be 4 letters.' }));
        return;
      }
      // Must already exist — a `join` used to silently create the room if
      // the code wasn't taken, which meant there was never a roomPasswords
      // entry to check a password against, so ANY (or no) password on that
      // join would get waved through. Rooms can only come into existence
      // via `create`, which is the only place a password actually gets set.
      if (!rooms.has(id)) {
        ws.send(JSON.stringify({ type: 'error', msg: 'Room not found.' }));
        return;
      }
      const requiredPassword = roomPasswords.get(id);
      if (requiredPassword && (typeof msg.password !== 'string' || msg.password.trim() !== requiredPassword)) {
        ws.send(JSON.stringify({ type: 'error', msg: 'Incorrect password.' }));
        return;
      }
      rooms.get(id).add(ws);
      ws._room = id;
      if (msg.client === 'plugin') ws._client = 'plugin';
      // hasPassword reflects whether the ROOM actually requires one, not
      // whether this joiner happened to type something into that field —
      // without it, a client that typed a password joining an unprotected
      // room (where the server just ignores the field) would have no way
      // to tell it wasn't actually needed, and would wrongly show itself
      // as "password protected".
      ws.send(JSON.stringify({ type: 'joined', room: id, hasPassword: roomPasswords.has(id) }));
      broadcastCount(id);
      // Catches the new (or reconnecting) member up on whatever's already
      // been synced in this room — without this a join always starts blank
      // until someone else happens to change something. Sent only to them,
      // not broadcast, and only if anyone's actually pushed a state yet.
      if (lastState.has(id)) ws.send(JSON.stringify({ type: 'state', state: lastState.get(id) }));

    } else if (msg.type === 'state') {
      if (!ws._room || !rooms.has(ws._room)) return;
      lastState.set(ws._room, msg.state);
      const payload = JSON.stringify(msg);
      for (const client of rooms.get(ws._room)) {
        if (client !== ws && client.readyState === WebSocket.OPEN) client.send(payload);
      }

    } else if (msg.type === 'readyCheck') {
      // Starting a fresh check counts the initiator as already ready —
      // matches the usual in-game ready-check convention (the person who
      // calls it isn't expected to also click their own "ready"). Only
      // true for a webapp initiator, though: a plugin client isn't counted
      // in `total` (see webappCount), so adding it here would let ready
      // outrun total and complete the check a member short.
      if (!ws._room || !rooms.has(ws._room)) return;
      const id = ws._room;
      clearReadyCheck(id); // in case one was already running for this room
      const timer = setTimeout(() => {
        // Still active means nobody completed or cancelled it in time —
        // report how far it actually got before dropping it, so "timed
        // out" isn't indistinguishable from "timed out with everyone
        // ready" (that shouldn't happen given the readyAck completion
        // check, but this is the truth on the wire either way).
        const finalReady = readyChecks.get(id).ready.size;
        clearReadyCheck(id);
        broadcastReadyCheck(id, { timedOut: true, ready: finalReady });
      }, READY_CHECK_TIMEOUT_MS);
      readyChecks.set(id, {
        ready: new Set(ws._client === 'plugin' ? [] : [ws]),
        timer,
        expiresAt: Date.now() + READY_CHECK_TIMEOUT_MS,
      });
      broadcastReadyCheck(id);
      // A solo webapp room (or, now, one where the initiator is the only
      // webapp member with everyone else on the plugin) is already
      // complete the instant it starts — without this it'd sit "active"
      // until the 30s timeout fired and wrongly reported a timeout despite
      // every webapp member already having been ready.
      if (readyChecks.get(id).ready.size >= webappCount(id)) clearReadyCheck(id);

    } else if (msg.type === 'readyAck') {
      const id = ws._room;
      if (!id || !readyChecks.has(id) || ws._client === 'plugin') return;
      readyChecks.get(id).ready.add(ws);
      broadcastReadyCheck(id);
      // Everyone (webapp) has acked — clear it so a subsequent join/leave
      // (or the next readyCheck message) starts clean rather than reusing
      // a spent Set.
      if (readyChecks.get(id).ready.size >= webappCount(id)) clearReadyCheck(id);

    } else if (msg.type === 'readyCancel') {
      const id = ws._room;
      if (!id || !rooms.has(id)) return;
      clearReadyCheck(id);
      broadcastReadyCheck(id);
    }
  });

  ws.on('close', () => {
    if (ws._room && rooms.has(ws._room)) {
      rooms.get(ws._room).delete(ws);
      if (rooms.get(ws._room).size === 0) {
        rooms.delete(ws._room);
        roomPasswords.delete(ws._room);
        clearReadyCheck(ws._room);
        lastState.delete(ws._room);
      } else {
        broadcastCount(ws._room);
        // A departing member could have been the last one not yet acked
        // (completing the check) or the only one who HAD acked (never
        // completing it now that they're gone) — recompute either way.
        if (readyChecks.has(ws._room)) {
          readyChecks.get(ws._room).ready.delete(ws);
          broadcastReadyCheck(ws._room);
          if (readyChecks.get(ws._room).ready.size >= webappCount(ws._room)) clearReadyCheck(ws._room);
        }
      }
    }
  });
});

server.listen(PORT, () => console.log(`WS server listening on :${PORT}`));
