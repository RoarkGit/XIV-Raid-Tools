const IC = {
  spread: `<svg viewBox="0 0 44 44" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
    <line x1="22" y1="22" x2="36" y2="8"/><polyline points="29,8 36,8 36,15"/>
    <line x1="22" y1="22" x2="8" y2="36"/><polyline points="15,36 8,36 8,29"/>
    <line x1="22" y1="22" x2="8" y2="8"/><polyline points="15,8 8,8 8,15"/>
    <line x1="22" y1="22" x2="36" y2="36"/><polyline points="29,36 36,36 36,29"/>
  </svg>`,

  stack: `<svg viewBox="0 0 44 44" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
    <line x1="36" y1="8" x2="25" y2="19"/><polyline points="36,15 36,8 29,8"/>
    <line x1="8" y1="36" x2="19" y2="25"/><polyline points="8,29 8,36 15,36"/>
    <line x1="8" y1="8" x2="19" y2="19"/><polyline points="8,15 8,8 15,8"/>
    <line x1="36" y1="36" x2="25" y2="25"/><polyline points="36,29 36,36 29,36"/>
    <circle cx="22" cy="22" r="4" fill="currentColor" stroke="none"/>
  </svg>`,

  still: `<svg viewBox="0 0 44 44">
    <rect x="10" y="9" width="10" height="26" rx="2" fill="currentColor"/>
    <rect x="24" y="9" width="10" height="26" rx="2" fill="currentColor"/>
  </svg>`,

  move: `<svg viewBox="0 0 44 44">
    <polygon points="9,5 9,39 39,22" fill="currentColor"/>
  </svg>`,

  gazeIn: `<svg viewBox="0 0 44 44" fill="none">
    <path d="M4,22 Q22,6 40,22 Q22,38 4,22 Z" stroke="currentColor" stroke-width="2.5"/>
    <circle cx="22" cy="22" r="7" fill="currentColor"/>
    <circle cx="25" cy="19" r="2.5" fill="white" opacity="0.4"/>
  </svg>`,

  gazeOut: `<svg viewBox="0 0 44 44" fill="none">
    <g opacity="0.3">
      <path d="M4,22 Q22,6 40,22 Q22,38 4,22 Z" stroke="currentColor" stroke-width="2.5"/>
      <circle cx="22" cy="22" r="7" fill="currentColor"/>
    </g>
    <line x1="8" y1="8" x2="36" y2="36" stroke="#cc5555" stroke-width="3" stroke-linecap="round"/>
  </svg>`,

  circle: `<svg viewBox="0 0 44 44">
    <circle cx="22" cy="22" r="19" fill="currentColor" opacity="0.85"/>
  </svg>`,

  donut: `<svg viewBox="0 0 44 44">
    <circle cx="22" cy="22" r="14.5" fill="none" stroke="currentColor" stroke-width="9" opacity="0.85"/>
  </svg>`,

  thunder: `<svg viewBox="0 0 44 44" fill="currentColor">
    <polygon points="27,2 13,25 23,25 17,42 31,19 21,19"/>
  </svg>`,

  thunderFake: `<svg viewBox="0 0 44 44">
    <polygon points="27,2 13,25 23,25 17,42 31,19 21,19" fill="currentColor" opacity="0.3"/>
    <line x1="6" y1="6" x2="38" y2="38" stroke="#cc5555" stroke-width="3.5" stroke-linecap="round"/>
  </svg>`,

  blizzard: `<svg viewBox="0 0 44 44" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" fill="none">
    <line x1="22" y1="4" x2="22" y2="40"/>
    <line x1="4" y1="22" x2="40" y2="22"/>
    <line x1="9" y1="9" x2="35" y2="35"/>
    <line x1="35" y1="9" x2="9" y2="35"/>
    <line x1="22" y1="4" x2="16" y2="11"/><line x1="22" y1="4" x2="28" y2="11"/>
    <line x1="22" y1="40" x2="16" y2="33"/><line x1="22" y1="40" x2="28" y2="33"/>
    <line x1="4" y1="22" x2="11" y2="16"/><line x1="4" y1="22" x2="11" y2="28"/>
    <line x1="40" y1="22" x2="33" y2="16"/><line x1="40" y1="22" x2="33" y2="28"/>
  </svg>`,

  blizzardFake: `<svg viewBox="0 0 44 44" fill="none">
    <g stroke="currentColor" stroke-width="2.5" stroke-linecap="round" opacity="0.3">
      <line x1="22" y1="4" x2="22" y2="40"/>
      <line x1="4" y1="22" x2="40" y2="22"/>
      <line x1="9" y1="9" x2="35" y2="35"/>
      <line x1="35" y1="9" x2="9" y2="35"/>
      <line x1="22" y1="4" x2="16" y2="11"/><line x1="22" y1="4" x2="28" y2="11"/>
      <line x1="22" y1="40" x2="16" y2="33"/><line x1="22" y1="40" x2="28" y2="33"/>
      <line x1="4" y1="22" x2="11" y2="16"/><line x1="4" y1="22" x2="11" y2="28"/>
      <line x1="40" y1="22" x2="33" y2="16"/><line x1="40" y1="22" x2="33" y2="28"/>
    </g>
    <line x1="6" y1="6" x2="38" y2="38" stroke="#cc5555" stroke-width="3.5" stroke-linecap="round"/>
  </svg>`,

  // Icon-mode button glyphs (not derived-status icons like the above — these
  // replace the plain "Real"/"Fake"/"Water"/etc. button text). Ported from
  // the Dalamud plugin's Icons.cs; unlike ImGui's draw list, SVG doesn't
  // double-blend overlapping opaque shapes, so the plugin's "seam" concerns
  // don't apply here — the multi-shape constructions (droplet, flame) are
  // safe as-is.
  check: `<svg viewBox="0 0 44 44" fill="none" stroke="currentColor" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round">
    <polyline points="8,24 18,34 37,10"/>
  </svg>`,

  cross: `<svg viewBox="0 0 44 44" fill="none" stroke="currentColor" stroke-width="3.5" stroke-linecap="round">
    <line x1="10" y1="10" x2="34" y2="34"/>
    <line x1="34" y1="10" x2="10" y2="34"/>
  </svg>`,

  droplet: `<svg viewBox="0 0 44 44" fill="currentColor">
    <path d="M22,4 L9,27 A13,13 0 1 0 35,27 Z"/>
  </svg>`,

  bomb: `<svg viewBox="0 0 44 44">
    <line x1="28" y1="14" x2="34" y2="6" stroke="currentColor" stroke-width="3" stroke-linecap="round"/>
    <circle cx="35" cy="5" r="3" fill="currentColor"/>
    <circle cx="20" cy="27" r="14" fill="currentColor"/>
  </svg>`,

  flame: `<svg viewBox="0 0 44 44" fill="currentColor">
    <polygon points="27,3 8,29 34,24"/>
    <circle cx="21" cy="29" r="13"/>
  </svg>`,

  wave: `<svg viewBox="0 0 44 44" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round">
    <path d="M4,28 Q14,14 22,28 Q30,42 40,28"/>
  </svg>`,
};
