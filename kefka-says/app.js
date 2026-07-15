// ── State ──────────────────────────────────────────────────────────────────
const S = {
  g1rf: null, g1pos: null, g1accel: false,
  it1type: null, it1rf: null,
  g2rf: null, g2pos: null, g2accel: false,
  it2rf: null,
  thunderRF: null, blizzardRF: null,
  enforceOrder: false,
};

function tog(k)    { S[k] = !S[k]; render(); }
function set(k, v) { S[k] = S[k] === v ? null : v; render(); }
function exc(k, v) { S[k] = S[k] === v ? null : v; render(); }
function reset()   { for (const k of Object.keys(S)) { if (k !== 'enforceOrder') S[k] = k.endsWith('accel') ? false : null; } render(); }

function toggleEnforceOrder() {
  S.enforceOrder = document.getElementById('enforce-order-cb').checked;
  render();
}

function setPos(gco, v) {
  const other = gco === 1 ? 2 : 1;
  const posKey     = `g${gco}pos`;
  const sameAccel  = `g${gco}accel`;
  const otherAccel = `g${other}accel`;
  const otherPos   = `g${other}pos`;
  S[posKey] = S[posKey] === v ? null : v;
  if (S[posKey]) {
    S[sameAccel]  = false;
    S[otherAccel] = true;
    S[otherPos]   = null;
  }
  render();
}

function togAccel(gco) {
  const accelKey   = `g${gco}accel`;
  const posKey     = `g${gco}pos`;
  const otherAccel = `g${gco === 1 ? 2 : 1}accel`;
  S[accelKey] = !S[accelKey];
  if (S[accelKey]) {
    S[posKey]     = null;
    S[otherAccel] = false;
  }
  render();
}

function it2type() { return S.it1type ? (S.it1type === 'inferno' ? 'tsunami' : 'inferno') : null; }

function calcSpread() {
  const chk = (pos, rf) => {
    if (!pos || !rf) return null;
    if (pos === 'water')     return rf === 'fake';
    if (pos === 'lightning') return rf === 'real';
    return null;
  };
  const s1 = chk(S.g1pos, S.g1rf), s2 = chk(S.g2pos, S.g2rf);
  if (s1 === true  || s2 === true)  return true;
  if (s1 === false || s2 === false) return false;
  return null;
}

function calcAccel() {
  const rf = (S.g1accel ? S.g1rf : null) || (S.g2accel ? S.g2rf : null);
  return rf ? (rf === 'real' ? 'still' : 'move') : null;
}

function floorAoe(type, rf) {
  if (!type || !rf) return null;
  return type === 'inferno'
    ? (rf === 'real' ? 'circle' : 'donut')
    : (rf === 'real' ? 'donut'  : 'circle');
}

// ── Render helpers ─────────────────────────────────────────────────────────
function hint(id, cond, text) {
  const el = document.getElementById(id);
  if (cond) el.setAttribute('data-tip', text);
  else el.removeAttribute('data-tip');
}

function btnCls(id, on, key) {
  document.getElementById(id).className = 'btn' + (on ? ` on-${key}` : '');
}

function setCard(iconId, valId, subId, iconSvg, colorCls, valText, subText) {
  const ic = document.getElementById(iconId);
  ic.innerHTML = iconSvg || IC.spread;
  ic.className = 'scard-icon' + (iconSvg ? '' : ' dim');
  ic.style.color = colorCls ? getComputedStyle(document.documentElement).getPropertyValue(colorVar(colorCls)).trim() : '';
  const vl = document.getElementById(valId);
  vl.textContent = valText || '';
  vl.className = 'scard-val ' + (colorCls || 'c-dim');
  if (subId) document.getElementById(subId).textContent = subText || '';
}

function setTBRow(iconId, valId, subId, iconSvg, colorCls, valText, subText) {
  const ic = document.getElementById(iconId);
  ic.innerHTML = iconSvg || ic.dataset.ph || '';
  ic.className = 'tb-icon' + (iconSvg ? '' : ' dim');
  ic.style.color = colorCls ? getComputedStyle(document.documentElement).getPropertyValue(colorVar(colorCls)).trim() : '';
  const vl = document.getElementById(valId);
  vl.textContent = valText || '';
  vl.className = 'tb-val ' + (colorCls || 'c-dim');
  if (subId) document.getElementById(subId).textContent = subText || '';
}

