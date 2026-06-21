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
export const AppCatalogSeed = routeStory('catalog', 'App catalog - Cert Clock seed')
export const InstallWizardShell = routeStory('install-app', 'Install wizard shell - save registration')
export const RenewalsSingleFlight = routeStory('renewals', 'Renewals - running and queued')
export const AppleAccessProbe = routeStory('apple-access', 'Apple Access - read-only probe')
export const DiagnosticsTraceLinked = routeStory('diagnostics', 'Diagnostics - filters + trace linked issues')
export const TeamsView = routeStory('teams', 'Teams - Apple teams and workspace')
export const UsersRoles = routeStory('users', 'Users - roles, members, invite, audit')
export const SettingsSessionAccess = routeStory('settings', 'Settings - full control-plane sections')

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

export const CommandMenuOpen: Story = {
  name: 'Command menu - ⌘K search',
  args: { data: fixtures, apiStatus: demoStatus, initialRoute: 'overview', initialCommandOpen: true },
}

export const DeviceDetailTabbed: Story = {
  name: 'Device detail - tabs + working refresh',
  args: { data: fixtures, apiStatus: demoStatus, initialRoute: 'device-detail' },
}
