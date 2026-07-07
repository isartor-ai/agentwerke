import type { CSSProperties } from 'react';
import { agentIdentity } from '../utils/agentIdentity';

export interface AgentIdentityBadgeProps {
  name: string;
  className?: string;
  showName?: boolean;
}

export function AgentIdentityBadge({ name, className, showName = true }: AgentIdentityBadgeProps) {
  const identity = agentIdentity(name);
  const style = {
    '--agent-accent': identity.accent,
    '--agent-on-accent': identity.onAccent,
  } as CSSProperties;
  const classes = ['agent-identity', className].filter(Boolean).join(' ');

  return (
    <span className={classes} style={style} title={`Agent ${name}`}>
      <span className="agent-identity-avatar" aria-hidden="true">
        <span className="agent-identity-icon">{identity.icon}</span>
      </span>
      {showName ? (
        <span className="agent-identity-name">{name}</span>
      ) : (
        <span className="sr-only">{name}</span>
      )}
    </span>
  );
}
