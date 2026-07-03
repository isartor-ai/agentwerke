# Map agent outcomes to Camunda job complete, fail, and incident state

## Summary
Complete Camunda jobs on successful agent execution and fail jobs predictably when agents, policy, or tools fail.

## Why
Operators need Camunda state and Agentwerke run state to agree, especially when retries or incidents occur.

## Scope
- Complete job with output variables on success.
- Fail job with retry decrement on retryable failure.
- Surface exhausted retries as incident/blocker in Agentwerke.
- Map policy rejection to blocked, approval-required, or failed according to policy result.

## Acceptance Criteria
- Success path completes the job and stores output variables.
- Retryable failures decrement retries and record Agentwerke events.
- Exhausted retries are visible as incident or blocked run state.
- Policy rejection does not silently complete the job.

## Verification
- Tests cover success, retryable failure, exhausted retries, and policy rejection.
- Manual failure path shows consistent Camunda and Agentwerke state.

## Suggested Files
- `src/Autofac.Infrastructure/Workers`
- `src/Autofac.AgentSecOps`
- `src/Autofac.Application/Workflows`
- `tests/Autofac.E2ETests`
