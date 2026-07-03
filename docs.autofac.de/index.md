# Autofac User Manual

Autofac is a governed software-delivery control plane for AI agents. It lets a team model delivery work as BPMN, assign steps to agents, gate tool use through policy, pause for human approvals, and keep an evidence trail for every run.

This site is the operator and developer manual for running Autofac, authoring workflows, configuring integrations, and extending the platform.

<div class="doc-map">
  <img src="/autofac-run-map.svg" alt="Map of an Autofac run from trigger to BPMN run, agent task, policy gate, tool call, approval, and evidence." />
</div>

## Choose your path

<div class="manual-grid">
  <a href="/start/quickstart.html">
    <strong>Start in five minutes</strong>
    <span>Run the tokenless quickstart stack and complete the sample workflow.</span>
  </a>
  <a href="/manual/runs.html">
    <strong>Operate a run</strong>
    <span>Start a workflow, inspect its BPMN state, and understand step outcomes.</span>
  </a>
  <a href="/manual/approvals-evidence.html">
    <strong>Approve and audit</strong>
    <span>Handle human gates and download an evidence pack for the run.</span>
  </a>
  <a href="/admin/deployment.html">
    <strong>Deploy Autofac</strong>
    <span>Move from quickstart to a hardened self-hosted deployment.</span>
  </a>
  <a href="/developer/local-development.html">
    <strong>Develop locally</strong>
    <span>Build, test, and extend the .NET control plane and React UI.</span>
  </a>
  <a href="/reference/bpmn-extensions.html">
    <strong>Use the reference</strong>
    <span>Look up BPMN extensions, policy boundaries, schema, and ADRs.</span>
  </a>
</div>

## What you can do with Autofac

- Model SDLC workflows as versioned BPMN instead of prompt transcripts.
- Run model-backed or deterministic agent tasks with bounded permissions.
- Route every connector and tool call through the Tool Gateway.
- Pause at human approval tasks, including high-risk policy decisions.
- Run code-producing work inside Docker, OpenSandbox, or Kubernetes-backed sandboxes.
- Export evidence packs that include prompts, redactions, policy decisions, tool invocations, approvals, model usage, artifacts, and audit records.
- Keep the core platform self-hosted under your own data boundary.

## Before production

The quickstart is intentionally easy: it uses a deterministic mock model provider and development authentication defaults. Production deployments must configure real authentication, secret handling, storage, sandbox isolation, and observability. Start with [Deployment](/admin/deployment), then review [Settings And Secrets](/admin/settings-secrets) and [Auth And Data Residency](/admin/auth-data-residency).
