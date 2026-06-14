import type { RunStep, RunStatus } from '../types';

export interface BpmnRunGraphProps {
  steps: RunStep[];
  currentStep?: string | null;
  runStatus?: RunStatus;
  selectedStepId?: string | null;
  onSelectStep?: (stepId: string | null) => void;
}

// ── Layout constants ──────────────────────────────────────────────────────────
const TW = 98;              // task rectangle width
const TH = 50;              // task rectangle height
const TRX = 6;              // task border-radius
const ER = 16;              // event circle radius
const AW = 36;              // arrow slot (gap between node edges)
const PH = 18;              // horizontal padding
const PV = 24;              // top padding
const CY = PV + TH / 2;    // vertical centre of all shapes
const BADGE_Y = PV + TH + 9;
const STATUS_Y = BADGE_Y + 13;
const SVG_H = STATUS_Y + 16;

// ── Node types ────────────────────────────────────────────────────────────────
type GNode =
  | { kind: 'start'; id: string }
  | { kind: 'end'; id: string }
  | { kind: 'step'; step: RunStep };

type StepNode = { kind: 'step'; step: RunStep };

function hw(n: GNode): number {
  return n.kind === 'step' ? TW / 2 : ER;
}

function buildNodes(
  steps: RunStep[],
  currentStep: string | null | undefined,
  runStatus: RunStatus | undefined,
): GNode[] {
  const nodes: GNode[] = [{ kind: 'start', id: '__start__' }];
  for (const s of steps) nodes.push({ kind: 'step', step: s });

  // Synthesise the awaiting-approval user task when the engine hasn't
  // persisted it as a step record yet (common for userTask steps).
  if (
    runStatus === 'awaiting_approval' &&
    currentStep &&
    !steps.some((s) => s.name === currentStep)
  ) {
    nodes.push({
      kind: 'step',
      step: { id: `__synth_${currentStep}__`, name: currentStep, type: 'userTask', status: 'awaiting_approval' },
    });
  }

  nodes.push({ kind: 'end', id: '__end__' });
  return nodes;
}

function calcCentres(nodes: GNode[]): number[] {
  const xs: number[] = [];
  let x = PH;
  for (let i = 0; i < nodes.length; i++) {
    const h = hw(nodes[i]);
    xs.push(x + h);
    x += h * 2 + (i < nodes.length - 1 ? AW : 0);
  }
  return xs;
}

function svgWidth(nodes: GNode[], xs: number[]): number {
  return xs[xs.length - 1] + hw(nodes[nodes.length - 1]) + PH;
}

// ── Dark-theme colour palette ─────────────────────────────────────────────────
type Palette = { fill: string; stroke: string; nameText: string; accentText: string };

const STATUS_PALETTE: Record<string, Palette> = {
  completed: {
    fill: 'rgba(195,244,0,0.09)',
    stroke: 'rgba(195,244,0,0.70)',
    nameText: '#e5e2e3',
    accentText: '#c3f400',
  },
  running: {
    fill: 'rgba(0,220,229,0.09)',
    stroke: 'rgba(0,220,229,0.70)',
    nameText: '#e5e2e3',
    accentText: '#00dce5',
  },
  awaiting_approval: {
    fill: 'rgba(255,180,162,0.10)',
    stroke: 'rgba(255,180,162,0.75)',
    nameText: '#e5e2e3',
    accentText: '#ffb4a2',
  },
  failed: {
    fill: 'rgba(255,180,171,0.09)',
    stroke: 'rgba(255,180,171,0.70)',
    nameText: '#e5e2e3',
    accentText: '#ffb4ab',
  },
  blocked: {
    fill: 'rgba(255,180,171,0.09)',
    stroke: 'rgba(255,180,171,0.70)',
    nameText: '#e5e2e3',
    accentText: '#ffb4ab',
  },
  cancelled: {
    fill: 'rgba(132,148,149,0.07)',
    stroke: 'rgba(132,148,149,0.45)',
    nameText: '#849495',
    accentText: '#849495',
  },
};

const PENDING_PALETTE: Palette = {
  fill: '#1c1b1c',
  stroke: '#3a494a',
  nameText: '#849495',
  accentText: '#849495',
};

function palette(step: RunStep): Palette {
  return STATUS_PALETTE[step.status] ?? PENDING_PALETTE;
}

// ── Label helpers ─────────────────────────────────────────────────────────────
function splitLabel(name: string, max = 13): [string, string] {
  if (name.length <= max) return [name, ''];
  const words = name.split(' ');
  let line1 = '';
  let cut = 0;
  for (let i = 0; i < words.length; i++) {
    const candidate = i === 0 ? words[i] : `${line1} ${words[i]}`;
    if (candidate.length <= max) { line1 = candidate; cut = i + 1; }
    else break;
  }
  if (!line1) return [name.slice(0, max - 1) + '…', ''];
  const rest = words.slice(cut).join(' ');
  return [line1, rest.length > max ? rest.slice(0, max - 1) + '…' : rest];
}

