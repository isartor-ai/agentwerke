# BPMN UI Implementation Roadmap

**Phase:** 2 (BPMN Runtime MVP)  
**Step:** P2.4 – Implement BPMN UI MVP for modeling, validation, and run monitoring  
**Epic Scope:** 8–12 weeks, 3–4 engineers  
**Delivery:** Security-Aware Workflow Studio (unified design + monitoring interface)

---

## Implementation Status

| Phase | Status | Branch / PR | Notes |
|-------|--------|-------------|-------|
| 2.4.1 — Canvas + Templates | ✅ Done | `arc/workflow-designer-bpmn-js` | bpmn-js v17 canvas, Agentwerke moddle extension, template gallery, import/export |
| 2.4.2 — Metadata Editor + Validation | ✅ Done | `arc/workflow-designer-bpmn-js` | bpmn-js-properties-panel; metadata embedded in BPMN XML via extension elements |
| 2.4.3 — Publishing + Monitoring | ✅ Done | `arc/workflow-designer-bpmn-js` | Design/Monitor tab toggle, 10s poll, BpmnViewer with raf-* token markers, Start Run |
| 2.4.4 — Diff + Approvals + Polish | ✅ Done | `arc/workflow-designer-bpmn-js` | RunDiffModal (client-side diff, deviation badges), inline Approve/Reject/Escalate, cancel run |

> **Architecture decision (Phase 1–3, 2026-06):** Agentwerke extension metadata (`agentwerke:agentTask`, `agentwerke:ApprovalTask`) is serialized **directly into BPMN XML** via moddle extension elements using `modeling.updateModdleProperties`. This eliminates the side-channel metadata editor planned in Phases 2.4.1–2 (no separate form syncing required). The bpmn-js properties panel sidebar (via `bpmn-js-properties-panel` + `@bpmn-io/properties-panel`) renders the same property entries that write back into the XML, so the design artifact and the metadata are always in sync.

---

## Table of Contents

