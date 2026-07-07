import type { RunStep } from '../types';

/** Compact token count for badges: 950 -> "950", 1234 -> "1.2K", 2500000 -> "2.5M". */
export function formatTokenCount(count: number): string {
  if (count >= 1_000_000) return `${(count / 1_000_000).toFixed(1).replace(/\.0$/, '')}M`;
  if (count >= 1_000) return `${(count / 1_000).toFixed(1).replace(/\.0$/, '')}K`;
  return count.toString();
}

export interface RunTokenTotals {
  inputTokens: number;
  outputTokens: number;
}

/** Sums per-step model token usage across a run's steps. */
export function sumRunTokens(steps: RunStep[]): RunTokenTotals {
  return steps.reduce<RunTokenTotals>(
    (acc, step) => {
      const usage = step.runtimeSnapshot?.tokenUsage;
      if (usage) {
        acc.inputTokens += usage.inputTokens;
        acc.outputTokens += usage.outputTokens;
      }
      return acc;
    },
    { inputTokens: 0, outputTokens: 0 },
  );
}
