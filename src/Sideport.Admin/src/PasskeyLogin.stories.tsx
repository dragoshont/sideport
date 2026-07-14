import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, within } from 'storybook/test'
import { PasskeyLogin } from './PasskeyLogin'

const meta = {
  title: 'Sideport/Passkey Login',
  component: PasskeyLogin,
  parameters: { layout: 'fullscreen' },
} satisfies Meta<typeof PasskeyLogin>

export default meta
type Story = StoryObj<typeof meta>

export const Ready: Story = {
  beforeEach: () => {
    const originalFetch = window.fetch
    window.fetch = (async () => Response.json({ mode: 'passkey', nativePasskeyEnabled: true })) as typeof window.fetch
    return () => { window.fetch = originalFetch }
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.findByRole('heading', { name: 'Sign in to Sideport' })).resolves.toBeVisible()
    await expect(canvas.findByRole('button', { name: 'Sign in with a passkey' })).resolves.toBeVisible()
    await expect(canvas.getByText(/There is no Sideport password/)).toBeVisible()
  },
}
