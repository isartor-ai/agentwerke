import { useState } from 'react';
import type { InteractionDelivery, RunInteraction } from '../types';
import { AgentIdentityBadge } from './AgentIdentityBadge';
import { agentIdentity, type AgentIdentityConfig } from '../utils/agentIdentity';

interface ConversationTabProps {
  interactions: RunInteraction[];
  error?: string | null;
  canAnswer: boolean;
  onAnswer: (interactionId: string, answer: string) => Promise<void>;
  onReject: (interactionId: string, reason: string) => Promise<void>;
  onRetryDelivery: (interactionId: string, channel: string) => Promise<void>;
  resolveAgentIdentity?: (name: string) => AgentIdentityConfig | undefined;
}

function addresseeLabel(interaction: RunInteraction): string {
  if (interaction.addresseeType === 'human') return 'you';
  return interaction.addressee ?? 'run';
}

function kindLabel(interaction: RunInteraction): string {
  switch (interaction.kind) {
    case 'post': return 'posted';
    case 'confirm': return 'needs confirmation';
    case 'question':
    case 'choice': return interaction.addresseeType === 'human' ? 'needs you' : 'asked';
    case 'agent_request': return 'agent request';
    case 'notify': return 'note';
    case 'approval': return 'approval';
    case 'tool_access': return 'needs tool access';
    default: return interaction.kind;
  }
}

function channelLabel(channel: string): string {
  return channel.length === 0 ? channel : channel[0].toUpperCase() + channel.slice(1);
}

function timeoutLabel(timeoutAt?: string | null): string | null {
  if (!timeoutAt) return null;
  const remainingMs = new Date(timeoutAt).getTime() - Date.now();
  if (remainingMs <= 0) return 'timeout reached';
  const minutes = Math.max(1, Math.round(remainingMs / 60_000));
  return `expires in ${minutes}m`;
}

export function ConversationTab({
  interactions,
  error,
  canAnswer,
  onAnswer,
  onReject,
  onRetryDelivery,
  resolveAgentIdentity,
}: ConversationTabProps) {
  if (error && interactions.length === 0) return <p className="validation-error">{error}</p>;
  if (interactions.length === 0) return <p>No agent conversation has been recorded for this run yet.</p>;

  return (
    <>
      {error ? <p className="validation-error">{error}</p> : null}
      <ol className="conversation-thread" role="list" aria-label="Agent conversation">
        {interactions.map((interaction) => (
          <ConversationEntry
            key={interaction.id}
            interaction={interaction}
            canAnswer={canAnswer}
            onAnswer={onAnswer}
            onReject={onReject}
            onRetryDelivery={onRetryDelivery}
            resolveAgentIdentity={resolveAgentIdentity}
            linkedDelegation={Boolean(interaction.correlationId && interactions.some(
              (candidate) => candidate.id !== interaction.id && candidate.correlationId === interaction.correlationId,
            ))}
          />
        ))}
      </ol>
    </>
  );
}

interface ConversationEntryProps {
  interaction: RunInteraction;
  canAnswer: boolean;
  onAnswer: (interactionId: string, answer: string) => Promise<void>;
  onReject: (interactionId: string, reason: string) => Promise<void>;
  onRetryDelivery: (interactionId: string, channel: string) => Promise<void>;
  resolveAgentIdentity?: (name: string) => AgentIdentityConfig | undefined;
  linkedDelegation: boolean;
}

function DeliveryState({
  interactionId,
  delivery,
  onRetry,
}: {
  interactionId: string;
  delivery: InteractionDelivery;
  onRetry: (interactionId: string, channel: string) => Promise<void>;
}) {
  const [retrying, setRetrying] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const retry = async () => {
    setRetrying(true);
    setError(null);
    try {
      await onRetry(interactionId, delivery.channel);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delivery retry failed.');
    } finally {
      setRetrying(false);
    }
  };

  if (delivery.status === 'not_supported' && delivery.channel.toLowerCase() === 'teams') {
    return <p className="cell-meta">Teams cannot accept replies from outbound webhook cards. Open Agentwerke to respond.</p>;
  }
  if (delivery.status !== 'failed') return null;

  return (
    <div className="conversation-delivery-error">
      <span>{channelLabel(delivery.channel)} delivery failed after {delivery.attempts} attempt(s).</span>
      {delivery.lastError ? <span className="form-error">{delivery.lastError}</span> : null}
      <button
        type="button"
        className="btn btn-secondary"
        disabled={retrying}
        aria-label={`Retry ${channelLabel(delivery.channel)} delivery`}
        onClick={() => void retry()}
      >
        {retrying ? 'Retrying…' : 'Retry'}
      </button>
      {error ? <span className="form-error">{error}</span> : null}
    </div>
  );
}

