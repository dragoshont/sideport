import { useQuery } from '@tanstack/react-query'
import { runtimeEmptyData, type ActivityEvent, type AppleAccessCapabilitySummary, type AppleAccessState, type AppleAccessSummary, type CatalogAppStatus, type CatalogAppSummary, type ConnectionState, type DiagnosticIssue, type HealthState, type InstalledAppSummary, type OperationLogEntry, type PersonalAppleState, type PersonalAppleSummary, type PersonalAppleTeamSummary, type RegisteredAppSummary, type RenewalItem, type RenewalRisk, type RenewalStatus, type SideportReadModel, type SourceKind, type SystemStatus, type MemberStatus, type WorkspaceRole, type WorkspaceSummary } from '../data/sideportTypes'
import { compactUdid } from '../lib/format'

const DEFAULT_API_BASE_URL = '/sideport-api'
const DEFAULT_REFRESH_INTERVAL_MS = 15_000
const REQUEST_TIMEOUT_MS = 4_500
const SESSION_TOKEN_KEY = 'sideport.apiToken'

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

export interface CatalogInspectPayload {
  ipaPath: string
  id?: string
  name?: string
  purpose?: string
}

export interface PersonalAppleSignInPayload {
  appleId: string
}

export interface PersonalAppleTwoFactorPayload {
  challengeId: string
  code: string
}

