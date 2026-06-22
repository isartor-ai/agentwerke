import type { RunStepSandboxExecution, RunStepTokenUsage } from '../types';

export interface SandboxExecutionDetailsProps {
  sandboxExecution?: RunStepSandboxExecution;
  tokenUsage?: RunStepTokenUsage;
  sectionClassName?: string;
  headingClassName?: string;
}

/**
 * Renders sandbox execution diagnostics (provider, state, logs, metadata) and
 * model token usage for an agent_sandboxed (or any model-backed) run step.
 * Shared by AgentDetailPanel and RunDetail's I/O tab so the two views don't
 * drift independently.
 */
export function SandboxExecutionDetails({
  sandboxExecution,
  tokenUsage,
  sectionClassName = 'adp-section',
  headingClassName = 'adp-section-label',
}: SandboxExecutionDetailsProps) {
  if (!sandboxExecution && !tokenUsage) {
    return null;
  }

  return (
    <>
      {sandboxExecution && (
        <section className={sectionClassName}>
          <h3 className={headingClassName}>Sandbox Diagnostics</h3>
          <dl className="definition-list">
            <div><dt>Provider</dt><dd>{sandboxExecution.provider}</dd></div>
            <div><dt>Sandbox ID</dt><dd>{sandboxExecution.sandboxId ?? '-'}</dd></div>
            <div><dt>State</dt><dd>{sandboxExecution.commandState}</dd></div>
            <div><dt>Exit code</dt><dd>{sandboxExecution.exitCode ?? '-'}</dd></div>
            <div><dt>Duration</dt><dd>{sandboxExecution.durationMs?.toLocaleString() ?? '-'} ms</dd></div>
          </dl>

          {Object.keys(sandboxExecution.diagnostics).length > 0 && (
            <>
              <h3 className={headingClassName}>Metadata</h3>
              <dl className="definition-list">
                {Object.entries(sandboxExecution.diagnostics)
                  .sort(([left], [right]) => left.localeCompare(right))
                  .map(([key, value]) => (
                    <div key={key}>
                      <dt>{key}</dt>
                      <dd>{value || '-'}</dd>
                    </div>
                  ))}
              </dl>
            </>
          )}

          {sandboxExecution.logs.length > 0 && (
            <>
              <h3 className={headingClassName}>Sandbox Logs</h3>
              <pre className="adp-pre">
                {sandboxExecution.logs.map((entry) => `[${entry.stream}] ${entry.message}`).join('\n')}
              </pre>
            </>
          )}
        </section>
      )}

      {tokenUsage && (
        <section className={sectionClassName}>
          <h3 className={headingClassName}>Model Usage</h3>
          <dl className="definition-list">
            <div><dt>Model</dt><dd>{tokenUsage.modelId ?? '-'}</dd></div>
            <div><dt>Input tokens</dt><dd>{tokenUsage.inputTokens.toLocaleString()}</dd></div>
            <div><dt>Output tokens</dt><dd>{tokenUsage.outputTokens.toLocaleString()}</dd></div>
            <div><dt>Elapsed</dt><dd>{tokenUsage.elapsedMs?.toLocaleString() ?? '-'} ms</dd></div>
          </dl>
        </section>
      )}
    </>
  );
}
