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

export interface AgentIdentityConfig {
  color?: string | null;
  icon?: string | null;
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

function normalizeConfiguredColor(color?: string | null): string | null {
  const normalized = color?.trim();
  if (!normalized) return null;
  return /^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i.test(normalized) ? normalized : null;
}

function normalizeConfiguredIcon(icon?: string | null): string | null {
  const normalized = icon?.trim();
  if (!normalized) return null;
  return Array.from(normalized).slice(0, 2).join('');
}

function toRgb(hex: string): { r: number; g: number; b: number } {
  const normalized = hex.length === 4
    ? `#${hex[1]}${hex[1]}${hex[2]}${hex[2]}${hex[3]}${hex[3]}`
    : hex;
  return {
    r: Number.parseInt(normalized.slice(1, 3), 16),
    g: Number.parseInt(normalized.slice(3, 5), 16),
    b: Number.parseInt(normalized.slice(5, 7), 16),
  };
}

function configuredOnAccent(accent: string): string {
  const { r, g, b } = toRgb(accent);
  const brightness = (r * 299 + g * 587 + b * 114) / 1000;
  return brightness > 150 ? '#0E0E0F' : '#F8FAFC';
}

export function normalizeAgentIdentityKey(name: string): string {
  return (name ?? '').trim().toLowerCase();
}

export function agentIdentity(name: string, config?: AgentIdentityConfig): AgentIdentity {
  const key = (name ?? '').trim().toLowerCase();
  const hashValue = hash(key);
  const slot = PALETTE[hashValue % PALETTE.length];
  const accent = normalizeConfiguredColor(config?.color) ?? slot.accent;
  const onAccent = normalizeConfiguredColor(config?.color) ? configuredOnAccent(accent) : slot.onAccent;
  const icon = normalizeConfiguredIcon(config?.icon) ?? ICONS[Math.floor(hashValue / PALETTE.length) % ICONS.length];
  return { accent, onAccent, icon, initials: initialsOf(name ?? '') };
}
