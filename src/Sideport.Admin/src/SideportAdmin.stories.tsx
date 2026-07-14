import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, fn, userEvent, waitFor, within } from 'storybook/test'
import { refreshPersonalAppleRequestSecurity, selectPersonalAppleTeam, type AdminDataStatus, type AppRegistrationDto, type InstallOperationPayload, type InstallPreflightPayload, type OnboardingCompletionPayload, type OperationPreflightDto, type OperationRecordDto, type PendingAppRegistrationPayload, type SchedulerStatusDto } from './api/sideportApi'
import {
  SideportAdminApp,
  type RouteId,
} from './App'
import { blockedFixtures, emptyFixtures, fixtures } from './data/sideportFixtures'
import { runtimeEmptyData, type SideportReadModel } from './data/sideportTypes'
import type { AddAppServices, AddIPhoneServices } from './add-flows/AddFlows'

const meta = {
  title: 'Sideport/Admin Shell',
  component: SideportAdminApp,
  args: { initialSetupOpen: true },
  parameters: {
    docs: {
      description: {
        component: 'Storybook renders the GitHub Pages demo portal with fixture data. The production bundle uses the live .NET API and keeps demo fixtures out of runtime imports.',
      },
    },
  },
} satisfies Meta<typeof SideportAdminApp>

export default meta

type Story = StoryObj<typeof meta>

const demoStatus: AdminDataStatus = {
  mode: 'demo',
  baseUrl: 'storybook://demo-data',
  message: 'Demo data for GitHub Pages and design review.',
  canMutate: false,
}

const apiUnavailableStatus: AdminDataStatus = {
  mode: 'unavailable',
  baseUrl: '/sideport-api',
  message: 'No Sideport API is reachable. Runtime pages stay empty until the .NET backend responds.',
  canMutate: false,
}

const tokenRequiredStatus: AdminDataStatus = {
  mode: 'partial',
  baseUrl: '/',
  message: 'Protected API calls are returning 401. Save the browser session token in Settings.',
  canMutate: true,
}

const workflowSteps = (completeThrough: 'server' | 'apple-signer' | 'device' | 'app' | 'install' | 'ready') => {
  const ids = ['server', 'apple-signer', 'device', 'app', 'install', 'ready'] as const
  const completeIndex = ids.indexOf(completeThrough)
  return ids.map((id, index) => ({
    id,
    state: index <= completeIndex ? 'complete' as const : index === completeIndex + 1 ? 'action-required' as const : 'not-started' as const,
    required: true,
    source: 'demo' as const,
    reason: index <= completeIndex ? `${id} evidence is complete.` : `${id} still needs attention.`,
    evidence: [],
  }))
}

const installPreflightReady: OperationPreflightDto = {
  preflightId: 'install_preflight_story',
  expiresAt: '2026-07-12T13:00:00Z',
  planVersion: 'sha256:storybook-plan',
  ready: true,
  blockers: [],
  warnings: [],
  plannedMutations: ['Create the pending registration', 'Sign and install over USB', 'Verify the app on the iPhone'],
  scarceLimits: [{ code: 'app-slots', label: 'Sideport app slots', used: 1, limit: 3 }],
  requiresConfirmation: true,
  source: 'demo',
}

const completionReceipt = {
  schemaVersion: 2 as const,
  completedAt: '2026-07-12T12:00:00Z',
  actor: { kind: 'oidc-user', displayName: 'owner@example.test' },
  accountProfileId: 'demo-personal-account',
  teamId: 'DEMO123456',
  deviceUdid: '00008030-FAKE-BB8F23A0C02E',
  registrationKey: { deviceUdid: '00008030-FAKE-BB8F23A0C02E', bundleId: 'com.example.certcountdown' },
  verifiedOperationId: 'op-onboarding-install',
  schedulerSettingsVersion: 'settings-story',
  operationalCheckedAt: '2026-07-12T11:59:59Z',
}

const freshOnboardingStatus: AdminDataStatus = {
  ...demoStatus,
  baseUrl: 'storybook://fresh-onboarding',
  onboarding: { firstRunComplete: false, schedulerEnabled: false, steps: [], setupState: 'in-progress', completionReceipt: null, workflow: { schemaVersion: 2, setupState: 'in-progress', readyNow: false, completedAt: null, verifiedOperationId: null, nextAction: { stepId: 'apple-signer', action: 'connect', label: 'Connect Apple' }, steps: workflowSteps('server') } },
}

const interactiveOnboardingStatus: AdminDataStatus = {
  ...demoStatus,
  baseUrl: 'storybook://interactive-onboarding',
  canMutate: true,
  onboarding: { firstRunComplete: false, schedulerEnabled: false, steps: [], setupState: 'in-progress', completionReceipt: null, workflow: { schemaVersion: 2, setupState: 'in-progress', readyNow: false, completedAt: null, verifiedOperationId: null, nextAction: { stepId: 'app', action: 'choose-app', label: 'Choose app' }, steps: workflowSteps('device') } },
}

const deviceOnboardingStatus: AdminDataStatus = {
  ...demoStatus,
  baseUrl: 'storybook://device-onboarding',
  canMutate: true,
  onboarding: { firstRunComplete: false, schedulerEnabled: false, steps: [], setupState: 'in-progress', completionReceipt: null, workflow: { schemaVersion: 2, setupState: 'in-progress', readyNow: false, completedAt: null, verifiedOperationId: null, nextAction: { stepId: 'device', action: 'start-enrollment', label: 'Add iPhone' }, steps: workflowSteps('apple-signer') } },
}

const completedOnboardingStatus: AdminDataStatus = {
  ...demoStatus,
  baseUrl: 'storybook://completed-onboarding',
  canMutate: true,
  onboarding: { firstRunComplete: true, schedulerEnabled: true, steps: [], setupState: 'complete', completionReceipt, workflow: { schemaVersion: 2, setupState: 'complete', readyNow: true, completedAt: completionReceipt.completedAt, verifiedOperationId: completionReceipt.verifiedOperationId, nextAction: null, steps: workflowSteps('ready') } },
}

const memberData: SideportReadModel = {
  ...fixtures,
  workspace: {
    ...fixtures.workspace,
    currentMember: fixtures.workspace.members.find((member) => member.role === 'family'),
    capabilities: {
      'devices.enroll': true,
      'operations.run': true,
    },
  },
}

const finalizationOperationId = 'op-onboarding-finalization-recovery'
const finalizationRecoveryStatus: AdminDataStatus = {
  ...interactiveOnboardingStatus,
  mode: 'live',
  baseUrl: 'storybook://onboarding-finalization-recovery',
  onboarding: {
    firstRunComplete: false,
    schedulerEnabled: false,
    steps: [],
    setupState: 'in-progress',
    selectedCatalogAppId: fixtures.catalogApps[0].id,
    completionReceipt: null,
    workflow: {
      schemaVersion: 2,
      setupState: 'in-progress',
      readyNow: false,
      completedAt: null,
      verifiedOperationId: null,
      nextAction: { stepId: 'install', action: 'retry-finalization', label: 'Retry finishing setup' },
      steps: workflowSteps('app').map((step) => step.id === 'install' ? {
        ...step,
        source: 'live' as const,
        state: 'action-required' as const,
        activeOperationId: finalizationOperationId,
        nextAction: { action: 'retry-finalization', label: 'Retry finishing setup' },
      } : { ...step, source: 'live' as const }),
    },
  },
}

