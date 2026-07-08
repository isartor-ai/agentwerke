/**
 * Deterministic visual identity for an agent.
 *
 * Colour encodes which agent, so a multi-agent conversation is scannable at a glance. The
 * mapping is a stable hash of the agent name into a curated palette and avatar family.
 * Configured colours, avatar families, seeds, and role badges override the defaults.
 */
export type AgentAvatarStyle = 'bot' | 'persona' | 'sketch' | 'pixel' | 'abstract' | 'initials';

export type AgentIdentityIconKey =
  | 'brain'
  | 'shield'
  | 'cloud'
  | 'wrench'
  | 'flask'
  | 'git-branch'
  | 'compass'
  | 'chart-column'
  | 'search'
  | 'sparkles';

export interface AgentIdentity {
  /** Accent used for the name, avatar background, and left stripe. */
  accent: string;
  /** Text colour on the accent-filled avatar (darkest stop of the same ramp). */
  onAccent: string;
  /** Stable non-colour icon so identity remains distinct without relying on colour alone. */
  icon: string;
  /** Semantic icon key when configured. */
  iconKey: AgentIdentityIconKey | null;
  /** 1–2 letter fallback shown in the avatar. */
  initials: string;
  /** Resolved avatar family. */
  avatarStyle: AgentAvatarStyle;
  /** Deterministic seed for the resolved avatar family. */
  avatarSeed: string;
  /** Stable numeric variant derived from the avatar style + seed. */
  variant: number;
}

export interface AgentIdentityConfig {
  color?: string | null;
  icon?: string | null;
  iconKey?: string | null;
  avatarStyle?: string | null;
  avatarSeed?: string | null;
}

export interface AgentAccentOption {
  label: string;
  accent: string;
  onAccent: string;
}

export interface AgentAvatarStyleOption {
  value: AgentAvatarStyle;
  label: string;
  description: string;
}

export interface AgentIdentityIconOption {
  key: AgentIdentityIconKey;
  label: string;
  glyph: string;
}

// Seven ramps (400 accent / 900 on-accent) — distinct hues, readable on the dark UI.
const PALETTE: ReadonlyArray<AgentAccentOption> = [
  { label: 'Purple', accent: '#7F77DD', onAccent: '#26215C' },
  { label: 'Teal', accent: '#1D9E75', onAccent: '#04342C' },
  { label: 'Coral', accent: '#D85A30', onAccent: '#4A1B0C' },
  { label: 'Pink', accent: '#D4537E', onAccent: '#4B1528' },
  { label: 'Blue', accent: '#378ADD', onAccent: '#042C53' },
  { label: 'Green', accent: '#639922', onAccent: '#173404' },
  { label: 'Amber', accent: '#BA7517', onAccent: '#412402' },
];

const FALLBACK_ICONS: ReadonlyArray<string> = ['◆', '●', '■', '▲', '✦', '✚', '◇'];
const AUTO_AVATAR_STYLES: ReadonlyArray<AgentAvatarStyle> = ['bot', 'persona', 'sketch', 'pixel', 'abstract', 'initials'];

export const AGENT_ACCENT_OPTIONS = PALETTE;

export const AGENT_AVATAR_STYLE_OPTIONS: ReadonlyArray<AgentAvatarStyleOption> = [
  { value: 'bot', label: 'Bot', description: 'System-like and machine-native.' },
  { value: 'persona', label: 'Persona', description: 'Human-friendly specialist.' },
  { value: 'sketch', label: 'Sketch', description: 'Editorial and analytical.' },
  { value: 'pixel', label: 'Pixel', description: 'Playful and technical.' },
  { value: 'abstract', label: 'Abstract', description: 'Minimal geometric mark.' },
  { value: 'initials', label: 'Initials', description: 'Simple fallback monogram.' },
];

export const AGENT_ICON_OPTIONS: ReadonlyArray<AgentIdentityIconOption> = [
  { key: 'brain', label: 'Brain', glyph: '✺' },
  { key: 'shield', label: 'Shield', glyph: '⛨' },
  { key: 'cloud', label: 'Cloud', glyph: '☁' },
  { key: 'wrench', label: 'Wrench', glyph: '⚙' },
  { key: 'flask', label: 'Flask', glyph: '⚗' },
  { key: 'git-branch', label: 'Git Branch', glyph: '⎇' },
  { key: 'compass', label: 'Compass', glyph: '⌖' },
  { key: 'chart-column', label: 'Chart', glyph: '▥' },
  { key: 'search', label: 'Search', glyph: '⌕' },
  { key: 'sparkles', label: 'Sparkles', glyph: '✦' },
];

const ICON_GLYPHS = new Map<AgentIdentityIconKey, string>(
  AGENT_ICON_OPTIONS.map((option) => [option.key, option.glyph]),
);

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

function normalizeConfiguredScalar(value?: string | null): string | null {
  const normalized = value?.trim();
  return normalized ? normalized : null;
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

export function normalizeAvatarStyle(style?: string | null): AgentAvatarStyle | null {
  const normalized = normalizeConfiguredScalar(style);
  if (!normalized) return null;

  const matched = AGENT_AVATAR_STYLE_OPTIONS.find((option) => option.value === normalized);
  return matched?.value ?? null;
}

export function normalizeIconKey(key?: string | null): AgentIdentityIconKey | null {
  const normalized = normalizeConfiguredScalar(key);
  if (!normalized) return null;

  const matched = AGENT_ICON_OPTIONS.find((option) => option.key === normalized);
  return matched?.key ?? null;
}

export function iconGlyphForKey(key?: string | null): string | null {
  const normalized = normalizeIconKey(key);
  return normalized ? (ICON_GLYPHS.get(normalized) ?? null) : null;
}

export function normalizeAgentIdentityKey(name: string): string {
  return (name ?? '').trim().toLowerCase();
}

export function avatarCandidateSeeds(baseSeed: string, style: AgentAvatarStyle, deck = 0): string[] {
  const normalizedBase = normalizeAgentIdentityKey(baseSeed).replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '') || 'agent';
  return Array.from({ length: 8 }, (_, index) => `${normalizedBase}:${style}:${deck}:${index}`);
}

function autoAvatarStyle(hashValue: number): AgentAvatarStyle {
  return AUTO_AVATAR_STYLES[Math.floor(hashValue / PALETTE.length) % AUTO_AVATAR_STYLES.length];
}

export function agentIdentity(name: string, config?: AgentIdentityConfig): AgentIdentity {
  const key = normalizeAgentIdentityKey(name);
  const hashValue = hash(key || 'agent');
  const slot = PALETTE[hashValue % PALETTE.length];
  const accent = normalizeConfiguredColor(config?.color) ?? slot.accent;
  const onAccent = normalizeConfiguredColor(config?.color) ? configuredOnAccent(accent) : slot.onAccent;
  const iconKey = normalizeIconKey(config?.iconKey);
  const icon = iconGlyphForKey(iconKey)
    ?? normalizeConfiguredIcon(config?.icon)
    ?? FALLBACK_ICONS[Math.floor(hashValue / PALETTE.length) % FALLBACK_ICONS.length];
  const avatarStyle = normalizeAvatarStyle(config?.avatarStyle) ?? autoAvatarStyle(hashValue);
  const avatarSeed = normalizeConfiguredScalar(config?.avatarSeed) ?? `${key || 'agent'}:${avatarStyle}:auto`;
  return {
    accent,
    onAccent,
    icon,
    iconKey,
    initials: initialsOf(name ?? ''),
    avatarStyle,
    avatarSeed,
    variant: hash(`${avatarStyle}:${avatarSeed}`),
  };
}
