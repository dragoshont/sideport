import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, fn, userEvent, waitFor, within } from 'storybook/test'
import { AddIPhoneDialog, type AddIPhoneServices, type IPhoneSoundCue } from './AddFlows'
import '../App.css'

const soundCue = fn((cue: IPhoneSoundCue) => cue)
const waitingServices: AddIPhoneServices = {
  start: async () => ({ operationId: 'op-waiting-dragos', status: 'waiting', stages: [{ id: 'wait-for-usb', status: 'waiting', message: 'Waiting for one iPhone over USB.' }] }),
  read: async () => ({ operationId: 'op-waiting-dragos', status: 'waiting', stages: [{ id: 'wait-for-usb', status: 'waiting', message: 'Waiting for one iPhone over USB.' }] }),
}
const detectedServices: AddIPhoneServices = {
  start: waitingServices.start,
  read: async () => ({ operationId: 'op-waiting-dragos', status: 'succeeded', stages: [{ id: 'accept-device', status: 'succeeded', message: 'iPhone added.' }], result: { deviceEnrollment: { selectedDeviceUdid: '000081-dragos', inventoryState: 'accepted' } } }),
}

const meta = {
  title: 'Sideport/Add iPhone Dialog',
  component: AddIPhoneDialog,
  args: {
    open: true,
    onOpenChange: fn(),
    onContinue: fn(),
    demoMode: false,
    canMutate: true,
    memberName: 'Dragos',
    services: waitingServices,
    soundPlayer: soundCue,
  },
  parameters: { layout: 'fullscreen' },
} satisfies Meta<typeof AddIPhoneDialog>

export default meta
type Story = StoryObj<typeof meta>

export const WaitingForDragos: Story = {
  args: { attentionDelayMs: 60_000 },
  play: async ({ canvasElement }) => {
    soundCue.mockClear()
    const page = within(canvasElement.ownerDocument.body)
    const dialog = page.getByRole('dialog', { name: 'Add an iPhone' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Connect iPhone' }))
    await expect(within(dialog).getByText('Waiting for Dragos’s iPhone…')).toBeVisible()
    await expect(within(dialog).getByRole('button', { name: 'Continue' })).toBeDisabled()
    await expect(soundCue).toHaveBeenCalledWith('listening')
  },
}

export const StillWaitingForDragos: Story = {
  args: { attentionDelayMs: 20 },
  play: async ({ canvasElement }) => {
    soundCue.mockClear()
    const page = within(canvasElement.ownerDocument.body)
    const dialog = page.getByRole('dialog', { name: 'Add an iPhone' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Connect iPhone' }))
    await waitFor(() => expect(within(dialog).getByText(/Still waiting/)).toBeVisible())
    await expect(within(dialog).getByText(/data-capable USB cable/)).toBeVisible()
    await expect(soundCue).toHaveBeenCalledWith('attention')
  },
}

export const DragosIPhoneDetected: Story = {
  args: { services: detectedServices },
  play: async ({ canvasElement }) => {
    soundCue.mockClear()
    const page = within(canvasElement.ownerDocument.body)
    const dialog = page.getByRole('dialog', { name: 'Add an iPhone' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Connect iPhone' }))
    await waitFor(() => expect(within(dialog).getByText('Dragos’s iPhone is ready')).toBeVisible(), { timeout: 3_000 })
    await expect(within(dialog).getByRole('button', { name: 'Continue' })).toBeEnabled()
    await expect(soundCue).toHaveBeenCalledWith('detected')
  },
}
