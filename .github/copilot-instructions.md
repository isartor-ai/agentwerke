# Copilot Instructions for Autofac

Autofac is a secure, BPMN-native, Docker-sandboxed autonomous software factory.
Prioritize security, governance, and auditability over convenience in all generated code.

## Primary Architecture Rules
- Keep backend services in C# and ASP.NET Core.
- Treat BPMN 2.0 workflow definitions as the source of orchestration truth.
- Route all sensitive agent actions through the Agent Action Broker.
- Enforce policy decisions through AgentSecOps before any side effects.
- Model high-risk operations with human-in-the-loop approval paths.
- Keep durable workflow state and policy decisions in PostgreSQL.
- Keep large artifacts and logs in object storage.
- Ensure audit events are immutable and tamper-evident.

## Non-Negotiable Security Constraints
- Never allow agents to execute tools directly.
- Never bypass policy checks for sensitive actions.
- Never hardcode secrets, API keys, tokens, or credentials.
- Never suggest privileged container execution by default.
- Never open unrestricted network egress from sandbox workloads.
- Never perform production-impacting actions without explicit approval flow.

## Agent Action and Policy Design
- Include action context: workflow instance, BPMN element ID, agent identity, target, purpose, and evidence.
- Use explicit decision outcomes: ALLOW, DENY, ESCALATE_TO_HUMAN, ALLOW_WITH_CONSTRAINTS.
- Add decision explanation payloads for all denied or escalated actions.
- Keep policy interfaces pluggable (for example OPA/Rego adapter now, optional Cedar later).
- Prefer deterministic policy input schemas over free-form text checks.

## Backend Coding Guidance
- Favor clean architecture boundaries: Domain, Application, Infrastructure, and API layers.
- Keep domain models free of transport-specific concerns.
- Use async APIs and cancellation tokens for all I/O-bound paths.
- Validate all external input at API boundaries.
- Add structured logs for workflow and policy decision paths.
- Emit OpenTelemetry traces and key metrics for runtime, policy, and sandbox operations.

## Workflow Runtime Guidance
- Validate BPMN models and Autofac extension metadata before run start.
- Persist workflow state transitions and replay-safe event logs.
- Keep task execution idempotent where retries are possible.
- Distinguish retriable vs non-retriable failures explicitly.

## Sandbox and Integration Guidance
- Use per-run sandbox isolation (container, network, volume) when possible.
- Enforce command, file, and egress controls in sandbox execution.
- Restrict integrations to approved connectors and scoped permissions.
- Audit every outbound side-effecting integration call.

## Testing Expectations
- Treat tests as required deliverables for all new code and behavior changes.
- For every new class/function/module, add or update corresponding unit tests in the same change whenever feasible.
- Prefer test-driven implementation for new logic and bug fixes.
- Keep code coverage high: add tests for success, failure, and edge-case paths, not just happy paths.
- If some code cannot be realistically unit-tested, document the reason and add the closest integration/regression coverage instead.
- Add unit tests for domain and policy evaluation logic.
- Add integration tests for workflow progression and retry behavior.
- Add security tests for deny/escalate policy scenarios.
- Add regression tests for approval and resume workflow paths.

## Pull Request Expectations
- Explain why changes are safe in terms of policy and approval flow.
- Include migration notes when schema changes are introduced.
- Include observability updates for new critical paths.
- Keep changes incremental and compatible with phased rollout.

## Skills Import and Usage
- Import and use local skills from `.github/skills` for implementation and review tasks.
- Treat `.github/skills/using-agent-skills/SKILL.md` as the entry-point skill to select the best skill for each task.
- Prefer these skills for the matching work:
	- `planning-and-task-breakdown`
	- `incremental-implementation`
	- `test-driven-development`
	- `security-and-hardening`
	- `code-review-and-quality`
	- `debugging-and-error-recovery`
	- `documentation-and-adrs`
	- `ci-cd-and-automation`
- For UI work, use `.github/skills/frontend-ui-engineering/SKILL.md`.
- For API contracts and boundaries, use `.github/skills/api-and-interface-design/SKILL.md`.
- For production readiness decisions, use `.github/skills/shipping-and-launch/SKILL.md`.
