import type { CSSProperties } from 'react';
import { agentIdentity, type AgentIdentityConfig } from '../utils/agentIdentity';

export interface AgentIdentityBadgeProps {
  name: string;
  className?: string;
  showName?: boolean;
  identity?: AgentIdentityConfig;
  isRunning?: boolean;
}

export function AgentIdentityBadge({
  name,
  className,
  showName = true,
  identity: configuredIdentity,
  isRunning = false,
}: AgentIdentityBadgeProps) {
  const identity = agentIdentity(name, configuredIdentity);
  const style = {
    '--agent-accent': identity.accent,
    '--agent-on-accent': identity.onAccent,
  } as CSSProperties;
  const classes = ['agent-identity', isRunning ? 'agent-identity-running' : null, className].filter(Boolean).join(' ');

  return (
    <span className={classes} style={style} title={`Agent ${name}`} aria-busy={isRunning || undefined}>
      <span className="agent-identity-avatar" aria-hidden="true">
        {isRunning ? <span className="agent-identity-spinner" /> : null}
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
