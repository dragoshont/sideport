import { useQuery } from '@tanstack/react-query'
import { runtimeEmptyData, type ActivityEvent, type AppleAccessCapabilitySummary, type AppleAccessState, type AppleAccessSummary, type CatalogAppStatus, type CatalogAppSummary, type ConnectionState, type DiagnosticIssue, type EvidenceOrigin, type HealthState, type InstalledAppSummary, type MemberStatus, type OnboardingCompletionReceipt as WorkflowCompletionReceipt, type OnboardingWorkflowAction, type OnboardingWorkflowEvidence, type OnboardingWorkflowStep, type OnboardingWorkflowStepId, type OnboardingWorkflowStepState, type OnboardingWorkflowV2, type OperationLogEntry, type OperationStageSummary, type OperationSummary, type PersonalAppleState, type PersonalAppleSummary, type PersonalAppleTeamSummary, type RegisteredAppSummary, type RenewalItem, type RenewalRisk, type RenewalStatus, type SideportReadModel, type SourceKind, type SystemOperationalCheck, type SystemStatus, type WorkspaceRole, type WorkspaceSummary } from '../data/sideportTypes'
import { compactUdid } from '../lib/format'

const DEFAULT_API_BASE_URL = '/sideport-api'
const DEFAULT_REFRESH_INTERVAL_MS = 15_000
const REQUEST_TIMEOUT_MS = 4_500
const SESSION_TOKEN_KEY = 'sideport.apiToken'
const CSRF_HEADER = 'X-Sideport-CSRF'
const CSRF_ISSUER_ROUTES = new Set([
  '/api/me',
  '/api/apple-access/personal/status',
])
const csrfByApiBase = new Map<string, string>()

export interface AdminDataStatus {
  mode: 'live' | 'partial' | 'unavailable' | 'demo'
  baseUrl: string
  lastUpdatedAt?: string
  message: string
  canMutate: boolean
  onboarding?: OnboardingStatus
}

export type OnboardingStepState = 'complete' | 'pending' | 'warning' | 'blocked'

export interface OnboardingStep {
  id: string
  label: string
  description: string
  state: OnboardingStepState
  surface: 'portal' | 'iphone'
  required: boolean
  settingsPath?: string | null
  detail?: string | null
  source: SourceKind
}

export interface OnboardingStatus {
  firstRunComplete: boolean
  schedulerEnabled: boolean
  steps: OnboardingStep[]
  setupState?: 'in-progress' | 'complete'
  selectedCatalogAppId?: string | null
  activeInstallOperationId?: string | null
  completionReceipt?: WorkflowCompletionReceipt | null
  workflow?: OnboardingWorkflowV2 | null
}

export interface ApiConfig {
  baseUrl: string
  token?: string
  canMutate: boolean
}

export interface AppRegistrationPayload {
  bundleId: string
  deviceUdid: string
  appleId: string
  teamId: string
  inputIpaPath: string
}

export interface PendingAppRegistrationPayload {
  catalogAppId: string
  deviceUdid: string
  accountProfileId: string
  lifecycle: 'pending-install'
}

export interface AppRegistrationDto {
  bundleId?: string
  appleIdHint?: string | null
  teamId?: string
  deviceUdid?: string
  lifecycle?: string
  catalogAppId?: string | null
  createdAt?: string | null
  activatedAt?: string | null
  lastVerifiedOperationId?: string | null
}

export interface CatalogInspectPayload {
  ipaPath: string
  id?: string
  name?: string
  purpose?: string
}

export interface DeviceEnrollmentPayload {
  idempotencyKey: string
  deviceUdid?: string
}

export interface InstallOperationPayload {
  deviceUdid: string
  bundleId: string
  catalogAppId: string
  accountProfileId: string
  preflightId: string
  planVersion: string
  finishOnboarding: boolean
  confirmedPlannedMutations: boolean
  idempotencyKey: string
}

export interface InstallPreflightPayload {
  deviceUdid: string
  bundleId: string
  catalogAppId: string
  accountProfileId: string
  finishOnboarding: boolean
}

export interface OnboardingCompletionPayload {
  verifiedOperationId: string
  idempotencyKey: string
}

export interface CatalogArtifactSourceDto {
  kind: string
  label: string
  repository?: string | null
  releaseTag?: string | null
  assetName?: string | null
}

export interface CatalogAppV2Dto {
  id: string
  catalogVersion: number
  name: string
  purpose: string
  bundleId: string
  version: string | null
  shortVersion: string | null
  source: string
  status: string
  sizeBytes: number | null
  sha256: string | null
  hasEmbeddedProfile: boolean
  signatureExpiresAt: string | null
  artifactSources: CatalogArtifactSourceDto[]
  lastInspectedAt: string | null
  notes: string[]
  icon?: string | null
}

export interface CatalogImportRootDto {
  id: string
  label: string
  available: boolean
  source: string
}

export interface CatalogRootImportPayload {
  rootId: string
  relativePath: string
  id?: string
  name?: string
  purpose?: string
  expectedCatalogVersion?: number
  idempotencyKey?: string
}

export interface CatalogUploadV2Payload {
  id?: string
  name?: string
  purpose?: string
  idempotencyKey?: string
  expectedCatalogVersion?: number
}

export interface GitHubPermissionSummaryDto {
  metadata: string
  contents: string
}

export interface GitHubProviderCapabilityDto {
  kind: string
  supported: boolean
  allowedNow: boolean
  blockedReason: string | null
  permissions: GitHubPermissionSummaryDto
}

export interface GitHubSourceDto {
  id: string
  repository: string
  visibility: string
  provider: string
  allowPrereleases: boolean
  permissions: GitHubPermissionSummaryDto
  status: string
}

export interface GitHubSourcesDto {
  capability: GitHubProviderCapabilityDto
  sources: GitHubSourceDto[]
}

export interface GitHubConnectionPayload {
  repository: string
  visibility: string
  idempotencyKey: string
}

export interface GitHubConnectionDto {
  id: string
  repository: string
  visibility: string
  status: string
  permissions: GitHubPermissionSummaryDto
  expiresAt: string | null
  sourceId: string | null
  authorizationUrl: string | null
  error: string | null
}

export interface GitHubReleaseAssetDto {
  assetId: number
  name: string
  sizeBytes: number
  updatedAt: string | null
  digest: string | null
  importable: boolean
}

export interface GitHubReleaseDto {
  releaseId: number
  tag: string
  name: string
  publishedAt: string | null
  updatedAt: string | null
  prerelease: boolean
  assets: GitHubReleaseAssetDto[]
}

export interface GitHubReleasePageDto {
  sourceId: string
  repository: string
  page: number
  releases: GitHubReleaseDto[]
}

export interface GitHubCatalogImportPayload {
  sourceId: string
  releaseId: number
  assetId: number
  idempotencyKey: string
  expectedDigest?: string
  catalogId?: string
  expectedCatalogVersion?: number
}

export interface PersonalAppleSignInPayload {
  appleId: string
}

export interface PersonalAppleConnectPayload {
  appleId: string
  password: string
}

export interface PersonalAppleTwoFactorPayload {
  challengeId: string
  code: string
}

export interface PersonalAppleTeamSelectionPayload {
  accountProfileId: string
  teamId: string
}

export interface PersonalAppleSigningPreflightDto {
  preflightId: string
  expiresAt: string
  accountProfileId: string
  teamId: string
  localIdentity: { state: string; expiresAt?: string | null; serialSuffix?: string | null }
  appleCertificates: Array<{ id: string; serialSuffix?: string | null; expiresAt?: string | null }>
  impact: 'reuse' | 'mint' | 'replace-existing' | 'unknown' | string
  requiresAcknowledgement: boolean
  inventoryVersion: string
  registrationCount: number
  deviceCount: number
  profileCount: number
}

export interface PersonalAppleCutoverPayload {
  preflightId: string
  inventoryVersion: string
  acknowledgedCertificateIds: string[]
  acknowledgedImpactCodes: string[]
  idempotencyKey: string
}

export interface AppleAccountReplacementCandidateDto {
  candidateId: string
  state: 'two-factor-required' | 'validated' | string
  appleIdHint: string
  accountProfileId: string
  teams: Array<{ teamId: string; name: string; type: string }>
  expiresAt: string
  challengeKind?: string | null
}

export interface RefreshResultDto {
  success?: boolean
  bundleId?: string
  expiresAt?: string | null
  error?: string | null
}

export interface RefreshOperationDto {
  operationId: string
  status: string
  result?: RefreshResultDto | null
  error?: { code?: string; message?: string; detail?: string | null } | null
}

export interface OperationPreflightDto {
  preflightId?: string
  expiresAt?: string
  planVersion?: string
  ready: boolean
  target?: { deviceUdid?: string; bundleId?: string }
  blockers: Array<{ code: string; message: string; detail?: string | null }>
  warnings: Array<{ code: string; message: string; detail?: string | null }>
  plannedMutations: string[]
  scarceLimits: Array<{ code: string; label: string; used: number; limit: number }>
  requiresConfirmation: boolean
  source?: SourceKind
  checks?: Array<{
    id?: string
    group?: string
    label?: string
    status?: string
    message?: string
    detail?: string | null
  }>
  groupedChecks?: Record<string, Array<{
    id?: string
    label?: string
    status?: string
    message?: string
    detail?: string | null
  }>>
}

export class SideportApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly data?: unknown

  constructor(message: string, status: number, code?: string, data?: unknown) {
    super(message)
    this.name = 'SideportApiError'
    this.status = status
    this.code = code
    this.data = data
  }
}

interface ApiResult<T> {
  ok: boolean
  source: SourceKind
  data?: T
  error?: string
  status?: number
}

interface ReadyResponse {
  ready: boolean
  checks: {
    anisette: { ok: boolean; error?: string | null }
    signer: { ok: boolean; path: string }
  }
}

interface SystemStatusDto {
  operational?: boolean
  checkedAt?: string
  checks?: SystemOperationalCheckDto[]
}

interface SystemOperationalCheckDto {
  id?: string
  status?: string
  source?: string
  checkedAt?: string
  scope?: string
  affectedResources?: string[]
  reason?: string
  nextAction?: string | null
}

interface HealthResponse {
  ok: boolean
}

export interface ReachableDeviceDto {
  udid?: string
  name?: string
  productType?: string
  osVersion?: string
  connection?: string | number
}

