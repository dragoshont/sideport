import type { Meta, StoryObj } from '@storybook/react-vite'
import {
  SideportAdminApp,
  type RouteId,
} from './App'
import { blockedFixtures, emptyFixtures, fixtures } from './data/sideportFixtures'

const meta = {
  title: 'Sideport/Admin Shell',
  component: SideportAdminApp,
  parameters: {
    docs: {
      description: {
        component: 'Mock-first Sideport admin routes. Refresh, install, delete, and register actions are intentionally disabled in this prototype.',
      },
    },
  },
} satisfies Meta<typeof SideportAdminApp>

export default meta

type Story = StoryObj<typeof meta>

const routeStory = (initialRoute: RouteId, name: string): Story => ({
  name,
  args: { data: fixtures, initialRoute },
})

export const OverviewHealthy = routeStory('overview', 'Overview - healthy mixed fleet')
export const DeviceInventory = routeStory('devices', 'Devices - table and mobile cards')
export const DeviceDetailTwoApps = routeStory('device-detail', 'Device detail - two app slots')
export const AddAppPreflight = routeStory('add-app', 'Add app - mock preflight')
export const RenewalsSingleFlight = routeStory('renewals', 'Renewals - running and queued')
export const DiagnosticsTraceLinked = routeStory('diagnostics', 'Diagnostics - trace linked issues')
export const SettingsReadOnly = routeStory('settings', 'Settings - read-only status')

export const EmptyFleet: Story = {
  args: { data: emptyFixtures, initialRoute: 'devices' },
}

export const AnisetteBlocked: Story = {
  args: { data: blockedFixtures, initialRoute: 'overview' },
}
