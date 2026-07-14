import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, fn, userEvent, waitFor, within } from 'storybook/test'
import { WorkspaceHandoff } from './WorkspaceHandoff'

const creationOptions = JSON.stringify({
  challenge: 'AQIDBA',
  rp: { id: 'localhost', name: 'Sideport' },
  user: { id: 'BQYHCA', name: 'sideport-fixture', displayName: 'Home Owner' },
  pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
  authenticatorSelection: { residentKey: 'required', userVerification: 'required' },
})

function pathOf(input: RequestInfo | URL): string {
  return typeof input === 'string' ? input : input instanceof URL ? input.pathname : new URL(input.url).pathname
}

function installOidcMock(enrollmentEnabled: boolean, handoffAvailable = true): () => void {
  const originalFetch = window.fetch
  window.fetch = (async (input: RequestInfo | URL) => {
    const path = pathOf(input)
    if (path === '/api/authentication/options') {
      return Response.json({
        mode: 'oidc',
        oidcEnabled: true,
        nativePasskeyEnabled: false,
        provider: 'company-sso',
        providerLabel: 'Company account',
        loginLabel: 'Continue with Company SSO',
        enrollmentLabel: 'Create passkey',
        preferredMethod: 'passkey',
        enrollmentEnabled,
      })
    }
    if (path === '/api/me') return Response.json({ authenticated: false, via: 'none' }, { status: 401 })
    if (path.endsWith('/handoff/session'))
      return handoffAvailable
        ? Response.json({ available: true })
        : Response.json({ error: 'owner-claim-unavailable' }, { status: 404 })
    if (path.endsWith('/enrollment')) {
      return Response.json({ available: true, enrollmentUrl: 'https://identity.example/enroll/fixture' })
    }
    return Response.json({ error: 'unexpected-request', message: `Unexpected Storybook request: ${path}` }, { status: 500 })
  }) as typeof window.fetch
  return () => { window.fetch = originalFetch }
}

function installNativeMock(
  kind: 'owner-claim' | 'invitation',
  ownerState: 'available' | 'private-link-required' | 'claimed' = 'available',
  handoffAvailable = true,
): () => void {
  const originalFetch = window.fetch
  const originalCredentials = Object.getOwnPropertyDescriptor(navigator, 'credentials')
  Object.defineProperty(navigator, 'credentials', {
    configurable: true,
    value: {
      create: fn(async () => ({
        toJSON: () => ({
          id: 'fixture-passkey',
          rawId: 'AQIDBA',
          type: 'public-key',
          response: { clientDataJSON: 'AQID', attestationObject: 'BAUG' },
        }),
      })),
      get: fn(),
    },
  })
  window.fetch = (async (input: RequestInfo | URL) => {
    const path = pathOf(input)
    if (path === '/api/authentication/options') {
      return Response.json({
        mode: 'passkey',
        oidcEnabled: false,
        nativePasskeyEnabled: true,
        providerLabel: 'Sideport passkey',
        enrollmentLabel: 'Create passkey',
        enrollmentEnabled: true,
      })
    }
    if (path === '/api/me') return Response.json({ authenticated: false, via: 'none' }, { status: 401 })
    if (path === '/api/workspace/owner-claims/native-passkey/status')
      return Response.json({ mode: 'passkey', state: ownerState })
    if (path.endsWith('/handoff/session'))
      return !handoffAvailable || ownerState !== 'available' && kind === 'owner-claim'
        ? Response.json({ error: 'owner-claim-unavailable' }, { status: 404 })
        : Response.json({ available: true })
    if (path === `/api/workspace/${kind === 'owner-claim' ? 'owner-claims' : 'invitations'}/native-passkey/options`)
      return Response.json({ mode: 'passkey', creationOptions })
    if (path.endsWith('/native-passkey/complete'))
      return Response.json({ signedIn: true, method: 'passkey', acceptance: { replayed: false } })
    return Response.json({ error: 'unexpected-request', message: `Unexpected Storybook request: ${path}` }, { status: 500 })
  }) as typeof window.fetch
  return () => {
    window.fetch = originalFetch
    if (originalCredentials) Object.defineProperty(navigator, 'credentials', originalCredentials)
    else Reflect.deleteProperty(navigator, 'credentials')
  }
}

