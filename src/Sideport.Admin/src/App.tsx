import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Activity,
  AlertTriangle,
  Apple,
  Building2,
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
  ListChecks,
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
import { cancelOperation, completePersonalAppleTwoFactor, getStoredSideportApiToken, inspectCatalogApp, preflightSideportRefresh, refreshSideportApp, registerSideportApp, rerunOperation, retryOperation, runDeviceDiagnostics, saveSideportApiToken, signInPersonalApple, uploadCatalogIpa, useSideportAdminData, type AdminDataStatus, type AppRegistrationPayload, type DeviceDiagnosticsDto, type OperationPreflightDto } from './api/sideportApi'
import type { OnboardingStep, OnboardingStepState } from './api/sideportApi'
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

export type RouteId = 'onboarding' | 'overview' | 'devices' | 'device-detail' | 'catalog' | 'install-app' | 'renewals' | 'operations' | 'apple-access' | 'diagnostics' | 'teams' | 'users' | 'settings'

const routeItems: Array<{ id: RouteId; label: string; icon: LucideIcon }> = [
  { id: 'onboarding', label: 'Onboarding', icon: ListChecks },
  { id: 'overview', label: 'Overview', icon: Gauge },
  { id: 'devices', label: 'Devices', icon: Smartphone },
  { id: 'catalog', label: 'App Catalog', icon: Package },
  { id: 'renewals', label: 'Renewals', icon: TimerReset },
  { id: 'operations', label: 'Operations', icon: Activity },
  { id: 'apple-access', label: 'Apple Access', icon: KeyRound },
  { id: 'diagnostics', label: 'Diagnostics', icon: Stethoscope },
  { id: 'teams', label: 'Teams', icon: Building2 },
  { id: 'users', label: 'Users', icon: Users },
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
  onApiTokenSaved?: () => void
}

const runtimeStatus: AdminDataStatus = {
  mode: 'unavailable',
  baseUrl: '/sideport-api',
  message: 'Waiting for the .NET API.',
  canMutate: false,
}

