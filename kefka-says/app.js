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

// When k was last set by an INCOMING remote update (see applyShared) —
// used below to stop a race where two people click the same correct call
// at nearly the same instant: if A's click's broadcast reaches B right
// before B's own (independent, already in-flight) click on the same value
// registers, naive toggle-off logic reads "already set to what I clicked"
// and clears it, undoing A's call out from under them. Within this window,
// treat that as B confirming the call, not un-calling it.
const _remoteSetAt = {};
const RACE_WINDOW_MS = 400;

function set(k, v) {
  if (S[k] === v && Date.now() - (_remoteSetAt[k] || 0) < RACE_WINDOW_MS) return;
  S[k] = S[k] === v ? null : v;
  render();
}
function exc(k, v) { S[k] = S[k] === v ? null : v; render(); }

// g1pos/g2pos/g1accel/g2accel are personal (not in SYNC_KEYS — see below),
// so a remote reset() never reached them through the normal state sync.
// _pendingClearDebuffs piggybacks a one-shot flag onto the very next
// syncState() push so every other client clears its own copy too.
const PERSONAL_KEYS = ['g1pos', 'g2pos', 'g1accel', 'g2accel'];
function clearLocalDebuffs() { for (const k of PERSONAL_KEYS) S[k] = k.endsWith('accel') ? false : null; }

// ── Pull history ─────────────────────────────────────────────────────────
// reset() snapshots the state it's about to clear so a misclick (or just
// wanting to see an earlier pull's calls) can be recovered — but only when
// there was actually something to lose, so spamming Reset on an already-
// clear state doesn't fill this with blank entries. Local per browser (like
// the personal debuff fields), not synced to the room.
const PULL_HISTORY_MAX = 20;
const HISTORY_KEYS = Object.keys(S).filter(k => k !== 'enforceOrder');
let pullHistory = []; // most recent first

function isStateEmpty() {
  return HISTORY_KEYS.every(k => S[k] === (k.endsWith('accel') ? false : null));
}

function snapshotState() {
  const snap = { timestamp: Date.now() };
  for (const k of HISTORY_KEYS) snap[k] = S[k];
  return snap;
}

function describeSnapshot(snap) {
  const parts = [];
  const addGco = (n, rf, pos, accel) => {
    if (!rf && !pos && !accel) return;
    const bits = [];
    if (rf) bits.push(rf);
    if (pos) bits.push(pos);
    if (accel) bits.push('accel');
    parts.push(`GCO${n}: ${bits.join(' ')}`);
  };
  addGco(1, snap.g1rf, snap.g1pos, snap.g1accel);
  addGco(2, snap.g2rf, snap.g2pos, snap.g2accel);
  if (snap.it1type || snap.it1rf) parts.push(`Floor: ${snap.it1type || ''} ${snap.it1rf || ''}`.trim());
  if (snap.thunderRF) parts.push(`Thunder: ${snap.thunderRF}`);
  if (snap.blizzardRF) parts.push(`Blizzard: ${snap.blizzardRF}`);
  return parts.length ? parts.join(' | ') : '(empty)';
}

function restoreSnapshot(index) {
  const snap = pullHistory[index];
  if (!snap) return;
  for (const k of HISTORY_KEYS) S[k] = snap[k];
  closeHistoryPanel();
  render();
}

function updateHistoryBadge() {
  const el = document.getElementById('history-count');
  if (el) el.textContent = pullHistory.length;
}

function clearHistory() {
  pullHistory = [];
  renderHistory();
}

function renderHistory() {
  updateHistoryBadge();
  const clearBtn = document.getElementById('history-clear-btn');
  if (clearBtn) clearBtn.disabled = pullHistory.length === 0;
  const list = document.getElementById('history-list');
  if (pullHistory.length === 0) {
    list.innerHTML = '<div class="history-empty">No saved pulls yet.</div>';
    return;
  }
  list.innerHTML = pullHistory.map((snap, i) => {
    const time = new Date(snap.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    return `
      <div class="history-entry">
        <div class="history-time">${time}</div>
        <div class="history-desc">${describeSnapshot(snap)}</div>
        <button class="btn history-restore-btn" onclick="restoreSnapshot(${i})">Restore</button>
      </div>`;
  }).join('');
}

function toggleHistoryPanel() {
  const panel = document.getElementById('history-panel');
  const opening = panel.style.display === 'none';
  panel.style.display = opening ? 'block' : 'none';
  if (opening) renderHistory();
}

function closeHistoryPanel() {
  document.getElementById('history-panel').style.display = 'none';
}

document.addEventListener('click', (e) => {
  const wrap = document.getElementById('history-wrap');
  if (wrap && !wrap.contains(e.target)) closeHistoryPanel();
});

// Piggybacked onto the next Session.syncState() push (see syncState() below)
// so every other client applies the same clear/history entry to their own
// copy — previously only the client that clicked Reset ever got a
// pullHistory entry, since the outgoing state message only carried the
// post-reset (empty) fields, never what was actually cleared.
let _pendingClearDebuffs = false;
let _pendingHistorySnapshot = null;
function reset() {
  if (!isStateEmpty()) {
    const snap = snapshotState();
    pullHistory.unshift(snap);
    if (pullHistory.length > PULL_HISTORY_MAX) pullHistory.length = PULL_HISTORY_MAX;
    updateHistoryBadge();
    _pendingHistorySnapshot = snap;
  }
  for (const k of Object.keys(S)) { if (k !== 'enforceOrder') S[k] = k.endsWith('accel') ? false : null; }
  _pendingClearDebuffs = true;
  render();
}

function recordRemoteHistorySnapshot(snap) {
  pullHistory.unshift(snap);
  if (pullHistory.length > PULL_HISTORY_MAX) pullHistory.length = PULL_HISTORY_MAX;
  updateHistoryBadge();
}

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

  if (!Session.isApplyingRemote()) syncState();
}

