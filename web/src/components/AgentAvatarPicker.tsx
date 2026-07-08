import { type CSSProperties, useMemo, useState } from 'react';
import {
  AGENT_ACCENT_OPTIONS,
  AGENT_AVATAR_STYLE_OPTIONS,
  AGENT_ICON_OPTIONS,
  agentIdentity,
  avatarCandidateSeeds,
  iconGlyphForKey,
  normalizeAvatarStyle,
  type AgentAvatarStyle,
  type AgentIdentityConfig,
} from '../utils/agentIdentity';
import { AgentIdentityBadge } from './AgentIdentityBadge';

export interface AgentAvatarPickerProps {
  agentId: string;
  name: string;
  value: AgentIdentityConfig;
  disabled?: boolean;
  onChange: (patch: Partial<AgentIdentityConfig>) => void;
}

function baseSeedFor(agentId: string, name: string): string {
  const raw = (agentId || name || 'agent').trim().toLowerCase();
  return raw.replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '') || 'agent';
}

export function AgentAvatarPicker({
  agentId,
  name,
  value,
  disabled = false,
  onChange,
}: AgentAvatarPickerProps) {
  const [seedDeck, setSeedDeck] = useState(0);
  const previewName = name.trim() || agentId || 'Agent';
  const previewIdentity = agentIdentity(previewName, value);
  const selectedStyle = normalizeAvatarStyle(value.avatarStyle) ?? previewIdentity.avatarStyle;
  const seedBase = baseSeedFor(agentId, previewName);
  const avatarSeeds = useMemo(
    () => avatarCandidateSeeds(seedBase, selectedStyle, seedDeck),
    [seedBase, selectedStyle, seedDeck],
  );
  const accentLabels = new Set(AGENT_ACCENT_OPTIONS.map((option) => option.accent.toLowerCase()));
  const customAccent = value.color && !accentLabels.has(value.color.toLowerCase()) ? value.color : null;
  const legacyIcon = !value.iconKey && value.icon?.trim() ? value.icon.trim() : null;

  const updateStyle = (style: AgentAvatarStyle) => {
    onChange({
      avatarStyle: style,
      avatarSeed: value.avatarStyle === style && value.avatarSeed ? value.avatarSeed : avatarCandidateSeeds(seedBase, style, seedDeck)[0],
    });
  };

  return (
    <section className="agent-avatar-picker" aria-label="Agent identity">
      <div className="agent-avatar-picker-preview">
        <div>
          <span className="panel-kicker">Identity</span>
          <h3>Preview</h3>
        </div>
        <div className="agent-avatar-preview-grid">
          <div className="agent-avatar-preview-state">
            <span className="cell-meta">Idle</span>
            <AgentIdentityBadge name={previewName} identity={value} />
          </div>
          <div className="agent-avatar-preview-state">
            <span className="cell-meta">Running</span>
            <AgentIdentityBadge name={previewName} identity={value} isRunning />
          </div>
        </div>
      </div>

      <div className="agent-avatar-control-block">
        <div className="agent-avatar-control-head">
          <span className="panel-kicker">Avatar Family</span>
          <p className="cell-meta">Choose the visual language first, then pick a concrete variant.</p>
        </div>
        <div className="agent-avatar-family-row" role="group" aria-label="Avatar family">
          {AGENT_AVATAR_STYLE_OPTIONS.map((option) => (
            <button
              key={option.value}
              type="button"
              className={`btn ${selectedStyle === option.value ? 'btn-primary' : 'btn-secondary'} agent-avatar-family-button`}
              aria-label={`Avatar family ${option.label}`}
              aria-pressed={selectedStyle === option.value}
              disabled={disabled}
              onClick={() => updateStyle(option.value)}
            >
              {option.label}
            </button>
          ))}
        </div>
      </div>

      <div className="agent-avatar-control-block">
        <div className="agent-avatar-control-head">
          <span className="panel-kicker">Choose Avatar</span>
          <div className="agent-avatar-actions">
            <button
              type="button"
              className="btn btn-secondary"
              disabled={disabled}
              onClick={() => setSeedDeck((current) => current + 1)}
            >
              Shuffle Options
            </button>
            <button
              type="button"
              className="btn btn-secondary"
              disabled={disabled}
              onClick={() => onChange({
                color: undefined,
                avatarStyle: undefined,
                avatarSeed: undefined,
                iconKey: undefined,
                icon: undefined,
              })}
            >
              Reset to Auto
            </button>
          </div>
        </div>
        <div className="agent-avatar-option-grid" role="list" aria-label="Avatar options">
          {avatarSeeds.map((seed, index) => {
            const optionIdentity = {
              ...value,
              avatarStyle: selectedStyle,
              avatarSeed: seed,
            };
            const selected = value.avatarStyle === selectedStyle && value.avatarSeed === seed;

            return (
              <button
                key={seed}
                type="button"
                className={`agent-avatar-option ${selected ? 'agent-avatar-option-selected' : ''}`}
                aria-label={`Choose avatar option ${index + 1}`}
                aria-pressed={selected}
                disabled={disabled}
                onClick={() => onChange({ avatarStyle: selectedStyle, avatarSeed: seed })}
              >
                <AgentIdentityBadge name={previewName} identity={optionIdentity} showName={false} />
                <span className="cell-meta">Option {index + 1}</span>
              </button>
            );
          })}
        </div>
      </div>

      <div className="agent-avatar-control-block">
        <div className="agent-avatar-control-head">
          <span className="panel-kicker">Accent Color</span>
        </div>
        <div className="agent-swatch-row" role="group" aria-label="Accent color">
          {customAccent ? (
            <button
              type="button"
              className={`agent-swatch ${value.color === customAccent ? 'agent-swatch-selected' : ''}`}
              aria-label="Accent Current"
              aria-pressed={value.color === customAccent}
              disabled={disabled}
              style={{ '--swatch-color': customAccent } as CSSProperties}
              onClick={() => onChange({ color: customAccent })}
            >
              <span className="agent-swatch-fill" />
              <span className="sr-only">Current accent</span>
            </button>
          ) : null}
          {AGENT_ACCENT_OPTIONS.map((option) => (
            <button
              key={option.accent}
              type="button"
              className={`agent-swatch ${previewIdentity.accent === option.accent ? 'agent-swatch-selected' : ''}`}
              aria-label={`Accent ${option.label}`}
              aria-pressed={previewIdentity.accent === option.accent}
              disabled={disabled}
              style={{ '--swatch-color': option.accent } as CSSProperties}
              onClick={() => onChange({ color: option.accent })}
            >
              <span className="agent-swatch-fill" />
              <span className="sr-only">{option.label}</span>
            </button>
          ))}
        </div>
      </div>

      <div className="agent-avatar-control-block">
        <div className="agent-avatar-control-head">
          <span className="panel-kicker">Role Badge</span>
        </div>
        <div className="agent-role-badge-grid" role="group" aria-label="Role badge">
          <button
            type="button"
            className={`agent-role-badge-option ${!value.iconKey && !value.icon ? 'agent-role-badge-option-selected' : ''}`}
            aria-label="Role badge None"
            aria-pressed={!value.iconKey && !value.icon}
            disabled={disabled}
            onClick={() => onChange({ iconKey: undefined, icon: undefined })}
          >
            <span className="agent-role-badge-glyph">∅</span>
            <span className="cell-meta">None</span>
          </button>
          {legacyIcon ? (
            <button
              type="button"
              className="agent-role-badge-option agent-role-badge-option-selected"
              aria-label="Role badge Current"
              aria-pressed={true}
              disabled={disabled}
            >
              <span className="agent-role-badge-glyph">{legacyIcon}</span>
              <span className="cell-meta">Current</span>
            </button>
          ) : null}
          {AGENT_ICON_OPTIONS.map((option) => (
            <button
              key={option.key}
              type="button"
              className={`agent-role-badge-option ${value.iconKey === option.key ? 'agent-role-badge-option-selected' : ''}`}
              aria-label={`Role badge ${option.label}`}
              aria-pressed={value.iconKey === option.key}
              disabled={disabled}
              onClick={() => onChange({ iconKey: option.key, icon: iconGlyphForKey(option.key) ?? undefined })}
            >
              <span className="agent-role-badge-glyph">{option.glyph}</span>
              <span className="cell-meta">{option.label}</span>
            </button>
          ))}
        </div>
      </div>
    </section>
  );
}
