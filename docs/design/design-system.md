# Agentwerke design system — shared tokens

Single source of truth for the visual language shared by the product UI (`web/`),
the marketing site (`website/`), and the docs site (`docs.agentwerke.de/`).
Each surface implements these as plain CSS variables; there is no shared build
artifact on purpose — the three stacks are independent.

Tracking issue: isartor-ai/autofac-private#198.

## Principles

- Calm over neon. One accent, used sparingly: primary actions, active nav, links.
- Status lives in badges and dots, never in card washes or gradients.
- Flat bordered surfaces: 1px borders, small radii, shadows only for overlays.
- Mono/uppercase is reserved for machine values (IDs, statuses, metrics), not
  for every label.
- Dense but scannable: keep information density, align to a 4px spacing grid.

## Typography

| Role | Font |
| --- | --- |
| UI and prose | Inter, ui-sans-serif, system-ui |
| Machine values (IDs, statuses, metrics, code) | JetBrains Mono, ui-monospace |

Headings use tight tracking (`-0.01em` to `-0.02em`) and weight 600–650.

## Color

### Brand accent (cyan family)

| Token | Value | Use |
| --- | --- | --- |
| `accent-300` | `#5fd4dd` | hover on dark surfaces |
| `accent-400` | `#33c5cf` | default accent on dark surfaces |
| `accent-500` | `#0193a0` | solid fills on light surfaces (white text, large only) |
| `accent-600` | `#017e8a` | default accent text/links on light surfaces (AA 4.7:1) |
| `accent-700` | `#016d77` | hover / active text on light surfaces |

The former neon `#00dce5` is retired; `#33c5cf` keeps the hue with less glare.

### Neutrals (dark, product UI and marketing default)

| Token | Value |
| --- | --- |
| canvas | `#0e0e0f` |
| surface | `#131314` |
| surface-low | `#1c1b1c` |
| surface-container | `#201f20` |
| border | `#3a494a` (soft: 45% alpha) |
| text | `#e5e2e3` / strong `#ffffff` / muted `#849495` |

Light-surface neutrals (marketing light theme, docs light theme) follow the
existing marketing light palette (`#FBFCFC` canvas, `#14201F` text).

### Status (quiet)

| Status | Dark surfaces | Light surfaces |
| --- | --- | --- |
| success / running | `#81c995` | `#1a7f37` |
| warning / awaiting | `#d9b06a` | `#9a6700` |
| error / blocked | `#ffb4ab` | `#b91c1c` |
| info | accent | accent |

Neon lime `#c3f400` is retired everywhere. Status colors appear as badge
text/border and small dots at up to ~13% alpha backgrounds — never as card
gradients or full-card tints.

## Shape and elevation

| Token | Value |
| --- | --- |
| radius (controls, badges) | 4px |
| radius (cards, panels, inputs) | 6px |
| radius (overlays) | 8px |
| shadow | none on cards; reserved for drawers/modals |

## Spacing

4px base grid. Section rhythm inside panels: 12–16px; page gutters 24px.

## Per-surface implementation

- `web/src/index.css` — `:root` variables (`--color-*`).
- `website/assets/styles.css` — `:root` and `[data-theme="light"]` variables.
- `docs.agentwerke.de/.vitepress/theme/custom.css` — VitePress `--vp-c-brand-*`
  variables mapped to the accent family.
