import type { AgentIdentity } from '../utils/agentIdentity';

export interface AgentAvatarProps {
  identity: AgentIdentity;
  isRunning?: boolean;
}

function renderBotAvatar(variant: number) {
  const eyeOffset = 10 + (variant % 3);
  const mouthWidth = 5 + (variant % 4);
  const showAntenna = variant % 2 === 0;

  return (
    <svg className="agent-avatar-svg" viewBox="0 0 32 32" aria-hidden="true">
      {showAntenna ? (
        <>
          <path d="M16 4v4" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
          <circle cx="16" cy="4" r="2" fill="currentColor" />
        </>
      ) : null}
      <rect x="7" y="8" width="18" height="15" rx="4.5" fill="currentColor" />
      <circle cx={eyeOffset} cy="15" r="1.8" fill="var(--agent-accent)" />
      <circle cx={32 - eyeOffset} cy="15" r="1.8" fill="var(--agent-accent)" />
      <path
        d={`M${16 - mouthWidth / 2} 20h${mouthWidth}`}
        stroke="var(--agent-accent)"
        strokeWidth="2"
        strokeLinecap="round"
      />
    </svg>
  );
}

function renderPersonaAvatar(variant: number) {
  const shoulderY = 20 + (variant % 2);

  return (
    <svg className="agent-avatar-svg" viewBox="0 0 32 32" aria-hidden="true">
      <circle cx="16" cy="11.5" r="5.5" fill="currentColor" />
      <path
        d={`M8 ${shoulderY}c2.8-3 6.2-4.5 8-4.5s5.2 1.5 8 4.5v4H8z`}
        fill="currentColor"
      />
    </svg>
  );
}

function renderSketchAvatar(variant: number) {
  const shoulderWidth = 7 + (variant % 3);

  return (
    <svg className="agent-avatar-svg" viewBox="0 0 32 32" aria-hidden="true">
      <circle cx="16" cy="11.5" r="5" fill="none" stroke="currentColor" strokeWidth="2" />
      <path
        d={`M${16 - shoulderWidth} 25c1.5-3.8 5-5.8 7-5.8s5.5 2 7 5.8`}
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path d="M11 10.5c1.3-1.8 3-2.7 5-2.7" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
    </svg>
  );
}

function renderPixelAvatar(variant: number) {
  const cells = Array.from({ length: 16 }, (_, index) => ((variant >> index) & 1) === 1);

  return (
    <svg className="agent-avatar-svg" viewBox="0 0 32 32" aria-hidden="true">
      {cells.map((filled, index) => {
        if (!filled) return null;
        const column = index % 4;
        const row = Math.floor(index / 4);
        return <rect key={index} x={6 + column * 5} y={6 + row * 5} width="4" height="4" rx="0.8" fill="currentColor" />;
      })}
    </svg>
  );
}

function renderAbstractAvatar(variant: number) {
  const ringRadius = 7 + (variant % 2);
  const slashOffset = 10 + (variant % 4);

  return (
    <svg className="agent-avatar-svg" viewBox="0 0 32 32" aria-hidden="true">
      <circle cx="16" cy="16" r={ringRadius} fill="none" stroke="currentColor" strokeWidth="2.4" />
      <path d={`M${slashOffset} 10l12 12`} stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" />
      <path d="M10 22l3.5-3.5" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" />
    </svg>
  );
}

function renderInitialsAvatar(initials: string) {
  return <span className="agent-avatar-initials">{initials}</span>;
}

function renderAvatarGraphic(identity: AgentIdentity) {
  switch (identity.avatarStyle) {
    case 'bot':
      return renderBotAvatar(identity.variant);
    case 'persona':
      return renderPersonaAvatar(identity.variant);
    case 'sketch':
      return renderSketchAvatar(identity.variant);
    case 'pixel':
      return renderPixelAvatar(identity.variant);
    case 'abstract':
      return renderAbstractAvatar(identity.variant);
    case 'initials':
    default:
      return renderInitialsAvatar(identity.initials);
  }
}

export function AgentAvatar({ identity, isRunning = false }: AgentAvatarProps) {
  return (
    <span className={`agent-identity-avatar agent-avatar-${identity.avatarStyle}`} aria-hidden="true">
      {isRunning ? <span className="agent-identity-spinner" /> : null}
      <span className="agent-avatar-graphic">{renderAvatarGraphic(identity)}</span>
      <span className="agent-avatar-role-badge">{identity.icon}</span>
    </span>
  );
}
