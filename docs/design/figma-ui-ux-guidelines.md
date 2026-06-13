# Autofac UI/UX and Figma Handoff

## Source Inputs

- Design system: `docs/design/DESIGN.md`
- Live product reference: `https://autofac-prime-149586821318.europe-west3.run.app/`
- Figma-ready mockup: `docs/design/mockups/execution-monitoring-refined.svg`

## Figma Setup

1. Create a desktop frame at `1440 x 900`.
2. Import `execution-monitoring-refined.svg` into the frame.
3. Create color styles from the Kinetic Industrialism tokens:
   - Surface base: `#0e0e0f`, `#131314`, `#201f20`
   - Borders: `#2a3839`, `#3a494a`
   - Primary signal: `#00dce5`
   - Success/running signal: `#c3f400`
   - Warning signal: `#ffb4a2`
   - Error signal: `#ffb4ab`
   - Primary text: `#e5e2e3`
   - Secondary text: `#b9caca`
4. Create text styles:
   - Headline XL: Inter, 32px, 700, 40px line height
   - Headline LG: Inter, 24px, 600, 32px line height
   - Body MD: Inter, 14px, 400, 20px line height
   - Label Caps: JetBrains Mono, 11px, 700, 16px line height
   - Code MD: JetBrains Mono, 13px, 400, 20px line height
5. Use a 4px spacing baseline with 16px gutters and 32px desktop page margin.

## Product Layout

The application should feel like an operational cockpit, not a marketing dashboard. Keep the first viewport focused on control, status, and execution confidence.

- Left navigation is fixed on desktop and hidden on small screens.
- Top bar carries search, environment status, and operator identity.
- Main content uses dense panels with clear borders, compact headings, and high-signal status chips.
- Primary actions use Electric Cyan. Running and healthy states use Cyber Lime. Warnings use Neon Orange.
- Tables remain available for detail, but the first screen should summarize live health before the ledger.

## Refined Execution Monitoring Screen

The refined screen is organized as:

- App shell with Autofac branding and deployment CTA.
- Top command bar with search, live environment state, and operator profile.
- Performance Metrics panel for throughput, success rate, and deployment frequency.
- System Health panel for runtime fabric state.
- KPI strip for running, failed, awaiting approval, and blocked work.
- Filters for status, risk, and search.
- Live Runs cards for fast scanning.
- Global Log Stream for recent event telemetry.
- Run Ledger table for precise drill-down.

## Interaction Guidelines

- Keep all clickable workflow and run elements keyboard accessible.
- Use status color only for semantic state. Do not use accents as decoration.
- Preserve visible focus outlines using the primary cyan token.
- Do not hide table detail behind cards; cards provide scan value, tables provide audit value.
- On mobile, stack the top bar, panels, KPIs, filters, cards, and table in a single column.
- Maintain body and code text sizes on mobile; reduce only headings when needed.

## Component Notes

- Buttons: 4px radius, compact padding, primary cyan fill for the highest-value action.
- Panels: dark tonal surfaces, 1px outline, no soft shadows.
- Status chips: JetBrains Mono, uppercase, compact, color-coded by state.
- Logs: monospaced timestamps and event tags, with message text in Inter.
- Data tables: dense rows, sticky visual hierarchy through type and row hover states.
- Workflow nodes: rectangular controls, visible affordance, accessible button semantics.

## Implementation Mapping

- Theme and shared components: `web/src/index.css`
- App shell and navigation: `web/src/layout/AppShell.tsx`
- Execution monitoring screen: `web/src/views/RunBoard.tsx`
- Workflow canvas accessibility and empty states: `web/src/views/WorkflowDesigner.tsx`