// ── Step node ─────────────────────────────────────────────────────────────────
interface StepNodeProps {
  node: StepNode;
  cx: number;
  selected: boolean;
  onSelect?: (id: string | null) => void;
}

function StepShape({ node, cx, selected, onSelect }: StepNodeProps) {
  const { step } = node;
  const p = palette(step);
  const isActive = step.status === 'running' || step.status === 'awaiting_approval';
  const isDone = step.status === 'completed';
  const isFailed = step.status === 'failed' || step.status === 'blocked';
  const [l1, l2] = splitLabel(step.name);
  const textCY = CY + (l2 ? -6 : 0);

  const toggle = () => onSelect?.(selected ? null : step.id);
  const keyDown = (e: React.KeyboardEvent) => { if (e.key === 'Enter' || e.key === ' ') toggle(); };

  return (
    <g
      role="button"
      aria-label={`${step.name} — ${step.status.replace(/_/g, ' ')}`}
      aria-pressed={selected}
      tabIndex={0}
      style={{ cursor: 'pointer', outline: 'none' }}
      onClick={toggle}
      onKeyDown={keyDown}
    >
      {/* Full name tooltip */}
      <title>{step.name} ({step.status.replace(/_/g, ' ')})</title>

      {/* Pulse ring for active nodes */}
      {isActive && (
        <rect
          x={cx - TW / 2 - 5}
          y={CY - TH / 2 - 5}
          width={TW + 10}
          height={TH + 10}
          rx={TRX + 5}
          fill="none"
          stroke={p.stroke}
          strokeWidth="2"
          className="bpmn-pulse-ring"
        />
      )}

      {/* Main task rectangle */}
      <rect
        x={cx - TW / 2}
        y={CY - TH / 2}
        width={TW}
        height={TH}
        rx={TRX}
        fill={p.fill}
        stroke={p.stroke}
        strokeWidth={step.type === 'userTask' ? 2 : 1.5}
        strokeDasharray={step.type === 'userTask' ? '5 2' : undefined}
      />

      {/* Selection ring */}
      {selected && (
        <rect
          x={cx - TW / 2 - 7}
          y={CY - TH / 2 - 7}
          width={TW + 14}
          height={TH + 14}
          rx={TRX + 7}
          fill="none"
          stroke="rgba(0,220,229,0.9)"
          strokeWidth="2"
          strokeDasharray="4 3"
        />
      )}

      {/* Node name — two lines max */}
      <text
        x={cx}
        y={textCY}
        textAnchor="middle"
        dominantBaseline="middle"
        fontSize="10"
        fontWeight="600"
        fill={p.nameText}
        pointerEvents="none"
      >
        {l1}
      </text>
      {l2 && (
        <text
          x={cx}
          y={CY + 7}
          textAnchor="middle"
          dominantBaseline="middle"
          fontSize="10"
          fontWeight="600"
          fill={p.nameText}
          pointerEvents="none"
        >
          {l2}
        </text>
      )}

      {/* Status corner icon */}
      {isDone && (
        <text
          x={cx + TW / 2 - 9}
          y={CY - TH / 2 + 10}
          textAnchor="middle"
          dominantBaseline="middle"
          fontSize="12"
          fill={p.accentText}
          pointerEvents="none"
        >
          ✓
        </text>
      )}
      {isFailed && (
        <text
          x={cx + TW / 2 - 9}
          y={CY - TH / 2 + 10}
          textAnchor="middle"
          dominantBaseline="middle"
          fontSize="12"
          fill={p.accentText}
          pointerEvents="none"
        >
          ✕
        </text>
      )}

      {/* Agent badge below shape */}
      {step.agentName && (
        <text
          x={cx}
          y={BADGE_Y}
          textAnchor="middle"
          fontSize="9"
          fill="#00dce5"
          fontWeight="500"
          pointerEvents="none"
        >
          {step.agentName}
        </text>
      )}

      {/* Status label */}
      <text
        x={cx}
        y={STATUS_Y}
        textAnchor="middle"
        fontSize="9"
        fill={p.accentText}
        pointerEvents="none"
      >
        {step.status.replace(/_/g, ' ')}
      </text>
    </g>
  );
}

