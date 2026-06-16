import type { AgentSummary } from '../types';

/**
 * Module-scoped cache of registered agents.
 *
 * The bpmn-js properties panel renders synchronously, so the agent drop-down in
 * `AgentTaskProps` cannot await an API call. `WorkflowDesigner` fetches the agent
 * list once on mount and stores it here; the properties panel reads it via
 * `getAgentCatalog()`.
 */
let catalog: AgentSummary[] = [];

export function setAgentCatalog(agents: AgentSummary[]): void {
  catalog = agents;
}

export function getAgentCatalog(): AgentSummary[] {
  return catalog;
}
