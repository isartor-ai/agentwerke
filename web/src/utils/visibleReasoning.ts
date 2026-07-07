import type { RunEvent, RunStep } from '../types';

export interface VisibleReasoningEntry {
  id: string;
  kind: 'started' | 'reasoning' | 'recorded' | 'tool_started' | 'tool_finished';
  summary: string;
  createdAt?: string;
  toolName?: string;
  status?: string;
  /** Concrete action detail — the file edited, command run, or PR opened. */
  detail?: string;
}

const LIVE_PROGRESS_EVENT_TYPES = new Set([
  'agent_reasoning_started',
  'agent_reasoning_delta',
  'agent_reasoning_recorded',
  'agent_tool_call_started',
  'agent_tool_call_finished',
]);

const REASONING_SOURCE_EVENT_TYPES = new Set([
  ...LIVE_PROGRESS_EVENT_TYPES,
  'service_task_attempted',
]);

function buildReasoningStartSummary(action: unknown, attempt: unknown): string {
  const normalizedAction = typeof action === 'string' && action.trim()
    ? `'${action.trim()}'`
    : 'this step';
  const normalizedAttempt = typeof attempt === 'number' && Number.isFinite(attempt) && attempt > 0
    ? attempt
    : 1;
  return `Starting ${normalizedAction}: assembling context, checking runtime constraints, and preparing the model/tool loop (attempt ${normalizedAttempt}).`;
}

function entryKey(entry: VisibleReasoningEntry): string {
  return [
    entry.kind,
    entry.toolName ?? '',
    entry.status ?? '',
    // Distinct actions on the same tool (e.g. two file writes) differ only by detail — keep them
    // apart so the activity log doesn't collapse them into one entry.
    entry.detail ?? '',
    entry.summary.trim(),
  ].join('|');
}

function canCollapseStreamingReasoning(
  previous: VisibleReasoningEntry | undefined,
  next: VisibleReasoningEntry,
): boolean {
  if (!previous) {
    return false;
  }

  if (previous.kind !== 'reasoning' || next.kind !== 'reasoning') {
    return false;
  }

  if ((previous.toolName ?? '') !== (next.toolName ?? '')) {
    return false;
  }

  if ((previous.status ?? '') !== (next.status ?? '')) {
    return false;
  }

  const previousSummary = previous.summary.trim();
  const nextSummary = next.summary.trim();
  return nextSummary.length > previousSummary.length && nextSummary.startsWith(previousSummary);
}

function appendReasoningEntry(entries: VisibleReasoningEntry[], entry: VisibleReasoningEntry): VisibleReasoningEntry[] {
  const summary = entry.summary.trim();
  if (!summary) {
    return entries;
  }

  const normalizedEntry = { ...entry, summary };
  const previous = entries[entries.length - 1];

  if (canCollapseStreamingReasoning(previous, normalizedEntry)) {
    return [...entries.slice(0, -1), normalizedEntry];
  }

  const key = entryKey(normalizedEntry);
  if (entries.some((existing) => entryKey(existing) === key)) {
    return entries;
  }

  return [...entries, normalizedEntry];
}

function parseEventPayload(message: string): Record<string, unknown> | null {
  try {
    const parsed = JSON.parse(message);
    return parsed && typeof parsed === 'object' ? parsed as Record<string, unknown> : null;
  } catch {
    return null;
  }
}

export function isLiveProgressEventType(type: string): boolean {
  return LIVE_PROGRESS_EVENT_TYPES.has(type);
}

export function extractAgentReasoningByStep(events: RunEvent[] | undefined): Record<string, VisibleReasoningEntry[]> {
  const byStep: Record<string, VisibleReasoningEntry[]> = {};
  const orderedEvents = [...(events ?? [])].sort((left, right) => {
    const leftTime = Date.parse(left.createdAt);
    const rightTime = Date.parse(right.createdAt);
    if (Number.isNaN(leftTime) || Number.isNaN(rightTime)) {
      return left.createdAt.localeCompare(right.createdAt);
    }
    return leftTime - rightTime;
  });

  for (const event of orderedEvents) {
    const payload = parseEventPayload(event.message);

    if (isLiveProgressEventType(event.type)) {
      const stepId = payload?.stepId;
      const summary = payload?.summary;
      if (typeof stepId !== 'string' || typeof summary !== 'string') {
        continue;
      }

      const entry: VisibleReasoningEntry = {
        id: event.id,
        kind: event.type === 'agent_reasoning_started'
          ? 'started'
          : event.type === 'agent_reasoning_delta'
            ? 'reasoning'
            : event.type === 'agent_reasoning_recorded'
              ? 'recorded'
              : event.type === 'agent_tool_call_started'
                ? 'tool_started'
                : 'tool_finished',
        summary,
        createdAt: event.createdAt,
        toolName: typeof payload?.toolName === 'string' ? payload.toolName : undefined,
        status: typeof payload?.status === 'string' ? payload.status : undefined,
        detail: typeof payload?.detail === 'string' && payload.detail.trim() ? payload.detail : undefined,
      };
      byStep[stepId] = appendReasoningEntry(byStep[stepId] ?? [], entry);
      continue;
    }

    if (event.type !== 'service_task_attempted') {
      continue;
    }

    const stepId = payload?.stepId;
    if (typeof stepId !== 'string') {
      continue;
    }

    const entry: VisibleReasoningEntry = {
      id: `${event.id}-legacy-start`,
      kind: 'started',
      summary: buildReasoningStartSummary(payload?.action, payload?.attempt),
      createdAt: event.createdAt,
    };
    byStep[stepId] = appendReasoningEntry(byStep[stepId] ?? [], entry);
  }

  return byStep;
}

export function mergeVisibleReasoningEntries(
  ...entryGroups: ReadonlyArray<VisibleReasoningEntry[]>
): VisibleReasoningEntry[] {
  return entryGroups
    .flat()
    .reduce<VisibleReasoningEntry[]>((entries, entry) => appendReasoningEntry(entries, entry), []);
}

export function getStepEventsForDisplay(step: RunStep | null, events: RunEvent[]): RunEvent[] {
  if (!step) {
    return [];
  }

  return [...events]
    .filter((event) => {
      const payload = parseEventPayload(event.message);
      if (typeof payload?.stepId === 'string') {
        return payload.stepId === step.id && !REASONING_SOURCE_EVENT_TYPES.has(event.type);
      }

      return event.message.includes(step.name);
    })
    .reverse();
}
