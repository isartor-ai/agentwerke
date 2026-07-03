import { defineConfig } from 'vitepress';

// The Agentwerke user manual (docs.agentwerke.de). Content is the surrounding docs/
// folder, so the manual versions in lockstep with the product — a feature PR can update
// the docs in the same change. Only the curated user-facing pages are published; the rest
// of docs/ is internal engineering material (planning, ADR spikes, the github-issues
// archive) excluded via srcExclude below.
export default defineConfig({
  cleanUrls: true,
  lang: 'en-US',
  title: 'Agentwerke',
  titleTemplate: ':title — Agentwerke Docs',
  description: 'Agentwerke by Isartor AI — the Governed Lights-Out Software Factory. User manual.',
  // The engineering docs cross-link to source files and each other with repo-relative
  // paths that are not VitePress pages; don't fail the build on those.
  ignoreDeadLinks: true,
  srcExclude: [
    'README.md',
    'github-issues/**',
    'design/**',
    'BPMN_UI_Implementation_Roadmap.md',
    'P2.4.2_IMPLEMENTATION.md',
    'architecture-design.md',
    'backend-implementation-plan.md',
    'camunda8-*.md',
    'functional-specification.md',
    'mvp-*.md',
    'ui-cleanup-refactor-plan.md',
    'manual-test-*.md',
    'decisions/ADR-003*.md',
  ],
  // The docs contain literal `{{ ... }}` (e.g. correlationKeyTemplate="{{input.branch_name}}").
  // Move Vue's interpolation delimiters off `{{ }}` so those render as plain text instead of
  // being parsed as Vue expressions and breaking the build.
  vue: {
    template: {
      compilerOptions: {
        delimiters: ['[[[[', ']]]]'],
      },
    },
  },
  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/favicon.svg' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:title', content: 'Agentwerke Documentation' }],
    ['meta', { property: 'og:description', content: 'Governed Lights-Out Software Factory by Isartor AI.' }],
    ['meta', { property: 'og:url', content: 'https://docs.agentwerke.de/' }],
  ],
  sitemap: { hostname: 'https://docs.agentwerke.de' },
  themeConfig: {
    siteTitle: 'Agentwerke Docs',
    nav: [
      { text: 'Guide', link: '/getting-started' },
      { text: 'Reference', link: '/architecture' },
      { text: 'Operations', link: '/deployment' },
      { text: 'agentwerke.de', link: 'https://agentwerke.de' },
    ],
    sidebar: [
      {
        text: 'Getting started',
        items: [
          { text: 'Quickstart (5-min, tokenless)', link: '/getting-started' },
          { text: 'Open-core model', link: '/open-core' },
        ],
      },
      {
        text: 'Concepts & reference',
        items: [
          { text: 'Architecture (as-built)', link: '/architecture' },
          { text: 'Security model', link: '/security-model' },
          { text: 'BPMN extensions', link: '/bpmn-extensions' },
          { text: 'Authoring agents & skills', link: '/agent-and-skill-authoring' },
          { text: 'GitHub issue trigger', link: '/github-issue-trigger' },
        ],
      },
      {
        text: 'Operations',
        items: [
          { text: 'Deployment', link: '/deployment' },
          { text: 'Auth & data residency', link: '/deployment-auth-data-residency' },
          { text: 'Persistence schema', link: '/persistence-schema' },
          { text: 'Settings', link: '/settings' },
        ],
      },
      {
        text: 'Decisions',
        items: [
          { text: 'ADR-002 — Agentwerke runtime by default', link: '/decisions/ADR-002-use-bpmn-centric-agentwerke-runtime-by-default' },
          { text: 'ADR-001 — (superseded) Camunda 8', link: '/decisions/ADR-001-use-camunda8-for-production-bpmn-runtime' },
        ],
      },
    ],
    search: { provider: 'local' },
    editLink: {
      pattern: 'https://github.com/isartor-ai/agentwerke/edit/main/docs/:path',
      text: 'Edit this page on GitHub',
    },
    socialLinks: [{ icon: 'github', link: 'https://github.com/isartor-ai/agentwerke' }],
    footer: {
      message: 'Agentwerke by Isartor AI — Governed Lights-Out Software Factory.',
      copyright: '© 2026 Isartor AI',
    },
  },
});
