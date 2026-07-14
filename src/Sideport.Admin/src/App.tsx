import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode, type RefObject } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Activity,
  AlertTriangle,
  Apple,
  Cable,
  CheckCircle2,
  ChevronRight,
  CircleDashed,
  Command,
  Filter,
  Gauge,
  HardDrive,
  History,
  KeyRound,
  Loader2,
  Network,
  Package,
  Play,
  Plus,
  RefreshCw,
  Search,
  Settings,
  ShieldCheck,
  Smartphone,
  Stethoscope,
  Terminal,
  TimerReset,
  UserPlus,
  Users,
  Wifi,
  X,
  XCircle,
  type LucideIcon,
} from 'lucide-react'
import * as Dialog from '@radix-ui/react-dialog'
import {
  flexRender,
  getCoreRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnDef,
  type SortingState,
} from '@tanstack/react-table'
import './App.css'
import { AddAppDialog, AddIPhoneDialog, GlobalAddMenu, type AddAppCatalogItem, type AddAppServices, type AddIPhoneServices, type EnrollmentCandidate, type EnrollmentOperation, type GitHubCallbackResume } from './add-flows/AddFlows'
import { cancelOperation, completePersonalAppleTwoFactor, completeReplacementAppleTwoFactor, completeSideportOnboarding, connectPersonalApple, connectReplacementAppleAccount, createGitHubCatalogConnection, cutoverPersonalAppleSigning, getGitHubCatalogConnection, getSideportOperation, getStoredSideportApiToken, importGitHubCatalogApp, inspectCatalogAppV2, installSideportCatalogApp, listCatalogImportRoots, listGitHubCatalogReleases, listGitHubCatalogSources, listReachableDevices, preflightPersonalAppleSigning, preflightSideportInstall, preflightSideportRefresh, reconcileSideportOperation, refreshSideportApp, registerPendingSideportApp, rerunOperation, retryOperation, runDeviceDiagnostics, saveSideportApiToken, selectPersonalAppleTeam, SideportApiError, signInPersonalApple, startDeviceEnrollment, updateSideportSchedulerSettings, uploadCatalogIpaV2, useSideportAdminData, type AdminDataStatus, type AppleAccountReplacementCandidateDto, type AppRegistrationDto, type CatalogAppV2Dto, type DeviceDiagnosticsDto, type InstallOperationPayload, type InstallPreflightPayload, type OnboardingCompletionPayload, type OperationPreflightDto, type OperationRecordDto, type PendingAppRegistrationPayload, type PersonalAppleSigningPreflightDto, type ReachableDeviceDto, type SchedulerStatusDto } from './api/sideportApi'
import {
  runtimeEmptyData,
  type ActivityEvent,
  type CatalogAppSummary,
  type DeviceSummary,
  type DiagnosticIssue,
  type HealthState,
  type InstalledAppSummary,
  type IssueStatus,
  type MemberStatus,
  type OperationLogEntry,
  type OperationStageSummary,
  type PersonalAppleSummary,
  type RegisteredAppSummary,
  type RenewalItem,
  type RenewalRisk,
  type RenewalStatus,
  type Severity,
  type SideportReadModel,
  type SourceKind,
  type SystemStatus,
  type AppleAccessCapabilitySummary,
  type AppleAccessSummary,
  type WorkspaceRole,
  type WorkspaceSummary,
} from './data/sideportTypes'
import { compactUdid, relativeTime, shortDateTime, sourceLabel, timeUntil } from './lib/format'
import { RuntimeFirstRunOnboarding } from './onboarding/RuntimeFirstRunOnboarding'

export type RouteId = 'home' | 'apps' | 'devices' | 'people' | 'activity' | 'settings' | 'device-detail' | 'install-app'

const routeItems: Array<{ id: RouteId; label: string; icon: LucideIcon }> = [
  { id: 'home', label: 'Home', icon: Gauge },
  { id: 'apps', label: 'Apps', icon: Package },
  { id: 'devices', label: 'Devices', icon: Smartphone },
  { id: 'people', label: 'People', icon: Users },
  { id: 'activity', label: 'Activity', icon: Activity },
  { id: 'settings', label: 'Settings', icon: Settings },
]

const healthCopy: Record<HealthState, string> = {
  healthy: 'Healthy',
  warning: 'Warning',
  blocked: 'Blocked',
  failed: 'Failed',
  offline: 'Offline',
}

const riskCopy: Record<RenewalRisk, string> = {
  blocked: 'Blocked',
  'due-now': 'Due now',
  upcoming: 'Upcoming',
  healthy: 'Healthy',
  unknown: 'Unknown',
}

const statusCopy: Record<RenewalStatus, string> = {
  idle: 'Idle',
  running: 'Running',
  queued: 'Queued',
  failed: 'Failed',
  blocked: 'Blocked',
}

export interface SideportAdminAppProps {
  data?: SideportReadModel
  apiStatus?: AdminDataStatus
  initialRoute?: RouteId
  initialCommandOpen?: boolean
  initialSetupOpen?: boolean
  addIPhoneServices?: AddIPhoneServices
  addAppServices?: AddAppServices
  installAppService?: (payload: InstallOperationPayload) => Promise<OperationRecordDto>
  preflightInstallService?: (payload: InstallPreflightPayload) => Promise<OperationPreflightDto>
  readOperationService?: (operationId: string) => Promise<OperationRecordDto>
  reconcileOperationService?: (operationId: string, payload: { idempotencyKey: string; note?: string }) => Promise<OperationRecordDto>
  completeOnboardingService?: (payload: OnboardingCompletionPayload) => Promise<unknown>
  registerPendingAppService?: (payload: PendingAppRegistrationPayload) => Promise<AppRegistrationDto>
  schedulerSettingsService?: (enabled: boolean) => Promise<SchedulerStatusDto>
  onApiTokenSaved?: () => void
}

const runtimeStatus: AdminDataStatus = {
  mode: 'unavailable',
  baseUrl: '/sideport-api',
  message: 'Waiting for the .NET API.',
  canMutate: false,
}

