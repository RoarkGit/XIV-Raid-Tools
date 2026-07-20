// Position (Spread/Stack) and Accel Bomb (Still/Move) card icons.
window.IC = window.IC || {};

IC.spread = `<svg viewBox="0 0 44 44" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
  <line x1="22" y1="22" x2="36" y2="8"/><polyline points="29,8 36,8 36,15"/>
  <line x1="22" y1="22" x2="8" y2="36"/><polyline points="15,36 8,36 8,29"/>
  <line x1="22" y1="22" x2="8" y2="8"/><polyline points="15,8 8,8 8,15"/>
  <line x1="22" y1="22" x2="36" y2="36"/><polyline points="29,36 36,36 36,29"/>
</svg>`;

IC.stack = `<svg viewBox="0 0 44 44" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
  <line x1="36" y1="8" x2="25" y2="19"/><polyline points="36,15 36,8 29,8"/>
  <line x1="8" y1="36" x2="19" y2="25"/><polyline points="8,29 8,36 15,36"/>
  <line x1="8" y1="8" x2="19" y2="19"/><polyline points="8,15 8,8 15,8"/>
  <line x1="36" y1="36" x2="25" y2="25"/><polyline points="36,29 36,36 29,36"/>
  <circle cx="22" cy="22" r="4" fill="currentColor" stroke="none"/>
</svg>`;

IC.still = `<svg viewBox="0 0 44 44">
  <rect x="10" y="9" width="10" height="26" rx="2" fill="currentColor"/>
  <rect x="24" y="9" width="10" height="26" rx="2" fill="currentColor"/>
</svg>`;

IC.move = `<svg viewBox="0 0 44 44">
  <polygon points="9,5 9,39 39,22" fill="currentColor"/>
</svg>`;
