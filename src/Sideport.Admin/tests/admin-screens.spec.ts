import { expect, test } from '@playwright/test'

const routes = [
  { name: 'Home', button: 'Home', heading: 'Apps and iPhones at a glance' },
  { name: 'Apps', button: 'Apps', heading: 'Apps' },
  { name: 'Devices', button: 'Devices', heading: /Devices|No devices known yet/ },
  { name: 'People', button: 'People', heading: 'People' },
  { name: 'Activity', button: 'Activity', heading: 'Activity' },
  { name: 'Settings', button: 'Settings', heading: 'Control plane status and session access' },
]

test.describe('Sideport admin runtime UI', () => {
  for (const route of routes) {
    test(`${route.name} renders and screenshots`, async ({ page }, testInfo) => {
      await page.goto('/')
      await page.locator('.nav-list').getByRole('button', { name: route.button, exact: true }).click({ force: true })
      await expect(page.getByRole('heading', { name: route.heading, exact: typeof route.heading === 'string' })).toBeVisible()
      await page.screenshot({ path: `test-results/${testInfo.project.name}-${route.name}.png`, fullPage: true })
    })
  }

  test('mutating actions stay disabled without live mutation config', async ({ page }) => {
    await page.goto('/')
    await page.locator('.nav-list').getByRole('button', { name: 'Apps', exact: true }).click({ force: true })
    await expect(page.getByRole('button', { name: 'Add app', exact: true })).toHaveCount(0)
    await page.locator('.nav-list').getByRole('button', { name: 'Devices', exact: true }).click({ force: true })
    await expect(page.getByText(/No known devices returned|Device inventory/)).toBeVisible()
  })
})
