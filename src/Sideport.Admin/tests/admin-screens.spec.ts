import { expect, test } from '@playwright/test'

const routes = [
  { name: 'Onboarding', button: 'Onboarding', heading: 'Bring Sideport online in the right order' },
  { name: 'Overview', button: 'Overview', heading: 'Sideport health at a glance' },
  { name: 'Devices', button: 'Devices', heading: /Device inventory|No devices known yet/ },
  { name: 'AppCatalog', button: 'App Catalog', heading: 'Reusable apps, separate from phone slots' },
  { name: 'Renewals', button: 'Renewals', heading: 'Renewal risk' },
  { name: 'AppleAccess', button: 'Apple Access', heading: 'Connect Apple data without over-trusting it' },
  { name: 'Diagnostics', button: 'Diagnostics', heading: 'Runtime failure evidence' },
  { name: 'Settings', button: 'Settings', heading: 'Control plane status and session access' },
]

test.describe('Sideport admin runtime UI', () => {
  for (const route of routes) {
    test(`${route.name} renders and screenshots`, async ({ page }, testInfo) => {
      await page.goto('/')
      await page.locator('.nav-list').getByRole('button', { name: route.button, exact: true }).click({ force: true })
      await expect(page.getByRole('heading', { name: route.heading })).toBeVisible()
      await page.screenshot({ path: `test-results/${testInfo.project.name}-${route.name}.png`, fullPage: true })
    })
  }

  test('mutating actions stay disabled without live mutation config', async ({ page }) => {
    await page.goto('/')
    await page.locator('.nav-list').getByRole('button', { name: 'App Catalog', exact: true }).click({ force: true })
    await expect(page.getByRole('button', { name: /Inspect IPA|Register on phone/ }).first()).toBeDisabled()
    await page.locator('.nav-list').getByRole('button', { name: 'Devices', exact: true }).click({ force: true })
    await expect(page.getByText(/No devices returned by \/api\/devices|Device inventory/)).toBeVisible()
  })
})
