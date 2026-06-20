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
  KeyRound,
  ListChecks,
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
import { completePersonalAppleTwoFactor, getStoredSideportApiToken, inspectCatalogApp, refreshSideportApp, registerSideportApp, runDeviceDiagnostics, saveSideportApiToken, signInPersonalApple, useSideportAdminData, type AdminDataStatus, type AppRegistrationPayload, type DeviceDiagnosticsDto } from './api/sideportApi'
import type { OnboardingStep, OnboardingStepState } from './api/sideportApi'
import {
  runtimeEmptyData,
  type ActivityEvent,
  type CatalogAppSummary,
  type DeviceSummary,
  type DiagnosticIssue,
  type HealthState,
  type InstalledAppSummary,
  type OperationLogEntry,
  type PersonalAppleSummary,
  type RegisteredAppSummary,
  type RenewalItem,
  type RenewalRisk,
  type RenewalStatus,
  type SideportReadModel,
  type SourceKind,
  type SystemStatus,
  type AppleAccessCapabilitySummary,
  type AppleAccessSummary,
} from './data/sideportTypes'
import { compactUdid, relativeTime, shortDateTime, sourceLabel, timeUntil } from './lib/format'

export type RouteId = 'onboarding' | 'overview' | 'devices' | 'device-detail' | 'catalog' | 'install-app' | 'renewals' | 'apple-access' | 'diagnostics' | 'settings'

const routeItems: Array<{ id: RouteId; label: string; icon: LucideIcon }> = [
  { id: 'onboarding', label: 'Onboarding', icon: ListChecks },
  { id: 'overview', label: 'Overview', icon: Gauge },
  { id: 'devices', label: 'Devices', icon: Smartphone },
  { id: 'catalog', label: 'App Catalog', icon: Package },
  { id: 'renewals', label: 'Renewals', icon: TimerReset },
  { id: 'apple-access', label: 'Apple Access', icon: KeyRound },
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
  const catalogApps = viewData.catalogApps
  const [route, setRoute] = useState<RouteId>(initialRoute)
  const [selectedCatalogAppId, setSelectedCatalogAppId] = useState(catalogApps[0]?.id ?? '')
  const selectedDevice = viewData.devices[0]
  const openInstallWizard = (catalogAppId = selectedCatalogAppId) => {
    setSelectedCatalogAppId(catalogAppId || catalogApps[0]?.id || '')
    setRoute('install-app')
  }

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
          {route === 'onboarding' && <OnboardingPage data={viewData} apiStatus={viewStatus} onNavigate={setRoute} onInstallFirstApp={() => openInstallWizard(catalogApps[0]?.id)} />}
          {route === 'overview' && <OverviewPage data={viewData} onNavigate={setRoute} />}
          {route === 'devices' && <DevicesPage data={viewData} onOpenDevice={() => setRoute('device-detail')} />}
          {route === 'device-detail' && <DeviceDetailPage data={viewData} device={selectedDevice} apiStatus={viewStatus} onInstallApp={() => openInstallWizard(catalogApps[0]?.id)} />}
          {route === 'catalog' && <AppCatalogPage data={viewData} apiStatus={viewStatus} catalogApps={catalogApps} onInstallApp={openInstallWizard} />}
          {route === 'install-app' && <InstallWizardPage data={viewData} apiStatus={viewStatus} catalogApps={catalogApps} initialCatalogAppId={selectedCatalogAppId} onOpenCatalog={() => setRoute('catalog')} />}
          {route === 'renewals' && <RenewalsPage data={viewData} apiStatus={viewStatus} />}
          {route === 'apple-access' && <AppleAccessPage appleAccess={viewData.appleAccess} personalApple={viewData.personalApple} apiStatus={viewStatus} />}
          {route === 'diagnostics' && <DiagnosticsPage data={viewData} />}
          {route === 'settings' && <SettingsPage system={viewData.system} apiStatus={viewStatus} onApiTokenSaved={onApiTokenSaved} />}
        </main>
      </div>
    </div>
  )
}

