import { useMemo, useState, type ReactNode } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Activity,
  AlertTriangle,
  Apple,
  Cable,
  CheckCircle2,
  ChevronRight,
  CircleDashed,
  Gauge,
  HardDrive,
  ListChecks,
  Network,
  Play,
  Plus,
  RefreshCw,
  Search,
  Settings,
  ShieldCheck,
  Smartphone,
  Stethoscope,
  TimerReset,
  Wifi,
  XCircle,
  type LucideIcon,
} from 'lucide-react'
import {
  flexRender,
  getCoreRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnDef,
  type SortingState,
} from '@tanstack/react-table'
import './App.css'
import { getStoredSideportApiToken, refreshSideportApp, registerSideportApp, saveSideportApiToken, useSideportAdminData, type AdminDataStatus, type AppRegistrationPayload } from './api/sideportApi'
import type { OnboardingStep, OnboardingStepState } from './api/sideportApi'
import {
  runtimeEmptyData,
  type ActivityEvent,
  type DeviceSummary,
  type DiagnosticIssue,
  type HealthState,
  type OperationLogEntry,
  type RegisteredAppSummary,
  type RenewalItem,
  type RenewalRisk,
  type RenewalStatus,
  type SideportReadModel,
  type SourceKind,
  type SystemStatus,
} from './data/sideportTypes'
import { compactUdid, relativeTime, shortDateTime, sourceLabel, timeUntil } from './lib/format'

export type RouteId = 'onboarding' | 'overview' | 'devices' | 'device-detail' | 'add-app' | 'renewals' | 'diagnostics' | 'settings'

const routeItems: Array<{ id: RouteId; label: string; icon: LucideIcon }> = [
  { id: 'onboarding', label: 'Onboarding', icon: ListChecks },
  { id: 'overview', label: 'Overview', icon: Gauge },
  { id: 'devices', label: 'Devices', icon: Smartphone },
  { id: 'add-app', label: 'Add App', icon: Plus },
  { id: 'renewals', label: 'Renewals', icon: TimerReset },
  { id: 'diagnostics', label: 'Diagnostics', icon: Stethoscope },
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
  onApiTokenSaved?: () => void
}

const runtimeStatus: AdminDataStatus = {
  mode: 'unavailable',
  baseUrl: '/sideport-api',
  message: 'Waiting for the .NET API.',
  canMutate: false,
}

