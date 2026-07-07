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
});