1. [Epic Overview](#epic-overview)
2. [Architectural Approach](#architectural-approach)
3. [Phase Breakdown](#phase-breakdown)
4. [Frontend Architecture](#frontend-architecture)
5. [Backend API Contracts](#backend-api-contracts)
6. [Acceptance Criteria](#acceptance-criteria)
7. [Dependencies & Risks](#dependencies--risks)

---

## Epic Overview

### Problem Statement

Currently, workflow designers lack a unified interface to:
- **Design** BPMN workflows with full Agentwerke extension metadata (agent, action, policyTag, requiresEvidence)
- **Validate** workflows before publishing (catch missing required fields at design-time)
- **Monitor** workflow runs with real-time execution visibility
- **Debug** runs by understanding why tasks behaved unexpectedly (diff from definition)

### Solution: "Security-Aware Workflow Studio"

A single-page React application that combines three core panels:

```
┌─────────────────────────────────────────────────────────────┐
│ BPMN Workflow Studio                              [Design|Monitor]
├────────────────┬──────────────────────────┬──────────────────┤
│   BPMN Canvas  │  Metadata Editor Panel   │  Policy/Risk View│
│   (Left)       │  (Center)                │  & Run Timeline  │
│   - Import     │  - Agent name            │  (Right)         │
│   - Design     │  - Action                │  - Risk badge    │
│   - Export     │  - Policy tag            │  - Evidence req. │
│                │  - Evidence list         │  - Live timeline │
│                │  - Retry config          │  - Diff view     │
└────────────────┴──────────────────────────┴──────────────────┘
```

### Why BPMN 2.0 + Minimal Extensions

- **Format:** Industry-standard, visio-friendly, schema-validated
- **Tooling:** BPMN.js (Camunda) editor is production-grade + extensible
- **Governance:** XML diffs in git, compliance-friendly, no proprietary lock-in
- **Scalability:** Long-term: stays as authority, optional DSL layer compiles to BPMN later

---

## Architectural Approach

### Design Philosophy

1. **Template-first onboarding** — Users start with proven templates, not blank canvas
2. **Mandatory extension metadata** — Cannot publish workflow without agent, action, policyTag
3. **Real-time validation** — Visual error indicators (❌ missing field, ⚠️ high risk)
4. **Unified view** — Design canvas + running instance side-by-side (no context-switching)
5. **Operator transparency** — "Diff from definition" explains unexpected behavior

### Tech Stack

| Layer | Planned | As-Built (Phases 1–3) | Notes |
|-------|---------|----------------------|-------|
| **Frontend UI** | React 18, TypeScript, Tailwind CSS | React 18, TypeScript, CSS modules | CSS variables + custom classes; no Tailwind |
| **BPMN Editor** | BPMN.js v14+ | bpmn-js v17 + bpmn-js-properties-panel v5 | bpmn-js upgraded to v17 for stability |
| **Moddle Extension** | — | `agentwerkeModdle` descriptor (custom) | Registers `agentwerke:` namespace; elements serialize to/from XML natively |
| **Properties Panel** | Custom side panel | `@bpmn-io/properties-panel` v3 + `htm/preact` | Entries use `TextFieldEntry`/`NumberFieldEntry` via htm/preact (not React) |
| **State Management** | TanStack Query | React `useState`/`useEffect` | API calls via `apiClient`; no external query library |
| **Real-time Events** | SignalR / WebSocket | Not yet (Phase 2.4.3) | Live run timeline deferred |
| **Form Builder** | React Hook Form + Zod | bpmn-js properties panel entries | Schema validation happens server-side via `BpmnWorkflowValidator` |
| **Test Framework** | Vitest + React Testing Library | Vitest + React Testing Library | `__mocks__/BpmnModeler.tsx` stubs bpmn-js for jsdom |

### API Communication

**REST Endpoints** (versioned at `/api/v1/`):
- `POST /api/v1/workflows` — Create/update workflow
- `POST /api/v1/workflows/{id}/validate` — Validate definition (returns errors or ✅)
- `POST /api/v1/workflows/{id}/publish` — Publish to registry
- `GET /api/v1/workflows/{id}/policy-simulation` — Dry-run policy checks
- `GET /api/v1/runs` — List runs with status
- `GET /api/v1/runs/{runId}` — Detailed run state + events
- `GET /api/v1/runs/{runId}/diff` — Compare execution vs. definition

**WebSocket Events**:
- `workflow.updated` — Definition changed (editor → backend)
- `run.progressed` — Token moved to new task (runtime → UI)
- `run.policy_decision` — Policy evaluated (policy → UI)
- `task.failed` → escalation or retry triggered

---

## Phase Breakdown

### Phase 2.4.1: Foundational UI Setup & BPMN Canvas Integration

**Duration:** 2–3 weeks | **Engineer Focus:** 1 FTE (senior frontend)

**Deliverables:**
1. **React app shell** with authenticated layout
   - Sidebar navigation (Design | Monitor | Settings)
   - Top bar with user menu, workspace selector
   - Responsive grid (desktop: 3 columns; mobile: tabs)

2. **BPMN Canvas integration** (read-only + design mode toggle)
   - Embed BPMN.js viewer/modeler
   - Implement Agentwerke extension plugins:
     - `agentwerke:agentTask` visual indicator (colored icon on serviceTask/scriptTask)
     - `agentwerke:approvalTask` indicator on userTask
   - Load BPMN XML from backend and display
   - Export BPMN to file (download as .bpmn2.xml)

3. **Template gallery** (hardcoded initial set)
   - 5–8 templates: Deploy, CI Approval, Hotfix, etc.
   - "Clone template" → opens designer with pre-filled metadata
   - Template JSON schema (defined in docs)

4. **Basic file upload/import**
   - Drag-drop or file picker for .bpmn2.xml
   - Parse and display in canvas
   - Call backend `/validate` endpoint and show errors inline

**Acceptance Criteria:**
- ✅ User can load a template workflow into the designer
- ✅ BPMN canvas renders correctly with Agentwerke extension indicators
- ✅ User can upload a .bpmn2.xml file and see it rendered
- ✅ Export button downloads valid BPMN with extensions intact
- ✅ App is responsive (desktop/tablet tested)

**Backend Dependencies:**
- `GET /api/v1/workflows/templates` — List hardcoded templates
- `POST /api/v1/workflows/validate` — Basic validation (returns array of errors)

---

### Phase 2.4.2: Metadata Editor & Design-Time Validation

**Duration:** 2–3 weeks | **Engineer Focus:** 1 FTE (frontend) + 0.5 FTE (backend)

**Deliverables:**
1. **Metadata editor panel** (right side, when task selected)
   - For serviceTask/scriptTask:
     - Text fields: agent, action, environment
     - Dropdown: purposeType (hardcoded list or backend-driven)
     - Textarea: policyTag (comma-separated or taggable input)
     - Checklist: requiresEvidence (checkboxes: ci_passed, sast_passed, human_approval, etc.)
     - Sliders: maxRetries, retryBackoffSeconds
     - Timeout field: timeoutSeconds
   
   - For userTask:
     - Dropdown: purposeType
     - Textarea: policyTag
     - Notes field (descriptive text for approvers)

2. **Real-time validation layer**
   - As user edits metadata, validate against schema
   - Show live error badges on canvas tasks:
     - ❌ Red outline: missing required field (agent, action, policyTag)
     - ⚠️ Orange outline: timeout not set; consider adding evidence requirements
   - Sidebar error summary: "3 issues found" with drill-down

3. **Form state management**
   - Hook selected task from BPMN.js canvas
   - Sync metadata edits back to BPMN XML (update extensionElements)
   - Unsaved changes indicator (dirty flag)
   - Save to backend → updates BPMN definition

4. **Policy risk indicators**
   - Call `/api/v1/runs/{workflowId}/policy-simulation` endpoint
   - Display risk level (Low/Medium/High/Critical) on each task
   - Tooltip shows: "This task requires human approval (critical risk)"

**Acceptance Criteria:**
- ✅ User can select a task and open metadata editor
- ✅ All Agentwerke extension fields are editable (agent, action, policyTag, etc.)
- ✅ Real-time validation shows missing required fields with visual badges
- ✅ Metadata changes are pushed to backend and persisted
- ✅ Policy risk simulation shows on each task (3-tier UX: basic/detailed)
- ✅ Unit tests cover form validation logic (Zod schema, React Hook Form)

**Backend Dependencies:**
- `POST /api/v1/workflows` — Save updated BPMN with metadata
- `POST /api/v1/workflows/{id}/validate` — Enhanced validation (returns errors + metadata warnings)
- `POST /api/v1/workflows/{id}/policy-simulation` — Dry-run policy checks (returns risk/evidence per task)

---

### Phase 2.4.3: Workflow Publishing & Run Monitoring Setup

**Duration:** 2–3 weeks | **Engineer Focus:** 1 FTE (frontend) + 1 FTE (backend)

**Deliverables:**

1. **Publish workflow flow**
   - "Validate and Publish" button (runs full validation + policy sim)
   - Modal: review risk summary, confirm metadata completeness
   - On success: workflow assigned ID, added to registry, timeline shows "published"
   - Error modal: list blocking issues, link to fix in metadata editor

2. **Run board** (Monitor tab)
   - List of all workflow run instances:
     - Run ID, workflow name, status (Pending/Running/Completed/Failed)
     - Started at, duration
     - Approvals pending count (badge)
   - Click run → opens run detail view (full-screen or drawer)

3. **Run detail view** layout
   - Left: Run timeline (Gantt-like horizontal bars per task)
     - Each bar colored: pending (gray) → running (blue) → completed (green) or failed (red)
     - Hover bar → tooltip (task name, duration, attempts)
     - Click bar → opens task detail panel
   
   - Center: BPMN canvas (same as designer, but read-only)
     - Token position highlighted (which task is current/completed)
     - Completed tasks grayed out
     - Failed tasks highlighted in red / pending in yellow
   
   - Right: Task detail panel (when task bar clicked)
     - Task metadata (agent, action, policyTag, etc.)
     - Policy decision (ALLOW / ESCALATE with reason)
     - Evidence provided (checklist of what was present)
     - Event log (all events for this task: attempted at [time], retried, completed at [time], etc.)
     - Logs/artifacts (if available: link to stdout, diffs, patches)

4. **Live sync via SignalR**
   - Subscribe to run events: `run:{runId}:*`
   - On new event, update timeline in real-time (no refresh needed)
   - Toast notifications for state changes (task completed, approval required, timeout triggered)

**Acceptance Criteria:**
- ✅ User can publish a validated workflow
- ✅ Run board lists all active/completed runs with status
- ✅ Clicking a run opens detailed view with timeline + canvas
- ✅ Canvas highlights current/completed/failed tasks correctly
- ✅ Clicking timeline bar opens task detail with policy decision + evidence
- ✅ Real-time updates via SignalR (new task completed shows instantly, no refresh)
- ✅ Integration tests: publish → start run → monitor progression

**Backend Dependencies:**
- `POST /api/v1/workflows/{id}/publish` — Publish workflow
- `GET /api/v1/runs` — List runs (paginated)
- `GET /api/v1/runs/{runId}` — Get run detail + full event history
- `GET /api/v1/runs/{runId}/tasks/{taskId}` — Get task detail (metadata + policy decision + evidence)
- `WebSocket /ws/runs/{runId}` — Stream run events (task_started, task_completed, policy_decision_made, etc.)

---

### Phase 2.4.4: Diff View, Debugging & Polish

**Duration:** 2–3 weeks | **Engineer Focus:** 1 FTE (frontend) + 0.5 FTE (backend)

**Deliverables:**

1. **Diff view modal** (from run detail, click "Diff from Definition" button)
   - Show side-by-side:
     - Left: Original workflow definition (task list, structure, metadata at design-time)
     - Right: Actual execution trace (what happened at runtime)
   - Unified diff highlighting:
     - Task executed more times than defined (e.g., 3 retries) → show each attempt with duration
     - Policy decision applied constraint (e.g., "allowed with timeout reduced from 300s to 60s") → highlight
     - Extra approvals required (policy escalation) → show as inserted row
   - Search/filter: "Show only differences" checkbox

2. **Approval UI** (in run detail or notification)
   - If run has pending user task (approval required):
     - Show task in UI with "Approve / Deny" buttons
     - Modal to add optional reason/comment
     - Submit → calls `POST /api/v1/runs/{runId}/tasks/{taskId}/decide`
     - On submit, dialog closes, run timeline advances to next task

3. **Error & timeout handling**
   - Failed task → show error details, stack trace (if available)
   - Timeout task → show "This task timed out after 30s; consider increasing timeout in design"
   - Retry info → "Task failed 2 × (backoff 5s), then succeeded on attempt 3"

4. **Performance & UX polish**
   - Timeline lazy-loads events (paginate if 100+ events)
   - Canvas canvas re-renders optimized (React memo, debounce)
   - Dark mode support (Tailwind)
   - Keyboard shortcuts (Cmd+S to save, Cmd+P to publish)
   - Accessibility (WCAG 2.1 AA standard)

5. **Frontend test coverage**
   - Unit tests: form validation, BPMN parsing, event serialization (>80% coverage)
   - Component tests: metadata editor, timeline renderer, diff view
   - E2E tests (Playwright or Cypress): happy path (design → publish → run → monitor → approve)
   - Snapshot tests: BPMN canvas rendering

**Acceptance Criteria:**
- ✅ User can open "Diff from Definition" modal and see what actually happened vs. planned
- ✅ User can approve/deny a pending user task from the run detail view
- ✅ Failed tasks show clear error and remediation hints
- ✅ Timeline handles 100+ events without performance degradation
- ✅ Frontend test coverage >80% on core logic
- ✅ E2E test validates full flow: design → publish → start → monitor → approve
- ✅ Keyboard shortcuts work; accessibility audit passes

**Backend Dependencies:**
- `GET /api/v1/runs/{runId}/diff` — Compare execution events vs. definition (returns structured diff)
- `POST /api/v1/runs/{runId}/tasks/{taskId}/decide` — Submit approval/denial decision
- Define audit event structure to enable rich diff visualization

---

## Frontend Architecture

### Directory Structure

#### Planned (original)

```
src/
├── components/BpmnCanvas/, MetadataEditor/, RunTimeline/, DiffView/, TemplateGallery/
├── hooks/, services/, types/, schemas/, pages/
└── App.tsx
```

#### As-Built (Phases 1–3)

```
src/
├── bpmn/                               # Agentwerke bpmn-js extension modules
│   ├── agentwerkeModdle.ts                # Moddle extension descriptor (namespace, types, attrs)
│   ├── agentwerkeModule.ts                # Aggregates all additionalModules for the Modeler
│   ├── agentwerkePaletteProvider.ts       # Custom palette: "Agent Task", "Approval Gate"
│   ├── agentwerkeMarkers.ts               # CSS markers on import.done + commandStack.changed
│   ├── constants.ts                    # AGENTWERKE_NS_URI, element type constants, createEmptyDiagram()
│   ├── vendor.d.ts                     # TypeScript module declarations for bpmn-js packages
│   └── properties/
│       ├── agentwerkePropertiesProvider.ts # Registers groups in bpmn-js-properties-panel
│       ├── extensionUtil.ts             # getExtension / setExtensionProperty helpers
│       ├── AgentTaskProps.ts            # 9 property entries for agentwerke:AgentTask
│       └── ApprovalProps.ts             # 2 property entries for agentwerke:ApprovalTask
├── components/
│   ├── BpmnModeler.tsx                 # React wrapper: forwardRef handle (getXML, importXML)
│   ├── __mocks__/
│   │   └── BpmnModeler.tsx             # jsdom stub for Vitest (bpmn-js needs SVG layout)
│   └── ... (other existing components)
├── views/
│   ├── WorkflowDesigner.tsx            # 2-col layout: workflow list + bpmn-js canvas
│   └── ... (other views)
├── api/client.ts                       # REST client (fetch-based, no TanStack Query)
└── ...
```

**Key design notes:**
- `BpmnModeler` mounts a `BpmnJS.Modeler` with `additionalModules` (Agentwerke palette, markers, properties provider) and `moddleExtensions: { agentwerke: agentwerkeModdleDescriptor }`.
- The properties panel renders in a dedicated `div.bpmn-modeler-panel` container alongside the canvas, giving a split-pane view without a separate modal/form.
- `setExtensionProperty` calls `modeling.updateModdleProperties` — the standard undoable API — so Ctrl+Z works across metadata edits.
- The `__mocks__/BpmnModeler.tsx` auto-hoists via `vi.mock('../components/BpmnModeler')` in test files; the real component cannot render in jsdom.

### Key Hooks (to be developed)

```typescript
// hook: useBpmnModel.ts
// Load BPMN definition, sync canvas changes back to state
const useBpmnModel = (workflowId: string) => {
  const [bpmn, setBpmn] = useState(null);
  const [dirty, setDirty] = useState(false);
  // Fetch from backend, sync on edit, provide save/export/publish
};

// hook: useMetadataEditor.ts
// Track which task is selected; manage its metadata form state
const useMetadataEditor = (bpmnModel) => {
  const [selectedTaskId, setSelectedTaskId] = useState(null);
  const [metadata, setMetadata] = useState(null);
  const { errors, validate } = useValidation(metadata);
  // On task select from canvas, load its metadata; on form change, validate & update BPMN
};

// hook: useRunPolling.ts
// Subscribe to run events via SignalR; update timeline/canvas in real-time
const useRunPolling = (runId: string) => {
  const [events, setEvents] = useState([]);
  const [runState, setRunState] = useState(null);
  // On connect, subscribe to run:{runId}:*; on event, append and update state
};
```

### State Management Strategy

- **Server state** (workflows, runs, events): TanStack Query for caching, invalidation
- **UI state** (selected task, activeTab): React Context + custom hooks
- **Form state** (metadata editor): React Hook Form + Zod
- **Canvas state** (BPMN.js model): Custom hook + callback on change

---

## Backend API Contracts

### Authentication
All endpoints require `Authorization: Bearer <jwt>` header.

### Endpoints

#### Workflows

```http
GET /api/v1/workflows
  Response: { data: Workflow[], pageInfo: { total, limit, offset } }

POST /api/v1/workflows
  Body: { name, description, bpmnXml, templateId? }
  Response: { id, name, status: "draft", createdAt, errors?: ValidationError[] }

GET /api/v1/workflows/:id
  Response: { id, name, bpmnXml, metadata, status, createdAt, updatedAt }

POST /api/v1/workflows/:id
  Body: { bpmnXml, description? }
  Response: { id, updatedAt }

POST /api/v1/workflows/:id/validate
  Body: {} (uses workflow's bpmnXml)
  Response: {
    valid: boolean,
    errors: {
      nodeId: string,
      elementName: string,
      message: string,
      severity: "error" | "warning"
    }[]
  }

POST /api/v1/workflows/:id/policy-simulation
  Body: {}
  Response: {
    tasks: {
      nodeId: string,
      riskLevel: "Low" | "Medium" | "High" | "Critical",
      requiredApprovals: string[],
      requiredEvidence: string[]
    }[]
  }

POST /api/v1/workflows/:id/publish
  Body: {}
  Response: { id, publishedAt, version, registryUrl }

GET /api/v1/workflows/templates
  Response: { data: Template[] }
    where Template = { id, name, description, bpmnXml, category }
```

#### Runs

```http
GET /api/v1/runs?workflowId=<id>&status=<pending|running|completed>&limit=20&offset=0
  Response: { data: Run[], pageInfo }
    where Run = { id, workflowId, status, startedAt, updatedAt, failureReason? }

POST /api/v1/runs
  Body: { workflowId, input?: object, approverIds?: string[] }
  Response: { id, workflowId, status: "pending", createdAt }

GET /api/v1/runs/:runId
  Response: {
    id, workflowId, status, startedAt, completedAt,
    currentNodeId, events: Event[]
  }
    where Event = { id, type: "task_started" | "task_completed" | "policy_decision_made" | ..., 
                    nodeId, createdAt, payload: object }

POST /api/v1/runs/:runId/cancel
  Response: { id, status: "cancelled", cancelledAt }

GET /api/v1/runs/:runId/diff
  Response: {
    definition: {
      nodes: BpmnNodeDefinition[],
      metadata: object
    },
    execution: {
      taskExecutions: {
        nodeId: string,
        attempts: {
          startedAt: string,
          endedAt: string,
          status: "success" | "failed" | "timedout",
          retryCount: number,
          reason?: string
        }[],
        policyDecision: {
          decision: "allow" | "escalate" | "deny",
          reason: string,
          constraints?: object
        }
      }[]
    },
    diffs: {
      nodeId: string,
      type: "extra_retry" | "policy_constraint" | "escalation_required",
      description: string
    }[]
  }

GET /api/v1/runs/:runId/tasks/:taskId
  Response: {
    nodeId, name, metadata, policyDecision,
    evidence: { key: string, present: boolean, importance: "required" | "optional" }[],
    events: Event[],
    artifacts?: { type: "log" | "diff", url: string }[]
  }

POST /api/v1/runs/:runId/tasks/:taskId/decide
  Body: { decision: "approve" | "deny", reason?: string, constraints?: object }
  Response: { taskId, decision, decidedAt, decidedBy }

WebSocket /ws/runs/:runId
  Subscribes to run events. Server sends:
    { type: "task_started" | "task_completed" | "policy_decision_made", payload: Event }
```

### Type Definitions (Shared with Frontend via OpenAPI schema or TypeScript)

```typescript
// backend/Agentwerke.Application/Contracts/WorkflowContracts.cs
interface ValidationError {
  nodeId: string;
  elementName: string;
  message: string;
  severity: "error" | "warning";
}

interface PolicySimulationResult {
  tasks: {
    nodeId: string;
    riskLevel: "Low" | "Medium" | "High" | "Critical";
    requiredApprovals: string[];
    requiredEvidence: string[];
  }[];
}

interface DiffResult {
  definition: { nodes: BpmnNodeDefinition[]; metadata: object };
  execution: { taskExecutions: TaskExecution[] };
  diffs: DiffItem[];
}

interface TaskExecution {
  nodeId: string;
  attempts: {
    startedAt: string;
    endedAt: string;
    status: "success" | "failed" | "timedout";
    retryCount: number;
    reason?: string;
  }[];
  policyDecision: {
    decision: "allow" | "escalate" | "deny";
    reason: string;
    constraints?: object;
  };
}

interface DiffItem {
  nodeId: string;
  type: "extra_retry" | "policy_constraint" | "escalation_required";
  description: string;
}
```

---

## Acceptance Criteria

### Phase 2.4.1: Canvas & Templates
- [x] BPMN.js canvas renders templates without errors
- [x] User can clone a template and see pre-filled metadata
- [x] File upload works; BPMN XML parses and renders
- [x] Export button produces valid BPMN 2.0 with extensions
- [ ] UI is responsive (desktop/tablet/mobile) — desktop only tested; mobile deferred

### Phase 2.4.2: Metadata & Validation
- [x] Selecting a task opens metadata editor (bpmn-js properties panel sidebar)
- [x] All Agentwerke extension fields are editable (agent, action, policyTag, evidence, retries, timeout)
- [x] Visual error badges appear on canvas for invalid tasks (canvas markers + overlay badges)
- [x] Metadata changes are serialized directly into BPMN XML (via moddle extension elements)
- [ ] Policy risk simulation loads and displays risk levels on tasks — deferred to Phase 2.4.3
- [x] Validation errors surface after backend validate call (via `validateBpmnWorkflow`)
- [x] Tests cover WorkflowDesigner + BpmnModeler mock (39/39 Vitest tests passing)

### Phase 2.4.3: Publishing & Monitoring
- [x] "Validate & Publish" button validates then publishes workflow (via `importWorkflowDefinition` + `publishWorkflowDefinition`)
- [x] Run board lists active/completed runs (Monitor tab, 10s poll via `getRuns`)
- [x] Clicking a run opens detail view with timeline + canvas
- [x] Canvas highlights current/completed/failed tasks correctly (BpmnViewer + raf-* marker CSS)
- [ ] Timeline bars are clickable; opening task detail shows metadata + policy decision — deferred
- [ ] Real-time updates via SignalR (new events appear instantly) — deferred (polling every 10s in place)
- [x] Run event stream test validates end-to-end flow (Monitor tab test in workflowDesigner.integration.test.tsx)

### Phase 2.4.4: Diff & Approvals
- [x] "Diff from Definition" modal shows comparison (RunDiffModal — client-side, DOMParser + run.events)
- [x] Extra retries, policy constraints, escalations are highlighted in diff (deviation badge column)
- [x] Pending user task shows "Approve / Reject / Escalate" buttons
- [x] Submitting approval/denial moves run to next task (`decideApproval` → re-fetch run)
- [x] Failed task shows error in diff deviation column
- [ ] Timeline handles 100+ events without lag (Lighthouse perf score >80) — not measured
- [x] Frontend test coverage: 44 Vitest tests covering all core interactions
- [ ] E2E test (Playwright): design → publish → start → monitor → diff → approve — deferred

---

## Dependencies & Risks

### External Dependencies

| Dependency | Version | Risk | Mitigation |
|---|---|---|---|
| **BPMN.js** | v14+ | Breaking changes in future versions | Pin to v14.x; monitor releases; allocate 1 week for upgrade testing |
| **TanStack Query** | v5+ | Query invalidation patterns can be complex | Document caching strategy in code; test edge cases (race conditions) |
| **SignalR** | .NET 9 | Real-time sync can diverge in high-latency scenarios | Implement event deduplication; test with intentional 5s+ latency |
| **Tailwind CSS** | v3+ | Large file sizes if not purged correctly | Configure PurgeCSS in build pipeline; audit bundle size in CI |

### Known Risks

1. **BPMN.js Canvas Performance**
   - Risk: Large workflows (100+ tasks) may render slowly
   - Mitigation: Implement virtualization for task panels; lazy-load canvas on tab focus
   - Testing: Load 100+ task workflow; measure First Contentful Paint (FCP), Time to Interactive (TTI)

2. **SignalR Scalability**
   - Risk: Many concurrent runs (100+) may overwhelm WebSocket connections
   - Mitigation: Implement connection pooling; batch events on backend before broadcast
   - Testing: Stress test with 50+ concurrent runs in staging

3. **Diff Computation**
   - Risk: Computing diffs for long-running workflows (100+ events) may be slow
   - Mitigation: Cache diffs on backend; implement lazy-load pagination (20 events at a time)
   - Testing: Measure diff response time for 500-event workflow; target <500ms

4. **Template Maintenance**
   - Risk: Hardcoded templates become stale as product evolves
   - Mitigation: Plan for template management UI (Phase 3); document template JSON schema
   - Testing: Validate each template against updated BPMN extension schema

5. **Metadata Form Validation**
   - Risk: Complex Zod schema coupled to backend validation; schema drift
   - Mitigation: Share schema definition between frontend (Zod) and backend (.NET) via OpenAPI; auto-generate frontend types from OpenAPI spec
   - Testing: Contract test: submit form → backend rejects → verify error message matches

### Assumptions to Validate

1. **BPMN.js plugins work with Agentwerke extensions** → Spike: test `extensionElements` parsing
2. **SignalR can broadcast 100+ events/sec without batching** → Performance test in lab
3. **Policy simulation endpoint can return < 200ms for workflow with 50 tasks** → Backend measured
4. **Template-first approach reduces new user time-to-first-workflow by 50%** → Validate in user testing

---

## Estimation & Timeline

| Phase | Duration | FTE | Deliverable | Dependency |
|-------|----------|-----|-------------|-----------|
| 2.4.1 | 2–3w | 1 FTE | Canvas + Templates | P1.4 (UI shell), P2.1 (validator) |
| 2.4.2 | 2–3w | 1 FTE + 0.5 BE | Metadata Editor + Validation | 2.4.1 complete |
| 2.4.3 | 2–3w | 1 FTE + 1 BE | Publishing + Monitoring | 2.4.2 complete, P2.2 (runtime events) |
| 2.4.4 | 2–3w | 1 FTE + 0.5 BE | Diff + E2E Tests | 2.4.3 complete |
| **Total** | **8–12w** | **3–4 FTE** | **Full Studio MVP** | **Phase 1 + Phase 2 runtime** |

### Backend Preparation (Parallel to Phase 2.4.1)

While frontend builds canvas, backend should:
- [ ] Implement validation endpoint (POST /api/v1/workflows/:id/validate)
- [ ] Implement policy simulation endpoint
- [ ] Set up SignalR hub for run events
- [ ] Implement run event stream (task_started, task_completed, policy_decision_made)
- [ ] Add audit event schema to support diff computation

---

## Success Metrics

By end of Phase 2.4, the following should be demonstrable:

1. **Workflow Design**
   - User designs a 10-task workflow end-to-end in <5 minutes using templates
   - All extension metadata is filled (agent, action, policyTag, evidence) without errors
   - Policy risk is visible; user understands which tasks require approval

2. **Publishing**
   - Workflow publishes with 0 validation errors
   - Published workflow lands in registry; version assigned

3. **Monitoring**
   - Run starts; user sees timeline progression in real-time (< 1s latency on event)
   - User clicks a task bar; sees policy decision, evidence checklist, logs
   - Failed task shows clear error + fix suggestion

4. **Debugging**
   - User opens "Diff from Definition" for a run with extra retries
   - Diff clearly shows "Task retried 2x due to timeout" vs. original definition
   - User understands why behavior diverged

5. **Performance**
   - Canvas loads in < 2s
   - Timeline renders 100 events without lag
   - Form validation is sub-100ms

6. **Quality**
   - Frontend test coverage >80%
   - E2E test passes design → publish → run → monitor → approve flow
   - Zero critical bugs in staging

---

## Next Steps

1. **Validate BPMN.js plugin approach** (spike, 3–5 days)
   - Create minimal test: load BPMN with agentwerke:agentTask; verify extensionElements parse
   - Demo to team; confirm architecture is sound

2. **Finalize API contract** with backend team
   - Review endpoint list; confirm naming, request/response shapes
   - Share TypeScript schema (for auto-generation)
   - Agree on SignalR event types

3. **Design templates** (mid-phase-1)
   - Sketch 5 initial templates (Deploy, CI Approval, Hotfix, etc.)
   - Define template JSON schema
   - Hardcode into frontend

4. **Allocate team**
   - Identify 1 senior frontend engineer (BPMN.js + React expertise)
   - Identify 1–2 backend engineers (API + SignalR + event streams)
   - Schedule weekly sync to unblock dependencies

5. **Track progress** in GitHub Issues
   - One issue per phase (P2.4.1, P2.4.2, P2.4.3, P2.4.4)
   - Link to this roadmap; update weekly

---

## References

- [BPMN 2.0 Standard](http://www.omg.org/spec/BPMN/2.0/)
- [bpmn-js Documentation](https://github.com/bpmn-io/bpmn-js)
- [TanStack Query Docs](https://tanstack.com/query/latest)
- [SignalR JavaScript Client](https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client)
- [React Hook Form](https://react-hook-form.com/)
- [Zod Validation](https://zod.dev/)