const finalizationWaitingOperation: OperationRecordDto = {
  operationId: finalizationOperationId,
  type: 'install',
  status: 'waiting',
  retryable: true,
  target: { kind: 'catalog-app', deviceUdid: fixtures.devices[1].udid, bundleId: 'com.example.certcountdown' },
  result: { success: true, bundleId: 'com.example.certcountdown', expiresAt: '2026-07-19T12:00:00Z' },
  error: { code: 'onboarding-operational-check-failed', message: 'Sideport is waiting to save automatic refresh.' },
  stages: [
    { id: 'verify', label: 'Verify on iPhone', status: 'succeeded', message: 'The app was verified.' },
    { id: 'activate-registration', label: 'Activate app', status: 'succeeded', message: 'The registration is active.' },
    { id: 'enable-scheduler', label: 'Enable automatic refresh', status: 'running', message: 'Waiting for operational checks.' },
    { id: 'write-completion-receipt', label: 'Finish setup', status: 'pending', message: 'Waiting for automatic refresh.' },
  ],
}
const finishOnboardingStory = fn(async (payload: OnboardingCompletionPayload) => {
  void payload
  return completionReceipt
})

const unknownInstallOperationId = 'op-onboarding-install-unknown'
const unknownInstallStatus: AdminDataStatus = {
  ...interactiveOnboardingStatus,
  baseUrl: 'storybook://onboarding-reconcile',
  onboarding: {
    firstRunComplete: false,
    schedulerEnabled: false,
    steps: [],
    setupState: 'in-progress',
    selectedCatalogAppId: fixtures.catalogApps[0].id,
    completionReceipt: null,
    workflow: {
      schemaVersion: 2,
      setupState: 'in-progress',
      readyNow: false,
      nextAction: { stepId: 'install', action: 'reconcile-install', label: 'Check iPhone status' },
      steps: workflowSteps('app').map((step) => step.id === 'install' ? {
        ...step,
        state: 'blocked' as const,
        activeOperationId: unknownInstallOperationId,
        nextAction: { action: 'reconcile-install', label: 'Check iPhone status' },
        reason: 'The USB transfer ended without a confirmed result.',
      } : step),
    },
  },
}
const unknownInstallOperation: OperationRecordDto = {
  operationId: unknownInstallOperationId,
  type: 'install',
  status: 'unknown',
  target: { kind: 'app', deviceUdid: fixtures.devices[1].udid, bundleId: 'com.example.certcountdown' },
  error: { code: 'install-outcome-unknown', message: 'The USB transfer ended without a confirmed result.' },
  stages: [{ id: 'install', label: 'Install app', status: 'unknown', message: 'The result is unknown.' }],
}
const reconcileOnboardingStory = fn(async (operationId: string) => ({
  operationId: 'op-onboarding-reconcile-child',
  parentOperationId: operationId,
  type: 'reconcile',
  status: 'queued',
  target: unknownInstallOperation.target,
  stages: [{ id: 'verify', label: 'Check iPhone', status: 'pending', message: 'Waiting to read the iPhone.' }],
}))
const reconciledMismatchOperation: OperationRecordDto = {
  operationId: 'op-onboarding-reconcile-child',
  parentOperationId: unknownInstallOperationId,
  type: 'reconcile',
  status: 'blocked',
  target: unknownInstallOperation.target,
  error: { code: 'reconciliation-evidence-mismatch', message: 'The installed version does not match the unknown install.' },
  stages: [{ id: 'verify', label: 'Check iPhone', status: 'blocked', message: 'The installed version does not match.', error: { message: 'The installed version does not match the unknown install.' } }],
}

const installStarted = (payload: InstallOperationPayload): OperationRecordDto => ({
  operationId: `op-${payload.finishOnboarding ? 'home' : 'standalone'}-install`,
  type: 'install',
  status: 'queued',
  target: { kind: 'app', deviceUdid: payload.deviceUdid, bundleId: payload.bundleId },
  stages: [{ id: 'preflight', label: 'Check install', status: 'pending', message: 'Waiting to start.' }],
})

const savePendingStoryApp = async (payload: PendingAppRegistrationPayload): Promise<AppRegistrationDto> => ({
  bundleId: fixtures.catalogApps.find((app) => app.id === payload.catalogAppId)?.expectedBundleId,
  deviceUdid: payload.deviceUdid,
  lifecycle: payload.lifecycle,
  catalogAppId: payload.catalogAppId,
})
const savePendingOnboardingStory = fn(savePendingStoryApp)
const preflightOnboardingStory = fn(async (payload: InstallPreflightPayload) => {
  void payload
  return installPreflightReady
})

const freshOnboardingData: SideportReadModel = {
  ...runtimeEmptyData,
  system: {
    ...fixtures.system,
    scheduler: { enabled: false, source: 'demo' },
  },
  catalogApps: fixtures.catalogApps,
  personalApple: {
    ...runtimeEmptyData.personalApple,
    state: 'not-configured',
    secretCustody: 'sideport-managed-encrypted-store',
    credentialEntry: { supported: true, allowedNow: true, blockedReason: null },
    message: 'No Apple account is connected yet.',
    source: 'demo',
  },
  workspace: fixtures.workspace,
}

const readyForAppOnboardingData: SideportReadModel = {
  ...freshOnboardingData,
  devices: [fixtures.devices[1]],
  personalApple: {
    ...freshOnboardingData.personalApple,
    state: 'authenticated',
    accountProfileId: 'demo-personal-account',
    appleIdHint: 'a***@example.test',
    selectedTeamId: 'DEMO123456',
    message: 'Apple accepted the account and returned one Personal Team.',
    teams: [{ teamId: 'DEMO123456', name: 'Example Personal Team', type: 'Personal Team' }],
  },
}

const readyForDeviceOnboardingData: SideportReadModel = {
  ...readyForAppOnboardingData,
  devices: [],
  system: {
    ...readyForAppOnboardingData.system,
    operational: false,
    checks: [
      ...readyForAppOnboardingData.system.checks.filter((check) => check.id !== 'device-transport'),
      {
        id: 'device-transport',
        status: 'fail',
        source: 'demo',
        checkedAt: '2026-07-14T01:20:00Z',
        scope: 'iphone',
        affectedResources: ['usbmux-transport'],
        reason: 'Sideport cannot reach the iPhone transport.',
        nextAction: 'Connect the iPhone over USB.',
      },
    ],
  },
}

const completedOnboardingData: SideportReadModel = {
  ...readyForAppOnboardingData,
  system: {
    ...readyForAppOnboardingData.system,
    scheduler: {
      enabled: true,
      checkedAt: '2026-07-12T12:00:00Z',
      policy: {
        mode: 'due-only',
        evaluationInterval: '01:00:00',
        refreshLeadTime: '2.00:00:00',
        resignInterval: null,
        catchUp: 'evaluate-on-startup',
        missedIntervals: 'not-replayed',
      },
      nextEvaluationAt: '2026-07-12T13:00:00Z',
      concurrency: { maxRunning: 1, lockState: 'idle', operationId: null },
      source: 'demo',
    },
  },
  installedApps: [{
    bundleId: 'com.example.certcountdown',
    deviceUdid: fixtures.devices[1].udid,
    name: 'Cert Clock',
    version: '0.1.0',
    managedBySideport: true,
    source: 'demo',
  }],
  apps: [{
    ...fixtures.apps[0],
    deviceUdid: fixtures.devices[1].udid,
    teamId: 'DEMO123456',
    lastSucceeded: true,
    lastError: null,
    displayName: { value: 'Cert Clock', source: 'demo' },
    version: { value: '0.1.0', source: 'demo' },
  }],
}

