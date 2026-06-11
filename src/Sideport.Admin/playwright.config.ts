import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './tests',
  timeout: 30_000,
  expect: { timeout: 5_000 },
  outputDir: './test-results',
  webServer: {
    command: 'npm run dev -- --host 127.0.0.1 --port 4177',
    url: 'http://127.0.0.1:4177',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'], baseURL: 'http://127.0.0.1:4177', viewport: { width: 1440, height: 1100 } } },
    { name: 'mobile', use: { ...devices['Desktop Chrome'], baseURL: 'http://127.0.0.1:4177', isMobile: true, hasTouch: true, viewport: { width: 390, height: 900 } } },
  ],
})
