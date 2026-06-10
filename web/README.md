# Autofac Web UI

React + TypeScript frontend for the Autofac operations cockpit.

## Prerequisites

- Node 20+
- npm 10+

## Local Development

1. Install dependencies:

  npm install

2. Start the development server:

  npm run dev

3. Build production assets:

  npm run build

4. Run lint checks:

  npm run lint

## Environment

- VITE_API_BASE_URL
  - Optional.
  - When omitted, the app uses mock fixtures so it can run offline.
  - When set, the typed API client will call the configured backend.

Example:

VITE_API_BASE_URL=http://localhost:5000

## Implemented Phase-1 Views

- Runs board: /runs
- Run detail: /runs/:runId
- Workflow designer shell: /workflows
- Approvals dashboard: /approvals
- Placeholder sections: /policies, /audit, /integrations, /settings