interface KnownDeviceDto {
  udid?: string
  displayName?: string
  productType?: string | null
  osVersion?: string | null
  connection?: string
  firstSeenAt?: string
  lastSeenAt?: string | null
  lastSeenSource?: string
  currentPollAt?: string | null
  inventoryState?: string
  acceptedAt?: string | null
  acceptedBy?: string | null
  enrollmentOperationId?: string | null
  trustState?: string
  trustReason?: string | null
  lockdownCheckedAt?: string | null
  usableForInstall?: boolean
  supportedForFirstInstall?: boolean
  health?: { state?: string; reason?: string; source?: string; checkedAt?: string; nextAction?: string | null }
  appSlots?: { used?: number; limit?: number; source?: string }
  owner?: string | null
  notes?: string | null
  source?: string
}

interface RegisteredAppDto {
  bundleId?: string
  deviceUdid?: string
  appleId?: string
  teamId?: string
  expiresAt?: string | null
  timeUntilExpiry?: string | null
  lastSucceeded?: boolean | null
  lastError?: string | null
  lifecycle?: string | null
  lastVerifiedOperationId?: string | null
}

interface CatalogAppDto {
  id?: string
  name?: string
  purpose?: string
  bundleId?: string
  ipaPath?: string
  version?: string | null
  shortVersion?: string | null
  sizeBytes?: number | null
  sha256?: string | null
  hasEmbeddedProfile?: boolean
  signatureExpiresAt?: string | null
  source?: string
  status?: string
  lastInspectedAt?: string | null
  notes?: string[]
}

interface InstalledAppDto {
  bundleId?: string
  name?: string
  version?: string
  signatureExpiresAt?: string | null
  deviceUdid?: string
}

interface AppleAccessStatusDto {
  connector?: string
  state?: string
  secretCustody?: string
  keyIdSuffix?: string | null
  issuerIdSuffix?: string | null
  message?: string
  capabilities?: AppleAccessCapabilityDto[]
}

interface AppleAccessCapabilityDto {
  id?: string
  label?: string
  endpoint?: string
  state?: string
  httpStatus?: number | null
  detail?: string
  count?: number | null
}

interface PersonalAppleStatusDto {
  connector?: string
  state?: string
  secretCustody?: string
  credentialSource?: string
  accountProfileId?: string | null
  credentialEntry?: {
    supported?: boolean
    allowedNow?: boolean
    blockedReason?: { code?: string; message?: string } | null
  } | null
  appleIdHint?: string | null
  message?: string
  pendingChallengeId?: string | null
  pendingChallengeKind?: string | null
  pendingChallengeExpiresAt?: string | null
  selectedTeamId?: string | null
  teamValidatedAt?: string | null
  lastAuthenticatedAt?: string | null
  authValidatedAt?: string | null
  teams?: PersonalAppleTeamDto[]
}

interface PersonalAppleTeamDto {
  teamId?: string
  name?: string
  type?: string
}

interface AnisetteInfoDto {
  deviceId?: string
  clientInfo?: string
  [key: string]: unknown
}

interface OnboardingStatusDto {
  firstRunComplete?: boolean
  schedulerEnabled?: boolean
  steps?: OnboardingStepDto[]
  setupState?: string
  selectedCatalogAppId?: string | null
  activeInstallOperationId?: string | null
  completionReceipt?: {
    schemaVersion?: number
    completedAt?: string
    actor?: { kind?: string; displayName?: string }
    accountProfileId?: string
    teamId?: string
    verifiedOperationId?: string
    deviceUdid?: string
    registrationKey?: {
      deviceUdid?: string
      bundleId?: string
    } | null
    schedulerSettingsVersion?: string
    operationalCheckedAt?: string
  } | null
  workflow?: OnboardingWorkflowDto | null
}

interface OnboardingWorkflowDto {
  schemaVersion?: number
  setupState?: string
  readyNow?: boolean
  completedAt?: string | null
  verifiedOperationId?: string | null
  nextAction?: (OnboardingWorkflowActionDto & { stepId?: string }) | null
  steps?: OnboardingWorkflowStepDto[]
}

interface OnboardingWorkflowActionDto {
  action?: string
  label?: string
}

interface OnboardingWorkflowEvidenceDto {
  id?: string
  label?: string
  detail?: string
  source?: string
  evidenceOrigin?: string
  checkedAt?: string
}

interface OnboardingWorkflowStepDto {
  id?: string
  state?: string
  required?: boolean
  source?: string
  evidenceOrigin?: string
  checkedAt?: string
  activeOperationId?: string | null
  reason?: string
  nextAction?: OnboardingWorkflowActionDto | null
  evidence?: OnboardingWorkflowEvidenceDto[]
}

interface OperationLogDto {
  id?: string | number
  at?: string
  level?: string
  category?: string
  eventId?: number
  message?: string
  exceptionType?: string | null
  exceptionMessage?: string | null
}

export interface OperationRecordDto {
  operationId?: string
  type?: string
  status?: string
  createdAt?: string
  startedAt?: string | null
  updatedAt?: string
  completedAt?: string | null
  actor?: { kind?: string; displayName?: string }
  idempotencyKey?: string | null
  attempt?: number
  target?: {
    deviceUdid?: string | null
    bundleId?: string | null
    appleId?: string | null
    teamId?: string | null
    kind?: string | null
    catalogAppId?: string | null
    accountProfileId?: string | null
    catalogVersion?: number | null
    version?: string | null
    catalogSha256?: string | null
  }
  stages?: OperationStageDto[]
  result?: {
    success?: boolean
    bundleId?: string | null
    expiresAt?: string | null
    error?: string | null
    nextEvaluationAt?: string | null
    schedulerSettingsVersion?: string | null
    version?: string | null
    safeToRerun?: boolean | null
    reconciledOperationId?: string | null
    deviceEnrollment?: {
      selectedDeviceUdid?: string | null
      inventoryState?: string
      acceptedAt?: string | null
      reason?: string | null
    } | null
  } | null
  error?: { message?: string; code?: string; detail?: string | null } | null
  cancelable?: boolean
  retryable?: boolean
  rerunnable?: boolean
  correlationId?: string
  parentOperationId?: string | null
  source?: string
  expiresAt?: string | null
  candidateDevices?: Array<{
    udidSuffix?: string
    name?: string
    productType?: string | null
    osVersion?: string | null
    connection?: string
  }> | null
  devicePairingRequestedAt?: string | null
  installIntent?: {
    finishOnboarding?: boolean
  } | null
}

