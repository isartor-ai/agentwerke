import type { CSSProperties } from 'react';
import { AgentAvatar } from './AgentAvatar';
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
      <AgentAvatar identity={identity} isRunning={isRunning} />
      {showName ? (
        <span className="agent-identity-name">{name}</span>
      ) : (
        <span className="sr-only">{name}</span>
      )}
    </span>
  );
}
