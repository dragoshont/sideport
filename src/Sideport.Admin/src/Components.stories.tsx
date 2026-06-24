import type { Meta, StoryObj } from '@storybook/react-vite'
import {
  AppSlotGrid,
  DeviceCard,
  DiagnosticIssueList,
  RenewalQueueList,
  RoleBadge,
  SourcePill,
  StatusPill,
} from './App'
import { fixtures } from './data/sideportFixtures'
import type { AdminDataStatus } from './api/sideportApi'
import type { DeviceSummary, HealthState, RegisteredAppSummary, SourceKind, WorkspaceRole } from './data/sideportTypes'

const meta: Meta = {
  title: 'Sideport/Components',
  decorators: [
    (Story) => (
      <div className="story-pad">
        <Story />
      </div>
    ),
  ],
  parameters: {
    layout: 'fullscreen',
    docs: {
      description: {
        component: 'Per-component state matrix from the design spec. Each story exercises a hard-to-reach state in isolation, so empty / blocked / failed / full paths can be reviewed without driving the whole shell.',
      },
    },
  },
}

export default meta

type Story = StoryObj<typeof meta>

const liveStatus: AdminDataStatus = { mode: 'live', baseUrl: '/sideport-api', message: 'Live API', canMutate: true }

const appsFor = (udid: string): RegisteredAppSummary[] =>
  fixtures.apps.filter((app) => app.deviceUdid === udid).map((app) => ({ ...app, deviceUdid: udid }))

const nApps = (count: number, udid: string): RegisteredAppSummary[] =>
  fixtures.apps.slice(0, count).map((app, index) => ({ ...app, deviceUdid: udid, bundleId: `${app.bundleId}.${index}` }))

const wifiDevice = fixtures.devices[0]
const usbDevice = fixtures.devices[1]
const offlineDevice = fixtures.devices[2]
const blockedDevice: DeviceSummary = { ...wifiDevice, udid: 'BLOCKED-UDID', name: 'Blocked iPhone', health: 'blocked', connection: 'usb', blocker: 'Provisioning profile expired.' }
const fullDevice: DeviceSummary = { ...usbDevice, udid: 'FULL-UDID', name: 'Full iPhone', appSlotsUsed: 3 }

// ---------- DeviceCard ----------
export const DeviceCardWifi: Story = { name: 'DeviceCard — Wi-Fi', render: () => <div className="story-card"><DeviceCard device={wifiDevice} apps={appsFor(wifiDevice.udid)} /></div> }
export const DeviceCardUsb: Story = { name: 'DeviceCard — USB', render: () => <div className="story-card"><DeviceCard device={usbDevice} apps={appsFor(usbDevice.udid)} /></div> }
export const DeviceCardOffline: Story = { name: 'DeviceCard — offline', render: () => <div className="story-card"><DeviceCard device={offlineDevice} apps={[]} /></div> }
export const DeviceCardBlocked: Story = { name: 'DeviceCard — blocked', render: () => <div className="story-card"><DeviceCard device={blockedDevice} apps={appsFor(wifiDevice.udid)} /></div> }
export const DeviceCardFullSlots: Story = { name: 'DeviceCard — 3/3 slots', render: () => <div className="story-card"><DeviceCard device={fullDevice} apps={nApps(3, fullDevice.udid)} /></div> }

// ---------- AppSlotGrid ----------
export const SlotsEmpty: Story = { name: 'AppSlotGrid — 0/3 empty', render: () => <AppSlotGrid apps={[]} canRegister /> }
export const SlotsPartial: Story = { name: 'AppSlotGrid — 1/3 used', render: () => <AppSlotGrid apps={nApps(1, 'demo-device')} canRegister /> }
export const SlotsFull: Story = { name: 'AppSlotGrid — 3/3 full', render: () => <AppSlotGrid apps={nApps(3, 'demo-device')} canRegister={false} /> }

// ---------- RenewalQueueList ----------
export const RenewalRunningQueued: Story = { name: 'RenewalQueue — operation states', render: () => <RenewalQueueList items={fixtures.renewals} apps={fixtures.apps} apiStatus={liveStatus} /> }
export const RenewalBlocked: Story = { name: 'RenewalQueue — blocked', render: () => <RenewalQueueList items={fixtures.renewals.filter((item) => item.risk === 'blocked')} apps={fixtures.apps} apiStatus={liveStatus} /> }
export const RenewalEmpty: Story = { name: 'RenewalQueue — empty', render: () => <RenewalQueueList items={[]} apps={fixtures.apps} apiStatus={liveStatus} /> }

// ---------- DiagnosticIssueList ----------
export const IssuesAll: Story = { name: 'Diagnostics — all categories', render: () => <DiagnosticIssueList issues={fixtures.issues} /> }
export const IssueInstallFailed: Story = { name: 'Diagnostics — install failed', render: () => <DiagnosticIssueList issues={fixtures.issues.filter((issue) => issue.category.includes('Install'))} /> }
export const IssueResolved: Story = { name: 'Diagnostics — resolved', render: () => <DiagnosticIssueList issues={fixtures.issues.filter((issue) => issue.status === 'resolved')} /> }

// ---------- Atoms ----------
const healthStates: HealthState[] = ['healthy', 'warning', 'blocked', 'failed', 'offline']
const sources: SourceKind[] = ['live', 'derived', 'demo', 'planned']
const roles: WorkspaceRole[] = ['owner', 'admin', 'operator', 'viewer']

export const StatusPills: Story = { name: 'StatusPill — all states', render: () => <div className="story-row">{healthStates.map((state) => <StatusPill key={state} state={state} label={state} />)}</div> }
export const SourcePills: Story = { name: 'SourcePill — all sources', render: () => <div className="story-row">{sources.map((source) => <SourcePill key={source} source={source} label={source} />)}</div> }
export const RoleBadges: Story = { name: 'RoleBadge — all roles', render: () => <div className="story-row">{roles.map((role) => <RoleBadge key={role} role={role} />)}</div> }
