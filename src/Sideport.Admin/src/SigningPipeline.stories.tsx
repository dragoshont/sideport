import type { Meta, StoryObj } from '@storybook/react-vite'
import { SigningPipeline, type PipelineStage } from './App'

const meta = {
  title: 'Sideport/Signing Pipeline',
  component: SigningPipeline,
  parameters: {
    docs: {
      description: {
        component:
          'The sign → install → verify operation view. This is the progress-stage surface the design spec asks for, replacing a bare spinner. It is reused by the install wizard (preview) and the Renewals single-flight strip (live operation).',
      },
    },
  },
  decorators: [
    (Story) => (
      <div className="story-pad">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof SigningPipeline>

export default meta

type Story = StoryObj<typeof meta>

const running: PipelineStage[] = [
  { id: 'authorize', label: 'Authorize', detail: 'GrandSlam login', state: 'done' },
  { id: 'provision', label: 'Provision', detail: 'App ID + profile', state: 'done' },
  { id: 'sign', label: 'Sign', detail: 'zsign re-sign', state: 'active' },
  { id: 'install', label: 'Install', detail: 'Push to device', state: 'pending' },
  { id: 'verify', label: 'Verify', detail: 'Launch check', state: 'pending' },
]

const failed: PipelineStage[] = [
  { id: 'authorize', label: 'Authorize', detail: 'GrandSlam login', state: 'done' },
  { id: 'provision', label: 'Provision', detail: 'App ID + profile', state: 'done' },
  { id: 'sign', label: 'Sign', detail: 'zsign re-sign', state: 'done' },
  { id: 'install', label: 'Install', detail: 'Device unreachable', state: 'failed' },
  { id: 'verify', label: 'Verify', detail: 'Launch check', state: 'pending' },
]

const complete: PipelineStage[] = [
  { id: 'authorize', label: 'Authorize', detail: 'GrandSlam login', state: 'done' },
  { id: 'provision', label: 'Provision', detail: 'App ID + profile', state: 'done' },
  { id: 'sign', label: 'Sign', detail: 'zsign re-sign', state: 'done' },
  { id: 'install', label: 'Install', detail: 'Push to device', state: 'done' },
  { id: 'verify', label: 'Verify', detail: 'Launch OK', state: 'done' },
]

export const Running: Story = {
  name: 'Running — signing in progress',
  args: { title: 'Refreshing Cert Clock', stages: running },
}

export const Failed: Story = {
  name: 'Failed — install step failed',
  args: {
    title: 'Refresh failed — Dice Roll',
    stages: failed,
    note: 'Install failed: the device became unreachable mid-install. Reconnect the iPhone and retry the operation.',
  },
}

export const Complete: Story = {
  name: 'Complete — signed and verified',
  args: { title: 'Cert Clock signed and verified', stages: complete },
}

export const NotStarted: Story = {
  name: 'Not started — wizard preview',
  args: {
    title: 'Operation preview',
    note: 'Saving a registration records intent only. These stages run when the refresh operation is wired to this device.',
  },
}