function colorVar(cls) {
  const map = {
    'c-spread':'--col-spread','c-stack':'--col-stack','c-still':'--col-still',
    'c-move':'--col-move','c-in':'--col-in','c-out':'--col-out',
    'c-inf-real':'--col-inferno','c-inf-fake':'--col-inferno',
    'c-tsu-real':'--col-tsunami','c-tsu-fake':'--col-tsunami',
    'c-thr-real':'--col-thunder','c-thr-fake':'--col-thunder',
    'c-blz-real':'--col-blizzard','c-blz-fake':'--col-blizzard',
  };
  return map[cls] || '--text-dim';
}

function setGaze(iconId, valId, rf) {
  const ic = document.getElementById(iconId);
  const vl = document.getElementById(valId);
  if (!rf) {
    ic.innerHTML = IC.gazeIn;
    ic.className = 'tb-icon dim';
    ic.style.color = '';
    vl.textContent = '';
    vl.className = 'tb-val c-dim';
  } else if (rf === 'real') {
    ic.innerHTML = IC.gazeOut;
    ic.className = 'tb-icon';
    ic.style.color = 'var(--col-out)';
    vl.textContent = 'OUT';
    vl.className = 'tb-val c-out';
  } else {
    ic.innerHTML = IC.gazeIn;
    ic.className = 'tb-icon';
    ic.style.color = 'var(--col-in)';
    vl.textContent = 'IN';
    vl.className = 'tb-val c-in';
  }
}