function newUiIdempotencyKey(scope: string): string {
  const suffix = typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(36).slice(2)}`
  return `ui-${scope}-${suffix}`
}

function catalogDtoToAddItem(app: CatalogAppV2Dto): AddAppCatalogItem {
  return {
    id: app.id,
    name: app.name,
    purpose: app.purpose,
    versionLabel: app.shortVersion || app.version || 'Version unavailable',
    status: app.status,
    artifactSources: app.artifactSources,
    icon: app.icon ?? undefined,
  }
}

function operationToEnrollment(record: OperationRecordDto): EnrollmentOperation {
  return {
    operationId: record.operationId ?? '',
    status: record.status ?? 'unknown',
    stages: (record.stages ?? []).map((stage) => ({ id: stage.id ?? '', status: stage.status ?? 'pending', message: stage.message ?? '' })),
    result: record.result ? { deviceEnrollment: record.result.deviceEnrollment } : null,
    error: record.error ? { code: record.error.code, message: record.error.message } : null,
    retryable: record.retryable === true,
    candidateDevices: record.candidateDevices?.map((candidate) => ({
      udidSuffix: candidate.udidSuffix ?? '',
      name: candidate.name ?? 'Connected iPhone',
      productType: candidate.productType,
      osVersion: candidate.osVersion,
      connection: candidate.connection ?? 'usb',
    })) ?? null,
  }
}

function githubCallbackFromLocation(): GitHubCallbackResume | null {
  if (typeof window === 'undefined') return null
  const params = new URLSearchParams(window.location.search)
  const connectionId = params.get('githubConnection')?.trim() ?? ''
  const sourceId = params.get('source')?.trim() ?? ''
  if (!connectionId || connectionId.length > 256 || sourceId.length > 256) return null
  return { connectionId, sourceId: sourceId || null }
}

function clearGitHubCallbackFromLocation(): void {
  if (typeof window === 'undefined') return
  const url = new URL(window.location.href)
  if (!url.searchParams.has('githubConnection')) return
  url.searchParams.delete('githubConnection')
  url.searchParams.delete('source')
  window.history.replaceState(window.history.state, '', `${url.pathname}${url.search}${url.hash}`)
}

function isUsbDevice(device: ReachableDeviceDto): boolean {
  return device.connection === 0 || (typeof device.connection === 'string' && device.connection.toLowerCase() === 'usb')
}

function authoritativeCandidateUdid(candidate: EnrollmentCandidate, devices: ReachableDeviceDto[]): string | null {
  const suffix = candidate.udidSuffix.trim().toLowerCase()
  if (suffix.length !== 8 || candidate.connection.toLowerCase() !== 'usb') return null
  const matches = devices.filter((device) =>
    Boolean(device.udid)
    && isUsbDevice(device)
    && device.udid!.toLowerCase().endsWith(suffix))
  return matches.length === 1 ? matches[0].udid! : null
}

const ACTIVE_INSTALL_STATUSES = new Set(['queued', 'waiting', 'running'])
const ONBOARDING_INSTALL_SESSION_PREFIX = 'sideport.onboarding.install.v1:'

function attemptCompletionChime(): void {
  try {
    const AudioContextClass = window.AudioContext ?? (window as typeof window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
    if (!AudioContextClass) return
    const context = new AudioContextClass()
    const oscillator = context.createOscillator()
    const gain = context.createGain()
    oscillator.frequency.setValueAtTime(660, context.currentTime)
    oscillator.frequency.exponentialRampToValueAtTime(880, context.currentTime + 0.16)
    gain.gain.setValueAtTime(0.0001, context.currentTime)
    gain.gain.exponentialRampToValueAtTime(0.12, context.currentTime + 0.02)
    gain.gain.exponentialRampToValueAtTime(0.0001, context.currentTime + 0.28)
    oscillator.connect(gain).connect(context.destination)
    oscillator.start()
    oscillator.stop(context.currentTime + 0.3)
    oscillator.addEventListener('ended', () => void context.close(), { once: true })
  } catch {
    // Browser audio is best-effort and never changes verified install state.
  }
}

function hasCapability(data: SideportReadModel, status: AdminDataStatus, capability: string): boolean {
  return status.canMutate
    && data.workspace.source !== 'planned'
    && data.workspace.authMode !== 'open-behind-proxy'
    && data.workspace.capabilities?.[capability] === true
}

function onboardingInstallSessionKey(baseUrl: string): string {
  return `${ONBOARDING_INSTALL_SESSION_PREFIX}${encodeURIComponent((baseUrl || '/').split(/[?#]/, 1)[0])}`
}

function readRememberedOperationId(key: string): string | null {
  try {
    const value = window.sessionStorage.getItem(key)?.trim() ?? ''
    return value && value.length <= 256 ? value : null
  } catch {
    return null
  }
}

function rememberOperationId(key: string, operationId: string | null): void {
  try {
    if (operationId) window.sessionStorage.setItem(key, operationId)
    else window.sessionStorage.removeItem(key)
  } catch {
    // The workflow activeOperationId remains the durable resume source.
  }
}

function replacementInstallPreflight(error: unknown): OperationPreflightDto | null {
  if (!(error instanceof SideportApiError) || error.code !== 'install-preflight-stale') return null
  const data = error.data
  if (!data || typeof data !== 'object') return null
  const record = data as Record<string, unknown>
  const candidate = (record.preflight ?? record.replacementPreflight ?? data) as Partial<OperationPreflightDto>
  if (typeof candidate.ready !== 'boolean' || !Array.isArray(candidate.blockers) || !Array.isArray(candidate.warnings) || !Array.isArray(candidate.plannedMutations) || !Array.isArray(candidate.scarceLimits)) return null
  return candidate as OperationPreflightDto
}

function preferredAcceptedUsbDevice(data: SideportReadModel): DeviceSummary | undefined {
  const candidates = data.devices.filter((device) =>
    device.inventoryState === 'accepted'
    && device.connection === 'usb'
    && device.usableForInstall !== false)
  return candidates.find((device) => device.supportedForFirstInstall) ?? candidates[0]
}

function operationFailure(record: OperationRecordDto): string | null {
  if (ACTIVE_INSTALL_STATUSES.has(record.status ?? '')) return null
  return record.error?.message ?? record.error?.detail ?? (record.status === 'failed' || record.status === 'blocked' ? 'Sideport could not start this install.' : null)
}

export function SideportAdminApp({ data, apiStatus, initialRoute = 'home', initialCommandOpen = false, initialSetupOpen = false, addIPhoneServices: injectedAddIPhoneServices, addAppServices: injectedAddAppServices, installAppService: injectedInstallAppService, preflightInstallService: injectedPreflightInstallService, readOperationService: injectedReadOperationService, reconcileOperationService: injectedReconcileOperationService, completeOnboardingService: injectedCompleteOnboardingService, registerPendingAppService: injectedRegisterPendingAppService, schedulerSettingsService: injectedSchedulerSettingsService, onApiTokenSaved }: SideportAdminAppProps) {
  const queryClient = useQueryClient()
  const viewData = data ?? runtimeEmptyData
  const viewStatus = apiStatus ?? runtimeStatus
  const catalogApps = viewData.catalogApps
  const [route, setRoute] = useState<RouteId>(initialRoute)
  const [setupOpen, setSetupOpen] = useState(initialSetupOpen)
  const [selectedCatalogAppId, setSelectedCatalogAppId] = useState('')
  const [selectedDeviceUdid, setSelectedDeviceUdid] = useState(viewData.devices[0]?.udid ?? '')
  const [commandOpen, setCommandOpen] = useState(initialCommandOpen)
  const [addIPhoneOpen, setAddIPhoneOpen] = useState(false)
  const [addIPhoneAutoStart, setAddIPhoneAutoStart] = useState(false)
  const [githubCallback, setGitHubCallback] = useState<GitHubCallbackResume | null>(() => githubCallbackFromLocation())
  const [addAppOpen, setAddAppOpen] = useState(Boolean(githubCallback))
  const [installRequestPending, setInstallRequestPending] = useState(false)
  const [installRequestError, setInstallRequestError] = useState<string | null>(null)
  const onboardingInstallKey = useMemo(() => onboardingInstallSessionKey(viewStatus.baseUrl), [viewStatus.baseUrl])
  const [submittedInstallOperationId, setSubmittedInstallOperationId] = useState<string | null>(() => readRememberedOperationId(onboardingInstallSessionKey(viewStatus.baseUrl)))
  const [onboardingPreflight, setOnboardingPreflight] = useState<OperationPreflightDto | null>(null)
  const [onboardingInstallOperation, setOnboardingInstallOperation] = useState<OperationRecordDto | null>(null)
  const [ignoredInstallOperationId, setIgnoredInstallOperationId] = useState<string | null>(null)
  const [installPollError, setInstallPollError] = useState<string | null>(null)
  const [finalizationPending, setFinalizationPending] = useState(false)
  const [reconciliationPending, setReconciliationPending] = useState(false)
  const [appSelectionPending, setAppSelectionPending] = useState(false)
  const [appSelectionError, setAppSelectionError] = useState<string | null>(null)
  const addFlowReturnFocusRef = useRef<HTMLElement | null>(null)
  const globalAddTriggerRef = useRef<HTMLButtonElement | null>(null)
  const installRequestInFlightRef = useRef(false)
  const appSelectionInFlightRef = useRef(false)
  const installApp = injectedInstallAppService ?? installSideportCatalogApp
  const preflightInstall = injectedPreflightInstallService ?? preflightSideportInstall
  const readOperation = injectedReadOperationService ?? getSideportOperation
  const reconcileOperation = injectedReconcileOperationService ?? reconcileSideportOperation
  const finalizeOnboarding = injectedCompleteOnboardingService ?? completeSideportOnboarding
  const registerPendingApp = injectedRegisterPendingAppService ?? registerPendingSideportApp
  const canManageAppleSigner = hasCapability(viewData, viewStatus, 'apple.signer.manage')
  const canAddIPhone = hasCapability(viewData, viewStatus, 'devices.enroll')
  const canImportCatalog = hasCapability(viewData, viewStatus, 'catalog.import')
  const canManageGitHub = hasCapability(viewData, viewStatus, 'integrations.github.manage')
  const canRunOperations = hasCapability(viewData, viewStatus, 'operations.run')
  const canCompleteOnboarding = hasCapability(viewData, viewStatus, 'onboarding.complete')
  const canManageScheduler = hasCapability(viewData, viewStatus, 'scheduler.manage')
  const workflowInstallStep = viewStatus.onboarding?.workflow?.steps.find((step) => step.id === 'install')
  const selectedDevice = viewData.devices.find((device) => device.udid === selectedDeviceUdid) ?? viewData.devices[0]
  const openDevice = (device: DeviceSummary) => {
    setSelectedDeviceUdid(device.udid)
    setRoute('device-detail')
  }
  const openInstallPage = (catalogAppId = selectedCatalogAppId) => {
    setSelectedCatalogAppId(catalogAppId || catalogApps[0]?.id || '')
    setRoute('install-app')
  }
  const continueAfterIPhone = () => {
    setAddIPhoneOpen(false)
    setAddIPhoneAutoStart(false)
    setRoute('apps')
    void refreshAdminData()
  }
  const rememberAddFlowTrigger = () => {
    const activeElement = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const transientOrigin = activeElement?.closest('.add-menu-popover, .command-content')
    addFlowReturnFocusRef.current = transientOrigin ? globalAddTriggerRef.current : activeElement
  }
  const openAddIPhone = () => {
    rememberAddFlowTrigger()
    setAddAppOpen(false)
    setAddIPhoneAutoStart(false)
    setAddIPhoneOpen(true)
  }
  const startOnboardingIPhone = () => {
    rememberAddFlowTrigger()
    setAddAppOpen(false)
    setAddIPhoneAutoStart(true)
    setAddIPhoneOpen(true)
  }
  const handleAddIPhoneOpenChange = (nextOpen: boolean) => {
    setAddIPhoneOpen(nextOpen)
    if (!nextOpen) setAddIPhoneAutoStart(false)
  }
  const openAddApp = () => {
    rememberAddFlowTrigger()
    setGitHubCallback(null)
    setAddIPhoneOpen(false)
    setAddAppOpen(true)
  }
  const handleAddAppOpenChange = (nextOpen: boolean) => {
    setAddAppOpen(nextOpen)
    if (!nextOpen) setGitHubCallback(null)
  }
  const refreshAdminData = () => queryClient.invalidateQueries({ queryKey: ['sideport-admin-data'] })
  const selectOnboardingApp = async (catalogAppId: string, acceptFreshImport = false): Promise<boolean> => {
    if (appSelectionInFlightRef.current) return false
    setInstallRequestError(null)
    setOnboardingPreflight(null)
    if (!onboardingInstallOperation || !ACTIVE_INSTALL_STATUSES.has(onboardingInstallOperation.status ?? '')) {
      setIgnoredInstallOperationId(onboardingInstallOperation?.operationId ?? workflowInstallStep?.activeOperationId ?? null)
      setOnboardingInstallOperation(null)
    }
    setSelectedCatalogAppId(catalogAppId)
    setAppSelectionError(null)
    if (viewStatus.mode === 'demo') return true
    const catalogApp = catalogApps.find((app) => app.id === catalogAppId && app.status === 'ready')
    const device = preferredAcceptedUsbDevice(viewData) ?? viewData.devices.find((candidate) => candidate.inventoryState === 'accepted')
    const accountProfileId = viewData.personalApple.accountProfileId?.trim() ?? ''
    if ((!catalogApp && !acceptFreshImport) || !catalogAppId.trim() || !device || !accountProfileId || !canImportCatalog) {
      setAppSelectionError('Sideport needs an accepted iPhone, connected Apple account, and permission to save this app choice.')
      return false
    }
    appSelectionInFlightRef.current = true
    setAppSelectionPending(true)
    try {
      await registerPendingApp({ catalogAppId, deviceUdid: device.udid, accountProfileId, lifecycle: 'pending-install' })
      await refreshAdminData()
      return true
    } catch (reason) {
      setAppSelectionError(reason instanceof Error ? reason.message : 'Sideport could not save this app choice.')
      void refreshAdminData()
      return false
    } finally {
      setAppSelectionPending(false)
      appSelectionInFlightRef.current = false
    }
  }
  const chooseAppFromAddFlow = (app: AddAppCatalogItem) => {
    setSelectedCatalogAppId(app.id)
    setInstallRequestError(null)
    setRoute('install-app')
    void refreshAdminData()
  }
  const onboardingInstallTarget = useCallback((catalogAppId: string) => {
    const catalogApp = catalogApps.find((app) => app.id === catalogAppId && app.status === 'ready')
    const device = preferredAcceptedUsbDevice(viewData)
    const selectedTeam = viewData.personalApple.teams.find((team) => team.teamId === viewData.personalApple.selectedTeamId)
    const accountProfileId = viewData.personalApple.accountProfileId?.trim() ?? ''
    const blocker = !catalogApp
      ? 'Choose an app that Sideport has inspected.'
      : !device
        ? 'Connect an accepted iPhone by USB before the first install.'
        : viewData.personalApple.state !== 'authenticated' || !accountProfileId || !selectedTeam
          ? 'Finish Apple sign-in and choose a team returned by Apple.'
          : !canRunOperations
            ? 'Sign in to a protected Sideport session before installing.'
            : null
    return { catalogApp, device, accountProfileId, blocker }
  }, [canRunOperations, catalogApps, viewData])

  const prepareOnboardingInstall = useCallback(async (catalogAppId: string) => {
    if (installRequestInFlightRef.current) return
    const { catalogApp, device, accountProfileId, blocker } = onboardingInstallTarget(catalogAppId)
    if (blocker || !catalogApp || !device || !accountProfileId) {
      setInstallRequestError(blocker ?? 'Sideport is missing an install requirement.')
      return
    }
    installRequestInFlightRef.current = true
    setInstallRequestPending(true)
    setInstallRequestError(null)
    setOnboardingPreflight(null)
    if (!onboardingInstallOperation || !ACTIVE_INSTALL_STATUSES.has(onboardingInstallOperation.status ?? '')) {
      setIgnoredInstallOperationId(onboardingInstallOperation?.operationId ?? workflowInstallStep?.activeOperationId ?? null)
      setOnboardingInstallOperation(null)
      setSubmittedInstallOperationId(null)
      rememberOperationId(onboardingInstallKey, null)
    }
    try {
      await registerPendingApp({ catalogAppId: catalogApp.id, deviceUdid: device.udid, accountProfileId, lifecycle: 'pending-install' })
      setOnboardingPreflight(await preflightInstall({ deviceUdid: device.udid, bundleId: catalogApp.expectedBundleId, catalogAppId: catalogApp.id, accountProfileId, finishOnboarding: true }))
    } catch (reason) {
      setInstallRequestError(reason instanceof Error ? reason.message : 'Sideport could not check this install.')
    } finally {
      setInstallRequestPending(false)
      installRequestInFlightRef.current = false
    }
  }, [onboardingInstallKey, onboardingInstallOperation, onboardingInstallTarget, preflightInstall, registerPendingApp, workflowInstallStep?.activeOperationId])

  const startOnboardingInstall = async (catalogAppId: string) => {
    const workflowBlocksNewInstall = workflowInstallStep?.state === 'in-progress'
      || workflowInstallStep?.nextAction?.action === 'retry-finalization'
      || workflowInstallStep?.nextAction?.action === 'reconcile-install'
    if (installRequestInFlightRef.current || submittedInstallOperationId || workflowBlocksNewInstall) return
    const { catalogApp, device, accountProfileId, blocker } = onboardingInstallTarget(catalogAppId)
    if (blocker || !catalogApp || !device || !accountProfileId || !onboardingPreflight?.preflightId || !onboardingPreflight.planVersion || !onboardingPreflight.ready) {
      setInstallRequestError(blocker ?? 'Review the current install checks before continuing.')
      return
    }

    installRequestInFlightRef.current = true
    setInstallRequestPending(true)
    setInstallRequestError(null)
    try {
      const record = await installApp({
        deviceUdid: device.udid,
        bundleId: catalogApp.expectedBundleId,
        catalogAppId: catalogApp.id,
        accountProfileId,
        preflightId: onboardingPreflight.preflightId,
        planVersion: onboardingPreflight.planVersion,
        finishOnboarding: true,
        confirmedPlannedMutations: true,
        idempotencyKey: newUiIdempotencyKey('onboarding-install'),
      })
      const failure = operationFailure(record)
      if (failure) setInstallRequestError(failure)
      else if (record.operationId) {
        setIgnoredInstallOperationId((ignored) => ignored === record.operationId ? null : ignored)
        setSubmittedInstallOperationId(record.operationId)
        setOnboardingInstallOperation(record)
        rememberOperationId(onboardingInstallKey, record.operationId)
      }
      else setInstallRequestError('Sideport accepted the request without returning an operation to follow.')
      await refreshAdminData()
    } catch (reason) {
      const replacement = replacementInstallPreflight(reason)
      if (replacement) {
        setOnboardingPreflight(replacement)
        setInstallRequestError('The install plan changed. Review the updated checks, then press Install and finish again.')
      } else setInstallRequestError(reason instanceof Error ? reason.message : 'Sideport could not start the install.')
    } finally {
      setInstallRequestPending(false)
      installRequestInFlightRef.current = false
    }
  }
  const runtimeAddIPhoneServices = useMemo<AddIPhoneServices>(() => ({
    start: async (deviceUdid) => operationToEnrollment(await startDeviceEnrollment({ idempotencyKey: newUiIdempotencyKey('device-enrollment'), deviceUdid })),
    read: async (operationId) => operationToEnrollment(await getSideportOperation(operationId)),
    retry: async (operationId) => operationToEnrollment(await retryOperation(operationId)),
    selectCandidate: async (candidate) => {
      const deviceUdid = authoritativeCandidateUdid(candidate, await listReachableDevices())
      if (!deviceUdid) throw new Error('Sideport could not safely match that iPhone. Leave only one new iPhone connected over USB, close this window, then start again.')
      return operationToEnrollment(await startDeviceEnrollment({ idempotencyKey: newUiIdempotencyKey('device-enrollment-selection'), deviceUdid }))
    },
  }), [])
  const runtimeAddAppServices = useMemo<AddAppServices>(() => ({
    loadImportRoots: async () => (await listCatalogImportRoots()).map((root) => ({ id: root.id, label: root.label, available: root.available })),
    upload: async (file) => catalogDtoToAddItem(await uploadCatalogIpaV2(file, { idempotencyKey: newUiIdempotencyKey('catalog-upload') })),
    importFromRoot: async (rootId, relativePath) => catalogDtoToAddItem(await inspectCatalogAppV2({ rootId, relativePath, idempotencyKey: newUiIdempotencyKey('catalog-root') })),
    loadGitHubSources: async () => {
      const snapshot = await listGitHubCatalogSources()
      return {
        capability: {
          kind: snapshot.capability.kind,
          supported: snapshot.capability.supported,
          allowedNow: snapshot.capability.allowedNow,
          blockedReason: snapshot.capability.blockedReason,
        },
        sources: snapshot.sources.map((source) => ({ id: source.id, repository: source.repository, visibility: source.visibility, status: source.status })),
      }
    },
    connectGitHub: async (repository, visibility) => createGitHubCatalogConnection({ repository, visibility, idempotencyKey: newUiIdempotencyKey('github-connection') }),
    readGitHubConnection: async (connectionId) => getGitHubCatalogConnection(connectionId),
    loadGitHubReleases: async (sourceId) => listGitHubCatalogReleases(sourceId),
    importGitHub: async (sourceId, releaseId, asset) => catalogDtoToAddItem(await importGitHubCatalogApp({ sourceId, releaseId, assetId: asset.assetId, expectedDigest: asset.digest ?? undefined, idempotencyKey: newUiIdempotencyKey('github-import') })),
  }), [])

  useEffect(() => {
    clearGitHubCallbackFromLocation()
  }, [])

  const workflowInstallOperationId = workflowInstallStep?.activeOperationId ?? null
  const reportedOnboardingInstallOperationId = workflowInstallOperationId ?? viewStatus.onboarding?.activeInstallOperationId ?? null
  const activeOnboardingInstallOperationId = reportedOnboardingInstallOperationId === ignoredInstallOperationId
    ? null
    : reportedOnboardingInstallOperationId
  const completedOperationId = viewStatus.onboarding?.completionReceipt?.verifiedOperationId
  const resumableSubmittedOperationId = completedOperationId === submittedInstallOperationId ? null : submittedInstallOperationId
  const polledInstallOperationId = activeOnboardingInstallOperationId ?? resumableSubmittedOperationId
  const workflowWaitingForFinalization = workflowInstallStep?.nextAction?.action === 'retry-finalization'
    && workflowInstallOperationId === polledInstallOperationId
  useEffect(() => {
    if (!polledInstallOperationId) return
    let cancelled = false
    let timer: number | undefined
    const poll = async () => {
      try {
        const operation = await readOperation(polledInstallOperationId)
        if (cancelled) return
        setOnboardingInstallOperation(operation)
        setInstallPollError(null)
        const active = ACTIVE_INSTALL_STATUSES.has(operation.status ?? '') && !workflowWaitingForFinalization
        if (active) timer = window.setTimeout(() => void poll(), 1_000)
        await queryClient.invalidateQueries({ queryKey: ['sideport-admin-data'] })
      } catch (reason) {
        if (cancelled) return
        setInstallPollError(reason instanceof Error ? reason.message : 'Sideport could not read the current install operation.')
        timer = window.setTimeout(() => void poll(), 2_000)
      }
    }
    rememberOperationId(onboardingInstallKey, polledInstallOperationId)
    void poll()
    return () => {
      cancelled = true
      if (timer !== undefined) window.clearTimeout(timer)
    }
  }, [onboardingInstallKey, polledInstallOperationId, queryClient, readOperation, workflowWaitingForFinalization])

  useEffect(() => {
    if (!completedOperationId) return
    rememberOperationId(onboardingInstallKey, null)
  }, [completedOperationId, onboardingInstallKey])

  const retryOnboardingFinalization = async () => {
    const operationId = onboardingInstallOperation?.operationId ?? polledInstallOperationId
    if (!operationId || finalizationPending || !canCompleteOnboarding) return
    setFinalizationPending(true)
    setInstallRequestError(null)
    try {
      await finalizeOnboarding({ verifiedOperationId: operationId, idempotencyKey: newUiIdempotencyKey('onboarding-finalization') })
      await refreshAdminData()
    } catch (reason) {
      setInstallRequestError(reason instanceof Error ? reason.message : 'Sideport could not finish setup.')
    } finally {
      setFinalizationPending(false)
    }
  }

  const reconcileOnboardingInstall = async () => {
    const sourceOperationId = onboardingInstallOperation?.type === 'reconcile'
      ? onboardingInstallOperation.parentOperationId
      : onboardingInstallOperation?.operationId ?? workflowInstallOperationId
    if (!sourceOperationId || reconciliationPending || !canRunOperations) return
    setReconciliationPending(true)
    setInstallRequestError(null)
    try {
      const child = await reconcileOperation(sourceOperationId, {
        idempotencyKey: newUiIdempotencyKey('onboarding-reconcile'),
        note: 'Verify the first-install outcome from the Sideport setup UI.',
      })
      if (!child.operationId) throw new Error('Sideport accepted the iPhone check without returning an operation to follow.')
      setIgnoredInstallOperationId(sourceOperationId)
      setOnboardingInstallOperation(child)
      setSubmittedInstallOperationId(child.operationId)
      rememberOperationId(onboardingInstallKey, child.operationId)
      await refreshAdminData()
    } catch (reason) {
      setInstallRequestError(reason instanceof Error ? reason.message : 'Sideport could not start the verify-only iPhone check.')
    } finally {
      setReconciliationPending(false)
    }
  }

  const activeEnrollmentOperationId = viewStatus.onboarding?.workflow?.steps.find((step) => step.id === 'device')?.activeOperationId ?? null

  const addFlowDialogs = <>
    <AddIPhoneDialog autoStart={addIPhoneAutoStart} canMutate={canAddIPhone} demoMode={viewStatus.mode === 'demo'} onAccepted={() => void refreshAdminData()} onContinue={continueAfterIPhone} onOpenChange={handleAddIPhoneOpenChange} open={addIPhoneOpen} persistenceKey={`${viewStatus.baseUrl}:device-enrollment`} resumeOperationId={activeEnrollmentOperationId} returnFocusRef={addFlowReturnFocusRef} services={injectedAddIPhoneServices ?? runtimeAddIPhoneServices} />
    <AddAppDialog canImport={canImportCatalog} canManageGitHub={canManageGitHub} catalogApps={catalogApps} demoMode={viewStatus.mode === 'demo'} githubCallback={githubCallback} onChooseApp={chooseAppFromAddFlow} onOpenChange={handleAddAppOpenChange} open={addAppOpen} returnFocusRef={addFlowReturnFocusRef} services={injectedAddAppServices ?? runtimeAddAppServices} />
  </>

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        setCommandOpen((open) => !open)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const setupIncomplete = viewStatus.onboarding !== undefined && viewStatus.onboarding.setupState !== 'complete'
  if (setupIncomplete && setupOpen) {
    return <><RuntimeFirstRunOnboarding apiStatus={viewStatus} appSelectionError={appSelectionError} appSelectionPending={appSelectionPending} appleContent={<PersonalAppleConnectorPanel canManageSigner={canManageAppleSigner} personalApple={viewData.personalApple} />} canAddApp={canImportCatalog} canAddIPhone={canAddIPhone} canCompleteOnboarding={canCompleteOnboarding} canRunInstall={canRunOperations} data={viewData} finalizationPending={finalizationPending} installOperation={onboardingInstallOperation} installPollError={installPollError} installPreflight={onboardingPreflight} installRequestError={installRequestError} installRequestPending={installRequestPending} onAddApp={openAddApp} onAddIPhone={startOnboardingIPhone} onExit={() => setSetupOpen(false)} onInstallApp={(catalogAppId) => void startOnboardingInstall(catalogAppId)} onOpenDevice={openDevice} onPrepareInstall={(catalogAppId) => void prepareOnboardingInstall(catalogAppId)} onReconcileInstall={() => void reconcileOnboardingInstall()} onRefresh={() => void refreshAdminData()} onRetryFinalization={() => void retryOnboardingFinalization()} onSelectedCatalogAppChange={(catalogAppId) => void selectOnboardingApp(catalogAppId)} reconciliationPending={reconciliationPending} selectedCatalogAppId={selectedCatalogAppId} />{addFlowDialogs}</>
  }

  return (
    <div className="admin-root">
      <aside className="sidebar" aria-label="Primary navigation">
        <div className="brand-lockup">
          <div className="brand-mark"><Apple size={18} /></div>
          <div>
            <div className="brand-name">Sideport</div>
            <div className="brand-meta">Apps for people you trust</div>
          </div>
        </div>

        <nav className="nav-list">
          {routeItems.map((item) => {
            const Icon = item.icon
            return (
              <button
                className={route === item.id ? 'nav-item active' : 'nav-item'}
                aria-current={route === item.id ? 'page' : undefined}
                key={item.id}
                onClick={() => setRoute(item.id)}
                type="button"
              >
                <Icon size={17} />
                <span>{item.label}</span>
              </button>
            )
          })}
        </nav>

        <div className="sidebar-note">
          <ShieldCheck size={17} />
          <span>{sidebarNote(viewStatus)}</span>
        </div>
      </aside>

      <div className="workspace">
        <TopBar addTriggerRef={globalAddTriggerRef} apiStatus={viewStatus} onAddApp={canImportCatalog ? openAddApp : undefined} onAddIPhone={openAddIPhone} onOpenCommand={() => setCommandOpen(true)} system={viewData.system} />
        <main className="content-area">
          {route === 'home' && <OverviewPage data={viewData} onContinueSetup={setupIncomplete ? () => setSetupOpen(true) : undefined} onNavigate={setRoute} setupCompletedCount={viewStatus.onboarding?.workflow?.steps.filter((step) => step.state === 'complete').length} setupNextAction={viewStatus.onboarding?.workflow?.nextAction?.label} setupTotalCount={viewStatus.onboarding?.workflow?.steps.length} />}
          {route === 'devices' && <DevicesPage data={viewData} onAddIPhone={openAddIPhone} onOpenDevice={openDevice} />}
          {route === 'device-detail' && <DeviceDetailPage data={viewData} device={selectedDevice} apiStatus={viewStatus} onInstallApp={() => openInstallPage(catalogApps[0]?.id)} />}
          {route === 'apps' && <AppCatalogPage data={viewData} catalogApps={catalogApps} onAddApp={canImportCatalog ? openAddApp : undefined} onInstallApp={openInstallPage} />}
          {route === 'install-app' && <InstallAppPage key={selectedCatalogAppId || 'default-app'} data={viewData} canRunOperations={canRunOperations} catalogApps={catalogApps} initialCatalogAppId={selectedCatalogAppId} installApp={installApp} onAddApp={canImportCatalog ? openAddApp : undefined} onAddIPhone={canAddIPhone ? openAddIPhone : undefined} onOpenCatalog={() => setRoute('apps')} preflightInstall={preflightInstall} readOperation={readOperation} registerPendingApp={registerPendingApp} />}
          {route === 'people' && <UsersPage workspace={viewData.workspace} activity={viewData.activity} apiStatus={viewStatus} />}
          {route === 'activity' && <ActivityPage data={viewData} apiStatus={viewStatus} />}
          {route === 'settings' && <CanonicalSettingsPage data={viewData} apiStatus={viewStatus} canManageScheduler={canManageScheduler} canManageSigner={canManageAppleSigner} schedulerSettingsService={injectedSchedulerSettingsService ?? updateSideportSchedulerSettings} onApiTokenSaved={onApiTokenSaved} />}
        </main>
      </div>
      <CommandMenu data={viewData} onAddApp={canImportCatalog ? openAddApp : undefined} onAddIPhone={openAddIPhone} onNavigate={setRoute} onOpenChange={setCommandOpen} onOpenDevice={openDevice} open={commandOpen} />
      {addFlowDialogs}
    </div>
  )
}

function TopBar({ system, apiStatus, onOpenCommand, onAddIPhone, onAddApp, addTriggerRef }: { system: SystemStatus; apiStatus: AdminDataStatus; onOpenCommand: () => void; onAddIPhone: () => void; onAddApp?: () => void; addTriggerRef: RefObject<HTMLButtonElement | null> }) {
  return (
    <header className="topbar">
      <div className="topbar-primary">
        <button className="search-shell" onClick={onOpenCommand} type="button" aria-label="Search devices, apps, and screens">
          <Search size={17} />
          <span className="search-label">Search devices, bundle IDs, blockers</span>
          <span className="search-kbd"><kbd>⌘</kbd><kbd>K</kbd></span>
        </button>
        <GlobalAddMenu onAddApp={onAddApp} onAddIPhone={onAddIPhone} triggerRef={addTriggerRef} />
      </div>
      <div className="topbar-actions">
        <span className={`api-mode ${apiStatus.mode}`}>{apiModeLabel(apiStatus.mode)}</span>
        <span className="api-base">{apiStatus.baseUrl}</span>
        <StatusPill state={system.ready.ready ? 'healthy' : 'blocked'} label={system.ready.ready ? 'Ready' : 'Not ready'} />
      </div>
    </header>
  )
}

function sidebarNote(apiStatus: AdminDataStatus): string {
  if (apiStatus.mode === 'unavailable') return 'No live API snapshot yet. Register, refresh, delete, and install remain disabled.'
  if (apiStatus.mode === 'demo') return 'Demo data is loaded for design review. Register, refresh, delete, and install are disabled.'
  if (apiStatus.canMutate) return 'Live API reads are connected. Register and refresh actions require explicit operator clicks.'
  return 'Live API reads are safe. Register, refresh, delete, and install remain disabled in this UI slice.'
}

export function OverviewPage({ data, onNavigate, onContinueSetup, setupCompletedCount = 0, setupNextAction, setupTotalCount = 0 }: { data: SideportReadModel; onNavigate?: (route: RouteId) => void; onContinueSetup?: () => void; setupCompletedCount?: number; setupNextAction?: string; setupTotalCount?: number }) {
  const reachable = data.devices.filter((device) => device.connection !== 'offline').length
  const blocked = data.renewals.filter((item) => item.risk === 'blocked').length
  const due = data.renewals.filter((item) => item.risk === 'due-now').length

  return (
    <div className="page-stack">
      <PageHeader
        eyebrow="Your Sideport"
        title="Apps and iPhones at a glance"
        description="Sideport keeps watching for connected iPhones and handles approved app updates in the background."
      />

      {onContinueSetup && <section aria-labelledby="sideport-setup-reminder-title" className="panel setup-reminder"><ShieldCheck aria-hidden="true" size={22} /><div className="setup-reminder-copy"><strong id="sideport-setup-reminder-title">Your Owner account is ready</strong><p className="muted">Connect Apple signing, an iPhone, and the first app whenever it is convenient. Sideport will keep your place and will not claim automatic refresh is ready until installation is verified.</p><small aria-live="polite" className="muted">{setupCompletedCount} of {setupTotalCount} setup checks complete{setupNextAction ? ` · Next: ${setupNextAction}` : ''}</small></div><button className="primary-action" onClick={onContinueSetup} type="button">Continue setup</button></section>}

      <section className="metric-grid" aria-label="Fleet health summary">
        <MetricCard icon={Smartphone} label="Reachable iPhones" value={String(reachable)} detail="Available over USB or paired Wi-Fi." source="live" tone="blue" />
        <MetricCard icon={TimerReset} label="Updates due" value={String(due)} detail="Approved apps inside the refresh window." source="derived" tone="amber" />
        <MetricCard icon={AlertTriangle} label="Needs attention" value={String(blocked)} detail="Usually a cable, Trust, or Owner action." source="derived" tone="red" />
        <MetricCard icon={Package} label="Approved apps" value={String(data.catalogApps.length)} detail="Ready to install from the shared library." source="live" tone="green" />
      </section>

      <div className="two-column-layout">
        <Panel title="App updates" actionLabel="Find apps" onAction={() => onNavigate?.('apps')}>
          <RenewalQueueList items={data.renewals.slice(0, 4)} apps={data.apps} compact />
        </Panel>
        <Panel title="Sideport status" actionLabel="View settings" onAction={() => onNavigate?.('settings')}>
          <SystemChecks system={data.system} />
        </Panel>
      </div>

      <Panel title="Recent activity">
        <ActivityTimeline events={data.activity} />
      </Panel>
    </div>
  )
}

export function DevicesPage({ data, onOpenDevice, onAddIPhone }: { data: SideportReadModel; onOpenDevice?: (device: DeviceSummary) => void; onAddIPhone?: () => void }) {
  const [query, setQuery] = useState('')
  const [connectionFacet, setConnectionFacet] = useState<'all' | DeviceSummary['connection']>('all')
  const [healthFacet, setHealthFacet] = useState<'all' | HealthState>('all')

  if (data.devices.length === 0) {
    return (
      <div className="page-stack">
        <PageHeader eyebrow="Devices" title="No devices known yet" description="Known devices come from /api/devices/known. Connect a trusted iPhone over USB or Wi-Fi, or add a known-device record, to populate this view." />
        <EmptyState actionLabel="Add iPhone" detail="Sideport has no durable device inventory yet, and the current reachability poll did not add a device to this snapshot." icon={Cable} onAction={onAddIPhone} title="No known devices returned" />
      </div>
    )
  }

  const q = query.trim().toLowerCase()
  const filtered = data.devices.filter((device) => {
    const haystack = `${device.name} ${device.udid} ${device.productType} ${device.teamId} ${appsForDevice(data.apps, device.udid).map((app) => app.bundleId).join(' ')}`.toLowerCase()
    const matchesQuery = !q || haystack.includes(q)
    const matchesConnection = connectionFacet === 'all' || device.connection === connectionFacet
    const matchesHealth = healthFacet === 'all' || device.health === healthFacet
    return matchesQuery && matchesConnection && matchesHealth
  })

  return (
    <div className="page-stack">
      <PageHeader eyebrow="Always watching the Sideport cable" title="Devices" description="See who owns each iPhone, where Sideport can reach it, and whether an app needs attention." />

      <div className="observability-panel"><Cable size={20} /><div><strong>USB port monitor is active</strong><p>Trusted iPhones are recognized when connected. A new iPhone starts the guided Trust setup.</p></div></div>

      {onAddIPhone && <div className="context-actions"><button className="ghost-action context-add-action" onClick={onAddIPhone} type="button"><Plus size={16} /> Add iPhone</button></div>}

      <div className="devices-toolbar">
        <div className="devices-search">
          <Search size={16} />
          <input aria-label="Search devices" onChange={(event) => setQuery(event.currentTarget.value)} placeholder="Name, app, or connection" value={query} />
        </div>
        <div className="facet-group" role="group" aria-label="Filter by connection">
          <span className="facet-label"><Filter size={13} /> Connection</span>
          {(['all', 'usb', 'wifi', 'offline'] as const).map((value) => (
            <FacetToggleButton key={value} onClick={() => setConnectionFacet(value)} pressed={connectionFacet === value}>{value === 'all' ? 'All' : connectionLabel(value)}</FacetToggleButton>
          ))}
        </div>
        <div className="facet-group" role="group" aria-label="Filter by health">
          <span className="facet-label">Health</span>
          {(['all', 'healthy', 'warning', 'blocked', 'offline'] as const).map((value) => (
            <FacetToggleButton key={value} onClick={() => setHealthFacet(value)} pressed={healthFacet === value}>{value === 'all' ? 'All' : healthCopy[value]}</FacetToggleButton>
          ))}
        </div>
        <span className="devices-count">{filtered.length} of {data.devices.length} {data.devices.length === 1 ? 'device' : 'devices'}</span>
      </div>

      {filtered.length === 0 ? (
        <EmptyState icon={Search} title="No devices match this filter" detail="Clear the search box or reset the connection and health filters to see the full inventory again." />
      ) : (
        <>
          <DeviceInventoryTable devices={filtered} apps={data.apps} onOpenDevice={onOpenDevice} />
          <div className="device-card-list">
            {filtered.map((device) => <DeviceCard key={device.udid} device={device} apps={appsForDevice(data.apps, device.udid)} onOpen={() => onOpenDevice?.(device)} />)}
          </div>
        </>
      )}
    </div>
  )
}

export function DeviceDetailPage({ data, device, apiStatus, onInstallApp }: { data: SideportReadModel; device?: DeviceSummary; apiStatus: AdminDataStatus; onInstallApp?: () => void }) {
  const tabs: Array<{ id: 'apps' | 'signing' | 'network' | 'activity'; label: string; count?: number }> = [
    { id: 'apps', label: 'Apps', count: device ? appsForDevice(data.apps, device.udid).length : undefined },
    { id: 'signing', label: 'Signing' },
    { id: 'network', label: 'Network' },
    { id: 'activity', label: 'Activity', count: device ? data.issues.filter((issue) => issue.deviceUdid === device.udid).length || undefined : undefined },
  ]
  type DeviceTab = (typeof tabs)[number]['id']
  const [activeTab, setActiveTab] = useState<DeviceTab>('apps')
  const apps = device ? appsForDevice(data.apps, device.udid) : []
  const refreshTarget = [...apps].sort((a, b) => (a.expiresAt?.value ?? '').localeCompare(b.expiresAt?.value ?? ''))[0]

  if (!device) {
    return <EmptyState icon={Smartphone} title="No selected device" detail="Device detail needs a reachable or known device read model." />
  }
  const installedApps = data.installedApps.filter((app) => app.deviceUdid === device.udid)
  const issues = data.issues.filter((issue) => issue.deviceUdid === device.udid)
  const refreshHelp = !apiStatus.canMutate
    ? 'Refresh runs from a live, mutations-enabled backend. It is disabled in this read-only build.'
    : !refreshTarget
      ? 'No Sideport-registered apps on this device to refresh yet.'
      : null

  return (
    <div className="page-stack">
      <div className="device-hero">
        <div>
          <div className="eyebrow">Device detail</div>
          <h1>{device.name}</h1>
          <p>{device.productType} · iOS {device.osVersion} · {compactUdid(device.udid)}</p>
        </div>
        <div className="hero-actions">
          <StatusPill state={device.health} label={healthCopy[device.health]} />
          {refreshTarget && <RefreshOperationButton apiStatus={apiStatus} bundleId={refreshTarget.bundleId} className="primary-action" deviceUdid={device.udid} />}
        </div>
      </div>

      <section className="section-grid three">
        <FactTile label="Connection" value={connectionLabel(device.connection)} source="live" />
        <FactTile label={device.currentPollAt ? 'Current poll' : 'Last seen'} value={device.currentPollAt ? relativeTime(device.currentPollAt.value) : device.hasDurableLastSeen ? relativeTime(device.lastSeenAt.value) : 'Not yet recorded'} source={(device.currentPollAt ?? device.lastSeenAt).source} />
        {device.currentPollAt && <FactTile label="Durable last seen" value={device.hasDurableLastSeen ? relativeTime(device.lastSeenAt.value) : 'Not yet recorded'} source={device.lastSeenAt.source} />}
        <FactTile label="Nearest registered expiry" value={expiryCopy(device.nearestExpiryAt?.value)} source={device.nearestExpiryAt?.source ?? 'planned'} />
        <FactTile label="Installed user apps" value={String(device.installedAppCount)} source="live" />
        <FactTile label="Unmanaged installed" value={String(device.unmanagedAppCount)} source={installedApps.length ? 'derived' : 'planned'} />
      </section>

      {refreshHelp && <p className="muted">{refreshHelp}</p>}

      <div className="device-tablist" role="tablist" aria-label="Device sections">
        {tabs.map((tab) => {
          const content = <>{tab.label}{tab.count ? <span className="tab-count">{tab.count}</span> : null}</>
          return activeTab === tab.id ? (
            <button aria-controls={`devpanel-${tab.id}`} aria-selected="true" className="device-tab" id={`devtab-${tab.id}`} key={tab.id} onClick={() => setActiveTab(tab.id)} role="tab" type="button">{content}</button>
          ) : (
            <button aria-controls={`devpanel-${tab.id}`} aria-selected="false" className="device-tab" id={`devtab-${tab.id}`} key={tab.id} onClick={() => setActiveTab(tab.id)} role="tab" type="button">{content}</button>
          )
        })}
      </div>

      <div aria-labelledby={`devtab-${activeTab}`} className="device-tabpanel" id={`devpanel-${activeTab}`} role="tabpanel">
        {activeTab === 'apps' && (
          <>
            <Panel title="Sideport-registered app slots">
              <AppSlotGrid apps={apps} canRegister={apiStatus.canMutate} onInstallApp={onInstallApp} />
              {apps.length >= 3 && <p className="singleflight-note"><AlertTriangle size={14} /> This device has 3 of 3 app slots in use. Remove an app registration before adding another — this is the Apple free-tier limit, not a Sideport one.</p>}
            </Panel>
            <Panel title="Installed on phone">
              {installedApps.length ? <InstalledAppList apps={installedApps} /> : <EmptyState icon={Package} title="Installed app list unavailable" detail="Sideport could not read installation_proxy data for this reachable phone in the current snapshot." />}
            </Panel>
          </>
        )}

        {activeTab === 'signing' && (
          <>
            <Panel title="Signing identity">
              <dl className="detail-list">
                <div><dt>Apple Developer Team</dt><dd>{device.teamId}</dd></div>
                <div><dt>Signing certificate</dt><dd>Reused across refreshes; re-minted only near its ~1-year expiry, so you trust it once.</dd></div>
                <div><dt>Single-flight signer</dt><dd>One signing operation at a time. Parallel refresh is intentionally serialized — see Renewals.</dd></div>
              </dl>
            </Panel>
            <Panel title="Per-app profile expiry">
              {apps.length ? (
                <dl className="detail-list">
                  {apps.map((app) => (
                    <div key={app.bundleId}>
                      <dt>{app.displayName.value}</dt>
                      <dd>{app.lifecycle === 'pending-install' ? 'Awaiting verified install' : `Profile ${expiryCopy(app.expiresAt?.value)} · ${app.lastSucceeded === false ? 'last refresh failed' : 'healthy'}`}</dd>
                    </div>
                  ))}
                </dl>
              ) : <EmptyState icon={KeyRound} title="No registered apps" detail="Register an app on this device to see its provisioning-profile expiry." />}
            </Panel>
          </>
        )}

        {activeTab === 'network' && (
          <Panel title="Network & trust">
            <dl className="detail-list">
              <div><dt>Current connection</dt><dd>{connectionLabel(device.connection)}</dd></div>
              <div><dt>Current poll</dt><dd>{device.currentPollAt ? relativeTime(device.currentPollAt.value) : 'Not reachable in this poll'}</dd></div>
              <div><dt>Durable last seen</dt><dd>{device.hasDurableLastSeen ? relativeTime(device.lastSeenAt.value) : 'Not yet recorded'}</dd></div>
              <div><dt>Wi-Fi pairing</dt><dd>{device.connection === 'wifi' ? 'Reachable through host netmuxd/usbmuxd over the network.' : device.connection === 'usb' ? 'Connected over USB. Wi-Fi pairing not reported in this snapshot.' : 'Offline. Showing the last known reachable state only.'}</dd></div>
              <div><dt>Trust state</dt><dd>{device.connection === 'offline' ? 'Unknown while the device is offline.' : 'Paired and trusted — the lockdown handshake succeeded.'}</dd></div>
            </dl>
            {device.blocker && <p className="pipeline-note"><AlertTriangle size={14} /> {device.blocker}</p>}
          </Panel>
        )}

        {activeTab === 'activity' && (
          <div className="page-stack"><Panel title="Device activity">{data.activity.length ? <ActivityTimeline events={data.activity} /> : <EmptyState icon={Activity} title="No recent activity" detail="Sign, install, and refresh events will appear here." />}</Panel>{issues.length ? <Panel title="Issues for this device"><DiagnosticIssueList issues={issues} /></Panel> : null}</div>
        )}
      </div>
    </div>
  )
}

export function AppCatalogPage({ data, catalogApps, onInstallApp, onAddApp }: { data: SideportReadModel; catalogApps: CatalogAppSummary[]; onInstallApp: (catalogAppId: string) => void; onAddApp?: () => void }) {
  const [query, setQuery] = useState('')
  const q = query.trim().toLowerCase()
  const filteredApps = q ? catalogApps.filter((app) => `${app.name} ${app.purpose} ${app.expectedBundleId} ${app.versionLabel} ${app.artifactSources?.map((source) => `${source.label} ${source.repository ?? ''}`).join(' ') ?? ''}`.toLowerCase().includes(q)) : catalogApps
  return (
    <div className="page-stack">
      <PageHeader
        eyebrow="Approved by the Owner"
        title="Apps"
        description="Find an app, choose an iPhone, and install. Sideport keeps approved apps updated afterward."
      />

      {onAddApp && <div className="context-actions"><button className="ghost-action context-add-action" onClick={onAddApp} type="button"><Plus size={16} /> Add app</button></div>}

      <label className="devices-search app-library-search"><Search size={17} /><span className="visually-hidden">Search approved apps</span><input aria-label="Search approved apps" onChange={(event) => setQuery(event.currentTarget.value)} placeholder="Search apps by name or description" type="search" value={query} /></label>

      {filteredApps.length ? (
        <div className="catalog-grid">
          {filteredApps.map((catalogApp) => (
            <CatalogAppCard
              catalogApp={catalogApp}
              installationCount={data.apps.filter((app) => app.bundleId === catalogApp.expectedBundleId).length}
              key={catalogApp.id}
              onInstall={() => onInstallApp(catalogApp.id)}
            />
          ))}
        </div>
      ) : catalogApps.length ? <EmptyState icon={Search} title="No apps match" detail="Try another name, description, version, or source." /> : (
        <EmptyState actionLabel="Add app" detail="Choose an IPA from this computer, this server, or a GitHub release." icon={Package} onAction={onAddApp} title="No apps in Sideport yet" />
      )}

      <Panel title="Registered installations">
        {data.apps.length ? <RegisteredInstallationList apps={data.apps} /> : (
          <EmptyState
            icon={Package}
            title="No Sideport registrations yet"
            detail="Apps already installed by AltServer or Xcode are not counted here until Sideport has a registration record for them."
          />
        )}
      </Panel>

      <Panel title="How apps get into Sideport">
        <AppIngestionGuide />
      </Panel>
    </div>
  )
}

export function InstallAppPage({ data, canRunOperations, catalogApps, initialCatalogAppId, installApp, preflightInstall, readOperation, registerPendingApp, onOpenCatalog, onAddIPhone, onAddApp }: { data: SideportReadModel; canRunOperations: boolean; catalogApps: CatalogAppSummary[]; initialCatalogAppId: string; installApp: (payload: InstallOperationPayload) => Promise<OperationRecordDto>; preflightInstall: (payload: InstallPreflightPayload) => Promise<OperationPreflightDto>; readOperation: (operationId: string) => Promise<OperationRecordDto>; registerPendingApp: (payload: PendingAppRegistrationPayload) => Promise<AppRegistrationDto>; onOpenCatalog: () => void; onAddIPhone?: () => void; onAddApp?: () => void }) {
  const queryClient = useQueryClient()
  const readyApps = catalogApps.filter((app) => app.status === 'ready')
  const usbDevices = data.devices.filter((device) => device.inventoryState === 'accepted' && device.connection === 'usb' && device.usableForInstall !== false)
  const [catalogAppId, setCatalogAppId] = useState(initialCatalogAppId || readyApps[0]?.id || '')
  const [deviceUdid, setDeviceUdid] = useState((usbDevices.find((device) => device.supportedForFirstInstall) ?? usbDevices[0])?.udid ?? '')
  const [requestPending, setRequestPending] = useState(false)
  const [requestError, setRequestError] = useState<string | null>(null)
  const [preflight, setPreflight] = useState<OperationPreflightDto | null>(null)
  const [submittedOperation, setSubmittedOperation] = useState<OperationRecordDto | null>(null)
  const [pollError, setPollError] = useState<string | null>(null)
  const requestInFlightRef = useRef(false)
  const preparedTargetRef = useRef('')
  const installErrorRef = useRef<HTMLParagraphElement>(null)
  const selectedCatalogApp = readyApps.find((app) => app.id === catalogAppId) ?? readyApps[0]
  const selectedDevice = usbDevices.find((device) => device.udid === deviceUdid) ?? usbDevices[0]
  const selectedTeam = data.personalApple.teams.find((team) => team.teamId === data.personalApple.selectedTeamId)
  const accountProfileId = data.personalApple.accountProfileId?.trim() ?? ''
  const selectedDeviceRegistrations = selectedDevice ? appsForDevice(data.apps, selectedDevice.udid) : []
  const alreadyRegistered = Boolean(selectedCatalogApp && selectedDeviceRegistrations.some((app) => app.bundleId === selectedCatalogApp.expectedBundleId))
  const slotAvailable = Boolean(selectedDevice && (alreadyRegistered || selectedDeviceRegistrations.length < 3))
  const resumableStandaloneOperation = selectedCatalogApp && selectedDevice
    ? data.operations.find((operation) =>
        operation.type === 'install'
        && operation.finishOnboarding === false
        && operation.deviceUdid === selectedDevice.udid
        && operation.bundleId === selectedCatalogApp.expectedBundleId
        && ACTIVE_INSTALL_STATUSES.has(operation.status))
    : undefined
  const resumableOperationRecord: OperationRecordDto | null = resumableStandaloneOperation ? {
    operationId: resumableStandaloneOperation.operationId,
    type: resumableStandaloneOperation.type,
    status: resumableStandaloneOperation.status,
    createdAt: resumableStandaloneOperation.createdAt,
    updatedAt: resumableStandaloneOperation.updatedAt,
    completedAt: resumableStandaloneOperation.completedAt,
    target: {
      deviceUdid: resumableStandaloneOperation.deviceUdid,
      bundleId: resumableStandaloneOperation.bundleId,
    },
    stages: resumableStandaloneOperation.stages.map((stage) => ({
      id: stage.id,
      label: stage.label,
      status: stage.status,
      startedAt: stage.startedAt,
      completedAt: stage.completedAt,
      message: stage.message,
      error: stage.error ? { message: stage.error } : null,
    })),
    error: resumableStandaloneOperation.error ? { message: resumableStandaloneOperation.error } : null,
    parentOperationId: resumableStandaloneOperation.parentOperationId,
  } : null
  const trackedOperation = submittedOperation ?? resumableOperationRecord
  const operationStatus = trackedOperation?.status
  const operationActive = Boolean(operationStatus && ACTIVE_INSTALL_STATUSES.has(operationStatus))
  const appleReady = data.personalApple.state === 'authenticated' && Boolean(accountProfileId && selectedTeam)
  const blockers = [
    !selectedCatalogApp ? 'Add or choose an inspected app.' : null,
    !appleReady ? 'Finish Apple sign-in and choose a team returned by Apple.' : null,
    !selectedDevice ? 'Connect an accepted iPhone by USB.' : null,
    selectedDevice && !slotAvailable ? `This iPhone already uses all 3 Sideport app slots.` : null,
    !canRunOperations ? 'This protected Sideport session does not have permission to install apps.' : null,
  ].filter((blocker): blocker is string => Boolean(blocker))
  const targetKey = selectedCatalogApp && selectedDevice ? `${selectedDevice.udid}:${selectedCatalogApp.expectedBundleId}` : ''
  const preflightReady = Boolean(preflight?.ready && preflight.preflightId && preflight.planVersion)
  const canInstall = blockers.length === 0 && preflightReady && !requestPending && !operationActive && !resumableStandaloneOperation
  const completedChimeRef = useRef<string | null>(null)

  useEffect(() => {
    if (operationStatus !== 'succeeded' || !trackedOperation?.operationId || trackedOperation.result?.success !== true) return
    if (completedChimeRef.current === trackedOperation.operationId) return
    completedChimeRef.current = trackedOperation.operationId
    attemptCompletionChime()
  }, [operationStatus, trackedOperation?.operationId, trackedOperation?.result?.success])

  useEffect(() => {
    if (requestError) installErrorRef.current?.focus()
  }, [requestError])

  const resetInstallTargetState = () => {
    setPreflight(null)
    setSubmittedOperation(null)
    setRequestError(null)
    setPollError(null)
    preparedTargetRef.current = ''
  }

  const prepareInstall = async () => {
    if (requestInFlightRef.current || blockers.length || !selectedCatalogApp || !selectedDevice || !accountProfileId) return
    requestInFlightRef.current = true
    setRequestPending(true)
    setRequestError(null)
    setPreflight(null)
    try {
      await registerPendingApp({ catalogAppId: selectedCatalogApp.id, deviceUdid: selectedDevice.udid, accountProfileId, lifecycle: 'pending-install' })
      setPreflight(await preflightInstall({ deviceUdid: selectedDevice.udid, bundleId: selectedCatalogApp.expectedBundleId, catalogAppId: selectedCatalogApp.id, accountProfileId, finishOnboarding: false }))
    } catch (reason) {
      setRequestError(reason instanceof Error ? reason.message : 'Sideport could not check this install.')
    } finally {
      setRequestPending(false)
      requestInFlightRef.current = false
    }
  }

  const autoPreflightEligible = Boolean(targetKey && blockers.length === 0 && !resumableStandaloneOperation)
  const autoPreflightDeviceUdid = selectedDevice?.udid ?? ''
  const autoPreflightBundleId = selectedCatalogApp?.expectedBundleId ?? ''
  const autoPreflightCatalogAppId = selectedCatalogApp?.id ?? ''
  useEffect(() => {
    if (!autoPreflightEligible || preparedTargetRef.current === targetKey) return
    preparedTargetRef.current = targetKey
    let cancelled = false
    requestInFlightRef.current = true
    void Promise.resolve().then(() => {
      if (!cancelled) setRequestPending(true)
      return registerPendingApp({ catalogAppId: autoPreflightCatalogAppId, deviceUdid: autoPreflightDeviceUdid, accountProfileId, lifecycle: 'pending-install' })
        .then(() => preflightInstall({ deviceUdid: autoPreflightDeviceUdid, bundleId: autoPreflightBundleId, catalogAppId: autoPreflightCatalogAppId, accountProfileId, finishOnboarding: false }))
    })
      .then((next) => { if (!cancelled) setPreflight(next) })
      .catch((reason) => { if (!cancelled) setRequestError(reason instanceof Error ? reason.message : 'Sideport could not check this install.') })
      .finally(() => {
        if (!cancelled) setRequestPending(false)
        requestInFlightRef.current = false
    })
    return () => { cancelled = true }
  }, [accountProfileId, autoPreflightBundleId, autoPreflightCatalogAppId, autoPreflightDeviceUdid, autoPreflightEligible, preflightInstall, registerPendingApp, targetKey])

  useEffect(() => {
    if (!trackedOperation?.operationId || !operationActive) return
    let cancelled = false
    let timer: number | undefined
    const operationId = trackedOperation.operationId
    const poll = async () => {
      try {
        const next = await readOperation(operationId)
        if (cancelled) return
        setSubmittedOperation(next)
        setPollError(null)
        if (ACTIVE_INSTALL_STATUSES.has(next.status ?? '')) timer = window.setTimeout(() => void poll(), 1_000)
        await queryClient.invalidateQueries({ queryKey: ['sideport-admin-data'] })
      } catch (reason) {
        if (cancelled) return
        setPollError(reason instanceof Error ? reason.message : 'Sideport could not read this install yet.')
        timer = window.setTimeout(() => void poll(), 2_000)
      }
    }
    void poll()
    return () => {
      cancelled = true
      if (timer !== undefined) window.clearTimeout(timer)
    }
  }, [operationActive, queryClient, readOperation, trackedOperation?.operationId])

  const submitInstall = async () => {
    if (!canInstall || requestInFlightRef.current || !selectedCatalogApp || !selectedDevice || !preflight?.preflightId || !preflight.planVersion) return
    requestInFlightRef.current = true
    setRequestPending(true)
    setRequestError(null)
    try {
      const record = await installApp({
        deviceUdid: selectedDevice.udid,
        bundleId: selectedCatalogApp.expectedBundleId,
        catalogAppId: selectedCatalogApp.id,
        accountProfileId,
        preflightId: preflight.preflightId,
        planVersion: preflight.planVersion,
        finishOnboarding: false,
        confirmedPlannedMutations: true,
        idempotencyKey: newUiIdempotencyKey('install'),
      })
      setSubmittedOperation(record)
      const failure = operationFailure(record)
      if (failure) setRequestError(failure)
      else if (!record.operationId) setRequestError('Sideport accepted the request without returning an operation to follow.')
      await queryClient.invalidateQueries({ queryKey: ['sideport-admin-data'] })
    } catch (reason) {
      const replacement = replacementInstallPreflight(reason)
      if (replacement) {
        setPreflight(replacement)
        setRequestError('The install plan changed. Review the updated checks, then press Install app again.')
      } else setRequestError(reason instanceof Error ? reason.message : 'Sideport could not start the install.')
    } finally {
      setRequestPending(false)
      requestInFlightRef.current = false
    }
  }

  return (
    <div className="page-stack">
      <PageHeader
        eyebrow="Install app"
        title="Install an app on your iPhone"
        description="Choose the app and an accepted USB-connected iPhone. Sideport uses the Apple account and team you already connected."
      />

      <div className="two-column-layout">
        <Panel title="App">
          {readyApps.length ? (
            <label className="form-field">
              <span>App to install</span>
              <select onChange={(event) => { resetInstallTargetState(); setCatalogAppId(event.currentTarget.value) }} value={selectedCatalogApp?.id ?? ''}>
                {readyApps.map((app) => <option key={app.id} value={app.id}>{app.name} · {app.versionLabel}</option>)}
              </select>
            </label>
          ) : <EmptyState actionLabel="Add app" detail="Upload an IPA, choose configured storage, or import a GitHub release." icon={Package} onAction={onAddApp} title="No app is ready" />}
          {selectedCatalogApp && <p className="pipeline-note">{selectedCatalogApp.purpose}</p>}
        </Panel>

        <Panel title="iPhone">
          {usbDevices.length ? (
            <label className="form-field">
              <span>Install on</span>
              <select onChange={(event) => { resetInstallTargetState(); setDeviceUdid(event.currentTarget.value) }} value={selectedDevice?.udid ?? ''}>
                {usbDevices.map((device) => <option key={device.udid} value={device.udid}>{device.name} · USB · {appsForDevice(data.apps, device.udid).length}/3 apps</option>)}
              </select>
            </label>
          ) : <EmptyState actionLabel="Add iPhone" detail="Use an accepted iPhone connected directly to the Sideport computer." icon={Smartphone} onAction={onAddIPhone} title="USB iPhone required" />}
          <p className="pipeline-note"><Cable size={14} /> Keep the iPhone unlocked and connected by USB. Sideport does not switch this install to Wi-Fi.</p>
        </Panel>
      </div>

      <Panel title="Ready to install">
        <div className="facts-grid">
          <FactTile label="App" source={selectedCatalogApp?.source ?? 'planned'} value={selectedCatalogApp?.name ?? 'Choose an app'} />
          <FactTile label="iPhone" source={selectedDevice ? 'live' : 'planned'} value={selectedDevice?.name ?? 'Connect USB'} />
          <FactTile label="Apple Developer Team" source={selectedTeam ? data.personalApple.source : 'planned'} value={selectedTeam?.name ?? 'Finish Apple setup'} />
          <FactTile label="Sideport slots" source="derived" value={selectedDevice ? `${selectedDeviceRegistrations.length}/3 used` : 'Unknown'} />
        </div>

        <PreflightList items={[
          ['IPA inspected and ready', Boolean(selectedCatalogApp), selectedCatalogApp?.source ?? 'planned'],
          ['Accepted iPhone connected by USB', Boolean(selectedDevice), selectedDevice ? 'live' : 'planned'],
          ['Apple account and returned team connected', appleReady, data.personalApple.source],
          ['App slot available', slotAvailable, 'derived'],
          ['Permission to install', canRunOperations, 'live'],
        ]} />

        {blockers.length > 0 && <div className="mutation-message" role="status">{blockers[0]}</div>}
        {preflight?.blockers.map((blocker) => <p className="mutation-message error" key={blocker.code}>{blocker.message}</p>)}
        {preflight?.warnings.map((warning) => <p className="mutation-message" key={warning.code}>{warning.message}</p>)}
        {preflight?.plannedMutations.length ? <details className="add-flow-advanced"><summary>Install plan</summary><ul>{preflight.plannedMutations.map((mutation) => <li key={mutation}>{mutation}</li>)}</ul></details> : null}
        {requestError && <p className="mutation-message error" id="standalone-install-error-summary" ref={installErrorRef} role="alert" tabIndex={-1}>{requestError}</p>}
        {pollError && <p className="mutation-message error" role="status">The install is still being tracked. {pollError}</p>}
        {operationStatus && !requestError && <p className={`mutation-message ${operationStatus === 'succeeded' ? 'success' : ''}`} role="status">{operationStatus === 'succeeded' && trackedOperation?.result?.success === true ? 'Installed — you can unplug. Sideport verified the app on the iPhone.' : operationActive ? 'Sideport is signing, installing, and verifying the app.' : `Install ${operationStatus}.`}</p>}
        {operationStatus === 'succeeded' && trackedOperation?.result?.success === true && <div className="install-completion-receipt"><CheckCircle2 size={22} /><div><strong>Installed — you can unplug</strong><span>{selectedCatalogApp?.name ?? trackedOperation.result.bundleId ?? 'The app'} was verified on {selectedDevice?.name ?? 'the iPhone'}. Automatic refresh remains enabled; paired Wi-Fi may work, with USB as the reliable fallback.</span><small>The completion chime was attempted when browser audio was available. Sideport verifies installation, not that the app opened.</small></div></div>}

        <div className="dialog-actions">
          <button className="ghost-action" onClick={onOpenCatalog} type="button">Back to apps</button>
          <button aria-describedby={requestError ? 'standalone-install-error-summary' : undefined} className="primary-action" disabled={operationActive || requestPending || (preflight ? !canInstall : blockers.length > 0)} onClick={() => void (preflight ? submitInstall() : prepareInstall())} type="button"><Play size={16} /> {requestPending ? preflight ? 'Starting install…' : 'Checking install…' : operationActive ? 'Installing…' : preflight ? 'Install app' : 'Check install'}</button>
        </div>

        <details className="add-flow-advanced"><summary>Technical details</summary><p>Sideport resolves the managed artifact and selected Apple account on the server, creates the device registration if needed, signs the app, installs over USB, then verifies the bundle and provisioning profile on the iPhone.</p></details>
      </Panel>

      {trackedOperation?.stages?.length ? <SigningPipeline title="Install progress" stages={trackedOperation.stages.map((stage) => ({ id: stage.id ?? 'stage', label: stage.label ?? stage.id ?? 'Stage', state: stage.status === 'succeeded' ? 'done' : stage.status === 'running' ? 'active' : stage.status === 'failed' || stage.status === 'blocked' ? 'failed' : 'pending', detail: stage.error?.message ?? stage.message ?? '' }))} /> : null}
    </div>
  )
}

function CatalogAppCard({ catalogApp, installationCount, onInstall }: { catalogApp: CatalogAppSummary; installationCount: number; onInstall: () => void }) {
  const canRegister = catalogApp.status === 'ready'
  const sourceLabelText = catalogApp.artifactSources?.length
    ? catalogApp.artifactSources.map((source) => source.repository ? `${source.label} · ${source.repository}` : source.label).join(' · ')
    : 'Sideport managed library'
  return (
    <article className="catalog-card">
      <div className="catalog-card-top">
        <div className={`app-icon ${catalogApp.iconTone}`}>{catalogApp.icon ? <img alt="" src={catalogApp.icon} /> : catalogApp.name.slice(0, 1)}</div>
        <div>
          <h2>{catalogApp.name}</h2>
          <span>{catalogApp.statusLabel}</span>
        </div>
        <SourcePill source={catalogApp.source} label={sourceLabel(catalogApp.source)} />
      </div>
      <p>{catalogApp.purpose}</p>
      <dl className="catalog-meta">
        <div><dt>Bundle ID</dt><dd>{catalogApp.expectedBundleId}</dd></div>
        <div><dt>Source</dt><dd>{sourceLabelText}</dd></div>
        <div><dt>Version</dt><dd>{catalogApp.versionLabel}</dd></div>
        <div><dt>Sideport registrations</dt><dd>{installationCount}</dd></div>
        <div><dt>Profile</dt><dd>{catalogApp.hasEmbeddedProfile ? expiryCopy(catalogApp.signatureExpiresAt) : 'No embedded profile'}</dd></div>
        <div><dt>Checksum</dt><dd>{catalogApp.sha256 ? `${catalogApp.sha256.slice(0, 12)}...` : 'Not available'}</dd></div>
      </dl>
      <ul className="catalog-notes">
        {catalogApp.notes.map((note) => <li key={note}>{note}</li>)}
      </ul>
      <button className="primary-action" disabled={!canRegister} onClick={onInstall} type="button"><Plus size={16} /> Install on iPhone</button>
    </article>
  )
}

function RegisteredInstallationList({ apps }: { apps: RegisteredAppSummary[] }) {
  const [view, setView] = useState<'all' | 'healthy' | 'pending' | 'failed'>('all')
  const filtered = apps.filter((app) => {
    if (view === 'failed') return app.lastSucceeded === false
    if (view === 'pending') return app.lifecycle === 'pending-install'
    if (view === 'healthy') return app.lifecycle !== 'pending-install' && app.lastSucceeded !== false
    return true
  })

  return (
    <>
      <div className="devices-toolbar">
        <div className="facet-group" role="group" aria-label="Filter installations">
          <span className="facet-label"><Filter size={13} /> View</span>
          {([['all', 'All'], ['healthy', 'Healthy'], ['pending', 'Awaiting install'], ['failed', 'Failed refresh']] as const).map(([value, label]) => (
            <FacetToggleButton key={value} onClick={() => setView(value)} pressed={view === value}>{label}</FacetToggleButton>
          ))}
        </div>
        <span className="devices-count">{filtered.length} of {apps.length} installations</span>
      </div>
      {filtered.length ? (
        <div className="registration-list">
          {filtered.map((app) => (
            <article className="registration-card" key={`${app.deviceUdid}:${app.bundleId}`}>
              <AppSummary app={app} />
              <div className="registration-meta">
                <span>{compactUdid(app.deviceUdid)}</span>
                <span>{app.teamId}</span>
                <span>{expiryCopy(app.expiresAt?.value)}</span>
                <StatusPill state={app.lifecycle === 'pending-install' ? 'warning' : app.lastSucceeded === false ? 'failed' : 'healthy'} label={app.lifecycle === 'pending-install' ? 'Awaiting verified install' : app.lastSucceeded === false ? 'Last refresh failed' : 'Healthy'} />
              </div>
              {app.lastError && <p className="registration-error">{app.lastError}</p>}
            </article>
          ))}
        </div>
      ) : <EmptyState icon={Filter} title="No installations match this view" detail="Switch the view filter back to All to see every Sideport registration." />}
    </>
  )
}

function InstalledAppList({ apps }: { apps: InstalledAppSummary[] }) {
  return (
    <div className="registration-list">
      {apps.map((app) => (
        <article className="registration-card" key={`${app.deviceUdid}:${app.bundleId}`}>
          <div className="app-summary">
            <div className={`app-icon ${app.managedBySideport ? 'blue' : 'slate'}`}>{app.name.slice(0, 1)}</div>
            <div>
              <strong>{app.name}</strong>
              <span>{app.bundleId}</span>
            </div>
          </div>
          <div className="registration-meta">
            <span>{app.managedBySideport ? 'Sideport' : 'External'}</span>
            <span>{app.version}</span>
            <span>{expiryCopy(app.signatureExpiresAt?.value)}</span>
          </div>
        </article>
      ))}
    </div>
  )
}

function AppIngestionGuide() {
  const steps = [
    ['Choose a source', 'Upload an IPA, choose a configured Sideport location, or connect a GitHub release.'],
    ['Inspect automatically', 'Sideport reads the bundle ID, version, checksum, and embedded-profile state.'],
    ['Save safely', 'The validated IPA moves into Sideport-managed durable storage; host paths stay private.'],
    ['Install anywhere', 'A saved app can be installed on any accepted iPhone without re-entering its identity.'],
  ]

  return (
    <div className="ingestion-steps">
      {steps.map(([title, detail], index) => <InfoStep detail={detail} index={index} key={title} title={title} />)}
    </div>
  )
}

function InfoStep({ title, detail, index }: { title: string; detail: string; index: number }) {
  return (
    <article className="ingestion-step">
      <span>{index + 1}</span>
      <div><strong>{title}</strong><p>{detail}</p></div>
    </article>
  )
}

function SingleFlightStrip({ data }: { data: SideportReadModel }) {
  const runningOperation = data.operations.find((operation) => operation.status === 'running')
  const running = runningOperation
    ? data.renewals.find((item) => item.operationId === runningOperation.operationId || (item.deviceUdid === runningOperation.deviceUdid && item.bundleId === runningOperation.bundleId))
    : data.renewals.find((item) => item.status === 'running')
  const queued = data.renewals.filter((item) => item.status === 'queued')
  const latestOperation = data.operations[0]
  const appName = (item: RenewalItem) => data.apps.find((app) => app.bundleId === item.bundleId && app.deviceUdid === item.deviceUdid)?.displayName.value ?? item.bundleId
  const runningName = running
    ? appName(running)
    : runningOperation
      ? data.apps.find((app) => app.bundleId === runningOperation.bundleId && app.deviceUdid === runningOperation.deviceUdid)?.displayName.value ?? runningOperation.bundleId
      : null
  const runningBundleId = running?.bundleId ?? runningOperation?.bundleId
  const runningDeviceUdid = running?.deviceUdid ?? runningOperation?.deviceUdid
  const isRunning = Boolean(running || runningOperation)

  return (
    <section className="singleflight" aria-label="Signing queue">
      <div className="singleflight-head">
        <RefreshCw size={17} />
        <h2>Single-flight signer</h2>
        <StatusPill state={isRunning ? 'warning' : 'healthy'} label={isRunning ? 'Operation running' : 'Idle'} />
      </div>

      {isRunning ? (
        <div className="singleflight-now">
          <div className="singleflight-row">
            <div>
              <strong>{runningName}</strong>
              <div className="muted">{runningBundleId} · {runningDeviceUdid ? compactUdid(runningDeviceUdid) : 'device unknown'}</div>
            </div>
            <span className="status-pill warning"><Loader2 className="stage-spin" size={14} /> Running</span>
          </div>
          <SigningPipeline
            title="Current operation"
            stages={operationPipelineStages(runningOperation?.stages)}
            note={runningOperation ? 'Stages are rendered from /api/operations for this running record.' : 'The renewal row is running, but this snapshot did not include operation stage details.'}
          />
        </div>
      ) : (
        <p className="singleflight-note"><CheckCircle2 size={14} /> No operation is running in the current API snapshot. Scheduled refreshes remain outside operation history in this slice.</p>
      )}

      <div>
        <div className="command-section-label queue-label">Queue ({queued.length})</div>
        {queued.length ? (
          <div className="queue-list">
            {queued.map((item, index) => (
              <div className="queue-item" key={item.id}>
                <span className="queue-pos">{index + 1}</span>
                <div className="queue-body">
                  <strong>{appName(item)}</strong>
                  <span>{item.blocker ?? 'Waiting for the signer to free up.'}</span>
                </div>
                <button className="row-action queue-action" disabled title="Cancel is available only before signing starts" type="button"><X size={13} /> Cancel</button>
              </div>
            ))}
          </div>
        ) : <p className="singleflight-note"><CheckCircle2 size={14} /> No backend queue is exposed yet. Refresh operations are recorded after the synchronous run completes.</p>}
      </div>

      {latestOperation && <p className="singleflight-note"><Activity size={14} /> Latest operation {latestOperation.operationId}: {latestOperation.status} · {latestOperation.actor}</p>}
      <p className="singleflight-note"><AlertTriangle size={14} /> Refresh is serialized: Sideport signs one app at a time to protect the single free-tier certificate. Cancel and rerun are not exposed until the backend owns a safe background operation boundary.</p>
    </section>
  )
}

function operationPipelineStages(stages?: OperationStageSummary[]): PipelineStage[] {
  if (!stages?.length) {
    return [{ id: 'operation-record', label: 'Operation record', detail: 'No stage details returned in this snapshot.', state: 'active' }]
  }
  return stages.map((stage) => ({
    id: stage.id,
    label: stage.label,
    detail: stage.error ?? stage.message,
    state: operationPipelineState(stage.status),
  }))
}

function operationPipelineState(status: OperationStageSummary['status']): PipelineStageState {
  if (status === 'succeeded') return 'done'
  if (status === 'running') return 'active'
  if (status === 'failed' || status === 'blocked') return 'failed'
  return 'pending'
}

export function RenewalsPage({ data, apiStatus }: { data: SideportReadModel; apiStatus: AdminDataStatus }) {
  return (
    <div className="page-stack">
      <PageHeader eyebrow="Renewals" title="Renewal risk" description="Current backend data comes from registered apps, expiry fields, operation history, refresh status, and last error details." />
      <SingleFlightStrip data={data} />
      <RenewalLane title="Blocked" items={data.renewals.filter((item) => item.risk === 'blocked')} apps={data.apps} apiStatus={apiStatus} />
      <RenewalLane title="Due now" items={data.renewals.filter((item) => item.risk === 'due-now')} apps={data.apps} apiStatus={apiStatus} />
      <RenewalLane title="Upcoming" items={data.renewals.filter((item) => item.risk === 'upcoming')} apps={data.apps} apiStatus={apiStatus} />
      <RenewalLane title="Healthy" items={data.renewals.filter((item) => item.risk === 'healthy')} apps={data.apps} apiStatus={apiStatus} />
    </div>
  )
}

export function OperationsPage({ data, apiStatus }: { data: SideportReadModel; apiStatus: AdminDataStatus }) {
  return (
    <div className="page-stack">
      <PageHeader eyebrow="Operations" title="Operation history" description="Durable refresh operations, worker stages, and safe follow-up actions from /api/operations." />
      <Panel title={`Recent operations (${data.operations.length})`}>
        {data.operations.length ? <OperationHistoryList operations={data.operations} apps={data.apps} apiStatus={apiStatus} /> : <EmptyState icon={Activity} title="No operations recorded" detail="Refresh, scheduler, retry, and rerun operations will appear here once the backend accepts them." />}
      </Panel>
    </div>
  )
}

function OperationHistoryList({ operations, apps, apiStatus }: { operations: SideportReadModel['operations']; apps: RegisteredAppSummary[]; apiStatus: AdminDataStatus }) {
  const queryClient = useQueryClient()
  const actionMutation = useMutation({
    mutationFn: async ({ action, operationId }: { action: 'cancel' | 'retry' | 'rerun'; operationId: string }) => {
      if (action === 'cancel') return await cancelOperation(operationId)
      if (action === 'retry') return await retryOperation(operationId)
      return await rerunOperation(operationId)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['sideport-admin-data'] }),
  })
  const appName = (operation: SideportReadModel['operations'][number]) => apps.find((app) => app.bundleId === operation.bundleId && app.deviceUdid === operation.deviceUdid)?.displayName.value ?? operation.bundleId
  return (
    <div className="renewal-list">
      {operations.map((operation) => (
        <article className="renewal-item" key={operation.operationId}>
          <div>
            <strong>{appName(operation)}</strong>
            <span>{operation.type} · {operation.operationId}</span>
          </div>
          <div className="renewal-meta">
            <StatusDot state={operation.status === 'succeeded' ? 'ok' : operation.status === 'failed' || operation.status === 'blocked' ? 'failed' : 'warning'} />
            <span>{operation.status} · {operation.actor}</span>
          </div>
          <span className="muted">{shortDateTime(operation.updatedAt)}</span>
          {operation.parentOperationId && <span className="muted">Parent {operation.parentOperationId}</span>}
          {operation.error && <p>{operation.error}</p>}
          <div className="row-actions">
            <button className="row-action" disabled={!apiStatus.canMutate || !operation.cancelable || actionMutation.isPending} onClick={() => actionMutation.mutate({ action: 'cancel', operationId: operation.operationId })} type="button"><X size={13} /> Cancel</button>
            <button className="row-action" disabled={!apiStatus.canMutate || !operation.retryable || actionMutation.isPending} onClick={() => actionMutation.mutate({ action: 'retry', operationId: operation.operationId })} type="button"><RefreshCw size={13} /> Retry</button>
            <button className="row-action" disabled={!apiStatus.canMutate || !operation.rerunnable || actionMutation.isPending} onClick={() => actionMutation.mutate({ action: 'rerun', operationId: operation.operationId })} type="button"><Play size={13} /> Rerun</button>
          </div>
          {operation.stages.length ? <SigningPipeline title={`${appName(operation)} stages`} stages={operationPipelineStages(operation.stages)} /> : null}
        </article>
      ))}
      {actionMutation.error && <p className="mutation-message error">{actionMutation.error.message}</p>}
    </div>
  )
}

export function ActivityPage({ data, apiStatus }: { data: SideportReadModel; apiStatus: AdminDataStatus }) {
  const needsAttention = data.operations.filter((operation) => operation.status === 'failed' || operation.status === 'blocked')
  return (
    <div className="page-stack">
      <PageHeader eyebrow="What happened and who needs help" title="Activity" description="Installs, updates, device changes, and access events in one plain-language history. Technical evidence stays inside detailed rows." />
      {needsAttention.length ? <Panel title={`Needs attention (${needsAttention.length})`}><OperationHistoryList operations={needsAttention} apps={data.apps} apiStatus={apiStatus} /></Panel> : null}
      <Panel title={`Recent operations (${data.operations.length})`}>
        {data.operations.length ? <OperationHistoryList operations={data.operations.filter((operation) => !needsAttention.includes(operation))} apps={data.apps} apiStatus={apiStatus} /> : <EmptyState icon={Activity} title="No activity yet" detail="App installs, refreshes, and device work will appear here." />}
      </Panel>
      {data.issues.length ? <Panel title={`Issues (${data.issues.length})`}><DiagnosticIssueList issues={data.issues} /></Panel> : null}
      {data.activity.length ? <Panel title="Workspace history"><ActivityTimeline events={data.activity} /></Panel> : null}
    </div>
  )
}

export function AppleAccessPage({ appleAccess, personalApple, canManageSigner = false }: { appleAccess: AppleAccessSummary; personalApple: PersonalAppleSummary; canManageSigner?: boolean }) {
  const verified = appleAccess.capabilities.filter((capability) => capability.state === 'verified').length
  const blocked = appleAccess.capabilities.filter((capability) => capability.state !== 'verified' && capability.state !== 'not-checked').length

  return (
    <div className="page-stack">
      <PageHeader
        eyebrow="Apple Access"
        title="Connect Apple data without over-trusting it"
        description="Use the protected Personal Apple Account connection for signing. The separate App Store Connect section is a read-only capability probe; Sideport never captures browser cookies."
      />

      <section className="section-grid three">
        <FactTile label="Default connector" value="Personal Apple ID" source={personalApple.source} />
        <FactTile label="Personal state" value={personalAppleStateLabel(personalApple.state)} source={personalApple.source} />
        <FactTile label="Credential custody" value={credentialCustodyShortLabel(personalApple.secretCustody)} source={personalApple.source} />
        <FactTile label="ASC probe" value={appleAccessStateLabel(appleAccess.state)} source={appleAccess.source} />
        <FactTile label="Capabilities" value={`${verified}/${Math.max(appleAccess.capabilities.length, 1)} verified`} source={appleAccess.source} />
        <FactTile label="Key ID" value={appleAccess.keyIdSuffix ?? 'Not configured'} source={appleAccess.keyIdSuffix ? appleAccess.source : 'planned'} />
        <FactTile label="Issuer ID" value={appleAccess.issuerIdSuffix ?? 'Not configured'} source={appleAccess.issuerIdSuffix ? appleAccess.source : 'planned'} />
      </section>

      <Panel title={`Personal Apple ID via ${credentialCustodyShortLabel(personalApple.secretCustody)}`}>
        <PersonalAppleConnectorPanel personalApple={personalApple} canManageSigner={canManageSigner} />
      </Panel>

      <Panel title="Connector posture">
        <div className="connector-posture">
          <StatusPill state={appleAccessHealth(appleAccess)} label={appleAccessStateLabel(appleAccess.state)} />
          <p>{appleAccess.message}</p>
          <div className="setup-meta">
            <SourcePill source={appleAccess.source} label={sourceLabel(appleAccess.source)} />
            <span>{blocked ? `${blocked} blocker(s) need attention before this can be used for signing preflight.` : 'Read-only probe only. Mutations still require preflight and confirmation.'}</span>
          </div>
        </div>
      </Panel>

      <Panel title="Capability probe">
        {appleAccess.capabilities.length ? <AppleAccessCapabilityList capabilities={appleAccess.capabilities} /> : <EmptyState icon={KeyRound} title="No capability rows yet" detail="Configure server-side App Store Connect team key references, then reload this page to run the read-only probe." />}
      </Panel>

      <div className="two-column-layout">
        <Panel title="What this is">
          <div className="ingestion-steps compact">
            {[
              ['Optional portal login', 'Sign in with Apple can identify a Sideport user, but Sideport must also work with reverse-proxy or local auth. It does not grant Developer API access.'],
              ['Developer access', 'App Store Connect team keys authorize provisioning API calls when role and agreements permit them.'],
              ['Fallback path', 'Apple ID session and local helper remain compatibility options for personal/free teams.'],
            ].map(([title, detail], index) => <InfoStep detail={detail} index={index} key={title} title={title} />)}
          </div>
        </Panel>
        <Panel title="Safety rules">
          <div className="ingestion-steps compact">
            {[
              ['No browser scraping', 'Being logged into Apple in a browser helps create an API key; Sideport must not capture cookies.'],
              ['Exact replacement only', 'Sideport never revokes certificates during install or refresh. The Owner-only signing flow can replace only the exact certificate IDs shown in its current impact review.'],
              ['No hidden mutations', 'This page only performs GET probes. Install and refresh actions must run through preflight.'],
            ].map(([title, detail], index) => <InfoStep detail={detail} index={index} key={title} title={title} />)}
          </div>
        </Panel>
      </div>
    </div>
  )
}

function PersonalAppleConnectorPanel({ personalApple, canManageSigner = false }: { personalApple: PersonalAppleSummary; canManageSigner?: boolean }) {
  const queryClient = useQueryClient()
  const [appleId, setAppleId] = useState('')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [connectPending, setConnectPending] = useState(false)
  const [connectError, setConnectError] = useState<string | null>(null)
  const [signingPreflight, setSigningPreflight] = useState<PersonalAppleSigningPreflightDto | null>(null)
  const [signingPending, setSigningPending] = useState(false)
  const [signingError, setSigningError] = useState<string | null>(null)
  const [signingAcknowledged, setSigningAcknowledged] = useState(false)
  const [signingOperation, setSigningOperation] = useState<OperationRecordDto | null>(null)
  const [replacementOpen, setReplacementOpen] = useState(false)
  const [replacementAppleId, setReplacementAppleId] = useState('')
  const [replacementPassword, setReplacementPassword] = useState('')
  const [replacementCode, setReplacementCode] = useState('')
  const [replacementCandidate, setReplacementCandidate] = useState<AppleAccountReplacementCandidateDto | null>(null)
  const connectInFlightRef = useRef(false)
  const signingIdempotencyKeyRef = useRef(newUiIdempotencyKey('signer-cutover'))
  const twoFactorInputRef = useRef<HTMLInputElement>(null)
  const credentialEntry = personalApple.credentialEntry
  const managedEntryAvailable = Boolean(credentialEntry?.supported && credentialEntry.allowedNow)
  const needsCredential = personalApple.state === 'not-configured'
  const signInMutation = useMutation({
    mutationFn: () => signInPersonalApple({ appleId: appleId.trim() }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['sideport-admin-data'] }),
  })
  const twoFactorMutation = useMutation({
    mutationFn: () => completePersonalAppleTwoFactor({ challengeId: personalApple.pendingChallengeId ?? '', code: code.trim() }),
    onSuccess: () => {
      setCode('')
      queryClient.invalidateQueries({ queryKey: ['sideport-admin-data'] })
    },
    onError: () => {
      setCode('')
      window.requestAnimationFrame(() => twoFactorInputRef.current?.focus())
    },
  })
  const teamMutation = useMutation({
    mutationFn: (teamId: string) => selectPersonalAppleTeam({ accountProfileId: personalApple.accountProfileId ?? '', teamId }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['sideport-admin-data'] }),
  })
  const canConnect = canManageSigner && managedEntryAvailable && appleId.trim().length > 0 && password.length > 0 && !connectPending
  const canSignIn = canManageSigner && (Boolean(personalApple.accountProfileId) || appleId.trim().length > 0) && !signInMutation.isPending
  const canComplete2Fa = canManageSigner && Boolean(personalApple.pendingChallengeId) && /^\d{6}$/.test(code.trim()) && !twoFactorMutation.isPending

  const reviewSigning = async () => {
    if (!personalApple.accountProfileId || !personalApple.selectedTeamId || signingPending) return
    setSigningPending(true)
    setSigningError(null)
    setSigningOperation(null)
    try {
      setSigningPreflight(await preflightPersonalAppleSigning(personalApple.accountProfileId, personalApple.selectedTeamId))
      setSigningAcknowledged(false)
    } catch (reason) {
      setSigningError(reason instanceof Error ? reason.message : 'Sideport could not review the current signing impact.')
    } finally { setSigningPending(false) }
  }
  const replaceSigning = async () => {
    if (!signingPreflight || signingPending || (signingPreflight.requiresAcknowledgement && !signingAcknowledged)) return
    setSigningPending(true)
    setSigningError(null)
    try {
      const operation = await cutoverPersonalAppleSigning({
        preflightId: signingPreflight.preflightId,
        inventoryVersion: signingPreflight.inventoryVersion,
        acknowledgedCertificateIds: signingPreflight.appleCertificates.map((certificate) => certificate.id),
        acknowledgedImpactCodes: [signingPreflight.impact],
        idempotencyKey: signingIdempotencyKeyRef.current,
      })
      setSigningOperation(operation)
      if (operation.status === 'succeeded') {
        signingIdempotencyKeyRef.current = newUiIdempotencyKey('signer-cutover')
        setSigningPreflight(null)
        await queryClient.invalidateQueries({ queryKey: ['sideport-admin-data'] })
      }
    } catch (reason) {
      setSigningError(reason instanceof Error ? reason.message : 'Sideport could not replace the signing identity.')
    } finally { setSigningPending(false) }
  }
  const connectReplacement = async () => {
    if (!replacementAppleId.trim() || !replacementPassword || signingPending) return
    let submittedPassword = replacementPassword
    setReplacementPassword('')
    setSigningPending(true)
    setSigningError(null)
    try {
      const request = connectReplacementAppleAccount(replacementAppleId.trim(), submittedPassword)
      submittedPassword = ''
      setReplacementCandidate(await request)
    } catch (reason) { setSigningError(reason instanceof Error ? reason.message : 'Sideport could not verify the replacement Apple account.') }
    finally { setReplacementPassword(''); setSigningPending(false) }
  }
  const verifyReplacementTwoFactor = async () => {
    if (!replacementCandidate || !/^\d{6}$/.test(replacementCode) || signingPending) return
    setSigningPending(true); setSigningError(null)
    try { setReplacementCandidate(await completeReplacementAppleTwoFactor(replacementCandidate.candidateId, replacementCode)); setReplacementCode('') }
    catch (reason) { setReplacementCode(''); setSigningError(reason instanceof Error ? reason.message : 'Apple rejected the verification code.') }
    finally { setSigningPending(false) }
  }
  const reviewReplacementTeam = async (teamId: string) => {
    if (!replacementCandidate || !personalApple.accountProfileId || signingPending) return
    setSigningPending(true); setSigningError(null)
    try {
      setSigningPreflight(await preflightPersonalAppleSigning(replacementCandidate.accountProfileId, teamId, undefined, replacementCandidate.candidateId, personalApple.accountProfileId))
      setSigningAcknowledged(false)
      setReplacementOpen(false)
    } catch (reason) { setSigningError(reason instanceof Error ? reason.message : 'Sideport could not review the replacement account impact.') }
    finally { setSigningPending(false) }
  }

  const submitManagedCredential = async () => {
    if (!canConnect || connectInFlightRef.current) return
    const submittedAppleId = appleId.trim()
    let submittedPassword = password
    connectInFlightRef.current = true
    setConnectPending(true)
    setConnectError(null)
    setPassword('')
    try {
      const request = connectPersonalApple({ appleId: submittedAppleId, password: submittedPassword })
      submittedPassword = ''
      await request
      setAppleId('')
      await queryClient.invalidateQueries({ queryKey: ['sideport-admin-data'] })
    } catch (reason) {
      setConnectError(reason instanceof Error ? reason.message : 'Sideport could not connect this Apple account.')
    } finally {
      setPassword('')
      setConnectPending(false)
      connectInFlightRef.current = false
    }
  }

  return (
    <div className="personal-apple-panel">
      <div className="connector-posture">
        <StatusPill state={personalAppleHealth(personalApple)} label={personalAppleStateLabel(personalApple.state)} />
        <p>{personalApple.message}</p>
        <div className="setup-meta">
          <SourcePill source={personalApple.source} label={sourceLabel(personalApple.source)} />
          <span>{personalApple.appleIdHint ? `Apple ID ${personalApple.appleIdHint}` : 'No Apple ID is configured or authenticated yet.'}</span>
        </div>
      </div>

      {needsCredential && managedEntryAvailable && (
        <form className="personal-apple-connect" onSubmit={(event) => { event.preventDefault(); void submitManagedCredential() }}>
          <div className="form-grid">
            <label className="form-field"><span>Apple Account email</span><input autoComplete="username" disabled={connectPending} onChange={(event) => setAppleId(event.currentTarget.value)} placeholder="name@example.com" value={appleId} /></label>
            <label className="form-field"><span>Password</span><input autoComplete="current-password" disabled={connectPending} onChange={(event) => setPassword(event.currentTarget.value)} type="password" value={password} /></label>
          </div>
          <p className="data-boundary-note">Your password is sent once to this protected Sideport server. The field clears when you submit, and only the server can keep the encrypted credential after Apple authentication succeeds.</p>
          <button className="primary-action" disabled={!canConnect} type="submit">{connectPending ? 'Connecting…' : 'Connect Apple account'}</button>
        </form>
      )}

      {needsCredential && credentialEntry?.supported && !credentialEntry.allowedNow && <p className="mutation-message">{credentialEntry.blockedReason?.message ?? 'Open Sideport over HTTPS or directly on the loopback address to add an Apple account.'}</p>}

      {needsCredential && !credentialEntry?.supported && (
        <div className="connector-readonly-setup">
          <p className="data-boundary-note">This deployment uses read-only {credentialCustodyLabel(personalApple.secretCustody)}. Its owner must configure the Apple credential in that provider; Sideport will never copy it into browser storage.</p>
          <label className="form-field"><span>Configured Apple Account email</span><input autoComplete="username" onChange={(event) => setAppleId(event.currentTarget.value)} placeholder="name@example.com" value={appleId} /></label>
          <button className="primary-action" disabled={!canSignIn} onClick={() => signInMutation.mutate()} type="button">Sign in</button>
        </div>
      )}

      {!needsCredential && personalApple.state !== 'authenticated' && personalApple.state !== 'two-factor-required' && (
        <div className="saved-apple-account">
          <div><strong>{personalApple.appleIdHint ?? 'Saved Apple account'}</strong><span>Credential held by {credentialCustodyShortLabel(personalApple.secretCustody)} custody.</span></div>
          <button className="primary-action" disabled={!canSignIn} onClick={() => signInMutation.mutate()} type="button">{signInMutation.isPending ? 'Signing in…' : 'Sign in saved account'}</button>
        </div>
      )}

      {personalApple.pendingChallengeId && (
        <div className="form-grid">
          <label className="form-field">
            <span>{personalApple.pendingChallengeKind ?? '2FA'} code</span>
            <input autoComplete="one-time-code" inputMode="numeric" maxLength={6} onChange={(event) => setCode(event.currentTarget.value.replace(/\D/g, '').slice(0, 6))} placeholder="123456" ref={twoFactorInputRef} value={code} />
          </label>
          <div className="form-field action-field">
            <span>{personalApple.pendingChallengeExpiresAt ? `Expires ${relativeTime(personalApple.pendingChallengeExpiresAt)}` : 'Short-lived verification'}</span>
            <button className="primary-action" disabled={!canComplete2Fa} onClick={() => twoFactorMutation.mutate()} type="button">Continue</button>
          </div>
        </div>
      )}
      {!canManageSigner && <p className="mutation-message">This protected Sideport session does not have permission to manage the Apple signer.</p>}
      {connectError && <p className="mutation-message error" role="alert">{connectError}</p>}
      {signInMutation.error && <p className="mutation-message error" role="alert">{signInMutation.error.message}</p>}
      {twoFactorMutation.error && <p className="mutation-message error" role="alert">{twoFactorMutation.error.message}</p>}
      {teamMutation.error && <p className="mutation-message error" role="alert">{teamMutation.error.message}</p>}
      {personalApple.teams.length > 0 && <PersonalAppleTeamList disabled={!canManageSigner || teamMutation.isPending} onSelect={(teamId) => {
        if (!personalApple.selectedTeamId) teamMutation.mutate(teamId)
        else if (teamId !== personalApple.selectedTeamId && personalApple.accountProfileId) void (async () => {
          setSigningPending(true)
          setSigningError(null)
          try {
            setSigningPreflight(await preflightPersonalAppleSigning(personalApple.accountProfileId!, teamId))
            setSigningAcknowledged(false)
          } catch (reason) { setSigningError(reason instanceof Error ? reason.message : 'Sideport could not review the team change.') }
          finally { setSigningPending(false) }
        })()
      }} selectedTeamId={personalApple.selectedTeamId} teams={personalApple.teams} />}
      {canManageSigner && personalApple.accountProfileId && personalApple.selectedTeamId && (
        <div className="signing-maintenance">
          <div><strong>Signing identity</strong><span>Review the exact Apple certificate impact before changing the active account or team.</span></div>
          <button className="ghost-action" disabled={signingPending} onClick={() => void reviewSigning()} type="button">{signingPending && !signingPreflight ? 'Checking…' : 'Review signing'}</button>
        </div>
      )}
      {canManageSigner && managedEntryAvailable && !needsCredential && <button className="ghost-action" onClick={() => setReplacementOpen((open) => !open)} type="button">Use a different Apple account</button>}
      {replacementOpen && <section className="signing-impact" aria-label="Replacement Apple account">
        <h3>Verify a different Apple account</h3>
        <p>The working account remains active until the new account, returned team, signing identity, and app registrations finish together.</p>
        {!replacementCandidate && <form className="form-grid" onSubmit={(event) => { event.preventDefault(); void connectReplacement() }}>
          <label className="form-field"><span>Replacement Apple Account</span><input autoComplete="username" onChange={(event) => setReplacementAppleId(event.currentTarget.value)} type="email" value={replacementAppleId} /></label>
          <label className="form-field"><span>Password</span><input autoComplete="current-password" onChange={(event) => setReplacementPassword(event.currentTarget.value)} type="password" value={replacementPassword} /></label>
          <button className="primary-action" disabled={!replacementAppleId.trim() || !replacementPassword || signingPending} type="submit">Verify replacement account</button>
        </form>}
        {replacementCandidate?.state === 'two-factor-required' && <div className="form-grid"><label className="form-field"><span>{replacementCandidate.challengeKind ?? '2FA'} code</span><input autoComplete="one-time-code" inputMode="numeric" maxLength={6} onChange={(event) => setReplacementCode(event.currentTarget.value.replace(/\D/g, '').slice(0, 6))} value={replacementCode} /></label><button className="primary-action" disabled={!/^\d{6}$/.test(replacementCode) || signingPending} onClick={() => void verifyReplacementTwoFactor()} type="button">Continue</button></div>}
        {replacementCandidate?.state === 'validated' && <div className="apple-team-list" role="radiogroup" aria-label="Replacement Apple Developer Team">{replacementCandidate.teams.map((team) => <button key={team.teamId} onClick={() => void reviewReplacementTeam(team.teamId)} role="radio" type="button"><span><strong>{team.name}</strong><small>{team.type} · returned by Apple</small></span><span className="apple-team-radio" /></button>)}</div>}
      </section>}
      {signingPreflight && (
        <section className="signing-impact" aria-label="Signing replacement impact">
          <h3>{signingPreflight.impact === 'reuse' ? 'Current identity can be reused' : signingPreflight.impact === 'mint' ? 'Sideport can create its first identity' : 'Replacing signing affects Apple certificates'}</h3>
          <dl className="detail-list">
            <div><dt>Apple certificates</dt><dd>{signingPreflight.appleCertificates.length}</dd></div>
            <div><dt>Registered apps</dt><dd>{signingPreflight.registrationCount}</dd></div>
            <div><dt>iPhones</dt><dd>{signingPreflight.deviceCount}</dd></div>
            <div><dt>Profiles</dt><dd>{signingPreflight.profileCount}</dd></div>
          </dl>
          {signingPreflight.appleCertificates.map((certificate) => <p key={certificate.id}>Certificate ending {certificate.serialSuffix ?? certificate.id.slice(-4)} will be the only certificate authorized for replacement.</p>)}
          {signingPreflight.requiresAcknowledgement && <label className="checkbox-row"><input checked={signingAcknowledged} onChange={(event) => setSigningAcknowledged(event.currentTarget.checked)} type="checkbox" /> I understand the exact certificate list above.</label>}
          <button className="primary-action" disabled={signingPending || (signingPreflight.requiresAcknowledgement && !signingAcknowledged)} onClick={() => void replaceSigning()} type="button">{signingPending ? 'Replacing…' : signingPreflight.impact === 'reuse' ? 'Keep current identity' : 'Replace signing identity'}</button>
        </section>
      )}
      {signingOperation?.status === 'succeeded' && <p className="mutation-message success">The new signing identity was verified.</p>}
      {signingOperation?.status === 'blocked' && <p className="mutation-message error" role="alert">{signingOperation.error?.message ?? 'The signing impact changed. Review it again.'}</p>}
      {signingOperation?.status === 'recovery-required' && <div className="mutation-message error" role="alert"><strong>Signing change needs recovery.</strong> Authenticate the same replacement Apple account again, choose the same returned team, review the unchanged impact, then retry. Sideport will reuse this saved operation and must not repeat a completed certificate replacement.<button className="ghost-action" onClick={() => { setReplacementCandidate(null); setSigningPreflight(null); setSigningAcknowledged(false); setReplacementOpen(true) }} type="button">Authenticate again and resume saved signing change</button></div>}
      {signingError && <p className="mutation-message error" role="alert">{signingError}</p>}
    </div>
  )
}