const terminalLineageOperationId = 'op-onboarding-terminal-lineage'
const terminalLineageStatus: AdminDataStatus = {
  ...unknownInstallStatus,
  baseUrl: 'storybook://onboarding-terminal-lineage',
  onboarding: {
    ...unknownInstallStatus.onboarding!,
    activeInstallOperationId: terminalLineageOperationId,
    workflow: {
      ...unknownInstallStatus.onboarding!.workflow!,
      nextAction: { stepId: 'install', action: 'review-install', label: 'Review install' },
      steps: unknownInstallStatus.onboarding!.workflow!.steps.map((step) => step.id === 'install' ? {
        ...step,
        activeOperationId: terminalLineageOperationId,
        nextAction: { action: 'review-install', label: 'Review install' },
        reason: 'The saved IPA changed after device verification.',
      } : step),
    },
  },
}
const terminalLineageOperation: OperationRecordDto = {
  operationId: terminalLineageOperationId,
  type: 'install',
  status: 'blocked',
  retryable: false,
  target: unknownInstallOperation.target,
  result: { success: true, bundleId: 'com.example.certcountdown', version: '0.1.0', expiresAt: '2026-07-19T12:00:00Z' },
  error: { code: 'onboarding-artifact-lineage-unavailable', message: 'The saved IPA changed after device verification.' },
  stages: [
    { id: 'verify', label: 'Verify on iPhone', status: 'succeeded', message: 'The app was verified.' },
    { id: 'write-completion-receipt', label: 'Finish setup', status: 'blocked', message: 'The saved IPA changed.', error: { message: 'The saved IPA changed after device verification.' } },
  ],
}

const standaloneResumeOperationId = 'op-standalone-resume'
const standaloneResumeData: SideportReadModel = {
  ...readyForAppOnboardingData,
  operations: [{
    operationId: standaloneResumeOperationId,
    type: 'install',
    status: 'running',
    createdAt: '2026-07-12T12:10:00Z',
    updatedAt: '2026-07-12T12:10:05Z',
    deviceUdid: fixtures.devices[1].udid,
    bundleId: 'com.example.certcountdown',
    actor: 'owner@example.test',
    stages: [{ id: 'install', label: 'Install app', status: 'running', message: 'Installing over USB.' }],
    cancelable: false,
    retryable: false,
    rerunnable: false,
    finishOnboarding: false,
    source: 'demo',
  }],
}

const pendingRegistrationData: SideportReadModel = {
  ...fixtures,
  apps: [{ ...fixtures.apps[0], lifecycle: 'pending-install', lastSucceeded: null, lastError: null, expiresAt: undefined, lastVerifiedOperationId: null }],
}

const schedulerDisabledStory = fn(async (enabled: boolean): Promise<SchedulerStatusDto> => ({
  enabled,
  checkedAt: '2026-07-12T12:05:00Z',
  policy: completedOnboardingData.system.scheduler.policy!,
  nextEvaluationAt: enabled ? '2026-07-12T13:05:00Z' : null,
  lastEvaluation: null,
  dueCount: 0,
  queuedCount: 0,
  concurrency: { maxRunning: 1, lockState: 'idle', operationId: null },
  historyRetention: { maxEvaluations: 100 },
  source: 'live',
}))

const runtimeAddAppServices: AddAppServices = {
  loadImportRoots: async () => [{ id: 'apps', label: 'Sideport app storage', available: true }],
  upload: async (file) => ({ id: 'uploaded-app', name: file.name.replace(/\.ipa$/i, ''), purpose: 'Uploaded and inspected.', versionLabel: '1.0', status: 'ready' }),
  importFromRoot: async () => ({ id: 'stored-app', name: 'Stored app', purpose: 'Imported from configured storage.', versionLabel: '1.0', status: 'ready' }),
  loadGitHubSources: async () => ({ capability: { kind: 'github-app', supported: true, allowedNow: true }, sources: [{ id: 'public-sideport', repository: 'dragoshont/sideport', visibility: 'public', status: 'connected' }] }),
  connectGitHub: async (repository, visibility) => ({ id: 'connection-1', repository, visibility, status: 'connected', sourceId: 'connected-source' }),
  loadGitHubReleases: async (sourceId) => ({ sourceId, repository: 'dragoshont/sideport', releases: [{ releaseId: 17, tag: 'sample-apps', name: 'Sample apps', prerelease: false, assets: [{ assetId: 41, name: 'Cert-Clock.ipa', sizeBytes: 18_432, digest: 'sha256:demo', importable: true }] }] }),
  importGitHub: async () => ({ id: 'cert-clock-github', name: 'Cert Clock', purpose: 'Imported from a GitHub release.', versionLabel: '0.1.0', status: 'ready' }),
}

const githubAvailableWhenStorageFails: AddAppServices = {
  ...runtimeAddAppServices,
  loadImportRoots: async () => { throw new Error('Configured storage is offline.') },
}

const runtimeAddIPhoneServices: AddIPhoneServices = {
  start: async () => ({ operationId: 'op-enroll-story', status: 'waiting', stages: [{ id: 'wait-for-usb', status: 'waiting', message: 'Waiting for iPhone.' }] }),
  read: async () => ({ operationId: 'op-enroll-story', status: 'succeeded', stages: [{ id: 'accept-device', status: 'succeeded', message: 'iPhone added.' }], result: { deviceEnrollment: { selectedDeviceUdid: '000081-story', inventoryState: 'accepted' } } }),
}

const startOnboardingIPhoneStory = fn(async () => ({ operationId: 'op-onboarding-enroll-story', status: 'waiting', stages: [{ id: 'wait-for-usb', status: 'waiting', message: 'Waiting for iPhone.' }] }))
const onboardingAutoStartServices: AddIPhoneServices = {
  start: startOnboardingIPhoneStory,
  read: async () => ({ operationId: 'op-onboarding-enroll-story', status: 'waiting', stages: [{ id: 'wait-for-usb', status: 'waiting', message: 'Waiting for iPhone.' }] }),
}
const startResumedOnboardingIPhoneStory = fn(async () => ({ operationId: 'unexpected-new-operation', status: 'waiting', stages: [] }))
const readResumedOnboardingIPhoneStory = fn(async () => ({ operationId: 'op-onboarding-enroll-resume', status: 'waiting', stages: [{ id: 'wait-for-usb', status: 'waiting', message: 'Still waiting for iPhone.' }] }))
const onboardingResumeServices: AddIPhoneServices = {
  start: startResumedOnboardingIPhoneStory,
  read: readResumedOnboardingIPhoneStory,
}

const deviceEnrollmentInProgressStatus: AdminDataStatus = {
  ...deviceOnboardingStatus,
  mode: 'live',
  onboarding: {
    ...deviceOnboardingStatus.onboarding!,
    workflow: {
      ...deviceOnboardingStatus.onboarding!.workflow!,
      nextAction: null,
      steps: deviceOnboardingStatus.onboarding!.workflow!.steps.map((step) => step.id === 'device' ? {
        ...step,
        state: 'in-progress' as const,
        activeOperationId: 'op-onboarding-enroll-resume',
        reason: 'Waiting for iPhone over USB.',
        nextAction: undefined,
      } : step),
    },
  },
}

