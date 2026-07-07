import { defineConfig } from 'vitepress';

export default defineConfig({
  lang: 'en-US',
  title: 'Agentwerke',
  description: 'User manual for governed AI software delivery with Agentwerke.',
  base: '/',
  lastUpdated: true,
  themeConfig: {
    logo: '/agentwerke-run-map.svg',
    siteTitle: 'Agentwerke Docs',
    search: {
      provider: 'local',
    },
    nav: [
      { text: 'Start', link: '/start/quickstart' },
      { text: 'Manual', link: '/manual/runs' },
      { text: 'Admin', link: '/admin/deployment' },
      { text: 'Developer', link: '/developer/local-development' },
      { text: 'Reference', link: '/reference/bpmn-extensions' },
    ],
    sidebar: [
      {
        text: 'Start Here',
        items: [
          { text: 'What Agentwerke Is', link: '/start/what-is-agentwerke' },
          { text: 'Quickstart', link: '/start/quickstart' },
          { text: 'Core Concepts', link: '/start/core-concepts' },
        ],
      },
      {
        text: 'User Manual',
        items: [
          { text: 'Runs', link: '/manual/runs' },
          { text: 'Approvals And Evidence', link: '/manual/approvals-evidence' },
          { text: 'Model Providers', link: '/manual/model-providers' },
          { text: 'GitHub Issue Trigger', link: '/manual/github-issue-trigger' },
          { text: 'NVIDIA Issue To PR Demo', link: '/manual/demo-issue-to-pr-nvidia' },
          { text: 'Workflow Authoring', link: '/manual/workflow-authoring' },
          { text: 'Agents And Skills', link: '/manual/agents-skills' },
        ],
      },
      {
        text: 'Administrator Guide',
        items: [
          { text: 'Deployment', link: '/admin/deployment' },
          { text: 'Settings And Secrets', link: '/admin/settings-secrets' },
          { text: 'Auth And Data Residency', link: '/admin/auth-data-residency' },
          { text: 'Sandboxes And Storage', link: '/admin/sandboxes-storage' },
        ],
      },
      {
        text: 'Developer Guide',
        items: [
          { text: 'Local Development', link: '/developer/local-development' },
          { text: 'Architecture', link: '/developer/architecture' },
          { text: 'API Overview', link: '/developer/api-overview' },
          { text: 'Extending Agentwerke', link: '/developer/extending-agentwerke' },
        ],
      },
      {
        text: 'Reference',
        items: [
          { text: 'BPMN Extensions', link: '/reference/bpmn-extensions' },
          { text: 'Security Model', link: '/reference/security-model' },
          { text: 'Persistence Schema', link: '/reference/persistence-schema' },
          { text: 'Open Core Boundary', link: '/reference/open-core' },
          { text: 'Architecture Decisions', link: '/reference/architecture-decisions' },
          { text: 'Docs Site Deployment', link: '/reference/docs-site-deployment' },
        ],
      },
    ],
    outline: {
      level: [2, 3],
    },
    editLink: {
      pattern: 'https://github.com/isartor-ai/agentwerke/edit/main/docs.agentwerke.de/:path',
      text: 'Edit this page on GitHub',
    },
    socialLinks: [
      { icon: 'github', link: 'https://github.com/isartor-ai/agentwerke' },
    ],
    footer: {
      message: 'Apache-2.0 open core. Enterprise-only capabilities are labeled in context.',
      copyright: 'Copyright 2026 Isartor AI',
    },
  },
});
