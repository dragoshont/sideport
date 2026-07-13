export type SourceKind = 'live' | 'derived' | 'demo' | 'planned'
export type EvidenceOrigin = 'operator' | 'device' | 'apple' | 'artifact' | 'system' | 'operation'
export type OnboardingSetupState = 'in-progress' | 'complete'
export type OnboardingWorkflowStepId = 'server' | 'apple-signer' | 'device' | 'app' | 'install' | 'ready'
export type OnboardingWorkflowStepState = 'not-started' | 'action-required' | 'in-progress' | 'complete' | 'blocked'
export type HealthState = 'healthy' | 'warning' | 'blocked' | 'failed' | 'offline'
export type ConnectionState = 'usb' | 'wifi' | 'offline'
export type RenewalRisk = 'blocked' | 'due-now' | 'upcoming' | 'healthy' | 'unknown'
export type RenewalStatus = 'idle' | 'running' | 'queued' | 'failed' | 'blocked'
export type Severity = 'info' | 'warning' | 'error' | 'fatal'
export type IssueStatus = 'unresolved' | 'investigating' | 'resolved' | 'ignored'
export type CatalogAppStatus = 'ready' | 'missing' | 'invalid'
export type AppleAccessState = 'not-configured' | 'invalid-configuration' | 'read-only-verified' | 'partial' | 'blocked' | 'unavailable'
export type PersonalAppleState = 'not-configured' | 'credential-configured' | 'two-factor-required' | 'authenticated' | 'unavailable'

export interface SourceTagged<T> {
  value: T
  source: SourceKind
}

export interface OnboardingWorkflowAction {
  action: string
  label: string
}

export interface OnboardingWorkflowEvidence {
  id: string
  label: string
  detail: string
  source: SourceKind
  evidenceOrigin: EvidenceOrigin
  checkedAt: string
}

export interface OnboardingWorkflowStep {
  id: OnboardingWorkflowStepId
  state: OnboardingWorkflowStepState
  required: boolean
  source: SourceKind
  evidenceOrigin?: EvidenceOrigin
  checkedAt?: string
  activeOperationId?: string | null
  reason?: string
  nextAction?: OnboardingWorkflowAction
  evidence: OnboardingWorkflowEvidence[]
}

export interface OnboardingCompletionReceipt {
  schemaVersion: 2
  completedAt: string
  actor: {
    kind: string
    displayName: string
  }
  accountProfileId: string
  teamId: string
  deviceUdid: string
  registrationKey: {
    deviceUdid: string
    bundleId: string
  }
  verifiedOperationId: string
  schedulerSettingsVersion: string
  operationalCheckedAt: string
}

export interface OnboardingWorkflowV2 {
  schemaVersion: 2
  setupState: OnboardingSetupState
  readyNow: boolean
  completedAt?: string | null
  verifiedOperationId?: string | null
  nextAction?: (OnboardingWorkflowAction & { stepId: OnboardingWorkflowStepId }) | null
  steps: OnboardingWorkflowStep[]
}

export interface SystemOperationalCheck {
  id: string
  status: 'pass' | 'fail'
  source: SourceKind
  checkedAt: string
  scope: string
  affectedResources: string[]
  reason: string
  nextAction?: string | null
}

export interface SchedulerStatusSummary {
  enabled: boolean
  checkedAt?: string
  policy?: {
    mode: string
    evaluationInterval: string
    refreshLeadTime: string
    resignInterval?: string | null
    catchUp: string
    missedIntervals: string
  }
  nextEvaluationAt?: string | null
  lastEvaluation?: {
    evaluationId: string
    startedAt: string
    completedAt: string
    outcome: string
    dueCount: number
    queuedCount: number
    blockedCount: number
    skippedCount: number
  } | null
  dueCount?: number
  queuedCount?: number
  concurrency?: {
    maxRunning: number
    lockState: string
    operationId?: string | null
  }
  historyRetention?: { maxEvaluations: number }
  source: SourceKind
}

export interface SystemStatus {
  operational: boolean
  checkedAt?: string
  checks: SystemOperationalCheck[]
  api: { ok: boolean; source: SourceKind }
  ready: {
    ready: boolean
    source: SourceKind
    checks: {
      anisette: { ok: boolean; error?: string | null; source: SourceKind }
      signer: { ok: boolean; path: string; source: SourceKind }
    }
  }
  apiAuth: { configured: boolean; source: SourceKind }
  scheduler: SchedulerStatusSummary
  observability: { exporter: string; connected: boolean; source: SourceKind }
}

