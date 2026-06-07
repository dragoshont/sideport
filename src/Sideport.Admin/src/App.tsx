import { useMemo, useState, type ReactNode } from 'react'
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
import {
  fixtures,
  type ActivityEvent,
  type DeviceSummary,
  type DiagnosticIssue,
  type HealthState,
  type RegisteredAppSummary,
  type RenewalItem,
  type RenewalRisk,
  type RenewalStatus,
  type SideportFixtureSet,
  type SourceKind,
  type SystemStatus,
} from './data/sideportFixtures'
import { compactUdid, relativeTime, shortDateTime, sourceLabel, timeUntil } from './lib/format'

export type RouteId = 'overview' | 'devices' | 'device-detail' | 'add-app' | 'renewals' | 'diagnostics' | 'settings'

const routeItems: Array<{ id: RouteId; label: string; icon: LucideIcon }> = [
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
  data?: SideportFixtureSet
  initialRoute?: RouteId
}

export function SideportAdminApp({ data = fixtures, initialRoute = 'overview' }: SideportAdminAppProps) {
  const [route, setRoute] = useState<RouteId>(initialRoute)
  const selectedDevice = data.devices[0]

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
          <span>Mock UI. Refresh/install actions are disabled until backend contracts are explicit.</span>
        </div>
      </aside>

      <div className="workspace">
        <TopBar system={data.system} />
        <main className="content-area">
          {route === 'overview' && <OverviewPage data={data} onNavigate={setRoute} />}
          {route === 'devices' && <DevicesPage data={data} onOpenDevice={() => setRoute('device-detail')} />}
          {route === 'device-detail' && <DeviceDetailPage data={data} device={selectedDevice} />}
          {route === 'add-app' && <AddAppPage data={data} />}
          {route === 'renewals' && <RenewalsPage data={data} />}
          {route === 'diagnostics' && <DiagnosticsPage data={data} />}
          {route === 'settings' && <SettingsPage system={data.system} />}
        </main>
      </div>
    </div>
  )
}

function TopBar({ system }: { system: SystemStatus }) {
  return (
    <header className="topbar">
      <div className="search-shell">
        <Search size={17} />
        <span>Search devices, bundle IDs, blockers</span>
      </div>
      <div className="topbar-actions">
        <SourcePill source="mock" label="Prototype" />
        <StatusPill state={system.ready.ready ? 'healthy' : 'blocked'} label={system.ready.ready ? 'Ready' : 'Not ready'} />
      </div>
    </header>
  )
}

