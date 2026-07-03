# Camunda 8 Spike

This spike captures the minimum mapping Agentwerke needs for Camunda 8. It has been superseded as a strategy document by `docs/decisions/ADR-001-use-camunda8-for-production-bpmn-runtime.md`: Camunda 8 is now the intended production BPMN runtime, not only a long-term replacement path.

## Goal

Prove that Agentwerke's MVP execution slice can run through a Camunda 8-oriented adapter boundary without leaking engine details into application orchestration or UI semantics.

## Mapped MVP Slice

| Agentwerke concern | Camunda 8 construct | Status |
| --- | --- | --- |
| Agent-backed service task | Service task + job worker | viable |
| Human approval step | User task | viable |
| Retry / wait timer | Intermediate timer catch event or timer boundary event | viable |
| Manual/API start | None start event + engine start call | viable |
| Message-driven start | Message start event | viable |
| Webhook-driven start | HTTP webhook inbound connector | viable |

## Notes

- Agentwerke should keep BPMN parsing, task metadata extraction, policy, and approval semantics in Agentwerke-owned code.
- Camunda should own execution scheduling, waiting states, and job/user-task orchestration for the production runtime.
- Service-task execution should be projected onto Camunda job types so the Agent Orchestrator can act as the controlled worker layer.
- Webhook starts are best treated as inbound connector configuration rather than hand-built HTTP endpoints inside the engine adapter.

## Primary References

- Camunda 8 BPMN coverage: https://docs.camunda.io/docs/components/modeler/bpmn/bpmn-coverage/
- Camunda 8 service tasks: https://docs.camunda.io/docs/components/modeler/bpmn/service-tasks/
- Camunda 8 user tasks: https://docs.camunda.io/docs/components/modeler/bpmn/user-tasks/
- Camunda 8 timer events: https://docs.camunda.io/docs/components/modeler/bpmn/timer-events/
- Camunda 8 message events: https://docs.camunda.io/docs/components/modeler/bpmn/message-events/
- Camunda 8 HTTP webhook connector: https://docs.camunda.io/docs/components/connectors/protocol/http-webhook/
