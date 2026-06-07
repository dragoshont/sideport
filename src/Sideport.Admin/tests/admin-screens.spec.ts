import { expect, test } from '@playwright/test'

const routes = [
  { name: 'Overview', button: 'Overview', heading: 'Sideport health at a glance' },
  { name: 'Devices', button: 'Devices', heading: 'Device inventory' },
  { name: 'DeviceDetail', button: 'Devices', heading: 'Device inventory', followUp: 'Open', finalHeading: 'Dragos iPhone' },
  { name: 'AddApp', button: 'Add App', heading: 'Preflight before Sideport touches Apple services' },
  { name: 'Renewals', button: 'Renewals', heading: 'Renewal risk, not fake queue control' },
  { name: 'Diagnostics', button: 'Diagnostics', heading: 'OpenTelemetry-first failure evidence' },
  { name: 'Settings', button: 'Settings', heading: 'Read-only control plane status' },
]

test.describe('Sideport admin mock UI', () => {
  for (const route of routes) {
    test(`${route.name} renders and screenshots`, async ({ page }, testInfo) => {
      await page.goto('/')
      await page.locator('.nav-list').getByRole('button', { name: route.button, exact: true }).click({ force: true })
      await expect(page.getByRole('heading', { name: route.heading })).toBeVisible()
      if (route.followUp && route.finalHeading) {
        await page.getByRole('button', { name: route.followUp }).first().click()
        await expect(page.getByRole('heading', { name: route.finalHeading })).toBeVisible()
      }
      await page.screenshot({ path: `test-results/${testInfo.project.name}-${route.name}.png`, fullPage: true })
    })
  }

  test('mutating actions stay disabled in prototype', async ({ page }) => {
    await page.goto('/')
    await page.locator('.nav-list').getByRole('button', { name: 'Add App', exact: true }).click({ force: true })
    await expect(page.getByRole('button', { name: /Register app is disabled/ })).toBeDisabled()
    await page.locator('.nav-list').getByRole('button', { name: 'Devices', exact: true }).click({ force: true })
    await page.getByRole('button', { name: 'Open' }).first().click()
    await expect(page.getByRole('button', { name: /Mock refresh only/ })).toBeDisabled()
  })
})
