---
name: Kinetic Industrialism
colors:
  surface: '#131314'
  surface-dim: '#131314'
  surface-bright: '#3a393a'
  surface-container-lowest: '#0e0e0f'
  surface-container-low: '#1c1b1c'
  surface-container: '#201f20'
  surface-container-high: '#2a2a2b'
  surface-container-highest: '#353436'
  on-surface: '#e5e2e3'
  on-surface-variant: '#b9caca'
  inverse-surface: '#e5e2e3'
  inverse-on-surface: '#313031'
  outline: '#849495'
  outline-variant: '#3a494a'
  surface-tint: '#00dce5'
  primary: '#e9feff'
  on-primary: '#003739'
  primary-container: '#00f5ff'
  on-primary-container: '#006c71'
  inverse-primary: '#00696e'
  secondary: '#ffffff'
  on-secondary: '#283500'
  secondary-container: '#c3f400'
  on-secondary-container: '#556d00'
  tertiary: '#fff9f7'
  on-tertiary: '#621100'
  tertiary-container: '#ffd4ca'
  on-tertiary-container: '#b92a00'
  error: '#ffb4ab'
  on-error: '#690005'
  error-container: '#93000a'
  on-error-container: '#ffdad6'
  primary-fixed: '#63f7ff'
  primary-fixed-dim: '#00dce5'
  on-primary-fixed: '#002021'
  on-primary-fixed-variant: '#004f53'
  secondary-fixed: '#c3f400'
  secondary-fixed-dim: '#abd600'
  on-secondary-fixed: '#161e00'
  on-secondary-fixed-variant: '#3c4d00'
  tertiary-fixed: '#ffdad2'
  tertiary-fixed-dim: '#ffb4a2'
  on-tertiary-fixed: '#3c0700'
  on-tertiary-fixed-variant: '#8a1d00'
  background: '#131314'
  on-background: '#e5e2e3'
  surface-variant: '#353436'
typography:
  headline-xl:
    fontFamily: Inter
    fontSize: 32px
    fontWeight: '700'
    lineHeight: 40px
    letterSpacing: -0.02em
  headline-lg:
    fontFamily: Inter
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
    letterSpacing: -0.01em
  headline-md:
    fontFamily: Inter
    fontSize: 20px
    fontWeight: '600'
    lineHeight: 28px
  body-lg:
    fontFamily: Inter
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  body-md:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  body-sm:
    fontFamily: Inter
    fontSize: 12px
    fontWeight: '400'
    lineHeight: 16px
  code-md:
    fontFamily: JetBrains Mono
    fontSize: 13px
    fontWeight: '400'
    lineHeight: 20px
  label-caps:
    fontFamily: JetBrains Mono
    fontSize: 11px
    fontWeight: '700'
    lineHeight: 16px
    letterSpacing: 0.05em
rounded:
  sm: 0.125rem
  DEFAULT: 0.25rem
  md: 0.375rem
  lg: 0.5rem
  xl: 0.75rem
  full: 9999px
spacing:
  base: 4px
  xs: 4px
  sm: 8px
  md: 16px
  lg: 24px
  xl: 48px
  gutter: 16px
  margin-mobile: 16px
  margin-desktop: 32px
---

## Brand & Style

This design system is engineered for the high-velocity world of automated software production. It adopts a "Dark Software Factory" aesthetic, prioritizing efficiency, observability, and technical precision. The brand personality is authoritative yet unobtrusive, designed to fade into the background while the user focuses on complex workflows and data streams.

The visual style is **Corporate Modern** with a **Technological/Industrial** edge. It utilizes a high-density layout to maximize information throughput, making it ideal for power users who manage large-scale automation and infrastructure. The emotional response is one of total control and peak performance—a digital cockpit for the modern engineer.

## Colors

The palette is rooted in deep, monochromatic tones to reduce eye strain during long sessions.
- **Primary (Electric Cyan):** Reserved for primary actions, focus states, and active workflow paths.
- **Secondary (Cyber Lime):** Used exclusively for "Success," "Running," and "Active" status indicators. It provides high-contrast visibility against dark backgrounds.
- **Tertiary (Neon Orange):** Used for warnings or high-priority interrupts.
- **Neutrals:** A scale of deep charcoals and slate grays defines the UI structure. The background is nearly black, with surfaces stepping up in lightness to indicate hierarchy.

Accent colors should be used sparingly (no more than 5% of the screen real estate) to ensure they retain their functional "signal" value within the noise of data.

## Typography

The typography system balances legibility with data density. 
- **Inter** handles all UI labels, body text, and headlines, providing a clean, neutral sans-serif foundation.
- **JetBrains Mono** is utilized for technical data, logs, code blocks, and system status labels. Its monospaced nature ensures that columns of numbers and symbols remain aligned for easy scanning.

For mobile viewports, headlines scale down by approximately 20%, but body and code sizes remain constant to ensure functional legibility. High-contrast white is used for primary text, while slate gray is used for secondary metadata.

## Layout & Spacing

This design system uses a **Fluid Grid** model with high-density spacing. The layout is based on a 4px baseline grid to allow for tight, professional alignments common in IDEs and dashboard environments.

- **Desktop:** 12-column grid with 16px gutters. Sidebars are typically fixed at 240px or 300px to maximize the central workspace.
- **Mobile:** 4-column grid with 16px margins. Content reflows into a single vertical stream, with horizontal scrolling permitted for wide code blocks.
- **Density:** Components use "Compact" padding by default (8px internal padding) to ensure more information is visible above the fold.

## Elevation & Depth

In a dark software factory, depth is conveyed through **Tonal Layers** and **Low-Contrast Outlines** rather than heavy shadows.

1.  **Background (Level 0):** The deepest charcoal. Used for the main application shell.
2.  **Surface (Level 1):** Slightly lighter slate. Used for cards, sidebars, and panels.
3.  **Overlay (Level 2):** The lightest slate gray. Used for modals and tooltips.
4.  **Borders:** Every container uses a 1px solid border (#2D3139). This creates a "blueprint" feel that reinforces the industrial narrative.

Shadows are used only for high-context floating elements (like dropdowns), utilizing a 0% blur, 4px offset "hard" shadow to maintain the technical aesthetic.

## Shapes

The shape language is geometric and disciplined. A **Soft (0.25rem)** roundedness is applied to buttons and inputs to provide just enough approachability without sacrificing the professional, "engineered" look. 

Large containers and cards use the same 4px radius. Status pips and small indicators may use a full pill shape to distinguish them as interactive or high-signal elements within the rigid grid.

## Components

- **Buttons:** Primary buttons use a solid Electric Cyan fill with black text. Ghost buttons use Cyan borders and text. Secondary actions use Slate Gray outlines.
- **Status Chips:** Small, high-contrast badges. "Running" uses Cyber Lime background; "Error" uses Neon Orange; "Idle" uses Slate Gray. Use JetBrains Mono for the text inside chips.
- **Input Fields:** Darker than the surface they sit on. Active states are indicated by a 1px Electric Cyan border glow.
- **Cards:** No shadows. Defined by 1px Slate Gray borders and a slightly elevated surface color. Headers within cards should have a subtle bottom border.
- **Data Tables:** High density. Row hover states should use a subtle highlight (#1E2024). Use monospaced fonts for all numerical data.
- **Workflow Nodes:** Representing automation steps. Rectangular with 4px radius, connected by 2px Cyan lines to indicate active flow.