// ── Session wiring ───────────────────────────────────────────────────────
// The room/relay/ready-check machinery itself lives in session.js (shared
// with any future webapp tool); this is just Kefka's own plug into it —
// which fields sync, and how to apply/re-emit them.
// Mechanic-wide fields only; player-specific debuffs (g1/g2 pos/accel) stay local
const SYNC_KEYS = ['g1rf', 'g2rf', 'it1type', 'it1rf', 'it2rf', 'thunderRF', 'blizzardRF', 'enforceOrder'];

function sharedState() {
  const out = {};
  for (const k of SYNC_KEYS) out[k] = S[k];
  return out;
}

function applyShared(state) {
  const now = Date.now();
  for (const k of SYNC_KEYS) {
    if (k in state) { S[k] = state[k]; _remoteSetAt[k] = now; }
  }
}

// Thin wrapper so render()'s tail call stays a plain syncState() — attaches
// whatever Reset() left pending (see reset()'s comment) before handing off
// to Session.syncState(), which sends sharedState() plus this extra.
function syncState() {
  const extra = {};
  if (_pendingClearDebuffs) { extra.clearDebuffs = true; _pendingClearDebuffs = false; }
  if (_pendingHistorySnapshot) { extra.historySnapshot = _pendingHistorySnapshot; _pendingHistorySnapshot = null; }
  Session.syncState(extra);
}

function onStateReceived(state) {
  applyShared(state);
  if (state.clearDebuffs) clearLocalDebuffs();
  if (state.historySnapshot) recordRemoteHistorySnapshot(state.historySnapshot);
  render();
}

// ── Icon mode ────────────────────────────────────────────────────────────
// A personal display preference, not a raid-shared fact like enforceOrder —
// stored in localStorage per-browser rather than synced via SYNC_KEYS, so
// one person turning it on doesn't change what anyone else sees.
const ICON_MODE_KEY = 'kefkaIconMode';

// Real/Fake have no in-game equivalent (they're not a game concept, just
// this tracker's own UI), so those stay hand-drawn vector glyphs.
const BTN_VECTOR_ICON = {
  g1r: 'check', g1f: 'cross', g2r: 'check', g2f: 'cross',
  i1r: 'check', i1f: 'cross', i2r: 'check', i2f: 'cross',
  thr: 'check', thf: 'cross', blr: 'check', blf: 'cross',
};

// Water/Lightning/Accel Bomb/Inferno/Tsunami DO have real debuffs behind
// them (Compressed Water / Forked Lightning / Acceleration Bomb / Entropy /
// Dynamic Fluid), so these show the actual game icon instead of a
// hand-drawn approximation — same real-name-lookup principle as the
// Dalamud plugin's GameIcons.cs. Self-hosted under assets/icons/ (originally
// resolved via XIVAPI v2's search/asset endpoints during development) rather
// than fetched from XIVAPI at runtime — these 5 icons never change, so
// there's no reason to depend on a third party being up mid-raid.
const GAME_ICON_PATH = {
  compressedWater: 'assets/icons/compressed-water.png',
  forkedLightning: 'assets/icons/forked-lightning.png',
  accelerationBomb: 'assets/icons/acceleration-bomb.png',
  entropy: 'assets/icons/entropy.png',
  dynamicFluid: 'assets/icons/dynamic-fluid.png',
};
const BTN_GAME_ICON = {
  g1wa: GAME_ICON_PATH.compressedWater, g2wa: GAME_ICON_PATH.compressedWater,
  g1li: GAME_ICON_PATH.forkedLightning, g2li: GAME_ICON_PATH.forkedLightning,
  g1ac: GAME_ICON_PATH.accelerationBomb, g2ac: GAME_ICON_PATH.accelerationBomb,
  t1i: GAME_ICON_PATH.entropy, t2i: GAME_ICON_PATH.entropy,
  t1t: GAME_ICON_PATH.dynamicFluid, t2t: GAME_ICON_PATH.dynamicFluid,
};

// Wraps each mapped button's existing text in a span alongside an injected
// icon span, once at load — render() never touches these buttons'
// text/innerHTML afterward (only className/style), so this is safe to do
// exactly once rather than on every render().
function initIconButtons() {
  for (const [id, key] of Object.entries(BTN_VECTOR_ICON)) {
    const btn = document.getElementById(id);
    if (!btn) continue;
    const text = btn.textContent;
    btn.dataset.icon = 'vector';
    btn.innerHTML = `<span class="btn-text">${text}</span><span class="btn-icon">${IC[key]}</span>`;
  }
  for (const [id, path] of Object.entries(BTN_GAME_ICON)) {
    const btn = document.getElementById(id);
    if (!btn) continue;
    const text = btn.textContent;
    btn.dataset.icon = 'game';
    btn.innerHTML = `<span class="btn-text">${text}</span><span class="btn-icon"><img src="${path}" alt="${text}" loading="lazy"></span>`;
  }
}

function setIconMode(on) {
  document.body.classList.toggle('icon-mode', on);
  localStorage.setItem(ICON_MODE_KEY, on ? '1' : '0');
}

function toggleIconMode() {
  setIconMode(document.getElementById('icon-mode-cb').checked);
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
initIconButtons();
render();

const _iconModeOn = localStorage.getItem(ICON_MODE_KEY) === '1';
document.getElementById('icon-mode-cb').checked = _iconModeOn;
setIconMode(_iconModeOn);

// Registers this tool's fields with the shared session layer, and (as part
// of that) auto-joins if the URL carries ?room=XXXX from a shared link —
// see session.js's init().
Session.init({ getSharedState: sharedState, onStateReceived });
