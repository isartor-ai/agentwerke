import { describe, expect, it } from 'vitest';
import { agentIdentity } from '../utils/agentIdentity';

describe('agentIdentity', () => {
  it('is deterministic and case/whitespace-insensitive for the same agent', () => {
    const a = agentIdentity('planner');
    const b = agentIdentity('  Planner  ');
    expect(a.accent).toBe(b.accent);
    expect(a.onAccent).toBe(b.onAccent);
    expect(a.icon).toBe(b.icon);
  });

  it('gives different agents different accents (best effort across a small set)', () => {
    const names = ['planner', 'coder', 'reviewer', 'security', 'release'];
    const accents = new Set(names.map((n) => agentIdentity(n).accent));
    expect(accents.size).toBeGreaterThan(1);
  });

  it('derives readable initials', () => {
    expect(agentIdentity('planner').initials).toBe('PL');
    expect(agentIdentity('security-reviewer').initials).toBe('SR');
    expect(agentIdentity('qa lead').initials).toBe('QL');
  });

  it('handles empty names without throwing', () => {
    const identity = agentIdentity('');
    expect(identity.initials).toBe('?');
    expect(identity.accent).toMatch(/^#[0-9A-Fa-f]{6}$/);
    expect(identity.icon.length).toBeGreaterThan(0);
  });

  it('prefers configured icon and color when provided', () => {
    const identity = agentIdentity('planner', { color: '#123456', icon: '⚙' });
    expect(identity.accent).toBe('#123456');
    expect(identity.icon).toBe('⚙');
    expect(identity.onAccent).toBe('#F8FAFC');
  });

  it('resolves configured avatar family, seed, and semantic icon key', () => {
    const identity = agentIdentity('planner', {
      color: '#123456',
      avatarStyle: 'pixel',
      avatarSeed: 'planner:pixel:7',
      iconKey: 'shield',
    });

    expect(identity.avatarStyle).toBe('pixel');
    expect(identity.avatarSeed).toBe('planner:pixel:7');
    expect(identity.iconKey).toBe('shield');
    expect(identity.icon).toBe('⛨');
  });

  it('falls back to the deterministic palette when configured color is invalid', () => {
    const configured = agentIdentity('planner', { color: 'teal-ish', icon: '⚙' });
    const fallback = agentIdentity('planner');
    expect(configured.accent).toBe(fallback.accent);
    expect(configured.icon).toBe('⚙');
  });
});
