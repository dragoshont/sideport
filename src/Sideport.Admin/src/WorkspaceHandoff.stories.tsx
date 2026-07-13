import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, waitFor, within } from 'storybook/test'
import { WorkspaceHandoff } from './WorkspaceHandoff'

function mockInvitationFetch(enrollmentEnabled: boolean): typeof window.fetch {
  return (async (input: RequestInfo | URL) => {
    const path = typeof input === 'string' ? input : input instanceof URL ? input.pathname : new URL(input.url).pathname
    if (path === '/api/authentication/options') {
      return Response.json({
        provider: 'authentik',
        providerLabel: 'Microsoft via Authentik',
        loginLabel: 'Continue with Microsoft',
        enrollmentEnabled,
      })
    }
    if (path === '/api/me')
      return Response.json({ authenticated: false, via: 'none' })
    if (path === '/api/workspace/invitations/enrollment') {
      return Response.json({
        available: true,
        enrollmentUrl: 'https://auth.example/if/flow/sideport-enrollment/?itoken=fixture',
      })
    }
    return Response.json({ error: 'unexpected-request', message: `Unexpected Storybook request: ${path}` }, { status: 500 })
  }) as typeof window.fetch
}

function installMock(enrollmentEnabled: boolean): () => void {
  const originalFetch = window.fetch
  window.fetch = mockInvitationFetch(enrollmentEnabled)
  return () => { window.fetch = originalFetch }
}

const meta = {
  title: 'Sideport/Workspace Handoff',
  component: WorkspaceHandoff,
  args: { kind: 'invitation' },
  parameters: { layout: 'fullscreen' },
} satisfies Meta<typeof WorkspaceHandoff>

export default meta
type Story = StoryObj<typeof meta>

export const PasskeyFirstWithExistingAccountFallback: Story = {
  beforeEach: () => installMock(true),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await waitFor(() => expect(canvas.getByRole('button', { name: 'Create passkey' })).toBeVisible())
    await expect(canvas.getByRole('button', { name: 'Continue with Microsoft' })).toBeVisible()
    await expect(canvas.getByText(/Microsoft via Authentik/)).toBeVisible()
  },
}

export const ExistingAccountOnlyWhenEnrollmentIsUnavailable: Story = {
  beforeEach: () => installMock(false),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await waitFor(() => expect(canvas.getByRole('button', { name: 'Continue with Microsoft' })).toBeVisible())
    await expect(canvas.queryByRole('button', { name: 'Create passkey' })).not.toBeInTheDocument()
  },
}