export interface SchedulerStatusDto {
  enabled: boolean
  checkedAt: string
  policy: {
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
  dueCount: number
  queuedCount: number
  concurrency: {
    maxRunning: number
    lockState: string
    operationId?: string | null
  }
  historyRetention: { maxEvaluations: number }
  source?: string
}

interface DiagnosticIssueDto {
  issueId?: string
  category?: string
  severity?: string
  status?: string
  affected?: { deviceUdid?: string | null; bundleId?: string | null }
  firstSeenAt?: string
  lastSeenAt?: string
  occurrenceCount?: number
  lastOperationId?: string | null
  correlationId?: string
  evidence?: Array<{ type?: string; label?: string; message?: string; source?: string; operationId?: string | null; stageId?: string | null }>
  remediation?: string
  source?: string
}

export interface OperationStageDto {
  id?: string
  label?: string
  status?: string
  startedAt?: string | null
  completedAt?: string | null
  message?: string
  error?: { message?: string; code?: string; detail?: string | null } | null
}

interface RenewalItemDto {
  id?: string
  deviceUdid?: string
  bundleId?: string
  teamId?: string
  risk?: string
  status?: string
  expiresAt?: string | null
  blocker?: string | null
  operationId?: string | null
  source?: string
}

interface OnboardingStepDto {
  id?: string
  label?: string
  description?: string
  state?: string
  surface?: string
  required?: boolean
  settingsPath?: string | null
  detail?: string | null
}

interface WorkspaceMemberDto {
  id?: string
  name?: string
  email?: string
  role?: string
  status?: string
  lastActiveAt?: string | null
  invitedAt?: string | null
  source?: string
}

interface WorkspaceRoleDto {
  id?: string
  label?: string
  capabilities?: string[]
}

interface WorkspaceDto {
  name?: string
  authMode?: string
  authDelegated?: boolean
  roleEnforcement?: string
  supportsUserAdministration?: boolean
  currentMember?: WorkspaceMemberDto
  members?: WorkspaceMemberDto[]
  roles?: WorkspaceRoleDto[]
  capabilities?: Record<string, boolean | { allowed?: boolean }>
}

function toWorkspace(dto: WorkspaceDto): WorkspaceSummary {
  const validRoles: WorkspaceRole[] = ['owner', 'family']
  const validStatuses: MemberStatus[] = ['active', 'suspended', 'offboarded']
  return {
    name: dto.name?.trim() || 'Sideport workspace',
    authMode: dto.authMode?.trim() || 'Reverse proxy',
    authDelegated: dto.authDelegated ?? true,
    roleEnforcement: dto.roleEnforcement,
    supportsUserAdministration: dto.supportsUserAdministration ?? false,
    currentMember: dto.currentMember ? toWorkspaceMember(dto.currentMember, 'current-member') : undefined,
    roles: (dto.roles ?? []).map((role) => ({ id: role.id ?? 'viewer', label: role.label ?? role.id ?? 'Viewer', capabilities: role.capabilities ?? [] })),
    capabilities: Object.fromEntries(Object.entries(dto.capabilities ?? {}).map(([key, value]) => [key, typeof value === 'boolean' ? value : value.allowed === true])),
    source: 'live',
    members: (dto.members ?? []).map((member, index) => toWorkspaceMember(member, `member-${index}`)),
  }

  function toWorkspaceMember(member: WorkspaceMemberDto, fallbackId: string) {
    return {
      id: member.id ?? fallbackId,
      name: member.name?.trim() || member.email?.trim() || 'Unknown member',
      email: member.email?.trim() || '',
      role: validRoles.includes((member.role ?? '') as WorkspaceRole) ? (member.role as WorkspaceRole) : 'family',
      status: validStatuses.includes((member.status ?? '') as MemberStatus) ? (member.status as MemberStatus) : 'active',
      lastActiveAt: member.lastActiveAt ?? undefined,
      invitedAt: member.invitedAt ?? undefined,
      source: normalizeSource(member.source),
    }
  }
}

function plannedWorkspace(error?: string): WorkspaceSummary {
  return {
    ...runtimeEmptyData.workspace,
    name: error ? `Workspace API planned (${error})` : runtimeEmptyData.workspace.name,
    authMode: 'Delegated auth / planned workspace API',
    authDelegated: true,
    roleEnforcement: 'planned',
    supportsUserAdministration: false,
    members: [],
    roles: [],
    capabilities: {},
    source: 'planned',
  }
}

interface ApiSnapshot {
  fetchedAt: string
  config: ApiConfig
  health: ApiResult<HealthResponse>
  ready: ApiResult<ReadyResponse>
  systemStatus: ApiResult<SystemStatusDto>
  devices: ApiResult<ReachableDeviceDto[]>
  knownDevices: ApiResult<KnownDeviceDto[]>
  catalog: ApiResult<CatalogAppV2Dto[]>
  appleAccess: ApiResult<AppleAccessStatusDto>
  personalApple: ApiResult<PersonalAppleStatusDto>
  installedApps: ApiResult<InstalledAppDto[]>
  apps: ApiResult<RegisteredAppDto[]>
  anisette: ApiResult<AnisetteInfoDto>
  onboarding: ApiResult<OnboardingStatusDto>
  scheduler: ApiResult<SchedulerStatusDto>
  logs: ApiResult<OperationLogDto[]>
  operations: ApiResult<OperationRecordDto[]>
  renewals: ApiResult<RenewalItemDto[]>
  diagnosticIssues: ApiResult<DiagnosticIssueDto[]>
  workspace: ApiResult<WorkspaceDto>
}

export function getSideportApiConfig(): ApiConfig {
  const env = import.meta.env
  return {
    baseUrl: env.VITE_SIDEPORT_API_URL?.trim() || DEFAULT_API_BASE_URL,
    token: readSessionApiToken() || env.VITE_SIDEPORT_API_TOKEN?.trim() || undefined,
    // Authentication and authorization stay server-authoritative. This flag is
    // an explicit emergency/build-time kill switch, not a second auth system.
    canMutate: env.VITE_SIDEPORT_ENABLE_MUTATIONS?.trim().toLowerCase() !== 'false',
  }
}

export function getStoredSideportApiToken(): string {
  return readSessionApiToken() ?? ''
}

export function saveSideportApiToken(token: string): void {
  const trimmed = token.trim()
  try {
    if (trimmed) window.sessionStorage.setItem(SESSION_TOKEN_KEY, trimmed)
    else window.sessionStorage.removeItem(SESSION_TOKEN_KEY)
  } catch {
    // Browser storage can be unavailable in hardened/private contexts; the next
    // request will simply continue without a session token.
  }
}

function readSessionApiToken(): string | undefined {
  try {
    return window.sessionStorage.getItem(SESSION_TOKEN_KEY)?.trim() || undefined
  } catch {
    return undefined
  }
}

export function useSideportAdminData(apiTokenRevision = 0) {
  const config = getSideportApiConfig()
  return useQuery({
    queryKey: ['sideport-admin-data', config.baseUrl, Boolean(config.token), config.canMutate, apiTokenRevision],
    queryFn: async () => buildAdminData(await fetchSnapshot(config)),
    placeholderData: buildUnavailableData(config, 'Connecting to Sideport API...'),
    refetchInterval: DEFAULT_REFRESH_INTERVAL_MS,
    retry: 1,
  })
}

/** Refresh the OIDC-only request token without exposing it to React state or callers. */
export async function refreshPersonalAppleRequestSecurity(config = getSideportApiConfig()): Promise<void> {
  const result = await requestJson<PersonalAppleStatusDto>(config, '/api/apple-access/personal/status', true)
  if (!result.ok) throw new Error(result.error ?? 'Sideport could not refresh Apple request security.')
}

async function fetchSnapshot(config: ApiConfig): Promise<ApiSnapshot> {
  const fetchedAt = new Date().toISOString()
  const [, health, ready, systemStatus, devices, knownDevices, catalog, appleAccess, personalApple, apps, anisette, onboarding, scheduler, logs, operations, renewals, diagnosticIssues, workspace] = await Promise.all([
    requestJson<unknown>(config, '/api/me', true),
    requestJson<HealthResponse>(config, '/healthz', false),
    requestJson<ReadyResponse>(config, '/readyz', false),
    requestJson<SystemStatusDto>(config, '/api/system/status', true),
    requestJson<ReachableDeviceDto[]>(config, '/api/devices', true),
    requestJson<KnownDeviceDto[]>(config, '/api/devices/known', true),
    requestJson<CatalogAppV2Dto[]>(config, '/api/v2/catalog/apps', true),
    requestJson<AppleAccessStatusDto>(config, '/api/apple-access/status', true),
    requestJson<PersonalAppleStatusDto>(config, '/api/apple-access/personal/status', true),
    requestJson<RegisteredAppDto[]>(config, '/api/apps', true),
    requestJson<AnisetteInfoDto>(config, '/api/anisette/info', true),
    requestJson<OnboardingStatusDto>(config, '/api/onboarding/status', true),
    requestJson<SchedulerStatusDto>(config, '/api/scheduler/status', true),
    requestJson<OperationLogDto[]>(config, '/api/logs?limit=80', true),
    requestJson<OperationRecordDto[]>(config, '/api/operations?limit=25', true),
    requestJson<RenewalItemDto[]>(config, '/api/renewals', true),
    requestJson<DiagnosticIssueDto[]>(config, '/api/diagnostics/issues', true),
    requestJson<WorkspaceDto>(config, '/api/workspace', true),
  ])
  const installedApps = await fetchInstalledApps(config, devices)

  return { fetchedAt, config, health, ready, systemStatus, devices, knownDevices, catalog, appleAccess, personalApple, installedApps, apps, anisette, onboarding, scheduler, logs, operations, renewals, diagnosticIssues, workspace }
}

async function fetchInstalledApps(config: ApiConfig, devices: ApiResult<ReachableDeviceDto[]>): Promise<ApiResult<InstalledAppDto[]>> {
  if (!devices.ok || !devices.data?.length) return { ok: true, source: 'live', data: [] }
  const reachableDevices = devices.data.filter((device) => device.udid)
  const results = await Promise.all(reachableDevices.map(async (device) => {
    const result = await requestJson<InstalledAppDto[]>(config, `/api/devices/${encodeURIComponent(device.udid!)}/installed-apps`, true)
    return { deviceUdid: device.udid!, result }
  }))

  const data = results.flatMap(({ deviceUdid, result }) =>
    result.ok && result.data ? result.data.map((app) => ({ ...app, deviceUdid })) : [])
  const failed = results.filter(({ result }) => !result.ok)
  if (!failed.length) return { ok: true, source: 'live', data }

  return {
    ok: false,
    source: 'live',
    data,
    status: failed[0]?.result.status,
    error: failed.map(({ deviceUdid, result }) => `${compactUdid(deviceUdid)}: ${result.error ?? 'unavailable'}`).join('; '),
  }
}

async function requestJson<T>(config: ApiConfig, path: string, protectedApi: boolean): Promise<ApiResult<T>> {
  const controller = new AbortController()
  const timeout = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  const headers: HeadersInit = { Accept: 'application/json' }

  if (protectedApi && config.token) headers.Authorization = `Bearer ${config.token}`

  try {
    const response = await fetch(joinUrl(config.baseUrl, path), {
      headers,
      credentials: 'same-origin',
      signal: controller.signal,
    })
    captureCsrf(config, path, response)
    const data = await readJsonBody<T>(response)

    if (!response.ok) {
      return {
        ok: false,
        source: 'live',
        data,
        status: response.status,
        error: responseErrorSummary(response, data),
      }
    }

    return { ok: true, source: 'live', data }
  } catch (error) {
    return { ok: false, source: 'live', error: describeError(error) }
  } finally {
    window.clearTimeout(timeout)
  }
}

function captureCsrf(config: ApiConfig, path: string, response: Response): void {
  if (config.token || !CSRF_ISSUER_ROUTES.has(path) || !isSameOriginApiRequest(config, path)) return
  const header = response.headers.get(CSRF_HEADER)
  if (header === null) {
    if (path === '/api/me') csrfByApiBase.delete(config.baseUrl)
    return
  }

  const token = header.trim()
  if (token && token.length <= 4_096) csrfByApiBase.set(config.baseUrl, token)
  else csrfByApiBase.delete(config.baseUrl)
}

async function readJsonBody<T>(response: Response): Promise<T | undefined> {
  const contentType = response.headers.get('content-type') ?? ''
  if (!contentType.includes('application/json')) return undefined
  return await response.json().catch(() => undefined) as T | undefined
}

function responseErrorSummary(response: Response, data: unknown): string {
  if (isRecord(data)) {
    const detail = typeof data.error === 'string' ? data.error : typeof data.message === 'string' ? data.message : undefined
    if (detail) return detail
  }
  return `${response.status} ${response.statusText || 'HTTP error'}`
}

async function queryJson<T>(config: ApiConfig, path: string): Promise<T> {
  const headers: HeadersInit = { Accept: 'application/json' }
  if (config.token) headers.Authorization = `Bearer ${config.token}`
  const response = await fetch(joinUrl(config.baseUrl, path), { headers, credentials: 'same-origin' })
  if (!response.ok) throw new Error(await responseError(response))
  const data = await readJsonBody<T>(response)
  if (data === undefined) throw new Error('Sideport API returned an empty response.')
  return data
}

export async function startDeviceEnrollment(payload: DeviceEnrollmentPayload, config = getSideportApiConfig()) {
  return mutateJson<OperationRecordDto>(config, '/api/devices/enrollments', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

/** Return the current authoritative transport snapshot, including full UDIDs for mutations. */
export async function listReachableDevices(config = getSideportApiConfig()) {
  return queryJson<ReachableDeviceDto[]>(config, '/api/devices')
}

/** Fetch one durable operation. Call repeatedly while its status is non-terminal. */
export async function getSideportOperation(operationId: string, config = getSideportApiConfig()) {
  return queryJson<OperationRecordDto>(config, `/api/operations/${encodeURIComponent(operationId)}`)
}

export async function listCatalogAppsV2(config = getSideportApiConfig()) {
  return queryJson<CatalogAppV2Dto[]>(config, '/api/v2/catalog/apps')
}

export async function listCatalogImportRoots(config = getSideportApiConfig()) {
  return queryJson<CatalogImportRootDto[]>(config, '/api/v2/catalog/import-roots')
}

export async function inspectCatalogAppV2(payload: CatalogRootImportPayload, config = getSideportApiConfig()) {
  return mutateJson<CatalogAppV2Dto>(config, '/api/v2/catalog/apps/inspect', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function uploadCatalogIpaV2(
  file: File,
  payload: CatalogUploadV2Payload = {},
  config = getSideportApiConfig(),
) {
  if (!config.canMutate) throw new Error('Mutations are disabled for this admin build.')
  const form = new FormData()
  form.set('ipa', file)
  if (payload.id?.trim()) form.set('id', payload.id.trim())
  if (payload.name?.trim()) form.set('name', payload.name.trim())
  if (payload.purpose?.trim()) form.set('purpose', payload.purpose.trim())
  if (payload.idempotencyKey?.trim()) form.set('idempotencyKey', payload.idempotencyKey.trim())
  if (payload.expectedCatalogVersion !== undefined) form.set('expectedCatalogVersion', String(payload.expectedCatalogVersion))
  const path = '/api/v2/catalog/apps/upload'
  const headers = new Headers({ Accept: 'application/json' })
  applyMutationAuthentication(config, path, 'POST', headers)
  const response = await fetch(joinUrl(config.baseUrl, path), {
    method: 'POST',
    headers,
    body: form,
    credentials: 'same-origin',
  })
  clearRejectedCsrf(config, path, 'POST', response)
  if (!response.ok) throw new Error(await responseError(response))
  return await response.json() as CatalogAppV2Dto
}

export async function listGitHubCatalogSources(config = getSideportApiConfig()) {
  return queryJson<GitHubSourcesDto>(config, '/api/v2/catalog/github/sources')
}

export async function createGitHubCatalogConnection(payload: GitHubConnectionPayload, config = getSideportApiConfig()) {
  return mutateJson<GitHubConnectionDto>(config, '/api/v2/catalog/github/connections', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function getGitHubCatalogConnection(connectionId: string, config = getSideportApiConfig()) {
  return queryJson<GitHubConnectionDto>(config, `/api/v2/catalog/github/connections/${encodeURIComponent(connectionId)}`)
}

export async function listGitHubCatalogReleases(sourceId: string, page = 1, config = getSideportApiConfig()) {
  return queryJson<GitHubReleasePageDto>(
    config,
    `/api/v2/catalog/github/sources/${encodeURIComponent(sourceId)}/releases?page=${encodeURIComponent(String(page))}`,
  )
}

export async function importGitHubCatalogApp(payload: GitHubCatalogImportPayload, config = getSideportApiConfig()) {
  return mutateJson<CatalogAppV2Dto>(config, '/api/v2/catalog/apps/import-github', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function registerSideportApp(payload: AppRegistrationPayload, config = getSideportApiConfig()) {
  return mutateJson<RegisteredAppDto>(config, '/api/apps', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export function registerPendingSideportApp(payload: PendingAppRegistrationPayload, config = getSideportApiConfig()) {
  return mutateJson<AppRegistrationDto>(config, '/api/apps', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function inspectCatalogApp(payload: CatalogInspectPayload, config = getSideportApiConfig()) {
  return mutateJson<CatalogAppDto>(config, '/api/catalog/apps/inspect', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function uploadCatalogIpa(file: File, payload: { id?: string; name?: string; purpose?: string; replace?: boolean }, config = getSideportApiConfig()) {
  if (!config.canMutate) throw new Error('Mutations are disabled for this admin build.')
  const form = new FormData()
  form.set('ipa', file)
  if (payload.id?.trim()) form.set('id', payload.id.trim())
  if (payload.name?.trim()) form.set('name', payload.name.trim())
  if (payload.purpose?.trim()) form.set('purpose', payload.purpose.trim())
  if (payload.replace) form.set('replace', 'true')
  const path = '/api/catalog/apps/upload'
  const headers = new Headers({ Accept: 'application/json' })
  applyMutationAuthentication(config, path, 'POST', headers)
  const response = await fetch(joinUrl(config.baseUrl, path), { method: 'POST', headers, body: form, credentials: 'same-origin' })
  clearRejectedCsrf(config, path, 'POST', response)
  if (!response.ok) throw new Error(await responseError(response))
  return await response.json() as CatalogAppDto
}

export async function refreshSideportApp(deviceUdid: string, bundleId: string, config = getSideportApiConfig()) {
  return mutateJson<RefreshOperationDto>(config, '/api/operations/refresh', {
    method: 'POST',
    body: JSON.stringify({
      deviceUdid,
      bundleId,
      idempotencyKey: `ui-${deviceUdid}-${bundleId}-${Date.now()}`,
    }),
  })
}

export function installSideportCatalogApp(payload: InstallOperationPayload, config = getSideportApiConfig()) {
  return mutateJson<OperationRecordDto>(config, '/api/operations/install', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export function preflightSideportInstall(payload: InstallPreflightPayload, config = getSideportApiConfig()) {
  return mutateJson<OperationPreflightDto>(config, '/api/operations/preflight', {
    method: 'POST',
    body: JSON.stringify({ type: 'install', ...payload }),
  })
}

export function completeSideportOnboarding(payload: OnboardingCompletionPayload, config = getSideportApiConfig()) {
  return mutateJson<WorkflowCompletionReceipt>(config, '/api/onboarding/complete', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export function reconcileSideportOperation(
  operationId: string,
  payload: { idempotencyKey: string; note?: string },
  config = getSideportApiConfig(),
) {
  return mutateJson<OperationRecordDto>(config, `/api/operations/${encodeURIComponent(operationId)}/reconcile`, {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export function updateSideportSchedulerSettings(enabled: boolean, config = getSideportApiConfig()) {
  return mutateJson<SchedulerStatusDto>(config, '/api/scheduler/settings', {
    method: 'PUT',
    body: JSON.stringify({ enabled }),
  })
}

export async function cancelOperation(operationId: string, config = getSideportApiConfig()) {
  return mutateJson<RefreshOperationDto>(config, `/api/operations/${encodeURIComponent(operationId)}/cancel`, {
    method: 'POST',
    body: JSON.stringify({ reason: 'Canceled from Sideport admin.' }),
  })
}

export async function retryOperation(operationId: string, config = getSideportApiConfig()) {
  return mutateJson<OperationRecordDto>(config, `/api/operations/${encodeURIComponent(operationId)}/retry`, {
    method: 'POST',
    body: JSON.stringify({ idempotencyKey: `ui-retry-${operationId}-${Date.now()}` }),
  })
}

export async function rerunOperation(operationId: string, config = getSideportApiConfig()) {
  return mutateJson<RefreshOperationDto>(config, `/api/operations/${encodeURIComponent(operationId)}/rerun`, {
    method: 'POST',
    body: JSON.stringify({ idempotencyKey: `ui-rerun-${operationId}-${Date.now()}` }),
  })
}

export async function preflightSideportRefresh(deviceUdid: string, bundleId: string, config = getSideportApiConfig()) {
  return mutateJson<OperationPreflightDto>(config, '/api/operations/preflight', {
    method: 'POST',
    body: JSON.stringify({
      type: 'refresh',
      deviceUdid,
      bundleId,
    }),
  })
}

export async function signInPersonalApple(payload: PersonalAppleSignInPayload, config = getSideportApiConfig()) {
  return mutateJson<PersonalAppleStatusDto>(config, '/api/apple-access/personal/sign-in', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export function connectPersonalApple(payload: PersonalAppleConnectPayload, config = getSideportApiConfig()) {
  return mutateJson<PersonalAppleStatusDto>(config, '/api/apple-access/personal/connect', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function completePersonalAppleTwoFactor(payload: PersonalAppleTwoFactorPayload, config = getSideportApiConfig()) {
  return mutateJson<PersonalAppleStatusDto>(config, '/api/apple-access/personal/2fa', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function selectPersonalAppleTeam(payload: PersonalAppleTeamSelectionPayload, config = getSideportApiConfig()) {
  return mutateJson<PersonalAppleStatusDto>(config, '/api/apple-access/personal/team', {
    method: 'PUT',
    body: JSON.stringify(payload),
  })
}

export async function preflightPersonalAppleSigning(accountProfileId: string, teamId: string, config = getSideportApiConfig(), candidateId?: string, currentAccountProfileId?: string) {
  return mutateJson<PersonalAppleSigningPreflightDto>(config, '/api/apple-access/personal/signing-preflight', {
    method: 'POST',
    body: JSON.stringify({ accountProfileId, teamId, candidateId, currentAccountProfileId }),
  })
}

export async function cutoverPersonalAppleSigning(payload: PersonalAppleCutoverPayload, config = getSideportApiConfig()) {
  return mutateJson<OperationRecordDto>(config, '/api/apple-access/personal/cutover', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function connectReplacementAppleAccount(appleId: string, password: string, config = getSideportApiConfig()) {
  return mutateJson<AppleAccountReplacementCandidateDto>(config, '/api/apple-access/personal/replacement-candidates', {
    method: 'POST', body: JSON.stringify({ appleId, password }),
  })
}

export async function completeReplacementAppleTwoFactor(candidateId: string, code: string, config = getSideportApiConfig()) {
  return mutateJson<AppleAccountReplacementCandidateDto>(config, '/api/apple-access/personal/replacement-candidates/2fa', {
    method: 'POST', body: JSON.stringify({ candidateId, code }),
  })
}

export async function deleteSideportApp(deviceUdid: string, bundleId: string, config = getSideportApiConfig()) {
  await mutateJson<unknown>(config, `/api/apps/${encodeURIComponent(deviceUdid)}/${encodeURIComponent(bundleId)}`, {
    method: 'DELETE',
  })
}

export interface DeviceCheckDto {
  id: string
  label: string
  status: 'ok' | 'warning' | 'blocked' | string
  detail: string
  remediation?: string | null
}

export interface DeviceDiagnosticsDto {
  status: 'ok' | 'warning' | 'blocked' | string
  checks: DeviceCheckDto[]
}

/** Run the backend device-connectivity self-test (usbmux -> enumerate -> trust). */
export async function runDeviceDiagnostics(config = getSideportApiConfig()): Promise<DeviceDiagnosticsDto> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (config.token) headers.Authorization = `Bearer ${config.token}`
  const response = await fetch(joinUrl(config.baseUrl, '/api/devices/diagnostics'), { headers, credentials: 'same-origin' })
  if (!response.ok) throw new Error(await responseError(response))
  return await response.json() as DeviceDiagnosticsDto
}

async function mutateJson<T>(config: ApiConfig, path: string, init: RequestInit): Promise<T> {
  if (!config.canMutate) throw new Error('Mutations are disabled for this admin build.')

  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (init.body) headers.set('Content-Type', 'application/json')
  applyMutationAuthentication(config, path, init.method, headers)

  const response = await fetch(joinUrl(config.baseUrl, path), { ...init, headers, credentials: 'same-origin' })
  clearRejectedCsrf(config, path, init.method, response)
  if (!response.ok) {
    const data = await readJsonBody<unknown>(response)
    const error = apiErrorDetails(response, data)
    throw new SideportApiError(error.message, response.status, error.code, data)
  }
  if (response.status === 204) return undefined as T
  return await response.json() as T
}

function applyMutationAuthentication(config: ApiConfig, path: string, method: string | undefined, headers: Headers): void {
  if (config.token) {
    headers.set('Authorization', `Bearer ${config.token}`)
    return
  }

  if (!isUnsafeSameOriginRequest(config, path, method)) return
  const csrf = csrfByApiBase.get(config.baseUrl)
  if (csrf) headers.set(CSRF_HEADER, csrf)
}

function clearRejectedCsrf(config: ApiConfig, path: string, method: string | undefined, response: Response): void {
  if (response.status === 403 && !config.token && isUnsafeSameOriginRequest(config, path, method)) {
    csrfByApiBase.delete(config.baseUrl)
  }
}

function isUnsafeSameOriginRequest(config: ApiConfig, path: string, method: string | undefined): boolean {
  const normalizedMethod = (method ?? 'GET').toUpperCase()
  return normalizedMethod !== 'GET' && normalizedMethod !== 'HEAD' && normalizedMethod !== 'OPTIONS' &&
    isSameOriginApiRequest(config, path)
}

function isSameOriginApiRequest(config: ApiConfig, path: string): boolean {
  try {
    return new URL(joinUrl(config.baseUrl, path), window.location.href).origin === window.location.origin
  } catch {
    return false
  }
}

async function responseError(response: Response): Promise<string> {
  const contentType = response.headers.get('content-type') ?? ''
  if (contentType.includes('application/json')) {
    const body = await response.json().catch(() => null) as { error?: string; message?: string } | null
    if (body?.message) return body.message
    if (body?.error) return body.error
  }
  const text = await response.text().catch(() => '')
  return text || `${response.status} ${response.statusText || 'HTTP error'}`
}

function apiErrorDetails(response: Response, data: unknown): { message: string; code?: string } {
  if (isRecord(data)) {
    const nestedError = isRecord(data.error) ? data.error : undefined
    const code = typeof data.code === 'string'
      ? data.code
      : typeof data.error === 'string'
        ? data.error
        : typeof nestedError?.code === 'string'
          ? nestedError.code
          : undefined
    const message = typeof data.message === 'string'
      ? data.message
      : typeof nestedError?.message === 'string'
        ? nestedError.message
        : typeof data.error === 'string'
          ? data.error
          : undefined
    if (message) return { message, code }
  }
  return { message: `${response.status} ${response.statusText || 'HTTP error'}` }
}

function buildAdminData(snapshot: ApiSnapshot): { data: SideportReadModel; status: AdminDataStatus } {
  const hasLiveCore = snapshot.health.ok || snapshot.ready.ok || snapshot.devices.ok || snapshot.apps.ok
  if (!hasLiveCore) return buildUnavailableData(snapshot.config, snapshot.health.error ?? 'Sideport API is not reachable.')

  const catalogApps = snapshot.catalog.ok && snapshot.catalog.data ? snapshot.catalog.data.map(toCatalogApp).filter(isPresent) : runtimeEmptyData.catalogApps
  const appleAccess = snapshot.appleAccess.ok && snapshot.appleAccess.data ? toAppleAccess(snapshot.appleAccess.data) : unavailableAppleAccess(snapshot.appleAccess.error)
  const personalApple = snapshot.personalApple.ok && snapshot.personalApple.data ? toPersonalApple(snapshot.personalApple.data) : unavailablePersonalApple(snapshot.personalApple.error)
  const apps = snapshot.apps.ok && snapshot.apps.data ? snapshot.apps.data.map((app) => toRegisteredApp(app, catalogApps)).filter(isPresent) : runtimeEmptyData.apps
  const installedApps = snapshot.installedApps.data ? snapshot.installedApps.data.map((app) => toInstalledApp(app, apps)).filter(isPresent) : runtimeEmptyData.installedApps
  const devices = snapshot.knownDevices.ok && snapshot.knownDevices.data
    ? snapshot.knownDevices.data.map((device) => toKnownDeviceSummary(device, apps, installedApps)).filter(isPresent)
    : snapshot.devices.ok && snapshot.devices.data
      ? snapshot.devices.data.map((device) => toDeviceSummary(device, apps, installedApps)).filter(isPresent)
    : runtimeEmptyData.devices
  const operations = snapshot.operations.ok && snapshot.operations.data ? snapshot.operations.data.map(toOperationSummary).filter(isPresent) : runtimeEmptyData.operations
  const renewals = snapshot.renewals.ok && snapshot.renewals.data
    ? snapshot.renewals.data.map(toRenewalItemFromDto).filter(isPresent)
    : apps.length ? apps.map(toRenewalItem) : runtimeEmptyData.renewals
  const logs = snapshot.logs.ok && snapshot.logs.data ? snapshot.logs.data.map((entry) => toOperationLog(entry, snapshot.fetchedAt)).filter(isPresent) : runtimeEmptyData.logs
  const issues = snapshot.diagnosticIssues.ok && snapshot.diagnosticIssues.data
    ? snapshot.diagnosticIssues.data.map(toDiagnosticIssue).filter(isPresent)
    : buildIssues(snapshot, apps, operations)
  const system = buildSystemStatus(snapshot)
  const workspace = snapshot.workspace.ok && snapshot.workspace.data ? toWorkspace(snapshot.workspace.data) : plannedWorkspace(snapshot.workspace.error)
  const serverAllowsMutations = workspace.source === 'live'
    && workspace.authMode !== 'open-behind-proxy'
    && Object.values(workspace.capabilities ?? {}).some(Boolean)

  const partial = [snapshot.health, snapshot.ready, snapshot.systemStatus, snapshot.devices, snapshot.knownDevices, snapshot.catalog, snapshot.appleAccess, snapshot.personalApple, snapshot.installedApps, snapshot.apps, snapshot.anisette, snapshot.onboarding, snapshot.scheduler, snapshot.logs, snapshot.operations, snapshot.renewals, snapshot.diagnosticIssues, snapshot.workspace].some((result) => !result.ok)
  const status: AdminDataStatus = {
    mode: partial ? 'partial' : 'live',
    baseUrl: snapshot.config.baseUrl,
    lastUpdatedAt: snapshot.fetchedAt,
    canMutate: snapshot.config.canMutate && serverAllowsMutations,
    onboarding: snapshot.onboarding.ok && snapshot.onboarding.data ? toOnboardingStatus(snapshot.onboarding.data) : undefined,
    message: partial ? degradedStatusMessage(snapshot) : 'Live Sideport API connected.',
  }

  return {
    data: {
      system,
      devices,
      catalogApps,
      appleAccess,
      personalApple,
      installedApps,
      apps,
      renewals,
      operations,
      issues,
      activity: buildActivity(snapshot, partial, logs),
      logs,
      workspace,
    },
    status,
  }
}

function buildUnavailableData(config: ApiConfig, message: string): { data: SideportReadModel; status: AdminDataStatus } {
  const fallbackSystem: SystemStatus = {
    ...runtimeEmptyData.system,
    api: { ok: false, source: 'live' },
    ready: {
      ...runtimeEmptyData.system.ready,
      ready: false,
      checks: {
        anisette: { ok: false, error: 'Waiting for API snapshot', source: 'live' },
        signer: { ...runtimeEmptyData.system.ready.checks.signer, ok: false, source: 'live' },
      },
    },
    apiAuth: { configured: false, source: 'live' },
  }

  return {
    data: {
      ...runtimeEmptyData,
      system: fallbackSystem,
    },
    status: {
      mode: 'unavailable',
      baseUrl: config.baseUrl,
      canMutate: config.canMutate,
      message,
    },
  }
}

function buildSystemStatus(snapshot: ApiSnapshot): SystemStatus {
  const operational = snapshot.systemStatus.ok && snapshot.systemStatus.data?.operational === true
  const checks = snapshot.systemStatus.ok
    ? (snapshot.systemStatus.data?.checks ?? []).map(toSystemOperationalCheck).filter(isPresent)
    : []
  const anisetteCheck = checks.find((check) => check.id === 'anisette-headers')
  const signerCheck = checks.find((check) => check.id === 'signer-executable')
  const mutationCheck = checks.find((check) => check.id === 'mutation-protection')

  return {
    operational,
    checkedAt: snapshot.systemStatus.data?.checkedAt,
    checks,
    api: { ok: snapshot.health.ok && snapshot.health.data?.ok !== false, source: 'live' },
    ready: {
      ready: operational,
      source: 'live',
      checks: {
        anisette: {
          ok: anisetteCheck?.status === 'pass',
          error: anisetteCheck?.status === 'fail' ? anisetteCheck.reason : snapshot.systemStatus.error ?? null,
          source: 'live',
        },
        signer: {
          ok: signerCheck?.status === 'pass',
          path: signerCheck?.reason ?? 'Signer check unavailable',
          source: 'live',
        },
      },
    },
    apiAuth: { configured: mutationCheck?.status === 'pass', source: 'live' },
    scheduler: snapshot.scheduler.ok && snapshot.scheduler.data
      ? toSchedulerStatus(snapshot.scheduler.data)
      : {
          enabled: Boolean(snapshot.onboarding.data?.schedulerEnabled),
          source: snapshot.onboarding.ok ? 'live' : 'planned',
        },
    observability: snapshot.logs.ok
      ? { exporter: 'API log ring buffer', connected: true, source: 'live' }
      : { exporter: 'OTLP not connected', connected: false, source: 'planned' },
  }
}

function toSchedulerStatus(status: SchedulerStatusDto): SystemStatus['scheduler'] {
  return {
    enabled: status.enabled === true,
    checkedAt: status.checkedAt,
    policy: status.policy ? {
      mode: status.policy.mode,
      evaluationInterval: status.policy.evaluationInterval,
      refreshLeadTime: status.policy.refreshLeadTime,
      resignInterval: status.policy.resignInterval ?? null,
      catchUp: status.policy.catchUp,
      missedIntervals: status.policy.missedIntervals,
    } : undefined,
    nextEvaluationAt: status.nextEvaluationAt ?? null,
    lastEvaluation: status.lastEvaluation ?? null,
    dueCount: status.dueCount,
    queuedCount: status.queuedCount,
    concurrency: status.concurrency,
    historyRetention: status.historyRetention,
    source: normalizeSource(status.source),
  }
}

function toSystemOperationalCheck(check: SystemOperationalCheckDto): SystemOperationalCheck | null {
  if (!check.id || (check.status !== 'pass' && check.status !== 'fail')) return null
  return {
    id: check.id,
    status: check.status,
    source: normalizeSource(check.source),
    checkedAt: check.checkedAt ?? '',
    scope: check.scope ?? 'system',
    affectedResources: check.affectedResources ?? [],
    reason: check.reason ?? (check.status === 'pass' ? 'Check passed.' : 'Check failed.'),
    nextAction: check.nextAction ?? null,
  }
}

function toDeviceSummary(device: ReachableDeviceDto, apps: RegisteredAppSummary[], installedApps: InstalledAppSummary[]) {
  if (!device.udid) return null
  const deviceApps = apps.filter((app) => app.deviceUdid === device.udid)
  const deviceInstalledApps = installedApps.filter((app) => app.deviceUdid === device.udid)
  const nearestExpiry = earliestDate(deviceApps.map((app) => app.expiresAt?.value))
  const hasLastError = deviceApps.some((app) => app.lastError)

  return {
    udid: device.udid,
    name: device.name || 'Reachable iPhone',
    productType: device.productType || 'Unknown model',
    osVersion: device.osVersion || 'Unknown',
    connection: normalizeConnection(device.connection),
    lastSeenAt: { value: new Date().toISOString(), source: 'derived' as const },
    health: healthFromExpiry(nearestExpiry, hasLastError),
    teamId: deviceApps[0]?.teamId || 'Unknown',
    appSlotsUsed: deviceApps.length,
    installedAppCount: deviceInstalledApps.length,
    unmanagedAppCount: deviceInstalledApps.filter((app) => !app.managedBySideport).length,
    nearestExpiryAt: nearestExpiry ? { value: nearestExpiry, source: 'live' as const } : undefined,
    blocker: hasLastError ? 'One app has a failed refresh/install state.' : undefined,
  }
}

function toKnownDeviceSummary(device: KnownDeviceDto, apps: RegisteredAppSummary[], installedApps: InstalledAppSummary[]) {
  if (!device.udid) return null
  const deviceApps = apps.filter((app) => app.deviceUdid === device.udid)
  const deviceInstalledApps = installedApps.filter((app) => app.deviceUdid === device.udid)
  const nearestExpiry = earliestDate(deviceApps.map((app) => app.expiresAt?.value))
  const hasLastError = deviceApps.some((app) => app.lastError)
  const seenAt = device.lastSeenAt ?? device.currentPollAt
  return {
    udid: device.udid,
    name: device.displayName || 'Known iPhone',
    productType: device.productType ?? 'Unknown model',
    osVersion: device.osVersion ?? 'Unknown',
    connection: normalizeConnection(device.connection),
    lastSeenAt: { value: seenAt ?? '', source: device.lastSeenAt ? 'live' as const : 'planned' as const },
    hasDurableLastSeen: Boolean(device.lastSeenAt),
    currentPollAt: device.currentPollAt ? { value: device.currentPollAt, source: 'live' as const } : undefined,
    lastSeenSource: device.lastSeenSource,
    inventoryState: device.inventoryState,
    acceptedAt: device.acceptedAt ? { value: device.acceptedAt, source: 'live' as const } : undefined,
    acceptedBy: device.acceptedBy ?? undefined,
    enrollmentOperationId: device.enrollmentOperationId ?? undefined,
    trustState: device.trustState,
    trustReason: device.trustReason ?? undefined,
    lockdownCheckedAt: device.lockdownCheckedAt ? { value: device.lockdownCheckedAt, source: 'live' as const } : undefined,
    usableForInstall: device.usableForInstall,
    supportedForFirstInstall: device.supportedForFirstInstall,
    health: normalizeHealthState(device.health?.state) ?? healthFromExpiry(nearestExpiry, hasLastError),
    teamId: deviceApps[0]?.teamId || 'Unknown',
    appSlotsUsed: device.appSlots?.used ?? deviceApps.length,
    installedAppCount: deviceInstalledApps.length,
    unmanagedAppCount: deviceInstalledApps.filter((app) => !app.managedBySideport).length,
    nearestExpiryAt: nearestExpiry ? { value: nearestExpiry, source: 'live' as const } : undefined,
    blocker: device.health?.nextAction ?? (hasLastError ? 'One app has a failed refresh/install state.' : undefined),
  }
}

function toInstalledApp(app: InstalledAppDto, registrations: RegisteredAppSummary[]): InstalledAppSummary | null {
  if (!app.bundleId || !app.deviceUdid) return null
  const managedBySideport = registrations.some((registration) =>
    registration.bundleId === app.bundleId && registration.deviceUdid === app.deviceUdid)
  return {
    bundleId: app.bundleId,
    deviceUdid: app.deviceUdid,
    name: app.name || displayNameFromBundleId(app.bundleId),
    version: app.version || 'Unknown',
    signatureExpiresAt: app.signatureExpiresAt ? { value: app.signatureExpiresAt, source: 'live' } : undefined,
    managedBySideport,
    source: 'live',
  }
}

function toOperationLog(entry: OperationLogDto, fallbackAt: string): OperationLogEntry | null {
  if (!entry.message || !entry.category) return null
  return {
    id: String(entry.id ?? `${entry.category}-${entry.at ?? fallbackAt}-${entry.message}`),
    at: entry.at ?? fallbackAt,
    level: entry.level ?? 'Information',
    category: entry.category,
    eventId: entry.eventId,
    message: entry.message,
    exceptionType: entry.exceptionType ?? null,
    exceptionMessage: entry.exceptionMessage ?? null,
    source: 'live',
  }
}

function toCatalogApp(app: CatalogAppV2Dto): CatalogAppSummary | null {
  if (!app.id || !app.name || !app.bundleId) return null
  const status = normalizeCatalogStatus(app.status)
  return {
    id: app.id,
    name: app.name,
    purpose: app.purpose || 'Server-side IPA catalog entry.',
    expectedBundleId: app.bundleId,
    catalogVersion: app.catalogVersion,
    artifactSources: app.artifactSources.map((source) => ({
      kind: source.kind,
      label: source.label,
      repository: source.repository ?? undefined,
      releaseTag: source.releaseTag ?? undefined,
      assetName: source.assetName ?? undefined,
    })),
    versionLabel: versionLabel(app.shortVersion, app.version),
    status,
    statusLabel: catalogStatusLabel(status),
    source: normalizeSource(app.source),
    iconTone: toneFromBundleId(app.bundleId),
    notes: app.notes?.length ? app.notes : [catalogStatusLabel(status)],
    sizeBytes: app.sizeBytes ?? undefined,
    sha256: app.sha256 ?? undefined,
    hasEmbeddedProfile: Boolean(app.hasEmbeddedProfile),
    signatureExpiresAt: app.signatureExpiresAt ?? undefined,
    lastInspectedAt: app.lastInspectedAt ?? undefined,
    icon: app.icon ?? undefined,
  }
}

function toAppleAccess(dto: AppleAccessStatusDto): AppleAccessSummary {
  return {
    connector: dto.connector || 'app-store-connect-jwt',
    state: normalizeAppleAccessState(dto.state),
    secretCustody: dto.secretCustody || 'server-configured-key-reference',
    keyIdSuffix: dto.keyIdSuffix ?? null,
    issuerIdSuffix: dto.issuerIdSuffix ?? null,
    message: dto.message || 'Apple Access status returned without a message.',
    capabilities: dto.capabilities?.map(toAppleAccessCapability).filter(isPresent) ?? [],
    source: 'live',
  }
}

function toPersonalApple(dto: PersonalAppleStatusDto): PersonalAppleSummary {
  return {
    connector: dto.connector || 'personal-apple-id',
    state: normalizePersonalAppleState(dto.state),
    secretCustody: dto.secretCustody || 'host-environment-or-secret-store',
    credentialSource: dto.credentialSource,
    accountProfileId: dto.accountProfileId ?? null,
    credentialEntry: dto.credentialEntry ? {
      supported: Boolean(dto.credentialEntry.supported),
      allowedNow: Boolean(dto.credentialEntry.allowedNow),
      blockedReason: dto.credentialEntry.blockedReason?.code && dto.credentialEntry.blockedReason.message
        ? { code: dto.credentialEntry.blockedReason.code, message: dto.credentialEntry.blockedReason.message }
        : null,
    } : null,
    appleIdHint: dto.appleIdHint ?? null,
    message: dto.message || 'Personal Apple ID connector returned without a message.',
    pendingChallengeId: dto.pendingChallengeId ?? null,
    pendingChallengeKind: dto.pendingChallengeKind ?? null,
    pendingChallengeExpiresAt: dto.pendingChallengeExpiresAt ?? null,
    selectedTeamId: dto.selectedTeamId ?? null,
    teamValidatedAt: dto.teamValidatedAt ?? null,
    lastAuthenticatedAt: dto.lastAuthenticatedAt ?? null,
    authValidatedAt: dto.authValidatedAt ?? null,
    teams: dto.teams?.map(toPersonalAppleTeam).filter(isPresent) ?? [],
    source: 'live',
  }
}

function toPersonalAppleTeam(team: PersonalAppleTeamDto): PersonalAppleTeamSummary | null {
  if (!team.teamId) return null
  return {
    teamId: team.teamId,
    name: team.name || 'Apple Developer Team',
    type: team.type || 'Unknown',
  }
}

function toAppleAccessCapability(dto: AppleAccessCapabilityDto): AppleAccessCapabilitySummary | null {
  if (!dto.id || !dto.label || !dto.endpoint) return null
  return {
    id: dto.id,
    label: dto.label,
    endpoint: dto.endpoint,
    state: normalizeCapabilityState(dto.state),
    httpStatus: dto.httpStatus ?? undefined,
    detail: dto.detail || 'No detail returned.',
    count: dto.count ?? undefined,
    source: 'live',
  }
}

function unavailableAppleAccess(error?: string): AppleAccessSummary {
  return {
    ...runtimeEmptyData.appleAccess,
    state: 'unavailable',
    message: error ?? 'Apple Access status endpoint is unavailable.',
  }
}

function unavailablePersonalApple(error?: string): PersonalAppleSummary {
  return {
    ...runtimeEmptyData.personalApple,
    state: 'unavailable',
    message: error ?? 'Personal Apple ID connector status endpoint is unavailable.',
  }
}

function toRegisteredApp(app: RegisteredAppDto, catalogApps: CatalogAppSummary[]) {
  if (!app.bundleId || !app.deviceUdid) return null
  const catalogApp = catalogApps.find((catalog) => catalog.expectedBundleId === app.bundleId)
  return {
    bundleId: app.bundleId,
    deviceUdid: app.deviceUdid,
    appleId: app.appleId || 'Unknown Apple ID',
    teamId: app.teamId || 'Unknown team',
    expiresAt: app.expiresAt ? { value: app.expiresAt, source: 'live' as const } : undefined,
    timeUntilExpiry: app.timeUntilExpiry ? { value: app.timeUntilExpiry, source: 'live' as const } : undefined,
    lastSucceeded: app.lastSucceeded ?? null,
    lastError: app.lastError ?? null,
    lifecycle: app.lifecycle === 'pending-install' ? 'pending-install' as const : 'active' as const,
    lastVerifiedOperationId: app.lastVerifiedOperationId ?? null,
    displayName: catalogApp ? { value: catalogApp.name, source: catalogApp.source } : { value: displayNameFromBundleId(app.bundleId), source: 'derived' as const },
    version: catalogApp ? { value: catalogApp.versionLabel, source: catalogApp.source } : { value: 'Unknown', source: 'planned' as const },
    iconTone: toneFromBundleId(app.bundleId),
  }
}

function toRenewalItem(app: RegisteredAppSummary): RenewalItem {
  const risk = riskForApp(app)
  const status = statusForApp(app)
  return {
    id: `${app.deviceUdid}:${app.bundleId}`,
    deviceUdid: app.deviceUdid,
    bundleId: app.bundleId,
    teamId: app.teamId,
    risk,
    status,
    expiresAt: app.expiresAt?.value,
    blocker: app.lifecycle === 'pending-install'
      ? 'Install and verify this app before automatic refresh can use it.'
      : app.lastError ?? undefined,
    source: 'live',
  }
}

function toRenewalItemFromDto(item: RenewalItemDto): RenewalItem | null {
  if (!item.id || !item.deviceUdid || !item.bundleId || !item.teamId) return null
  return {
    id: item.id,
    deviceUdid: item.deviceUdid,
    bundleId: item.bundleId,
    teamId: item.teamId,
    risk: normalizeRenewalRisk(item.risk),
    status: normalizeRenewalStatus(item.status),
    expiresAt: item.expiresAt ?? undefined,
    blocker: item.blocker ?? undefined,
    operationId: item.operationId ?? undefined,
    source: normalizeSource(item.source),
  }
}

function toOperationSummary(operation: OperationRecordDto): OperationSummary | null {
  if (!operation.operationId || !operation.target?.deviceUdid || !operation.target.bundleId) return null
  return {
    operationId: operation.operationId,
    type: operation.type || 'refresh',
    status: operation.status || 'failed',
    createdAt: operation.createdAt || new Date().toISOString(),
    updatedAt: operation.updatedAt || operation.createdAt || new Date().toISOString(),
    completedAt: operation.completedAt ?? undefined,
    deviceUdid: operation.target.deviceUdid,
    bundleId: operation.target.bundleId,
    actor: operation.actor?.displayName || operation.actor?.kind || 'api-token-client',
    stages: operation.stages?.map(toOperationStage).filter(isPresent) ?? [],
    error: operation.error?.message ?? null,
    cancelable: Boolean(operation.cancelable),
    retryable: Boolean(operation.retryable),
    rerunnable: Boolean(operation.rerunnable),
    parentOperationId: operation.parentOperationId ?? null,
    finishOnboarding: operation.installIntent?.finishOnboarding,
    source: 'live',
  }
}

function toOperationStage(stage: OperationStageDto): OperationStageSummary | null {
  if (!stage.id) return null
  return {
    id: stage.id,
    label: stage.label || stage.id,
    status: normalizeOperationStageStatus(stage.status),
    startedAt: stage.startedAt ?? undefined,
    completedAt: stage.completedAt ?? undefined,
    message: stage.message || '',
    error: stage.error?.message ?? null,
  }
}

function buildIssues(snapshot: ApiSnapshot, apps: RegisteredAppSummary[], operations: OperationSummary[]): DiagnosticIssue[] {
  const now = snapshot.fetchedAt
  const issues: DiagnosticIssue[] = []

  for (const [category, result] of [
    ['Health check unavailable', snapshot.health],
    ['Readiness check unavailable', snapshot.ready],
    ['Device API unavailable', snapshot.devices],
    ['Installed apps API unavailable', snapshot.installedApps],
    ['App catalog API unavailable', snapshot.catalog],
    ['Apple Access API unavailable', snapshot.appleAccess],
    ['Personal Apple API unavailable', snapshot.personalApple],
    ['App registry API unavailable', snapshot.apps],
    ['Anisette API unavailable', snapshot.anisette],
    ['Onboarding API unavailable', snapshot.onboarding],
    ['Scheduler API unavailable', snapshot.scheduler],
    ['Operation history API unavailable', snapshot.operations],
    ['Renewals API unavailable', snapshot.renewals],
  ] as const) {
    if (!result.ok) issues.push(apiIssue(category, result.error ?? 'Unknown API error', now, result.status))
  }

  for (const app of apps) {
    if (!app.lastError) continue
    issues.push({
      id: `issue-${app.deviceUdid}-${app.bundleId}`,
      category: 'Refresh failed',
      severity: 'error',
      status: 'unresolved',
      deviceUdid: app.deviceUdid,
      bundleId: app.bundleId,
      firstSeenAt: now,
      lastSeenAt: now,
      operationId: latestOperationFor(app, operations)?.operationId ?? 'operation-not-recorded',
      traceId: 'trace-not-reported',
      spanSummary: [{ name: 'sideport.refresh', durationMs: 0, state: 'failed' }],
      logSnippet: `Derived from app registry lastError: ${app.lastError}`,
      source: 'derived',
    })
  }

  return issues
}

function toDiagnosticIssue(issue: DiagnosticIssueDto): DiagnosticIssue | null {
  if (!issue.issueId || !issue.category) return null
  const evidence = issue.evidence?.[0]
  return {
    id: issue.issueId,
    category: issue.category,
    severity: normalizeSeverity(issue.severity),
    status: normalizeIssueStatus(issue.status),
    deviceUdid: issue.affected?.deviceUdid ?? undefined,
    bundleId: issue.affected?.bundleId ?? undefined,
    firstSeenAt: issue.firstSeenAt ?? new Date().toISOString(),
    lastSeenAt: issue.lastSeenAt ?? issue.firstSeenAt ?? new Date().toISOString(),
    operationId: issue.lastOperationId ?? evidence?.operationId ?? 'operation-not-linked',
    traceId: 'trace-not-reported',
    spanSummary: [{ name: evidence?.stageId ?? evidence?.type ?? 'diagnostic.issue', durationMs: 0, state: issue.severity === 'warning' ? 'warning' : 'failed' }],
    logSnippet: evidence?.message ?? issue.remediation ?? 'Durable diagnostic issue from operation evidence.',
    source: normalizeSource(issue.source),
  }
}

function latestOperationFor(app: RegisteredAppSummary, operations: OperationSummary[]): OperationSummary | undefined {
  return operations.find((operation) => operation.bundleId === app.bundleId && operation.deviceUdid === app.deviceUdid)
}

function toOnboardingStatus(dto: OnboardingStatusDto): OnboardingStatus {
  const steps = dto.steps?.map(toOnboardingStep).filter(isPresent) ?? []
  const receipt = dto.completionReceipt
  const completionReceipt = receipt?.schemaVersion === 2
    && receipt.completedAt
    && receipt.actor?.kind
    && receipt.actor.displayName
    && receipt.accountProfileId
    && receipt.teamId
    && receipt.verifiedOperationId
    && receipt.deviceUdid
    && receipt.registrationKey?.deviceUdid
    && receipt.registrationKey.bundleId
    && receipt.schedulerSettingsVersion
    && receipt.operationalCheckedAt
    ? {
        schemaVersion: 2 as const,
        completedAt: receipt.completedAt,
        actor: { kind: receipt.actor.kind, displayName: receipt.actor.displayName },
        accountProfileId: receipt.accountProfileId,
        teamId: receipt.teamId,
        verifiedOperationId: receipt.verifiedOperationId,
        deviceUdid: receipt.deviceUdid,
        registrationKey: {
          deviceUdid: receipt.registrationKey.deviceUdid,
          bundleId: receipt.registrationKey.bundleId,
        },
        schedulerSettingsVersion: receipt.schedulerSettingsVersion,
        operationalCheckedAt: receipt.operationalCheckedAt,
      }
    : null
  const status: OnboardingStatus = {
    firstRunComplete: Boolean(dto.firstRunComplete),
    schedulerEnabled: Boolean(dto.schedulerEnabled),
    steps,
  }
  if (dto.setupState === 'complete' || dto.setupState === 'in-progress') status.setupState = dto.setupState
  if ('selectedCatalogAppId' in dto) status.selectedCatalogAppId = dto.selectedCatalogAppId ?? null
  if ('activeInstallOperationId' in dto) status.activeInstallOperationId = dto.activeInstallOperationId ?? null
  if ('completionReceipt' in dto) status.completionReceipt = completionReceipt
  if ('workflow' in dto) status.workflow = toOnboardingWorkflow(dto.workflow)
  return status
}

function toOnboardingWorkflow(dto: OnboardingWorkflowDto | null | undefined): OnboardingWorkflowV2 | null {
  if (!dto || dto.schemaVersion !== 2 || (dto.setupState !== 'in-progress' && dto.setupState !== 'complete')) return null
  const steps = (dto.steps ?? []).map(toOnboardingWorkflowStep).filter(isPresent)
  const nextAction = dto.nextAction && isWorkflowStepId(dto.nextAction.stepId)
    ? toWorkflowAction(dto.nextAction)
    : null
  return {
    schemaVersion: 2,
    setupState: dto.setupState,
    readyNow: dto.readyNow === true,
    completedAt: dto.completedAt ?? null,
    verifiedOperationId: dto.verifiedOperationId ?? null,
    nextAction: nextAction ? { ...nextAction, stepId: dto.nextAction!.stepId as OnboardingWorkflowStepId } : null,
    steps,
  }
}

function toOnboardingWorkflowStep(step: OnboardingWorkflowStepDto): OnboardingWorkflowStep | null {
  if (!isWorkflowStepId(step.id) || !isWorkflowStepState(step.state)) return null
  const nextAction = toWorkflowAction(step.nextAction)
  return {
    id: step.id,
    state: step.state,
    required: step.required !== false,
    source: normalizeSource(step.source),
    evidenceOrigin: normalizeEvidenceOrigin(step.evidenceOrigin),
    checkedAt: step.checkedAt,
    activeOperationId: step.activeOperationId ?? null,
    reason: step.reason,
    nextAction: nextAction ?? undefined,
    evidence: (step.evidence ?? []).map(toWorkflowEvidence).filter(isPresent),
  }
}

function toWorkflowAction(action: OnboardingWorkflowActionDto | null | undefined): OnboardingWorkflowAction | null {
  if (!action?.action || !action.label) return null
  return { action: action.action, label: action.label }
}

function toWorkflowEvidence(evidence: OnboardingWorkflowEvidenceDto): OnboardingWorkflowEvidence | null {
  const origin = normalizeEvidenceOrigin(evidence.evidenceOrigin)
  if (!evidence.id || !evidence.label || !evidence.detail || !origin || !evidence.checkedAt) return null
  return {
    id: evidence.id,
    label: evidence.label,
    detail: evidence.detail,
    source: normalizeSource(evidence.source),
    evidenceOrigin: origin,
    checkedAt: evidence.checkedAt,
  }
}

function isWorkflowStepId(value: string | undefined): value is OnboardingWorkflowStepId {
  return value === 'server' || value === 'apple-signer' || value === 'device' || value === 'app' || value === 'install' || value === 'ready'
}

function isWorkflowStepState(value: string | undefined): value is OnboardingWorkflowStepState {
  return value === 'not-started' || value === 'action-required' || value === 'in-progress' || value === 'complete' || value === 'blocked'
}

function normalizeEvidenceOrigin(value: string | undefined): EvidenceOrigin | undefined {
  if (value === 'operator' || value === 'device' || value === 'apple' || value === 'artifact' || value === 'system' || value === 'operation') return value
  return undefined
}

function toOnboardingStep(step: OnboardingStepDto): OnboardingStep | null {
  if (!step.id || !step.label || !step.description) return null
  return {
    id: step.id,
    label: step.label,
    description: step.description,
    state: normalizeStepState(step.state),
    surface: normalizeSurface(step.surface),
    required: step.required ?? true,
    settingsPath: step.settingsPath ?? null,
    detail: step.detail ?? null,
    source: 'live',
  }
}

function normalizeStepState(state: string | undefined): OnboardingStepState {
  if (state === 'complete' || state === 'warning' || state === 'blocked') return state
  return 'pending'
}

function normalizeSurface(surface: string | undefined): OnboardingStep['surface'] {
  return surface === 'iphone' ? 'iphone' : 'portal'
}

function normalizeSource(source: string | undefined): SourceKind {
  if (source === 'live' || source === 'derived' || source === 'demo' || source === 'planned') return source
  return 'live'
}

function normalizeRenewalRisk(risk: string | undefined): RenewalRisk {
  if (risk === 'blocked' || risk === 'due-now' || risk === 'upcoming' || risk === 'healthy') return risk
  return 'unknown'
}

function normalizeRenewalStatus(status: string | undefined): RenewalStatus {
  if (status === 'running' || status === 'queued' || status === 'failed' || status === 'blocked') return status
  return 'idle'
}

function normalizeOperationStageStatus(status: string | undefined): OperationStageSummary['status'] {
  if (status === 'running' || status === 'succeeded' || status === 'failed' || status === 'blocked') return status
  return 'pending'
}

function normalizeSeverity(severity: string | undefined): DiagnosticIssue['severity'] {
  if (severity === 'info' || severity === 'warning' || severity === 'error' || severity === 'fatal') return severity
  return 'error'
}

function normalizeIssueStatus(status: string | undefined): DiagnosticIssue['status'] {
  if (status === 'unresolved' || status === 'investigating' || status === 'resolved' || status === 'ignored') return status
  return 'unresolved'
}

function buildActivity(snapshot: ApiSnapshot, partial: boolean, logs: OperationLogEntry[]): ActivityEvent[] {
  const state = partial ? 'warning' : 'ok'
  const snapshotEvent: ActivityEvent = {
    id: `api-snapshot-${snapshot.fetchedAt}`,
    at: snapshot.fetchedAt,
    actor: 'system',
    title: partial ? 'API snapshot degraded' : 'API snapshot loaded',
    detail: `Source: ${snapshot.config.baseUrl}`,
    state,
    source: 'live',
  }

  const logEvents: ActivityEvent[] = logs.slice(0, 8).map((entry) => ({
    id: `log-activity-${entry.id}`,
    at: entry.at,
    actor: compactCategory(entry.category),
    title: logTitle(entry),
    detail: entry.exceptionMessage ? `${entry.message}: ${entry.exceptionMessage}` : entry.message,
    state: logState(entry.level),
    source: entry.source,
  }))

  return [snapshotEvent, ...logEvents]
}

function logTitle(entry: OperationLogEntry): string {
  if (entry.category.endsWith('.Requests')) return 'API request logged'
  if (entry.level.toLowerCase() === 'warning') return 'Warning logged'
  if (entry.level.toLowerCase() === 'error' || entry.level.toLowerCase() === 'critical') return 'Error logged'
  return 'Runtime log recorded'
}

function logState(level: string): ActivityEvent['state'] {
  const normalized = level.toLowerCase()
  if (normalized === 'warning') return 'warning'
  if (normalized === 'error' || normalized === 'critical') return 'failed'
  return 'info'
}

function compactCategory(category: string): string {
  return category.replace(/^Sideport\./, '').replace(/\.+/g, '.')
}

function apiIssue(category: string, detail: string, at: string, status?: number): DiagnosticIssue {
  return {
    id: `api-${category.toLowerCase().replaceAll(' ', '-')}`,
    category,
    severity: status === 401 ? 'warning' : 'error',
    status: 'unresolved',
    firstSeenAt: at,
    lastSeenAt: at,
    operationId: 'api.snapshot',
    traceId: 'trace-not-reported',
    spanSummary: [{ name: 'sideport.admin.fetch', durationMs: 0, state: 'failed' }],
    logSnippet: `Derived from admin API fetch result: ${detail}`,
    source: 'derived',
  }
}

function degradedStatusMessage(snapshot: ApiSnapshot): string {
  const failed = [
    ['health', snapshot.health],
    ['readiness', snapshot.ready],
    ['devices', snapshot.devices],
    ['installed apps', snapshot.installedApps],
    ['catalog', snapshot.catalog],
    ['apple access', snapshot.appleAccess],
    ['personal apple', snapshot.personalApple],
    ['apps', snapshot.apps],
    ['anisette', snapshot.anisette],
    ['onboarding', snapshot.onboarding],
    ['logs', snapshot.logs],
    ['operations', snapshot.operations],
    ['renewals', snapshot.renewals],
  ].filter(([, result]) => !(result as ApiResult<unknown>).ok).map(([label]) => label)

  if (!failed.length) return 'Live Sideport API connected.'
  const summary = failed.length === 1 ? failed[0] : `${failed.slice(0, -1).join(', ')} and ${failed.at(-1)}`
  return `Live API connected with ${summary} failing. Healthy endpoints still show live data.`
}

function normalizeCatalogStatus(status: string | undefined): CatalogAppStatus {
  if (status === 'ready' || status === 'invalid') return status
  return 'missing'
}

function normalizeAppleAccessState(state: string | undefined): AppleAccessState {
  if (state === 'not-configured' || state === 'invalid-configuration' || state === 'read-only-verified' || state === 'partial' || state === 'blocked') return state
  return 'unavailable'
}

function normalizePersonalAppleState(state: string | undefined): PersonalAppleState {
  if (state === 'validated-recently') return 'authenticated'
  if (state === 'validation-stale') return 'credential-configured'
  if (state === 'not-configured' || state === 'credential-configured' || state === 'two-factor-required' || state === 'authenticated') return state
  return 'unavailable'
}

function normalizeCapabilityState(state: string | undefined): AppleAccessCapabilitySummary['state'] {
  if (state === 'verified' || state === 'not-checked' || state === 'unauthorized' || state === 'denied' || state === 'rate-limited') return state
  return 'failed'
}

function catalogStatusLabel(status: CatalogAppStatus): string {
  if (status === 'ready') return 'IPA inspected'
  if (status === 'invalid') return 'Inspection failed'
  return 'IPA missing on server'
}

function versionLabel(shortVersion?: string | null, version?: string | null): string {
  if (shortVersion && version && shortVersion !== version) return `${shortVersion} (build ${version})`
  if (shortVersion) return shortVersion
  if (version) return `build ${version}`
  return 'Unknown'
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function normalizeConnection(value: ReachableDeviceDto['connection']): ConnectionState {
  if (value === 0) return 'usb'
  if (value === 1) return 'wifi'
  if (typeof value === 'string' && value.toLowerCase() === 'wifi') return 'wifi'
  if (typeof value === 'string' && (value.toLowerCase() === 'offline' || value.toLowerCase() === 'unknown')) return 'offline'
  return 'usb'
}

function healthFromExpiry(expiresAt: string | undefined, hasLastError: boolean): HealthState {
  if (hasLastError) return 'warning'
  if (!expiresAt) return 'healthy'
  const ms = new Date(expiresAt).getTime() - Date.now()
  if (Number.isNaN(ms)) return 'healthy'
  if (ms < 0) return 'blocked'
  if (ms <= 3 * 24 * 60 * 60 * 1000) return 'warning'
  return 'healthy'
}

function normalizeHealthState(state: string | undefined): HealthState | undefined {
  if (state === 'healthy' || state === 'warning' || state === 'blocked' || state === 'failed' || state === 'offline') return state
  return undefined
}

function riskForApp(app: RegisteredAppSummary): RenewalRisk {
  if (app.lifecycle === 'pending-install') return 'blocked'
  if (app.lastError) return 'blocked'
  const expiresAt = app.expiresAt?.value
  if (!expiresAt) return 'unknown'
  const ms = new Date(expiresAt).getTime() - Date.now()
  if (Number.isNaN(ms)) return 'unknown'
  if (ms < 0) return 'blocked'
  if (ms <= 3 * 24 * 60 * 60 * 1000) return 'due-now'
  if (ms <= 7 * 24 * 60 * 60 * 1000) return 'upcoming'
  return 'healthy'
}

function statusForApp(app: RegisteredAppSummary): RenewalStatus {
  if (app.lifecycle === 'pending-install') return 'blocked'
  if (app.lastError) return 'failed'
  if (app.lastSucceeded === false) return 'failed'
  return 'idle'
}

function earliestDate(values: Array<string | undefined>): string | undefined {
  const dates = values
    .filter((value): value is string => Boolean(value))
    .map((value) => new Date(value))
    .filter((date) => !Number.isNaN(date.getTime()))
    .sort((left, right) => left.getTime() - right.getTime())
  return dates[0]?.toISOString()
}

function displayNameFromBundleId(bundleId: string): string {
  return bundleId
    .split('.')
    .at(-1)
    ?.replace(/[-_]/g, ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase()) || bundleId
}

function toneFromBundleId(bundleId: string): RegisteredAppSummary['iconTone'] {
  const tones: Array<RegisteredAppSummary['iconTone']> = ['blue', 'green', 'amber', 'red', 'slate']
  const sum = [...bundleId].reduce((total, char) => total + char.charCodeAt(0), 0)
  return tones[sum % tones.length]
}

function joinUrl(baseUrl: string, path: string): string {
  if (!baseUrl || baseUrl === '/') return path
  return `${baseUrl.replace(/\/$/, '')}/${path.replace(/^\//, '')}`
}

function describeError(error: unknown): string {
  if (error instanceof DOMException && error.name === 'AbortError') return 'Request timed out'
  if (error instanceof Error) return error.message
  return 'Unknown network error'
}

function isPresent<T>(value: T | null | undefined): value is T {
  return value !== null && value !== undefined
}
