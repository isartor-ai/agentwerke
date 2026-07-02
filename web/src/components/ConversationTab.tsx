import { useState } from 'react';
import type { RunInteraction } from '../types';
import { agentIdentity } from '../utils/agentIdentity';

interface ConversationTabProps {
  interactions: RunInteraction[];
  canAnswer: boolean;
  onAnswer: (interactionId: string, answer: string) => Promise<void>;
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

export function ConversationTab({ interactions, canAnswer, onAnswer }: ConversationTabProps) {
  if (interactions.length === 0) {
    return <p>No agent conversation has been recorded for this run yet.</p>;
  }

  return (
    <ol className="conversation-thread" role="list" aria-label="Agent conversation">
      {interactions.map((interaction) => (
        <ConversationEntry
          key={interaction.id}
          interaction={interaction}
          canAnswer={canAnswer}
          onAnswer={onAnswer}
        />
      ))}
    </ol>
  );
}

interface ConversationEntryProps {
  interaction: RunInteraction;
  canAnswer: boolean;
  onAnswer: (interactionId: string, answer: string) => Promise<void>;
}

function ConversationEntry({ interaction, canAnswer, onAnswer }: ConversationEntryProps) {
  const [answer, setAnswer] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isPending = interaction.status === 'pending' && interaction.addresseeType === 'human';
  const identity = agentIdentity(interaction.from);

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
        <span
          className="conversation-avatar"
          style={{ backgroundColor: identity.accent, color: identity.onAccent }}
          aria-hidden="true"
        >
          {identity.initials}
        </span>
        <strong className="conversation-from" style={{ color: identity.accent }}>{interaction.from}</strong>
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