function ConversationEntry({
  interaction,
  canAnswer,
  onAnswer,
  onReject,
  onRetryDelivery,
  resolveAgentIdentity,
  linkedDelegation,
}: ConversationEntryProps) {
  const [answer, setAnswer] = useState('');
  const [reason, setReason] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const isPending = interaction.status === 'pending' && interaction.addresseeType === 'human';
  const configuredIdentity = resolveAgentIdentity?.(interaction.from);
  const identity = agentIdentity(interaction.from, configuredIdentity);
  const sentChannels = interaction.requestedChannels?.length
    ? interaction.requestedChannels
    : interaction.deliveries?.map((delivery) => delivery.channel) ?? [];

  const perform = async (action: () => Promise<void>) => {
    if (submitting) return;
    setSubmitting(true);
    setError(null);
    try {
      await action();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to submit response.');
    } finally {
      setSubmitting(false);
    }
  };
  const submit = (value: string) => {
    const trimmed = value.trim();
    if (trimmed) void perform(() => onAnswer(interaction.id, trimmed));
  };
  const reject = () => {
    const trimmed = reason.trim();
    if (trimmed) void perform(() => onReject(interaction.id, trimmed));
  };

  return (
    <li
      className={`conversation-entry conversation-entry-${interaction.status}${interaction.kind === 'agent_request' ? ' conversation-entry-delegation' : ''}${linkedDelegation ? ' conversation-entry-linked' : ''}`}
      data-correlation-id={interaction.correlationId || undefined}
      style={{ borderLeftColor: identity.accent, borderLeftWidth: 3 }}
    >
      <div className="conversation-head">
        <AgentIdentityBadge name={interaction.from} identity={configuredIdentity} className="conversation-identity" />
        <span aria-hidden="true"> → </span>
        <span>{addresseeLabel(interaction)}</span>
        <span className="chip chip-static">{kindLabel(interaction)}</span>
        <span className="cell-meta">{new Date(interaction.createdAt).toLocaleString()}</span>
      </div>
      <p className="conversation-prompt">{interaction.prompt}</p>
      {interaction.stepId ? <span className="cell-meta">step {interaction.stepId}</span> : null}
      {interaction.kind === 'agent_request' ? (
        <p className="cell-meta">
          {interaction.correlationId ? `Delegation exchange ${interaction.correlationId}` : 'Delegation exchange'}
          {interaction.delegationDepth != null ? ` · depth ${interaction.delegationDepth}` : ''}
        </p>
      ) : null}
      {sentChannels.length > 0 ? <p className="cell-meta">Sent to: {sentChannels.join(', ')}</p> : null}
      {isPending && timeoutLabel(interaction.timeoutAt) ? <p className="cell-meta">{timeoutLabel(interaction.timeoutAt)}</p> : null}
      {interaction.response ? <p className="conversation-response">Answer: {interaction.response}</p> : null}
      {interaction.respondedChannel ? (
        <p className="conversation-response">
          Answered via {channelLabel(interaction.respondedChannel)}{interaction.respondedBy ? ` by ${interaction.respondedBy}` : ''}
          {interaction.respondedAt ? <span className="cell-meta"> at {new Date(interaction.respondedAt).toLocaleString()}</span> : null}
        </p>
      ) : null}
      {interaction.status === 'expired' ? (
        <p className="conversation-terminal">
          Expired{interaction.timeoutAt ? ` at ${new Date(interaction.timeoutAt).toLocaleString()}` : ''}
          {interaction.expiresAction ? ` · ${interaction.expiresAction}` : ''}
          {interaction.defaultAnswer ? ` · default answer: ${interaction.defaultAnswer}` : ''}
        </p>
      ) : interaction.status === 'rejected' ? (
        <p className="conversation-terminal">Rejected{interaction.response ? `: ${interaction.response}` : ''}</p>
      ) : interaction.status === 'cancelled' ? (
        <p className="conversation-terminal">Cancelled{interaction.cancelledAt ? ` at ${new Date(interaction.cancelledAt).toLocaleString()}` : ''}</p>
      ) : null}
      {interaction.deliveries?.map((delivery) => (
        <DeliveryState key={delivery.channel} interactionId={interaction.id} delivery={delivery} onRetry={onRetryDelivery} />
      ))}
      {isPending && canAnswer ? (
        <div className="conversation-answer">
          {interaction.kind === 'confirm' ? (
            <>
              <button type="button" className="btn btn-primary" disabled={submitting} onClick={() => submit('approve')}>Approve</button>
              <div className="conversation-answer-row">
                <input
                  type="text"
                  value={reason}
                  placeholder="Reason required to reject"
                  aria-label="Rejection reason"
                  disabled={submitting}
                  onChange={(event) => setReason(event.target.value)}
                />
                <button type="button" className="btn btn-danger" disabled={submitting || !reason.trim()} onClick={reject}>Reject</button>
              </div>
            </>
          ) : interaction.kind === 'choice' && interaction.options.length > 0 ? (
            <div className="tag-row">
              {interaction.options.map((option) => (
                <button key={option} type="button" className="btn btn-secondary" disabled={submitting} onClick={() => submit(option)}>{option}</button>
              ))}
            </div>
          ) : interaction.kind === 'question' ? (
            <div className="conversation-answer-row">
              <input type="text" value={answer} placeholder="Type an answer" aria-label="Answer" disabled={submitting} onChange={(event) => setAnswer(event.target.value)} />
              <button type="button" className="btn btn-primary" disabled={submitting || !answer.trim()} onClick={() => submit(answer)}>Send</button>
            </div>
          ) : null}
          {error ? <p className="form-error">{error}</p> : null}
        </div>
      ) : isPending ? <span className="chip chip-static">Waiting for a response</span> : null}
    </li>
  );
}
