import type { Meta, StoryObj } from '@storybook/react-vite'
import type { AdminDataStatus } from './api/sideportApi'
import {
  SideportAdminApp,
  type RouteId,
} from './App'
import { blockedFixtures, emptyFixtures, fixtures } from './data/sideportFixtures'
import { runtimeEmptyData } from './data/sideportTypes'

const meta = {
  title: 'Sideport/Admin Shell',
  component: SideportAdminApp,
  parameters: {
    docs: {
      description: {
        component: 'Storybook renders the GitHub Pages demo portal with fixture data. The production bundle uses the live .NET API and keeps demo fixtures out of runtime imports.',
      },
    },
  },
} satisfies Meta<typeof SideportAdminApp>

export default meta

type Story = StoryObj<typeof meta>

const demoStatus: AdminDataStatus = {
  mode: 'demo',
  baseUrl: 'storybook://demo-data',
  message: 'Demo data for GitHub Pages and design review.',
  canMutate: false,
}

const apiUnavailableStatus: AdminDataStatus = {
  mode: 'unavailable',
  baseUrl: '/sideport-api',
  message: 'No Sideport API is reachable. Runtime pages stay empty until the .NET backend responds.',
  canMutate: false,
}

const tokenRequiredStatus: AdminDataStatus = {
  mode: 'partial',
  baseUrl: '/',
  message: 'Protected API calls are returning 401. Save the browser session token in Settings.',
  canMutate: true,
}

const routeStory = (initialRoute: RouteId, name: string): Story => ({
  name,
  args: { data: fixtures, apiStatus: demoStatus, initialRoute },
})

export const OverviewHealthy = routeStory('overview', 'Overview - healthy mixed fleet')
export const FirstRunOnboarding = routeStory('onboarding', 'Onboarding - first run checklist')
export const DeviceInventory = routeStory('devices', 'Devices - table and mobile cards')
export const DeviceDetailTwoApps = routeStory('device-detail', 'Device detail - two app slots')
export const AddAppPreflight = routeStory('add-app', 'Add app - demo registration form')
export const RenewalsSingleFlight = routeStory('renewals', 'Renewals - running and queued')
export const DiagnosticsTraceLinked = routeStory('diagnostics', 'Diagnostics - trace linked issues')
export const SettingsSessionAccess = routeStory('settings', 'Settings - session token and checks')

export const EmptyFleet: Story = {
  args: { data: emptyFixtures, apiStatus: demoStatus, initialRoute: 'devices' },
}

export const AnisetteBlocked: Story = {
  args: { data: blockedFixtures, apiStatus: demoStatus, initialRoute: 'overview' },
}

export const ApiUnavailableRuntime: Story = {
  args: { data: runtimeEmptyData, apiStatus: apiUnavailableStatus, initialRoute: 'onboarding' },
}

export const TokenRequiredSettings: Story = {
  args: { data: fixtures, apiStatus: tokenRequiredStatus, initialRoute: 'settings' },
}