const enrollmentRecoverySource = {
  operationId: 'op-enroll-recovery-source',
  status: 'recovery-required',
  retryable: true,
  stages: [{ id: 'request-pairing', status: 'succeeded', message: 'Trust was already requested.' }],
  error: { code: 'device-enrollment-recovery-required', message: 'Reconnect this iPhone so Sideport can check the existing Trust request.' },
}
const startEnrollmentRecoveryStory = fn(async () => enrollmentRecoverySource)
const retryEnrollmentRecoveryStory = fn(async (operationId: string) => {
  void operationId
  return {
    operationId: 'op-enroll-recovery-child',
    status: 'waiting',
    retryable: false,
    stages: [
      { id: 'request-pairing', status: 'succeeded', message: 'The earlier Trust request will not be repeated.' },
      { id: 'verify-lockdown', status: 'waiting', message: 'Checking Trust.' },
    ],
  }
})
const readEnrollmentRecoveryStory = fn(async () => ({
  operationId: 'op-enroll-recovery-child',
  status: 'succeeded',
  stages: [{ id: 'accept-device', status: 'succeeded', message: 'iPhone added.' }],
  result: { deviceEnrollment: { selectedDeviceUdid: '000081-recovery', inventoryState: 'accepted' } },
}))
const enrollmentRecoveryServices: AddIPhoneServices = {
  start: startEnrollmentRecoveryStory,
  retry: retryEnrollmentRecoveryStory,
  read: readEnrollmentRecoveryStory,
}

const routeStory = (initialRoute: RouteId, name: string): Story => ({
  name,
  args: { data: fixtures, apiStatus: demoStatus, initialRoute },
})

export const OverviewHealthy = routeStory('home', 'Overview - healthy mixed fleet')
export const FirstRunOnboarding: Story = {
  name: 'First Run Onboarding',
  args: { data: freshOnboardingData, apiStatus: freshOnboardingStatus, initialRoute: 'home' },
  parameters: {
    docs: {
      description: {
        story: 'The production six-step runtime shell with a deterministic fresh-deployment read model: no Apple signer, accepted iPhone, installation, or scheduler.',
      },
    },
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByTestId('runtime-first-run-onboarding')).toBeVisible()
    await expect(canvas.getByRole('heading', { name: 'Connect Apple' })).toBeVisible()
    await expect(canvas.getAllByRole('main')).toHaveLength(1)
    await expect(canvas.getByText('1 of 6 complete')).toBeVisible()
  },
}
export const OwnerAccountCanEnterPortalWithoutIPhone: Story = {
  name: 'Owner account - portal access before iPhone setup',
  args: { data: freshOnboardingData, apiStatus: freshOnboardingStatus, initialRoute: 'home', initialSetupOpen: false },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByRole('heading', { name: 'Apps and iPhones at a glance' })).toBeVisible()
    await expect(canvas.getByText('Your Owner account is ready')).toBeVisible()
    await expect(canvas.getByText(/Next: Connect Apple/)).toBeVisible()
    await expect(canvas.queryByTestId('runtime-first-run-onboarding')).not.toBeInTheDocument()

    await userEvent.click(canvas.getByRole('button', { name: 'Continue setup' }))
    await expect(canvas.getByTestId('runtime-first-run-onboarding')).toBeVisible()
    await expect(canvas.getByRole('heading', { name: 'Connect Apple' })).toBeVisible()

    await userEvent.click(canvas.getByRole('button', { name: 'Set up later' }))
    await expect(canvas.getByRole('heading', { name: 'Apps and iPhones at a glance' })).toBeVisible()
    await expect(canvas.getByRole('button', { name: 'Continue setup' })).toBeVisible()
  },
}
export const FirstRunConnectIPhoneActionable: Story = {
  name: 'First Run - missing iPhone is actionable and automatic',
  args: { data: readyForDeviceOnboardingData, apiStatus: { ...deviceOnboardingStatus, mode: 'live' }, initialRoute: 'home', addIPhoneServices: onboardingAutoStartServices },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const page = within(canvasElement.ownerDocument.body)
    startOnboardingIPhoneStory.mockClear()
    const panel = within(canvas.getByTestId('runtime-onboarding-panel-device'))
    await expect(panel.getByRole('heading', { name: 'Connect iPhone' })).toBeVisible()
    await expect(panel.getByText('Connect the iPhone now')).toBeVisible()
    await expect(panel.getByText('Use a data-capable cable and plug the iPhone directly into the computer running Sideport.')).toBeVisible()
    await expect(panel.getByText('When asked, tap Trust This Computer and enter the iPhone passcode.')).toBeVisible()
    await expect(panel.getByText(/advance automatically/)).toBeVisible()
    await expect(panel.getByRole('button', { name: 'Start connecting' })).toBeEnabled()
    await expect(panel.queryByRole('button', { name: /Pair|I tapped Trust|Add to Sideport/ })).not.toBeInTheDocument()
    await expect(canvas.getByText('2 of 6 complete')).toBeVisible()
    await userEvent.click(panel.getByRole('button', { name: 'Start connecting' }))
    const dialog = page.getByRole('dialog', { name: 'Add an iPhone' })
    await waitFor(() => expect(startOnboardingIPhoneStory).toHaveBeenCalledTimes(1))
    await expect(within(dialog).getByRole('button', { name: 'Close Add iPhone' })).toHaveFocus()
    await expect(within(dialog).getByRole('button', { name: 'Waiting for iPhone…' })).toBeDisabled()
    await expect(within(dialog).queryByRole('button', { name: 'Connect iPhone' })).not.toBeInTheDocument()
    await new Promise((resolve) => window.setTimeout(resolve, 1_200))
    await expect(startOnboardingIPhoneStory).toHaveBeenCalledTimes(1)
  },
}
export const FirstRunConnectIPhoneResumesWithoutStartingAgain: Story = {
  name: 'First Run - active iPhone connection resumes without another start',
  args: { data: readyForDeviceOnboardingData, apiStatus: deviceEnrollmentInProgressStatus, initialRoute: 'home', addIPhoneServices: onboardingResumeServices },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const page = within(canvasElement.ownerDocument.body)
    startResumedOnboardingIPhoneStory.mockClear()
    readResumedOnboardingIPhoneStory.mockClear()
    await userEvent.click(within(canvas.getByTestId('runtime-onboarding-panel-device')).getByRole('button', { name: 'Show connection status' }))
    const dialog = page.getByRole('dialog', { name: 'Add an iPhone' })
    await waitFor(() => expect(readResumedOnboardingIPhoneStory).toHaveBeenCalled())
    await expect(startResumedOnboardingIPhoneStory).not.toHaveBeenCalled()
    await expect(within(dialog).getByRole('button', { name: 'Waiting for iPhone…' })).toBeDisabled()
  },
}
export const LiveOnboarding: Story = {
  name: 'First Run - selection survives a runtime remount',
  args: { data: readyForAppOnboardingData, apiStatus: interactiveOnboardingStatus, initialRoute: 'home' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByTestId('runtime-first-run-onboarding')).toBeVisible()
    const setupNavigation = canvas.getByRole('navigation', { name: 'First-run setup steps' })
    await expect(setupNavigation).toBeVisible()
    await expect(within(setupNavigation).getByRole('button', { name: /Check Sideport/ })).toBeVisible()
    await expect(within(setupNavigation).getByRole('button', { name: /Connect Apple/ })).toBeVisible()
    await expect(within(setupNavigation).getByRole('button', { name: /Connect iPhone/ })).toBeVisible()
    await expect(within(setupNavigation).getByRole('button', { name: /Choose app/ })).toBeVisible()
    await expect(within(setupNavigation).getByRole('button', { name: /Install/ })).toBeVisible()
    await expect(within(setupNavigation).getByRole('button', { name: /Ready/ })).toBeVisible()

    const appPanel = within(canvas.getByTestId('runtime-onboarding-panel-app'))
    const radios = appPanel.getAllByRole('radio')
    await userEvent.click(radios[0])
    await userEvent.keyboard('{ArrowDown}')
    await expect(radios[1]).toBeChecked()
    await expect(canvas.getByTestId('runtime-onboarding-live-region')).toHaveTextContent('Dice Roll selected')

    await userEvent.click(canvas.getByRole('button', { name: 'Show technical details' }))
    await expect(canvas.getByText('Browser-session app choice · non-authoritative')).toBeVisible()
  },
}

