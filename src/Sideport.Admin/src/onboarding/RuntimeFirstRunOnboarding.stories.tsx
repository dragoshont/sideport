import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, fn, waitFor, within } from 'storybook/test'
import type { AdminDataStatus } from '../api/sideportApi'
import { fixtures } from '../data/sideportFixtures'
import { runtimeEmptyData, type OnboardingWorkflowStep, type SideportReadModel } from '../data/sideportTypes'
import { RuntimeFirstRunOnboarding } from './RuntimeFirstRunOnboarding'

const checkedAt = '2026-07-14T01:30:00Z'

function workflowStep(id: OnboardingWorkflowStep['id'], state: OnboardingWorkflowStep['state'], reason: string): OnboardingWorkflowStep {
  return { id, state, required: true, source: 'live', reason, evidence: [] }
}

const blockedStatus: AdminDataStatus = {
  mode: 'live',
  baseUrl: 'storybook://auto-onboarding',
  message: 'Live Sideport setup.',
  canMutate: true,
  onboarding: {
    firstRunComplete: false,
    schedulerEnabled: false,
    steps: [],
    setupState: 'in-progress',
    completionReceipt: null,
    workflow: {
      schemaVersion: 2,
      setupState: 'in-progress',
      readyNow: false,
      nextAction: { stepId: 'server', action: 'retry-checks', label: 'Check again' },
      steps: [
        { ...workflowStep('server', 'blocked', 'Sideport cannot save setup state.'), nextAction: { action: 'retry-checks', label: 'Check again' } },
        workflowStep('apple-signer', 'not-started', 'Waiting for Sideport.'),
        workflowStep('device', 'not-started', 'Waiting for Apple setup.'),
        workflowStep('app', 'not-started', 'Waiting for an iPhone.'),
        workflowStep('install', 'not-started', 'Waiting for an app.'),
        workflowStep('ready', 'not-started', 'Waiting for a verified install.'),
      ],
    },
  },
}

const advancedStatus: AdminDataStatus = {
  ...blockedStatus,
  onboarding: {
    ...blockedStatus.onboarding!,
    workflow: {
      ...blockedStatus.onboarding!.workflow!,
      nextAction: { stepId: 'apple-signer', action: 'connect-apple', label: 'Connect Apple account' },
      steps: [
        workflowStep('server', 'complete', 'Sideport core services are ready.'),
        { ...workflowStep('apple-signer', 'action-required', 'Connect the Apple account used to sign apps.'), nextAction: { action: 'connect-apple', label: 'Connect Apple account' } },
        workflowStep('device', 'not-started', 'Waiting for Apple setup.'),
        workflowStep('app', 'not-started', 'Waiting for an iPhone.'),
        workflowStep('install', 'not-started', 'Waiting for an app.'),
        workflowStep('ready', 'not-started', 'Waiting for a verified install.'),
      ],
    },
  },
}

const data: SideportReadModel = {
  ...runtimeEmptyData,
  system: {
    ...fixtures.system,
    operational: false,
    checks: [{ id: 'state-writable', status: 'fail', source: 'live', checkedAt, scope: 'storage', affectedResources: ['sideport-state'], reason: 'Sideport cannot save setup state.', nextAction: 'Repair the Sideport state volume.' }],
  },
  workspace: fixtures.workspace,
  personalApple: {
    ...runtimeEmptyData.personalApple,
    state: 'not-configured',
    message: 'No Apple account is connected yet.',
    credentialEntry: { supported: true, allowedNow: true, blockedReason: null },
  },
}

const noop = fn()

function AutoAdvanceHarness() {
  const [status, setStatus] = useState(blockedStatus)
  return <RuntimeFirstRunOnboarding apiStatus={status} canAddApp canAddIPhone canCompleteOnboarding canRunInstall data={data} onAddApp={noop} onAddIPhone={noop} onInstallApp={noop} onOpenDevice={noop} onPrepareInstall={noop} onReconcileInstall={noop} onRefresh={() => setStatus(advancedStatus)} onRetryFinalization={noop} />
}

const meta = {
  title: 'Sideport/Runtime First Run Onboarding',
  component: RuntimeFirstRunOnboarding,
  args: {
    data,
    apiStatus: blockedStatus,
    onAddApp: noop,
    onAddIPhone: noop,
    onInstallApp: noop,
    onOpenDevice: noop,
    onPrepareInstall: noop,
    onReconcileInstall: noop,
    onRefresh: noop,
    onRetryFinalization: noop,
  },
} satisfies Meta<typeof RuntimeFirstRunOnboarding>

export default meta
type Story = StoryObj<typeof meta>

export const PollsAndAdvancesFromServerEvidence: Story = {
  name: 'Polls and advances from server evidence',
  render: () => <AutoAdvanceHarness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByRole('heading', { name: 'Check Sideport' })).toBeVisible()
    await waitFor(() => expect(canvas.getByRole('heading', { name: 'Connect Apple' })).toBeVisible(), { timeout: 7_000 })
    await expect(canvas.getByTestId('runtime-onboarding-live-region')).toHaveTextContent('Connect Apple. Action required.')
  },
}
