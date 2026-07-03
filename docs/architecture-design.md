# Agentwerke Architecture Design

Version: Draft v0.3
Status: Working Draft
Last reviewed: 2026-06-29
Related documents: `docs/architecture.md` (concise current overview), `docs/functional-specification.md`

> **Reader's note.** Sections 1–20 describe the **target architecture** (the north star). Sections 21–24 describe the **as-built reality**, the **gap analysis**, the **implementation roadmap**, and **future enhancements**, refreshed against the source on 2026-06-29. Where the target and the as-built state diverge, Section 21 is authoritative for "what exists today." For a short, current summary, see `docs/architecture.md`.

## 1. Purpose

This document defines the target architecture for Agentwerke based on the current Functional Specification Document.

The objective is to describe how Agentwerke should be structured as a secure, cloud-native, observable software factory platform that:
- models SDLC workflows in BPMN
- orchestrates agents as workflow executors
- integrates with enterprise systems
- enforces human approvals and policy gates
- provides full visibility across workflow and agent activity

This architecture is intended for product, engineering, platform, security, and operations stakeholders.

## 2. Architectural Goals

The architecture should satisfy the following product and technical goals:

1. Support configurable SDLC workflows using BPMN.
2. Treat agent execution as a first-class runtime concept.
3. Keep humans in control for high-impact actions.
4. Enforce policy, RBAC, and auditability across all actions.
5. Provide real-time observability for workflows, agents, tools, and integrations.
6. Support extensibility through plugins and connectors.
7. Support secure execution through sandboxing and isolation.
8. Run reliably in Docker and Kubernetes environments.

## 3. Architectural Principles

- Workflow-first: All automation is expressed as governed workflow execution.
- Human-governed autonomy: Agents can automate work, but policy and approval remain authoritative.
- Secure by default: Sensitive tools and production-facing actions are denied unless explicitly allowed.
- Observable by design: Every workflow transition, tool invocation, and agent action must be traceable.
- Extensible core: Integrations, tools, triggers, and custom BPMN nodes should plug into a stable platform contract.
- Cloud-native runtime: Stateless services, durable state stores, event-driven coordination, and containerized execution.

## 4. System Context

Agentwerke sits between human operators, enterprise systems, LLM providers, execution sandboxes, and delivery platforms.

### C4 Model Level 1: System Context

```mermaid
flowchart LR
    user["Users\nProduct, Engineering, QA, DevOps, Approvers, Admins"]
    autofac["Agentwerke\nDark software factory platform"]
    jira["Jira"]
    github["GitHub"]
    slack["Slack"]
    teams["Microsoft Teams"]
    email["Email Systems"]
    cicd["CI/CD Platforms"]
    cloud["Cloud / Kubernetes Environments"]
    llm["LLM Providers / Model Runtime"]
    mcp["MCP Tools / External Tooling"]
    obs["Enterprise Observability Stack"]

    user --> autofac
    jira <--> autofac
    github <--> autofac
    slack <--> autofac
    teams <--> autofac
    email <--> autofac
    cicd <--> autofac
    cloud <--> autofac
    llm <--> autofac
    mcp <--> autofac
    autofac --> obs
```

## 5. Container Architecture

Agentwerke should be designed as a set of cooperating platform services rather than a single monolith. The main architectural units are:

- React Web Application
- API Gateway / Backend API
- Workflow Runtime Engine
- Agent Orchestrator
- Sandbox Execution Manager
- Policy and Approval Services
- Integration Hub
- Plugin Runtime
- Observability Pipeline
- Persistent stores and event infrastructure

### C4 Model Level 2: Container Diagram

```mermaid
flowchart TB
    user["User"]

    subgraph autofac["Agentwerke Platform"]
        web["Web App\nReact"]
        api["Platform API\nC# ASP.NET Core"]
        workflow["Workflow Runtime Engine\nBPMN execution, run state, scheduling"]
        agent["Agent Orchestrator\nplanning, task dispatch, coordination"]
        sandbox["Sandbox Execution Manager\nOpenSandbox control plane / local Docker fallback"]
        policy["Policy and Approval Service\nRBAC, approval gates, action policy"]
        integration["Integration Hub\nSlack, Teams, Jira, GitHub, Email, Webhooks, CI/CD"]
        plugin["Plugin Runtime\ncustom triggers, tools, nodes, connectors"]
        obs["Observability Pipeline\nlogs, traces, metrics, audit events"]
        bus["Event Bus / Message Bus"]
        db["Operational Database"]
        store["Artifact and Blob Store"]
        secrets["Secret Store"]
        cache["Cache / Queue / Scheduler"]
    end

    llm["LLM Provider"]
    external["External Systems"]
    k8s["Kubernetes / Cloud Runtime"]

    user --> web
    web --> api
    api --> workflow
    api --> policy
    api --> integration
    api --> plugin

    workflow <--> bus
    workflow --> db
    workflow --> cache
    workflow --> policy
    workflow --> integration
    workflow --> agent

    agent <--> bus
    agent --> sandbox
    agent --> policy
    agent --> secrets
    agent --> store
    agent --> llm

    sandbox --> k8s
    sandbox --> store
    sandbox --> obs

    integration <--> external
    plugin --> integration
    plugin --> workflow
    plugin --> agent

    policy --> db
    policy --> secrets
    policy --> obs

    workflow --> obs
    agent --> obs
    integration --> obs
    api --> obs
```

## 6. Container Responsibilities

### 6.1 Web App

Technology: React

Responsibilities:
- BPMN workflow designer UI
- Dashboard and operational monitoring
- Approval inbox and review workflow
- Workflow run inspection
- Integration and plugin configuration
- RBAC-aware navigation and administration views

### 6.2 Platform API

Technology: C# ASP.NET Core

Responsibilities:
- Primary entry point for UI and external clients
- Authentication and authorization enforcement
- Workflow CRUD APIs
- Run control APIs
- Approval APIs
- Integration configuration APIs
- Plugin administration APIs

### 6.3 Workflow Runtime Engine

Responsibilities:
- Execute the Agentwerke-supported BPMN subset through the default Postgres-backed runtime
- Maintain workflow state and transitions through the engine adapter
- Process triggers and schedules
- Create and resume workflow runs
- Pause on approval gates
- Route task execution to agent or integration handlers
- Handle retries, compensation, and rollback controls

Architecture decision: Agentwerke is BPMN-centric, but the default runtime is the bounded Postgres-backed Agentwerke runtime. Camunda 8 is retained as an optional enterprise adapter, not the production default. See `docs/decisions/ADR-002-use-bpmn-centric-autofac-runtime-by-default.md`.

### 6.4 Agent Orchestrator

Responsibilities:
- Resolve task-to-agent assignment
- Load skill definitions
- Build execution context
- Invoke LLM-backed task execution
- Coordinate multi-agent workflows
- Track agent run state and outcomes
- Publish agent events to observability and audit streams