export const OnboardingInstallStartsInline: Story = {
  name: 'First Run - install starts inline once',
  args: {
    data: readyForAppOnboardingData,
    apiStatus: { ...interactiveOnboardingStatus, baseUrl: 'storybook://onboarding-install-start' },
    initialRoute: 'home',
    registerPendingAppService: savePendingOnboardingStory,
    preflightInstallService: preflightOnboardingStory,
    installAppService: async (payload) => {
      if (!payload.finishOnboarding) throw new Error('The onboarding install must carry finishOnboarding=true.')
      if (payload.preflightId !== installPreflightReady.preflightId || payload.planVersion !== installPreflightReady.planVersion) throw new Error('The confirmed preflight was not submitted.')
      if (payload.catalogAppId !== fixtures.catalogApps[0].id || payload.accountProfileId !== 'demo-personal-account') throw new Error('The selected catalog app and Apple account were not bound to the install.')
      return installStarted(payload)
    },
    readOperationService: async (operationId) => ({ ...installStarted({ deviceUdid: fixtures.devices[1].udid, bundleId: 'com.example.certcountdown', catalogAppId: fixtures.catalogApps[0].id, accountProfileId: 'demo-personal-account', preflightId: 'install_preflight_story', planVersion: 'sha256:storybook-plan', finishOnboarding: true, confirmedPlannedMutations: true, idempotencyKey: 'story' }), operationId, status: 'succeeded', stages: [{ id: 'verify', label: 'Verify on iPhone', status: 'succeeded', message: 'Verified.' }] }),
  },
  play: async ({ canvasElement }) => {
    savePendingOnboardingStory.mockClear()
    preflightOnboardingStory.mockClear()
    const canvas = within(canvasElement)
    const appPanel = within(canvas.getByTestId('runtime-onboarding-panel-app'))
    await userEvent.click(appPanel.getAllByRole('radio')[0])
    await userEvent.click(appPanel.getByRole('button', { name: /Continue to install/ }))
    const installPanel = within(canvas.getByTestId('runtime-onboarding-panel-install'))
    await expect(await installPanel.findByRole('button', { name: 'Install and finish' })).toBeEnabled()
    await expect(savePendingOnboardingStory).toHaveBeenCalledWith({ catalogAppId: fixtures.catalogApps[0].id, deviceUdid: fixtures.devices[1].udid, accountProfileId: 'demo-personal-account', lifecycle: 'pending-install' })
    await expect(preflightOnboardingStory).toHaveBeenCalledWith({ deviceUdid: fixtures.devices[1].udid, bundleId: 'com.example.certcountdown', catalogAppId: fixtures.catalogApps[0].id, accountProfileId: 'demo-personal-account', finishOnboarding: true })
    await expect(savePendingOnboardingStory.mock.invocationCallOrder[0]).toBeLessThan(preflightOnboardingStory.mock.invocationCallOrder[0])
    await userEvent.click(installPanel.getByRole('button', { name: 'Install and finish' }))
    await expect(await installPanel.findByText('Verify on iPhone')).toBeVisible()
    await expect(canvas.getByRole('heading', { name: 'Install' })).toBeVisible()
  },
}

export const OnboardingInstallRequestError: Story = {
  name: 'First Run - install request error stays inline',
  args: {
    data: readyForAppOnboardingData,
    apiStatus: { ...interactiveOnboardingStatus, baseUrl: 'storybook://onboarding-install-error' },
    initialRoute: 'home',
    registerPendingAppService: savePendingStoryApp,
    preflightInstallService: async () => installPreflightReady,
    installAppService: async () => { throw new Error('Sideport could not safely start this USB install.') },
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const appPanel = within(canvas.getByTestId('runtime-onboarding-panel-app'))
    await userEvent.click(appPanel.getAllByRole('radio')[0])
    await userEvent.click(appPanel.getByRole('button', { name: /Continue to install/ }))
    const installPanel = within(canvas.getByTestId('runtime-onboarding-panel-install'))
    await expect(await installPanel.findByRole('button', { name: 'Install and finish' })).toBeEnabled()
    await userEvent.click(installPanel.getByRole('button', { name: 'Install and finish' }))
    await expect(await installPanel.findByRole('alert')).toHaveTextContent('Sideport could not safely start this USB install.')
    await expect(installPanel.getByRole('button', { name: 'Install and finish' })).toBeEnabled()
  },
}

export const FirstRunOnboardingMobile390: Story = {
  name: 'First Run - mobile setup at 390px',
  args: { data: freshOnboardingData, apiStatus: freshOnboardingStatus, initialRoute: 'home' },
  parameters: {
    viewport: { defaultViewport: 'mobile1' },
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByLabelText('Step 2 of 6: Connect Apple')).toBeVisible()
    await expect(canvas.getByRole('progressbar', { name: 'Setup progress' })).toBeVisible()
    const primary = within(canvas.getByTestId('runtime-onboarding-panel-apple-signer')).getByRole('button', { name: /Finish Apple setup above/ })
    await expect(primary.getBoundingClientRect().height).toBeGreaterThanOrEqual(44)
    await expect(canvas.getAllByRole('main')).toHaveLength(1)
  },
}

