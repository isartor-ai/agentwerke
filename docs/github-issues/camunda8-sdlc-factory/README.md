# Camunda 8 SDLC Factory GitHub Issue Drafts

This directory contains one markdown body per GitHub issue for the Camunda 8 SDLC factory architecture shift.

Target repository:

```text
isartor-ai/autofac-private
```

Create the issues with:

```bash
GH_TOKEN=... scripts/create-camunda8-github-issues.sh
```

The script also accepts overrides:

```bash
scripts/create-camunda8-github-issues.sh isartor-ai/autofac-private docs/github-issues/camunda8-sdlc-factory
```

The issue bodies are intentionally small enough for autonomous code agents to implement one issue at a time.
