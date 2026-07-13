import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import {
  AlertTriangle,
  Apple,
  CheckCircle2,
  ChevronRight,
  Package,
  RefreshCw,
  Server,
  Settings2,
  Smartphone,
  Wifi,
  XCircle,
  type LucideIcon,
} from 'lucide-react'
import type { AdminDataStatus, OperationPreflightDto, OperationRecordDto } from '../api/sideportApi'
import type { CatalogAppSummary, DeviceSummary, OnboardingWorkflowStepId, OnboardingWorkflowStepState, OnboardingWorkflowV2, SideportReadModel } from '../data/sideportTypes'
import './FirstRunOnboardingPrototype.css'

const LIVE_STAGES = [
  { id: 'server', label: 'Check Sideport', kicker: 'Deployment', icon: Server },
  { id: 'apple-signer', label: 'Connect Apple', kicker: 'Account and team', icon: Apple },
  { id: 'device', label: 'Connect iPhone', kicker: 'USB once', icon: Smartphone },
  { id: 'app', label: 'Choose app', kicker: 'Sideport library', icon: Package },
  { id: 'install', label: 'Install', kicker: 'One action', icon: RefreshCw },
  { id: 'ready', label: 'Ready', kicker: 'Automatic refresh', icon: CheckCircle2 },
] as const satisfies ReadonlyArray<{ id: OnboardingWorkflowStepId; label: string; kicker: string; icon: LucideIcon }>

const SESSION_EVIDENCE_VERSION = 1
const SESSION_EVIDENCE_PREFIX = 'sideport.onboarding.resume.v1:'
const ACTIVE_OPERATION_STATUSES = new Set(['queued', 'waiting', 'running'])
const FINALIZATION_STAGE_IDS = new Set(['activate-registration', 'enable-scheduler', 'compute-next-evaluation', 'write-completion-receipt'])

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
    // Best-effort audio never changes the server-verified completion state.
  }
}

type LiveStageId = (typeof LIVE_STAGES)[number]['id']
type LiveStepState = OnboardingWorkflowStepState

interface RuntimeSessionEvidence {
  schemaVersion: typeof SESSION_EVIDENCE_VERSION
  selectedCatalogAppId?: string
}

export interface RuntimeFirstRunOnboardingProps {
  data: SideportReadModel
  apiStatus: AdminDataStatus
  onAddIPhone: () => void
  onAddApp: () => void
  canAddIPhone?: boolean
  canAddApp?: boolean
  canRunInstall?: boolean
  appleContent?: ReactNode
  selectedCatalogAppId?: string
  onSelectedCatalogAppChange?: (catalogAppId: string) => void
  onPrepareInstall: (catalogAppId: string) => void
  onInstallApp: (catalogAppId: string) => void
  onReconcileInstall: () => void
  onRetryFinalization: () => void
  installPreflight?: OperationPreflightDto | null
  installOperation?: OperationRecordDto | null
  appSelectionPending?: boolean
  appSelectionError?: string | null
  installRequestPending?: boolean
  finalizationPending?: boolean
  reconciliationPending?: boolean
  canCompleteOnboarding?: boolean
  installRequestError?: string | null
  installPollError?: string | null
  onOpenDevice: (device: DeviceSummary) => void
  onRefresh: () => void
}