export const CompletedOnboardingReload: Story = {
  name: 'First Run - server completion survives remount',
  args: { data: completedOnboardingData, apiStatus: completedOnboardingStatus, initialRoute: 'home' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByRole('heading', { name: 'Apps and iPhones at a glance' })).toBeVisible()
    await expect(canvas.queryByRole('button', { name: 'Onboarding' })).not.toBeInTheDocument()
  },
}
export const OnboardingFinalizationRecovery: Story = {
  name: 'First Run - verified install retries finalization only',
  args: {
    data: readyForAppOnboardingData,
    apiStatus: finalizationRecoveryStatus,
    initialRoute: 'home',
    completeOnboardingService: finishOnboardingStory,
    readOperationService: async () => finalizationWaitingOperation,
  },
  play: async ({ canvasElement }) => {
    finishOnboardingStory.mockClear()
    const canvas = within(canvasElement)
    const installPanel = within(canvas.getByTestId('runtime-onboarding-panel-install'))
    const retry = await installPanel.findByRole('button', { name: 'Retry finishing setup' })
    await expect(retry).toBeEnabled()
    await expect(installPanel.getByText('The app is already verified')).toBeVisible()
    await expect(installPanel.queryByRole('button', { name: 'Installing…' })).not.toBeInTheDocument()
    await userEvent.click(retry)
    await expect(finishOnboardingStory).toHaveBeenCalledWith(expect.objectContaining({ verifiedOperationId: finalizationOperationId }))
  },
}
export const OnboardingUnknownInstallReconciliation: Story = {
  name: 'First Run - unknown install checks iPhone without reinstalling',
  args: {
    data: readyForAppOnboardingData,
    apiStatus: unknownInstallStatus,
    initialRoute: 'home',
    reconcileOperationService: reconcileOnboardingStory,
    readOperationService: async (operationId) => operationId === unknownInstallOperationId ? unknownInstallOperation : reconciledMismatchOperation,
  },
  play: async ({ canvasElement }) => {
    reconcileOnboardingStory.mockClear()
    const canvas = within(canvasElement)
    const installPanel = within(canvas.getByTestId('runtime-onboarding-panel-install'))
    await expect(await installPanel.findByText('Check before trying again')).toBeVisible()
    await userEvent.click(installPanel.getByRole('button', { name: 'Check iPhone status' }))
    await expect(reconcileOnboardingStory).toHaveBeenCalledWith(
      unknownInstallOperationId,
      expect.objectContaining({ idempotencyKey: expect.stringContaining('onboarding-reconcile') }),
    )
    await expect(await installPanel.findByRole('alert')).toHaveTextContent('The installed version does not match the unknown install.')
    await expect(installPanel.getByRole('button', { name: 'Check iPhone status' })).toBeEnabled()
  },
}
export const OnboardingTerminalLineageBlock: Story = {
  name: 'First Run - terminal lineage block never offers finalization retry',
  args: {
    data: readyForAppOnboardingData,
    apiStatus: terminalLineageStatus,
    initialRoute: 'home',
    readOperationService: async () => terminalLineageOperation,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const installPanel = within(canvas.getByTestId('runtime-onboarding-panel-install'))
    await expect(await installPanel.findByText('Saved setup evidence no longer matches')).toBeVisible()
    await expect(installPanel.queryByRole('button', { name: 'Retry finishing setup' })).not.toBeInTheDocument()
    await expect(installPanel.getByRole('alert')).toHaveTextContent('The saved IPA changed after device verification.')
  },
}
export const DeviceInventory = routeStory('devices', 'Devices - table and mobile cards')
export const DeviceDetailTwoApps = routeStory('device-detail', 'Device detail - two app slots')
export const AppCatalogSeed = routeStory('apps', 'App catalog - Cert Clock seed')
export const AppCatalogSearch: Story = {
  name: 'Apps - search approved library',
  args: { data: fixtures, apiStatus: completedOnboardingStatus, initialRoute: 'apps' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const search = canvas.getByRole('searchbox', { name: 'Search approved apps' })
    await userEvent.type(search, 'memory')
    await expect(canvas.getByRole('heading', { name: 'Concentration' })).toBeVisible()
    await expect(canvas.queryByRole('heading', { name: 'Cert Clock' })).not.toBeInTheDocument()
    await userEvent.clear(search)
    await expect(canvas.getByRole('heading', { name: 'Cert Clock' })).toBeVisible()
  },
}
export const InstallAppOneAction: Story = {
  name: 'Install app - one action with smart defaults',
  args: {
    data: completedOnboardingData,
    apiStatus: { ...completedOnboardingStatus, baseUrl: 'storybook://standalone-install' },
    initialRoute: 'install-app',
    registerPendingAppService: savePendingStoryApp,
    preflightInstallService: async () => installPreflightReady,
    installAppService: async (payload) => {
      if (payload.finishOnboarding) throw new Error('A signed-in install must not finish onboarding.')
      return installStarted(payload)
    },
    readOperationService: async (operationId) => ({ ...installStarted({ deviceUdid: fixtures.devices[1].udid, bundleId: 'com.example.certcountdown', catalogAppId: fixtures.catalogApps[0].id, accountProfileId: 'demo-personal-account', preflightId: 'install_preflight_story', planVersion: 'sha256:storybook-plan', finishOnboarding: false, confirmedPlannedMutations: true, idempotencyKey: 'story' }), operationId, status: 'running' }),
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByRole('heading', { name: 'Install an app on your iPhone' })).toBeVisible()
    await expect(canvas.getByText('Example Personal Team')).toBeVisible()
    await expect(canvas.getByText(/does not switch this install to Wi-Fi/)).toBeVisible()
    await expect(canvas.queryByLabelText('Apple ID')).not.toBeInTheDocument()
    await expect(canvas.queryByLabelText('Team ID')).not.toBeInTheDocument()
    await expect(canvas.queryByText('Server IPA path')).not.toBeInTheDocument()
    await userEvent.click(await canvas.findByRole('button', { name: 'Install app' }))
    await expect(await canvas.findByText('Sideport is signing, installing, and verifying the app.')).toBeVisible()
    await expect(canvas.getByRole('button', { name: 'Installing…' })).toBeDisabled()
    await expect(canvas.queryByText('Installed — you can unplug')).not.toBeInTheDocument()
  },
}
export const InstallAppReloadResume: Story = {
  name: 'Install app - active standalone install resumes after reload',
  args: {
    data: standaloneResumeData,
    apiStatus: { ...completedOnboardingStatus, baseUrl: 'storybook://standalone-resume' },
    initialRoute: 'install-app',
    registerPendingAppService: fn(savePendingStoryApp),
    preflightInstallService: fn(async () => installPreflightReady),
    readOperationService: fn(async () => ({
      operationId: standaloneResumeOperationId,
      type: 'install',
      status: 'succeeded',
      target: { deviceUdid: fixtures.devices[1].udid, bundleId: 'com.example.certcountdown' },
      stages: [{ id: 'verify', label: 'Verify on iPhone', status: 'succeeded', message: 'Verified after reload.' }],
      result: { success: true, bundleId: 'com.example.certcountdown', version: '0.1.0', expiresAt: '2026-07-19T12:00:00Z' },
    })),
  },
  play: async ({ canvasElement, args }) => {
    const canvas = within(canvasElement)
    await waitFor(() => expect(args.readOperationService).toHaveBeenCalledWith(standaloneResumeOperationId), { timeout: 3_000 })
    await waitFor(() => expect(canvas.getAllByText(/Installed — you can unplug/).length).toBeGreaterThan(0), { timeout: 3_000 })
    await expect(canvas.getByText(/completion chime was attempted/i)).toBeVisible()
    await expect(args.registerPendingAppService).not.toHaveBeenCalled()
  },
}
export const PendingRegistrationState: Story = {
  name: 'App catalog - pending registration is not healthy',
  args: { data: pendingRegistrationData, apiStatus: demoStatus, initialRoute: 'apps' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const pendingLabel = canvas.getByText('Awaiting verified install')
    await expect(pendingLabel).toBeVisible()
    const registration = pendingLabel.closest('article')
    await expect(registration).not.toBeNull()
    await expect(within(registration!).queryByText('Healthy')).not.toBeInTheDocument()
  },
}
export const ActivityOperations = routeStory('activity', 'Activity - operation history')
export const PeopleWorkspace = routeStory('people', 'People - roles, members, invite, audit')
export const SettingsConsolidated = routeStory('settings', 'Settings - sign-in, refresh, signing, and system')
export const SettingsSchedulerPolicy: Story = {
  name: 'Settings - live automatic refresh policy',
  args: {
    data: completedOnboardingData,
    apiStatus: completedOnboardingStatus,
    initialRoute: 'settings',
    schedulerSettingsService: schedulerDisabledStory,
  },
  play: async ({ canvasElement }) => {
    schedulerDisabledStory.mockClear()
    const canvas = within(canvasElement)
    await expect(canvas.getByText('Every hour · only apps that are due')).toBeVisible()
    await userEvent.click(canvas.getByRole('button', { name: 'Turn off automatic refresh' }))
    await expect(schedulerDisabledStory).toHaveBeenCalledWith(false)
    await expect(canvas.getByRole('button', { name: 'Turn on automatic refresh' })).toBeVisible()
    await expect(canvas.getByText('Not scheduled')).toBeVisible()
  },
}

export const EmptyFleet: Story = {
  args: { data: emptyFixtures, apiStatus: demoStatus, initialRoute: 'devices' },
}

export const AnisetteBlocked: Story = {
  args: { data: blockedFixtures, apiStatus: demoStatus, initialRoute: 'home' },
}

export const ApiUnavailableRuntime: Story = {
  args: { data: runtimeEmptyData, apiStatus: apiUnavailableStatus, initialRoute: 'home' },
}

export const TokenRequiredSettings: Story = {
  args: { data: fixtures, apiStatus: tokenRequiredStatus, initialRoute: 'settings' },
}