export interface RefreshResultDto {
  success?: boolean
  bundleId?: string
  expiresAt?: string | null
  error?: string | null
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

interface HealthResponse {
  ok: boolean
}

interface DeviceDto {
  udid?: string
  name?: string
  productType?: string
  osVersion?: string
  connection?: string | number
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
  appleIdHint?: string | null
  message?: string
  pendingChallengeId?: string | null
  pendingChallengeKind?: string | null
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
}

interface WorkspaceDto {
  name?: string
  authMode?: string
  authDelegated?: boolean
  members?: WorkspaceMemberDto[]
}

function toWorkspace(dto: WorkspaceDto): WorkspaceSummary {
  const validRoles: WorkspaceRole[] = ['owner', 'admin', 'operator', 'viewer']
  const validStatuses: MemberStatus[] = ['active', 'invited', 'suspended']
  return {
    name: dto.name?.trim() || 'Sideport workspace',
    authMode: dto.authMode?.trim() || 'Reverse proxy',
    authDelegated: dto.authDelegated ?? true,
    source: 'live',
    members: (dto.members ?? []).map((member, index) => ({
      id: member.id ?? `member-${index}`,
      name: member.name?.trim() || member.email?.trim() || 'Unknown member',
      email: member.email?.trim() || '',
      role: validRoles.includes((member.role ?? '') as WorkspaceRole) ? (member.role as WorkspaceRole) : 'viewer',
      status: validStatuses.includes((member.status ?? '') as MemberStatus) ? (member.status as MemberStatus) : 'active',
      lastActiveAt: member.lastActiveAt ?? undefined,
      invitedAt: member.invitedAt ?? undefined,
      source: 'live',
    })),
  }
}

interface ApiSnapshot {
  fetchedAt: string
  config: ApiConfig
  health: ApiResult<HealthResponse>
  ready: ApiResult<ReadyResponse>
  devices: ApiResult<DeviceDto[]>
  catalog: ApiResult<CatalogAppDto[]>
  appleAccess: ApiResult<AppleAccessStatusDto>
  personalApple: ApiResult<PersonalAppleStatusDto>
  installedApps: ApiResult<InstalledAppDto[]>
  apps: ApiResult<RegisteredAppDto[]>
  anisette: ApiResult<AnisetteInfoDto>
  onboarding: ApiResult<OnboardingStatusDto>
  logs: ApiResult<OperationLogDto[]>
  workspace: ApiResult<WorkspaceDto>
}

export function getSideportApiConfig(): ApiConfig {
  const env = import.meta.env
  return {
    baseUrl: env.VITE_SIDEPORT_API_URL?.trim() || DEFAULT_API_BASE_URL,
    token: readSessionApiToken() || env.VITE_SIDEPORT_API_TOKEN?.trim() || undefined,
    canMutate: env.VITE_SIDEPORT_ENABLE_MUTATIONS === 'true',
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

async function fetchSnapshot(config: ApiConfig): Promise<ApiSnapshot> {
  const fetchedAt = new Date().toISOString()
  const [health, ready, devices, catalog, appleAccess, personalApple, apps, anisette, onboarding, logs, workspace] = await Promise.all([
    requestJson<HealthResponse>(config, '/healthz', false),
    requestJson<ReadyResponse>(config, '/readyz', false),
    requestJson<DeviceDto[]>(config, '/api/devices', true),
    requestJson<CatalogAppDto[]>(config, '/api/catalog/apps', true),
    requestJson<AppleAccessStatusDto>(config, '/api/apple-access/status', true),
    requestJson<PersonalAppleStatusDto>(config, '/api/apple-access/personal/status', true),
    requestJson<RegisteredAppDto[]>(config, '/api/apps', true),
    requestJson<AnisetteInfoDto>(config, '/api/anisette/info', true),
    requestJson<OnboardingStatusDto>(config, '/api/onboarding/status', true),
    requestJson<OperationLogDto[]>(config, '/api/logs?limit=80', true),
    requestJson<WorkspaceDto>(config, '/api/workspace', true),
  ])
  const installedApps = await fetchInstalledApps(config, devices)

  return { fetchedAt, config, health, ready, devices, catalog, appleAccess, personalApple, installedApps, apps, anisette, onboarding, logs, workspace }
}

async function fetchInstalledApps(config: ApiConfig, devices: ApiResult<DeviceDto[]>): Promise<ApiResult<InstalledAppDto[]>> {
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

export async function registerSideportApp(payload: AppRegistrationPayload, config = getSideportApiConfig()) {
  return mutateJson<RegisteredAppDto>(config, '/api/apps', {
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

export async function refreshSideportApp(deviceUdid: string, bundleId: string, config = getSideportApiConfig()) {
  return mutateJson<RefreshResultDto>(config, `/api/apps/${encodeURIComponent(deviceUdid)}/${encodeURIComponent(bundleId)}/refresh`, {
    method: 'POST',
  })
}

export async function signInPersonalApple(payload: PersonalAppleSignInPayload, config = getSideportApiConfig()) {
  return mutateJson<PersonalAppleStatusDto>(config, '/api/apple-access/personal/sign-in', {
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
  if (config.token) headers.set('Authorization', `Bearer ${config.token}`)

  const response = await fetch(joinUrl(config.baseUrl, path), { ...init, headers, credentials: 'same-origin' })
  if (!response.ok) throw new Error(await responseError(response))
  if (response.status === 204) return undefined as T
  return await response.json() as T
}

async function responseError(response: Response): Promise<string> {
  const contentType = response.headers.get('content-type') ?? ''
  if (contentType.includes('application/json')) {
    const body = await response.json().catch(() => null) as { error?: string; message?: string } | null
    if (body?.error) return body.error
    if (body?.message) return body.message
  }
  const text = await response.text().catch(() => '')
  return text || `${response.status} ${response.statusText || 'HTTP error'}`
}

function buildAdminData(snapshot: ApiSnapshot): { data: SideportReadModel; status: AdminDataStatus } {
  const hasLiveCore = snapshot.health.ok || snapshot.ready.ok || snapshot.devices.ok || snapshot.apps.ok
  if (!hasLiveCore) return buildUnavailableData(snapshot.config, snapshot.health.error ?? 'Sideport API is not reachable.')

  const catalogApps = snapshot.catalog.ok && snapshot.catalog.data ? snapshot.catalog.data.map(toCatalogApp).filter(isPresent) : runtimeEmptyData.catalogApps
  const appleAccess = snapshot.appleAccess.ok && snapshot.appleAccess.data ? toAppleAccess(snapshot.appleAccess.data) : unavailableAppleAccess(snapshot.appleAccess.error)
  const personalApple = snapshot.personalApple.ok && snapshot.personalApple.data ? toPersonalApple(snapshot.personalApple.data) : unavailablePersonalApple(snapshot.personalApple.error)
  const apps = snapshot.apps.ok && snapshot.apps.data ? snapshot.apps.data.map((app) => toRegisteredApp(app, catalogApps)).filter(isPresent) : runtimeEmptyData.apps
  const installedApps = snapshot.installedApps.data ? snapshot.installedApps.data.map((app) => toInstalledApp(app, apps)).filter(isPresent) : runtimeEmptyData.installedApps
  const devices = snapshot.devices.ok && snapshot.devices.data
    ? snapshot.devices.data.map((device) => toDeviceSummary(device, apps, installedApps)).filter(isPresent)
    : runtimeEmptyData.devices
  const renewals = apps.length ? apps.map(toRenewalItem) : runtimeEmptyData.renewals
  const logs = snapshot.logs.ok && snapshot.logs.data ? snapshot.logs.data.map((entry) => toOperationLog(entry, snapshot.fetchedAt)).filter(isPresent) : runtimeEmptyData.logs
  const issues = buildIssues(snapshot, apps)
  const system = buildSystemStatus(snapshot)

  const partial = [snapshot.health, snapshot.ready, snapshot.devices, snapshot.catalog, snapshot.appleAccess, snapshot.personalApple, snapshot.installedApps, snapshot.apps, snapshot.anisette, snapshot.onboarding, snapshot.logs].some((result) => !result.ok)
  const status: AdminDataStatus = {
    mode: partial ? 'partial' : 'live',
    baseUrl: snapshot.config.baseUrl,
    lastUpdatedAt: snapshot.fetchedAt,
    canMutate: snapshot.config.canMutate,
    onboarding: snapshot.onboarding.data ? toOnboardingStatus(snapshot.onboarding.data) : buildFallbackOnboarding(system, devices, apps),
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
      issues,
      activity: buildActivity(snapshot, partial, logs),
      logs,
      workspace: snapshot.workspace.ok && snapshot.workspace.data ? toWorkspace(snapshot.workspace.data) : runtimeEmptyData.workspace,
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
      onboarding: buildFallbackOnboarding(fallbackSystem, [], []),
      message,
    },
  }
}

function buildSystemStatus(snapshot: ApiSnapshot): SystemStatus {
  const ready = snapshot.ready.data
  const tokenMissing = [snapshot.devices, snapshot.catalog, snapshot.installedApps, snapshot.apps, snapshot.anisette].some((result) => result.status === 401)

  return {
    api: { ok: snapshot.health.ok && snapshot.health.data?.ok !== false, source: 'live' },
    ready: {
      ready: Boolean(ready?.ready),
      source: 'live',
      checks: {
        anisette: {
          ok: Boolean(ready?.checks.anisette.ok),
          error: ready?.checks.anisette.error ?? snapshot.ready.error ?? null,
          source: 'live',
        },
        signer: {
          ok: Boolean(ready?.checks.signer.ok),
          path: ready?.checks.signer.path ?? 'Unknown',
          source: 'live',
        },
      },
    },
    apiAuth: { configured: Boolean(snapshot.config.token) || tokenMissing, source: 'derived' },
    scheduler: { enabled: false, source: 'planned' },
    observability: snapshot.logs.ok
      ? { exporter: 'API log ring buffer', connected: true, source: 'live' }
      : { exporter: 'OTLP not connected', connected: false, source: 'planned' },
  }
}

function toDeviceSummary(device: DeviceDto, apps: RegisteredAppSummary[], installedApps: InstalledAppSummary[]) {
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
    lastSeenAt: { value: new Date().toISOString(), source: 'live' as const },
    health: healthFromExpiry(nearestExpiry, hasLastError),
    teamId: deviceApps[0]?.teamId || 'Unknown',
    appSlotsUsed: deviceApps.length,
    installedAppCount: deviceInstalledApps.length,
    unmanagedAppCount: deviceInstalledApps.filter((app) => !app.managedBySideport).length,
    nearestExpiryAt: nearestExpiry ? { value: nearestExpiry, source: 'live' as const } : undefined,
    blocker: hasLastError ? 'One app has a failed refresh/install state.' : undefined,
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

function toCatalogApp(app: CatalogAppDto): CatalogAppSummary | null {
  if (!app.id || !app.name || !app.bundleId || !app.ipaPath) return null
  const status = normalizeCatalogStatus(app.status)
  return {
    id: app.id,
    name: app.name,
    purpose: app.purpose || 'Server-side IPA catalog entry.',
    expectedBundleId: app.bundleId,
    suggestedIpaPath: app.ipaPath,
    versionLabel: versionLabel(app.shortVersion, app.version),
    status,
    statusLabel: catalogStatusLabel(status),
    source: status === 'missing' ? 'planned' : 'live',
    iconTone: toneFromBundleId(app.bundleId),
    notes: app.notes?.length ? app.notes : [catalogStatusLabel(status)],
    sizeBytes: app.sizeBytes ?? undefined,
    sha256: app.sha256 ?? undefined,
    hasEmbeddedProfile: Boolean(app.hasEmbeddedProfile),
    signatureExpiresAt: app.signatureExpiresAt ?? undefined,
    lastInspectedAt: app.lastInspectedAt ?? undefined,
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
    appleIdHint: dto.appleIdHint ?? null,
    message: dto.message || 'Personal Apple ID connector returned without a message.',
    pendingChallengeId: dto.pendingChallengeId ?? null,
    pendingChallengeKind: dto.pendingChallengeKind ?? null,
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
    blocker: app.lastError ?? undefined,
    source: 'live',
  }
}

function buildIssues(snapshot: ApiSnapshot, apps: RegisteredAppSummary[]): DiagnosticIssue[] {
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
      operationId: `refresh:${app.deviceUdid}:${app.bundleId}`,
      traceId: 'trace-pending-otel',
      spanSummary: [{ name: 'sideport.refresh', durationMs: 0, state: 'failed' }],
      logSnippet: app.lastError,
      source: 'live',
    })
  }

  return issues
}

function toOnboardingStatus(dto: OnboardingStatusDto): OnboardingStatus {
  const steps = dto.steps?.map(toOnboardingStep).filter(isPresent) ?? []
  return {
    firstRunComplete: Boolean(dto.firstRunComplete),
    schedulerEnabled: Boolean(dto.schedulerEnabled),
    steps,
  }
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

function buildFallbackOnboarding(system: SystemStatus, devices: Array<{ appSlotsUsed?: number }>, apps: RegisteredAppSummary[]): OnboardingStatus {
  const steps: OnboardingStep[] = [
    {
      id: 'api-auth',
      label: 'Protect the API',
      description: 'Set SIDEPORT_API_TOKEN or keep the portal behind trusted LAN/proxy auth.',
      state: system.apiAuth.configured ? 'complete' : 'warning',
      surface: 'portal',
      required: true,
      settingsPath: null,
      detail: system.apiAuth.configured ? 'Bearer token configured.' : 'Token not detected in the current UI session.',
      source: system.apiAuth.source,
    },
    {
      id: 'anisette',
      label: 'Trust anisette identity',
      description: 'Use the provisioned host anisette identity so GrandSlam login does not trigger first-run 2FA repeatedly.',
      state: system.ready.checks.anisette.ok ? 'complete' : 'blocked',
      surface: 'portal',
      required: true,
      settingsPath: null,
      detail: system.ready.checks.anisette.error ?? 'Anisette client info available.',
      source: system.ready.checks.anisette.source,
    },
    {
      id: 'signer',
      label: 'Verify signer binary',
      description: 'Sideport needs the patched zsign binary before any refresh can be run.',
      state: system.ready.checks.signer.ok ? 'complete' : 'blocked',
      surface: 'portal',
      required: true,
      settingsPath: null,
      detail: system.ready.checks.signer.path,
      source: system.ready.checks.signer.source,
    },
    {
      id: 'device',
      label: 'Connect a device',
      description: 'A reachable USB or Wi-Fi device is required before registering apps.',
      state: devices.length ? 'complete' : 'pending',
      surface: 'portal',
      required: true,
      settingsPath: null,
      detail: devices.length ? `${devices.length} reachable device(s).` : 'No reachable devices in the current read model.',
      source: 'derived',
    },
    {
      id: 'iphone-trust-computer',
      label: 'Trust this computer',
      description: 'On the iPhone, keep the screen awake, connect over USB, tap Trust, and enter the passcode if prompted.',
      state: devices.length ? 'complete' : 'pending',
      surface: 'iphone',
      required: false,
      settingsPath: null,
      detail: devices.length ? 'Device discovery works from the host.' : 'Needed before Sideport can see a new iPhone.',
      source: 'derived',
    },
    {
      id: 'iphone-developer-mode',
      label: 'Enable Developer Mode',
      description: 'On iOS 16+, open Settings > Privacy & Security > Developer Mode, enable it, then restart when prompted.',
      state: apps.length ? 'warning' : 'pending',
      surface: 'iphone',
      required: false,
      settingsPath: 'Settings > Privacy & Security > Developer Mode',
      detail: 'Required before development-signed apps can launch on newer iOS.',
      source: 'planned',
    },
    {
      id: 'iphone-profile-trust',
      label: 'Trust the developer profile',
      description: 'After the first install, open Settings > General > VPN & Device Management, choose the Apple Development profile, then tap Trust.',
      state: apps.length ? 'warning' : 'pending',
      surface: 'iphone',
      required: false,
      settingsPath: 'Settings > General > VPN & Device Management',
      detail: 'Only appears on the iPhone after the first app is installed.',
      source: 'planned',
    },
    {
      id: 'iphone-keep-awake',
      label: 'Keep the iPhone awake during install',
      description: 'Leave the iPhone unlocked on the same network while Sideport signs and installs the app.',
      state: 'pending',
      surface: 'iphone',
      required: false,
      settingsPath: null,
      detail: 'Prevents install failures caused by the device going unreachable.',
      source: 'planned',
    },
    {
      id: 'first-app',
      label: 'Register first app',
      description: 'Add an IPA path, Apple ID, team, device, and bundle ID before enabling manual refresh.',
      state: apps.length ? 'complete' : 'pending',
      surface: 'portal',
      required: true,
      settingsPath: null,
      detail: apps.length ? `${apps.length} app registration(s).` : 'No registered apps yet.',
      source: apps.length ? 'live' : 'planned',
    },
  ]

  return {
    firstRunComplete: steps.every((step) => !step.required || step.state === 'complete'),
    schedulerEnabled: system.scheduler.enabled,
    steps,
  }
}

function normalizeStepState(state: string | undefined): OnboardingStepState {
  if (state === 'complete' || state === 'warning' || state === 'blocked') return state
  return 'pending'
}

function normalizeSurface(surface: string | undefined): OnboardingStep['surface'] {
  return surface === 'iphone' ? 'iphone' : 'portal'
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
    traceId: 'trace-pending-otel',
    spanSummary: [{ name: 'sideport.admin.fetch', durationMs: 0, state: 'failed' }],
    logSnippet: detail,
    source: 'live',
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

function normalizeConnection(value: DeviceDto['connection']): ConnectionState {
  if (value === 0) return 'usb'
  if (value === 1) return 'wifi'
  if (typeof value === 'string' && value.toLowerCase() === 'wifi') return 'wifi'
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

function riskForApp(app: RegisteredAppSummary): RenewalRisk {
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