function sessionEvidenceKey(baseUrl: string): string {
  const deploymentScope = (baseUrl || '/').split(/[?#]/, 1)[0]
  return `${SESSION_EVIDENCE_PREFIX}${encodeURIComponent(deploymentScope)}`
}

function readSessionEvidence(key: string): RuntimeSessionEvidence | null {
  if (typeof window === 'undefined') return null
  try {
    const parsed = JSON.parse(window.sessionStorage.getItem(key) ?? 'null') as Partial<RuntimeSessionEvidence> | null
    if (!parsed || parsed.schemaVersion !== SESSION_EVIDENCE_VERSION) return null
    return {
      schemaVersion: SESSION_EVIDENCE_VERSION,
      selectedCatalogAppId: typeof parsed.selectedCatalogAppId === 'string' && parsed.selectedCatalogAppId.length <= 256 ? parsed.selectedCatalogAppId : undefined,
    }
  } catch {
    return null
  }
}

function writeSessionEvidence(key: string, evidence: RuntimeSessionEvidence): void {
  if (typeof window === 'undefined') return
  try {
    window.sessionStorage.setItem(key, JSON.stringify(evidence))
  } catch {
    // The server workflow remains authoritative when session storage is unavailable.
  }
}

function acceptedDevices(data: SideportReadModel): DeviceSummary[] {
  return data.devices.filter((device) => device.inventoryState === 'accepted')
}

function preferredAcceptedDevice(data: SideportReadModel): DeviceSummary | undefined {
  const accepted = acceptedDevices(data)
  return accepted.find((device) => device.supportedForFirstInstall)
    ?? accepted.find((device) => device.connection === 'usb' && device.usableForInstall !== false)
    ?? accepted.find((device) => device.usableForInstall)
    ?? accepted[0]
}

function deviceForApp(data: SideportReadModel, app: CatalogAppSummary | undefined, preferredDeviceUdid?: string | null): DeviceSummary | undefined {
  if (preferredDeviceUdid) {
    const receiptDevice = data.devices.find((candidate) => candidate.udid === preferredDeviceUdid && candidate.inventoryState === 'accepted')
    if (receiptDevice) return receiptDevice
  }
  if (app) {
    const registration = data.apps.find((candidate) => candidate.bundleId === app.expectedBundleId)
    const registeredDevice = registration && data.devices.find((candidate) => candidate.udid === registration.deviceUdid && candidate.inventoryState === 'accepted')
    if (registeredDevice) return registeredDevice
  }
  return preferredAcceptedDevice(data)
}

function stateLabel(state: LiveStepState): string {
  if (state === 'complete') return 'Complete'
  if (state === 'in-progress') return 'In progress'
  if (state === 'blocked') return 'Blocked'
  if (state === 'action-required') return 'Action required'
  return 'Not started'
}

function StepStatus({ state }: { state: LiveStepState }) {
  return <span className={`spo-workflow-status ${state}`}>{stateLabel(state)}</span>
}

function AppChoice({ app, checked, disabled = false, onSelect }: { app: CatalogAppSummary; checked: boolean; disabled?: boolean; onSelect: () => void }) {
  const source = app.artifactSources?.[0]
  return (
    <label className={`spo-live-app ${checked ? 'selected' : ''}`}>
      <input checked={checked} className="spo-visually-hidden" disabled={disabled} name="runtime-catalog-app" onChange={onSelect} type="radio" value={app.id} />
      <span className={`app-icon tone-${app.iconTone}`}>{app.icon ? <img alt="" src={app.icon} /> : <Package aria-hidden="true" size={19} />}</span>
      <span className="spo-live-app-copy"><strong>{app.name}</strong><small>{app.purpose}</small><em>{app.versionLabel}{source?.label ? ` · ${source.label}` : ''}</em></span>
      {checked && <CheckCircle2 aria-hidden="true" size={20} />}
    </label>
  )
}

function resumeStage(workflow: OnboardingWorkflowV2 | null | undefined, setupComplete: boolean): LiveStageId {
  if (setupComplete) return 'ready'
  return LIVE_STAGES.find((stage) => workflow?.steps.find((step) => step.id === stage.id)?.state !== 'complete')?.id ?? 'server'
}

function operationMessage(operation: OperationRecordDto | null | undefined): string | null {
  return operation?.error?.message ?? operation?.error?.detail ?? null
}

function operationNeedsFinalization(operation: OperationRecordDto | null | undefined): boolean {
  if (!operation) return false
  const verified = operation.stages?.some((stage) => stage.id === 'verify' && stage.status === 'succeeded')
  const finalizationPending = operation.stages?.some((stage) => stage.id && FINALIZATION_STAGE_IDS.has(stage.id) && (stage.status === 'running' || stage.status === 'waiting'))
  return Boolean(verified && operation.retryable === true && (finalizationPending || operation.status === 'waiting' || operation.status === 'recovery-required'))
}

function schedulerCadenceLabel(value: string | undefined): string {
  if (!value) return 'Automatic when due'
  const match = /^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})$/.exec(value)
  if (!match) return value
  const days = Number(match[1] ?? 0)
  const hours = Number(match[2]) + (days * 24)
  const minutes = Number(match[3])
  if (hours === 1 && minutes === 0) return 'Checks hourly when due'
  if (hours > 0 && minutes === 0) return `Checks every ${hours} hours when due`
  if (hours === 0 && minutes > 0) return `Checks every ${minutes} minutes when due`
  return `Checks every ${value} when due`
}