export const OidcAppleRequestSecurity: Story = {
  name: 'Request security - OIDC CSRF stays in memory',
  args: { data: fixtures, apiStatus: demoStatus, initialRoute: 'settings' },
  play: async () => {
    const originalFetch = window.fetch
    const requestHeaders: Headers[] = []
    window.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url
      requestHeaders.push(new Headers(init?.headers ?? (input instanceof Request ? input.headers : undefined)))
      return new Response('{}', {
        status: 200,
        headers: {
          'Content-Type': 'application/json',
          ...(url.endsWith('/api/apple-access/personal/status') ? { 'X-Sideport-CSRF': 'storybook-csrf-token' } : {}),
        },
      })
    }) as typeof window.fetch
    try {
      const oidcConfig = { baseUrl: '/storybook-csrf', canMutate: true }
      await refreshPersonalAppleRequestSecurity(oidcConfig)
      await selectPersonalAppleTeam({ accountProfileId: 'acct_story', teamId: 'TEAMSTORY1' }, oidcConfig)
      await expect(requestHeaders[1].get('X-Sideport-CSRF')).toBe('storybook-csrf-token')
      await expect(requestHeaders[1].get('Authorization')).toBeNull()

      await selectPersonalAppleTeam(
        { accountProfileId: 'acct_story', teamId: 'TEAMSTORY1' },
        { ...oidcConfig, token: 'storybook-bearer-token' },
      )
      await expect(requestHeaders[2].get('X-Sideport-CSRF')).toBeNull()
      await expect(requestHeaders[2].get('Authorization')).toBe('Bearer storybook-bearer-token')
    } finally {
      window.fetch = originalFetch
    }
  },
}

export const CommandMenuOpen: Story = {
  name: 'Command menu - ⌘K search',
  args: { data: fixtures, apiStatus: demoStatus, initialRoute: 'home', initialCommandOpen: true },
}

export const DeviceDetailTabbed: Story = {
  name: 'Device detail - tabs + working refresh',
  args: { data: fixtures, apiStatus: demoStatus, initialRoute: 'device-detail' },
}

export const GlobalAddMenu: Story = {
  name: 'Signed in - global Add menu + keyboard focus',
  args: { data: fixtures, apiStatus: completedOnboardingStatus, initialRoute: 'home' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const page = within(canvasElement.ownerDocument.body)
    const trigger = canvas.getByTestId('global-add-trigger')

    await userEvent.click(trigger)
    const addChoices = page.getByRole('group', { name: 'Add to Sideport choices' })
    await expect(addChoices).toBeVisible()
    await expect(within(addChoices).getByRole('button', { name: /Add iPhone/ })).toBeVisible()
    await expect(within(addChoices).getByRole('button', { name: /Add app/ })).toBeVisible()

    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(trigger).toHaveFocus())
  },
}

export const AddIPhoneAutomatic: Story = {
  name: 'Signed in - Add iPhone waits for Trust automatically',
  args: { data: fixtures, apiStatus: demoStatus, initialRoute: 'devices' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const page = within(canvasElement.ownerDocument.body)
    const globalTrigger = canvas.getByRole('button', { name: 'Add to Sideport' })
    await userEvent.click(globalTrigger)
    const addChoices = page.getByRole('group', { name: 'Add to Sideport choices' })
    await userEvent.click(within(addChoices).getByRole('button', { name: /Add iPhone/ }))

    const dialog = page.getByRole('dialog', { name: 'Add an iPhone' })
    await expect(within(dialog).getByText(/Developer Mode/)).toBeVisible()
    await expect(within(dialog).getAllByRole('button', { name: 'Connect iPhone' })).toHaveLength(1)
    await expect(within(dialog).queryByRole('button', { name: /Pair|I tapped Trust|Add to Sideport/ })).not.toBeInTheDocument()

    await userEvent.click(within(dialog).getByRole('button', { name: 'Connect iPhone' }))
    await waitFor(() => expect(within(dialog).getByText('iPhone added to Sideport')).toBeVisible(), { timeout: 4_000 })
    await expect(within(dialog).queryByRole('button', { name: /Pair|I tapped Trust|Add to Sideport/ })).not.toBeInTheDocument()
    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(globalTrigger).toHaveFocus())
  },
}

export const AddAppSources: Story = {
  name: 'Signed in - Add app sources + private GitHub permission',
  args: { data: fixtures, apiStatus: completedOnboardingStatus, initialRoute: 'home' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const page = within(canvasElement.ownerDocument.body)
    await userEvent.click(canvas.getByRole('button', { name: 'Add to Sideport' }))
    const addChoices = page.getByRole('group', { name: 'Add to Sideport choices' })
    await userEvent.click(within(addChoices).getByRole('button', { name: /Add app/ }))

    const dialog = page.getByRole('dialog', { name: 'Choose or add an app' })
    await expect(within(dialog).getByRole('radio', { name: /Cert Clock/ })).toBeVisible()
    await expect(within(dialog).getByRole('button', { name: /Choose a file/ })).toBeVisible()
    await expect(within(dialog).getByRole('button', { name: /Sideport storage/ })).toBeVisible()
    await userEvent.click(within(dialog).getByRole('button', { name: /GitHub release/ }))
    await userEvent.click(within(dialog).getByText('Add a repository'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Private selected repository' }))
    await expect(within(dialog).getByText(/Metadata:/)).toBeVisible()
    await expect(within(dialog).getByText(/Write access:/)).toBeVisible()
    await expect(within(dialog).getByText(/cannot push code, change settings, or read other private repositories/)).toBeVisible()

    const repository = within(dialog).getByRole('textbox', { name: 'GitHub repository' })
    await userEvent.clear(repository)
    await userEvent.type(repository, 'https://github.com/dragoshont/sideport')
    await expect(within(dialog).getByText('Enter one repository as owner/repository, without a URL.')).toBeVisible()
    await expect(within(dialog).getByRole('button', { name: 'Continue with GitHub' })).toBeDisabled()
    await userEvent.clear(repository)
    await userEvent.type(repository, 'dragoshont/sideport')

    await userEvent.click(within(dialog).getByRole('button', { name: 'Continue with GitHub' }))
    await waitFor(() => expect(within(dialog).getByText('GitHub source connected')).toBeVisible(), { timeout: 3_000 })
    await expect(within(dialog).getByRole('radio', { name: /Cert-Clock/ })).toBeVisible()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Import app' }))
    await waitFor(() => expect(canvas.getByRole('heading', { name: 'Install an app on your iPhone' })).toBeVisible())
    await expect(page.queryByRole('dialog', { name: 'Add an app' })).not.toBeInTheDocument()
  },
}

export const GlobalAddMobile390: Story = {
  name: 'Signed in - global Add at 390px',
  args: { data: fixtures, apiStatus: completedOnboardingStatus, initialRoute: 'activity' },
  parameters: {
    viewport: { defaultViewport: 'mobile1' },
    a11y: { test: 'todo' },
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const page = within(canvasElement.ownerDocument.body)
    const trigger = canvas.getByRole('button', { name: 'Add to Sideport' })
    await expect(trigger).toBeVisible()
    await expect(trigger.getBoundingClientRect().height).toBeGreaterThanOrEqual(44)
    await userEvent.click(trigger)
    const addChoices = page.getByRole('group', { name: 'Add to Sideport choices' })
    await expect(within(addChoices).getByRole('button', { name: /Add iPhone/ })).toBeVisible()
    await expect(within(addChoices).getByRole('button', { name: /Add app/ })).toBeVisible()
  },
}

export const AddAppRuntimeBound: Story = {
  name: 'Signed in - live GitHub releases are selectable',
  args: { data: fixtures, apiStatus: tokenRequiredStatus, initialRoute: 'apps', addAppServices: runtimeAddAppServices },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const page = within(canvasElement.ownerDocument.body)
    await userEvent.click(canvas.getByRole('button', { name: 'Add to Sideport' }))
    const addChoices = page.getByRole('group', { name: 'Add to Sideport choices' })
    await userEvent.click(within(addChoices).getByRole('button', { name: /Add app/ }))
    const dialog = page.getByRole('dialog', { name: 'Choose or add an app' })
    await userEvent.click(within(dialog).getByRole('button', { name: /GitHub release/ }))
    await waitFor(() => expect(within(dialog).getByRole('button', { name: /dragoshont\/sideport/ })).toBeVisible())
    await userEvent.click(within(dialog).getByRole('button', { name: /dragoshont\/sideport/ }))
    await waitFor(() => expect(within(dialog).getByRole('radio', { name: /Cert-Clock/ })).toBeVisible())
    await expect(within(dialog).getByRole('button', { name: 'Import app' })).toBeEnabled()
  },
}

export const AddAppSourcesLoadIndependently: Story = {
  name: 'Signed in - GitHub remains available when configured storage fails',
  args: { data: fixtures, apiStatus: tokenRequiredStatus, initialRoute: 'apps', addAppServices: githubAvailableWhenStorageFails },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const page = within(canvasElement.ownerDocument.body)
    await userEvent.click(canvas.getByRole('button', { name: 'Add to Sideport' }))
    await userEvent.click(within(page.getByRole('group', { name: 'Add to Sideport choices' })).getByRole('button', { name: /Add app/ }))
    const dialog = page.getByRole('dialog', { name: 'Choose or add an app' })
    await expect(await within(dialog).findByText(/Configured storage is unavailable/)).toBeVisible()
    const github = within(dialog).getByRole('button', { name: /GitHub release/ })
    await expect(github).toBeEnabled()
    await userEvent.click(github)
    await expect(await within(dialog).findByRole('button', { name: /dragoshont\/sideport/ })).toBeVisible()
  },
}