**Run context (inter-task data flow, Phase A).** Each run owns a key/value
context bag (`workflow_run_context`). It is seeded at run start with the
triggering issue (`input.title`, `input.body`, `input.external_url`, …) and
appended after every completed service task with that task's primary output
(`output.<nodeId>`). During prompt assembly the orchestrator loads the bag and
exposes it both as template variables (e.g. `{{input.body}}`,
`{{output.WriteRequirements}}`) and as a rendered `run_context` prompt section,
so a later agent (architect, analyst, …) can build on earlier agents' output.
See issue isartor-ai/autofac-private#89.

SDLC agent profiles registered in `AgentRegistry`: `business-analyst`,
`solution-architect`, `technical-analyst`, `implementation-engineer`,
`senior-code-reviewer` (plus the existing deploy/security/infra/test/github agents).

### 6.5 Sandbox Execution Manager

Responsibilities:
- Provision agent sandboxes through a provider-neutral execution boundary
- Apply runtime controls for file system, network, secrets, and CPU/memory
- Mount approved artifacts and workspaces
- Collect outputs, logs, traces, and artifacts
- Destroy or recycle isolated environments safely

Target direction: use OpenSandbox as the preferred sandbox control plane, with Kata-class secure runtimes for production and Docker kept as a local fallback. See `docs/decisions/ADR-003-use-opensandbox-control-plane-with-kata-runtime.md`.

#### Sandbox Profiles

Agents do not get an open-ended sandbox. Every sandbox-routed action requests one of four named profiles (`Autofac.Sandboxes.SandboxProfileCatalog`), and the profile name determines the OpenSandbox `resourceLimits`, `networkPolicy`, `volumes`, and credential-vault bindings the sandbox receives:

| Profile | Network egress | Repository mount | Credentials | Typical agents |
| --- | --- | --- | --- | --- |
| `offline` (default) | None | None | None | `business-analyst`, `solution-architect`, `technical-analyst` |
| `repo-read` | Restricted to GitHub hosts | Read-only `/workspace` | Read-only `github-token` (file-mounted) | `senior-code-reviewer`, `test-agent`, `security-agent` |
| `repo-write` | Restricted to GitHub hosts | Writable `/workspace` | Read-only `github-token` (file-mounted) | `github-agent`, `implementation-engineer` |
| `deployment` | Restricted, target-specific allow-list | None | `deployment-credentials` (file-mounted) | `deploy-agent`, `infra-agent` |

Credentials are always bound by name from the secret/vault layer at a fixed file path inside the sandbox; they are never inlined as command arguments or written to logs.

**Selection.** Each `AgentProfile` declares the profiles it may request (`AgentProfile.SandboxProfiles`; empty means "offline" only — least privilege by default). A BPMN service task may pin a specific profile with the `autofac:agentTask` `sandboxProfile` attribute; if omitted, the agent's first declared profile is used (or "offline" if it declares none). `Autofac.AgentSecOps.ISandboxProfileSelector` checks the requested profile against the agent's declared allow-list and, as defense in depth, rejects the `deployment` profile outright when the action-level policy decision's risk level is `critical` — independent of what the agent declares. Rejections happen in `ToolGateway` before any sandbox is created, are recorded in the run's tool-invocation history with status `profile_rejected`, and carry a diagnostic trail explaining which check failed.

This is a second, narrower gate than the action-level allow/deny/escalate decision from `IPolicyEvaluationService`: that decision answers "is this action permitted at all"; the sandbox profile answers "what can the sandbox running it touch."

### 6.6 Policy and Approval Service

Responsibilities:
- Enforce RBAC
- Evaluate action policies
- Manage approval queues and approval state
- Block or permit sensitive actions
- Record decision history
- Support AgentSecOps and MLOps guardrails

### 6.7 Integration Hub

Responsibilities:
- Inbound event ingestion
- Outbound notifications and commands
- Connector execution for Jira, GitHub, Slack, Teams, email, CI/CD, and cloud APIs
- Credential-aware external communication
- Connector health and retry handling

### 6.8 Plugin Runtime

Responsibilities:
- Register custom nodes, tools, connectors, and triggers
- Load extension metadata and execution contracts
- Isolate plugin behavior from core platform services
- Support safe extension points without core service rewrites

### 6.9 Observability Pipeline

Responsibilities:
- Collect logs, metrics, traces, and audit events
- Correlate workflow, agent, tool, and approval activity
- Feed dashboard views and external observability platforms
- Support incident analysis and compliance review

## 7. Component Architecture

The most important internal system boundary is the backend platform. Its core components should be clearly separated so the workflow model, execution model, and security model do not collapse into one service layer.

### C4 Model Level 3: Platform API and Runtime Components

```mermaid
flowchart LR
    ui["React UI"]

    subgraph backend["C# Backend Platform"]
        api["API Controllers / GraphQL or REST Layer"]
        auth["Auth and RBAC"]
        workflowsvc["Workflow Service"]
        runsvc["Run Service"]
        approvalsvc["Approval Service"]
        policysvc["Policy Evaluation Service"]
        agentsvc["Agent Service"]
        toolsvc["Tool Gateway"]
        integrationsvc["Integration Service"]
        pluginsvc["Plugin Registry Service"]
        schedulesvc["Trigger and Schedule Service"]
        audit["Audit Service"]
    end

    bpmn["BPMN Engine Adapter"]
    bus["Message Bus"]
    db["Operational DB"]
    secrets["Secret Store"]
    sandbox["Sandbox Manager"]
    llm["LLM Provider Adapter"]
    ext["External Connector Adapters"]
    telemetry["Telemetry Exporters"]

    ui --> api
    api --> auth
    api --> workflowsvc
    api --> runsvc
    api --> approvalsvc
    api --> integrationsvc
    api --> pluginsvc

    workflowsvc --> bpmn
    workflowsvc --> schedulesvc
    workflowsvc --> bus
    workflowsvc --> db

    runsvc --> db
    runsvc --> audit
    runsvc --> bus

    approvalsvc --> policysvc
    approvalsvc --> db
    approvalsvc --> audit

    agentsvc --> toolsvc
    agentsvc --> sandbox
    agentsvc --> llm
    agentsvc --> bus
    agentsvc --> db

    toolsvc --> policysvc
    toolsvc --> secrets
    toolsvc --> ext

    integrationsvc --> ext
    integrationsvc --> db

    pluginsvc --> db
    policysvc --> db
    audit --> telemetry
```

## 8. Runtime Flow

### 8.1 Workflow Execution Lifecycle

1. A trigger is received from Slack, Teams, Jira, GitHub, webhook, email, schedule, or manual invocation.
2. The Integration Hub or Trigger Service validates the event and creates a workflow run.
3. The Workflow Runtime Engine loads the BPMN definition and starts execution.
4. When an Agentwerke Agent Task is reached, the Agent Orchestrator resolves agent profile, skills, tools, and policies.
5. The Sandbox Execution Manager provisions an isolated execution environment if required.
6. The agent performs the LLM task and invokes approved tools through the Tool Gateway.
7. Tool invocations are evaluated by policy before execution.
8. Outputs are stored as artifacts and execution events are emitted to observability streams.
9. If a Human Approval Task is reached, the run pauses until a decision is recorded.
10. Once approved, the workflow continues to the next task, test gate, PR action, or deployment action.
11. The run completes with status, logs, audit records, traces, and metrics preserved.

