// Icon-mode button glyphs - Real/Fake as check/cross. Real game debuffs
// (Water/Lightning/Accel Bomb/Inferno/Tsunami) use actual icons instead
// (see BTN_GAME_ICON in app.js), not vectors.
window.IC = window.IC || {};

IC.check = `<svg viewBox="0 0 44 44" fill="none" stroke="currentColor" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round">
  <polyline points="8,24 18,34 37,10"/>
</svg>`;

IC.cross = `<svg viewBox="0 0 44 44" fill="none" stroke="currentColor" stroke-width="3.5" stroke-linecap="round">
  <line x1="10" y1="10" x2="34" y2="34"/>
  <line x1="34" y1="10" x2="10" y2="34"/>
</svg>`;
