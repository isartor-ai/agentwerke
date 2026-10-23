import { test } from '@playwright/test';
import {
  mountVideoStage,
   moveCursorTo,
  prepareCleanScreenshotPage,
  setCaption,
  smoothClick,
  smoothHover,
  waitForAppSettled,
} from './helpers/demoStage';

test.describe('Agentwerke 90s Choreographed Product Demo Video', () => {
  test('records 5-act end-to-end V-Model control plane tour', async ({ page }) => {
    await prepareCleanScreenshotPage(page);

    // =========================================================================
    // ACT I (~16s): Executive Control Plane & Run Board (/runs)
    // =========================================================================
    await page.goto('/runs');
    await waitForAppSettled(page, 900);
    await mountVideoStage(page);

    await setCaption(
      page,
      'ACT I • CONTROL PLANE',
      'Enterprise Control Plane for Autonomous V-Model Engineering',
      'Live fleet telemetry, sandbox readiness signals, and multi-agent SDLC run ledger'
    );
    await page.waitForTimeout(2400);

    const kpiCards = page.locator('.kpi-card, .stat-card, .metric-card');
    if ((await kpiCards.count()) > 0) {
      await smoothHover(page, kpiCards.first(), 1400);
    } else {
      await moveCursorTo(page, 420, 210, 1400);
    }

    await setCaption(
      page,
      'ACT I • RUN LEDGER',
      'Deterministic Governance Across Every Workflow State',
      'Tracking completed V-Model releases, HITL approval gates, self-healing rework loops, and policy blocks'
    );

    const liveRunCard = page
      .locator('.run-card, tr')
      .filter({ hasText: /run-showcase-vmodel-001|V-Model Autonomous SDLC Pipeline/i })
      .first();
    if (await liveRunCard.isVisible().catch(() => false)) {
      await smoothHover(page, liveRunCard, 2200);
    } else {
      await moveCursorTo(page, 680, 480, 2200);
    }

    const approvalRunRow = page
      .locator('.run-card, tr')
      .filter({ hasText: /run-showcase-approval-002|waiting_user|awaiting_approval/i })
      .first();
    if (await approvalRunRow.isVisible().catch(() => false)) {
      await smoothHover(page, approvalRunRow, 2000);
    }

    await page.waitForTimeout(1600);

    // =========================================================================
    // ACT II (~18s): BPMN 2.0 Workflow Studio & Template Factory (/workflows)
    // =========================================================================
    const workflowsNav = page.locator('a[href="/workflows"], nav >> text=Workflows').first();
    if (await workflowsNav.isVisible().catch(() => false)) {
      await smoothClick(page, workflowsNav, 1000);
    } else {
      await page.goto('/workflows');
      await waitForAppSettled(page, 900);
    }

    await setCaption(
      page,
      'ACT II • WORKFLOW STUDIO',
      'Guided V-Model Factory & BPMN 2.0 Process Designer',
      'Instantiate production SDLC blueprints or author custom BPMN 2.0 agent graphs'
    );
    await page.waitForTimeout(2200);

    const vmodelWorkflowItem = page
      .locator('button.workflow-item')
      .filter({ hasText: 'V-Model Autonomous SDLC Pipeline' })
      .first();
    if (await vmodelWorkflowItem.isVisible().catch(() => false)) {
      await smoothClick(page, vmodelWorkflowItem, 1600);
    }

    await setCaption(
      page,
      'ACT II • V-MODEL CONTRACTS',
      'Static BPMN Validation & Bilateral Evidence Bindings',
      'Every V-Model stage declares upstream tracesTo obligations and downstream verification gates'
    );

    const validateBtn = page.getByRole('button', { name: 'Validate', exact: true });
    if (await validateBtn.isVisible().catch(() => false)) {
      await smoothClick(page, validateBtn, 1500);
    }

    const implNode = page.locator('[data-element-id="ImplementComponents"]').first();
    if (await implNode.isVisible().catch(() => false)) {
      await smoothClick(page, implNode, 2200);
    }

    const factoryBtn = page.getByRole('tab', { name: 'Factory', exact: true });
    if (await factoryBtn.isVisible().catch(() => false)) {
      await smoothClick(page, factoryBtn, 1800);
    }

    await page.waitForTimeout(1400);

    // =========================================================================
    // ACT III (~26s): Live Run Detail, OpenSandbox, LLM Trace & Traceability
    // =========================================================================
    await page.goto('/runs/run-showcase-vmodel-001');
    await waitForAppSettled(page, 1100);
    await mountVideoStage(page);

    await setCaption(
      page,
      'ACT III • LIVE BPMN TELEMETRY',
      'End-to-End V-Model Run Inspection (CHG-204: OAuth2 PKCE & Token Rotation)',
      'Color-coded BPMN execution path, step-by-step agent reasoning, and deterministic gateway decisions'
    );
    await page.waitForTimeout(2400);

    const completedSummary = page.locator('summary.timeline-completed-summary').first();
    if (await completedSummary.isVisible().catch(() => false)) {
      await smoothClick(page, completedSummary, 1000);
    }

    const implStepHeader = page
      .locator('button.timeline-trigger')
      .filter({ hasText: 'Implementation' })
      .first();
    if (await implStepHeader.isVisible().catch(() => false)) {
      await smoothClick(page, implStepHeader, 1800);
    }

    const runtimeStepTab = page
      .locator('.step-tabs [role="tab"]')
      .filter({ hasText: /^Runtime$/i })
      .first();
    if (await runtimeStepTab.isVisible().catch(() => false)) {
      await smoothClick(page, runtimeStepTab, 1600);
    }

    await setCaption(
      page,
      'ACT III • OPENSANDBOX & LLM TRACE',
      'Air-Gapped OpenSandbox Container & Claude 3.7 Sonnet Telemetry',
      'Inspect container stdout logs, zero-egress network policy, token usage, and guardrail hooks'
    );

    const ioPanelTab = page
      .locator('.run-side-panel [role="tab"]')
      .filter({ hasText: /^I\/O$/i })
      .first();
    if (await ioPanelTab.isVisible().catch(() => false)) {
      await smoothClick(page, ioPanelTab, 2800);
    }

    await setCaption(
      page,
      'ACT III • BILATERAL TRACEABILITY',
      '100% Verified V-Model Traceability Matrix',
      'Linking GitHub Issue #204 -> Architecture ADR -> Implementation Diff -> 4/4 JUnit Test Cases'
    );

    const traceabilityTab = page
      .locator('.run-side-panel [role="tab"]')
      .filter({ hasText: /^Traceability$/i })
      .first();
    if (await traceabilityTab.isVisible().catch(() => false)) {
      await page.evaluate(() => window.scrollTo(0, 120));
      await smoothClick(page, traceabilityTab, 3200);
    }

    const evidenceTab = page
      .locator('.run-side-panel [role="tab"]')
      .filter({ hasText: /^Evidence$/i })
      .first();
    if (await evidenceTab.isVisible().catch(() => false)) {
      await smoothClick(page, evidenceTab, 2200);
    }

    const diffBtn = page
      .locator('button')
      .filter({ hasText: /View Execution Diff/i })
      .first();
    if (await diffBtn.isVisible().catch(() => false)) {
      await smoothClick(page, diffBtn, 2400);
      const closeDiffBtn = page
        .locator('.modal-backdrop button, .modal-card button')
        .filter({ hasText: /Close|×/i })
        .first();
      if (await closeDiffBtn.isVisible().catch(() => false)) {
        await smoothClick(page, closeDiffBtn, 900);
      }
    }

    // =========================================================================
    // ACT IV (~16s): Human-in-the-Loop Approvals & Agent Interactions (/approvals)
    // =========================================================================
    const approvalsNav = page.locator('a[href="/approvals"], nav >> text=Approvals').first();
    if (await approvalsNav.isVisible().catch(() => false)) {
      await smoothClick(page, approvalsNav, 1100);
    } else {
      await page.goto('/approvals');
      await waitForAppSettled(page, 900);
    }
    await mountVideoStage(page);

    await setCaption(
      page,
      'ACT IV • HUMAN-IN-THE-LOOP',
      'Unified Governance Queue: Tool Escalations, Agent Questions & Release Gates',
      'Operators review risk scores, SLA countdowns, and rendered Markdown artifacts before sign-off'
    );
    await page.waitForTimeout(2400);

    const reviewBtn = page
      .locator('.approval-card')
      .filter({ hasText: /CHG-219|Approve Production Release Cut/i })
      .locator('button:has-text("Review")')
      .first();
    if (await reviewBtn.isVisible().catch(() => false)) {
      await smoothClick(page, reviewBtn, 3200);
    }

    const closeDrawerBtn = page
      .locator('.drawer button, [aria-label="Close"], button:has-text("Close")')
      .first();
    if (await closeDrawerBtn.isVisible().catch(() => false)) {
      await smoothClick(page, closeDrawerBtn, 900);
    }

    await page.waitForTimeout(1600);

    // =========================================================================
    // ACT V (~14s): Policy Dry-Run Simulator & Immutable Audit Trail (/policies & /audit)
    // =========================================================================
    const policiesNav = page.locator('a[href="/policies"], nav >> text=Policies').first();
    if (await policiesNav.isVisible().catch(() => false)) {
      await smoothClick(page, policiesNav, 1000);
    } else {
      await page.goto('/policies');
      await waitForAppSettled(page, 900);
    }
    await mountVideoStage(page);

    await setCaption(
      page,
      'ACT V • ZERO-TRUST GOVERNANCE',
      'Policy Dry-Run Simulator & Immutable Compliance Audit Trail',
      'Simulate tool-level risk policies before deployment and export verifiable audit records'
    );
    await page.waitForTimeout(1800);

    await page.getByLabel('Action', { exact: true }).fill('github.create_pull_request');
    await page.getByLabel('Environment', { exact: true }).fill('production');
    await page.getByLabel('Purpose', { exact: true }).fill('delivery');
    await page.getByLabel('Policy tag', { exact: true }).fill('production');

    const proposedSelect = page.getByLabel('Proposed change');
    const optionCount = await proposedSelect.locator('option').count();
    if (optionCount > 1) {
      const val = await proposedSelect.locator('option').nth(1).getAttribute('value');
      if (val) {
        await proposedSelect.selectOption(val);
      }
    }

    const simulateBtn = page.getByRole('button', { name: /Run simulation/i });
    if (await simulateBtn.isEnabled().catch(() => false)) {
      await smoothClick(page, simulateBtn, 2400);
    }

    const auditNav = page.locator('a[href="/audit"], nav >> text=Audit').first();
    if (await auditNav.isVisible().catch(() => false)) {
      await smoothClick(page, auditNav, 2200);
    } else {
      await page.goto('/audit');
      await waitForAppSettled(page, 1800);
    }
    await mountVideoStage(page);

    await setCaption(
      page,
      'AGENTWERKE • READY FOR PRODUCTION',
      'Deterministic V-Model Engineering with Sandboxed AI Agents',
      'github.com/isartor-ai/agentwerke — BPMN 2.0 • OpenSandbox • Full Bilateral Traceability'
    );
    await page.waitForTimeout(2600);
  });
});