### 8.2 Example Delivery Flow

For the Jira-driven use case:
- Jira triggers requirement intake
- Analysis agent generates technical specification
- Human approves specification
- Planner or engineering agent creates implementation tasks
- Engineering agent writes code and creates PR
- Human reviews PR
- Tester agent runs validation
- DevOps agent deploys through CI/CD
- Platform records full end-to-end evidence

## 9. Data Architecture

Agentwerke requires a combination of transactional storage, event streams, artifacts, and secrets.

### Core Data Domains

- User and identity data
- Roles and permissions
- Workflow definitions and versions
- BPMN models and custom node metadata
- Workflow run state and history
- Agent definitions and skill manifests
- Approval records
- Tool policies and execution records
- Integration configurations
- Plugin manifests
- Telemetry metadata
- Artifacts and generated outputs

### Recommended Data Stores

- Relational database for operational data and workflow state
- Object/blob store for artifacts, attachments, generated files, logs, and trace exports
- Message bus for event-driven coordination
- Secret manager for integration credentials, API keys, and runtime secrets
- Search or analytics store for observability-heavy querying if needed at scale

## 10. Integration Architecture

Agentwerke should use a connector-based integration model.

### Connector Categories

- Trigger connectors
- Notification connectors
- Work management connectors
- Source control connectors
- CI/CD connectors
- Cloud action connectors
- MCP tool connectors

### Connector Design Rules

- Every connector must implement a standard execution contract.
- Every connector action must be policy-evaluable.
- Connector credentials must be externalized to secret storage.
- Connector execution must emit audit, logs, and traces.
- Connector failures must support retry, dead-letter, or operator intervention patterns.

## 11. Security Architecture

Security is central to Agentwerke because the platform can perform code generation, repository changes, infrastructure actions, and deployment activities.

### Security Controls

- SSO and enterprise identity integration
- Role-based access control
- Approval-based control for sensitive actions
- Policy-based tool authorization
- Secret isolation and least-privilege access
- Secure sandbox isolation with runtime-specific controls
- Network egress restrictions for agents
- Audit logs for all user and agent actions
- Environment separation for dev, test, and production

### Sensitive Actions Requiring Policy and Approval

- Pull request merge
- Production environment changes
- Secret access
- Shell command execution with elevated privileges
- External notifications to regulated audiences
- Cloud API calls with production scope
- Deployment actions

## 12. Observability Architecture

Agentwerke must support logging, tracing, and monitoring as first-class capabilities.

### Telemetry Types

- Workflow run logs
- Agent execution logs
- Tool invocation records
- Connector request and response logs
- Approval decision logs
- Distributed traces
- Metrics for latency, failure rate, throughput, retries, and approval wait time
- Audit events for governance review

### Observability Objectives

- Understand what happened
- Understand who or what initiated it
- Understand which tools and policies were involved
- Understand why an action was blocked, failed, or retried
- Correlate technical telemetry with workflow business state

## 13. Deployment Architecture

Agentwerke should be deployed as a cloud-native platform on Kubernetes.

### C4 Model Level 4 Style Deployment View

```mermaid
flowchart TB
    subgraph cluster["Kubernetes Cluster"]
        subgraph ingress["Ingress Layer"]
            gw["API Gateway / Ingress"]
        end

        subgraph app["Application Namespace"]
            web["Web Frontend Pods"]
            api["API Pods"]
            workflow["Workflow Runtime Pods"]
            agent["Agent Orchestrator Pods"]
            integration["Integration Hub Pods"]
            policy["Policy and Approval Pods"]
            plugin["Plugin Runtime Pods"]
        end

        subgraph jobs["Execution Namespace"]
            sandbox["Ephemeral Agent Sandbox Pods / Secure Containers"]
        end

        subgraph data["Platform Data Services"]
            db["Relational DB"]
            bus["Message Bus"]
            cache["Cache / Scheduler"]
            blob["Artifact Store"]
            obs["Telemetry Collectors"]
            secrets["Secret Manager"]
        end
    end

    ext["External Systems and LLM Providers"]
    users["Users"]

    users --> gw
    gw --> web
    gw --> api
    api --> workflow
    api --> policy
    api --> integration
    workflow --> bus
    workflow --> db
    workflow --> agent
    agent --> sandbox
    agent --> blob
    agent --> secrets
    integration <--> ext
    agent <--> ext
    web --> api
    api --> obs
    workflow --> obs
    agent --> obs
    sandbox --> obs
```

## 14. Technology Direction

### Frontend

- React for the application shell and workflow UI
- Open source BPMN modeler extended with Agentwerke custom components
- Real-time operational dashboards for workflow and agent monitoring

### Backend

- C# with ASP.NET Core
- BPMN engine integration through an adapter layer
- Background services for workflow scheduling, run processing, and event handling
- Policy, approval, orchestration, and integration services separated by responsibility

### Runtime and Infrastructure

- OpenSandbox as the preferred sandbox control plane
- Kata-class secure runtimes for production agent isolation, with Docker retained for local fallback
- Kubernetes for orchestration and scaling
- Message bus for asynchronous coordination
- Centralized telemetry pipeline for logs, metrics, traces, and audit

### 14.1 BPMN Runtime Strategy

Agentwerke needs two distinct BPMN capabilities:
- a React-friendly modeling experience that can be extended with Agentwerke custom components
- a reliable runtime that executes the governed SDLC workflow subset used by Agentwerke templates

These capabilities are deliberately separated. BPMN remains the workflow artifact, import/export format, and governance language. Runtime choice is infrastructure behind the Agentwerke workflow boundary.

#### Candidate Comparison

| Candidate | Strengths | Risks / Constraints | Fit for Agentwerke |
| --- | --- | --- | --- |
| Agentwerke bounded runtime | Native .NET/Postgres deployment; already integrated with run state, context, approvals, audit, outbox, timers, and recovery; simplest self-hosted path for German companies | Must remain explicitly scoped to Agentwerke-supported SDLC templates; not a general BPMN engine | Default runtime for MVP, pilots, and first self-hosted deployments |
| Camunda 8 with Zeebe | Mature orchestration runtime; service-task/job-worker model aligns with agent tasks; strong user task, timer, retry, and incident concepts | Adds separate production runtime, licensing and operations questions, Camunda-specific BPMN projection, and installation complexity | Optional enterprise adapter when a customer requires or already operates Camunda |
| Flowable OSS | Mature classical BPM engine; broad BPMN support; strong self-hosted story in Java environments | Java-first runtime and separate operational surface; not aligned with simple .NET/Postgres default deployment | Fallback only if richer BPMN breadth becomes a customer requirement |
| Embedded third-party workflow engine | Could reduce custom runtime maintenance while keeping deployment simpler than a full external BPM platform | No current option clearly beats the existing Agentwerke runtime for .NET, Postgres, BPMN artifact continuity, and template-first strategy | Deferred evaluation |

#### Evaluation Criteria