export function SideportAdminApp({ data, apiStatus, initialRoute = 'onboarding', initialCommandOpen = false, onApiTokenSaved }: SideportAdminAppProps) {
  const viewData = data ?? runtimeEmptyData
  const viewStatus = apiStatus ?? runtimeStatus
  const catalogApps = viewData.catalogApps
  const [route, setRoute] = useState<RouteId>(initialRoute)
  const [selectedCatalogAppId, setSelectedCatalogAppId] = useState(catalogApps[0]?.id ?? '')
  const [selectedDeviceUdid, setSelectedDeviceUdid] = useState(viewData.devices[0]?.udid ?? '')
  const [commandOpen, setCommandOpen] = useState(initialCommandOpen)
  const selectedDevice = viewData.devices.find((device) => device.udid === selectedDeviceUdid) ?? viewData.devices[0]
  const openDevice = (device: DeviceSummary) => {
    setSelectedDeviceUdid(device.udid)
    setRoute('device-detail')
  }
  const openInstallWizard = (catalogAppId = selectedCatalogAppId) => {
    setSelectedCatalogAppId(catalogAppId || catalogApps[0]?.id || '')
    setRoute('install-app')
  }

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
        <TopBar system={viewData.system} apiStatus={viewStatus} onOpenCommand={() => setCommandOpen(true)} />
        <main className="content-area">
          {route === 'onboarding' && <OnboardingPage data={viewData} apiStatus={viewStatus} onNavigate={setRoute} onInstallFirstApp={() => openInstallWizard(catalogApps[0]?.id)} />}
          {route === 'overview' && <OverviewPage data={viewData} onNavigate={setRoute} />}
          {route === 'devices' && <DevicesPage data={viewData} onOpenDevice={openDevice} />}
          {route === 'device-detail' && <DeviceDetailPage data={viewData} device={selectedDevice} apiStatus={viewStatus} onInstallApp={() => openInstallWizard(catalogApps[0]?.id)} />}
          {route === 'catalog' && <AppCatalogPage data={viewData} apiStatus={viewStatus} catalogApps={catalogApps} onInstallApp={openInstallWizard} />}
          {route === 'install-app' && <InstallWizardPage data={viewData} apiStatus={viewStatus} catalogApps={catalogApps} initialCatalogAppId={selectedCatalogAppId} onOpenCatalog={() => setRoute('catalog')} />}
          {route === 'renewals' && <RenewalsPage data={viewData} apiStatus={viewStatus} />}
          {route === 'operations' && <OperationsPage data={viewData} apiStatus={viewStatus} />}
          {route === 'apple-access' && <AppleAccessPage appleAccess={viewData.appleAccess} personalApple={viewData.personalApple} apiStatus={viewStatus} />}
          {route === 'diagnostics' && <DiagnosticsPage data={viewData} />}
          {route === 'teams' && <TeamsPage data={viewData} onNavigate={setRoute} />}
          {route === 'users' && <UsersPage workspace={viewData.workspace} activity={viewData.activity} apiStatus={viewStatus} />}
          {route === 'settings' && <SettingsPage system={viewData.system} apiStatus={viewStatus} onApiTokenSaved={onApiTokenSaved} />}
        </main>
      </div>
      <CommandMenu open={commandOpen} onOpenChange={setCommandOpen} data={viewData} onNavigate={setRoute} onOpenDevice={openDevice} />
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
              <button className={step.id === activeStep.id ? 'setup-step-tab active' : 'setup-step-tab'} aria-current={step.id === activeStep.id ? 'step' : undefined} key={step.id} onClick={() => setActiveStepId(step.id)} type="button">
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

function TopBar({ system, apiStatus, onOpenCommand }: { system: SystemStatus; apiStatus: AdminDataStatus; onOpenCommand: () => void }) {
  return (
    <header className="topbar">
      <button className="search-shell" onClick={onOpenCommand} type="button" aria-label="Search devices, apps, and screens">
        <Search size={17} />
        <span className="search-label">Search devices, bundle IDs, blockers</span>
        <span className="search-kbd"><kbd>⌘</kbd><kbd>K</kbd></span>
      </button>
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
  const [query, setQuery] = useState('')
  const [connectionFacet, setConnectionFacet] = useState<'all' | DeviceSummary['connection']>('all')
  const [healthFacet, setHealthFacet] = useState<'all' | HealthState>('all')

  if (data.devices.length === 0) {
    return (
      <div className="page-stack">
        <PageHeader eyebrow="Devices" title="No devices known yet" description="Known devices come from /api/devices/known. Connect a trusted iPhone over USB or Wi-Fi, or add a known-device record, to populate this view." />
        <EmptyState icon={Cable} title="No known devices returned" detail="Sideport has no durable device inventory yet, and the current reachability poll did not add a device to this snapshot." />
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
      <PageHeader eyebrow="Devices" title="Device inventory" description="Known devices come from /api/devices/known. Current reachability is overlaid from the live device poll without pretending every known device is reachable now." />

      <div className="devices-toolbar">
        <div className="devices-search">
          <Search size={16} />
          <input aria-label="Search devices" onChange={(event) => setQuery(event.currentTarget.value)} placeholder="Name, UDID, bundle ID, team" value={query} />
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
  const tabs: Array<{ id: 'apps' | 'signing' | 'network' | 'diagnostics' | 'activity'; label: string; count?: number }> = [
    { id: 'apps', label: 'Apps', count: device ? appsForDevice(data.apps, device.udid).length : undefined },
    { id: 'signing', label: 'Signing' },
    { id: 'network', label: 'Network' },
    { id: 'diagnostics', label: 'Diagnostics', count: device ? data.issues.filter((issue) => issue.deviceUdid === device.udid).length || undefined : undefined },
    { id: 'activity', label: 'Activity' },
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
                      <dd>Profile {expiryCopy(app.expiresAt?.value)} · {app.lastSucceeded === false ? 'last refresh failed' : 'healthy'}</dd>
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

        {activeTab === 'diagnostics' && (
          <Panel title="Diagnostics for this device">
            {issues.length ? <DiagnosticIssueList issues={issues} /> : <EmptyState icon={CheckCircle2} title="No open device issues" detail="Durable issue grouping appears when operation evidence exists for this device." />}
          </Panel>
        )}

        {activeTab === 'activity' && (
          <Panel title="Device activity">
            {data.activity.length ? <ActivityTimeline events={data.activity} /> : <EmptyState icon={Activity} title="No recent activity" detail="Sign, install, and refresh events will appear here." />}
            <p className="pipeline-note"><AlertTriangle size={14} /> Showing the workspace activity feed. Per-device filtering activates once the API tags each event with a device UDID.</p>
          </Panel>
        )}
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

      <Panel title="Import IPA from this browser">
        <CatalogUploadPanel apiStatus={apiStatus} />
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

function CatalogUploadPanel({ apiStatus }: { apiStatus: AdminDataStatus }) {
  const queryClient = useQueryClient()
  const [file, setFile] = useState<File | null>(null)
  const [id, setId] = useState('')
  const [name, setName] = useState('')
  const [replace, setReplace] = useState(false)
  const uploadMutation = useMutation({
    mutationFn: () => {
      if (!file) throw new Error('Choose an .ipa file first.')
      return uploadCatalogIpa(file, { id, name, replace })
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['sideport-admin-data'] }),
  })
  return (
    <div className="catalog-inspect">
      <label className="form-field">
        <span>IPA file</span>
        <input accept=".ipa" disabled={!apiStatus.canMutate || uploadMutation.isPending} onChange={(event) => setFile(event.currentTarget.files?.[0] ?? null)} type="file" />
      </label>
      <div className="form-grid">
        <label className="form-field">
          <span>Catalog ID (optional)</span>
          <input disabled={!apiStatus.canMutate || uploadMutation.isPending} onChange={(event) => setId(event.currentTarget.value)} placeholder="cert-clock" value={id} />
        </label>
        <label className="form-field">
          <span>Name (optional)</span>
          <input disabled={!apiStatus.canMutate || uploadMutation.isPending} onChange={(event) => setName(event.currentTarget.value)} placeholder="Cert Clock" value={name} />
        </label>
      </div>
      <label className="checkbox-row"><input checked={replace} disabled={!apiStatus.canMutate || uploadMutation.isPending} onChange={(event) => setReplace(event.currentTarget.checked)} type="checkbox" /> Replace existing catalog ID after inspection succeeds</label>
      <button className="primary-action" disabled={!apiStatus.canMutate || !file || uploadMutation.isPending} onClick={() => uploadMutation.mutate()} type="button"><Plus size={16} /> {uploadMutation.isPending ? 'Importing...' : 'Import IPA'}</button>
      {!apiStatus.canMutate && <p className="mutation-message">Mutations are disabled for this build. IPA import is unavailable in read-only mode.</p>}
      {uploadMutation.isSuccess && <p className="mutation-message success">Imported {uploadMutation.data.name}. This saved and inspected the IPA; it did not register, sign, or install it.</p>}
      {uploadMutation.error && <p className="mutation-message error">{uploadMutation.error.message}</p>}
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

          <Panel title="What happens when you refresh">
            <SigningPipeline
              title="Operation preview"
              stages={[
                { id: 'authorize', label: 'Authorize', detail: 'GrandSlam login', state: 'pending' },
                { id: 'provision', label: 'Provision', detail: 'App ID + profile', state: 'pending' },
                { id: 'sign', label: 'Sign', detail: 'zsign re-sign', state: 'pending' },
                { id: 'install', label: 'Install', detail: 'Push to device', state: 'pending' },
                { id: 'verify', label: 'Verify', detail: 'Launch check', state: 'pending' },
              ]}
              note="Saving a registration records intent only. These stages run when the refresh operation is wired to this device."
            />
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
  const [view, setView] = useState<'all' | 'healthy' | 'failed'>('all')
  const filtered = apps.filter((app) => {
    if (view === 'failed') return app.lastSucceeded === false
    if (view === 'healthy') return app.lastSucceeded !== false
    return true
  })

  return (
    <>
      <div className="devices-toolbar">
        <div className="facet-group" role="group" aria-label="Filter installations">
          <span className="facet-label"><Filter size={13} /> View</span>
          {([['all', 'All'], ['healthy', 'Healthy'], ['failed', 'Failed refresh']] as const).map(([value, label]) => (
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
                <StatusPill state={app.lastSucceeded === false ? 'failed' : 'healthy'} label={app.lastSucceeded === false ? 'Last refresh failed' : 'Healthy'} />
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
          {operation.stages.length ? <SigningPipeline title="Stages" stages={operationPipelineStages(operation.stages)} /> : null}
        </article>
      ))}
      {actionMutation.error && <p className="mutation-message error">{actionMutation.error.message}</p>}
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
      <Panel title="Scheduler">
        <dl className="detail-list">
          <div><dt>Automatic refresh</dt><dd>{system.scheduler.enabled ? 'Enabled' : 'Disabled'}</dd></div>
          <div><dt>Cadence</dt><dd>Every 6 hours (Sideport__Scheduler__ResignInterval).</dd></div>
          <div><dt>Behavior</dt><dd>Re-signs apps due within the lead time, soonest first, single-flight.</dd></div>
        </dl>
        <SourcePill source={system.scheduler.source} label={sourceLabel(system.scheduler.source)} />
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
        <small>Expires {timeUntil(app.expiresAt?.value)}</small>
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
  { id: 'verify', label: 'Verify', detail: 'Launch check', state: 'pending' },
]

export function SigningPipeline({ title = 'Sign · install · verify', stages = defaultPipelineStages, note }: { title?: string; stages?: PipelineStage[]; note?: string }) {
  const failed = stages.some((stage) => stage.state === 'failed')
  const active = stages.find((stage) => stage.state === 'active')
  const allDone = stages.length > 0 && stages.every((stage) => stage.state === 'done')
  const overall: HealthState = failed ? 'failed' : allDone ? 'healthy' : active ? 'warning' : 'offline'
  const overallLabel = failed ? 'Failed' : allDone ? 'Verified' : active ? `Running · ${active.label}` : 'Not started'

  return (
    <section className="signing-pipeline" aria-label="Signing pipeline">
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
  group: 'Go to' | 'Devices' | 'Apps'
  icon: LucideIcon
  run: () => void
}

function CommandMenu({ open, onOpenChange, data, onNavigate, onOpenDevice }: { open: boolean; onOpenChange: (open: boolean) => void; data: SideportReadModel; onNavigate: (route: RouteId) => void; onOpenDevice: (device: DeviceSummary) => void }) {
  const [query, setQuery] = useState('')
  const dismiss = (run: () => void) => {
    run()
    onOpenChange(false)
    setQuery('')
  }

  const targets: CommandTarget[] = [
    ...routeItems.map((item) => ({ id: `route:${item.id}`, label: item.label, meta: 'Screen', group: 'Go to' as const, icon: item.icon, run: () => onNavigate(item.id) })),
    ...data.devices.map((device) => ({ id: `device:${device.udid}`, label: device.name, meta: `${connectionLabel(device.connection)} · ${compactUdid(device.udid)}`, group: 'Devices' as const, icon: Smartphone, run: () => onOpenDevice(device) })),
    ...data.apps.map((app) => ({ id: `app:${app.deviceUdid}:${app.bundleId}`, label: app.displayName.value, meta: app.bundleId, group: 'Apps' as const, icon: Package, run: () => onNavigate('catalog') })),
  ]
  const q = query.trim().toLowerCase()
  const filtered = q ? targets.filter((target) => `${target.label} ${target.meta ?? ''}`.toLowerCase().includes(q)) : targets
  const groups = (['Go to', 'Devices', 'Apps'] as const).map((group) => ({ group, items: filtered.filter((target) => target.group === group) })).filter((entry) => entry.items.length)

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

interface DerivedAppleTeam {
  teamId: string
  name: string
  type: string
  appsUsing: number
  devicesRegistered: number
  certificateLabel: string
  certificateState: HealthState
  source: SourceKind
}

function deriveAppleTeams(data: SideportReadModel): DerivedAppleTeam[] {
  const ids = new Set<string>()
  data.devices.forEach((device) => { if (device.teamId && device.teamId !== 'Unknown') ids.add(device.teamId) })
  data.apps.forEach((app) => { if (app.teamId && app.teamId !== 'Unknown') ids.add(app.teamId) })
  data.personalApple.teams.forEach((team) => ids.add(team.teamId))
  const certCap = data.appleAccess.capabilities.find((capability) => capability.id === 'certificates')

  return Array.from(ids).map((teamId) => {
    const personalTeam = data.personalApple.teams.find((team) => team.teamId === teamId)
    let certificateState: HealthState = 'offline'
    let certificateLabel = 'Not probed'
    if (certCap) {
      if (certCap.state === 'verified') { certificateState = 'healthy'; certificateLabel = `Readable (${certCap.count ?? 0})` }
      else if (certCap.state === 'denied' || certCap.state === 'unauthorized') { certificateState = 'warning'; certificateLabel = 'Access denied by role' }
      else { certificateState = 'blocked'; certificateLabel = capabilityStateLabel(certCap.state) }
    }
    return {
      teamId,
      name: personalTeam?.name ?? 'Apple Developer Team',
      type: personalTeam?.type ?? (data.personalApple.connector === 'personal-apple-id' ? 'Free (personal)' : 'Unknown'),
      appsUsing: data.apps.filter((app) => app.teamId === teamId).length,
      devicesRegistered: data.devices.filter((device) => device.teamId === teamId).length,
      certificateLabel,
      certificateState,
      source: 'derived',
    }
  })
}

export function TeamsPage({ data, onNavigate }: { data: SideportReadModel; onNavigate?: (route: RouteId) => void }) {
  const appleTeams = deriveAppleTeams(data)
  const workspace = data.workspace

  return (
    <div className="page-stack">
      <PageHeader
        eyebrow="Teams"
        title="Two different kinds of team"
        description="An Apple Developer Team is where certificates, App IDs, and provisioning profiles come from. The Sideport workspace is who can operate this console. They are intentionally kept separate."
      />

      <section className="panel">
        <div className="panel-header">
          <h2>Apple Developer Teams</h2>
          <button className="ghost-action" onClick={() => onNavigate?.('apple-access')} type="button">Apple Access<ChevronRight size={15} /></button>
        </div>
        {appleTeams.length ? (
          <div className="teams-grid">
            {appleTeams.map((team) => (
              <article className="team-card" key={team.teamId}>
                <div className="team-card-top">
                  <div className="team-mark"><Apple size={18} /></div>
                  <div>
                    <h3>{team.name}</h3>
                    <span>{team.teamId} · {team.type}</span>
                  </div>
                  <SourcePill source={team.source} label={sourceLabel(team.source)} />
                </div>
                <div className="team-stats">
                  <div><strong>{team.appsUsing}</strong><span>Apps using it</span></div>
                  <div><strong>{team.devicesRegistered}</strong><span>Devices registered</span></div>
                </div>
                <StatusPill state={team.certificateState} label={`Certificates: ${team.certificateLabel}`} />
              </article>
            ))}
          </div>
        ) : <EmptyState icon={Apple} title="No Apple team detected yet" detail="Connect a Personal Apple ID or App Store Connect key in Apple Access. Teams are derived from devices, registered apps, and the Apple connector." />}
      </section>

      <section className="panel">
        <div className="panel-header">
          <h2>Sideport workspace</h2>
          <button className="ghost-action" onClick={() => onNavigate?.('users')} type="button">Manage users<ChevronRight size={15} /></button>
        </div>
        <div className="workspace-row">
          <div className="team-mark workspace-mark"><Building2 size={18} /></div>
          <dl className="detail-list workspace-detail">
            <div><dt>Workspace</dt><dd>{workspace.name}</dd></div>
            <div><dt>Current member</dt><dd>{workspace.currentMember?.name ?? 'API token client'}</dd></div>
            <div><dt>Members</dt><dd>{workspace.members.length} reported · user admin {workspace.supportsUserAdministration ? 'available' : 'not available'}</dd></div>
            <div><dt>Authentication</dt><dd>{workspace.authMode}</dd></div>
            <div><dt>Role enforcement</dt><dd>{workspace.roleEnforcement ?? 'not reported'}</dd></div>
          </dl>
          <SourcePill source={workspace.source} label={sourceLabel(workspace.source)} />
        </div>
        <p className="pipeline-note"><AlertTriangle size={14} /> The workspace controls who can use Sideport. It does not grant Apple signing access — that always comes from an Apple Developer Team above.</p>
      </section>
    </div>
  )
}

const roleCopy: Record<WorkspaceRole, { label: string; permissions: string }> = {
  owner: { label: 'Owner', permissions: 'Credentials, settings, users, refresh, and destructive actions.' },
  admin: { label: 'Admin', permissions: 'Teams, devices, apps, refresh, and diagnostics.' },
  operator: { label: 'Operator', permissions: 'Refresh and diagnostics; read-only on devices and apps.' },
  viewer: { label: 'Viewer', permissions: 'Read-only status and diagnostics.' },
}

const memberStatusCopy: Record<MemberStatus, string> = {
  active: 'Active',
  invited: 'Invite pending',
  suspended: 'Suspended',
}

export function RoleBadge({ role }: { role: WorkspaceRole }) {
  return <span className={`role-badge role-${role}`}>{roleCopy[role].label}</span>
}

export function UsersPage({ workspace, activity, apiStatus }: { workspace: WorkspaceSummary; activity: ActivityEvent[]; apiStatus: AdminDataStatus }) {
  const [inviteEmail, setInviteEmail] = useState('')
  const [inviteRole, setInviteRole] = useState<WorkspaceRole>('viewer')
  const canInvite = apiStatus.canMutate && inviteEmail.trim().length > 3 && !workspace.authDelegated && Boolean(workspace.capabilities?.['users.invite'])

  return (
    <div className="page-stack">
      <PageHeader
        eyebrow="Users & roles"
        title="Who can operate Sideport"
        description="Roles scope what each person can do — from read-only status to destructive credential actions. These are workspace roles, separate from any Apple Developer Team."
      />

      {workspace.authDelegated && (
        <p className="pipeline-note"><AlertTriangle size={14} /> Authentication is delegated to {workspace.authMode}. Role enforcement is {workspace.roleEnforcement ?? 'advisory'} and user administration is {workspace.supportsUserAdministration ? 'available' : 'not live yet'}.</p>
      )}

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
                <span className={`status-label ${member.status === 'active' ? 'complete' : member.status === 'invited' ? 'pending' : 'blocked'}`}>{memberStatusCopy[member.status]}</span>
                <span className="muted">{member.status === 'invited' ? `Invited ${member.invitedAt ? relativeTime(member.invitedAt) : ''}` : member.lastActiveAt ? relativeTime(member.lastActiveAt) : '—'}</span>
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
            {workspace.authDelegated && <p className="mutation-message">Invites are disabled while authentication is delegated to the reverse proxy.</p>}
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
