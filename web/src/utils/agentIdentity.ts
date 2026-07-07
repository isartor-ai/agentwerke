/**
 * Deterministic visual identity for an agent (#192 Phase 7).
 *
 * Colour encodes *which* agent, so a multi-agent conversation is scannable at a glance. The
 * mapping is a stable hash of the agent name into a curated palette — no config needed and identical
 * across runs. Colour is always paired with the name and avatar initials, never the sole signal.
 * Accents are mid-tone so they read on the app's dark surface; avatars pair the accent with a dark
 * on-accent text colour from the same ramp.
 */
export interface AgentIdentity {
  /** Accent used for the name, avatar background, and left stripe. */
  accent: string;
  /** Text colour on the accent-filled avatar (darkest stop of the same ramp). */
  onAccent: string;
  /** Stable non-colour icon so identity remains distinct without relying on colour alone. */
  icon: string;
  /** 1–2 letter fallback shown in the avatar. */
  initials: string;
}

// Seven ramps (400 accent / 900 on-accent) — distinct hues, readable on the dark UI.
const PALETTE: ReadonlyArray<{ accent: string; onAccent: string }> = [
  { accent: '#7F77DD', onAccent: '#26215C' }, // purple
  { accent: '#1D9E75', onAccent: '#04342C' }, // teal
  { accent: '#D85A30', onAccent: '#4A1B0C' }, // coral
  { accent: '#D4537E', onAccent: '#4B1528' }, // pink
  { accent: '#378ADD', onAccent: '#042C53' }, // blue
  { accent: '#639922', onAccent: '#173404' }, // green
  { accent: '#BA7517', onAccent: '#412402' }, // amber
];

const ICONS: ReadonlyArray<string> = ['◆', '●', '■', '▲', '✦', '✚', '◇'];

function hash(value: string): number {
  let h = 0;
  for (let i = 0; i < value.length; i += 1) {
    h = (Math.imul(h, 31) + value.charCodeAt(i)) | 0;
  }
  return Math.abs(h);
}

function initialsOf(name: string): string {
  const words = name.trim().split(/[\s._-]+/).filter(Boolean);
  if (words.length === 0) return '?';
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[1][0]).toUpperCase();
}

export function agentIdentity(name: string): AgentIdentity {
  const key = (name ?? '').trim().toLowerCase();
  const hashValue = hash(key);
  const slot = PALETTE[hashValue % PALETTE.length];
  const icon = ICONS[Math.floor(hashValue / PALETTE.length) % ICONS.length];
  return { accent: slot.accent, onAccent: slot.onAccent, icon, initials: initialsOf(name ?? '') };
}