function PersonalAppleTeamList({ teams, selectedTeamId, disabled, onSelect }: { teams: PersonalAppleSummary['teams']; selectedTeamId?: string | null; disabled: boolean; onSelect: (teamId: string) => void }) {
  return (
    <div className="apple-team-list" role="radiogroup" aria-label="Apple Developer Team">
      {teams.map((team) => (
        <button aria-checked={selectedTeamId === team.teamId} disabled={disabled} key={team.teamId} onClick={() => onSelect(team.teamId)} role="radio" type="button">
          <span><strong>{team.name}</strong><small>{team.type} · returned by Apple</small></span>
          {selectedTeamId === team.teamId ? <CheckCircle2 size={19} /> : <span className="apple-team-radio" />}
        </button>
      ))}
    </div>
  )
}

function AppleAccessCapabilityList({ capabilities }: { capabilities: AppleAccessCapabilitySummary[] }) {
  return (
    <div className="capability-list">
      {capabilities.map((capability) => (
        <article className="capability-row" key={capability.id}>
          <div>
            <strong>{capability.label}</strong>
            <span>{capability.endpoint}</span>
          </div>
          <StatusPill state={capabilityHealth(capability)} label={capabilityStateLabel(capability.state)} />
          <div className="capability-detail">
            <span>{capability.httpStatus ? `HTTP ${capability.httpStatus}` : 'Not checked'}</span>
            {capability.count !== undefined && <span>{capability.count} returned</span>}
            <SourcePill source={capability.source} label={sourceLabel(capability.source)} />
          </div>
          <p>{capability.detail}</p>
        </article>
      ))}
    </div>
  )
}