export function SideportAdminApp({ data, apiStatus, initialRoute = 'onboarding', onApiTokenSaved }: SideportAdminAppProps) {
  const viewData = data ?? runtimeEmptyData
  const viewStatus = apiStatus ?? runtimeStatus
  const [route, setRoute] = useState<RouteId>(initialRoute)
  const selectedDevice = viewData.devices[0]

  return (
    <div className="admin-root">
      <aside className="sidebar" aria-label="Primary navigation">
        <div className="brand-lockup">
          <div className="brand-mark"><Apple size={18} /></div>
          <div>
            <div className="brand-name">Sideport</div>
            <div className="brand-meta">Admin console</div>
          </div>
        </div>

        <nav className="nav-list">
          {routeItems.map((item) => {
            const Icon = item.icon
            return (
              <button
                className={route === item.id ? 'nav-item active' : 'nav-item'}
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
        <TopBar system={viewData.system} apiStatus={viewStatus} />
        <main className="content-area">
          {route === 'onboarding' && <OnboardingPage data={viewData} apiStatus={viewStatus} onNavigate={setRoute} />}
          {route === 'overview' && <OverviewPage data={viewData} onNavigate={setRoute} />}
          {route === 'devices' && <DevicesPage data={viewData} onOpenDevice={() => setRoute('device-detail')} />}
          {route === 'device-detail' && <DeviceDetailPage data={viewData} device={selectedDevice} />}
          {route === 'add-app' && <AddAppPage data={viewData} apiStatus={viewStatus} />}
          {route === 'renewals' && <RenewalsPage data={viewData} apiStatus={viewStatus} />}
          {route === 'diagnostics' && <DiagnosticsPage data={viewData} />}
          {route === 'settings' && <SettingsPage system={viewData.system} apiStatus={viewStatus} onApiTokenSaved={onApiTokenSaved} />}
        </main>
      </div>
    </div>
  )
}

export function OnboardingPage({ data, apiStatus, onNavigate }: { data: SideportReadModel; apiStatus: AdminDataStatus; onNavigate?: (route: RouteId) => void }) {
  const onboarding = apiStatus.onboarding
  const steps = onboarding?.steps ?? []
  const requiredSteps = steps.filter((step) => step.surface === 'portal' && step.required)
  const iphoneSteps = steps.filter((step) => step.surface === 'iphone')
  const completeSteps = steps.filter((step) => step.state === 'complete').length
  const requiredComplete = onboarding?.firstRunComplete ?? false
  const primaryBlocked = !data.system.ready.ready
  const hasApiSnapshot = apiStatus.mode === 'live' || apiStatus.mode === 'partial' || apiStatus.mode === 'demo'

  return (
    <div className="page-stack">
      <PageHeader
        eyebrow="First run"
        title="Bring Sideport online in the right order"
        description="The portal starts here because signing and install actions are only safe after auth, anisette, signer, device, and app-registration prerequisites are visible."
      />

      <section className="onboarding-hero" aria-label="Onboarding progress">
        <div>
          <span className={`setup-ring ${requiredComplete ? 'complete' : primaryBlocked ? 'blocked' : 'pending'}`}>{completeSteps}/{Math.max(steps.length, 1)}</span>
        </div>
        <div>
          <h2>{requiredComplete ? 'Ready for controlled refreshes' : 'Setup still needs attention'}</h2>
          <p>{apiStatus.message}</p>
          <div className="setup-meta">
            <SourcePill source={apiStatus.mode === 'unavailable' ? 'planned' : apiStatus.mode === 'demo' ? 'demo' : 'live'} label={apiStatus.mode === 'unavailable' ? 'No API data' : apiStatus.mode === 'demo' ? 'Demo data' : 'Live backend'} />
            <span>{apiStatus.lastUpdatedAt ? `Updated ${shortDateTime(apiStatus.lastUpdatedAt)}` : 'Waiting for first API snapshot'}</span>
          </div>
        </div>
        <div className="hero-actions">
          <button className="primary-action" disabled={primaryBlocked} onClick={() => onNavigate?.('add-app')} type="button"><Plus size={16} /> Add first app</button>
          <button className="ghost-action" onClick={() => onNavigate?.('settings')} type="button">View checks<ChevronRight size={15} /></button>
        </div>
      </section>

      <div className="two-column-layout">
        <Panel title="Required setup">
          <OnboardingStepList steps={requiredSteps} />
        </Panel>
        <Panel title="On iPhone">
          <IPhoneSetupList steps={iphoneSteps} />
        </Panel>
      </div>

      <div className="two-column-layout">
        <Panel title="First app registration path">
          <div className="setup-path">
            <FactTile label="Device" value={hasApiSnapshot ? data.devices[0]?.name ?? 'No reachable device' : 'Waiting for API'} source={hasApiSnapshot && data.devices.length ? 'live' : 'planned'} />
            <FactTile label="App slots" value={hasApiSnapshot && data.devices[0] ? `${data.devices[0].appSlotsUsed}/3 used` : 'Unknown'} source={hasApiSnapshot ? 'derived' : 'planned'} />
            <FactTile label="Registered apps" value={hasApiSnapshot ? String(data.apps.length) : 'Unknown'} source={hasApiSnapshot && data.apps.length ? 'live' : 'planned'} />
          </div>
        </Panel>
        <Panel title="Backend posture">
          <SystemChecks system={data.system} />
        </Panel>
      </div>
    </div>
  )
}

function TopBar({ system, apiStatus }: { system: SystemStatus; apiStatus: AdminDataStatus }) {
  return (
    <header className="topbar">
      <div className="search-shell">
        <Search size={17} />
        <span>Search devices, bundle IDs, blockers</span>
      </div>
      <div className="topbar-actions">
        <span className={`api-mode ${apiStatus.mode}`}>{apiStatus.mode === 'live' ? 'Live API' : apiStatus.mode === 'partial' ? 'Partial API' : apiStatus.mode === 'demo' ? 'Demo data' : 'API unavailable'}</span>
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

export function OverviewPage({ data, onNavigate }: { data: SideportReadModel; onNavigate?: (route: RouteId) => void }) {
  const reachable = data.devices.filter((device) => device.connection !== 'offline').length
  const blocked = data.renewals.filter((item) => item.risk === 'blocked').length
  const due = data.renewals.filter((item) => item.risk === 'due-now').length
  const openIssues = data.issues.filter((issue) => issue.status !== 'resolved').length

  return (
    <div className="page-stack">
      <PageHeader
        eyebrow="Operations overview"
        title="Sideport health at a glance"
        description="Live API checks are shown as they arrive. If the backend has no devices or apps yet, those sections stay empty."
      />

      <section className="metric-grid" aria-label="Fleet health summary">
        <MetricCard icon={Smartphone} label="Reachable devices" value={String(reachable)} detail="Returned by /api/devices." source="live" tone="blue" />
        <MetricCard icon={TimerReset} label="Due soon" value={String(due)} detail="Inside configured renewal lead time." source="derived" tone="amber" />
        <MetricCard icon={AlertTriangle} label="Blocked" value={String(blocked)} detail="From app refresh errors and readiness blockers." source="derived" tone="red" />
        <MetricCard icon={Activity} label="Open issues" value={String(openIssues)} detail="From API failures and app lastError fields." source="derived" tone="green" />
      </section>

      <div className="two-column-layout">
        <Panel title="Renewal risk" actionLabel="View renewals" onAction={() => onNavigate?.('renewals')}>
          <RenewalQueueList items={data.renewals.slice(0, 4)} apps={data.apps} compact />
        </Panel>
        <Panel title="System checks" actionLabel="View settings" onAction={() => onNavigate?.('settings')}>
          <SystemChecks system={data.system} />
        </Panel>
      </div>

      <Panel title="Recent activity">
        <ActivityTimeline events={data.activity} />
      </Panel>
    </div>
  )
}

export function DevicesPage({ data, onOpenDevice }: { data: SideportReadModel; onOpenDevice?: (device: DeviceSummary) => void }) {
  if (data.devices.length === 0) {
    return (
      <div className="page-stack">
        <PageHeader eyebrow="Devices" title="No devices known yet" description="Connect a trusted iPhone over USB or Wi-Fi. The current API only reports devices it can reach right now." />
        <EmptyState icon={Cable} title="No devices returned by /api/devices" detail="The current API reports devices it can reach right now. Connect a trusted iPhone over USB or Wi-Fi to populate this view." />
      </div>
    )
  }

  return (
    <div className="page-stack">
      <PageHeader eyebrow="Devices" title="Device inventory" description="Desktop uses a scan-friendly table. Mobile switches to cards with one clear primary action per device." />
      <DeviceInventoryTable devices={data.devices} apps={data.apps} onOpenDevice={onOpenDevice} />
      <div className="device-card-list">
        {data.devices.map((device) => <DeviceCard key={device.udid} device={device} apps={appsForDevice(data.apps, device.udid)} onOpen={() => onOpenDevice?.(device)} />)}
      </div>
    </div>
  )
}

export function DeviceDetailPage({ data, device }: { data: SideportReadModel; device?: DeviceSummary }) {
  if (!device) {
    return <EmptyState icon={Smartphone} title="No selected device" detail="Device detail needs a reachable or known device read model." />
  }
  const apps = appsForDevice(data.apps, device.udid)
  const issues = data.issues.filter((issue) => issue.deviceUdid === device.udid)

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
          <button className="primary-action" disabled type="button"><RefreshCw size={16} /> Refresh disabled</button>
        </div>
      </div>

      <section className="section-grid three">
        <FactTile label="Connection" value={connectionLabel(device.connection)} source="live" />
        <FactTile label="Last seen" value={relativeTime(device.lastSeenAt.value)} source={device.lastSeenAt.source} />
        <FactTile label="Nearest expiry" value={timeUntil(device.nearestExpiryAt?.value)} source={device.nearestExpiryAt?.source ?? 'planned'} />
      </section>

      <Panel title="App slots">
        <AppSlotGrid apps={apps} />
      </Panel>

      <div className="two-column-layout">
        <Panel title="Signing and network">
          <dl className="detail-list">
            <div><dt>Apple Developer Team</dt><dd>{device.teamId}</dd></div>
            <div><dt>Single-flight signer</dt><dd>Visible in renewals. Parallel refresh is intentionally blocked.</dd></div>
            <div><dt>Wi-Fi pairing</dt><dd>{device.connection === 'wifi' ? 'Reachable through host netmuxd/usbmuxd.' : 'No Wi-Fi pairing data reported by the API yet.'}</dd></div>
          </dl>
        </Panel>
        <Panel title="Diagnostics for this device">
          {issues.length ? <DiagnosticIssueList issues={issues} compact /> : <EmptyState icon={CheckCircle2} title="No open device issues" detail="Trace-linked issue grouping will appear when OpenTelemetry-backed diagnostics are wired." />}
        </Panel>
      </div>
    </div>
  )
}

export function AddAppPage({ data, apiStatus }: { data: SideportReadModel; apiStatus: AdminDataStatus }) {
  const firstDevice = data.devices[0]
  const queryClient = useQueryClient()
  const [form, setForm] = useState<AppRegistrationPayload>({
    bundleId: '',
    deviceUdid: firstDevice?.udid ?? '',
    appleId: '',
    teamId: firstDevice?.teamId && firstDevice.teamId !== 'Unknown' ? firstDevice.teamId : '',
    inputIpaPath: '',
  })
  const defaultTeamId = firstDevice?.teamId && firstDevice.teamId !== 'Unknown' ? firstDevice.teamId : ''
  const registrationPayload: AppRegistrationPayload = {
    ...form,
    deviceUdid: form.deviceUdid || firstDevice?.udid || '',
    teamId: form.teamId || defaultTeamId,
  }
  const registerMutation = useMutation({
    mutationFn: () => registerSideportApp(registrationPayload),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['sideport-admin-data'] }),
  })
  const update = (key: keyof AppRegistrationPayload, value: string) => setForm((current) => ({ ...current, [key]: value }))
  const hasRequiredFields = Object.values(registrationPayload).every((value) => value.trim().length > 0)
  const canSubmit = apiStatus.canMutate && hasRequiredFields && !registerMutation.isPending

  return (
    <div className="page-stack">
      <PageHeader eyebrow="Add app" title="Preflight before Sideport touches Apple services" description="Registration writes to the live API only when mutations are enabled for this build. The operator must provide the server-side IPA path and Apple/team/device identifiers explicitly." />
      <div className="wizard-grid">
        <Panel title="Target">
          <div className="form-grid">
            <label className="form-field">
              <span>Device UDID</span>
              {data.devices.length ? (
                <select value={registrationPayload.deviceUdid} onChange={(event) => update('deviceUdid', event.currentTarget.value)}>
                  {data.devices.map((device) => <option key={device.udid} value={device.udid}>{device.name} · {compactUdid(device.udid)}</option>)}
                </select>
              ) : (
                <input autoComplete="off" onChange={(event) => update('deviceUdid', event.currentTarget.value)} placeholder="00008140..." value={form.deviceUdid} />
              )}
            </label>
            <label className="form-field">
              <span>Bundle ID</span>
              <input autoComplete="off" onChange={(event) => update('bundleId', event.currentTarget.value)} placeholder="ro.hont.example" value={form.bundleId} />
            </label>
            <label className="form-field">
              <span>Apple ID</span>
              <input autoComplete="username" onChange={(event) => update('appleId', event.currentTarget.value)} placeholder="name@example.com" value={form.appleId} />
            </label>
            <label className="form-field">
              <span>Team ID</span>
              <input autoComplete="off" onChange={(event) => update('teamId', event.currentTarget.value)} placeholder="TEAMID1234" value={registrationPayload.teamId} />
            </label>
            <label className="form-field wide-field">
              <span>Server IPA path</span>
              <input autoComplete="off" onChange={(event) => update('inputIpaPath', event.currentTarget.value)} placeholder="/var/lib/sideport/ipa/App.ipa" value={form.inputIpaPath} />
            </label>
          </div>
        </Panel>
        <Panel title="Preflight">
          <PreflightList items={[
            ['Device is currently reachable', Boolean(firstDevice), firstDevice ? 'live' : 'planned'],
            ['App slot available on selected device', firstDevice ? firstDevice.appSlotsUsed < 3 : false, 'derived'],
            ['Signer binary ready', data.system.ready.checks.signer.ok, 'live'],
            ['Anisette identity trusted', data.system.ready.checks.anisette.ok, 'live'],
            ['Mutating API enabled in this build', apiStatus.canMutate, apiStatus.canMutate ? 'live' : 'planned'],
          ]} />
          <button className="primary-action wide" disabled={!canSubmit} onClick={() => registerMutation.mutate()} type="button">
            <Play size={16} /> {registerMutation.isPending ? 'Registering...' : 'Register app'}
          </button>
          {!apiStatus.canMutate && <p className="mutation-message">Mutations are disabled for this build. Set VITE_SIDEPORT_ENABLE_MUTATIONS=true for the real portal bundle.</p>}
          {registerMutation.isSuccess && <p className="mutation-message success">Registration saved. The app list will refresh from the API snapshot.</p>}
          {registerMutation.error && <p className="mutation-message error">{registerMutation.error.message}</p>}
        </Panel>
      </div>
    </div>
  )
}

export function RenewalsPage({ data, apiStatus }: { data: SideportReadModel; apiStatus: AdminDataStatus }) {
  return (
    <div className="page-stack">
      <PageHeader eyebrow="Renewals" title="Renewal risk" description="Current backend data comes from registered apps, expiry fields, refresh status, and last error details." />
      <RenewalLane title="Blocked" items={data.renewals.filter((item) => item.risk === 'blocked')} apps={data.apps} apiStatus={apiStatus} />
      <RenewalLane title="Due now" items={data.renewals.filter((item) => item.risk === 'due-now')} apps={data.apps} apiStatus={apiStatus} />
      <RenewalLane title="Upcoming" items={data.renewals.filter((item) => item.risk === 'upcoming')} apps={data.apps} apiStatus={apiStatus} />
    </div>
  )
}

export function DiagnosticsPage({ data }: { data: SideportReadModel }) {
  return (
    <div className="page-stack">
      <PageHeader eyebrow="Diagnostics" title="Runtime failure evidence" description="Live evidence comes from readiness checks, API fetch failures, app lastError fields, and the protected API log stream." />
      {data.issues.length ? <DiagnosticIssueList issues={data.issues} /> : <EmptyState icon={Stethoscope} title="No diagnostic issues" detail="When OpenTelemetry is wired, this page will group refresh/sign/install failures by operation and trace ID." />}
      <Panel title="Live API logs">
        {data.logs.length ? <OperationLogList logs={data.logs} /> : <EmptyState icon={Activity} title="No API logs yet" detail="The runtime log endpoint has not returned entries for this snapshot." />}
      </Panel>
    </div>
  )
}

export function SettingsPage({ system, apiStatus, onApiTokenSaved }: { system: SystemStatus; apiStatus: AdminDataStatus; onApiTokenSaved?: () => void }) {
  const [token, setToken] = useState(getStoredSideportApiToken())
  const [saved, setSaved] = useState(false)
  const saveToken = () => {
    saveSideportApiToken(token)
    setSaved(true)
    onApiTokenSaved?.()
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

function StatusPill({ state, label }: { state: HealthState; label: string }) {
  const Icon = state === 'healthy' ? CheckCircle2 : state === 'offline' ? CircleDashed : state === 'warning' ? AlertTriangle : XCircle
  return <span className={`status-pill ${state}`}><Icon size={14} />{label}</span>
}

function SourcePill({ source, label }: { source: SourceKind; label: string }) {
  return <span className={`source-pill ${source}`}>{label}</span>
}

function SystemChecks({ system }: { system: SystemStatus }) {
  return (
    <div className="check-list">
      <CheckRow label="API process" ok={system.api.ok} source={system.api.source} detail="/healthz responds." />
      <CheckRow label="Readiness" ok={system.ready.ready} source={system.ready.source} detail="Aggregates signer and anisette." />
      <CheckRow label="Anisette" ok={system.ready.checks.anisette.ok} source={system.ready.checks.anisette.source} detail={system.ready.checks.anisette.error ?? 'Client info available.'} />
      <CheckRow label="Signer binary" ok={system.ready.checks.signer.ok} source={system.ready.checks.signer.source} detail={system.ready.checks.signer.path} />
      <CheckRow label="API bearer token" ok={system.apiAuth.configured} source={system.apiAuth.source} detail="Do not expose /api refresh actions without auth." />
      <CheckRow label="Scheduler" ok={!system.scheduler.enabled} source={system.scheduler.source} detail={system.scheduler.enabled ? 'Automatic refresh scheduling is enabled.' : 'Automatic refresh scheduling is disabled.'} />
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

function OnboardingStepList({ steps }: { steps: OnboardingStep[] }) {
  if (!steps.length) return <EmptyState icon={ListChecks} title="Waiting for onboarding status" detail="The backend onboarding endpoint has not returned a setup checklist yet." />
  return (
    <div className="onboarding-list">
      {steps.map((step) => <OnboardingStepRow key={step.id} step={step} />)}
    </div>
  )
}

function IPhoneSetupList({ steps }: { steps: OnboardingStep[] }) {
  if (!steps.length) return <EmptyState icon={Smartphone} title="No iPhone actions yet" detail="Connect a device or register an app to reveal the iPhone-side setup prompts." />
  return (
    <div className="iphone-guide">
      {steps.map((step, index) => (
        <article className={`iphone-action ${step.state}`} key={step.id}>
          <div className="step-number">{index + 1}</div>
          <div>
            <strong>{step.label}</strong>
            <span>{step.description}</span>
            {step.settingsPath && <code className="settings-path">{step.settingsPath}</code>}
            {step.detail && <small>{step.detail}</small>}
          </div>
          <StatusLabel state={step.state} />
        </article>
      ))}
    </div>
  )
}

function OnboardingStepRow({ step }: { step: OnboardingStep }) {
  const Icon = step.state === 'complete' ? CheckCircle2 : step.state === 'blocked' ? XCircle : step.state === 'warning' ? AlertTriangle : CircleDashed
  return (
    <article className={`onboarding-step ${step.state}`}>
      <div className="onboarding-icon"><Icon size={17} /></div>
      <div>
        <strong>{step.label}</strong>
        <span>{step.description}</span>
        {step.detail && <small>{step.detail}</small>}
      </div>
      <StatusLabel state={step.state} />
    </article>
  )
}

function StatusLabel({ state }: { state: OnboardingStepState }) {
  const label = state === 'complete' ? 'Complete' : state === 'blocked' ? 'Blocked' : state === 'warning' ? 'Warning' : 'Pending'
  return <span className={`status-label ${state}`}>{label}</span>
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
      header: 'Last seen',
      cell: ({ row }) => <span>{relativeTime(row.original.lastSeenAt.value)}</span>,
    },
    {
      id: 'apps',
      header: 'Apps',
      cell: ({ row }) => <span>{appsForDevice(apps, row.original.udid).length}/3</span>,
    },
    {
      id: 'expiry',
      header: 'Nearest expiry',
      cell: ({ row }) => <span>{timeUntil(row.original.nearestExpiryAt?.value)}</span>,
    },
    {
      accessorKey: 'health',
      header: 'Health',
      cell: ({ row }) => <StatusPill state={row.original.health} label={healthCopy[row.original.health]} />,
    },
    {
      id: 'action',
      header: '',
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
            <tr key={group.id}>{group.headers.map((header) => <th key={header.id}>{flexRender(header.column.columnDef.header, header.getContext())}</th>)}</tr>
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

function DeviceCard({ device, apps, onOpen }: { device: DeviceSummary; apps: RegisteredAppSummary[]; onOpen?: () => void }) {
  return (
    <article className="device-card">
      <div className="device-card-top">
        <div><strong>{device.name}</strong><span>{device.productType} · {compactUdid(device.udid)}</span></div>
        <StatusPill state={device.health} label={healthCopy[device.health]} />
      </div>
      <div className="device-card-grid">
        <FactTile label="Connection" value={connectionLabel(device.connection)} source="live" />
        <FactTile label="Apps" value={`${apps.length}/3`} source="derived" />
        <FactTile label="Expiry" value={timeUntil(device.nearestExpiryAt?.value)} source={device.nearestExpiryAt?.source ?? 'planned'} />
      </div>
      <button className="row-action" onClick={onOpen} type="button">Open device</button>
    </article>
  )
}

function AppSlotGrid({ apps }: { apps: RegisteredAppSummary[] }) {
  const slots = [0, 1, 2].map((index) => apps[index] ?? null)
  return (
    <div className="slot-grid">
      {slots.map((app, index) => (
        <article className={app ? 'slot-card filled' : 'slot-card empty'} key={index}>
          <div className="slot-number">Slot {index + 1}</div>
          {app ? <AppSummary app={app} /> : <div className="empty-slot"><Plus size={18} /><span>Available</span><small>Registration disabled</small></div>}
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
        <small>Expires {timeUntil(app.expiresAt?.value)}</small>
      </div>
    </div>
  )
}

function RenewalLane({ title, items, apps, apiStatus }: { title: string; items: RenewalItem[]; apps: RegisteredAppSummary[]; apiStatus?: AdminDataStatus }) {
  return <Panel title={`${title} (${items.length})`}>{items.length ? <RenewalQueueList items={items} apps={apps} apiStatus={apiStatus} /> : <EmptyState icon={CheckCircle2} title={`No ${title.toLowerCase()} renewals`} detail="Nothing in this risk lane for the current API snapshot." />}</Panel>
}

function RenewalQueueList({ items, apps, compact = false, apiStatus }: { items: RenewalItem[]; apps: RegisteredAppSummary[]; compact?: boolean; apiStatus?: AdminDataStatus }) {
  const queryClient = useQueryClient()
  const refreshMutation = useMutation({
    mutationFn: (item: RenewalItem) => refreshSideportApp(item.deviceUdid, item.bundleId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['sideport-admin-data'] }),
  })
  return (
    <div className={compact ? 'renewal-list compact' : 'renewal-list'}>
      {items.map((item) => {
        const app = apps.find((candidate) => candidate.bundleId === item.bundleId && candidate.deviceUdid === item.deviceUdid)
        const isRefreshing = refreshMutation.isPending && refreshMutation.variables?.id === item.id
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
            {item.blocker && <p>{item.blocker}</p>}
            <SourcePill source={item.source} label={sourceLabel(item.source)} />
            {!compact && <button className="row-action inline-action" disabled={!apiStatus?.canMutate || isRefreshing} onClick={() => refreshMutation.mutate(item)} type="button"><RefreshCw size={14} /> {isRefreshing ? 'Refreshing' : 'Refresh'}</button>}
          </article>
        )
      })}
      {refreshMutation.isSuccess && <p className="mutation-message success">Refresh request finished. Results will reload from the API.</p>}
      {refreshMutation.error && <p className="mutation-message error">{refreshMutation.error.message}</p>}
    </div>
  )
}

function DiagnosticIssueList({ issues, compact = false }: { issues: DiagnosticIssue[]; compact?: boolean }) {
  return (
    <div className={compact ? 'issue-list compact' : 'issue-list'}>
      {issues.map((issue) => (
        <article className={`issue-card severity-${issue.severity}`} key={issue.id}>
          <div className="issue-head">
            <div>
              <strong>{issue.category}</strong>
              <span>{issue.status} · last seen {relativeTime(issue.lastSeenAt)}</span>
            </div>
            <SourcePill source={issue.source} label={sourceLabel(issue.source)} />
          </div>
          {!compact && <p>{issue.logSnippet}</p>}
          <div className="trace-row"><Activity size={15} /><span>{issue.operationId}</span><span>{issue.traceId}</span></div>
          {!compact && <div className="span-strip">{issue.spanSummary.map((span) => <span className={`span-chip ${span.state}`} key={span.name}>{span.name} · {span.durationMs}ms</span>)}</div>}
        </article>
      ))}
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

function PreflightList({ items }: { items: Array<[string, boolean, SourceKind]> }) {
  return <div className="check-list">{items.map(([label, ok, source]) => <CheckRow key={label} label={label} ok={ok} detail={ok ? 'Ready' : 'Not available yet'} source={source} />)}</div>
}

function EmptyState({ icon: Icon, title, detail }: { icon: LucideIcon; title: string; detail: string }) {
  return (
    <div className="empty-state">
      <Icon size={24} />
      <strong>{title}</strong>
      <span>{detail}</span>
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

function App() {
  const [apiTokenRevision, setApiTokenRevision] = useState(0)
  const { data } = useSideportAdminData(apiTokenRevision)
  return <SideportAdminApp data={data?.data} apiStatus={data?.status} onApiTokenSaved={() => setApiTokenRevision((revision) => revision + 1)} />
}

export default App