// ── Render ─────────────────────────────────────────────────────────────────
function render() {
  btnCls('g1r',  S.g1rf === 'real',       'real');
  btnCls('g1f',  S.g1rf === 'fake',       'fake');
  btnCls('g1wa', S.g1pos === 'water',     'water');
  btnCls('g1li', S.g1pos === 'lightning', 'lightning');
  btnCls('g1ac', S.g1accel,              'accel');
  btnCls('t1i',  S.it1type === 'inferno', 'inferno');
  btnCls('t1t',  S.it1type === 'tsunami', 'tsunami');
  btnCls('i1r',  S.it1rf === 'real',      'real');
  btnCls('i1f',  S.it1rf === 'fake',      'fake');
  btnCls('g2r',  S.g2rf === 'real',       'real');
  btnCls('g2f',  S.g2rf === 'fake',       'fake');
  btnCls('g2wa', S.g2pos === 'water',     'water');
  btnCls('g2li', S.g2pos === 'lightning', 'lightning');
  btnCls('g2ac', S.g2accel,              'accel');
  btnCls('i2r',  S.it2rf === 'real',      'real');
  btnCls('i2f',  S.it2rf === 'fake',      'fake');
  btnCls('thr',  S.thunderRF === 'real',  'thunder');
  btnCls('thf',  S.thunderRF === 'fake',  'fake');
  btnCls('blr',  S.blizzardRF === 'real', 'blizzard');
  btnCls('blf',  S.blizzardRF === 'fake', 'fake');

  document.getElementById('enforce-order-cb').checked = S.enforceOrder;

  const g1Done  = !!(S.g1pos || S.g1accel);
  const g1Order = S.enforceOrder && !S.g1rf;
  const g1PosOrder = S.enforceOrder && !g1Done;
  document.getElementById('g2r').disabled  = g1Order;
  document.getElementById('g2f').disabled  = g1Order;
  document.getElementById('g2wa').disabled = S.g2accel || g1PosOrder;
  document.getElementById('g2li').disabled = S.g2accel || g1PosOrder;
  document.getElementById('g2ac').disabled = !!S.g2pos || S.g1accel || g1PosOrder;

  // Titled on the wrapping .btn-group, not the buttons themselves —
  // disabled buttons don't fire hover events in most browsers, so a
  // title on a disabled <button> never shows a tooltip.
  hint('g2rf-group',  g1Order,     'Set Grand Cross Omega #1 Cast first');
  hint('g2pos-group', g1PosOrder, 'Set Grand Cross Omega #1 Debuffs first');

  const t2 = it2type();
  btnCls('t2i', t2 === 'inferno', 'inferno');
  btnCls('t2t', t2 === 'tsunami', 'tsunami');
  hint('t2type-group', true, 'Automatically set from Floor AOE #1 type');

  const it1Order = S.enforceOrder && !t2;
  document.getElementById('i2r').disabled = it1Order;
  document.getElementById('i2f').disabled = it1Order;
  hint('i2rf-group', it1Order, 'Set Floor AOE #1 Type first');

  // Position
  const spread = calcSpread();
  if (spread === true)       setCard('si-pos','sv-pos', null, IC.spread, 'c-spread', 'SPREAD');
  else if (spread === false) setCard('si-pos','sv-pos', null, IC.stack,  'c-stack',  'STACK');
  else                       setCard('si-pos','sv-pos', null, null, null, null);

  // Accel Bomb
  const accel = calcAccel();
  if (accel === 'still')     setCard('si-accel','sv-accel', null, IC.still, 'c-still', 'STAY STILL');
  else if (accel === 'move') setCard('si-accel','sv-accel', null, IC.move,  'c-move',  'KEEP MOVING');
  else                       setCard('si-accel','sv-accel', null, null, null, null);

  // Gaze
  setGaze('gi-gz1','gv-gz1', S.g1rf);
  setGaze('gi-gz2','gv-gz2', S.g2rf);

  // Floor AOE
  const infernoRF    = S.it1type === 'inferno' ? S.it1rf : (t2 === 'inferno' ? S.it2rf : null);
  const infernoShape = floorAoe('inferno', infernoRF);
  setTBRow('si-inf','sv-inf','ss-inf',
    infernoShape ? IC[infernoShape] : null,
    infernoShape ? (infernoRF === 'real' ? 'c-inf-real' : 'c-inf-fake') : null,
    infernoShape ? (infernoRF === 'real' ? 'REAL' : 'FAKE') : null,
    infernoShape === 'circle' ? 'Stack → get out' : infernoShape === 'donut' ? 'Stack → stay in' : null);

  const tsunamiRF    = S.it1type === 'tsunami' ? S.it1rf : (t2 === 'tsunami' ? S.it2rf : null);
  const tsunamiShape = floorAoe('tsunami', tsunamiRF);
  setTBRow('si-tsu','sv-tsu','ss-tsu',
    tsunamiShape ? IC[tsunamiShape] : null,
    tsunamiShape ? (tsunamiRF === 'real' ? 'c-tsu-real' : 'c-tsu-fake') : null,
    tsunamiShape ? (tsunamiRF === 'real' ? 'REAL' : 'FAKE') : null,
    tsunamiShape === 'donut' ? 'Stack → stay in' : tsunamiShape === 'circle' ? 'Stack → get out' : null);

  // Thunder & Blizzard
  setTBRow('si-thr','sv-thr', null,
    S.thunderRF  ? (S.thunderRF  === 'real' ? IC.thunder      : IC.thunderFake)  : null,
    S.thunderRF  ? (S.thunderRF  === 'real' ? 'c-thr-real'    : 'c-thr-fake')    : null,
    S.thunderRF  ? (S.thunderRF  === 'real' ? 'REAL'          : 'FAKE')          : null);
  setTBRow('si-blz','sv-blz', null,
    S.blizzardRF ? (S.blizzardRF === 'real' ? IC.blizzard     : IC.blizzardFake) : null,
    S.blizzardRF ? (S.blizzardRF === 'real' ? 'c-blz-real'    : 'c-blz-fake')    : null,
    S.blizzardRF ? (S.blizzardRF === 'real' ? 'REAL'          : 'FAKE')          : null);

  if (!_applying) syncState();
}

// ── Session (WebSocket) ────────────────────────────────────────────────────
// Mechanic-wide fields only; player-specific debuffs (g1/g2 pos/accel) stay local
const SYNC_KEYS = ['g1rf', 'g2rf', 'it1type', 'it1rf', 'it2rf', 'thunderRF', 'blizzardRF', 'enforceOrder'];

