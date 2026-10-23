import { Locator, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { fileURLToPath } from 'url';

const CURRENT_DIR = path.dirname(fileURLToPath(import.meta.url));
export const SCREENSHOT_DIR = path.resolve(CURRENT_DIR, '../../../docs/assets/screenshots');

export function ensureScreenshotDir(): void {
  fs.mkdirSync(SCREENSHOT_DIR, { recursive: true });
}

export async function seedShowcaseArtifacts(page: Page): Promise<void> {
  const apiBase = process.env.DEMO_API_URL || 'http://localhost:8081';
  const checklistMd = [
    '# V-Model Production Release Readiness Checklist',
    '',
    '- **Workflow**: `V-Model Autonomous SDLC Pipeline (v2.4.0)`',
    '- **Change Request**: `CHG-219` — Idempotent Payment Ledger Settlement',
    '- **Traceability Coverage**: **100%** (7/7 V-Model obligations verified)',
    '',
    '## Verification Gates',
    '',
    '| V-Model Stage | Artifact | Status | Evidence |',
    '| :--- | :--- | :--- | :--- |',
    '| Requirements | `requirements-spec.md` | ✅ Verified | `REQ-219-1..4` |',
    '| Architecture | `architecture-adr-004.md` | ✅ Verified | `ADR-004-A, ADR-004-B` |',
    '| Implementation | `implementation-diff.patch` | ✅ Verified | OpenSandbox `--network=none` |',
    '| Unit Verification | `junit-results.xml` | ✅ 4/4 Passed | `0 failed, 0 skipped (3.84s)` |',
    '| Integration Test | `integration-test-report.json` | ✅ 18/18 Passed | Ephemeral PostgreSQL 16 |',
    '',
    '## Security & Policy Attestation',
    '- **Secret Scanner**: `0` findings across `148` added lines',
    '- **Sandbox Egress**: `0` outbound packets (`netns-sbx-7f9a2c`)',
  ].join('\n');

  const adrMd = [
    '# ADR-004: Family-Chained Refresh Token Lineage & PKCE S256',
    '',
    '- **Status**: Accepted',
    '- **Traces To**: `REQ-204-1`, `REQ-204-2`, `REQ-204-3`, `REQ-204-4`',
    '',
    '## Decision',
    '1. Enforce `CryptographicOperations.FixedTimeEquals` on SHA-256 PKCE `code_verifier`.',
    '2. Track refresh token lineage by `family_id` and revoke entire family on replay detection.',
  ].join('\n');

  const uploads: Array<{ runId: string; name: string; content: string; contentType: string }> = [
    {
      runId: 'run-showcase-approval-002',
      name: 'release-readiness-checklist.md',
      content: checklistMd,
      contentType: 'text/markdown',
    },
    {
      runId: 'run-showcase-approval-002',
      name: 'architecture-adr-004.md',
      content: adrMd,
      contentType: 'text/markdown',
    },
    {
      runId: 'run-showcase-vmodel-001',
      name: 'release-readiness-checklist.md',
      content: checklistMd,
      contentType: 'text/markdown',
    },
    {
      runId: 'run-showcase-vmodel-001',
      name: 'requirements-spec.md',
      content: adrMd,
      contentType: 'text/markdown',
    },
  ];

  for (const item of uploads) {
    await page.request
      .post(`${apiBase}/api/runs/${item.runId}/artifacts/${item.name}`, {
        data: item.content,
        headers: { 'Content-Type': item.contentType },
      })
      .catch(() => {});
  }
}

export async function prepareCleanScreenshotPage(page: Page): Promise<void> {
  await seedShowcaseArtifacts(page);

  // Strip any stale build-time Bearer token so the API's DevAuthenticationHandler
  // (dev:admin with Admin role) authenticates every request in the local showcase stack.
  await page.route('**/api/**', async (route) => {
    const headers = { ...route.request().headers() };
    delete headers['authorization'];
    await route.continue({ headers });
  });

  await page.addInitScript(() => {
    try {
      window.localStorage.setItem('agentwerke-theme', 'dark');
    } catch {
      // Ignore storage access errors before navigation
    }
  });

  await page.addStyleTag({
    content: `
      *, *::before, *::after {
        caret-color: transparent !important;
      }
      ::-webkit-scrollbar {
        width: 6px;
        height: 6px;
      }
    `,
  }).catch(() => {});
}

export async function waitForAppSettled(page: Page, extraMs = 450): Promise<void> {
  await page.waitForLoadState('domcontentloaded');
  await page
    .waitForFunction(() => document.querySelectorAll('.skeleton, .loading-spinner').length === 0, {
      timeout: 8_000,
    })
    .catch(() => {});
  await page.waitForTimeout(extraMs);
}

export async function saveRetinaScreenshot(page: Page, filename: string): Promise<string> {
  ensureScreenshotDir();
  const outPath = path.join(SCREENSHOT_DIR, filename);
  await page.screenshot({
    path: outPath,
    fullPage: false,
    type: 'png',
    animations: 'disabled',
  });
  return outPath;
}

export async function mountVideoStage(page: Page): Promise<void> {
  await page.evaluate(() => {
    if (document.getElementById('aw-demo-stage-style')) return;

    const style = document.createElement('style');
    style.id = 'aw-demo-stage-style';
    style.textContent = `
      #aw-demo-cursor {
        position: fixed;
        top: 540px;
        left: 960px;
        width: 22px;
        height: 22px;
        margin-left: -11px;
        margin-top: -11px;
        border-radius: 9999px;
        background: rgba(56, 189, 248, 0.92);
        border: 2.5px solid #ffffff;
        box-shadow: 0 0 0 6px rgba(56, 189, 248, 0.22), 0 8px 20px rgba(0, 0, 0, 0.45);
        pointer-events: none;
        z-index: 2147483646;
        transition: left 420ms cubic-bezier(0.22, 1, 0.36, 1),
                    top 420ms cubic-bezier(0.22, 1, 0.36, 1),
                    transform 160ms ease,
                    background 160ms ease;
      }
      #aw-demo-cursor.aw-cursor-click {
        transform: scale(0.78);
        background: rgba(16, 185, 129, 0.98);
        box-shadow: 0 0 0 14px rgba(16, 185, 129, 0.28), 0 8px 24px rgba(0, 0, 0, 0.5);
      }
      #aw-demo-caption-bar {
        position: fixed;
        left: 50%;
        bottom: 28px;
        transform: translateX(-50%);
        min-width: 760px;
        max-width: 1180px;
        padding: 14px 24px;
        border-radius: 14px;
        background: rgba(11, 15, 25, 0.90);
        border: 1px solid rgba(56, 189, 248, 0.38);
        box-shadow: 0 18px 42px rgba(0, 0, 0, 0.62), 0 0 28px rgba(56, 189, 248, 0.14);
        backdrop-filter: blur(14px);
        color: #f8fafc;
        font-family: Inter, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
        pointer-events: none;
        z-index: 2147483645;
        display: flex;
        align-items: center;
        gap: 16px;
        opacity: 0;
        transition: opacity 280ms ease, transform 280ms ease;
      }
      #aw-demo-caption-bar.aw-visible {
        opacity: 1;
      }
      #aw-demo-caption-act {
        padding: 5px 11px;
        border-radius: 999px;
        background: linear-gradient(135deg, rgba(14, 165, 233, 0.28), rgba(99, 102, 241, 0.28));
        border: 1px solid rgba(56, 189, 248, 0.55);
        color: #7dd3fc;
        font-size: 11px;
        font-weight: 700;
        letter-spacing: 0.08em;
        text-transform: uppercase;
        white-space: nowrap;
      }
      #aw-demo-caption-text {
        display: flex;
        flex-direction: column;
        gap: 3px;
      }
      #aw-demo-caption-title {
        font-size: 15.5px;
        font-weight: 700;
        color: #f8fafc;
        letter-spacing: -0.01em;
      }
      #aw-demo-caption-sub {
        font-size: 13px;
        font-weight: 450;
        color: #cbd5e1;
      }
    `;
    document.head.appendChild(style);

    const cursor = document.createElement('div');
    cursor.id = 'aw-demo-cursor';
    document.body.appendChild(cursor);

    const bar = document.createElement('div');
    bar.id = 'aw-demo-caption-bar';
    bar.innerHTML = `
      <span id="aw-demo-caption-act">ACT I</span>
      <div id="aw-demo-caption-text">
        <div id="aw-demo-caption-title">Agentwerke</div>
        <div id="aw-demo-caption-sub">Enterprise Control Plane for Autonomous V-Model Engineering</div>
      </div>
    `;
    document.body.appendChild(bar);
  });
}

export async function setCaption(
  page: Page,
  act: string,
  title: string,
  subtitle: string
): Promise<void> {
  await mountVideoStage(page);
  await page.evaluate(
    ({ actText, titleText, subText }) => {
      const bar = document.getElementById('aw-demo-caption-bar');
      const actEl = document.getElementById('aw-demo-caption-act');
      const titleEl = document.getElementById('aw-demo-caption-title');
      const subEl = document.getElementById('aw-demo-caption-sub');
      if (actEl) actEl.textContent = actText;
      if (titleEl) titleEl.textContent = titleText;
      if (subEl) subEl.textContent = subText;
      if (bar) bar.classList.add('aw-visible');
    },
    { actText: act, titleText: title, subText: subtitle }
  );
}

export async function moveCursorTo(page: Page, x: number, y: number, waitMs = 440): Promise<void> {
  await mountVideoStage(page);
  await page.evaluate(
    ({ targetX, targetY }) => {
      const cursor = document.getElementById('aw-demo-cursor');
      if (cursor) {
        cursor.style.left = `${Math.round(targetX)}px`;
        cursor.style.top = `${Math.round(targetY)}px`;
      }
    },
    { targetX: x, targetY: y }
  );
  await page.mouse.move(x, y, { steps: 12 });
  await page.waitForTimeout(waitMs);
}

export async function smoothHover(
  page: Page,
  locator: Locator,
  pauseAfterMs = 900
): Promise<void> {
  await locator.first().waitFor({ state: 'visible', timeout: 10_000 });
  const box = await locator.first().boundingBox();
  if (box) {
    await moveCursorTo(page, box.x + box.width / 2, box.y + box.height / 2);
  }
  await locator.first().hover({ force: true }).catch(() => {});
  await page.waitForTimeout(pauseAfterMs);
}

export async function smoothClick(
  page: Page,
  locator: Locator,
  pauseAfterMs = 1100
): Promise<void> {
  await locator.first().waitFor({ state: 'visible', timeout: 10_000 });
  const box = await locator.first().boundingBox();
  if (box) {
    await moveCursorTo(page, box.x + box.width / 2, box.y + box.height / 2);
  }
  await page.evaluate(() => {
    const cursor = document.getElementById('aw-demo-cursor');
    if (cursor) {
      cursor.classList.add('aw-cursor-click');
      window.setTimeout(() => cursor.classList.remove('aw-cursor-click'), 220);
    }
  });
  await locator.first().click({ force: true });
  await page.waitForTimeout(pauseAfterMs);
}
