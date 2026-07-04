# Agentwerke documentation

Start here.

## Getting started
- [Getting started (5-min tokenless quickstart)](getting-started.md)
- [Open-core model](open-core.md) — what's OSS vs commercial

## Concepts & reference
- [Architecture (as-built)](architecture.md)
- [Security model](security-model.md)
- [BPMN extensions reference](bpmn-extensions.md) — the `agentwerke:` elements
- [Authoring agents and skills](agent-and-skill-authoring.md)
- [GitHub issue trigger](github-issue-trigger.md) — starting a run from a labeled issue

## Operations
- [Deployment](deployment.md)
- [Auth & data residency](deployment-auth-data-residency.md)
- [Persistence schema](persistence-schema.md)

## Decisions
- [ADR-002 — BPMN-centric Agentwerke runtime by default](decisions/ADR-002-use-bpmn-centric-agentwerke-runtime-by-default.md)
- [ADR-001 — (superseded) Camunda 8 for production runtime](decisions/ADR-001-use-camunda8-for-production-bpmn-runtime.md)

## Contributing
- [CONTRIBUTING](../CONTRIBUTING.md) · [Code of Conduct](../CODE_OF_CONDUCT.md) · [Security policy](../SECURITY.md) · [Changelog](../CHANGELOG.md)

## Docs site (docs.agentwerke.de)

This folder is also the source for the user-manual site, built with
[VitePress](https://vitepress.dev) (config in [`.vitepress/config.ts`](.vitepress/config.ts)).
Only the curated user-facing pages are published; internal planning docs, ADR
spikes, and the `github-issues/` archive are excluded via `srcExclude`.

```bash
cd docs
npm install
npm run docs:dev      # local dev server with hot reload
npm run docs:build    # production build → .vitepress/dist
```

Pushes to `main` touching `docs/**` deploy to GitHub Pages via
[`.github/workflows/docs-site.yml`](../.github/workflows/docs-site.yml); the
custom domain comes from [`public/CNAME`](public/CNAME). One-time setup: repo
**Settings → Pages → Source: GitHub Actions**, and a `docs.agentwerke.de` DNS
`CNAME` → `isartor-ai.github.io`.
