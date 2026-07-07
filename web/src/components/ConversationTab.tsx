import { useState } from 'react';
import type { RunInteraction } from '../types';
import { AgentIdentityBadge } from './AgentIdentityBadge';
import { agentIdentity, type AgentIdentityConfig } from '../utils/agentIdentity';

interface ConversationTabProps {
  interactions: RunInteraction[];
  error?: string | null;
  canAnswer: boolean;
  onAnswer: (interactionId: string, answer: string) => Promise<void>;
  resolveAgentIdentity?: (name: string) => AgentIdentityConfig | undefined;
}

function addresseeLabel(interaction: RunInteraction): string {
  if (interaction.addresseeType === 'human') return 'you';
  return interaction.addressee ?? 'run';
}

function kindLabel(interaction: RunInteraction): string {
  switch (interaction.kind) {
    case 'post':
      return 'posted';
    case 'question':
    case 'choice':
      return interaction.addresseeType === 'human' ? 'needs you' : 'asked';
    case 'agent_request':
      return 'asked';
    case 'notify':
      return 'note';
    case 'approval':
      return 'approval';
    default:
      return interaction.kind;
  }
}

export function ConversationTab({
  interactions,
  error,
  canAnswer,
  onAnswer,
  resolveAgentIdentity,
}: ConversationTabProps) {
  if (error && interactions.length === 0) {
    return <p className="validation-error">{error}</p>;
  }

  if (interactions.length === 0) {
    return <p>No agent conversation has been recorded for this run yet.</p>;
  }

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
            resolveAgentIdentity={resolveAgentIdentity}
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
  resolveAgentIdentity?: (name: string) => AgentIdentityConfig | undefined;
}

function ConversationEntry({ interaction, canAnswer, onAnswer, resolveAgentIdentity }: ConversationEntryProps) {
  const [answer, setAnswer] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isPending = interaction.status === 'pending' && interaction.addresseeType === 'human';
  const configuredIdentity = resolveAgentIdentity?.(interaction.from);
  const identity = agentIdentity(interaction.from, configuredIdentity);

  const submit = async (value: string) => {
    if (!value.trim() || submitting) return;
    setSubmitting(true);
    setError(null);
    try {
      await onAnswer(interaction.id, value.trim());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to submit answer.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <li className="conversation-entry" style={{ borderLeftColor: identity.accent, borderLeftWidth: 3 }}>
      <div className="conversation-head">
        <AgentIdentityBadge
          name={interaction.from}
          identity={configuredIdentity}
          className="conversation-identity"
        />
        <span aria-hidden="true"> → </span>
        <span>{addresseeLabel(interaction)}</span>
        <span className="chip chip-static">{kindLabel(interaction)}</span>
        <span className="cell-meta">{new Date(interaction.createdAt).toLocaleString()}</span>
      </div>
      <p className="conversation-prompt">{interaction.prompt}</p>
      {interaction.stepId ? <span className="cell-meta">step {interaction.stepId}</span> : null}
      {interaction.response ? (
        <p className="conversation-response">
          Answer: {interaction.response}
          {interaction.respondedBy ? ` — ${interaction.respondedBy}` : ''}
        </p>
      ) : null}
      {isPending && canAnswer ? (
        <div className="conversation-answer">
          {interaction.options.length > 0 ? (
            <div className="tag-row">
              {interaction.options.map((option) => (
                <button
                  key={option}
                  type="button"
                  className="btn btn-secondary"
                  disabled={submitting}
                  onClick={() => void submit(option)}
                >
                  {option}
                </button>
              ))}
            </div>
          ) : null}
          <div className="conversation-answer-row">
            <input
              type="text"
              value={answer}
              placeholder="Type an answer"
              aria-label="Answer"
              disabled={submitting}
              onChange={(event) => setAnswer(event.target.value)}
            />
            <button
              type="button"
              className="btn btn-primary"
              disabled={submitting || !answer.trim()}
              onClick={() => void submit(answer)}
            >
              Send
            </button>
          </div>
          {error ? <p className="form-error">{error}</p> : null}
        </div>
      ) : isPending ? (
        <span className="chip chip-static">Waiting for a response</span>
      ) : null}
    </li>
  );
}