export const AddIPhoneRuntimeBound: Story = {
  name: 'Signed in - live iPhone enrollment polls to accepted',
  args: { data: fixtures, apiStatus: tokenRequiredStatus, initialRoute: 'devices', addIPhoneServices: runtimeAddIPhoneServices },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const page = within(canvasElement.ownerDocument.body)
    await userEvent.click(canvas.getByRole('button', { name: 'Add to Sideport' }))
    await userEvent.click(within(page.getByRole('group', { name: 'Add to Sideport choices' })).getByRole('button', { name: /Add iPhone/ }))
    const dialog = page.getByRole('dialog', { name: 'Add an iPhone' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Connect iPhone' }))
    await waitFor(() => expect(within(dialog).getByText('iPhone added to Sideport')).toBeVisible(), { timeout: 3_000 })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Choose an app' }))
    await expect(canvas.getByRole('heading', { name: 'Apps' })).toBeVisible()
    await expect(page.queryByRole('dialog', { name: 'Add an iPhone' })).not.toBeInTheDocument()
  },
}

export const AddIPhoneResumeAfterClose: Story = {
  name: 'Signed in - active iPhone enrollment survives close and reopen',
  args: {
    data: fixtures,
    apiStatus: tokenRequiredStatus,
    initialRoute: 'devices',
    addIPhoneServices: runtimeAddIPhoneServices,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const page = within(canvasElement.ownerDocument.body)
    const openDialog = async () => {
      await userEvent.click(canvas.getByRole('button', { name: 'Add to Sideport' }))
      await userEvent.click(within(page.getByRole('group', { name: 'Add to Sideport choices' })).getByRole('button', { name: /Add iPhone/ }))
      return page.getByRole('dialog', { name: 'Add an iPhone' })
    }
    const firstDialog = await openDialog()
    await userEvent.click(within(firstDialog).getByRole('button', { name: 'Connect iPhone' }))
    await userEvent.click(within(firstDialog).getByRole('button', { name: 'Close' }))
    const resumedDialog = await openDialog()
    await expect(within(resumedDialog).getByRole('button', { name: /Waiting for iPhone/ })).toBeDisabled()
    await waitFor(() => expect(within(resumedDialog).getByText('iPhone added to Sideport')).toBeVisible(), { timeout: 3_000 })
  },
}

export const AddIPhoneRecoveryRetry: Story = {
  name: 'Signed in - iPhone recovery verifies Trust without pairing again',
  args: {
    data: fixtures,
    apiStatus: { ...tokenRequiredStatus, baseUrl: 'storybook://device-enrollment-recovery' },
    initialRoute: 'devices',
    addIPhoneServices: enrollmentRecoveryServices,
  },
  play: async ({ canvasElement }) => {
    startEnrollmentRecoveryStory.mockClear()
    retryEnrollmentRecoveryStory.mockClear()
    readEnrollmentRecoveryStory.mockClear()
    const canvas = within(canvasElement)
    const page = within(canvasElement.ownerDocument.body)
    const openDialog = async () => {
      await userEvent.click(canvas.getByRole('button', { name: 'Add to Sideport' }))
      await userEvent.click(within(page.getByRole('group', { name: 'Add to Sideport choices' })).getByRole('button', { name: /Add iPhone/ }))
      return page.getByRole('dialog', { name: 'Add an iPhone' })
    }

    const firstDialog = await openDialog()
    await userEvent.click(within(firstDialog).getByRole('button', { name: 'Connect iPhone' }))
    await expect(await within(firstDialog).findByRole('button', { name: 'Check Trust and continue' })).toBeEnabled()
    await expect(within(firstDialog).getByText(/will not ask iOS to pair again/)).toBeVisible()
    await userEvent.click(within(firstDialog).getByRole('button', { name: 'Close' }))

    const resumedDialog = await openDialog()
    const recover = within(resumedDialog).getByRole('button', { name: 'Check Trust and continue' })
    await expect(recover).toBeEnabled()
    await userEvent.click(recover)
    await expect(startEnrollmentRecoveryStory).toHaveBeenCalledTimes(1)
    await expect(retryEnrollmentRecoveryStory).toHaveBeenCalledWith(enrollmentRecoverySource.operationId)
  },
}

export const CommandMenuAddActions: Story = {
  name: 'Signed in - command menu includes Add actions',
  args: { data: fixtures, apiStatus: completedOnboardingStatus, initialRoute: 'settings', initialCommandOpen: true },
  play: async ({ canvasElement }) => {
    const page = within(canvasElement.ownerDocument.body)
    const commandDialog = page.getByRole('dialog', { name: 'Search and commands' })
    const addIPhone = within(commandDialog).getByRole('button', { name: /Add iPhone/ })
    const addApp = within(commandDialog).getByRole('button', { name: /Add app/ })
    await waitFor(() => expect(addIPhone).toBeVisible())
    await waitFor(() => expect(addApp).toBeVisible())
    await userEvent.click(addIPhone)
    await expect(page.getByRole('dialog', { name: 'Add an iPhone' })).toBeVisible()
  },
}

export const MemberCapabilityBoundaries: Story = {
  name: 'Signed in - Member can add iPhone without Owner controls',
  args: { data: memberData, apiStatus: completedOnboardingStatus, initialRoute: 'settings' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const page = within(canvasElement.ownerDocument.body)

    await expect(canvas.queryByRole('heading', { name: 'Connect Apple data without over-trusting it' })).not.toBeInTheDocument()
    await userEvent.click(canvas.getByRole('button', { name: 'Add to Sideport' }))
    const addChoices = page.getByRole('group', { name: 'Add to Sideport choices' })
    await expect(within(addChoices).getByRole('button', { name: /Add iPhone/ })).toBeVisible()
    await expect(within(addChoices).queryByRole('button', { name: /Add app/ })).not.toBeInTheDocument()
  },
}
