import { mergeVisibleReasoningEntries, type VisibleReasoningEntry } from '../utils/visibleReasoning';

interface VisibleReasoningLogProps {
  entries?: VisibleReasoningEntry[];
  isRunning?: boolean;
  sectionClassName?: string;
  headingClassName?: string;
}

function reasoningEntryMeta(entry: VisibleReasoningEntry): { marker: string; label: string; tone: string } {
  switch (entry.kind) {
    case 'tool_started':
      return { marker: '↗', label: entry.toolName ?? 'Tool', tone: 'tool-started' };
    case 'tool_finished':
      if (entry.status === 'blocked') {
        return { marker: '!', label: entry.toolName ?? 'Tool', tone: 'tool-blocked' };
      }
      if (entry.status === 'failed') {
        return { marker: '!', label: entry.toolName ?? 'Tool', tone: 'tool-failed' };
      }
      return { marker: '✓', label: entry.toolName ?? 'Tool', tone: 'tool-finished' };
    case 'recorded':
      return { marker: '✓', label: 'Final', tone: 'recorded' };
    case 'started':
      return { marker: '○', label: 'Start', tone: 'started' };
    default:
      return { marker: '•', label: 'Reasoning', tone: 'reasoning' };
  }
}

export function VisibleReasoningLog({
  entries = [],
  isRunning = false,
  sectionClassName = 'timeline-reasoning-log',
  headingClassName,
}: VisibleReasoningLogProps) {
  const visibleEntries = mergeVisibleReasoningEntries(entries);

  if (visibleEntries.length === 0 && !isRunning) {
    return null;
  }

  return (
    <section
      className={sectionClassName}
      aria-label="Visible agent reasoning"
      aria-live={isRunning ? 'polite' : undefined}
    >
      <h3 className={headingClassName}>Visible Reasoning</h3>
      {visibleEntries.length > 0 ? (
        <ol role="list">
          {visibleEntries.map((item) => {
            const meta = reasoningEntryMeta(item);
            return (
              <li
                key={item.id}
                className={`timeline-reasoning-entry timeline-reasoning-entry-${meta.tone}`}
              >
                <div className="timeline-reasoning-head">
                  <span className="timeline-reasoning-marker" aria-hidden="true">{meta.marker}</span>
                  <span className="timeline-reasoning-label">{meta.label}</span>
                </div>
                <p>{item.summary}</p>
              </li>
            );
          })}
        </ol>
      ) : (
        <p>Agent is preparing the model/tool loop for this step.</p>
      )}
    </section>
  );
}