export interface DeviceSummary {
  udid: string
  name: string
  productType: string
  osVersion: string
  connection: ConnectionState
  lastSeenAt: SourceTagged<string>
  hasDurableLastSeen?: boolean
  currentPollAt?: SourceTagged<string>
  lastSeenSource?: string
  inventoryState?: string
  acceptedAt?: SourceTagged<string>
  acceptedBy?: string
  enrollmentOperationId?: string
  trustState?: string
  trustReason?: string
  lockdownCheckedAt?: SourceTagged<string>
  usableForInstall?: boolean
  supportedForFirstInstall?: boolean
  health: HealthState
  teamId: string
  appSlotsUsed: number
  installedAppCount: number
  unmanagedAppCount: number
  nearestExpiryAt?: SourceTagged<string>
  blocker?: string
}

export interface InstalledAppSummary {
  bundleId: string
  deviceUdid: string
  name: string
  version: string
  signatureExpiresAt?: SourceTagged<string>
  managedBySideport: boolean
  source: SourceKind
}

export interface RegisteredAppSummary {
  bundleId: string
  deviceUdid: string
  appleId: string
  teamId: string
  expiresAt?: SourceTagged<string>
  timeUntilExpiry?: SourceTagged<string>
  lastSucceeded?: boolean | null
  lastError?: string | null
  lifecycle?: 'pending-install' | 'active'
  lastVerifiedOperationId?: string | null
  displayName: SourceTagged<string>
  version: SourceTagged<string>
  iconTone: 'blue' | 'green' | 'amber' | 'red' | 'slate'
}

export interface CatalogAppSummary {
  id: string
  name: string
  purpose: string
  expectedBundleId: string
  suggestedIpaPath?: string
  catalogVersion?: number
  artifactSources?: Array<{
    kind: string
    label: string
    repository?: string
    releaseTag?: string
    assetName?: string
  }>
  versionLabel: string
  status: CatalogAppStatus
  statusLabel: string
  source: SourceKind
  iconTone: RegisteredAppSummary['iconTone']
  notes: string[]
  sizeBytes?: number
  sha256?: string
  hasEmbeddedProfile: boolean
  signatureExpiresAt?: string
  lastInspectedAt?: string
  icon?: string
}

export interface WorkspaceRoleSummary {
  id: WorkspaceRole | string
  label: string
  capabilities: string[]
}

export interface RenewalItem {
  id: string
  deviceUdid: string
  bundleId: string
  teamId: string
  risk: RenewalRisk
  status: RenewalStatus
  expiresAt?: string
  blocker?: string
  operationId?: string
  source: SourceKind
}

export interface DiagnosticIssue {
  id: string
  category: string
  severity: Severity
  status: IssueStatus
  deviceUdid?: string
  bundleId?: string
  firstSeenAt: string
  lastSeenAt: string
  operationId: string
  traceId: string
  spanSummary: Array<{ name: string; durationMs: number; state: 'ok' | 'warning' | 'failed' }>
  logSnippet: string
  source: SourceKind
}

export interface ActivityEvent {
  id: string
  at: string
  actor: string
  title: string
  detail: string
  state: 'ok' | 'warning' | 'failed' | 'info'
  source: SourceKind
}

export interface OperationLogEntry {
  id: string
  at: string
  level: string
  category: string
  eventId?: number
  message: string
  exceptionType?: string | null
  exceptionMessage?: string | null
  source: SourceKind
}

export interface OperationStageSummary {
  id: string
  label: string
  status: 'pending' | 'running' | 'succeeded' | 'failed' | 'blocked'
  startedAt?: string
  completedAt?: string
  message: string
  error?: string | null
}

export interface OperationSummary {
  operationId: string
  type: 'refresh' | string
  status: 'running' | 'blocked' | 'succeeded' | 'failed' | string
  createdAt: string
  updatedAt: string
  completedAt?: string | null
  deviceUdid: string
  bundleId: string
  actor: string
  stages: OperationStageSummary[]
  error?: string | null
  cancelable: boolean
  retryable: boolean
  rerunnable: boolean
  parentOperationId?: string | null
  finishOnboarding?: boolean
  source: SourceKind
}