function installDirectOwnerSetupMock(): () => void {
  const originalFetch = window.fetch
  window.fetch = (async (input: RequestInfo | URL) => {
    const path = pathOf(input)
    if (path === '/api/authentication/options')
      return Response.json({ mode: 'passkey', nativePasskeyEnabled: true, enrollmentEnabled: true })
    if (path === '/api/me')
      return Response.json({ authenticated: false, via: 'none' }, { status: 401 })
    if (path === '/api/workspace/owner-claims/native-passkey/status')
      return Response.json({ mode: 'passkey', state: 'available' })
    return Response.json({ error: 'unexpected-request', message: `Unexpected Storybook request: ${path}` }, { status: 500 })
  }) as typeof window.fetch
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

export const NativeOwnerReady: Story = {
  args: { kind: 'owner-claim' },
  beforeEach: () => installNativeMock('owner-claim'),
}

export const NativeInvitationReady: Story = {
  beforeEach: () => installNativeMock('invitation'),
}

export const NativeInvitationRequiresPrivateLink: Story = {
  beforeEach: () => installNativeMock('invitation', 'available', false),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.findByRole('alert')).resolves.toHaveTextContent('Open the complete private invitation link')
    await expect(canvas.queryByRole('button', { name: 'Create passkey' })).not.toBeInTheDocument()
  },
}

export const NativeOwnerDirectVisitStartsSetup: Story = {
  args: { kind: 'owner-claim' },
  beforeEach: () => installDirectOwnerSetupMock(),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.findByText('First setup')).resolves.toBeVisible()
    await expect(canvas.findByRole('textbox', { name: 'Name' })).resolves.toBeVisible()
    await expect(canvas.getByRole('textbox', { name: 'Email' })).toBeVisible()
    await expect(canvas.getByRole('button', { name: 'Create passkey' })).toBeDisabled()
    await expect(canvas.queryByText(/setup link|startup logs/i)).not.toBeInTheDocument()
  },
}

export const NativeOwnerAlreadyClaimed: Story = {
  args: { kind: 'owner-claim' },
  beforeEach: () => installNativeMock('owner-claim', 'claimed'),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.findByText('Sideport is already set up')).resolves.toBeVisible()
    await expect(canvas.getByRole('button', { name: 'Sign in' })).toBeVisible()
    await expect(canvas.queryByRole('textbox', { name: 'Name' })).not.toBeInTheDocument()
    await expect(canvas.queryByRole('textbox', { name: 'Email' })).not.toBeInTheDocument()
  },
}

export const NativeOwnerRecoveryRequiresPrivateLink: Story = {
  args: { kind: 'owner-claim' },
  beforeEach: () => installNativeMock('owner-claim', 'private-link-required'),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.findByRole('alert')).resolves.toHaveTextContent('Open the complete private Owner recovery link')
    await expect(canvas.queryByRole('textbox', { name: 'Name' })).not.toBeInTheDocument()
    await expect(canvas.queryByRole('button', { name: 'Create passkey' })).not.toBeInTheDocument()
  },
}

export const NativeOwnerCreatesPasskey: Story = {
  args: { kind: 'owner-claim' },
  beforeEach: () => installNativeMock('owner-claim'),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const create = await canvas.findByRole('button', { name: 'Create passkey' })
    await expect(create).toBeDisabled()
    await userEvent.type(canvas.getByRole('textbox', { name: 'Name' }), 'Home Owner')
    await userEvent.type(canvas.getByRole('textbox', { name: 'Email' }), 'owner@example.test')
    await expect(create).toBeEnabled()
    await userEvent.click(create)
    await expect(canvas.findByText('Access saved')).resolves.toBeVisible()
    await expect(canvas.queryByText(/Authentik|Company account/)).not.toBeInTheDocument()
  },
}

export const NativeInvitationCreatesPasskey: Story = {
  beforeEach: () => installNativeMock('invitation'),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await userEvent.click(await canvas.findByRole('button', { name: 'Create passkey' }))
    await expect(canvas.findByText('Access saved')).resolves.toBeVisible()
    await expect(canvas.queryByRole('textbox')).not.toBeInTheDocument()
  },
}

export const OidcProviderEnrollmentAndExistingAccount: Story = {
  beforeEach: () => installOidcMock(true),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await waitFor(() => expect(canvas.getByRole('button', { name: 'Create passkey' })).toBeVisible())
    await expect(canvas.getByRole('button', { name: 'Continue with Company SSO' })).toBeVisible()
    await expect(canvas.queryByRole('textbox', { name: 'Name' })).not.toBeInTheDocument()
  },
}

export const OidcExistingAccountOnly: Story = {
  beforeEach: () => installOidcMock(false),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await waitFor(() => expect(canvas.getByRole('button', { name: 'Continue with Company SSO' })).toBeVisible())
    await expect(canvas.queryByRole('button', { name: 'Create passkey' })).not.toBeInTheDocument()
  },
}

export const OidcOwnerStillRequiresPrivateLink: Story = {
  args: { kind: 'owner-claim' },
  beforeEach: () => installOidcMock(true, false),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.findByRole('alert')).resolves.toHaveTextContent('Open the complete private Owner recovery link')
    await expect(canvas.queryByRole('button', { name: 'Create passkey' })).not.toBeInTheDocument()
    await expect(canvas.queryByRole('button', { name: 'Continue with Company SSO' })).not.toBeInTheDocument()
  },
}