export function RuntimeFirstRunOnboarding({
  data,
  apiStatus,
  onAddIPhone,
  onAddApp,
  canAddIPhone = false,
  canAddApp = false,
  canRunInstall = false,
  appleContent,
  selectedCatalogAppId,
  onSelectedCatalogAppChange,
  onPrepareInstall,
  onInstallApp,
  onReconcileInstall,
  onRetryFinalization,
  installPreflight,
  installOperation,
  appSelectionPending = false,
  appSelectionError,
  installRequestPending = false,
  finalizationPending = false,
  reconciliationPending = false,
  canCompleteOnboarding = false,
  installRequestError,
  installPollError,
  onOpenDevice,
  onRefresh,
}: RuntimeFirstRunOnboardingProps) {
  const readyApps = useMemo(() => data.catalogApps.filter((app) => app.status === 'ready'), [data.catalogApps])
  const onboardingEvidence = apiStatus.onboarding
  const workflow = onboardingEvidence?.workflow ?? null
  const completionReceipt = onboardingEvidence?.completionReceipt ?? null
  const setupComplete = workflow?.setupState === 'complete'
    && Boolean(completionReceipt)
    && workflow.verifiedOperationId === completionReceipt?.verifiedOperationId
  const evidenceKey = useMemo(() => sessionEvidenceKey(apiStatus.baseUrl), [apiStatus.baseUrl])
  const initialSessionEvidence = useMemo(() => readSessionEvidence(evidenceKey), [evidenceKey])
  const receiptBundleId = completionReceipt?.registrationKey.bundleId
  const receiptSelectedAppId = receiptBundleId ? readyApps.find((app) => app.expectedBundleId === receiptBundleId)?.id : undefined
  const serverSelectedAppId = onboardingEvidence?.selectedCatalogAppId ?? receiptSelectedAppId
  const [activeStage, setActiveStage] = useState<LiveStageId>(() => resumeStage(workflow, setupComplete))
  const [localSelectedAppId, setLocalSelectedAppId] = useState(() => {
    const candidates = [selectedCatalogAppId, serverSelectedAppId, initialSessionEvidence?.selectedCatalogAppId]
    return candidates.find((id) => id && readyApps.some((app) => app.id === id)) ?? ''
  })
  const [showTechnicalDetails, setShowTechnicalDetails] = useState(false)
  const headingRef = useRef<HTMLHeadingElement>(null)
  const errorSummaryRef = useRef<HTMLParagraphElement>(null)
  const previousStageRef = useRef<LiveStageId>(activeStage)
  const previousWorkflowRef = useRef<string | null>(workflow ? JSON.stringify(workflow.steps.map((step) => [step.id, step.state, step.activeOperationId])) : null)
  const autoPreparedTargetRef = useRef('')
  const chimedReceiptRef = useRef<string | null>(null)

  useEffect(() => {
    if (!setupComplete || !completionReceipt?.verifiedOperationId) return
    if (chimedReceiptRef.current === completionReceipt.verifiedOperationId) return
    chimedReceiptRef.current = completionReceipt.verifiedOperationId
    attemptCompletionChime()
  }, [completionReceipt?.verifiedOperationId, setupComplete])

  const controlledSelectedAppId = selectedCatalogAppId && readyApps.some((app) => app.id === selectedCatalogAppId) ? selectedCatalogAppId : ''
  const validLocalSelectedAppId = readyApps.some((app) => app.id === localSelectedAppId) ? localSelectedAppId : ''
  const restoredSelectedAppId = initialSessionEvidence?.selectedCatalogAppId && readyApps.some((app) => app.id === initialSessionEvidence.selectedCatalogAppId) ? initialSessionEvidence.selectedCatalogAppId : ''
  const validServerSelectedAppId = serverSelectedAppId && readyApps.some((app) => app.id === serverSelectedAppId) ? serverSelectedAppId : ''
  const attemptedSelectedAppId = appSelectionPending || appSelectionError ? validLocalSelectedAppId || controlledSelectedAppId : ''
  const selectedAppId = apiStatus.mode === 'demo'
    ? controlledSelectedAppId || validLocalSelectedAppId || restoredSelectedAppId || validServerSelectedAppId
    : attemptedSelectedAppId || validServerSelectedAppId || controlledSelectedAppId || validLocalSelectedAppId || restoredSelectedAppId

  const selectedApp = readyApps.find((app) => app.id === selectedAppId)
  const receiptDeviceUdid = completionReceipt?.deviceUdid ?? completionReceipt?.registrationKey.deviceUdid
  const device = deviceForApp(data, selectedApp, receiptDeviceUdid)
  const connectedToUsb = Boolean(device && device.connection === 'usb' && device.usableForInstall !== false)
  const selectedTeam = data.personalApple.teams.find((team) => team.teamId === data.personalApple.selectedTeamId)
  const workflowInstallStep = workflow?.steps.find((step) => step.id === 'install')
  const workflowInstallOperationId = workflowInstallStep?.activeOperationId ?? null
  const workflowNeedsFinalization = workflowInstallStep?.nextAction?.action === 'retry-finalization'
  const finalizationRecovery = workflowNeedsFinalization || operationNeedsFinalization(installOperation)
  const operationActive = !finalizationRecovery && (Boolean(installOperation?.status && ACTIVE_OPERATION_STATUSES.has(installOperation.status)) || workflowInstallStep?.state === 'in-progress')
  const operationResumePending = Boolean(workflowInstallOperationId && installOperation?.operationId !== workflowInstallOperationId)
  const unknownInstall = installOperation?.status === 'unknown' || workflowInstallStep?.nextAction?.action === 'reconcile-install'
  const terminalFinalizationBlock = Boolean(
    installOperation?.stages?.some((stage) => stage.id === 'verify' && stage.status === 'succeeded')
    && installOperation.status === 'blocked'
    && installOperation.retryable !== true,
  )
  const preflightReady = Boolean(installPreflight?.ready && installPreflight.preflightId && installPreflight.planVersion)
  const readyNow = workflow?.readyNow === true

  const stepState = (id: LiveStageId): LiveStepState => {
    if (!workflow) return id === 'server' ? 'blocked' : 'not-started'
    return workflow.steps.find((step) => step.id === id)?.state ?? 'not-started'
  }
  const serverSelectionSaved = apiStatus.mode !== 'demo'
    && stepState('app') === 'complete'
    && Boolean(validServerSelectedAppId && selectedAppId === validServerSelectedAppId)
  const appSelectionSaved = apiStatus.mode === 'demo' ? Boolean(selectedApp) : serverSelectionSaved

  const workflowKey = workflow ? JSON.stringify(workflow.steps.map((step) => [step.id, step.state, step.activeOperationId])) : null
  useEffect(() => {
    if (!workflowKey || workflowKey === previousWorkflowRef.current) return
    const nextStage = resumeStage(workflow, setupComplete)
    const activeIndex = LIVE_STAGES.findIndex((stage) => stage.id === activeStage)
    const nextIndex = LIVE_STAGES.findIndex((stage) => stage.id === nextStage)
    const activeWorkflowState = workflow?.steps.find((step) => step.id === activeStage)?.state ?? (activeStage === 'server' ? 'blocked' : 'not-started')
    if (setupComplete || (activeWorkflowState === 'complete' && nextIndex > activeIndex) || previousWorkflowRef.current === null) setActiveStage(nextStage)
    previousWorkflowRef.current = workflowKey
  }, [activeStage, setupComplete, workflow, workflowKey])

  useEffect(() => {
    const next: RuntimeSessionEvidence = { schemaVersion: SESSION_EVIDENCE_VERSION, selectedCatalogAppId: selectedAppId || undefined }
    if (next.selectedCatalogAppId) writeSessionEvidence(evidenceKey, next)
  }, [evidenceKey, selectedAppId])

  useEffect(() => {
    if (previousStageRef.current !== activeStage) headingRef.current?.focus()
    previousStageRef.current = activeStage
  }, [activeStage])

  useEffect(() => {
    if (activeStage === 'install' && installRequestError) errorSummaryRef.current?.focus()
  }, [activeStage, installRequestError])

  const installTargetKey = selectedApp && device ? `${device.udid}:${selectedApp.expectedBundleId}` : ''
  useEffect(() => {
    if (activeStage !== 'install' || !installTargetKey || !connectedToUsb || !canRunInstall || workflowInstallOperationId || operationActive || installOperation || installPreflight || installRequestPending || installRequestError) return
    if (autoPreparedTargetRef.current === installTargetKey) return
    autoPreparedTargetRef.current = installTargetKey
    onPrepareInstall(selectedApp!.id)
  }, [activeStage, canRunInstall, connectedToUsb, installOperation, installPreflight, installRequestError, installRequestPending, installTargetKey, onPrepareInstall, operationActive, selectedApp, workflowInstallOperationId])

  const completedCount = LIVE_STAGES.filter((stage) => stepState(stage.id) === 'complete').length
  const activeIndex = LIVE_STAGES.findIndex((stage) => stage.id === activeStage)
  const activeDefinition = LIVE_STAGES[activeIndex]
  const firstIncompleteIndex = Math.max(0, LIVE_STAGES.findIndex((stage) => stepState(stage.id) !== 'complete'))
  const reachableIndex = setupComplete ? LIVE_STAGES.length - 1 : firstIncompleteIndex
  const moveTo = (stage: LiveStageId) => setActiveStage(stage)
  const moveNext = () => moveTo(LIVE_STAGES[Math.min(activeIndex + 1, LIVE_STAGES.length - 1)].id)

  let body: ReactNode
  let primaryLabel: string
  let primaryDisabled = false
  let primaryAction: () => void

  if (activeStage === 'server') {
    const ready = stepState('server') === 'complete'
    const serverStep = workflow?.steps.find((step) => step.id === 'server')
    primaryLabel = ready ? 'Connect Apple account' : 'Check again'
    primaryAction = ready ? moveNext : onRefresh
    body = (
      <div className="spo-live-stage">
        <div className="spo-stage-intro"><span className="spo-stage-icon"><Server size={21} /></span><div><h3>{ready ? 'Sideport is ready' : 'Let’s check Sideport'}</h3><p>Sideport checks saved data, Apple services, app signing, and the iPhone connection before anything changes.</p></div></div>
        <div className={`spo-live-callout ${ready ? 'success' : 'warning'}`}><div>{ready ? <CheckCircle2 size={20} /> : <AlertTriangle size={20} />}</div><div><strong>{ready ? 'The essentials are available' : workflow ? 'Sideport needs attention' : 'Setup status is unavailable'}</strong><span>{ready ? 'This deployment can continue to Apple setup.' : serverStep?.reason ?? apiStatus.message}</span></div></div>
        {serverStep?.evidence.map((evidence) => <div className="spo-live-callout spo-technical-only" key={evidence.id}><CheckCircle2 size={18} /><div><strong>{evidence.label}</strong><span>{evidence.detail}</span></div></div>)}
        <p className="spo-friendly-note"><Wifi size={17} /> Use USB for pairing and the first install. A saved pairing may support later refreshes on the same Wi-Fi, but USB is the reliable choice if a wireless refresh stalls or is unclear.</p>
        <div className="spo-technical-only spo-system-grid" data-testid="runtime-technical-system-checks">
          {data.system.checks.length ? data.system.checks.map((check) => <span key={check.id}><strong>{check.id.replaceAll('-', ' ')}</strong><small>{check.status === 'pass' ? check.reason : check.nextAction ?? check.reason}</small></span>) : <span><strong>Operational status</strong><small>Unavailable</small></span>}
        </div>
        <details className="spo-inline-details spo-technical-only"><summary>Container setup requirements</summary><p>Keep Sideport’s state and work storage writable and persistent, connect the provisioned anisette service, and expose the host usbmux socket. This page verifies those dependencies; it does not change Docker or host settings.</p></details>
      </div>
    )
  } else if (activeStage === 'apple-signer') {
    const connectedApple = stepState('apple-signer') === 'complete'
    const appleStep = workflow?.steps.find((step) => step.id === 'apple-signer')
    primaryLabel = connectedApple ? 'Continue to iPhone' : 'Finish Apple setup above'
    primaryDisabled = !connectedApple
    primaryAction = moveNext
    body = (
      <div className="spo-live-stage">
        <div className="spo-stage-intro"><span className="spo-stage-icon"><Apple size={21} /></span><div><h3>{connectedApple ? 'Apple account connected' : 'Connect your Apple account'}</h3><p>Sideport uses this account to prepare apps for your iPhone and keep them refreshed.</p></div></div>
        {connectedApple ? <div className="spo-live-callout success"><CheckCircle2 size={20} /><div><strong>{data.personalApple.appleIdHint || 'Apple account'}</strong><span>{selectedTeam ? `${selectedTeam.name} was returned by Apple and selected for signing.` : appleStep?.reason ?? 'The signing team and certificate are ready.'}</span></div></div> : <div className="spo-live-callout warning"><AlertTriangle size={20} /><div><strong>{data.personalApple.state === 'two-factor-required' ? 'Apple needs a verification code' : 'Finish Apple setup'}</strong><span>{appleStep?.reason ?? data.personalApple.message}</span></div></div>}
        {!connectedApple && appleContent}
        <p className="spo-friendly-note"><CheckCircle2 size={17} /> Most free Apple accounts return one Personal Team. Sideport asks only when Apple returns more than one; there is no manual Team ID box.</p>
        <details className="spo-inline-details"><summary>About certificates</summary><p>Sideport checks certificate availability before installation and never revokes one automatically. If replacement is required, Install shows the blocker and stops.</p></details>
      </div>
    )
  } else if (activeStage === 'device') {
    const deviceComplete = stepState('device') === 'complete'
    const deviceStep = workflow?.steps.find((step) => step.id === 'device')
    primaryLabel = deviceComplete ? 'Choose an app' : 'Connect iPhone'
    primaryDisabled = !deviceComplete && !canAddIPhone
    primaryAction = deviceComplete ? moveNext : onAddIPhone
    body = (
      <div className="spo-live-stage">
        <div className="spo-stage-intro"><span className="spo-stage-icon"><Smartphone size={21} /></span><div><h3>{deviceComplete && device ? `${device.name} is added` : 'Connect and trust your iPhone'}</h3><p>Connect it by USB to the Mac or PC running Sideport, unlock it, then tap Trust This Computer. Sideport pairs and adds it automatically.</p></div></div>
        {deviceComplete && device ? <button className="spo-live-device" onClick={() => onOpenDevice(device)} type="button"><span className="add-menu-icon"><Smartphone size={19} /></span><span><strong>{device.name}</strong><small>{device.productType} · iOS {device.osVersion} · {device.connection === 'wifi' ? 'Wi-Fi' : 'USB'}</small></span><ChevronRight size={17} /></button> : <div className="spo-live-callout warning"><AlertTriangle size={20} /><div><strong>{deviceStep?.state === 'in-progress' ? 'Sideport is waiting for the iPhone' : 'No accepted iPhone yet'}</strong><span>{deviceStep?.reason ?? 'Start one five-minute connection session. There is no separate Pair, “I tapped Trust,” or Add button.'}</span></div></div>}
        {deviceComplete && device && !connectedToUsb && <div className="spo-live-callout warning"><AlertTriangle size={20} /><div><strong>Reconnect USB before Install</strong><span>The iPhone is already accepted, but Sideport requires a direct USB connection for the first install.</span></div></div>}
        <div className="spo-iphone-guide"><strong>Before the first app</strong><ol><li>Open Settings → Privacy &amp; Security → Developer Mode.</li><li>Turn it on and restart the iPhone.</li><li>Unlock, tap Enable, enter the passcode, and reconnect USB if needed.</li></ol></div>
      </div>
    )
  } else if (activeStage === 'app') {
    const installedOnDevice = device ? data.installedApps.filter((app) => app.deviceUdid === device.udid) : []
    primaryLabel = appSelectionPending ? 'Saving app…' : appSelectionError && selectedApp ? 'Try saving app again' : appSelectionSaved ? 'Continue to install' : selectedApp ? 'Save app choice' : readyApps.length ? 'Choose an app' : 'Add an app'
    primaryDisabled = appSelectionPending || (readyApps.length > 0 ? !selectedApp : !canAddApp)
    primaryAction = selectedApp && !appSelectionSaved ? () => onSelectedCatalogAppChange?.(selectedApp.id) : selectedApp ? moveNext : onAddApp
    body = (
      <div className="spo-live-stage">
        <div className="spo-stage-intro"><span className="spo-stage-icon"><Package size={21} /></span><div><h3>Choose an app</h3><p>Apps already inspected by Sideport come first. You can also upload an IPA or add one from configured storage or a GitHub release.</p></div></div>
        {readyApps.length ? <div className="spo-live-apps" role="radiogroup" aria-label="Apps ready to install">{readyApps.map((app) => <AppChoice app={app} checked={selectedAppId === app.id} disabled={appSelectionPending} key={app.id} onSelect={() => { setLocalSelectedAppId(app.id); onSelectedCatalogAppChange?.(app.id) }} />)}</div> : <div className="spo-live-callout warning"><AlertTriangle size={20} /><div><strong>No apps in Sideport yet</strong><span>Add an IPA from this computer, a configured location, or a GitHub release.</span></div></div>}
        {installedOnDevice.length > 0 && <section className="spo-installed-apps" aria-labelledby="runtime-installed-apps-title"><div className="spo-library-heading"><div><strong id="runtime-installed-apps-title">Already on this iPhone</strong><span>Read-only app metadata from the connected iPhone.</span></div></div><div className="spo-installed-app-list">{installedOnDevice.map((installed) => { const match = readyApps.find((app) => app.expectedBundleId === installed.bundleId); return <div className="spo-installed-app-row" key={installed.bundleId}><span aria-hidden="true" className="spo-installed-app-icon">{installed.name.slice(0, 2).toUpperCase()}</span><span><strong>{installed.name}</strong><small>Version {installed.version}</small></span>{match ? <button className="spo-installed-match ready" disabled={appSelectionPending} onClick={() => { setLocalSelectedAppId(match.id); onSelectedCatalogAppChange?.(match.id) }} type="button">Choose above</button> : <span className="spo-installed-match">IPA file needed</span>}</div> })}</div><small>Sideport can see these apps, but it cannot copy or sign an app directly from your iPhone. Choose a matching app above or add its IPA file.</small></section>}
        {appSelectionError && <p className="mutation-message error" role="alert">{appSelectionError}</p>}
        <button className="spo-button spo-button-ghost spo-add-another" disabled={!canAddApp || appSelectionPending} onClick={onAddApp} type="button">Add another app <ChevronRight size={16} /></button>
      </div>
    )
  } else if (activeStage === 'install') {
    const canPrepare = Boolean(selectedApp && device && connectedToUsb && canRunInstall)
    if (finalizationRecovery) {
      primaryLabel = finalizationPending ? 'Finishing setup…' : 'Retry finishing setup'
      primaryDisabled = finalizationPending || !canCompleteOnboarding
      primaryAction = onRetryFinalization
    } else if (operationActive) {
      primaryLabel = 'Installing…'
      primaryDisabled = true
      primaryAction = onRefresh
    } else if (unknownInstall) {
      primaryLabel = reconciliationPending ? 'Checking iPhone status…' : 'Check iPhone status'
      primaryDisabled = reconciliationPending || !canRunInstall
      primaryAction = onReconcileInstall
    } else if (operationResumePending) {
      primaryLabel = 'Loading install status…'
      primaryDisabled = true
      primaryAction = onRefresh
    } else if (installOperation && installOperation.rerunnable) {
      primaryLabel = installRequestPending ? 'Checking again…' : 'Review and try again'
      primaryDisabled = installRequestPending || !canPrepare
      primaryAction = () => selectedApp && onPrepareInstall(selectedApp.id)
    } else if (!installPreflight || !preflightReady) {
      primaryLabel = installRequestPending ? 'Checking install…' : 'Check again'
      primaryDisabled = installRequestPending || !canPrepare
      primaryAction = () => selectedApp && onPrepareInstall(selectedApp.id)
    } else {
      primaryLabel = installRequestPending ? 'Starting install…' : 'Install and finish'
      primaryDisabled = installRequestPending || !installPreflight.ready || !canPrepare
      primaryAction = () => selectedApp && onInstallApp(selectedApp.id)
    }
    body = (
      <div className="spo-live-stage">
        <div className="spo-stage-intro"><span className="spo-stage-icon"><RefreshCw size={21} /></span><div><h3>{finalizationRecovery ? 'Finish setup safely' : operationActive && installOperation?.type === 'reconcile' ? 'Checking the iPhone' : operationActive ? 'Installing your app' : 'Review the install'}</h3><p>Keep USB connected. Sideport checks the plan, signs and installs the app, verifies it on the iPhone, then enables automatic refresh.</p></div></div>
        <div className="spo-install-summary"><span><small>iPhone</small><strong>{device?.name ?? 'Connect an iPhone first'}</strong></span><span><small>App</small><strong>{selectedApp?.name ?? 'Choose an app first'}</strong></span><span><small>Apple team</small><strong>{selectedTeam?.name ?? 'Connect Apple first'}</strong></span><span><small>First install</small><strong>{connectedToUsb ? 'USB connected' : 'USB required'}</strong></span></div>
        {!canPrepare && !installOperation && <div className="spo-live-callout warning"><AlertTriangle size={20} /><div><strong>Finish the earlier requirement</strong><span>Sideport needs the server workflow, an accepted iPhone connected by USB, a ready app, and server permission to install.</span></div></div>}
        {installPreflight && <section className="spo-runtime-preflight" aria-label="Install checks"><div className={`spo-live-callout ${installPreflight.ready ? 'success' : 'warning'}`}>{installPreflight.ready ? <CheckCircle2 size={20} /> : <AlertTriangle size={20} />}<div><strong>{installPreflight.ready ? 'Ready to install' : 'Install is blocked'}</strong><span>{installPreflight.ready ? 'The server rechecked the Apple signer, iPhone, IPA, limits, and automatic refresh plan.' : 'Fix the items below, then check again.'}</span></div></div>{installPreflight.blockers.map((item) => <div className="spo-preflight-item blocked" key={item.code}><XCircle size={17} /><span><strong>{item.message}</strong>{item.detail && <small>{item.detail}</small>}</span></div>)}{installPreflight.warnings.map((item) => <div className="spo-preflight-item warning" key={item.code}><AlertTriangle size={17} /><span><strong>{item.message}</strong>{item.detail && <small>{item.detail}</small>}</span></div>)}{installPreflight.scarceLimits.map((limit) => <div className="spo-preflight-item" key={limit.code}><Package size={17} /><span><strong>{limit.label}</strong><small>{limit.used} of {limit.limit} in use</small></span></div>)}{installPreflight.plannedMutations.length > 0 && <details className="spo-inline-details"><summary>What Sideport will do</summary><ul>{installPreflight.plannedMutations.map((mutation) => <li key={mutation}>{mutation}</li>)}</ul></details>}</section>}
        {installOperation?.stages?.length ? <ol className="spo-runtime-operation-stages" aria-label="Installation progress">{installOperation.stages.map((stage) => <li className={stage.status ?? 'pending'} key={stage.id ?? stage.label}><span>{stage.status === 'succeeded' ? <CheckCircle2 size={16} /> : stage.status === 'failed' || stage.status === 'blocked' ? <XCircle size={16} /> : <RefreshCw className={stage.status === 'running' ? 'stage-spin' : ''} size={16} />}</span><div><strong>{stage.label ?? stage.id?.replaceAll('-', ' ') ?? 'Install stage'}</strong><small>{stage.error?.message ?? stage.message ?? stateLabel(stage.status === 'succeeded' ? 'complete' : stage.status === 'running' ? 'in-progress' : stage.status === 'failed' || stage.status === 'blocked' ? 'blocked' : 'not-started')}</small></div></li>)}</ol> : null}
        {finalizationRecovery && <div className="spo-live-callout warning"><AlertTriangle size={20} /><div><strong>The app is already verified</strong><span>Retry resumes activation, automatic refresh, and the completion receipt. It does not sign or install the app again.{!canCompleteOnboarding ? ' This session cannot finish onboarding.' : ''}</span></div></div>}
        {terminalFinalizationBlock && <div className="spo-live-callout warning"><XCircle size={20} /><div><strong>Saved setup evidence no longer matches</strong><span>Sideport will not reuse stale app, Apple-team, or profile evidence. Review the current plan before installing again.</span></div></div>}
        {unknownInstall && <div className="spo-live-callout warning"><AlertTriangle size={20} /><div><strong>Check before trying again</strong><span>Sideport will only read the installed bundle, version, and profile over USB. It will not pair, sign, or install during this check.</span></div></div>}
        {installPollError && <p className="mutation-message error" role="status">The install is still being tracked. {installPollError}</p>}
        {(installRequestError || operationMessage(installOperation)) && <p className="mutation-message error" id="runtime-install-error-summary" ref={errorSummaryRef} role="alert" tabIndex={-1}>{installRequestError ?? operationMessage(installOperation)}</p>}
      </div>
    )
  } else {
    primaryLabel = device ? 'View iPhone' : 'Setup complete'
    primaryDisabled = !setupComplete || !device
    primaryAction = () => device && onOpenDevice(device)
    body = setupComplete ? (
      <div className="spo-live-stage spo-ready-stage"><span className="spo-ready-mark"><CheckCircle2 size={36} /></span><h3>Installed — you can unplug</h3><p>{readyNow ? `Sideport verified the app on the iPhone and saved the setup receipt. Automatic refresh is on. ${schedulerCadenceLabel(data.system.scheduler.policy?.evaluationInterval)}.` : 'The verified setup receipt is safe. Something currently needs attention; use Home or Activity to restore service.'}</p><div className="spo-install-summary"><span><small>iPhone</small><strong>{device?.name ?? 'Saved iPhone'}</strong></span><span><small>App</small><strong>{selectedApp?.name ?? receiptBundleId ?? 'Installed app'}</strong></span><span><small>Connection</small><strong>USB reliable · paired Wi-Fi optional</strong></span><span><small>Refresh</small><strong>{data.system.scheduler.enabled ? schedulerCadenceLabel(data.system.scheduler.policy?.evaluationInterval) : 'Needs attention'}</strong></span></div><p className="spo-friendly-note"><CheckCircle2 size={17} /> The completion chime was attempted when browser audio was available. The immutable receipt—not the sound—is proof that Sideport finished.</p><p className="spo-friendly-note"><Wifi size={17} /> A saved pairing may allow later refreshes on the same Wi-Fi. If a wireless refresh stalls or its result is unclear, reconnect USB and retry only after Sideport reports the operation has stopped.</p><p className="spo-friendly-note"><Smartphone size={17} /> If iOS asks, open Settings → General → VPN &amp; Device Management, trust the developer profile, then open the app once. Sideport verifies installation, not successful launch.</p></div>
    ) : <div className="spo-live-callout warning"><AlertTriangle size={20} /><div><strong>Setup is not complete</strong><span>Ready appears only after the server returns the immutable completion receipt.</span></div></div>
  }

  const currentState = stepState(activeStage)
  const announcementDetail = activeStage === 'app' && selectedApp
    ? `${selectedApp.name} selected.`
    : activeStage === 'install' && operationActive
      ? 'Installation is in progress.'
      : workflow?.steps.find((step) => step.id === activeStage)?.reason ?? (setupComplete ? 'Sideport recorded setup completion on the server.' : 'Waiting for current server evidence.')
  const announcement = `${activeDefinition.label}. ${stateLabel(currentState)}. ${announcementDetail}`

  return (
    <div className={`spo-prototype spo-runtime ${showTechnicalDetails ? 'technical-mode' : ''}`} data-stage={activeStage} data-testid="runtime-first-run-onboarding">
      <header className="spo-header"><div className="spo-brand"><div><strong>Sideport Setup</strong><span>Live setup · server status is authoritative</span></div></div><div className="spo-header-actions"><button aria-controls="runtime-technical-details" aria-expanded={showTechnicalDetails} aria-pressed={showTechnicalDetails} className="spo-button spo-button-ghost spo-technical-toggle" onClick={() => setShowTechnicalDetails((visible) => !visible)} type="button"><Settings2 size={16} />{showTechnicalDetails ? 'Hide technical details' : 'Show technical details'}</button></div></header>
      <main className="spo-shell">
        <nav className="spo-stepper" aria-label="First-run setup steps"><div className="spo-stepper-heading"><span>Your setup</span><strong>{completedCount} of {LIVE_STAGES.length} complete</strong></div><div className="spo-mobile-step" aria-label={`Step ${activeIndex + 1} of ${LIVE_STAGES.length}: ${activeDefinition.label}`}><div><span>Step {activeIndex + 1} of {LIVE_STAGES.length}</span><strong>{activeDefinition.label}</strong></div><progress aria-label="Setup progress" max={LIVE_STAGES.length} value={activeIndex + 1} /></div><ol>{LIVE_STAGES.map((definition, index) => { const state = stepState(definition.id); const Icon: LucideIcon = state === 'complete' ? CheckCircle2 : state === 'blocked' ? XCircle : state === 'action-required' ? AlertTriangle : state === 'in-progress' ? RefreshCw : definition.icon; const reachable = index <= reachableIndex; return <li key={definition.id}><span className="spo-visually-hidden" id={`runtime-step-state-${definition.id}`}>{stateLabel(state)}</span><button aria-current={definition.id === activeStage ? 'step' : undefined} aria-describedby={`runtime-step-state-${definition.id}`} className={`spo-step ${definition.id === activeStage ? 'active' : ''}`} disabled={!reachable} onClick={() => moveTo(definition.id)} type="button"><span className={`spo-step-icon ${state}`}><Icon aria-hidden="true" size={17} /></span><span className="spo-step-copy"><strong>{definition.label}</strong><small>{definition.kicker}</small></span><span className="spo-step-status"><StepStatus state={state} /></span></button></li> })}</ol></nav>
        <section aria-labelledby="runtime-onboarding-heading" className="spo-detail" data-testid={`runtime-onboarding-panel-${activeStage}`}><div className="spo-detail-heading"><div><span>Step {activeIndex + 1} of {LIVE_STAGES.length} · {setupComplete ? 'Setup complete' : 'Setup in progress'}</span><h2 id="runtime-onboarding-heading" ref={headingRef} tabIndex={-1}>{activeDefinition.label}</h2></div><StepStatus state={currentState} /></div><div className="spo-detail-body">{body}<div className="spo-technical-only" id="runtime-technical-details">{selectedAppId && <div className="spo-live-callout" data-testid="runtime-session-evidence"><Settings2 size={20} /><div><strong>{serverSelectionSaved ? 'Server-saved app choice' : 'Browser-session app choice · non-authoritative'}</strong><span>{serverSelectionSaved ? 'Sideport saved this pending registration and will revalidate it before installation.' : 'This tab remembers only the catalog choice. Save it to Sideport before installation.'}</span></div></div>}{completionReceipt && <div className="spo-live-callout" data-testid="runtime-completion-receipt"><CheckCircle2 size={20} /><div><strong>Immutable completion receipt</strong><span>Verified operation {completionReceipt.verifiedOperationId}</span></div></div>}</div></div><footer className="spo-footer"><button className="spo-button spo-button-ghost" disabled={activeIndex === 0} onClick={() => moveTo(LIVE_STAGES[activeIndex - 1].id)} type="button">Back</button><button aria-describedby={activeStage === 'install' && (installRequestError || operationMessage(installOperation)) ? 'runtime-install-error-summary' : undefined} className="spo-button spo-button-primary" disabled={primaryDisabled} onClick={primaryAction} type="button">{primaryLabel} <ChevronRight size={16} /></button></footer></section>
      </main>
      <p aria-atomic="true" aria-live="polite" className="spo-visually-hidden" data-testid="runtime-onboarding-live-region">{announcement}</p>
    </div>
  )
}