Agentwerke's engine choice should optimize for:
- React-compatible BPMN modeling and custom node extensibility
- simple self-hosted deployment with PostgreSQL
- immediate usability for German companies with data residency requirements
- bounded long-running workflow support for SDLC templates
- timer and human-approval orchestration
- service-task execution that maps cleanly to Agentwerke agents
- run observability, auditability, and evidence capture
- low migration risk between MVP and long-term platform evolution

#### Decision (Revised - 2026-06-17)

**Agentwerke remains BPMN-centric, but Camunda 8 is no longer the default production runtime.** The default runtime is the bounded Postgres-backed Agentwerke runtime, executing the supported BPMN subset required by curated SDLC templates. Camunda 8 is an optional enterprise adapter behind the runtime boundary.

Rationale:
- Agentwerke's strategic value is SDLC semantics, real agent execution, policy, evidence, approvals, integrations, audit, and operator UX.
- Company SDLC processes are usually stable enough to start from a small catalog of governed templates rather than arbitrary BPMN.
- The existing Postgres/outbox runtime already covers the near-term needs for durable runs, recovery, timers, approvals, and run context without adding a separate stateful engine.
- A simple .NET/Postgres self-hosted deployment better matches first German customer adoption than a required Camunda 8 cluster.
- The adapter boundary remains useful so Camunda can be enabled when a customer requires it.

#### Engine Strategy

- Default runtime: Agentwerke bounded runtime backed by PostgreSQL, outbox workers, run events, checkpoints, and run context.
- Supported BPMN subset: `startEvent`, `serviceTask`, `userTask`, `exclusiveGateway`, `parallelGateway`, `intermediateCatchEvent`, `boundaryEvent`, `endEvent`, and sequence flows required by curated SDLC templates.
- Unsupported BPMN constructs must fail validation for the default runtime instead of being interpreted partially.
- Optional runtime: Camunda 8 adapter, enabled only through explicit configuration and customer need.
- The adapter boundary stays mandatory so default and optional enterprise runtimes can coexist without changing product-level workflow APIs.
- Runtime extension pressure should be handled through agent profiles, tools, connectors, policies, evidence handlers, and templates before expanding BPMN semantics.

#### Impact on the Architecture

- `Workflow Runtime Engine` = bounded Agentwerke runtime by default, reached through product workflow services
- `BPMN Engine Adapter` = runtime boundary for optional engines such as Camunda 8
- `React BPMN UI` = template-first SDLC builder plus advanced `bpmn-js` editor; BPMN XML remains the design artifact
- `WorkflowInstanceEngine` = default runtime until a measured re-decision trigger requires a different engine

### 14.2 Sandbox Runtime Strategy

Agentwerke's sandbox layer also needs two distinct choices:
- a control plane that manages lifecycle, command execution, files, resources, and cleanup
- a runtime that provides the actual isolation boundary for agent execution

These choices are deliberately separated. The current implementation is Docker-specific, but the target production architecture should not be.

#### Candidate Comparison

| Candidate | Strengths | Risks / Constraints | Fit for Agentwerke |
| --- | --- | --- | --- |
| OpenSandbox control plane with Kata runtime | Matches Agentwerke's need for lifecycle, command, file, resource, network, and credential control while delegating secure-runtime details; aligns with Kubernetes deployment | Adds a new platform dependency and requires an integration spike before full adoption | Preferred target architecture |
| Direct Kubernetes plus Kata executor | Strong production isolation and direct control of the runtime integration | Agentwerke must own more lifecycle, exec, artifact, and cleanup behavior directly | Fallback if OpenSandbox fails the spike |
| gVisor-based runtime path | Better isolation than plain containers with lighter operational weight than Kata in some environments | Weaker isolation than Kata for the most sensitive execution cases | Secondary production option |
| Docker direct | Already implemented and easy for local development | Daemon-specific, weaker isolation, not the desired long-term production posture | Local and test fallback only |
| Podman rootless | Cleaner local engine story than Docker in some environments | Does not materially change the production isolation story Agentwerke needs | Local alternative only |
| Firecracker direct | Strong microVM posture | Too much platform ownership too early | Deferred |

#### Decision (2026-06-18)

**Agentwerke will evaluate OpenSandbox as the preferred sandbox control plane and use Kata-class secure runtimes as the default production isolation target.** Docker remains acceptable for local development and narrow integration testing behind the same Agentwerke sandbox interface.

Rationale:
- Agentwerke needs stronger production isolation for untrusted or LLM-generated code than a direct Docker daemon path provides.
- OpenSandbox already models lifecycle, command execution, files, resource limits, network policy, and credential proxy behavior that Agentwerke would otherwise have to build.
- Kata is still the right default production boundary because the key missing property is isolation strength, not only container engine choice.
- Keeping the Agentwerke-owned `ISandboxExecutor` boundary allows a clean fallback to direct Kubernetes plus Kata if the OpenSandbox spike proves unsuitable.

#### Sandbox Strategy

- Preferred control plane: OpenSandbox behind an Agentwerke-owned provider boundary.
- Default production runtime: Kata Containers, with Kata plus Firecracker allowed when a platform operator wants that stricter microVM posture.
- Local fallback: Docker behind the same provider-neutral contract.
- Secondary production runtime: gVisor where customers want a lighter operational profile.
- Application boundary: `ISandboxExecutor` remains the Agentwerke interface; OpenSandbox REST or OpenAPI integration should be wrapped behind Agentwerke-owned client abstractions.

#### Impact on the Architecture

- `Autofac.Sandboxes` remains the Agentwerke-owned sandbox boundary and should become provider-neutral.
- The current Docker executor becomes a fallback implementation rather than the target production design.
- Production deployment should assume `Agentwerke -> OpenSandbox -> Kubernetes secure runtime` instead of `Agentwerke -> direct Docker daemon`.
- Sandbox profiles should eventually map Agentwerke policy intent into provider resource, network, filesystem, and credential configuration.

## 15. Key Architectural Decisions

### Decision 1: BPMN-Centric Orchestration with Bounded Agentwerke Runtime

Why:
- aligns with business-readable workflow modeling
- supports auditability and lifecycle visibility
- allows custom Agentwerke nodes for agent and governance behavior
- keeps first deployment simple: .NET service plus PostgreSQL, with Camunda 8 available only as an optional enterprise adapter
- constrains runtime scope to curated SDLC templates instead of arbitrary BPMN execution

### Decision 2: Separate Workflow Engine and Agent Orchestrator

Why:
- keeps process state management separate from AI task execution
- avoids coupling BPMN semantics to LLM runtime concerns
- allows independent scaling and evolution

### Decision 3: Tool Gateway with Policy Enforcement

Why:
- creates a single control point for risky actions
- simplifies logging and auditability
- supports consistent approval and authorization rules

### Decision 4: Sandbox-Based Agent Runtime

Why:
- reduces blast radius for agent execution
- supports reproducible task environments
- helps enforce operational and compliance boundaries

### Decision 5: Connector and Plugin Architecture

Why:
- supports enterprise integration breadth
- preserves a stable core platform
- reduces custom-code pressure on central services

## 16. Risks and Tradeoffs