// TODO: after Railway deploy, replace the production URL with your actual deployment URL
const WS_URL = (!location.hostname || location.hostname === 'localhost' || location.hostname === '127.0.0.1')
  ? 'ws://localhost:3000'
  : 'wss://xiv-raid-tools-production.up.railway.app';

function sharedState() {
  const out = {};
  for (const k of SYNC_KEYS) out[k] = S[k];
  return out;
}

function applyShared(state) {
  for (const k of SYNC_KEYS) { if (k in state) S[k] = state[k]; }
}

let _applying = false;
let _ws = null;
const P = { sessionId: null, status: 'idle' };

function genRoomId() {
  return Array.from({length: 4}, () => String.fromCharCode(65 + Math.floor(Math.random() * 26))).join('');
}

function connectWS(onopen) {
  if (_ws) { _ws.onclose = null; _ws.close(); }
  _ws = new WebSocket(WS_URL);
  _ws.onopen = onopen;
  _ws.onmessage = (e) => {
    let msg;
    try { msg = JSON.parse(e.data); } catch { return; }
    if (msg.type === 'created' || msg.type === 'joined') {
      P.sessionId = msg.room;
      P.status = 'active';
      renderSession();
    } else if (msg.type === 'state') {
      _applying = true;
      applyShared(msg.state);
      render();
      _applying = false;
    } else if (msg.type === 'error') {
      alert('Session error: ' + msg.msg);
      leaveSession();
    }
  };
  _ws.onclose = () => {
    if (P.status === 'active') {
      P.sessionId = null;
      P.status = 'idle';
      _ws = null;
      renderSession();
    }
  };
}

function createSession() {
  connectWS(() => _ws.send(JSON.stringify({ type: 'create' })));
}

function joinSessionFromInput() {
  const val = document.getElementById('room-input').value.trim().toUpperCase();
  if (val.length !== 4) return;
  connectWS(() => _ws.send(JSON.stringify({ type: 'join', room: val })));
}

function syncState() {
  if (!_ws || _ws.readyState !== WebSocket.OPEN) return;
  _ws.send(JSON.stringify({ type: 'state', state: sharedState() }));
}

function leaveSession() {
  if (_ws) { _ws.onclose = null; _ws.close(); _ws = null; }
  P.sessionId = null; P.status = 'idle';
  renderSession();
}

function copyRoom() {
  if (!P.sessionId) return;
  const url = location.href.split('?')[0] + '?room=' + P.sessionId;
  navigator.clipboard.writeText(url).then(() => {
    const confirm = document.getElementById('copy-confirm');
    confirm.style.display = 'inline';
    setTimeout(() => { confirm.style.display = 'none'; }, 1800);
  }).catch(() => {});
}

function renderSession() {
  document.getElementById('sbar-idle').style.display   = P.status === 'idle'   ? '' : 'none';
  document.getElementById('sbar-active').style.display = P.status === 'active' ? '' : 'none';
  if (P.sessionId) document.getElementById('room-code-val').textContent = P.sessionId;
}

// ── Init ───────────────────────────────────────────────────────────────────
function initPlaceholders() {
  function ph(id, svg) { const el = document.getElementById(id); el.innerHTML = svg; el.dataset.ph = svg; }
  ph('si-pos',   IC.spread);
  ph('si-accel', IC.still);
  ph('si-inf',   IC.circle);
  ph('si-tsu',   IC.donut);
  ph('si-thr',   IC.thunder);
  ph('si-blz',   IC.blizzard);
  document.getElementById('gi-gz1').innerHTML = IC.gazeIn;
  document.getElementById('gi-gz2').innerHTML = IC.gazeIn;
}

initPlaceholders();
render();

// Auto-join if URL contains ?room=XXXX, then clear param so refresh starts fresh
const _roomParam = new URLSearchParams(location.search).get('room');
if (_roomParam && /^[A-Za-z]{4}$/.test(_roomParam)) {
  connectWS(() => _ws.send(JSON.stringify({ type: 'join', room: _roomParam.toUpperCase() })));
  history.replaceState(null, '', location.pathname);
}
