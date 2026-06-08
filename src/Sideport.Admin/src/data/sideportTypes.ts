export type SourceKind = 'live' | 'derived' | 'demo' | 'planned'
export type HealthState = 'healthy' | 'warning' | 'blocked' | 'failed' | 'offline'
export type ConnectionState = 'usb' | 'wifi' | 'offline'
export type RenewalRisk = 'blocked' | 'due-now' | 'upcoming' | 'healthy' | 'unknown'
export type RenewalStatus = 'idle' | 'running' | 'queued' | 'failed' | 'blocked'
export type Severity = 'info' | 'warning' | 'error' | 'fatal'
export type IssueStatus = 'unresolved' | 'investigating' | 'resolved' | 'ignored'

export interface SourceTagged<T> {
  value: T
  source: SourceKind
}

export interface SystemStatus {
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
  scheduler: { enabled: boolean; source: SourceKind }
  observability: { exporter: string; connected: boolean; source: SourceKind }
}

export interface DeviceSummary {
  udid: string
  name: string
  productType: string
  osVersion: string
  connection: ConnectionState
  lastSeenAt: SourceTagged<string>
  health: HealthState
  teamId: string
  appSlotsUsed: number
  nearestExpiryAt?: SourceTagged<string>
  blocker?: string
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
  displayName: SourceTagged<string>
  version: SourceTagged<string>
  iconTone: 'blue' | 'green' | 'amber' | 'red' | 'slate'
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

export interface SideportReadModel {
  system: SystemStatus
  devices: DeviceSummary[]
  apps: RegisteredAppSummary[]
  renewals: RenewalItem[]
  issues: DiagnosticIssue[]
  activity: ActivityEvent[]
  logs: OperationLogEntry[]
}

export const runtimeEmptyData: SideportReadModel = {
  system: {
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
  apps: [],
  renewals: [],
  issues: [],
  activity: [],
  logs: [],
}