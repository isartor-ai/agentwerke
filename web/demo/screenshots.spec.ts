import { expect, test } from '@playwright/test';
import {
  prepareCleanScreenshotPage,
  saveRetinaScreenshot,
  waitForAppSettled,
} from './helpers/demoStage';

test.describe('Agentwerke Retina Screenshot Suite (1920x1080 @ 2x)', () => {
  test('captures all 8 product showcase screenshots', async ({ page }) => {
    await prepareCleanScreenshotPage(page);

    // =========================================================================
    // 01. Workflow Designer (Advanced BPMN Modeler + V-Model Inspector)
    // =========================================================================
    await page.goto('/workflows');
    await waitForAppSettled(page, 800);

    const vmodelWorkflowBtn = page
      .locator('button.workflow-item')
      .filter({ hasText: 'V-Model Autonomous SDLC Pipeline' })
      .first();
    await vmodelWorkflowBtn.waitFor({ state: 'visible', timeout: 10_000 });
    await vmodelWorkflowBtn.click();
    await waitForAppSettled(page, 1000);

    // Ensure the BPMN modeler SVG has rendered the V-Model nodes
    const implNode = page.locator('[data-element-id="ImplementComponents"]').first();
    if (await implNode.isVisible().catch(() => false)) {
      await implNode.click({ force: true });
      await page.waitForTimeout(450);
    }

    // Expand Agentwerke — Agent Task & Metadata sections in the properties panel if collapsed
    const agentTaskHeader = page
      .locator('.bio-properties-panel-group-header-title')
      .filter({ hasText: /Agent Task/i })
      .first();
    if (await agentTaskHeader.isVisible().catch(() => false)) {
      await agentTaskHeader.click();
      await page.waitForTimeout(250);
    }

    const validateBtn = page.getByRole('button', { name: 'Validate', exact: true });
    if (await validateBtn.isVisible().catch(() => false)) {
      await validateBtn.click();
      await waitForAppSettled(page, 600);
    }
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.waitForTimeout(200);

    await saveRetinaScreenshot(page, '01-workflow-designer.png');

    // =========================================================================
    // 02. Template Catalog & Guided Factory Configuration
    // =========================================================================
    const factoryTab = page.getByRole('tab', { name: 'Factory', exact: true });
    if (await factoryTab.isVisible().catch(() => false)) {
      await factoryTab.click();
      await waitForAppSettled(page, 600);
    }

    const configureBtn = page
      .locator('.template-card button')
      .filter({ hasText: /^Configure$/i })
      .first();
    if (await configureBtn.isVisible().catch(() => false)) {
      await configureBtn.click();
      await waitForAppSettled(page, 500);
    }

    const issueUrlInput = page.locator('label:has-text("issue_url") input').first();
    if (await issueUrlInput.isVisible().catch(() => false)) {
      await issueUrlInput.fill('https://github.com/isartor-ai/agentwerke/issues/204');
    }
    const repoInput = page.locator('label:has-text("repository") input').first();
    if (await repoInput.isVisible().catch(() => false)) {
      await repoInput.fill('isartor-ai/agentwerke');
    }

    await saveRetinaScreenshot(page, '02-template-catalog.png');

    // =========================================================================
    // 03. Run Board Overview (KPIs, Readiness Strip, Live Runs & Run Ledger)
    // =========================================================================
    await page.goto('/runs');
    await waitForAppSettled(page, 900);
    await expect(page.locator('text=V-Model Autonomous SDLC Pipeline').first()).toBeVisible();
    // Scroll cleanly to the Performance Metrics cohort card so KPIs, Status counters, Filters, and Live Runs are all framed
    const perfMetricsPanel = page.locator('.metric-panel').first();
    if (await perfMetricsPanel.isVisible().catch(() => false)) {
      await perfMetricsPanel.scrollIntoViewIfNeeded();
      await page.evaluate(() => window.scrollBy(0, -16));
    } else {
      await page.evaluate(() => window.scrollTo(0, 275));
    }
    await page.waitForTimeout(300);

    await saveRetinaScreenshot(page, '03-run-board-overview.png');

    // =========================================================================
    // 04. Run Detail — Live BPMN Overlay, Step Timeline & Gateway Decisions
    // =========================================================================
    await page.goto('/runs/run-showcase-vmodel-001');
    await waitForAppSettled(page, 1100);

    // Expand completed steps group in Execution Timeline
    const completedSummary = page.locator('summary.timeline-completed-summary').first();
    if (await completedSummary.isVisible().catch(() => false)) {
      await completedSummary.click();
      await page.waitForTimeout(350);
    }

    // Select the Implementation step to highlight it on the BPMN canvas and show Visible Reasoning
    const implStepTrigger = page
      .locator('button.timeline-trigger')
      .filter({ hasText: 'Implementation' })
      .first();
    if (await implStepTrigger.isVisible().catch(() => false)) {
      await implStepTrigger.click();
      await page.waitForTimeout(500);
    }

    // Frame both the live BPMN Canvas (with green completed overlays), Gateway Decisions, and Execution Timeline
    await page.evaluate(() => window.scrollTo(0, 140));
    await page.waitForTimeout(300);

    await saveRetinaScreenshot(page, '04-run-detail-live-bpmn.png');

    // =========================================================================
    // 05. Agent Sandbox (OpenSandbox Docker Logs) & LLM Activity Trace
    // =========================================================================
    // Open the I/O tab in the right side panel (renders SandboxExecutionDetails + ModelActivityDetails)
    const ioSideTab = page.locator('.run-side-panel [role="tab"]').filter({ hasText: /^I\/O$/i }).first();
    if (await ioSideTab.isVisible().catch(() => false)) {
      await ioSideTab.click();
      await page.waitForTimeout(500);
    }

    // Also switch the expanded step detail on the left to the Runtime tab
    const runtimeStepTab = page
      .locator('.step-tabs [role="tab"]')
      .filter({ hasText: /^Runtime$/i })
      .first();
    if (await runtimeStepTab.isVisible().catch(() => false)) {
      await runtimeStepTab.click();
      await page.waitForTimeout(400);
    }

    // Scroll so both the OpenSandbox/Model Activity side panel and Step Runtime panel are centered
    await page.evaluate(() => window.scrollTo(0, 420));
    await page.waitForTimeout(350);

    await saveRetinaScreenshot(page, '05-agent-sandbox-and-llm-trace.png');

    // =========================================================================
    // 06. V-Model Traceability Matrix (Verified JUnit + Declared 100% Matrix)
    // =========================================================================
    const activityStepTab = page
      .locator('.step-tabs [role="tab"]')
      .filter({ hasText: /^Activity$/i })
      .first();
    if (await activityStepTab.isVisible().catch(() => false)) {
      await activityStepTab.click();
      await page.waitForTimeout(350);
    }

    const traceabilityTab = page
      .locator('.run-side-panel [role="tab"]')
      .filter({ hasText: /^Traceability$/i })
      .first();
    if (await traceabilityTab.isVisible().catch(() => false)) {
      await traceabilityTab.click();
      await waitForAppSettled(page, 650);
    }

    // Scroll back to the top of the Run Detail grid so both Verified Traceability & Declared Traceability are visible
    await page.evaluate(() => window.scrollTo(0, 120));
    await page.waitForTimeout(300);

    await saveRetinaScreenshot(page, '06-vmodel-traceability-matrix.png');

    // =========================================================================
    // 07. HITL Approvals, Tool Escalations & Artifact Markdown Preview Drawer
    // =========================================================================
    await page.goto('/approvals');
    await waitForAppSettled(page, 900);

    const reviewBtn = page
      .locator('.approval-card')
      .filter({ hasText: /CHG-219|Approve Production Release Cut/i })
      .locator('button:has-text("Review")')
      .first();
    if (await reviewBtn.isVisible().catch(() => false)) {
      await reviewBtn.click();
      await waitForAppSettled(page, 800);
    }
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.waitForTimeout(250);

    await saveRetinaScreenshot(page, '07-hitl-approvals-and-interactions.png');

    // =========================================================================
    // 08. Policy Simulation Engine & Governance Rules
    // =========================================================================
    await page.goto('/policies');
    await waitForAppSettled(page, 800);

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
      await simulateBtn.click();
      await waitForAppSettled(page, 700);
    }

    await saveRetinaScreenshot(page, '08-policies-and-audit.png');
  });
});
