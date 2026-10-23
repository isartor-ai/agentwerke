import { defineConfig } from '@playwright/test';

const baseURL = process.env.DEMO_BASE_URL ?? 'http://localhost:3002';

export default defineConfig({
  testDir: './demo',
  timeout: 180_000,
  expect: {
    timeout: 15_000,
  },
  fullyParallel: false,
  workers: 1,
  reporter: [['list']],
  outputDir: './test-results/demo-artifacts',
  use: {
    baseURL,
    browserName: 'chromium',
    headless: true,
    colorScheme: 'dark',
    locale: 'en-US',
    timezoneId: 'Europe/Berlin',
    ignoreHTTPSErrors: true,
  },
  projects: [
    {
      name: 'screenshots',
      testMatch: /screenshots\.spec\.ts/,
      use: {
        viewport: { width: 1920, height: 1080 },
        deviceScaleFactor: 2,
        video: 'off',
      },
    },
    {
      name: 'video',
      testMatch: /video-tour\.spec\.ts/,
      use: {
        viewport: { width: 1920, height: 1080 },
        deviceScaleFactor: 1,
        video: {
          mode: 'on',
          size: { width: 1920, height: 1080 },
        },
      },
    },
  ],
});