// ── Boundary nodes (Start / End) ──────────────────────────────────────────────
function StartNode({ cx, anyStarted }: { cx: number; anyStarted: boolean }) {
  return (
    <g>
      <title>Workflow start</title>
      <circle
        cx={cx}
        cy={CY}
        r={ER}
        fill={anyStarted ? 'rgba(195,244,0,0.12)' : '#1c1b1c'}
        stroke={anyStarted ? 'rgba(195,244,0,0.7)' : '#3a494a'}
        strokeWidth="1.5"
      />
      <text x={cx} y={STATUS_Y} textAnchor="middle" fontSize="9" fill="#849495">
        start
      </text>
    </g>
  );
}

function EndNode({ cx, allDone, anyFailed }: { cx: number; allDone: boolean; anyFailed: boolean }) {
  const stroke = allDone ? 'rgba(195,244,0,0.7)' : anyFailed ? 'rgba(255,180,171,0.7)' : '#3a494a';
  const fill = allDone ? 'rgba(195,244,0,0.12)' : anyFailed ? 'rgba(255,180,171,0.09)' : '#1c1b1c';
  return (
    <g>
      <title>Workflow end</title>
      <circle cx={cx} cy={CY} r={ER} fill={fill} stroke={stroke} strokeWidth="3" />
      <circle cx={cx} cy={CY} r={ER - 5} fill={stroke} opacity={allDone || anyFailed ? 0.5 : 0.2} />
      <text x={cx} y={STATUS_Y} textAnchor="middle" fontSize="9" fill="#849495">
        end
      </text>
    </g>
  );
}

// ── Main component ────────────────────────────────────────────────────────────
const LEGEND = [
  { fill: 'rgba(195,244,0,0.09)', stroke: 'rgba(195,244,0,0.70)', label: 'completed' },
  { fill: 'rgba(0,220,229,0.09)', stroke: 'rgba(0,220,229,0.70)', label: 'running' },
  { fill: 'rgba(255,180,162,0.10)', stroke: 'rgba(255,180,162,0.75)', label: 'awaiting approval' },
  { fill: '#1c1b1c', stroke: '#3a494a', label: 'pending' },
  { fill: 'rgba(255,180,171,0.09)', stroke: 'rgba(255,180,171,0.70)', label: 'failed' },
];

export function BpmnRunGraph({ steps, currentStep, runStatus, selectedStepId, onSelectStep }: BpmnRunGraphProps) {
  const nodes = buildNodes(steps, currentStep, runStatus);
  const xs = calcCentres(nodes);
  const W = svgWidth(nodes, xs);

  const anyStarted = steps.length > 0;
  const allDone = steps.length > 0 && steps.every((s) => s.status === 'completed');
  const anyFailed = steps.some((s) => s.status === 'failed' || s.status === 'blocked');

  return (
    <section className="panel graph-panel" aria-label="BPMN workflow graph">
      <h2>BPMN Flow</h2>
      <div style={{ overflowX: 'auto' }}>
        <svg
          width={W}
          height={SVG_H}
          viewBox={`0 0 ${W} ${SVG_H}`}
          aria-label="Workflow execution flow"
          role="img"
        >
          <defs>
            <marker
              id="bpmn-arrow"
              markerWidth="8"
              markerHeight="6"
              refX="7"
              refY="3"
              orient="auto"
              markerUnits="userSpaceOnUse"
            >
              <polygon points="0 0, 8 3, 0 6" fill="#3a494a" />
            </marker>
          </defs>

          {/* Arrows between nodes */}
          {nodes.slice(0, -1).map((n, i) => (
            <line
              key={`arrow-${i}`}
              x1={xs[i] + hw(n) + 2}
              y1={CY}
              x2={xs[i + 1] - hw(nodes[i + 1]) - 2}
              y2={CY}
              stroke="#3a494a"
              strokeWidth="1.5"
              markerEnd="url(#bpmn-arrow)"
            />
          ))}

          {/* Node shapes */}
          {nodes.map((node, i) => {
            const cx = xs[i];
            if (node.kind === 'start') return <StartNode key="start" cx={cx} anyStarted={anyStarted} />;
            if (node.kind === 'end') return <EndNode key="end" cx={cx} allDone={allDone} anyFailed={anyFailed} />;
            return (
              <StepShape
                key={node.step.id}
                node={node}
                cx={cx}
                selected={selectedStepId === node.step.id}
                onSelect={onSelectStep}
              />
            );
          })}
        </svg>
      </div>

      <div className="graph-legend">
        {LEGEND.map(({ fill, stroke, label }) => (
          <span key={label} className="graph-legend-item">
            <span
              className="graph-legend-swatch"
              style={{ background: fill, borderColor: stroke }}
            />
            {label}
          </span>
        ))}
      </div>
    </section>
  );
}