export function OnboardingPage({ data, apiStatus, onNavigate, onInstallFirstApp }: { data: SideportReadModel; apiStatus: AdminDataStatus; onNavigate?: (route: RouteId) => void; onInstallFirstApp?: () => void }) {
  const onboarding = apiStatus.onboarding
  const steps = onboarding?.steps ?? []
  const iphoneSteps = steps.filter((step) => step.surface === 'iphone')
  const completeSteps = steps.filter((step) => step.state === 'complete').length
  const requiredComplete = onboarding?.firstRunComplete ?? false
  const readyCatalogApps = data.catalogApps.filter((app) => app.status === 'ready').length
  const primaryBlocked = !data.system.ready.ready || readyCatalogApps === 0
  const hasApiSnapshot = apiStatus.mode === 'live' || apiStatus.mode === 'partial' || apiStatus.mode === 'demo'
  const wizardSteps = buildOnboardingWizardSteps(data, apiStatus, iphoneSteps, onNavigate, onInstallFirstApp)
  const [activeStepId, setActiveStepId] = useState(wizardSteps[0]?.id ?? 'server')
  const activeIndex = Math.max(0, wizardSteps.findIndex((step) => step.id === activeStepId))
  const activeStep = wizardSteps[activeIndex] ?? wizardSteps[0]
  const goStep = (index: number) => setActiveStepId(wizardSteps[Math.min(Math.max(index, 0), wizardSteps.length - 1)]?.id ?? activeStepId)

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
          <button className="primary-action" disabled={primaryBlocked} onClick={onInstallFirstApp} type="button"><Plus size={16} /> Register first app</button>
          <button className="ghost-action" onClick={() => onNavigate?.('settings')} type="button">View checks<ChevronRight size={15} /></button>
        </div>
      </section>

      {activeStep && (
        <section className="setup-wizard" aria-label="Focused setup wizard">
          <aside className="setup-stepper">
            {wizardSteps.map((step, index) => (
              <button className={step.id === activeStep.id ? 'setup-step-tab active' : 'setup-step-tab'} key={step.id} onClick={() => setActiveStepId(step.id)} type="button">
                <span>{index + 1}</span>
                <div><strong>{step.title}</strong><small>{step.kicker}</small></div>
                <StatusPill state={step.health} label={step.statusLabel} />
              </button>
            ))}
          </aside>
          <Panel title={activeStep.title}>
            <div className="setup-step-panel">
              <p>{activeStep.description}</p>
              {activeStep.content}
              <div className="setup-step-actions">
                <button className="ghost-action" disabled={activeIndex === 0} onClick={() => goStep(activeIndex - 1)} type="button">Back</button>
                {activeStep.secondaryAction && <button className="ghost-action" onClick={activeStep.secondaryAction.onClick} type="button">{activeStep.secondaryAction.label}<ChevronRight size={15} /></button>}
                {activeStep.primaryAction && <button className="primary-action" disabled={activeStep.primaryAction.disabled} onClick={activeStep.primaryAction.onClick} type="button">{activeStep.primaryAction.label}<ChevronRight size={15} /></button>}
                {!activeStep.primaryAction && <button className="primary-action" disabled={activeIndex === wizardSteps.length - 1} onClick={() => goStep(activeIndex + 1)} type="button">Next<ChevronRight size={15} /></button>}
              </div>
            </div>
          </Panel>
        </section>
      )}

      <div className="two-column-layout">
        <Panel title="First app registration path">
          <div className="setup-path">
            <FactTile label="Device" value={hasApiSnapshot ? data.devices[0]?.name ?? 'No reachable device' : 'Waiting for API'} source={hasApiSnapshot && data.devices.length ? 'live' : 'planned'} />
            <FactTile label="Ready catalog apps" value={hasApiSnapshot ? String(readyCatalogApps) : 'Unknown'} source={readyCatalogApps ? 'live' : 'planned'} />
            <FactTile label="Sideport registrations" value={hasApiSnapshot && data.devices[0] ? `${data.devices[0].appSlotsUsed}/3 used` : 'Unknown'} source={hasApiSnapshot ? 'derived' : 'planned'} />
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

interface SetupWizardStep {
  id: string
  title: string
  kicker: string
  statusLabel: string
  health: HealthState
  description: string
  content: ReactNode
  primaryAction?: { label: string; onClick: () => void; disabled?: boolean }
  secondaryAction?: { label: string; onClick: () => void }
}

function buildOnboardingWizardSteps(
  data: SideportReadModel,
  apiStatus: AdminDataStatus,
  iphoneSteps: OnboardingStep[],
  onNavigate?: (route: RouteId) => void,
  onInstallFirstApp?: () => void,
): SetupWizardStep[] {
  const readyCatalogApps = data.catalogApps.filter((app) => app.status === 'ready').length
  const canRegister = apiStatus.canMutate && data.devices.length > 0 && readyCatalogApps > 0
  const appleAccessConfigured = data.personalApple.state === 'authenticated' || data.personalApple.state === 'two-factor-required' || data.appleAccess.state === 'read-only-verified' || data.appleAccess.state === 'partial'

  return [
    {
      id: 'server',
      title: 'Server readiness',
      kicker: 'Verified by Sideport',
      statusLabel: data.system.ready.ready ? 'Ready' : 'Blocked',
      health: data.system.ready.ready ? 'healthy' : 'blocked',
      description: 'Start with checks Sideport can verify automatically: API, anisette, signer, API auth, and scheduler posture.',
      content: <SystemChecks system={data.system} />,
      secondaryAction: { label: 'Open settings', onClick: () => onNavigate?.('settings') },
    },
    {
      id: 'identity',
      title: 'Sideport identity',
      kicker: 'Optional Apple login',
      statusLabel: 'Optional',
      health: 'offline',
      description: 'Sign in with Apple should be an optional Sideport login provider. It can identify the portal user, but it does not grant Developer signing access.',
      content: (
        <div className="check-list">
          <CheckRow label="Reverse proxy or local auth" ok source="planned" detail="Sideport can run without Sign in with Apple." />
          <CheckRow label="Sign in with Apple" ok={false} source="planned" detail="Optional identity provider. Not required for signing." />
          <CheckRow label="Developer access" ok={false} source="planned" detail="Handled separately by Personal Apple ID or App Store Connect connector." />
        </div>
      ),
      secondaryAction: { label: 'Open Apple Access', onClick: () => onNavigate?.('apple-access') },
    },
    {
      id: 'apple-access',
      title: 'Apple signing access',
      kicker: 'Separate from login',
      statusLabel: appleAccessConfigured ? 'Probe ready' : 'Planned',
      health: appleAccessConfigured ? 'warning' : 'offline',
      description: `For your free Apple ID, Sideport uses the Personal Apple ID connector backed by ${credentialCustodyLabel(data.personalApple.secretCustody)}. Paid-team App Store Connect JWT is optional and currently read-only.`,
      content: <AppleAccessSetupSummary appleAccess={data.appleAccess} personalApple={data.personalApple} />,
      secondaryAction: { label: 'Open Apple Access', onClick: () => onNavigate?.('apple-access') },
    },
    {
      id: 'iphone',
      title: 'iPhone readiness',
      kicker: 'Live and guided',
      statusLabel: data.devices.length ? 'Reachable' : 'Waiting',
      health: data.devices.length ? 'healthy' : 'warning',
      description: 'Sideport can detect reachable devices. Physical iPhone steps like Developer Mode and profile trust stay guided until a real signal exists.',
      content: iphoneSteps.length ? <IPhoneSetupList steps={iphoneSteps} /> : <EmptyState icon={Smartphone} title="No iPhone actions yet" detail="Connect and unlock an iPhone, tap Trust This Computer, then retry discovery." />,
      secondaryAction: { label: 'Open devices', onClick: () => onNavigate?.('devices') },
    },
    {
      id: 'catalog',
      title: 'Catalog app',
      kicker: 'Detected from IPA',
      statusLabel: readyCatalogApps ? 'Ready' : 'Needed',
      health: readyCatalogApps ? 'healthy' : 'blocked',
      description: 'The IPA should provide app identity automatically: bundle ID, version, checksum, and profile state. Server-path import works now; upload comes later.',
      content: data.catalogApps.length ? <CatalogReadinessList apps={data.catalogApps} /> : <EmptyState icon={Package} title="No catalog apps" detail="Inspect a server IPA path in App Catalog before registering it on a phone." />,
      secondaryAction: { label: 'Open catalog', onClick: () => onNavigate?.('catalog') },
    },
    {
      id: 'registration',
      title: 'Register first app',
      kicker: 'Stored registration',
      statusLabel: data.apps.length ? 'Saved' : 'Next action',
      health: data.apps.length ? 'healthy' : canRegister ? 'warning' : 'blocked',
      description: 'This saves Sideport intent for one catalog app on one reachable phone. It does not sign or install until the preflight/operation slice exists.',
      content: (
        <div className="setup-path">
          <FactTile label="Reachable devices" value={String(data.devices.length)} source={data.devices.length ? 'live' : 'planned'} />
          <FactTile label="Ready catalog apps" value={String(readyCatalogApps)} source={readyCatalogApps ? 'live' : 'planned'} />
          <FactTile label="Existing registrations" value={String(data.apps.length)} source={data.apps.length ? 'live' : 'planned'} />
        </div>
      ),
      primaryAction: { label: 'Register first app', onClick: () => onInstallFirstApp?.(), disabled: !canRegister },
      secondaryAction: { label: 'Open catalog', onClick: () => onNavigate?.('catalog') },
    },
  ]
}

function AppleAccessSetupSummary({ appleAccess, personalApple }: { appleAccess: AppleAccessSummary; personalApple: PersonalAppleSummary }) {
  return (
    <div className="check-list">
      <CheckRow label="Optional portal login" ok={false} source="planned" detail="Sign in with Apple can identify the Sideport user, but is not required." />
      <CheckRow label={`Personal Apple ID via ${credentialCustodyShortLabel(personalApple.secretCustody)}`} ok={personalApple.state === 'authenticated'} source={personalApple.source} detail={personalApple.message} />
      <CheckRow label="App Store Connect JWT" ok={appleAccess.state === 'read-only-verified'} source={appleAccess.source} detail={appleAccess.message} />
    </div>
  )
}

function CatalogReadinessList({ apps }: { apps: CatalogAppSummary[] }) {
  return (
    <div className="check-list">
      {apps.map((app) => <CheckRow key={app.id} label={app.name} ok={app.status === 'ready'} source={app.source} detail={`${app.expectedBundleId} - ${app.statusLabel}`} />)}
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
      <PageHeader eyebrow="Devices" title="Device inventory" description="Reachability comes from /api/devices. App counts and expiry dates come from apps registered in Sideport, not from every app already installed on the phone." />
      <DeviceInventoryTable devices={data.devices} apps={data.apps} onOpenDevice={onOpenDevice} />
      <div className="device-card-list">
        {data.devices.map((device) => <DeviceCard key={device.udid} device={device} apps={appsForDevice(data.apps, device.udid)} onOpen={() => onOpenDevice?.(device)} />)}
      </div>
    </div>
  )
}

export function DeviceDetailPage({ data, device, apiStatus, onInstallApp }: { data: SideportReadModel; device?: DeviceSummary; apiStatus: AdminDataStatus; onInstallApp?: () => void }) {
  if (!device) {
    return <EmptyState icon={Smartphone} title="No selected device" detail="Device detail needs a reachable or known device read model." />
  }
  const apps = appsForDevice(data.apps, device.udid)
  const installedApps = data.installedApps.filter((app) => app.deviceUdid === device.udid)
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
        <FactTile label="Nearest registered expiry" value={expiryCopy(device.nearestExpiryAt?.value)} source={device.nearestExpiryAt?.source ?? 'planned'} />
        <FactTile label="Installed user apps" value={String(device.installedAppCount)} source={installedApps.length ? 'live' : 'planned'} />
        <FactTile label="Unmanaged installed" value={String(device.unmanagedAppCount)} source={installedApps.length ? 'derived' : 'planned'} />
      </section>

      <Panel title="Sideport-registered app slots">
        <AppSlotGrid apps={apps} canRegister={apiStatus.canMutate} onInstallApp={onInstallApp} />
      </Panel>

      <Panel title="Installed on phone">
        {installedApps.length ? <InstalledAppList apps={installedApps} /> : <EmptyState icon={Package} title="Installed app list unavailable" detail="Sideport could not read installation_proxy data for this reachable phone in the current snapshot." />}
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

export function AppCatalogPage({ data, apiStatus, catalogApps, onInstallApp }: { data: SideportReadModel; apiStatus: AdminDataStatus; catalogApps: CatalogAppSummary[]; onInstallApp: (catalogAppId: string) => void }) {
  return (
    <div className="page-stack">
      <PageHeader
        eyebrow="App Catalog"
        title="Reusable apps, separate from phone slots"
        description="Catalog apps are reusable IPA definitions from /api/catalog/apps. Registered installations are /api/apps records that occupy Sideport slots on phones."
      />

      {catalogApps.length ? (
        <div className="catalog-grid">
          {catalogApps.map((catalogApp) => (
            <CatalogAppCard
              catalogApp={catalogApp}
              installationCount={data.apps.filter((app) => app.bundleId === catalogApp.expectedBundleId).length}
              key={catalogApp.id}
              onInstall={() => onInstallApp(catalogApp.id)}
            />
          ))}
        </div>
      ) : (
        <EmptyState icon={Package} title="No catalog apps returned" detail="The live runtime only shows apps returned by /api/catalog/apps. Inspect a server IPA path to add one." />
      )}

      <Panel title="Add a server IPA">
        <CatalogInspectPanel apiStatus={apiStatus} />
      </Panel>

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

function CatalogInspectPanel({ apiStatus }: { apiStatus: AdminDataStatus }) {
  const queryClient = useQueryClient()
  const [ipaPath, setIpaPath] = useState('')
  const inspectMutation = useMutation({
    mutationFn: () => inspectCatalogApp({ ipaPath: ipaPath.trim() }),
    onSuccess: () => {
      setIpaPath('')
      queryClient.invalidateQueries({ queryKey: ['sideport-admin-data'] })
    },
  })
  const canInspect = apiStatus.canMutate && ipaPath.trim().length > 0 && !inspectMutation.isPending

  return (
    <div className="inspect-form">
      <label className="form-field wide-field">
        <span>Server IPA path</span>
        <input autoComplete="off" onChange={(event) => setIpaPath(event.currentTarget.value)} placeholder="/var/lib/sideport/ipa/App.ipa" value={ipaPath} />
      </label>
      <button className="primary-action" disabled={!canInspect} onClick={() => inspectMutation.mutate()} type="button">
        <RefreshCw size={16} /> {inspectMutation.isPending ? 'Inspecting...' : 'Inspect IPA'}
      </button>
      {!apiStatus.canMutate && <p className="mutation-message">Catalog changes are disabled for this build.</p>}
      {inspectMutation.isSuccess && <p className="mutation-message success">Catalog app inspected and saved.</p>}
      {inspectMutation.error && <p className="mutation-message error">{inspectMutation.error.message}</p>}
    </div>
  )
}

export function InstallWizardPage({ data, apiStatus, catalogApps, initialCatalogAppId, onOpenCatalog }: { data: SideportReadModel; apiStatus: AdminDataStatus; catalogApps: CatalogAppSummary[]; initialCatalogAppId: string; onOpenCatalog: () => void }) {
  const firstDevice = data.devices[0]
  const [catalogAppId, setCatalogAppId] = useState(initialCatalogAppId || catalogApps[0]?.id || '')
  const selectedCatalogApp = catalogApps.find((app) => app.id === catalogAppId) ?? catalogApps[0]
  const catalogReady = selectedCatalogApp?.status === 'ready'
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
    bundleId: form.bundleId || selectedCatalogApp?.expectedBundleId || '',
    deviceUdid: form.deviceUdid || firstDevice?.udid || '',
    teamId: form.teamId || defaultTeamId,
    inputIpaPath: form.inputIpaPath || selectedCatalogApp?.suggestedIpaPath || '',
  }
  const selectedDevice = data.devices.find((device) => device.udid === registrationPayload.deviceUdid) ?? firstDevice
  const selectedDeviceRegistrations = selectedDevice ? appsForDevice(data.apps, selectedDevice.udid) : []
  const slotAvailable = Boolean(selectedDevice && selectedDeviceRegistrations.length < 3)
  const registerMutation = useMutation({
    mutationFn: () => registerSideportApp(registrationPayload),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['sideport-admin-data'] }),
  })
  const update = (key: keyof AppRegistrationPayload, value: string) => setForm((current) => ({ ...current, [key]: value }))
  const hasRequiredFields = Object.values(registrationPayload).every((value) => value.trim().length > 0)
  const canSubmit = apiStatus.canMutate && catalogReady && hasRequiredFields && slotAvailable && !registerMutation.isPending
  const registrationHelp = registrationDisabledHelp(apiStatus.canMutate, registrationPayload, catalogReady, slotAvailable)

  return (
    <div className="page-stack">
      <PageHeader
        eyebrow="Registration wizard"
        title="Register a catalog app for Sideport refresh"
        description="This saves a durable /api/apps registration after the catalog IPA has been inspected by the backend. It does not sign, install, or refresh the IPA yet."
      />
      <div className="wizard-shell">
        <aside className="wizard-step-rail" aria-label="Registration steps">
          {([
            ['1', 'Catalog IPA', Boolean(catalogReady)],
            ['2', 'Reachable phone', Boolean(selectedDevice)],
            ['3', 'Registration fields', hasRequiredFields],
            ['4', 'Save record', canSubmit || registerMutation.isSuccess],
          ] satisfies Array<[string, string, boolean]>).map(([index, label, complete]) => (
            <div className={complete ? 'wizard-step complete' : 'wizard-step'} key={String(label)}>
              <span>{index}</span>
              <strong>{label}</strong>
            </div>
          ))}
        </aside>

        <div className="wizard-content">
          <Panel title="Choose catalog app">
            {catalogApps.length ? (
              <div className="catalog-picker">
                {catalogApps.map((catalogApp) => (
                  <button className={catalogApp.id === catalogAppId ? 'catalog-pick selected' : 'catalog-pick'} key={catalogApp.id} onClick={() => setCatalogAppId(catalogApp.id)} type="button">
                    <span className={`app-icon ${catalogApp.iconTone}`}>{catalogApp.name.slice(0, 1)}</span>
                    <span><strong>{catalogApp.name}</strong><small>{catalogApp.statusLabel}</small></span>
                  </button>
                ))}
              </div>
            ) : (
              <EmptyState icon={Package} title="No catalog app selected" detail="Inspect a server IPA path in App Catalog before registering it on a phone." />
            )}
          </Panel>

          {selectedCatalogApp && (
            <Panel title="Catalog metadata">
              <dl className="catalog-meta compact">
                <div><dt>Bundle ID</dt><dd>{selectedCatalogApp.expectedBundleId}</dd></div>
                <div><dt>Server IPA path</dt><dd>{selectedCatalogApp.suggestedIpaPath}</dd></div>
                <div><dt>Version</dt><dd>{selectedCatalogApp.versionLabel}</dd></div>
                <div><dt>Inspection</dt><dd>{selectedCatalogApp.lastInspectedAt ? shortDateTime(selectedCatalogApp.lastInspectedAt) : selectedCatalogApp.statusLabel}</dd></div>
                <div><dt>SHA-256</dt><dd>{selectedCatalogApp.sha256 ? `${selectedCatalogApp.sha256.slice(0, 12)}...` : 'Not available'}</dd></div>
                <div><dt>Profile</dt><dd>{selectedCatalogApp.hasEmbeddedProfile ? expiryCopy(selectedCatalogApp.signatureExpiresAt) : 'No embedded profile'}</dd></div>
              </dl>
              {selectedCatalogApp.status !== 'ready' && <p className="mutation-message error">This catalog app is not ready for registration: {selectedCatalogApp.statusLabel}.</p>}
            </Panel>
          )}

          <Panel title="Choose reachable phone">
            {data.devices.length ? (
              <label className="form-field">
                <span>Target phone</span>
                <select value={registrationPayload.deviceUdid} onChange={(event) => update('deviceUdid', event.currentTarget.value)}>
                  {data.devices.map((device) => <option key={device.udid} value={device.udid}>{device.name} · {compactUdid(device.udid)} · {appsForDevice(data.apps, device.udid).length}/3 registered</option>)}
                </select>
              </label>
            ) : (
              <EmptyState icon={Smartphone} title="No reachable iPhone" detail="The live API only reports reachable phones today. Persistent known-offline phones need the planned device store." />
            )}
          </Panel>

          <Panel title="Registration fields">
            <div className="form-grid">
              <label className="form-field">
                <span>Apple ID</span>
                <input autoComplete="username" onChange={(event) => update('appleId', event.currentTarget.value)} placeholder="name@example.com" value={form.appleId} />
              </label>
              <label className="form-field">
                <span>Team ID</span>
                <input autoComplete="off" onChange={(event) => update('teamId', event.currentTarget.value)} placeholder="TEAMID1234" value={registrationPayload.teamId} />
              </label>
            </div>
          </Panel>

          <Panel title="Registration preflight">
            <PreflightList items={[
              ['Catalog IPA inspected', Boolean(catalogReady), selectedCatalogApp?.source ?? 'planned'],
              ['Reachable phone selected', Boolean(selectedDevice), selectedDevice ? 'live' : 'planned'],
              [`Sideport registrations ${selectedDeviceRegistrations.length}/3`, slotAvailable, 'derived'],
              ['Signer ready for future refresh', data.system.ready.checks.signer.ok, 'live'],
              ['Anisette identity trusted', data.system.ready.checks.anisette.ok, 'live'],
              ['Mutation endpoint enabled', apiStatus.canMutate, apiStatus.canMutate ? 'live' : 'planned'],
            ]} />
            <button className="primary-action wide" disabled={!canSubmit} onClick={() => registerMutation.mutate()} type="button">
              <Play size={16} /> {registerMutation.isPending ? 'Saving...' : 'Save registration'}
            </button>
            {registrationHelp && <p className="mutation-message">{registrationHelp}</p>}
            {!apiStatus.canMutate && <p className="mutation-message">Mutations are disabled for this build. Set VITE_SIDEPORT_ENABLE_MUTATIONS=true for the real portal bundle.</p>}
            {registerMutation.isSuccess && <p className="mutation-message success">Registration saved. This records the app for Sideport refresh; it did not install the IPA yet.</p>}
            {registerMutation.error && <p className="mutation-message error">{registerMutation.error.message}</p>}
            <button className="ghost-action" onClick={onOpenCatalog} type="button">Back to catalog<ChevronRight size={15} /></button>
          </Panel>
        </div>
      </div>
    </div>
  )
}

function CatalogAppCard({ catalogApp, installationCount, onInstall }: { catalogApp: CatalogAppSummary; installationCount: number; onInstall: () => void }) {
  const canRegister = catalogApp.status === 'ready'
  return (
    <article className="catalog-card">
      <div className="catalog-card-top">
        <div className={`app-icon ${catalogApp.iconTone}`}>{catalogApp.name.slice(0, 1)}</div>
        <div>
          <h2>{catalogApp.name}</h2>
          <span>{catalogApp.statusLabel}</span>
        </div>
        <SourcePill source={catalogApp.source} label={sourceLabel(catalogApp.source)} />
      </div>
      <p>{catalogApp.purpose}</p>
      <dl className="catalog-meta">
        <div><dt>Bundle ID</dt><dd>{catalogApp.expectedBundleId}</dd></div>
        <div><dt>Server path</dt><dd>{catalogApp.suggestedIpaPath}</dd></div>
        <div><dt>Version</dt><dd>{catalogApp.versionLabel}</dd></div>
        <div><dt>Sideport registrations</dt><dd>{installationCount}</dd></div>
        <div><dt>Profile</dt><dd>{catalogApp.hasEmbeddedProfile ? expiryCopy(catalogApp.signatureExpiresAt) : 'No embedded profile'}</dd></div>
        <div><dt>Checksum</dt><dd>{catalogApp.sha256 ? `${catalogApp.sha256.slice(0, 12)}...` : 'Not available'}</dd></div>
      </dl>
      <ul className="catalog-notes">
        {catalogApp.notes.map((note) => <li key={note}>{note}</li>)}
      </ul>
      <button className="primary-action" disabled={!canRegister} onClick={onInstall} type="button"><Plus size={16} /> Register on phone</button>
    </article>
  )
}

function RegisteredInstallationList({ apps }: { apps: RegisteredAppSummary[] }) {
  return (
    <div className="registration-list">
      {apps.map((app) => (
        <article className="registration-card" key={`${app.deviceUdid}:${app.bundleId}`}>
          <AppSummary app={app} />
          <div className="registration-meta">
            <span>{compactUdid(app.deviceUdid)}</span>
            <span>{app.teamId}</span>
            <span>{expiryCopy(app.expiresAt?.value)}</span>
          </div>
        </article>
      ))}
    </div>
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
    ['Seeded app', 'Cert Clock is configured as the first server-side catalog seed.'],
    ['Server IPA path', 'The operator points Sideport at an IPA already present on the server.'],
    ['Inspection endpoint', '/api/catalog/apps/inspect reads bundle ID, version, checksum, and embedded-profile state.'],
    ['Catalog store', 'Inspected apps become durable catalog records that can be registered on one or more phones.'],
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

export function AppleAccessPage({ appleAccess, personalApple, apiStatus }: { appleAccess: AppleAccessSummary; personalApple: PersonalAppleSummary; apiStatus: AdminDataStatus }) {
  const verified = appleAccess.capabilities.filter((capability) => capability.state === 'verified').length
  const blocked = appleAccess.capabilities.filter((capability) => capability.state !== 'verified' && capability.state !== 'not-checked').length

  return (
    <div className="page-stack">
      <PageHeader
        eyebrow="Apple Access"
        title="Connect Apple data without over-trusting it"
        description="This page runs a read-only App Store Connect API-key probe. It does not use browser cookies, does not ask for an Apple ID password, and performs no Apple mutations."
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
        <PersonalAppleConnectorPanel personalApple={personalApple} apiStatus={apiStatus} />
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
              ['No routine revocation', 'Certificate creation or revocation requires explicit cutover acknowledgement.'],
              ['No hidden mutations', 'This page only performs GET probes. Install and refresh actions must run through preflight.'],
            ].map(([title, detail], index) => <InfoStep detail={detail} index={index} key={title} title={title} />)}
          </div>
        </Panel>
      </div>
    </div>
  )
}

function PersonalAppleConnectorPanel({ personalApple, apiStatus }: { personalApple: PersonalAppleSummary; apiStatus: AdminDataStatus }) {
  const queryClient = useQueryClient()
  const [appleId, setAppleId] = useState('')
  const [code, setCode] = useState('')
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
  })
  const canSignIn = apiStatus.canMutate && appleId.trim().length > 0 && !signInMutation.isPending
  const canComplete2Fa = apiStatus.canMutate && Boolean(personalApple.pendingChallengeId) && code.trim().length >= 4 && !twoFactorMutation.isPending

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
      <div className="form-grid">
        <label className="form-field">
          <span>Apple ID</span>
          <input autoComplete="username" onChange={(event) => setAppleId(event.currentTarget.value)} placeholder="name@example.com" value={appleId} />
        </label>
        <div className="form-field action-field">
          <span>{credentialCustodyShortLabel(personalApple.secretCustody)} credential</span>
          <button className="primary-action" disabled={!canSignIn} onClick={() => signInMutation.mutate()} type="button">Start sign-in</button>
        </div>
      </div>
      <p className="data-boundary-note">Sideport does not accept Apple passwords in the browser. Configure the matching {credentialCustodyLabel(personalApple.secretCustody)} first, then start sign-in. If Apple requires 2FA, enter the code here locally.</p>
      {personalApple.pendingChallengeId && (
        <div className="form-grid">
          <label className="form-field">
            <span>{personalApple.pendingChallengeKind ?? '2FA'} code</span>
            <input autoComplete="one-time-code" inputMode="numeric" onChange={(event) => setCode(event.currentTarget.value)} placeholder="123456" value={code} />
          </label>
          <div className="form-field action-field">
            <span>Pending challenge</span>
            <button className="primary-action" disabled={!canComplete2Fa} onClick={() => twoFactorMutation.mutate()} type="button">Complete 2FA</button>
          </div>
        </div>
      )}
      {!apiStatus.canMutate && <p className="mutation-message">Mutations are disabled for this build. Personal Apple sign-in is unavailable in read-only mode.</p>}
      {signInMutation.error && <p className="mutation-message error">{signInMutation.error.message}</p>}
      {twoFactorMutation.error && <p className="mutation-message error">{twoFactorMutation.error.message}</p>}
      {personalApple.teams.length > 0 && <PersonalAppleTeamList teams={personalApple.teams} />}
    </div>
  )
}