export function OverviewPage({ data, onNavigate }: { data: SideportFixtureSet; onNavigate?: (route: RouteId) => void }) {
  const reachable = data.devices.filter((device) => device.connection !== 'offline').length
  const blocked = data.renewals.filter((item) => item.risk === 'blocked').length
  const due = data.renewals.filter((item) => item.risk === 'due-now').length
  const openIssues = data.issues.filter((issue) => issue.status !== 'resolved').length

  return (
    <div className="page-stack">
      <PageHeader
        eyebrow="Operations overview"
        title="Sideport health at a glance"
        description="Live API checks and mock read models are shown together, with source labels kept visible until the backend contracts catch up."
      />

      <section className="metric-grid" aria-label="Fleet health summary">
        <MetricCard icon={Smartphone} label="Reachable devices" value={`${reachable}/${data.devices.length}`} detail="Current API sees reachable devices only." source="live" tone="blue" />
        <MetricCard icon={TimerReset} label="Due soon" value={String(due)} detail="Inside configured renewal lead time." source="derived" tone="amber" />
        <MetricCard icon={AlertTriangle} label="Blocked" value={String(blocked)} detail="Needs operator action before refresh." source="mock" tone="red" />
        <MetricCard icon={Activity} label="Open issues" value={String(openIssues)} detail="Trace-linked diagnostics are mocked for now." source="planned" tone="green" />
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

export function DevicesPage({ data, onOpenDevice }: { data: SideportFixtureSet; onOpenDevice?: (device: DeviceSummary) => void }) {
  if (data.devices.length === 0) {
    return (
      <div className="page-stack">
        <PageHeader eyebrow="Devices" title="No devices known yet" description="Connect a trusted iPhone over USB or Wi-Fi. Persistent last-seen inventory is planned, so this empty state is fixture-driven." />
        <EmptyState icon={Cable} title="No devices returned by /api/devices" detail="The current API only lists reachable devices. Offline history needs the planned device observation store." />
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

export function DeviceDetailPage({ data, device }: { data: SideportFixtureSet; device?: DeviceSummary }) {
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
          <button className="primary-action" disabled type="button"><RefreshCw size={16} /> Mock refresh only</button>
        </div>
      </div>

      <section className="section-grid three">
        <FactTile label="Connection" value={connectionLabel(device.connection)} source="live" />
        <FactTile label="Last seen" value={relativeTime(device.lastSeenAt.value)} source={device.lastSeenAt.source} />
        <FactTile label="Nearest expiry" value={timeUntil(device.nearestExpiryAt?.value)} source={device.nearestExpiryAt?.source ?? 'mock'} />
      </section>

      <Panel title="App slots">
        <AppSlotGrid apps={apps} />
      </Panel>

      <div className="two-column-layout">
        <Panel title="Signing and network">
          <dl className="detail-list">
            <div><dt>Apple Developer Team</dt><dd>{device.teamId}</dd></div>
            <div><dt>Single-flight signer</dt><dd>Visible in renewals. Parallel refresh is intentionally blocked.</dd></div>
            <div><dt>Wi-Fi pairing</dt><dd>{device.connection === 'wifi' ? 'Reachable through host netmuxd/usbmuxd.' : 'Pairing history requires planned observation store.'}</dd></div>
          </dl>
        </Panel>
        <Panel title="Diagnostics for this device">
          {issues.length ? <DiagnosticIssueList issues={issues} compact /> : <EmptyState icon={CheckCircle2} title="No open device issues" detail="Trace-linked issue grouping is mocked until the diagnostics endpoint exists." />}
        </Panel>
      </div>
    </div>
  )
}

export function AddAppPage({ data }: { data: SideportFixtureSet }) {
  const firstDevice = data.devices[0]
  return (
    <div className="page-stack">
      <PageHeader eyebrow="Add app" title="Preflight before Sideport touches Apple services" description="This mock flow uses server IPA paths and manual IDs because upload, IPA inspection, and team discovery endpoints are not live yet." />
      <div className="wizard-grid">
        <Panel title="Target">
          <FieldPreview label="Device" value={firstDevice ? firstDevice.name : 'No reachable device'} source={firstDevice ? 'live' : 'mock'} />
          <FieldPreview label="Server IPA path" value="/var/lib/altserver/ipa/Example.ipa" source="mock" />
          <FieldPreview label="Apple Developer Team" value="M62Z4M5EUY" source="mock" />
        </Panel>
        <Panel title="Preflight">
          <PreflightList items={[
            ['Device is currently reachable', Boolean(firstDevice), firstDevice ? 'live' : 'mock'],
            ['App slot available on selected device', firstDevice ? firstDevice.appSlotsUsed < 3 : false, 'derived'],
            ['Signer binary ready', data.system.ready.checks.signer.ok, 'live'],
            ['Anisette identity trusted', data.system.ready.checks.anisette.ok, 'live'],
            ['IPA metadata inspection endpoint exists', false, 'planned'],
          ]} />
          <button className="primary-action wide" disabled type="button"><Play size={16} /> Register app is disabled in prototype</button>
        </Panel>
      </div>
    </div>
  )
}

export function RenewalsPage({ data }: { data: SideportFixtureSet }) {
  return (
    <div className="page-stack">
      <PageHeader eyebrow="Renewals" title="Renewal risk, not fake queue control" description="Current backend has last refresh state. Durable queue, cancel, and reorder are planned, so this page is fixture-backed and read-only." />
      <RenewalLane title="Blocked" items={data.renewals.filter((item) => item.risk === 'blocked')} apps={data.apps} />
      <RenewalLane title="Due now" items={data.renewals.filter((item) => item.risk === 'due-now')} apps={data.apps} />
      <RenewalLane title="Upcoming" items={data.renewals.filter((item) => item.risk === 'upcoming')} apps={data.apps} />
    </div>
  )
}

export function DiagnosticsPage({ data }: { data: SideportFixtureSet }) {
  return (
    <div className="page-stack">
      <PageHeader eyebrow="Diagnostics" title="OpenTelemetry-first failure evidence" description="Issues are mocked as future operation history. Today, live evidence comes from readiness checks and app lastError fields." />
      {data.issues.length ? <DiagnosticIssueList issues={data.issues} /> : <EmptyState icon={Stethoscope} title="No diagnostic issues" detail="When OpenTelemetry is wired, this page will group refresh/sign/install failures by operation and trace ID." />}
    </div>
  )
}

export function SettingsPage({ system }: { system: SystemStatus }) {
  return (
    <div className="page-stack">
      <PageHeader eyebrow="Settings" title="Read-only control plane status" description="Editable settings are intentionally not modeled yet. This page explains the runtime posture without exposing secrets." />
      <Panel title="Runtime checks">
        <SystemChecks system={system} />
      </Panel>
      <Panel title="Observability">
        <div className="observability-panel">
          <Network size={22} />
          <div>
            <h3>{system.observability.exporter}</h3>
            <p>OpenTelemetry export is planned. The UI will show trace links only when they explain a blocker or failed operation.</p>
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
      <CheckRow label="Scheduler" ok={!system.scheduler.enabled} source={system.scheduler.source} detail="Off for prototype / single-signer safety." />
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
        <FactTile label="Expiry" value={timeUntil(device.nearestExpiryAt?.value)} source={device.nearestExpiryAt?.source ?? 'mock'} />
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
          {app ? <AppSummary app={app} /> : <div className="empty-slot"><Plus size={18} /><span>Available</span><small>Mock add flow only</small></div>}
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

function RenewalLane({ title, items, apps }: { title: string; items: RenewalItem[]; apps: RegisteredAppSummary[] }) {
  return <Panel title={`${title} (${items.length})`}>{items.length ? <RenewalQueueList items={items} apps={apps} /> : <EmptyState icon={CheckCircle2} title={`No ${title.toLowerCase()} renewals`} detail="Nothing in this risk lane for the current fixture." />}</Panel>
}

function RenewalQueueList({ items, apps, compact = false }: { items: RenewalItem[]; apps: RegisteredAppSummary[]; compact?: boolean }) {
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
            {item.blocker && <p>{item.blocker}</p>}
            <SourcePill source={item.source} label={sourceLabel(item.source)} />
          </article>
        )
      })}
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

function PreflightList({ items }: { items: Array<[string, boolean, SourceKind]> }) {
  return <div className="check-list">{items.map(([label, ok, source]) => <CheckRow key={label} label={label} ok={ok} detail={ok ? 'Ready' : 'Not available yet'} source={source} />)}</div>
}

function FieldPreview({ label, value, source }: { label: string; value: string; source: SourceKind }) {
  return (
    <div className="field-preview">
      <span>{label}</span>
      <strong>{value}</strong>
      <SourcePill source={source} label={sourceLabel(source)} />
    </div>
  )
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
  return <SideportAdminApp />
}

export default App