export interface AppleAccessCapabilitySummary {
  id: string
  label: string
  endpoint: string
  state: 'verified' | 'not-checked' | 'unauthorized' | 'denied' | 'rate-limited' | 'failed'
  httpStatus?: number
  detail: string
  count?: number
  source: SourceKind
}

export interface AppleAccessSummary {
  connector: string
  state: AppleAccessState
  secretCustody: string
  keyIdSuffix?: string | null
  issuerIdSuffix?: string | null
  message: string
  capabilities: AppleAccessCapabilitySummary[]
  source: SourceKind
}

export interface PersonalAppleTeamSummary {
  teamId: string
  name: string
  type: string
}

export interface PersonalAppleSummary {
  connector: string
  state: PersonalAppleState
  secretCustody: string
  credentialSource?: string
  accountProfileId?: string | null
  credentialEntry?: {
    supported: boolean
    allowedNow: boolean
    blockedReason?: { code: string; message: string } | null
  } | null
  appleIdHint?: string | null
  message: string
  pendingChallengeId?: string | null
  pendingChallengeKind?: string | null
  pendingChallengeExpiresAt?: string | null
  selectedTeamId?: string | null
  teamValidatedAt?: string | null
  lastAuthenticatedAt?: string | null
  authValidatedAt?: string | null
  teams: PersonalAppleTeamSummary[]
  source: SourceKind
}

export type WorkspaceRole = 'owner' | 'family'
export type MemberStatus = 'active' | 'suspended' | 'offboarded'

export interface WorkspaceMember {
  id: string
  name: string
  email: string
  role: WorkspaceRole
  status: MemberStatus
  lastActiveAt?: string
  invitedAt?: string
  source: SourceKind
}

export interface WorkspaceSummary {
  name: string
  authMode: string
  authDelegated: boolean
  roleEnforcement?: string
  supportsUserAdministration?: boolean
  currentMember?: WorkspaceMember
  members: WorkspaceMember[]
  roles?: WorkspaceRoleSummary[]
  capabilities?: Record<string, boolean>
  source: SourceKind
}

export interface SideportReadModel {
  system: SystemStatus
  devices: DeviceSummary[]
  catalogApps: CatalogAppSummary[]
  appleAccess: AppleAccessSummary
  personalApple: PersonalAppleSummary
  installedApps: InstalledAppSummary[]
  apps: RegisteredAppSummary[]
  renewals: RenewalItem[]
  operations: OperationSummary[]
  issues: DiagnosticIssue[]
  activity: ActivityEvent[]
  logs: OperationLogEntry[]
  workspace: WorkspaceSummary
}

export const runtimeEmptyData: SideportReadModel = {
  system: {
    operational: false,
    checks: [],
    api: { ok: false, source: 'live' },
    ready: {
      ready: false,
      source: 'live',
      checks: {
        anisette: { ok: false, error: 'Waiting for API snapshot', source: 'live' },
        signer: { ok: false, path: 'Unknown', source: 'live' },
      },
    },
    apiAuth: { configured: false, source: 'live' },
    scheduler: { enabled: false, source: 'planned' },
    observability: { exporter: 'OTLP not connected', connected: false, source: 'planned' },
  },
  devices: [],
  catalogApps: [],
  appleAccess: {
    connector: 'app-store-connect-jwt',
    state: 'unavailable',
    secretCustody: 'server-configured-key-reference',
    message: 'Waiting for Apple Access status.',
    capabilities: [],
    source: 'live',
  },
  personalApple: {
    connector: 'personal-apple-id',
    state: 'unavailable',
    secretCustody: 'host-environment-or-secret-store',
    message: 'Waiting for Personal Apple ID connector status.',
    teams: [],
    source: 'live',
  },
  installedApps: [],
  apps: [],
  renewals: [],
  operations: [],
  issues: [],
  activity: [],
  logs: [],
  workspace: {
    name: 'Sideport workspace',
    authMode: 'Reverse proxy (not yet reported)',
    authDelegated: true,
    roleEnforcement: 'planned',
    supportsUserAdministration: false,
    members: [],
    roles: [],
    capabilities: {},
    source: 'planned',
  },
}
