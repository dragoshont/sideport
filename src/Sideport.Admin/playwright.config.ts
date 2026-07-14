import { defineConfig, devices } from '@playwright/test'

const port = Number(process.env.PLAYWRIGHT_PORT ?? 4177)
const baseURL = `http://127.0.0.1:${port}`

export default defineConfig({
  testDir: './tests',
  timeout: 30_000,
  expect: { timeout: 5_000 },
  outputDir: './test-results',
  webServer: {
    command: `npm run dev -- --host 127.0.0.1 --port ${port}`,
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'], baseURL, viewport: { width: 1440, height: 1100 } } },
    { name: 'mobile', use: { ...devices['Desktop Chrome'], baseURL, isMobile: true, hasTouch: true, viewport: { width: 390, height: 900 } } },
  ],
})
