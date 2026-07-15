import { useState } from 'react';
import { Link } from 'react-router-dom';
import { apiClient } from '../api/client';
import type { RunInteraction } from '../types';
import { AgentIdentityBadge } from './AgentIdentityBadge';

interface PendingInteractionsProps {
  interactions: RunInteraction[];
  canDecide: boolean;
  onChanged: () => Promise<void>;
}

function minutesOld(createdAt: string): number {
  return Math.max(0, Math.floor((Date.now() - new Date(createdAt).getTime()) / 60_000));
}

function minutesUntil(timeoutAt: string): number {
  return Math.max(0, Math.ceil((new Date(timeoutAt).getTime() - Date.now()) / 60_000));
}

export function PendingInteractions({ interactions, canDecide, onChanged }: PendingInteractionsProps) {
  return (
    <section className="panel" role="region" aria-label="Pending interactions">
      <div className="section-heading">
        <div>
          <h2>Pending interactions</h2>
          <p>Questions and confirmations currently blocking agent work.</p>
        </div>
        <span className="chip chip-static">{interactions.length} pending</span>
      </div>
      {interactions.length === 0 ? (
        <p className="cell-meta">No agent questions need a response.</p>
      ) : (
        <ol className="conversation-thread" role="list" aria-label="Pending human interactions">
          {interactions.map((interaction) => (
            <PendingInteractionRow
              key={interaction.id}
              interaction={interaction}
              canDecide={canDecide}
              onChanged={onChanged}
            />
          ))}
        </ol>
      )}
    </section>
  );
}

function PendingInteractionRow({
  interaction,
  canDecide,
  onChanged,
}: {
  interaction: RunInteraction;
  canDecide: boolean;
  onChanged: () => Promise<void>;
}) {
  const [text, setText] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [message, setMessage] = useState<{ tone: 'neutral' | 'error'; text: string } | null>(null);

  const submit = async (answer: string, reject = false) => {
    if (!answer.trim() || submitting || interaction.status !== 'pending') return;
    setSubmitting(true);
    setMessage(null);
    try {
      if (reject) {
        await apiClient.rejectInteraction(interaction.runId, interaction.id, answer.trim());
      } else {
        await apiClient.answerInteraction(interaction.runId, interaction.id, answer.trim());
      }
      await onChanged();
    } catch (error) {
      const status = (error as { status?: number }).status;
      const text = error instanceof Error ? error.message : 'Unable to submit response.';
      if (status === 409) {
        setMessage({ tone: 'neutral', text });
        await onChanged();
      } else {
        setMessage({ tone: 'error', text });
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <li className="conversation-entry">
      <div className="conversation-head">
        <AgentIdentityBadge name={interaction.from} />
        <strong>{interaction.workflowName ?? 'Workflow interaction'}</strong>
        {interaction.blocking ? <span className="status-badge status-awaiting_approval">Blocking</span> : null}
      </div>
      <p>{interaction.prompt}</p>
      <p className="cell-meta">
        <Link to={`/runs/${encodeURIComponent(interaction.runId)}`}>run {interaction.runId}</Link>
        {interaction.stepId ? ` · step ${interaction.stepId}` : ''}
        {` · ${minutesOld(interaction.createdAt)}m old`}
        {interaction.timeoutAt ? ` · expires in ${minutesUntil(interaction.timeoutAt)}m` : ''}
      </p>
      {canDecide ? (
        <div className="conversation-answer">
          {interaction.kind === 'confirm' ? (
            <div className="tag-row">
              <button className="btn btn-primary" type="button" disabled={submitting} onClick={() => void submit('approve')}>Approve</button>
              <button className="btn btn-danger" type="button" disabled={submitting || !text.trim()} onClick={() => void submit(text, true)}>Reject</button>
            </div>
          ) : interaction.options.length > 0 ? (
            <div className="tag-row">
              {interaction.options.map((option) => (
                <button className="btn btn-secondary" type="button" key={option} disabled={submitting} onClick={() => void submit(option)}>{option}</button>
              ))}
            </div>
          ) : null}
          {(interaction.kind === 'question' || interaction.kind === 'confirm') ? (
            <div className="conversation-answer-row">
              <input
                aria-label={interaction.kind === 'confirm' ? 'Rejection reason' : 'Answer'}
                placeholder={interaction.kind === 'confirm' ? 'Reason required to reject' : 'Type an answer'}
                value={text}
                disabled={submitting}
                onChange={(event) => setText(event.target.value)}
              />
              {interaction.kind === 'question' ? (
                <button className="btn btn-primary" type="button" disabled={submitting || !text.trim()} onClick={() => void submit(text)}>Send</button>
              ) : null}
            </div>
          ) : null}
        </div>
      ) : <p className="cell-meta">Approver role required to respond.</p>}
      {message ? <p role={message.tone === 'error' ? 'alert' : 'status'} className={message.tone === 'error' ? 'validation-error' : 'cell-meta'}>{message.text}</p> : null}
    </li>
  );
}
