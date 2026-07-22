// ── Session ──────────────────────────────────────────────────────────────
// Generic room/relay-connection layer: create/join/leave, room passwords,
// connected count, ready check. Deliberately has NO knowledge of Kefka
// Says' own mechanic fields (g1rf, it1type, etc.) — a tool wires itself in
// via Session.init({ getSharedState, onStateReceived }) and everything
// tool-specific (SYNC_KEYS, applying remote state, pull history) stays in
// that tool's own script. This is what lets a second webapp tool reuse the
// exact same room/password/ready-check machinery without copy-pasting it.
//
// Expects the DOM to provide the same session-bar/ready-check-banner
// element IDs kefka-says/index.html does (sbar-idle, sbar-active,
// room-code-val, room-count, room-lock, copy-confirm, room-input,
// room-password-input, ready-check-banner, ready-check-count,
// ready-check-actions, ready-check-btn) — any tool embedding this module
// needs that same markup, not just the script tag.
//
// Speaks the same wire protocol as server/index.js and the Dalamud
// plugin's SessionClient.cs: {type:'create'|'join', room?, password?,
// client:'webapp'} -> {type:'created'|'joined', room} / {type:'error', msg}
// {type:'state', state:{...}} broadcast to the rest of the room
// {type:'count', count} / {type:'readyCheck', active, ready, total, expiresAt?, timedOut?}
const Session = (() => {
  const WS_URL = (!location.hostname || location.hostname === 'localhost' || location.hostname === '127.0.0.1')
    ? 'ws://localhost:3000'
    : 'wss://xiv-raid-tools-production.up.railway.app';

  let _ws = null;
  // True while dispatching an incoming 'state' message to the tool's
  // onStateReceived callback — the tool's own render/mutation cycle checks
  // this (via isApplyingRemote()) before syncing back out, so applying a
  // remote update never immediately bounces right back to the room.
  let _applying = false;

  // The room we intend to stay connected to — null means "no reconnect
  // wanted" (idle, or the user hit Leave). Set once a create/join is
  // actually confirmed (mirrors the Dalamud plugin's SessionClient
  // _desiredRoom), so a later drop always retries via `join`, even for a
  // session that started with `create`.
  let _desiredRoom = null;
  let _reconnectAttempt = 0;
  let _reconnectTimer = null;
  const RECONNECT_DELAYS = [1000, 2000, 5000, 10000, 20000];

  // Supplied by the embedding tool via init() — getSharedState() returns
  // this tool's own synced-fields object on demand (called fresh on every
  // syncState()), onStateReceived(state) applies an incoming state payload
  // to the tool's own model (including any extra keys it piggybacked, like
  // Kefka's clearDebuffs/historySnapshot).
  let _getSharedState = () => ({});
  let _onStateReceived = () => {};

  const P = { sessionId: null, status: 'idle', count: 1, password: null };

  // Anonymous, room-wide (not per-name — the room has no concept of who's
  // who): a count of how many connected clients have acked vs. the total,
  // while a check is active. Mirrors P's reset points (leaveSession/onclose).
  const RC = { active: false, ready: 0, total: 0, expiresAt: null };
  // Set only on the server's timeout broadcast (see server/index.js's
  // READY_CHECK_TIMEOUT_MS) — a transient phase shown for a few seconds
  // after RC.active has already gone false, same idea as the all-ready fade.
  let _rcTimedOut = false;
  let _rcFadeTimer = null;
  // Ticks the countdown text once a second while a check is active — the
  // server only pushes a new message on actual state changes (join/ack/
  // cancel/timeout), not once a second, so nothing else would advance
  // "Xs left" between those.
  let _rcCountdownInterval = null;
  // Whether THIS client has already acked — the server only broadcasts
  // aggregate counts (ready/total), never who specifically, so this is purely
  // local bookkeeping. Set for the initiator too, since starting a check
  // counts as their own ack server-side (see server/index.js's readyCheck
  // handler) — without this they'd see their own "I'm Ready" button still
  // sitting there despite already being counted.
  let _iAmReady = false;

  function playReadyCheckBeep() {
    try {
      const ctx = new (window.AudioContext || window.webkitAudioContext)();
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.connect(gain); gain.connect(ctx.destination);
      osc.frequency.value = 880;
      gain.gain.setValueAtTime(0.15, ctx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.4);
      osc.start(); osc.stop(ctx.currentTime + 0.4);
    } catch {}
  }

  function renderReadyCheck() {
    const banner = document.getElementById('ready-check-banner');
    const actions = document.getElementById('ready-check-actions');

    if (RC.active) {
      const allReady = RC.ready >= RC.total;
      banner.style.display = 'flex';
      banner.classList.toggle('all-ready', allReady);
      banner.classList.remove('timed-out');
      let text = allReady ? 'Everyone ready!' : `Ready check: ${RC.ready}/${RC.total} ready`;
      if (!allReady && RC.expiresAt) {
        const secondsLeft = Math.max(0, Math.ceil((RC.expiresAt - Date.now()) / 1000));
        text += ` · ${secondsLeft}s left`;
      }
      document.getElementById('ready-check-count').textContent = text;
      actions.style.display = '';
      document.getElementById('ready-check-btn').style.display = (allReady || _iAmReady) ? 'none' : '';
      return;
    }

    if (_rcTimedOut) {
      banner.style.display = 'flex';
      banner.classList.remove('all-ready');
      banner.classList.add('timed-out');
      document.getElementById('ready-check-count').textContent = `Ready check timed out · ${RC.ready}/${RC.total} ready`;
      actions.style.display = 'none'; // nothing left to ack or cancel
      return;
    }

    banner.style.display = 'none';
  }

  function applyReadyCheck(msg) {
    const wasActive = RC.active;
    clearTimeout(_rcFadeTimer);
    RC.active = msg.active;
    RC.ready = msg.ready;
    RC.total = msg.total;
    RC.expiresAt = msg.expiresAt || null;
    // Explicit end (cancel/timeout) — the local-fade branches below handle
    // resetting it for the "everyone's ready" ending instead, since that one
    // never arrives as its own active:false message (see the comment below).
    if (!RC.active) _iAmReady = false;
    _rcTimedOut = !RC.active && !!msg.timedOut;
    if (RC.active && !wasActive) playReadyCheckBeep();
    renderReadyCheck();

    // Only worth ticking while there's an actual countdown on screen — once
    // everyone's ready the text no longer shows one (see renderReadyCheck),
    // and leaving the interval running through that fade would otherwise
    // never get cleared (that fade resolves via its own setTimeout below, not
    // by coming back through this function).
    clearInterval(_rcCountdownInterval);
    _rcCountdownInterval = (RC.active && RC.ready < RC.total) ? setInterval(renderReadyCheck, 1000) : null;

    // The server clears its own state the instant everyone's acked (or a
    // check times out), so these are the LAST frames we'll get for either —
    // fade them out locally after a beat rather than leaving them on screen
    // until the next check.
    if (RC.active && RC.ready >= RC.total) {
      _rcFadeTimer = setTimeout(() => { RC.active = false; _iAmReady = false; renderReadyCheck(); }, 3000);
    } else if (_rcTimedOut) {
      _rcFadeTimer = setTimeout(() => { _rcTimedOut = false; renderReadyCheck(); }, 4000);
    }
  }

  function resetReadyCheck() {
    clearTimeout(_rcFadeTimer);
    clearInterval(_rcCountdownInterval);
    _rcCountdownInterval = null;
    RC.active = false; RC.ready = 0; RC.total = 0; RC.expiresAt = null;
    _rcTimedOut = false;
    _iAmReady = false;
    renderReadyCheck();
  }

  function startReadyCheck() {
    if (!_ws || _ws.readyState !== WebSocket.OPEN) return;
    // The server auto-marks the initiator ready (see server/index.js's
    // readyCheck handler) — set optimistically here rather than waiting for
    // the broadcast to round-trip back, same reasoning as ackReady below.
    _iAmReady = true;
    _ws.send(JSON.stringify({ type: 'readyCheck' }));
  }

  function ackReady() {
    if (!_ws || _ws.readyState !== WebSocket.OPEN) return;
    _iAmReady = true;
    _ws.send(JSON.stringify({ type: 'readyAck' }));
  }

  function cancelReadyCheck() {
    if (!_ws || _ws.readyState !== WebSocket.OPEN) return;
    _ws.send(JSON.stringify({ type: 'readyCancel' }));
  }

  // onError defaults to the normal user-facing "session error" path
  // (alert + drop to idle) — the reconnect flow below passes its own
  // handler instead, since silently alert()ing on every automatic retry
  // would be obnoxious, and a failed retry needs to try something else
  // rather than giving up outright.
  function connectWS(onopen, onError) {
    if (_ws) { _ws.onclose = null; _ws.close(); }
    _ws = new WebSocket(WS_URL);
    // A connection failure (e.g. the relay process itself being down, mid
    // reconnect) fires 'error' then 'close' — onclose's reconnect logic is
    // what actually matters, this just needs to exist so the error itself
    // isn't left unhandled.
    _ws.onerror = () => {};
    _ws.onopen = onopen;
    _ws.onmessage = (e) => {
      let msg;
      try { msg = JSON.parse(e.data); } catch { return; }
      if (msg.type === 'created' || msg.type === 'joined') {
        P.sessionId = msg.room;
        P.status = 'active';
        // hasPassword reflects whether the ROOM actually requires one, not
        // whether we happened to type something into that field — without
        // this, joining an unprotected room after typing a (server-ignored)
        // password would wrongly keep showing "🔒 Password protected".
        if (!msg.hasPassword) P.password = null;
        _desiredRoom = P.sessionId;
        _reconnectAttempt = 0;
        renderSession();
      } else if (msg.type === 'count') {
        P.count = msg.count;
        renderSession();
      } else if (msg.type === 'readyCheck') {
        applyReadyCheck(msg);
      } else if (msg.type === 'state') {
        _applying = true;
        _onStateReceived(msg.state);
        _applying = false;
      } else if (msg.type === 'error') {
        (onError || defaultError)(msg.msg);
      }
    };
    _ws.onclose = () => {
      _ws = null;
      // _desiredRoom is the actual "should we still be connected somewhere"
      // signal, not P.status — status flips to 'reconnecting' the moment a
      // retry starts, so gating on 'active' here would silently swallow a
      // FAILED reconnect attempt's own close/error (e.g. the relay still
      // being down) instead of scheduling the next one. leaveSession()
      // clears _desiredRoom (and nulls this handler) before closing, so a
      // manual Leave/Give-up never reaches this branch at all.
      if (_desiredRoom) {
        scheduleReconnect();
      } else if (P.status !== 'idle') {
        P.sessionId = null;
        P.status = 'idle';
        P.count = 1;
        P.password = null;
        renderSession();
        resetReadyCheck();
      }
    };
  }

  let _errorFadeTimer = null;
  function showSessionError(msg) {
    clearTimeout(_errorFadeTimer);
    const el = document.getElementById('session-error');
    el.textContent = msg;
    el.style.display = 'block';
    _errorFadeTimer = setTimeout(() => { el.style.display = 'none'; }, 5000);
  }

  function hideSessionError() {
    clearTimeout(_errorFadeTimer);
    document.getElementById('session-error').style.display = 'none';
  }

  function defaultError(msg) {
    showSessionError(msg);
    leaveSession();
  }

  // Drop triggers this automatically (see connectWS's onclose); a "Give up"
  // click while reconnecting calls it directly via giveUpReconnect. Retries
  // `join` against the room we were last confirmed in; if the room's gone
  // (the relay process itself restarted and wiped its in-memory rooms —
  // see server/index.js), falls back to `create`-ing that exact code right
  // back so everyone else's own `join` retry lands back in the same room.
  // Whoever gets there first "wins" the recreate; anyone who loses that race
  // just retries `join` again on their next backoff tick, which by then
  // succeeds against the room the winner just stood back up.
  function scheduleReconnect() {
    if (!_desiredRoom) return;
    P.status = 'reconnecting';
    renderSession();
    const delay = RECONNECT_DELAYS[Math.min(_reconnectAttempt, RECONNECT_DELAYS.length - 1)];
    _reconnectAttempt++;
    clearTimeout(_reconnectTimer);
    _reconnectTimer = setTimeout(reconnectAttempt, delay);
  }

  function reconnectAttempt() {
    if (!_desiredRoom) return; // superseded by a manual Leave/Join/Create meanwhile
    const room = _desiredRoom;
    connectWS(
      () => _ws.send(JSON.stringify({ type: 'join', room, password: P.password || undefined, client: 'webapp' })),
      () => {
        if (_desiredRoom !== room) return;
        connectWS(
          () => _ws.send(JSON.stringify({ type: 'create', room, password: P.password || undefined, client: 'webapp' })),
          () => { if (_desiredRoom === room) scheduleReconnect(); },
        );
      },
    );
  }


  // One button instead of separate Create/Join — typing nothing and
  // clicking it creates a fresh (random-code) room, same as Create used to.
  // Typing a code tries to join it, falling back to creating that exact
  // code only if it doesn't exist yet (not on any other error — a wrong
  // password shouldn't silently spin up an unrelated empty room). Two
  // people independently typing the same code and both landing in one room
  // is the whole point of a shared code, not a collision to guard against.
  //
  // 'client: webapp' tells the relay this connection counts toward ready
  // check (see server/index.js's webappCount) — the Dalamud plugin identifies
  // itself as 'plugin' instead and is excluded, since the game already has
  // its own ready check.
  function joinOrCreate() {
    hideSessionError();
    const room = document.getElementById('room-input').value.trim().toUpperCase();
    const pw = document.getElementById('room-password-input').value;
    P.password = pw || null;

    if (!room) {
      connectWS(() => _ws.send(JSON.stringify({ type: 'create', password: pw || undefined, client: 'webapp' })));
      return;
    }
    if (room.length !== 4) return;

    connectWS(
      () => _ws.send(JSON.stringify({ type: 'join', room, password: pw || undefined, client: 'webapp' })),
      (errorMsg) => {
        if (errorMsg === 'Room not found.') {
          connectWS(() => _ws.send(JSON.stringify({ type: 'create', room, password: pw || undefined, client: 'webapp' })));
        } else {
          defaultError(errorMsg);
        }
      },
    );
  }

  // extra carries whatever one-shot flags the tool wants to piggyback onto
  // this specific push (Kefka's clearDebuffs/historySnapshot) — see
  // getSharedState's comment. Merged on top of the tool's own synced
  // fields, not the other way around, so a key collision favors the extra.
  function syncState(extra) {
    if (!_ws || _ws.readyState !== WebSocket.OPEN) return;
    const state = _getSharedState();
    if (extra) Object.assign(state, extra);
    _ws.send(JSON.stringify({ type: 'state', state }));
  }

  function leaveSession() {
    clearTimeout(_reconnectTimer);
    _desiredRoom = null; // must clear before closing, or onclose schedules a reconnect
    if (_ws) { _ws.onclose = null; _ws.close(); _ws = null; }
    P.sessionId = null; P.status = 'idle'; P.count = 1; P.password = null;
    renderSession();
    resetReadyCheck();
  }

  function copyRoom() {
    if (!P.sessionId) return;
    // Bundling the password into the share link (rather than making whoever
    // clicks it type it in separately) is the whole point of tying it to the
    // room code in the first place — a link a raid leader drops in Discord
    // should just work for the raid, not add a second secret to relay.
    let url = location.href.split('?')[0] + '?room=' + P.sessionId;
    if (P.password) url += '&pw=' + encodeURIComponent(P.password);
    navigator.clipboard.writeText(url).then(() => {
      const confirm = document.getElementById('copy-confirm');
      confirm.style.display = 'inline';
      setTimeout(() => { confirm.style.display = 'none'; }, 1800);
    }).catch(() => {});
  }

  function renderSession() {
    document.getElementById('sbar-idle').style.display         = P.status === 'idle'         ? '' : 'none';
    document.getElementById('join-hint').style.display         = P.status === 'idle'         ? '' : 'none';
    document.getElementById('sbar-active').style.display       = P.status === 'active'       ? '' : 'none';
    document.getElementById('sbar-reconnecting').style.display = P.status === 'reconnecting' ? '' : 'none';
    if (P.status === 'reconnecting') {
      document.getElementById('reconnecting-room').textContent = _desiredRoom || '';
    }
    if (P.sessionId) document.getElementById('room-code-val').textContent = P.sessionId;
    document.getElementById('room-count').textContent = `· ${P.count} connected`;
    // Revealed on hover via the native title tooltip — not sensitive enough
    // to hide from someone already in the room, since the share link
    // already carries it in plain text anyway (see copyRoom() below).
    const lock = document.getElementById('room-lock');
    lock.style.display = P.password ? '' : 'none';
    lock.title = P.password ? `Password: ${P.password}` : '';
  }

  // Registers the tool's callbacks, then auto-joins if the URL carries
  // ?room=XXXX (and optionally &pw=...) from a shared link — must run AFTER
  // the callbacks are registered, since a fast 'joined'/'state' response
  // could otherwise arrive before onStateReceived exists. Clears both
  // params afterward so a refresh starts fresh instead of re-joining.
  function init({ getSharedState, onStateReceived }) {
    _getSharedState = getSharedState;
    _onStateReceived = onStateReceived;

    const roomParam = new URLSearchParams(location.search).get('room');
    const pwParam = new URLSearchParams(location.search).get('pw');
    if (roomParam && /^[A-Za-z]{4}$/.test(roomParam)) {
      P.password = pwParam || null;
      connectWS(() => _ws.send(JSON.stringify({ type: 'join', room: roomParam.toUpperCase(), password: pwParam || undefined, client: 'webapp' })));
      history.replaceState(null, '', location.pathname);
    }
  }

  return {
    init,
    isApplyingRemote: () => _applying,
    syncState,
    joinOrCreate,
    leaveSession,
    copyRoom,
    startReadyCheck,
    ackReady,
    cancelReadyCheck,
  };
})();
