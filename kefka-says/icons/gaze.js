// Gaze row icons (1st/2nd GCO "look away" vs "look at it").
window.IC = window.IC || {};

IC.gazeIn = `<svg viewBox="0 0 44 44" fill="none">
  <path d="M4,22 Q22,6 40,22 Q22,38 4,22 Z" stroke="currentColor" stroke-width="2.5"/>
  <circle cx="22" cy="22" r="7" fill="currentColor"/>
  <circle cx="25" cy="19" r="2.5" fill="white" opacity="0.4"/>
</svg>`;

IC.gazeOut = `<svg viewBox="0 0 44 44" fill="none">
  <g opacity="0.3">
    <path d="M4,22 Q22,6 40,22 Q22,38 4,22 Z" stroke="currentColor" stroke-width="2.5"/>
    <circle cx="22" cy="22" r="7" fill="currentColor"/>
  </g>
  <line x1="8" y1="8" x2="36" y2="36" stroke="#cc5555" stroke-width="3" stroke-linecap="round"/>
</svg>`;
