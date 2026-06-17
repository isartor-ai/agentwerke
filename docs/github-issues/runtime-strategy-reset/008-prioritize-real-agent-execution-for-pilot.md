# Prioritize real agent execution for pilot readiness

## Context

The core value of the dark software factory is that workflow service tasks execute real governed agents. Current simulated agent output blocks pilot value more than BPMN engine selection.

## Scope

- Add a provider-neutral language model client abstraction.
- Implement a first model provider behind configuration.
- Wire the model client into the existing agent orchestrator and prompt snapshot flow.
- Route all tool calls through the existing Tool Gateway and policy enforcement.
- Persist model usage, tool invocations, outputs, and artifacts into the agent runtime snapshot.
- Add metrics for latency, tokens, cost, failures, and policy denials.

## Acceptance Criteria

- A service task can invoke a real model and produce non-deterministic agent output.
- Tool calls remain policy-gated.
- Agent runtime snapshots include prompt, model, tools, outputs, and usage metadata.
- Tests cover success, tool denial, model failure, and redaction behavior.