function DeviceConnectivitySelfTest() {
  const [result, setResult] = useState<DeviceDiagnosticsDto | null>(null)
  const [running, setRunning] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const run = async () => {
    setRunning(true)
    setError(null)
    try {
      setResult(await runDeviceDiagnostics())
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setRunning(false)
    }
  }

  return (
    <Panel title="Device connectivity self-test">
      <p className="muted">Walks the device transport chain — usbmux socket → device discovery → trust/pairing — and shows the first layer that fails, with how to fix it.</p>
      <button className="primary-action" disabled={running} onClick={run} type="button">
        <Stethoscope size={16} /> {running ? 'Running check…' : 'Run connectivity check'}
      </button>
      {error && <p className="muted selftest-error">Could not run the check: {error}</p>}
      {result && (
        <ul className="selftest-list">
          {result.checks.map((check) => (
            <li className="selftest-item" key={check.id}>
              <StatusPill state={check.status === 'ok' ? 'healthy' : check.status === 'warning' ? 'warning' : 'blocked'} label={check.status} />
              <div className="selftest-body">
                <strong>{check.label}</strong>
                <p className="muted selftest-detail">{check.detail}</p>
                {check.remediation && <p className="selftest-remediation">→ {check.remediation}</p>}
              </div>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  )
}

function severityLabel(severity: Severity): string {
  return severity.charAt(0).toUpperCase() + severity.slice(1)
}

function issueStatusLabel(status: IssueStatus): string {
  if (status === 'unresolved') return 'Unresolved'
  if (status === 'investigating') return 'Investigating'
  if (status === 'resolved') return 'Resolved'
  return 'Ignored'
}

export function DiagnosticsPage({ data }: { data: SideportReadModel }) {
  const [severityFacet, setSeverityFacet] = useState<'all' | Severity>('all')
  const [statusFacet, setStatusFacet] = useState<'all' | IssueStatus>('all')
  const filteredIssues = data.issues.filter((issue) => (severityFacet === 'all' || issue.severity === severityFacet) && (statusFacet === 'all' || issue.status === statusFacet))

  return (
    <div className="page-stack">
      <PageHeader eyebrow="Diagnostics" title="Runtime failure evidence" description="Durable issues come from operation evidence. Derived API fetch failures still appear only when the issue endpoint is unavailable." />
      <DeviceConnectivitySelfTest />

      <Panel title={`Issues (${filteredIssues.length})`}>
        {data.issues.length ? (
          <>
            <div className="devices-toolbar">
              <div className="facet-group" role="group" aria-label="Filter by severity">
                <span className="facet-label"><Filter size={13} /> Severity</span>
                {(['all', 'info', 'warning', 'error', 'fatal'] as const).map((value) => (
                  <FacetToggleButton key={value} onClick={() => setSeverityFacet(value)} pressed={severityFacet === value}>{value === 'all' ? 'All' : severityLabel(value)}</FacetToggleButton>
                ))}
              </div>
              <div className="facet-group" role="group" aria-label="Filter by status">
                <span className="facet-label">Status</span>
                {(['all', 'unresolved', 'investigating', 'resolved', 'ignored'] as const).map((value) => (
                  <FacetToggleButton key={value} onClick={() => setStatusFacet(value)} pressed={statusFacet === value}>{value === 'all' ? 'All' : issueStatusLabel(value)}</FacetToggleButton>
                ))}
              </div>
              <span className="devices-count">{filteredIssues.length} of {data.issues.length} issues</span>
            </div>
            {filteredIssues.length ? <DiagnosticIssueList issues={filteredIssues} /> : <EmptyState icon={Filter} title="No issues match this filter" detail="Reset the severity and status filters to see all diagnostic issues." />}
          </>
        ) : <EmptyState icon={Stethoscope} title="No durable diagnostic issues" detail="No grouped operation failures were returned by /api/diagnostics/issues for this snapshot." />}
      </Panel>

      <Panel title="Log highlights">
        {data.logs.length ? <OperationLogList logs={data.logs.slice(0, 10)} /> : <EmptyState icon={Activity} title="No API logs yet" detail="The runtime log endpoint has not returned entries for this snapshot." />}
      </Panel>
      <Panel title="Advanced log tail">
        {data.logs.length ? <LogTailConsole logs={data.logs} /> : <EmptyState icon={Terminal} title="No log tail yet" detail="The protected log stream is empty for this snapshot." />}
      </Panel>
    </div>
  )
}

function schedulerIntervalCopy(value: string | undefined): string {
  if (!value) return 'Unavailable'
  const match = /^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})$/.exec(value)
  if (!match) return value
  const totalHours = Number(match[1] ?? 0) * 24 + Number(match[2])
  const minutes = Number(match[3])
  if (totalHours === 1 && minutes === 0) return 'Every hour'
  if (totalHours > 0 && minutes === 0) return `Every ${totalHours} hours`
  if (totalHours === 0 && minutes === 1) return 'Every minute'
  if (totalHours === 0 && minutes > 0) return `Every ${minutes} minutes`
  return value
}

export function SettingsPage({ system, apiStatus, canManageScheduler = false, schedulerSettingsService = updateSideportSchedulerSettings, onApiTokenSaved }: { system: SystemStatus; apiStatus: AdminDataStatus; canManageScheduler?: boolean; schedulerSettingsService?: (enabled: boolean) => Promise<SchedulerStatusDto>; onApiTokenSaved?: () => void }) {
  const queryClient = useQueryClient()
  const [token, setToken] = useState(getStoredSideportApiToken())
  const [saved, setSaved] = useState(false)
  const [schedulerOverride, setSchedulerOverride] = useState<SchedulerStatusDto | null>(null)
  const [schedulerPending, setSchedulerPending] = useState(false)
  const [schedulerError, setSchedulerError] = useState<string | null>(null)
  const schedulerEnabled = schedulerOverride?.enabled ?? system.scheduler.enabled
  const schedulerPolicy = schedulerOverride?.policy ?? system.scheduler.policy
  const nextEvaluationAt = schedulerOverride?.nextEvaluationAt ?? system.scheduler.nextEvaluationAt
  const schedulerLock = schedulerOverride?.concurrency ?? system.scheduler.concurrency
  const saveToken = () => {
    saveSideportApiToken(token)
    setSaved(true)
    onApiTokenSaved?.()
  }
  const changeScheduler = async () => {
    if (!canManageScheduler || schedulerPending) return
    setSchedulerPending(true)
    setSchedulerError(null)
    try {
      const updated = await schedulerSettingsService(!schedulerEnabled)
      setSchedulerOverride(updated)
      await queryClient.invalidateQueries({ queryKey: ['sideport-admin-data'] })
    } catch (reason) {
      setSchedulerError(reason instanceof Error ? reason.message : 'Sideport could not change automatic refresh.')
    } finally {
      setSchedulerPending(false)
    }
  }

  return (
    <div className="page-stack">
      <PageHeader eyebrow="Settings" title="Control plane status and session access" description="Runtime checks, observability posture, and the browser-only bearer token live here. Server secrets are never bundled into the admin JavaScript." />
      <Panel title="Runtime checks">
        <SystemChecks system={system} />
      </Panel>
      <Panel title="Browser API token">
        <div className="token-panel">
          <label className="form-field">
            <span>Bearer token for this browser session</span>
            <input autoComplete="off" onChange={(event) => setToken(event.currentTarget.value)} placeholder={apiStatus.mode === 'partial' ? 'Paste token after 401' : 'Optional'} type="password" value={token} />
          </label>
          <button className="primary-action" onClick={saveToken} type="button"><ShieldCheck size={16} /> Save token</button>
          <p>{saved ? 'Token saved for this browser session. The live API snapshot will retry with the new bearer token.' : apiStatus.mode === 'partial' ? 'If protected API calls return 401, save the Sideport API token here. It is stored in sessionStorage, not bundled into the image.' : 'Use this only when Sideport:Api:AuthToken is enabled and the portal is served from the same origin.'}</p>
        </div>
      </Panel>
      <Panel title="Scheduler">
        <dl className="detail-list">
          <div><dt>Automatic refresh</dt><dd>{schedulerEnabled ? 'Enabled' : 'Disabled'}</dd></div>
          <div><dt>Checks</dt><dd>{schedulerIntervalCopy(schedulerPolicy?.evaluationInterval)} · only apps that are due</dd></div>
          <div><dt>Next check</dt><dd>{schedulerEnabled && nextEvaluationAt ? relativeTime(nextEvaluationAt) : 'Not scheduled'}</dd></div>
          <div><dt>Current work</dt><dd>{schedulerLock?.lockState === 'busy' ? 'One operation is running' : schedulerLock?.lockState === 'held' ? 'Paused for an unresolved device operation' : 'Idle'}</dd></div>
        </dl>
        <SourcePill source={system.scheduler.source} label={sourceLabel(system.scheduler.source)} />
        {schedulerError && <p className="mutation-message error" role="alert">{schedulerError}</p>}
        <div className="dialog-actions">
          <button className="primary-action" disabled={!canManageScheduler || schedulerPending} onClick={() => void changeScheduler()} type="button">{schedulerPending ? 'Saving…' : schedulerEnabled ? 'Turn off automatic refresh' : 'Turn on automatic refresh'}</button>
        </div>
        {!canManageScheduler && <p className="pipeline-note">This session can view scheduler status but cannot change it.</p>}
      </Panel>

      <Panel title="Signer binary">
        <dl className="detail-list">
          <div><dt>Status</dt><dd>{system.ready.checks.signer.ok ? 'Ready' : 'Not found'}</dd></div>
          <div><dt>Path</dt><dd>{system.ready.checks.signer.path}</dd></div>
          <div><dt>Method</dt><dd>Bundled zsign re-signs each IPA before install.</dd></div>
        </dl>
        <SourcePill source={system.ready.checks.signer.source} label={sourceLabel(system.ready.checks.signer.source)} />
      </Panel>

      <Panel title="Device bridge">
        <dl className="detail-list">
          <div><dt>Transport</dt><dd>usbmuxd socket with netmuxd for cable-free Wi-Fi reach.</dd></div>
          <div><dt>Health</dt><dd>Run the device connectivity self-test on the Diagnostics page.</dd></div>
        </dl>
        <SourcePill source="derived" label={sourceLabel('derived')} />
      </Panel>

      <Panel title="Data retention">
        <dl className="detail-list">
          <div><dt>Operation logs</dt><dd>Held in the API log store; the retention window is not yet configurable from the UI.</dd></div>
          <div><dt>Trace links</dt><dd>Not reported yet. Durable issues currently link operation and stage evidence only.</dd></div>
        </dl>
        <SourcePill source="planned" label={sourceLabel('planned')} />
      </Panel>

      <Panel title="Observability">
        <div className="observability-panel">
          <Network size={22} />
          <div>
            <h3>{system.observability.exporter}</h3>
            <p>The live log stream is available now. Trace links appear here once the API reports them for a blocker or failed operation.</p>
          </div>
          <SourcePill source={system.observability.source} label={sourceLabel(system.observability.source)} />
        </div>
      </Panel>

      <Panel title="Integrations">
        <dl className="detail-list">
          <div><dt>Storybook</dt><dd>This admin UI is developed and reviewed in Storybook before wiring live data.</dd></div>
          <div><dt>OpenTelemetry collector</dt><dd>{system.observability.exporter}</dd></div>
          <div><dt>Crash reporting</dt><dd>Sentry / Firebase Crashlytics are future, opt-in app-SDK integrations.</dd></div>
        </dl>
        <SourcePill source="planned" label={sourceLabel('planned')} />
      </Panel>
    </div>
  )
}

function CanonicalSettingsPage({ data, apiStatus, canManageScheduler, canManageSigner, schedulerSettingsService, onApiTokenSaved }: { data: SideportReadModel; apiStatus: AdminDataStatus; canManageScheduler: boolean; canManageSigner: boolean; schedulerSettingsService: (enabled: boolean) => Promise<SchedulerStatusDto>; onApiTokenSaved?: () => void }) {
  return (
    <div className="page-stack">
      <PageHeader eyebrow="Simple by default" title="Settings" description="Sign-in, automatic refresh, Apple signing, and technical setup." />
      <Panel title="Sign-in and recovery">
        <p className="muted">Managed by Authentik. Passkeys stay on the member's trusted devices; Sideport stores only membership by validated OIDC identity.</p>
      </Panel>
      <SettingsPage system={data.system} apiStatus={apiStatus} canManageScheduler={canManageScheduler} schedulerSettingsService={schedulerSettingsService} onApiTokenSaved={onApiTokenSaved} />
      {canManageSigner ? <AppleAccessPage appleAccess={data.appleAccess} personalApple={data.personalApple} canManageSigner={canManageSigner} /> : null}
    </div>
  )
}

function PageHeader({ eyebrow, title, description }: { eyebrow: string; title: string; description: string }) {
  return (
    <header className="page-header">
      <div className="eyebrow">{eyebrow}</div>
      <h1>{title}</h1>
      <p>{description}</p>
    </header>
  )
}

function MetricCard({ icon: Icon, label, value, detail, source, tone }: { icon: LucideIcon; label: string; value: string; detail: string; source: SourceKind; tone: string }) {
  return (
    <article className={`metric-card tone-${tone}`}>
      <div className="metric-icon"><Icon size={21} /></div>
      <div className="metric-value">{value}</div>
      <div className="metric-label">{label}</div>
      <p>{detail}</p>
      <SourcePill source={source} label={sourceLabel(source)} />
    </article>
  )
}

function Panel({ title, children, actionLabel, onAction }: { title: string; children: ReactNode; actionLabel?: string; onAction?: () => void }) {
  return (
    <section className="panel">
      <div className="panel-header">
        <h2>{title}</h2>
        {actionLabel && <button className="ghost-action" onClick={onAction} type="button">{actionLabel}<ChevronRight size={15} /></button>}
      </div>
      {children}
    </section>
  )
}

export function StatusPill({ state, label }: { state: HealthState; label: string }) {
  const Icon = state === 'healthy' ? CheckCircle2 : state === 'offline' ? CircleDashed : state === 'warning' ? AlertTriangle : XCircle
  return <span className={`status-pill ${state}`}><Icon size={14} />{label}</span>
}

function appleAccessHealth(appleAccess: AppleAccessSummary): HealthState {
  if (appleAccess.state === 'read-only-verified') return 'healthy'
  if (appleAccess.state === 'partial') return 'warning'
  if (appleAccess.state === 'not-configured' || appleAccess.state === 'unavailable') return 'offline'
  return 'blocked'
}

function capabilityHealth(capability: AppleAccessCapabilitySummary): HealthState {
  if (capability.state === 'verified') return 'healthy'
  if (capability.state === 'not-checked') return 'offline'
  if (capability.state === 'rate-limited') return 'warning'
  return 'blocked'
}

function personalAppleHealth(personalApple: PersonalAppleSummary): HealthState {
  if (personalApple.state === 'authenticated') return 'healthy'
  if (personalApple.state === 'credential-configured' || personalApple.state === 'two-factor-required') return 'warning'
  if (personalApple.state === 'not-configured' || personalApple.state === 'unavailable') return 'offline'
  return 'blocked'
}

function appleAccessStateLabel(state: AppleAccessSummary['state']): string {
  if (state === 'read-only-verified') return 'Read-only verified'
  if (state === 'not-configured') return 'Not configured'
  if (state === 'invalid-configuration') return 'Invalid configuration'
  if (state === 'partial') return 'Partial access'
  if (state === 'blocked') return 'Blocked'
  return 'Unavailable'
}

function capabilityStateLabel(state: AppleAccessCapabilitySummary['state']): string {
  if (state === 'verified') return 'Verified'
  if (state === 'not-checked') return 'Not checked'
  if (state === 'unauthorized') return 'Unauthorized'
  if (state === 'denied') return 'Denied'
  if (state === 'rate-limited') return 'Rate limited'
  return 'Failed'
}

function personalAppleStateLabel(state: PersonalAppleSummary['state']): string {
  if (state === 'authenticated') return 'Authenticated'
  if (state === 'credential-configured') return 'Credential configured'
  if (state === 'two-factor-required') return '2FA required'
  if (state === 'not-configured') return 'Not configured'
  return 'Unavailable'
}

function credentialCustodyShortLabel(custody: string): string {
  if (custody === 'macos-keychain') return 'macOS Keychain'
  if (custody === 'cached-grand-slam-session') return 'Cached session'
  if (custody === 'environment-or-sops') return 'SOPS/env'
  return 'Host secret'
}

function credentialCustodyLabel(custody: string): string {
  if (custody === 'macos-keychain') return 'the macOS login keychain'
  if (custody === 'cached-grand-slam-session') return 'cached GrandSlam session'
  if (custody === 'environment-or-sops') return 'environment/SOPS runtime secret'
  return 'host-side secret custody'
}

function apiModeLabel(mode: AdminDataStatus['mode']): string {
  if (mode === 'live') return 'Live API'
  if (mode === 'partial') return 'Live API degraded'
  if (mode === 'demo') return 'Demo data'
  return 'API unavailable'
}

export function SourcePill({ source, label }: { source: SourceKind; label: string }) {
  return <span className={`source-pill ${source}`}>{label}</span>
}

function SystemChecks({ system }: { system: SystemStatus }) {
  return (
    <div className="check-list">
      <CheckRow label="API process" ok={system.api.ok} source={system.api.source} detail="/healthz responds." />
      <CheckRow label="Operational readiness" ok={system.operational} source={system.ready.source} detail="Authenticated system checks for signing, storage, and device transport." />
      <CheckRow label="Anisette" ok={system.ready.checks.anisette.ok} source={system.ready.checks.anisette.source} detail={system.ready.checks.anisette.error ?? 'Client info available.'} />
      <CheckRow label="Signer binary" ok={system.ready.checks.signer.ok} source={system.ready.checks.signer.source} detail={system.ready.checks.signer.path} />
      <CheckRow label="API bearer token" ok={system.apiAuth.configured} source={system.apiAuth.source} detail="Do not expose /api refresh actions without auth." />
      <CheckRow label="Scheduler" ok={system.scheduler.enabled} source={system.scheduler.source} detail={system.scheduler.enabled ? 'Automatic due-only refresh is enabled.' : 'Automatic refresh scheduling is disabled.'} />
    </div>
  )
}

function CheckRow({ label, ok, detail, source }: { label: string; ok: boolean; detail: string; source: SourceKind }) {
  return (
    <div className="check-row">
      <div className={ok ? 'check-icon ok' : 'check-icon fail'}>{ok ? <CheckCircle2 size={16} /> : <XCircle size={16} />}</div>
      <div><strong>{label}</strong><span>{detail}</span></div>
      <SourcePill source={source} label={sourceLabel(source)} />
    </div>
  )
}

function FacetToggleButton({ pressed, onClick, children }: { pressed: boolean; onClick: () => void; children: ReactNode }) {
  if (pressed) {
    return <button aria-pressed="true" className="facet-chip" onClick={onClick} type="button">{children}</button>
  }
  return <button aria-pressed="false" className="facet-chip" onClick={onClick} type="button">{children}</button>
}

function DeviceInventoryTable({ devices, apps, onOpenDevice }: { devices: DeviceSummary[]; apps: RegisteredAppSummary[]; onOpenDevice?: (device: DeviceSummary) => void }) {
  const [sorting, setSorting] = useState<SortingState>([{ id: 'health', desc: false }])
  const columns = useMemo<Array<ColumnDef<DeviceSummary>>>(() => [
    {
      accessorKey: 'name',
      header: 'Device',
      cell: ({ row }) => <div className="table-device"><strong>{row.original.name}</strong><span>{row.original.productType} · iOS {row.original.osVersion}</span></div>,
    },
    {
      accessorKey: 'connection',
      header: 'Connection',
      cell: ({ row }) => <ConnectionBadge connection={row.original.connection} />,
    },
    {
      accessorKey: 'lastSeenAt.value',
      header: 'Last seen / current poll',
      cell: ({ row }) => <span>{row.original.currentPollAt ? `Now · ${relativeTime(row.original.currentPollAt.value)}` : row.original.hasDurableLastSeen ? relativeTime(row.original.lastSeenAt.value) : 'Not yet recorded'}</span>,
    },
    {
      id: 'apps',
      header: 'Registered apps',
      cell: ({ row }) => <span>{appsForDevice(apps, row.original.udid).length}/3</span>,
    },
    {
      id: 'installedApps',
      header: 'Installed apps',
      cell: ({ row }) => <span>{row.original.installedAppCount} total · {row.original.unmanagedAppCount} external</span>,
    },
    {
      id: 'expiry',
      header: 'Nearest registered expiry',
      cell: ({ row }) => <span>{expiryCopy(row.original.nearestExpiryAt?.value)}</span>,
    },
    {
      accessorKey: 'health',
      header: 'Health',
      cell: ({ row }) => <StatusPill state={row.original.health} label={healthCopy[row.original.health]} />,
    },
    {
      id: 'action',
      header: 'Actions',
      cell: ({ row }) => <button className="row-action" onClick={() => onOpenDevice?.(row.original)} type="button">Open</button>,
    },
  ], [apps, onOpenDevice])
  // eslint-disable-next-line react-hooks/incompatible-library -- TanStack Table exposes table helpers that React Compiler cannot memoize.
  const table = useReactTable({ data: devices, columns, state: { sorting }, onSortingChange: setSorting, getCoreRowModel: getCoreRowModel(), getSortedRowModel: getSortedRowModel() })

  return (
    <div className="table-shell">
      <table>
        <thead>
          {table.getHeaderGroups().map((group) => (
            <tr key={group.id}>{group.headers.map((header) => <th key={header.id} scope="col">{flexRender(header.column.columnDef.header, header.getContext())}</th>)}</tr>
          ))}
        </thead>
        <tbody>
          {table.getRowModel().rows.map((row) => (
            <tr key={row.id}>{row.getVisibleCells().map((cell) => <td key={cell.id}>{flexRender(cell.column.columnDef.cell, cell.getContext())}</td>)}</tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

export function DeviceCard({ device, apps, onOpen }: { device: DeviceSummary; apps: RegisteredAppSummary[]; onOpen?: () => void }) {
  return (
    <article className="device-card">
      <div className="device-card-top">
        <div><strong>{device.name}</strong><span>{device.productType} · {compactUdid(device.udid)}</span></div>
        <StatusPill state={device.health} label={healthCopy[device.health]} />
      </div>
      <div className="device-card-grid">
        <FactTile label="Connection" value={connectionLabel(device.connection)} source="live" />
        <FactTile label="Registered apps" value={`${apps.length}/3`} source="derived" />
        <FactTile label="Installed apps" value={`${device.installedAppCount} total`} source="live" />
        <FactTile label="Registered expiry" value={expiryCopy(device.nearestExpiryAt?.value)} source={device.nearestExpiryAt?.source ?? 'planned'} />
      </div>
      <button className="row-action" onClick={onOpen} type="button">Open device</button>
    </article>
  )
}

export function AppSlotGrid({ apps, canRegister, onInstallApp }: { apps: RegisteredAppSummary[]; canRegister: boolean; onInstallApp?: () => void }) {
  const slots = [0, 1, 2].map((index) => apps[index] ?? null)
  return (
    <div className="slot-grid">
      {slots.map((app, index) => (
        <article className={app ? 'slot-card filled' : 'slot-card empty'} key={index}>
          <div className="slot-number">Slot {index + 1}</div>
          {app ? <AppSummary app={app} /> : (
            <div className="empty-slot">
              <Plus size={18} />
              <span>Available</span>
              <small>{canRegister ? 'No Sideport registration' : 'Registration endpoint disabled'}</small>
              {canRegister && <button className="row-action" onClick={onInstallApp} type="button">Register app</button>}
            </div>
          )}
        </article>
      ))}
    </div>
  )
}

function AppSummary({ app }: { app: RegisteredAppSummary }) {
  return (
    <div className="app-summary">
      <div className={`app-icon ${app.iconTone}`}>{app.displayName.value.slice(0, 1)}</div>
      <div>
        <strong>{app.displayName.value}</strong>
        <span>{app.bundleId}</span>
        <small>{app.lifecycle === 'pending-install' ? 'Install not verified yet' : `Expires ${timeUntil(app.expiresAt?.value)}`}</small>
      </div>
    </div>
  )
}

function RenewalLane({ title, items, apps, apiStatus }: { title: string; items: RenewalItem[]; apps: RegisteredAppSummary[]; apiStatus?: AdminDataStatus }) {
  return <Panel title={`${title} (${items.length})`}>{items.length ? <RenewalQueueList items={items} apps={apps} apiStatus={apiStatus} /> : <EmptyState icon={CheckCircle2} title={`No ${title.toLowerCase()} renewals`} detail="Nothing in this risk lane for the current API snapshot." />}</Panel>
}

export function RenewalQueueList({ items, apps, compact = false, apiStatus }: { items: RenewalItem[]; apps: RegisteredAppSummary[]; compact?: boolean; apiStatus?: AdminDataStatus }) {
  return (
    <div className={compact ? 'renewal-list compact' : 'renewal-list'}>
      {items.map((item) => {
        const app = apps.find((candidate) => candidate.bundleId === item.bundleId && candidate.deviceUdid === item.deviceUdid)
        return (
          <article className="renewal-item" key={item.id}>
            <div>
              <strong>{app?.displayName.value ?? item.bundleId}</strong>
              <span>{item.bundleId}</span>
            </div>
            <div className="renewal-meta">
              <StatusDot state={item.risk === 'blocked' ? 'failed' : item.risk === 'due-now' ? 'warning' : 'ok'} />
              <span>{riskCopy[item.risk]} · {statusCopy[item.status]}</span>
            </div>
            {item.expiresAt && <span className="muted">{timeUntil(item.expiresAt)}</span>}
            {item.operationId && <span className="muted">Operation {item.operationId}</span>}
            {item.blocker && <p>{item.blocker}</p>}
            <SourcePill source={item.source} label={sourceLabel(item.source)} />
            {!compact && apiStatus && <RefreshOperationButton apiStatus={apiStatus} bundleId={item.bundleId} className="row-action inline-action" deviceUdid={item.deviceUdid} small />}
          </article>
        )
      })}
    </div>
  )
}

function RefreshOperationButton({ apiStatus, deviceUdid, bundleId, className, small = false }: { apiStatus: AdminDataStatus; deviceUdid: string; bundleId: string; className: string; small?: boolean }) {
  const queryClient = useQueryClient()
  const [preflight, setPreflight] = useState<OperationPreflightDto | null>(null)
  const [dialogOpen, setDialogOpen] = useState(false)
  const preflightMutation = useMutation({
    mutationFn: () => preflightSideportRefresh(deviceUdid, bundleId),
    onSuccess: (result) => {
      setPreflight(result)
      setDialogOpen(true)
    },
  })
  const refreshMutation = useMutation({
    mutationFn: () => refreshSideportApp(deviceUdid, bundleId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['sideport-admin-data'] }),
  })
  const disabled = !apiStatus.canMutate || preflightMutation.isPending || refreshMutation.isPending
  const label = preflightMutation.isPending
    ? 'Checking'
    : refreshMutation.isPending
      ? 'Starting'
      : small ? 'Start operation' : 'Start refresh operation'

  return (
    <>
      <button className={className} disabled={disabled} onClick={() => preflightMutation.mutate()} type="button"><RefreshCw size={small ? 14 : 16} /> {label}</button>
      {preflightMutation.error && <p className="mutation-message error">{preflightMutation.error.message}</p>}
      {refreshMutation.isSuccess && <p className="mutation-message success">Operation {refreshMutation.data.operationId} finished with status {refreshMutation.data.status}. Results will reload from the API.</p>}
      {refreshMutation.error && <p className="mutation-message error">{refreshMutation.error.message}</p>}
      <Dialog.Root open={dialogOpen} onOpenChange={setDialogOpen}>
        <Dialog.Portal>
          <Dialog.Overlay className="dialog-overlay" />
          <Dialog.Content className="dialog-content refresh-dialog">
            <Dialog.Title>Confirm refresh operation</Dialog.Title>
            <Dialog.Description>Sideport re-runs preflight on the server before signing. Review the current blockers, warnings, limits, and planned mutations before starting.</Dialog.Description>
            {preflight && <OperationPreflightSummary preflight={preflight} />}
            <div className="dialog-actions">
              <Dialog.Close asChild><button className="ghost-action" type="button">Close</button></Dialog.Close>
              <button className="primary-action" disabled={!preflight?.ready || refreshMutation.isPending} onClick={() => refreshMutation.mutate()} type="button"><RefreshCw size={16} /> {refreshMutation.isPending ? 'Starting operation' : 'Confirm and start'}</button>
            </div>
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>
    </>
  )
}

function OperationPreflightSummary({ preflight }: { preflight: OperationPreflightDto }) {
  return (
    <div className="preflight-summary">
      <StatusPill state={preflight.ready ? 'healthy' : 'blocked'} label={preflight.ready ? 'Ready' : 'Blocked'} />
      {preflight.blockers.length > 0 && <OperationPreflightList title="Blockers" items={preflight.blockers.map((item) => item.message)} tone="error" />}
      {preflight.warnings.length > 0 && <OperationPreflightList title="Warnings" items={preflight.warnings.map((item) => item.message)} tone="warning" />}
      {preflight.scarceLimits.length > 0 && <OperationPreflightList title="Limits" items={preflight.scarceLimits.map((limit) => `${limit.label}: ${limit.used}/${limit.limit}`)} tone="neutral" />}
      {preflight.plannedMutations.length > 0 && <OperationPreflightList title="Planned mutations" items={preflight.plannedMutations} tone="neutral" />}
    </div>
  )
}

function OperationPreflightList({ title, items, tone }: { title: string; items: string[]; tone: 'error' | 'warning' | 'neutral' }) {
  return (
    <div className={`preflight-list ${tone}`}>
      <strong>{title}</strong>
      <ul>{items.map((item) => <li key={item}>{item}</li>)}</ul>
    </div>
  )
}

export function DiagnosticIssueList({ issues, compact = false }: { issues: DiagnosticIssue[]; compact?: boolean }) {
  return (
    <div className={compact ? 'issue-list compact' : 'issue-list'}>
      {issues.map((issue) => {
        const recency = issue.source === 'derived'
          ? `derived from snapshot ${relativeTime(issue.lastSeenAt)}`
          : `last seen ${relativeTime(issue.lastSeenAt)}`
        return (
          <article className={`issue-card severity-${issue.severity}`} key={issue.id}>
            <div className="issue-head">
              <div>
                <strong>{issue.category}</strong>
                <span>{issue.status} · {recency}</span>
              </div>
              <SourcePill source={issue.source} label={sourceLabel(issue.source)} />
            </div>
            {!compact && <p>{issue.logSnippet}</p>}
            <div className="trace-row"><Activity size={15} /><span>{issue.operationId}</span><span>{issue.traceId === 'trace-not-reported' ? 'No trace reported' : issue.traceId}</span></div>
            {!compact && <div className="span-strip">{issue.spanSummary.map((span) => <span className={`span-chip ${span.state}`} key={span.name}>{span.name} · {span.durationMs}ms</span>)}</div>}
          </article>
        )
      })}
    </div>
  )
}

function ActivityTimeline({ events }: { events: ActivityEvent[] }) {
  return (
    <div className="timeline">
      {events.map((event) => (
        <div className="timeline-event" key={event.id}>
          <StatusDot state={event.state === 'failed' ? 'failed' : event.state === 'warning' ? 'warning' : 'ok'} />
          <div><strong>{event.title}</strong><span>{event.detail}</span><small>{shortDateTime(event.at)} · {event.actor}</small></div>
          <SourcePill source={event.source} label={sourceLabel(event.source)} />
        </div>
      ))}
    </div>
  )
}

function OperationLogList({ logs }: { logs: OperationLogEntry[] }) {
  return (
    <div className="log-list">
      {logs.map((entry) => (
        <article className={`log-entry level-${entry.level.toLowerCase()}`} key={entry.id}>
          <div className="log-head">
            <span className="log-level">{entry.level}</span>
            <strong>{entry.category}</strong>
            <small>{shortDateTime(entry.at)}</small>
          </div>
          <p>{entry.message}</p>
          {entry.exceptionMessage && <small className="log-exception">{entry.exceptionType}: {entry.exceptionMessage}</small>}
          <SourcePill source={entry.source} label={sourceLabel(entry.source)} />
        </article>
      ))}
    </div>
  )
}

function LogTailConsole({ logs }: { logs: OperationLogEntry[] }) {
  return (
    <div className="log-tail" role="log" aria-label="Advanced API log tail">
      <div className="log-tail-toolbar">
        <span><Terminal size={15} /> Protected /api/logs tail</span>
        <small>{logs.length} lines · newest first</small>
      </div>
      <pre aria-label="Log entries" className="log-tail-scroll" tabIndex={0}>
        {logs.slice(0, 60).map((entry) => <code className={`tail-line level-${entry.level.toLowerCase()}`} key={entry.id}>{tailLine(entry)}</code>)}
      </pre>
    </div>
  )
}

function PreflightList({ items }: { items: Array<[string, boolean, SourceKind]> }) {
  return <div className="check-list">{items.map(([label, ok, source]) => <CheckRow key={label} label={label} ok={ok} detail={ok ? 'Ready' : 'Not available yet'} source={source} />)}</div>
}

function expiryCopy(value?: string): string {
  return value ? timeUntil(value) : 'No registered expiry'
}

function tailLine(entry: OperationLogEntry): string {
  const at = new Date(entry.at)
  const stamp = Number.isNaN(at.getTime()) ? entry.at : at.toISOString()
  const suffix = entry.exceptionMessage ? ` :: ${entry.exceptionType ?? 'Exception'}: ${entry.exceptionMessage}` : ''
  return `${stamp}  ${entry.level.padEnd(11)}  ${entry.category}  ${entry.message}${suffix}\n`
}

function EmptyState({ icon: Icon, title, detail, actionLabel, onAction }: { icon: LucideIcon; title: string; detail: string; actionLabel?: string; onAction?: () => void }) {
  return (
    <div className="empty-state">
      <Icon size={24} />
      <strong>{title}</strong>
      <span>{detail}</span>
      {actionLabel && onAction && <button className="primary-action empty-state-action" onClick={onAction} type="button"><Plus size={16} /> {actionLabel}</button>}
    </div>
  )
}

function FactTile({ label, value, source }: { label: string; value: string; source: SourceKind }) {
  return (
    <div className="fact-tile">
      <span>{label}</span>
      <strong>{value}</strong>
      <SourcePill source={source} label={sourceLabel(source)} />
    </div>
  )
}

function ConnectionBadge({ connection }: { connection: DeviceSummary['connection'] }) {
  const Icon = connection === 'wifi' ? Wifi : connection === 'usb' ? Cable : HardDrive
  return <span className={`connection-badge ${connection}`}><Icon size={14} />{connectionLabel(connection)}</span>
}

function StatusDot({ state }: { state: 'ok' | 'warning' | 'failed' }) {
  return <span className={`status-dot ${state}`} />
}

function appsForDevice(apps: RegisteredAppSummary[], udid: string) {
  return apps.filter((app) => app.deviceUdid === udid)
}

function connectionLabel(connection: DeviceSummary['connection']) {
  if (connection === 'wifi') return 'Wi-Fi'
  if (connection === 'usb') return 'USB'
  return 'Offline'
}

export type PipelineStageState = 'pending' | 'active' | 'done' | 'failed'

export interface PipelineStage {
  id: string
  label: string
  detail?: string
  state: PipelineStageState
}

const defaultPipelineStages: PipelineStage[] = [
  { id: 'authorize', label: 'Authorize', detail: 'GrandSlam login', state: 'pending' },
  { id: 'provision', label: 'Provision', detail: 'App ID + profile', state: 'pending' },
  { id: 'sign', label: 'Sign', detail: 'zsign re-sign', state: 'pending' },
  { id: 'install', label: 'Install', detail: 'Push to device', state: 'pending' },
  { id: 'verify', label: 'Verify', detail: 'Bundle + profile evidence', state: 'pending' },
]

export function SigningPipeline({ title = 'Sign · install · verify', stages = defaultPipelineStages, note }: { title?: string; stages?: PipelineStage[]; note?: string }) {
  const failed = stages.some((stage) => stage.state === 'failed')
  const active = stages.find((stage) => stage.state === 'active')
  const allDone = stages.length > 0 && stages.every((stage) => stage.state === 'done')
  const overall: HealthState = failed ? 'failed' : allDone ? 'healthy' : active ? 'warning' : 'offline'
  const overallLabel = failed ? 'Failed' : allDone ? 'Verified' : active ? `Running · ${active.label}` : 'Not started'

  return (
    <section className="signing-pipeline" aria-label={`${title} pipeline`}>
      <div className="pipeline-head">
        <div>
          <h3>{title}</h3>
          <span className="pipeline-sub">One operation at a time, in order, on the single-flight signer.</span>
        </div>
        <StatusPill state={overall} label={overallLabel} />
      </div>
      <div className="pipeline-stages">
        {stages.map((stage) => (
          <div className={`pipeline-stage ${stage.state}`} key={stage.id}>
            <span className="stage-icon">
              {stage.state === 'done' ? <CheckCircle2 size={17} />
                : stage.state === 'failed' ? <XCircle size={17} />
                : stage.state === 'active' ? <Loader2 className="stage-spin" size={17} />
                : <CircleDashed size={17} />}
            </span>
            <strong>{stage.label}</strong>
            {stage.detail && <small>{stage.detail}</small>}
          </div>
        ))}
      </div>
      {note && <p className="pipeline-note"><AlertTriangle size={14} /> {note}</p>}
    </section>
  )
}

interface CommandTarget {
  id: string
  label: string
  meta?: string
  group: 'Actions' | 'Go to' | 'Devices' | 'Apps'
  icon: LucideIcon
  run: () => void
}

function CommandMenu({ open, onOpenChange, data, onNavigate, onOpenDevice, onAddIPhone, onAddApp }: { open: boolean; onOpenChange: (open: boolean) => void; data: SideportReadModel; onNavigate: (route: RouteId) => void; onOpenDevice: (device: DeviceSummary) => void; onAddIPhone: () => void; onAddApp?: () => void }) {
  const [query, setQuery] = useState('')
  const dismiss = (run: () => void) => {
    run()
    onOpenChange(false)
    setQuery('')
  }

  const targets: CommandTarget[] = [
    { id: 'action:add-iphone', label: 'Add iPhone', meta: 'Connect once with USB', group: 'Actions', icon: Smartphone, run: onAddIPhone },
    ...(onAddApp ? [{ id: 'action:add-app', label: 'Add app', meta: 'Computer, server, or GitHub', group: 'Actions' as const, icon: Plus, run: onAddApp }] : []),
    ...routeItems.map((item) => ({ id: `route:${item.id}`, label: item.label, meta: 'Screen', group: 'Go to' as const, icon: item.icon, run: () => onNavigate(item.id) })),
    ...data.devices.map((device) => ({ id: `device:${device.udid}`, label: device.name, meta: `${connectionLabel(device.connection)} · ${compactUdid(device.udid)}`, group: 'Devices' as const, icon: Smartphone, run: () => onOpenDevice(device) })),
    ...data.catalogApps.map((app) => ({ id: `catalog:${app.id}`, label: app.name, meta: `${app.versionLabel} · ${app.purpose}`, group: 'Apps' as const, icon: Package, run: () => onNavigate('apps') })),
    ...data.apps.map((app) => ({ id: `app:${app.deviceUdid}:${app.bundleId}`, label: app.displayName.value, meta: app.bundleId, group: 'Apps' as const, icon: Package, run: () => onNavigate('apps') })),
  ]
  const q = query.trim().toLowerCase()
  const filtered = q ? targets.filter((target) => `${target.label} ${target.meta ?? ''}`.toLowerCase().includes(q)) : targets
  const groups = (['Actions', 'Go to', 'Devices', 'Apps'] as const).map((group) => ({ group, items: filtered.filter((target) => target.group === group) })).filter((entry) => entry.items.length)

  return (
    <Dialog.Root onOpenChange={onOpenChange} open={open}>
      <Dialog.Portal>
        <Dialog.Overlay className="command-overlay" />
        <Dialog.Content aria-describedby={undefined} className="command-content">
          <Dialog.Title className="visually-hidden">Search and commands</Dialog.Title>
          <div className="command-search">
            <Command size={18} />
            <input autoFocus onChange={(event) => setQuery(event.currentTarget.value)} placeholder="Search devices, apps, screens…" value={query} />
            <span className="search-kbd"><kbd>esc</kbd></span>
          </div>
          <div className="command-list">
            {groups.length ? groups.map((entry) => (
              <div key={entry.group}>
                <div className="command-section-label">{entry.group}</div>
                {entry.items.map((target) => {
                  const Icon = target.icon
                  return (
                    <button className="command-item" key={target.id} onClick={() => dismiss(target.run)} type="button">
                      <Icon size={16} />
                      <span>{target.label}</span>
                      {target.meta && <span className="ci-meta">{target.meta}</span>}
                    </button>
                  )
                })}
              </div>
            )) : <div className="command-empty">No matches for “{query}”.</div>}
          </div>
          <div className="command-foot">
            <span><kbd>tab</kbd> move</span>
            <span><kbd>↵</kbd> open</span>
            <span><kbd>esc</kbd> close</span>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

const roleCopy: Record<WorkspaceRole, { label: string; permissions: string }> = {
  owner: { label: 'Owner', permissions: 'Credentials, settings, users, refresh, and destructive actions.' },
  family: { label: 'Member', permissions: 'Approved apps, owned devices, and relevant activity.' },
}

const memberStatusCopy: Record<MemberStatus, string> = {
  active: 'Active',
  suspended: 'Suspended',
  offboarded: 'Offboarded',
}

export function RoleBadge({ role }: { role: WorkspaceRole }) {
  return <span className={`role-badge role-${role}`}>{roleCopy[role].label}</span>
}

export function UsersPage({ workspace, activity, apiStatus }: { workspace: WorkspaceSummary; activity: ActivityEvent[]; apiStatus: AdminDataStatus }) {
  const [inviteEmail, setInviteEmail] = useState('')
  const [inviteRole, setInviteRole] = useState<WorkspaceRole>('family')
  const canInvite = apiStatus.canMutate && inviteEmail.trim().length > 3 && Boolean(workspace.capabilities?.['members.invite'])

  return (
    <div className="page-stack">
      <PageHeader
        eyebrow="Your Sideport"
        title="People"
        description="Invite someone you trust. Members can use approved apps on their own iPhone; Apple signing and app sources stay with the Owner."
      />

      <p className="pipeline-note"><ShieldCheck size={14} /> Authentik proves sign-in. Sideport enforces Owner and Member access for every device, app, and action.</p>

      <Panel title="Roles">
        <div className="role-legend">
          {(workspace.roles?.length ? workspace.roles : (Object.keys(roleCopy) as WorkspaceRole[]).map((role) => ({ id: role, label: roleCopy[role].label, capabilities: [roleCopy[role].permissions] }))).map((role) => (
            <div className="role-legend-row" key={role.id}>
              <span className={`role-badge role-${role.id}`}>{role.label}</span>
              <span>{role.capabilities.join(', ')}</span>
            </div>
          ))}
        </div>
      </Panel>

      <Panel title="Current capabilities">
        <div className="registration-meta">
          {Object.entries(workspace.capabilities ?? {}).map(([capability, enabled]) => <span key={capability}>{enabled ? 'yes' : 'no'} {capability}</span>)}
        </div>
      </Panel>

      <Panel title={`Members (${workspace.members.length})`}>
        {workspace.members.length ? (
          <div className="members-table">
            <div className="members-head">
              <span>Member</span><span>Role</span><span>Status</span><span>Last active</span><span aria-hidden="true" />
            </div>
            {workspace.members.map((member) => (
              <div className="members-row" key={member.id}>
                <div className="member-id">
                  <span className="member-avatar">{member.name.slice(0, 1)}</span>
                  <div><strong>{member.name}</strong><span>{member.email}</span></div>
                </div>
                <RoleBadge role={member.role} />
                <span className={`status-label ${member.status === 'active' ? 'complete' : 'blocked'}`}>{memberStatusCopy[member.status]}</span>
                <span className="muted">{member.lastActiveAt ? relativeTime(member.lastActiveAt) : '—'}</span>
                <button className="row-action member-action" disabled type="button">Manage</button>
              </div>
            ))}
          </div>
        ) : <EmptyState icon={Users} title="No workspace members yet" detail="When Sideport owns identity, invited members and their roles will appear here." />}
      </Panel>

      <div className="two-column-layout">
        <Panel title="Invite a member">
          <div className="invite-form">
            <label className="form-field">
              <span>Email</span>
              <input autoComplete="off" onChange={(event) => setInviteEmail(event.currentTarget.value)} placeholder="name@example.com" value={inviteEmail} />
            </label>
            <label className="form-field">
              <span>Role</span>
              <select onChange={(event) => setInviteRole(event.currentTarget.value as WorkspaceRole)} value={inviteRole}>
                {(Object.keys(roleCopy) as WorkspaceRole[]).map((role) => <option key={role} value={role}>{roleCopy[role].label}</option>)}
              </select>
            </label>
            <button className="primary-action" disabled={!canInvite} type="button"><UserPlus size={16} /> Send invite</button>
            {!workspace.capabilities?.['members.invite'] && <p className="mutation-message">Only the Sideport Owner can create an invitation.</p>}
          </div>
        </Panel>
        <Panel title="Audit trail">
          {activity.length ? <ActivityTimeline events={activity} /> : <EmptyState icon={History} title="No audit events yet" detail="Sign-in, role change, and operator actions will be recorded here." />}
        </Panel>
      </div>
    </div>
  )
}

function App() {
  const [apiTokenRevision, setApiTokenRevision] = useState(0)
  const { data } = useSideportAdminData(apiTokenRevision)
  return <SideportAdminApp data={data?.data} apiStatus={data?.status} onApiTokenSaved={() => setApiTokenRevision((revision) => revision + 1)} />
}

export default App