- BPMN flexibility can increase workflow complexity for non-technical users.
- Fine-grained policy and approval systems can reduce execution speed if overused.
- Sandbox isolation improves safety but increases runtime overhead.
- Multi-agent communication introduces coordination and observability complexity.
- Plugin extensibility increases platform flexibility but expands security review scope.

## 17. Recommended MVP Architecture

For MVP, the architecture should focus on the smallest coherent slice:

- React web app
- C# Platform API
- BPMN workflow runtime
- Agent orchestrator
- Docker sandbox execution
- Policy and approval service
- Jira and GitHub connectors
- Basic CI/CD connector
- Observability pipeline with run logs, traces, and audit
- Kubernetes-ready deployment topology

MVP should avoid overbuilding:
- start with a small set of custom BPMN nodes
- support a minimal connector framework first
- implement essential approvals and policy gates before advanced self-improvement
- treat plugin isolation as a controlled extension path, not an open execution surface

## 18. Open Architecture Questions

1. Should workflow state be persisted directly by the BPMN engine or through an Agentwerke run abstraction?
2. What message bus should be used for agent coordination and event propagation?
3. How should plugin execution be isolated from core platform services?
4. What is the first supported secret management provider?
5. Which telemetry platform should be the default reference implementation?
6. How much agent-to-agent autonomy is acceptable in MVP versus later phases?

## 19. Architecture References

The BPMN engine recommendation above is based on current primary-source documentation reviewed on June 13, 2026:

- Camunda 8 BPMN modeling and coverage docs: https://docs.camunda.io/docs/components/modeler/bpmn/
- Camunda 8 BPMN coverage: https://docs.camunda.io/docs/components/modeler/bpmn/bpmn-coverage/
- Camunda 8 Zeebe overview: https://docs.camunda.io/docs/components/zeebe/zeebe-overview/
- Flowable open source BPMN getting started and constructs docs: https://www.flowable.com/open-source/docs/bpmn/ch02-GettingStarted and https://www.flowable.com/open-source/docs/bpmn/ch07b-BPMN-Constructs/
- Apache KIE project page for jBPM ecosystem status: https://kie.apache.org

## 20. Summary

Agentwerke should be implemented as a workflow-first, cloud-native orchestration platform with strong separation between workflow state management, agent execution, policy enforcement, integration handling, and observability.

The C4 views in this document show Agentwerke as a governed execution fabric sitting between human operators, enterprise systems, LLM-based agents, and deployment infrastructure. Its strength comes from combining BPMN process clarity, secure agent runtime controls, and complete operational visibility in one software factory platform.

---

## 21. Current Implementation Status (As-Built)

This section reflects the codebase as of 2026-06-29. It is the authoritative description of what exists today.

### 21.1 Solution Topology

The backend is a layered C# solution (`net9.0`) with clean dependency direction (Domain ← Application ← Infrastructure/Workflows/Agents ← Api):