function PersonalAppleTeamList({ teams }: { teams: PersonalAppleSummary['teams'] }) {
  return (
    <div className="registration-list">
      {teams.map((team) => (
        <article className="registration-card" key={team.teamId}>
          <div>
            <strong>{team.name}</strong>
            <span>{team.teamId}</span>
          </div>
          <div className="registration-meta"><span>{team.type}</span><span>Detected from Apple</span></div>
        </article>
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
      {error && <p className="muted" style={{ marginTop: 12 }}>Could not run the check: {error}</p>}
      {result && (
        <ul style={{ listStyle: 'none', margin: '14px 0 0', padding: 0, display: 'grid', gap: 12 }}>
          {result.checks.map((check) => (
            <li key={check.id} style={{ display: 'flex', gap: 12, alignItems: 'flex-start' }}>
              <StatusPill state={check.status === 'ok' ? 'healthy' : check.status === 'warning' ? 'warning' : 'blocked'} label={check.status} />
              <div style={{ minWidth: 0 }}>
                <strong>{check.label}</strong>
                <p className="muted" style={{ margin: '2px 0 0' }}>{check.detail}</p>
                {check.remediation && <p style={{ margin: '4px 0 0', fontSize: 13 }}>→ {check.remediation}</p>}
              </div>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  )
}

export function DiagnosticsPage({ data }: { data: SideportReadModel }) {
  return (
    <div className="page-stack">
      <PageHeader eyebrow="Diagnostics" title="Runtime failure evidence" description="Live evidence comes from readiness checks, API fetch failures, app lastError fields, and the protected API log stream." />
      <DeviceConnectivitySelfTest />
      {data.issues.length ? <DiagnosticIssueList issues={data.issues} /> : <EmptyState icon={Stethoscope} title="No diagnostic issues" detail="When OpenTelemetry is wired, this page will group refresh/sign/install failures by operation and trace ID." />}
      <Panel title="Log highlights">
        {data.logs.length ? <OperationLogList logs={data.logs.slice(0, 10)} /> : <EmptyState icon={Activity} title="No API logs yet" detail="The runtime log endpoint has not returned entries for this snapshot." />}
      </Panel>
      <Panel title="Advanced log tail">
        {data.logs.length ? <LogTailConsole logs={data.logs} /> : <EmptyState icon={Terminal} title="No log tail yet" detail="The protected log stream is empty for this snapshot." />}
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
        <FactTile label="Registered apps" value={`${apps.length}/3`} source="derived" />
        <FactTile label="Installed apps" value={`${device.installedAppCount} total`} source={device.installedAppCount ? 'live' : 'planned'} />
        <FactTile label="Registered expiry" value={expiryCopy(device.nearestExpiryAt?.value)} source={device.nearestExpiryAt?.source ?? 'planned'} />
      </div>
      <button className="row-action" onClick={onOpen} type="button">Open device</button>
    </article>
  )
}

function AppSlotGrid({ apps, canRegister, onInstallApp }: { apps: RegisteredAppSummary[]; canRegister: boolean; onInstallApp?: () => void }) {
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

function LogTailConsole({ logs }: { logs: OperationLogEntry[] }) {
  return (
    <div className="log-tail" role="log" aria-label="Advanced API log tail">
      <div className="log-tail-toolbar">
        <span><Terminal size={15} /> Protected /api/logs tail</span>
        <small>{logs.length} lines · newest first</small>
      </div>
      <pre className="log-tail-scroll">
        {logs.slice(0, 60).map((entry) => <code className={`tail-line level-${entry.level.toLowerCase()}`} key={entry.id}>{tailLine(entry)}</code>)}
      </pre>
    </div>
  )
}

function PreflightList({ items }: { items: Array<[string, boolean, SourceKind]> }) {
  return <div className="check-list">{items.map(([label, ok, source]) => <CheckRow key={label} label={label} ok={ok} detail={ok ? 'Ready' : 'Not available yet'} source={source} />)}</div>
}

function registrationDisabledHelp(canMutate: boolean, payload: AppRegistrationPayload, catalogReady: boolean, slotAvailable: boolean): string | null {
  if (!canMutate) return null
  if (!catalogReady) return 'Inspect a ready catalog IPA before saving a phone registration.'
  if (!slotAvailable) return 'This phone already has 3 Sideport registrations. Apps installed outside Sideport are reported separately when device app inspection is available.'
  const labels: Record<keyof AppRegistrationPayload, string> = {
    bundleId: 'Bundle ID',
    deviceUdid: 'Device UDID',
    appleId: 'Apple ID',
    teamId: 'Team ID',
    inputIpaPath: 'Server IPA path',
  }
  const missing = Object.entries(payload)
    .filter(([, value]) => !value.trim())
    .map(([key]) => labels[key as keyof AppRegistrationPayload])
  if (!missing.length) return null
  return `Fill ${missing.join(', ')} to enable registration.`
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