| Project | Role | Maturity |
| --- | --- | --- |
| `Autofac.Domain` | Persistence entities, agent-runtime contracts, policy decisions | Solid |
| `Autofac.Application` | Run orchestration service, authoring service, observability + run contracts | Solid |
| `Autofac.Workflows` | In-process BPMN engine, BPMN validator, engine-adapter boundary | Solid; graph-based traversal, sequence flow parsing |
| `Autofac.Agents` | Agent orchestrator, tool gateway, hook gateway, MCP session, skills, prompt assembler | Solid; drives **real** model execution via `Autofac.Agents.Models` |
| `Autofac.Agents.Models` | `ILanguageModelClient` (Anthropic + mock + null providers), `AnthropicRetryHandler` (resilient HTTP), `IAgentModelRunner` tool-use loop, sandboxed agent runner | Real LLM execution (#143/#149/#150/#151) |
| `Autofac.AgentSecOps` | Rule-based policy evaluation service | MVP rules, hardcoded |
| `Autofac.Sandboxes` | Docker sandbox executor (Docker.DotNet) | Real container lifecycle; runs the agent-runner workload; OpenSandbox control plane selected in ADR-003 |
| `Autofac.Integrations` | `IConnector`/`ConnectorBase` abstraction; GitHub, Jira (ADF comments), Slack, Teams connectors; `IConnectorRegistry`; per-connector policy gate, audit, metrics, and OTel spans | Solid (Phase E) |
| `Autofac.Infrastructure` | EF Core + PostgreSQL (Npgsql), repositories, runtime store, 8 migrations | Solid |
| `Autofac.Storage` | Artifact storage abstraction; local filesystem + S3 (`AWSSDK.S3`) drivers | Phase E: S3 added |
| `Autofac.Observability` | Prometheus metrics, JSON console logs, correlation middleware, OTel tracing (`WithTracing` + OTLP exporter), `IWorkflowTracer`/`ISpan` abstraction, Jaeger via docker-compose | Metrics + tracing (Phase F) |
| `Autofac.Api` | ASP.NET Core controllers, contract mapping, OpenAPI | Solid; unauthenticated |

Frontend (`web/`, React + Vite + bpmn-js): `WorkflowDesigner`, `RunBoard`, `RunDetail`, `ApprovalsDashboard`, `Login`, plus a component library and ~8 Vitest integration/e2e suites. The `WorkflowDesigner` view embeds a full `BpmnModeler` component (bpmn-js v17) with Agentwerke moddle extension (`autofac:AgentTask`, `autofac:ApprovalTask`) — extension metadata is serialized directly into BPMN XML via `modeling.updateModdleProperties`. Custom `additionalModules` add a drag-and-drop palette, CSS canvas markers, and a `bpmn-js-properties-panel` sidebar for editing all Agentwerke-specific fields. A `__mocks__/BpmnModeler.tsx` stub allows Vitest to run without jsdom SVG layout.

### 21.2 What Actually Works End-to-End

- **BPMN authoring → validation → publish** via `WorkflowAuthoringService` and `BpmnWorkflowValidator`, exposed through `WorkflowsController` and the bpmn-js designer. The designer provides a drag-and-drop canvas with Agentwerke-specific palette entries ("Agent Task", "Approval Gate"), a properties panel sidebar for editing `autofac:agentTask` and `autofac:ApprovalTask` extension elements, canvas accent markers for visual identification, and overlay badges that surface backend validation errors directly on the relevant BPMN elements. Extension metadata round-trips natively through `bpmnXml` — no side-channel JSON.
- **Workflow execution today** runs via the in-process `WorkflowInstanceEngine` (`EngineId => "in-process"`): start events, service tasks (retry + boundary timeout), user/approval tasks, exclusive gateways (condition evaluation), parallel gateways (sequential branches with fork/join detection), intermediate/boundary timer events, and end events. The engine uses **graph-based traversal**: `BpmnSequenceFlow` elements are parsed and stored; when absent (tests), flows are inferred from node order. Checkpoints are keyed by node ID (not list index) and are written as event-sourced `checkpoint_saved` events, enabling `ResumeAsync` and `RecoverAsync`. This is now the default runtime foundation for MVP and pilots, constrained to curated Agentwerke SDLC templates rather than arbitrary BPMN execution.
- **Policy enforcement** at the Tool Gateway (`ToolGateway`): allow/deny lists, permission-level checks, input validation, and `PolicyEvaluationService` evaluation before every tool call — the single control point envisioned in Decision 3.
- **Agent runtime assembly**: skill resolution from markdown manifests (`SkillRepository`/`MarkdownSkillLoader`), prompt assembly with full prompt snapshots, hook execution (`HookGateway` with `before/after_agent_run`), MCP tool sessions (`McpToolSessionFactory`), and a complete `AgentRuntimeSnapshot` persisted per step.
- **Real agent execution**: `AgentOrchestrator` drives a governed model tool-use loop via `IAgentModelRunner` — the assembled prompt + resolved skills + allowed tools are sent to the configured `ILanguageModelClient` (Anthropic SDK behind `AnthropicRetryHandler`, with a deterministic `MockLanguageModelClient` for tokenless demos/CI and a `NullLanguageModelClient` fallback), and **every** tool call still routes through `ToolGateway` so policy stays authoritative. Provider is selected by `Anthropic:Provider` — `anthropic`, `openai`, `litellm` (any OpenAI Chat Completions-compatible endpoint incl. Azure OpenAI or a LiteLLM proxy, via `OpenAiCompatibleLanguageModelClient` — #174), `mock`, or `auto`; token usage, tool invocations, and artifacts are captured into `AgentRuntimeSnapshot`. Per-run **cost/token budgets** (`MaxRunCostUsd`/`MaxRunTokens`) halt further model calls with a `budget_exceeded` status (`ModelRunBudget`, #175). The Docker/OpenSandbox path runs a real **agent runner** (`OpenSandboxedAgentRunner` / `SandboxedAgentRuntimeExecutor`), not a placeholder workload. Per-task prompts (#149) and seeded run-context inputs (#142) feed the prompt; agent output flows into the PR (#150).
- **GitHub delivery**: real branch creation, commit, and pull-request creation via `GitHubConnector`; the PR body now includes the agent's output/artifacts (#150), not just a metadata marker.
- **Approvals + audit**: approval requests created at user-task gates, decided through `ApprovalsController`, with run resume on approval and `AuditRecord` written for governance. Approvers can decide **from Slack** — interactive Approve/Reject buttons + a signature-verified `/webhooks/slack/interactions` endpoint reuse the same decision path (#172/#31). Each decision is captured as per-agent feedback (see below).
- **Knowledge retrieval (RAG)**: `IKnowledgeRetriever` (lexical default over a configured corpus) exposed as the policy-gated `knowledge.search` tool, returning snippets with source citations (#176). Enterprise document sources (Confluence/SharePoint/wiki) + a pgvector/embeddings backend are tracked in epic #179.
- **Inter-agent coordination**: a run-scoped `IAgentCoordinationChannel` with `agent.post_message`/`agent.read_messages` policy-gated tools lets agents coordinate within a run, e.g. one agent waiting on another's output (#173).
- **Per-agent feedback**: human approval decisions are recorded as `AgentFeedback`; `AgentScorecard` aggregates per agent, exposed via `GET /api/agents/{id}/scorecard` (#177). LLM-assisted, human-reviewed improvement proposals are the remaining follow-up.
- **Policy lifecycle, simulation & risk scoring**: data-driven rules with a draft→publish lifecycle (`POST /api/policies/{id}/publish`|`unpublish`) and **impact simulation** (`POST /api/policies/simulate`) surfaced in a Policies admin UI (#34/#170); `PurposeRiskScorer` attaches a 0–100 purpose-confidence and a context-correlated (escalating) risk to every decision (#26).
- **Sandbox provider selection**: `ConfiguredSandboxExecutor` routes each run to the provider pinned on its sandbox profile (Docker / OpenSandbox / Kubernetes), so the execution backend is selectable **per policy/project** (#36); a kata-isolated Kubernetes executor with NetworkPolicy egress is implemented behind it (#171).
- **Inbound triggers**: GitHub/Jira webhook ingestion with HMAC signature validation and tag-based workflow routing (`TagBasedTriggerRouter`).
- **Observability**: workflow/step/approval/webhook metrics on `/metrics` (Prometheus), structured JSON logs with scoped `RunId`/`WorkflowId`/`Operation`, correlation-ID propagation, and **distributed tracing** via OTel `WithTracing` + OTLP to Jaeger (`Autofac.Workflows` `ActivitySource`; spans on engine start/resume/recover and every connector call).
- **Connectors**: `IConnector`/`ConnectorBase` policy-gated abstraction; GitHub, Jira (Atlassian Document Format), Slack, Teams connectors; data-driven `IPolicyRuleStore` with `FilePolicyRuleStore` (YAML-backed) and `InMemoryPolicyRuleStore` (tests); S3 artifact driver.
- **Persistence + deployment**: PostgreSQL via EF Core migrations; `docker-compose` brings up Postgres + migrate + api + web + Jaeger; **Helm chart** (`deploy/helm/agentwerke`) deploys api (HPA 2→8), worker (HPA 2→6), web, Postgres StatefulSet, RBAC for sandbox namespace; Grafana dashboard at `deploy/grafana/dashboards/workflow-overview.json`; Prometheus alert rules at `deploy/prometheus/alerts.yml`; a **production single-host profile** (`docker/docker-compose.prod.yml` — non-dev auth, env secrets, resource limits) and deploy docs (`deploy/README.md`) added in #160.
- **OSS readiness + supply chain**: Apache-2.0 `LICENSE` with an open-core boundary doc; community health files (SECURITY, CODE_OF_CONDUCT, CHANGELOG, issue templates); a one-command tokenless quickstart; a stable docs set. CI gates build/test/lint on every PR plus CodeQL and dependency review (#168), and a tag-driven release pipeline publishes container images to GHCR + a GitHub Release (#159).

### 21.3 Honest Limitations

These are the load-bearing gaps between the running system and the target architecture:

1. ~~**Agent execution is simulated.**~~ **Resolved.** Agents run a real model tool-use loop (`IAgentModelRunner` → `ILanguageModelClient`): the Anthropic SDK behind `AnthropicRetryHandler` (jittered backoff, `Retry-After`), with a deterministic tokenless `MockLanguageModelClient` for demos/CI and a `NullLanguageModelClient` fallback. Every tool call is routed through `ToolGateway`, and the Docker/OpenSandbox path runs a real agent runner. Remaining hardening: broader provider coverage (OpenAI/Azure are drop-in via the interface) and per-run cost/budget governance (see Section 24).
2. **Runtime strategy has been reset to Agentwerke-default.** The current implementation executes through `WorkflowInstanceEngine`, backed by Postgres persistence, run context, outbox dispatch, and recovery. This is now the default path for MVP and pilots, not a temporary gap. Camunda work is optional adapter groundwork only.
3. ~~**No asynchronous backbone.**~~ **Resolved (Phase C).** `WorkflowRunOrchestrationService.StartRunAsync` now creates a `pending` run and enqueues an outbox entry; the API returns 202 immediately. `RunDispatchWorker` (BackgroundService) polls the `run_outbox` table every 2 s, executes via `WorkflowRunExecutor`, and handles crash recovery on startup.
4. ~~**Authentication/RBAC is stubbed.**~~ **Resolved.** JWT/OIDC bearer validation (enterprise SSO via configurable `Authority`), Viewer/Operator/Approver/Admin enforcement on product controllers, a role-based administration model (#33/#120), **LDAP/AD group-to-role mapping** (`Ldap:*`, #178), and approval decisions that record the authenticated principal. Dev-token/dev-identity modes are explicit and disabled by default in the production profile (#160). Secret-provider (vault) integration remains future work — see limitation #10.
5. **Timers and parallelism are partially resolved.** Parallel branches run sequentially (Phase C adds `Task.WhenAll` via `IServiceScopeFactory`). Timer scheduling is real in Phase C (`waiting_timer` checkpoint + outbox `timer` entry with `visibleAfter=dueAt`). In Phase D standalone, timers still fire immediately (the Phase C async backbone is a separate branch).
6. ~~**One outbound connector.**~~ **Resolved (Phase E).** `IConnector`/`ConnectorBase` abstraction; GitHub, Jira (ADF), Slack, Teams connectors registered via `IConnectorRegistry`; email/CI-CD remain future work.
7. ~~**No distributed tracing.**~~ **Resolved (Phase F).** `WithTracing` + OTLP exporter wired; `IWorkflowTracer`/`ISpan` abstraction in `Autofac.Application`; engine and connectors emit spans to Jaeger.
8. ~~**No Kubernetes footprint.**~~ **Resolved (Phase F).** Helm chart at `deploy/helm/agentwerke` covers api, worker, web, Postgres StatefulSet, RBAC, HPA; Grafana dashboard and Prometheus alert rules included.
9. ~~**Policy is code, not data.**~~ **Resolved (Phase E).** `IPolicyRuleStore` with `FilePolicyRuleStore` (YAML file) and `InMemoryPolicyRuleStore`; admin authoring surface (policy-rule file) outside of code.
10. **No plugin runtime.** The Plugin Runtime container (Section 6.8) and secret manager (Section 9) are not implemented; credentials come from configuration/env only.

## 22. Gap Analysis

| Capability (target) | As-built | Gap severity | Notes |
| --- | --- | --- | --- |
| BPMN modeling (bpmn-js) | Implemented (Phase 1–3 complete) | — | Drag-and-drop canvas; Agentwerke moddle extension; properties panel; palette; markers; validation overlays |
| BPMN execution engine | Bounded Agentwerke runtime backed by Postgres/outbox | Medium | Keep scope capped to SDLC templates; do not expand into arbitrary BPMN |
| Agent task execution (LLM) | Real model client (Anthropic) + tokenless mock; governed tool-use loop | — | **Resolved** (#143/#149/#150/#151); OpenAI/Azure drop-in via `ILanguageModelClient` |
| Tool Gateway + policy | Implemented | — | Strong; single control point in place |
| Sandbox isolation | Real container lifecycle + agent-runner workload | Medium | network=none, mem/cpu limits; OpenSandbox runner wired (ADR-003); Kata-class production runtime + K8s provider (#36) remain |
| Human approvals | Implemented | — | Create/decide/resume + audit |
| Policy engine | Hardcoded MVP rules | Medium | Needs policy-as-data + store |
| Async coordination / outbox | Postgres outbox + BackgroundService worker | — | **Resolved (Phase C)**: 202-async API; crash recovery; timer scheduling |
| AuthN / RBAC / SSO | JWT/OIDC bearer validation + RBAC policies | — | **Resolved (Phase B baseline)**: Viewer/Operator/Approver/Admin policies protect product APIs; local development has explicit dev identity/token modes |
| Secret management | Config/env only | High | No vault/secret provider |
| Connectors | GitHub, Jira (ADF), Slack, Teams; `IConnector`/`ConnectorBase`; data-driven policy store | — | **Resolved (Phase E)**; email/CI-CD remain future work |
| Observability — metrics | Implemented (Prometheus) | — | Good coverage |
| Observability — tracing | OTel `WithTracing` + OTLP → Jaeger; engine + connector spans | — | **Resolved (Phase F)** |
| Persistence | EF Core + Postgres | — | 8 migrations, solid |
| Artifact storage | Local filesystem + S3 driver (`AWSSDK.S3`) | — | **Resolved (Phase E)** |
| Plugin runtime | Missing | Low (MVP) | Deferred per Section 17 |
| Deployment | docker-compose + Helm chart (api/worker/web/postgres/RBAC/HPA); Grafana + Prometheus alerts | — | **Resolved (Phase F)** |
| Timers / true parallelism | Real timer scheduling (Phase C); sequential branches (Phase D standalone) | Low | Parallel concurrency available via Phase C `Task.WhenAll` |

**Critical path:** the former blocker — real LLM execution — is resolved (Phase A complete). Remaining work is incremental hardening: secret/vault management, the K8s sandbox provider (#36), and per-run cost/budget governance. The runtime should be hardened only within the bounded SDLC-template scope; Camunda migration is not on the default critical path.

## 23. Implementation Roadmap

A phased plan ordered by dependency and risk. Each phase is independently shippable and leaves the system in a working state.

### Phase A — Make agents real (Critical, ~highest value) ✓ **Complete**

**Goal:** replace simulation with governed LLM execution.

1. ✓ `Autofac.Agents.Models` abstraction: `ILanguageModelClient` with a provider-agnostic request/response (messages, tools, max-tokens, stop). First provider is Anthropic Claude behind `AnthropicRetryHandler`; the interface keeps OpenAI/Azure drop-in. A tokenless `MockLanguageModelClient` (#151) and a `NullLanguageModelClient` fallback round out provider selection (`Anthropic:Provider` = `mock`/`anthropic`/`auto`).
2. ✓ Wired into `AgentOrchestrator` via `IAgentModelRunner`: the assembled prompt + resolved skills + allowed tools drive a tool-use loop, with every tool call routed through `ToolGateway` (policy stays authoritative).
3. ✓ The loop runs behind the sandbox interface through `OpenSandboxedAgentRunner`/`SandboxedAgentRuntimeExecutor` — a real agent runner replaces the placeholder workload, capturing token usage, tool invocations, and artifacts into `AgentRuntimeSnapshot`. Docker remains the interim local fallback.
4. ~ Model-call usage (tokens) is captured per run; latency/cost metrics and full prompt/output secret-redaction remain incremental hardening (see Section 24, cost/budget governance).

*Exit met:* a service task drives a real model that reads context, calls policy-gated tools, and produces non-deterministic output with full snapshot capture — proven by the BYO-key end-to-end run (`docs/manual-test-sdlc-e2e.md`) and the gated `RealClaudeIntegrationTests`.

### Phase B — Authentication, authorization, and secrets (Critical)

1. ✓ Replace the stub `AuthController` with OIDC/JWT bearer validation (`AddAuthentication().AddJwtBearer`), configurable issuer/audience for enterprise SSO.
2. ✓ Introduce a role model (`Viewer`, `Operator`, `Approver`, `Admin`) and apply `[Authorize]` policies per controller/endpoint — especially run start, approval decisions, and workflow publish.
3. Add a secret-provider abstraction (`ISecretStore`) with a first implementation (env/file for dev, then a vault driver). Route `GitHubOptions.PersonalAccessToken` and future connector creds through it.
4. ✓ Enforce that approval decisions record the authenticated principal, not `api-user`.

*Exit:* every state-changing endpoint requires an authenticated, authorized principal; credentials are not read from plain config in production. The auth/RBAC portion is complete; secret-provider integration is still pending.

### Phase C — Durable, asynchronous execution backbone (High) ✓ **Default runtime foundation**

1. Introduce a background worker: move `_runner.StartAsync`/`ResumeAsync` off the request thread into a hosted service that consumes a durable queue (start with an outbox table in Postgres; graduate to a message bus).
2. Add a run supervisor that auto-invokes `RecoverAsync` for runs left `running` after a crash (there is already a recovery path — make it automatic).
3. Implement real timer scheduling (persisted due-time + a timer dispatcher) so intermediate/boundary timer events actually wait.
4. Make parallel gateways execute branches concurrently with a join barrier.

*Exit:* workflows survive process restarts, timers genuinely delay, and the API returns immediately while work proceeds in the background.

### Phase D — Default runtime scope and template conformance (High)

1. ADR-001 has been superseded by `docs/decisions/ADR-002-use-bpmn-centric-autofac-runtime-by-default.md`.
2. The Agentwerke/Postgres runtime is the default production path for MVP and pilots. Camunda 8 remains an optional adapter, not a dependency of the core plan.
3. **Engine hardened**:
   - `BpmnSequenceFlow` added to `BpmnWorkflowDefinition`; `BpmnWorkflowValidator` now parses `<sequenceFlow>` and `<conditionExpression>` elements and validates source/target references.
   - `WorkflowInstanceEngine` refactored to graph-based traversal via `FlowGraph` (node map + outgoing-flows map). When sequence flows are absent (tests), `FlowGraph` infers linear edges from node order.
   - Exclusive gateway evaluates `ConditionExpression` (`true`/`false` literals; first matching or default); full FEEL/JUEL deferred to Phase E.
   - Parallel gateway fork: branch node IDs come from outgoing sequence flows (not list position). Branch sub-traversal follows edges to the join.
   - Checkpoint stores `string? NextNodeId` (node ID, not list index); recovery resolves by node map lookup — resilient to node reordering in future BPMN revisions.
   - Compensation/rollback: documented as Phase E concern; engine emits `compensation_not_supported` when it would be needed (not yet implemented).

4. Add a default-runtime conformance suite for every built-in SDLC template. Unsupported BPMN constructs must fail validation before publish.
5. Add runtime-mode configuration and startup diagnostics so `Agentwerke` is the default mode and `Camunda` is explicit opt-in.

*Exit:* all curated templates validate and run on the default runtime; Camunda-specific paths are optional and configuration-gated.

### Phase E — Connector and policy breadth (Medium) ✓ **Complete**

1. ~~Generalize the connector contract~~ — `IConnector`/`ConnectorBase` abstraction with policy gate, audit, and metrics in `ConnectorBase.ExecuteAsync`. `IConnectorRegistry` for DI-based connector lookup.
2. ~~Add Jira and Slack/Teams connectors~~ — `JiraConnector` (Jira REST API v3, ADF comment format), `SlackConnector` (Incoming Webhooks), `TeamsConnector` (Adaptive Cards).
3. ~~Move policy from code to data~~ — `IPolicyRuleStore` + `FilePolicyRuleStore` (YAML) + `InMemoryPolicyRuleStore` (tests); `PolicyEvaluationService` unchanged as the evaluator.
4. ~~Add a blob/S3 artifact driver~~ — `S3ArtifactStorage` behind `IArtifactStorage`; selected via `Storage:Provider = "s3"` config.

*Exit criterion met:* new connectors and policies are configuration/data, not core-code changes. PR: https://github.com/isartor-ai/autofac/pull/40

### Phase F — Production observability and deployment (Medium) ✓ **Complete**

1. ~~Add OTel tracing~~ — `WithTracing` + OTLP exporter in `Autofac.Observability`; `IWorkflowTracer`/`ISpan` abstraction in `Autofac.Application` keeps core libraries OTel-free; `WorkflowRunExecutor` and `ConnectorBase` emit spans with `RunId`, `WorkflowId`, `connector.id`, `connector.operation`, and error recording; Jaeger added to `docker/docker-compose.yml`.
2. ~~Author Helm charts~~ — `deploy/helm/agentwerke/`: api (HPA 2→8), worker (HPA 2→6; `ServiceAccount` with `Role`/`RoleBinding` for sandbox namespace), web, Postgres `StatefulSet` (20 Gi PVC), namespace + RBAC, optional ingress.
3. ~~SLO dashboards and alerting~~ — `deploy/grafana/dashboards/workflow-overview.json` (run success/failure rate, approval wait p50/p95, step/connector latency p50/p95, tool-call latency by category); `deploy/prometheus/alerts.yml` (high failure rate, approval SLA breach, worker backlog, connector error rate).

*Exit criterion met:* a request can be traced end-to-end via Jaeger and the platform deploys to Kubernetes via Helm.

### Sequencing summary

```
A (agents real) ─┐
B (auth/secrets)─┼─► C (async backbone) ─► D (runtime conformance) ─► E (breadth) ─► F (prod ops)
                 │
   A and B can proceed in parallel; both are prerequisites for a credible pilot.
   Phase D now hardens the default Agentwerke runtime and template contract, not a Camunda migration.
```

## 24. Future Enhancements (Beyond the Roadmap)

Forward-looking proposals to capture now, sequence later:

- **Multi-agent coordination.** Sub-agent delegation is modeled in the contract (`AgentSubAgentContract`) but not executed. Add planner/worker decomposition with depth limits and per-sub-agent policy scoping.
- **Cost and budget governance.** Per-run / per-workflow token and dollar budgets enforced at the model client, with policy escalation when a run exceeds budget.
- **Policy simulation & dry-run.** Extend the existing `PolicySimulation` API into a full "what would happen" preview across a whole workflow before publish.
- **Evidence and compliance packs.** Bundle run artifacts, approvals, policy decisions, and traces into an exportable, signed evidence package per delivery (supports audit/regulated use).
- **Replay and time-travel debugging.** Use the event-sourced run history to deterministically replay a run for debugging and post-incident review.
- **Skill marketplace + versioning.** Promote skills from on-disk markdown to a versioned, governed registry with provenance (fingerprints already exist on `SkillManifest`).
- **Human-in-the-loop richness.** Inline diff review for PRs, partial approvals, and delegation/escalation chains beyond the current single-decision gate.
- **Connector health & circuit breaking.** Per-connector health, retry budgets, and dead-letter handling (Section 10 design rules) once more than one connector exists.
- **Model routing & fallback.** Route by task type/risk to different models, with automatic fallback and quality gates.
- **Workflow templates & golden paths.** Curated, validated BPMN templates for the common SDLC flows (Jira→spec→code→PR→test→deploy) to lower authoring friction.
- **Tenancy & isolation.** Multi-tenant data partitioning and per-tenant secret/policy scoping for shared-platform deployments.
