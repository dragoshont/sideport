import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, userEvent, waitFor, within } from 'storybook/test'
import { CanonicalSideport } from './canonical/CanonicalSideport'

const meta = {
  title: 'Sideport/Canonical Product',
  component: CanonicalSideport,
  parameters: {
    docs: {
      description: {
        component: 'Canonical Storybook-only proposal for Sideport’s four-job product shell, secondary inbox/account surfaces, secure owner/invitation handoff, and one-cable iPhone journey. Fixtures make no live account, device, app, or infrastructure changes.',
      },
    },
  },
} satisfies Meta<typeof CanonicalSideport>

export default meta

type Story = StoryObj<typeof meta>

export const OwnerHome: Story = {
  name: '01 Shell · owner home',
  args: { experience: 'shell', role: 'owner', initialRoute: 'home' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const shell = canvas.getByTestId('canonical-signed-in-shell')
    const navigation = within(shell).getByRole('navigation', { name: 'Sideport navigation' })
    const destinations = within(navigation).getAllByRole('button').map((button) => button.textContent?.trim())

    await expect(destinations).toEqual(['Home', 'Apps', 'Devices', 'People'])
    await expect(within(navigation).queryByRole('button', { name: /Onboarding|Renewals|Operations|Diagnostics|Apple Access|Teams|Users|Install App/i })).not.toBeInTheDocument()
    await expect(canvas.getByRole('heading', { name: 'Good evening, Dragos' })).toBeVisible()
    await expect(canvas.getByText('One thing to do')).toBeVisible()
    await expect(canvas.getByText('Connect Sam’s iPhone')).toBeVisible()
    await expect(canvas.getByRole('button', { name: /Activity/ })).toBeVisible()
    await expect(canvas.getByText(/Storybook fixture/)).toBeVisible()
    await userEvent.click(canvas.getByRole('button', { name: /Connect Sam’s iPhone/ }))
    await expect(canvas.getByRole('heading', { name: 'Sam’s iPhone' })).toBeVisible()
    await expect(canvas.getByText('Connect and unlock this iPhone')).toBeVisible()
    await userEvent.click(within(navigation).getByRole('button', { name: 'People' }))
    await userEvent.click(canvas.getByRole('button', { name: /Mara Member/ }))
    await expect(canvas.getByRole('heading', { name: 'Mara' })).toBeVisible()
    await expect(canvas.getByRole('heading', { name: 'Mara’s iPhone' })).toBeVisible()
  },
}

export const OwnerSigningReplacement: Story = {
  name: '13 Settings · exact signing replacement impact',
  args: { experience: 'shell', role: 'owner', initialRoute: 'settings' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const page = within(canvasElement.ownerDocument.body)
    await userEvent.click(canvas.getByRole('button', { name: 'Review Apple signing' }))
    const dialog = page.getByRole('dialog', { name: 'Review Apple signing' })
    await expect(within(dialog).getByText(/one Apple account and one team/)).toBeVisible()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Change account or team' }))
    await expect(within(dialog).getByLabelText('Password')).toHaveAttribute('autocomplete', 'current-password')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Check current signing' }))
    await expect(within(dialog).getByText('Certificate ending A1B2 will be revoked')).toBeVisible()
    await expect(within(dialog).getByRole('button', { name: 'Replace signing identity' })).toBeDisabled()
    await userEvent.click(within(dialog).getByRole('checkbox'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Replace signing identity' }))
    await waitFor(() => expect(within(dialog).getByRole('heading', { name: 'New signing identity verified' })).toBeVisible(), { timeout: 2_000 })
  },
}

export const MemberHome: Story = {
  name: '02 Shell · member scope and safe projections',
  args: { experience: 'shell', role: 'member', initialRoute: 'home', memberName: 'Mara' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const navigation = canvas.getByRole('navigation', { name: 'Sideport navigation' })
    await expect(within(navigation).getAllByRole('button')).toHaveLength(4)

    await expect(canvas.queryByRole('button', { name: 'Add' })).not.toBeInTheDocument()
    await expect(canvas.queryByRole('button', { name: /Add another iPhone/ })).not.toBeInTheDocument()

    await userEvent.click(within(navigation).getByRole('button', { name: 'Devices' }))
    await expect(canvas.queryByRole('button', { name: 'Add iPhone' })).not.toBeInTheDocument()
    await expect(canvas.getByText('Need another iPhone?')).toBeVisible()

    await userEvent.click(canvas.getByRole('button', { name: /Activity/ }))
    await expect(canvas.queryByRole('button', { name: /technical details/i })).not.toBeInTheDocument()
    await expect(canvas.queryByText(/network-usbmux|onboarding_v2|op_refresh_01/)).not.toBeInTheDocument()

    await userEvent.click(canvas.getByRole('button', { name: /M Mara Member/ }))
    await expect(canvas.getByRole('heading', { name: 'Settings' })).toBeVisible()
    await expect(canvas.queryByRole('heading', { name: 'Signing' })).not.toBeInTheDocument()
  },
}

export const FreshDeployment: Story = {
  name: '03 Setup · fresh deployment outside shell',
  args: { experience: 'first-run', role: 'owner' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByRole('heading', { name: 'Welcome to Sideport' })).toBeVisible()
    await expect(canvas.queryByRole('navigation', { name: 'Sideport navigation' })).not.toBeInTheDocument()
    await expect(canvas.getByText('Sideport is running')).toBeVisible()
    await expect(canvas.getByText(/Apple signing/)).toBeVisible()
    await expect(canvas.getByText(/First iPhone and app/)).toBeVisible()

    await userEvent.click(canvas.getByRole('button', { name: 'How this installation is saved' }))
    await expect(canvas.getByText(/Docker keeps Sideport state/)).toBeVisible()
    await expect(canvas.getByText(/proposed Apple Container path is experimental/)).toBeVisible()
    await userEvent.click(canvas.getByRole('button', { name: 'Hide installation details' }))
  },
}

export const OwnerClaimSetup: Story = {
  name: '02b Setup · direct Owner passkey',
  args: { experience: 'owner-claim', ownerClaimState: 'setup', role: 'owner' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByRole('heading', { name: 'Finish setting up Sideport' })).toBeVisible()
    await expect(canvas.getByText(/no setup token or link to copy/)).toBeVisible()
    await expect(canvas.queryByLabelText(/API key|recovery key/i)).not.toBeInTheDocument()
    const create = canvas.getByRole('button', { name: 'Create passkey' })
    await expect(create).toBeDisabled()
    await userEvent.type(canvas.getByRole('textbox', { name: 'Name' }), 'Dragos')
    await userEvent.type(canvas.getByRole('textbox', { name: 'Email' }), 'dragos@example.test')
    await expect(create).toBeEnabled()
    await userEvent.click(create)
    await expect(canvas.getByRole('heading', { name: 'Welcome to Sideport' })).toBeVisible()
  },
}

export const OwnerClaimSetupPreview: Story = {
  name: '02b.1 Setup · owner claim preview',
  args: { experience: 'owner-claim', ownerClaimState: 'setup', role: 'owner' },
}

export const OwnerClaimRecovery: Story = {
  name: '02c Recovery · replace inaccessible owner',
  args: { experience: 'owner-claim', ownerClaimState: 'recovery', role: 'owner' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByRole('heading', { name: 'Recover Sideport owner access' })).toBeVisible()
    await expect(canvas.getByText('The current owner will be signed out.')).toBeVisible()
    await expect(canvas.getByText(/Apps, iPhones, signing state, and activity are retained/)).toBeVisible()
    await userEvent.click(canvas.getByRole('button', { name: 'Continue to sign in' }))
    await expect(canvas.getByRole('heading', { name: 'Recover owner access?' })).toBeVisible()
    await expect(canvas.getByText('Mara · mara@example.test')).toBeVisible()
    await expect(canvas.getByText(/Dragos will lose Owner access and be signed out/)).toBeVisible()
    await expect(canvas.getByText(/2 Members, 2 iPhones, 3 installed apps/)).toBeVisible()
    await userEvent.click(canvas.getByRole('button', { name: 'Recover owner access' }))
    await expect(canvas.getByRole('heading', { name: 'Good evening, Dragos' })).toBeVisible()
  },
}

export const OwnerClaimRecoveryPreview: Story = {
  name: '02c.1 Recovery · owner claim preview',
  args: { experience: 'owner-claim', ownerClaimState: 'recovery', role: 'owner' },
}

export const OwnerAppleSigningIsSeparateFromMemberLogin: Story = {
  name: '04 Setup · owner signing is not member login',
  args: { experience: 'first-run', role: 'owner' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await userEvent.click(canvas.getByRole('button', { name: 'Connect Apple account' }))
    await expect(canvas.getByRole('heading', { name: 'Connect your Apple account' })).toBeVisible()
    await expect(canvas.getByText(/separate from member sign-in/)).toBeVisible()
    await expect(canvas.getByText(/Team selection is automatic/)).toBeVisible()
    await expect(canvas.getByText(/do not enter real credentials/)).toBeVisible()
    await expect(canvas.getByLabelText('Demo Apple Account email')).toHaveAttribute('autocomplete', 'off')
    await expect(canvas.getByLabelText('Demo password')).toHaveAttribute('autocomplete', 'off')
    await expect(canvas.getByRole('button', { name: 'Continue demo' })).toBeEnabled()
  },
}

export const MemberInvitation: Story = {
  name: '05 People · invitation and signed-account consent',
  args: { experience: 'invitation', role: 'member', memberName: 'Mara' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByRole('heading', { name: 'Dragos invited you to Sideport' })).toBeVisible()
    await expect(canvas.getByText(/Face ID, Touch ID, Windows Hello/)).toBeVisible()
    await expect(canvas.getByText(/not official/)).toBeVisible()
    await expect(canvas.getByText(/No invitation, sign-in, account, device, app, or audio action occurs/)).toBeVisible()
    await expect(canvas.queryByLabelText(/password/i)).not.toBeInTheDocument()
    await expect(canvas.queryByRole('navigation', { name: 'Sideport navigation' })).not.toBeInTheDocument()
    await userEvent.click(canvas.getByRole('button', { name: 'Continue to sign in' }))
    await expect(canvas.getByRole('heading', { name: 'Join Dragos’s Sideport?' })).toBeVisible()
    await expect(canvas.getByText('Mara · mara@example.test')).toBeVisible()
    await expect(canvas.getByRole('button', { name: 'Join Sideport' })).toBeVisible()
    await expect(canvas.getByText(/not kept in browser storage/)).toBeVisible()
  },
}

export const MemberInvitationExpired: Story = {
  name: '05b People · invitation expired',
  args: { experience: 'invitation', invitationState: 'expired', role: 'member' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByRole('heading', { name: 'Ask Dragos for a new link' })).toBeVisible()
    await expect(canvas.getByText('A new invitation is required.')).toBeVisible()
    await expect(canvas.queryByRole('button', { name: /passkey/i })).not.toBeInTheDocument()
  },
}

export const MemberInvitationAlreadyUsed: Story = {
  name: '05c People · invitation already used',
  args: { experience: 'invitation', invitationState: 'used', role: 'member' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByRole('heading', { name: 'This invitation has already been used' })).toBeVisible()
    await expect(canvas.getByRole('button', { name: 'Continue to sign in' })).toBeVisible()
    await expect(canvas.getByText(/confirms the signed-in account before showing membership/)).toBeVisible()
  },
}

export const MemberAccessSuspended: Story = {
  name: '05d People · access suspended',
  args: { experience: 'invitation', invitationState: 'suspended', role: 'member' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByRole('heading', { name: 'Your Sideport access is paused' })).toBeVisible()
    await expect(canvas.getByText('The owner must restore access.')).toBeVisible()
    await expect(canvas.queryByRole('button', { name: /passkey/i })).not.toBeInTheDocument()
  },
}

export const MemberPasskeyRecovery: Story = {
  name: '05e People · Authentik-owned recovery',
  args: { experience: 'invitation', invitationState: 'recovery', role: 'member' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByRole('heading', { name: 'Recover your Authentik sign-in' })).toBeVisible()
    await expect(canvas.getByRole('button', { name: 'Continue to sign-in recovery' })).toBeVisible()
    await expect(canvas.getByText(/Sideport cannot create, reset, or read your passkey/)).toBeVisible()
    await userEvent.click(canvas.getByRole('button', { name: 'Continue to sign-in recovery' }))
    await expect(canvas.getByRole('heading', { name: 'Good evening, Mara' })).toBeVisible()
    await expect(canvas.queryByRole('heading', { name: 'Connect the iPhone' })).not.toBeInTheDocument()
  },
}

export const FullMemberOneCableJourney: Story = {
  name: '06 Member · invite to verified install',
  args: { experience: 'invitation', role: 'member', memberName: 'Mara' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await userEvent.click(canvas.getByRole('button', { name: 'Continue to sign in' }))
    await expect(canvas.getByRole('heading', { name: 'Join Dragos’s Sideport?' })).toBeVisible()
    await userEvent.click(canvas.getByRole('button', { name: 'Join Sideport' }))
    await expect(canvas.getByRole('heading', { name: 'Connect the iPhone' })).toBeVisible()
    await expect(canvas.getByText(/Tap “Trust” on the iPhone/)).toBeVisible()
    await expect(canvas.queryByRole('button', { name: /Pair|Add to Sideport/ })).not.toBeInTheDocument()

    await userEvent.click(canvas.getByRole('button', { name: 'Start connecting' }))
    await expect(canvas.getByRole('heading', { name: 'Unlock it and tap Trust' })).toBeVisible()
    await waitFor(() => expect(canvas.getByRole('heading', { name: 'Turn on Developer Mode' })).toBeVisible(), { timeout: 2500 })

    await userEvent.click(canvas.getByRole('button', { name: 'I restarted and reconnected' }))
    await expect(canvas.getByRole('heading', { name: 'Choose an app' })).toBeVisible()
    await expect(canvas.getByText('Automatic refresh is on')).toBeVisible()
    await userEvent.click(canvas.getByRole('radio', { name: /Dice Roll/ }))
    await userEvent.click(canvas.getByRole('button', { name: 'Install Dice Roll' }))
    await expect(canvas.getByRole('heading', { name: 'Installing Dice Roll' })).toBeVisible()
    await waitFor(() => expect(canvas.getByRole('heading', { name: 'Installed — you can unplug' })).toBeVisible(), { timeout: 3000 })
    await expect(canvas.getByText(/Paired Wi-Fi attempted/)).toBeVisible()
    await expect(canvas.getByText(/Cable remains the reliable fallback/)).toBeVisible()
    await expect(canvas.getByText(/Sideport verifies installation, not successful launch/)).toBeVisible()

    await userEvent.click(canvas.getByRole('button', { name: 'Open Sideport' }))
    await expect(canvas.getByRole('navigation', { name: 'Sideport navigation' })).toBeVisible()
    await expect(canvas.getByRole('heading', { name: 'Good evening, Mara' })).toBeVisible()
  },
}

export const AddIPhoneFromSignedInShell: Story = {
  name: '07 Add · one shared iPhone assistant',
  args: { experience: 'shell', role: 'owner', initialRoute: 'activity' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const addTrigger = canvas.getByRole('button', { name: 'Add' })
    await userEvent.click(addTrigger)
    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(addTrigger).toHaveFocus())
    await userEvent.click(addTrigger)
    await userEvent.click(canvas.getByRole('button', { name: /Add iPhone/ }))
    await expect(canvas.getByRole('heading', { name: 'Who will use this iPhone?' })).toBeVisible()
    await expect(canvas.queryByRole('heading', { name: 'Connect the iPhone' })).not.toBeInTheDocument()
    await userEvent.click(canvas.getByRole('button', { name: 'Mara' }))
    await expect(canvas.getByRole('heading', { name: 'Connect the iPhone' })).toBeVisible()
    await expect(canvas.queryByRole('navigation', { name: 'Sideport navigation' })).not.toBeInTheDocument()
    await expect(canvas.getByText('Step 1 of 3')).toBeVisible()
  },
}

export const AppsLibraryAndSources: Story = {
  name: '08 Apps · approved library and sources',
  args: { experience: 'shell', role: 'owner', initialRoute: 'apps' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByRole('heading', { name: 'Apps' })).toBeVisible()
    await expect(canvas.getByRole('tab', { name: 'Your apps' })).toHaveAttribute('aria-selected', 'true')
    await expect(canvas.getByRole('tab', { name: 'Browse' })).toBeVisible()
    await userEvent.click(canvas.getByRole('tab', { name: 'Browse' }))
    await expect(canvas.getByRole('tab', { name: 'Browse' })).toHaveAttribute('aria-selected', 'true')
    await userEvent.click(canvas.getByRole('tab', { name: 'Your apps' }))
    await expect(canvas.getByRole('button', { name: /Dice Roll/ })).toBeVisible()
    await expect(canvas.getByRole('button', { name: 'Manage sources' })).toBeVisible()

    await userEvent.click(canvas.getByRole('button', { name: 'Manage sources' }))
    const sourceGroup = canvas.getByRole('group', { name: 'IPA source' })
    await expect(within(sourceGroup).getByRole('button', { name: /This computer/ })).toBeVisible()
    await expect(within(sourceGroup).getByRole('button', { name: /On this Sideport/ })).toBeVisible()
    await userEvent.click(within(sourceGroup).getByRole('button', { name: /GitHub release/ }))
    await expect(canvas.getByText('Metadata')).toBeVisible()
    await expect(canvas.getByText('Contents')).toBeVisible()
    await expect(canvas.getByText('Write access')).toBeVisible()
    await expect(canvas.getByText('None')).toBeVisible()
    await userEvent.click(canvas.getByRole('button', { name: 'Close app import' }))

    const appSearch = canvas.getByRole('searchbox', { name: 'Search apps' })
    await userEvent.type(appSearch, 'dice')
    await expect(canvas.getByRole('button', { name: /Dice Roll/ })).toBeVisible()
    await expect(canvas.queryByRole('button', { name: /Cert Clock/ })).not.toBeInTheDocument()
    await userEvent.clear(appSearch)
    await userEvent.click(canvas.getByRole('button', { name: /Dice Roll/ }))
    await expect(canvas.getByRole('heading', { name: 'Dice Roll' })).toBeVisible()
    await expect(canvas.getByText('Where this app is installed')).toBeVisible()
    await userEvent.click(within(canvas.getByRole('main')).getByRole('button', { name: 'Apps' }))
    await expect(canvas.getByRole('heading', { name: 'Apps' })).toBeVisible()
  },
}

export const GlobalSearchKeyboardAndFocus: Story = {
  name: '09 Search · keyboard close and focus restoration',
  args: { experience: 'shell', role: 'owner', initialRoute: 'home' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const trigger = canvas.getByRole('button', { name: 'Search Sideport' })
    await userEvent.keyboard('{Control>}k{/Control}')
    const dialog = canvas.getByRole('dialog', { name: 'Search Sideport' })
    const searchbox = within(dialog).getByRole('searchbox', { name: 'Search Sideport' })
    await expect(searchbox).toHaveFocus()
    await userEvent.keyboard('{Shift>}{Tab}{/Shift}')
    const dialogButtons = within(dialog).getAllByRole('button')
    await expect(dialogButtons[dialogButtons.length - 1]).toHaveFocus()
    await userEvent.tab()
    await expect(searchbox).toHaveFocus()
    await userEvent.type(searchbox, 'Cert')
    await expect(within(dialog).getByRole('button', { name: /App Cert Clock Version/ })).toBeVisible()
    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(trigger).toHaveFocus())
  },
}

export const MultiUserActivity: Story = {
  name: '09b Activity · people, devices, apps, and attention',
  args: { experience: 'shell', role: 'owner', initialRoute: 'activity' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByRole('heading', { name: 'Activity' })).toBeVisible()
    await expect(canvas.getByText('Sam’s iPhone needs the cable')).toBeVisible()
    await expect(canvas.getByText('Cert Clock updated on Mara’s iPhone')).toBeVisible()
    await expect(canvas.getByText('Alex’s iPhone came home')).toBeVisible()
    await expect(canvas.getByText('Sam joined Sideport')).toBeVisible()
    const filters = canvas.getByRole('group', { name: 'Activity filters' })
    await userEvent.click(within(filters).getByRole('button', { name: 'Apps' }))
    await expect(canvas.getByText('Dice Roll 0.1.1 became available')).toBeVisible()
    await expect(canvas.queryByText('Sam’s iPhone needs the cable')).not.toBeInTheDocument()
  },
}

export const ContinuousUsbMonitoring: Story = {
  name: '09c Devices · continuous USB monitor and three owners',
  args: { experience: 'shell', role: 'owner', initialRoute: 'devices' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByText('Watching the Sideport cable')).toBeVisible()
    await expect(canvas.getByRole('button', { name: /Mara’s iPhone/ })).toBeVisible()
    await expect(canvas.getByRole('button', { name: /Alex’s iPhone/ })).toBeVisible()
    await expect(canvas.getByRole('button', { name: /Sam’s iPhone/ })).toBeVisible()
    await expect(canvas.getByText('Dice Roll update waiting')).toBeVisible()
    await userEvent.click(canvas.getByRole('button', { name: /Sam’s iPhone/ }))
    await expect(canvas.getByRole('heading', { name: 'Sam’s iPhone' })).toBeVisible()
    await expect(canvas.getByText('Connect and unlock this iPhone')).toBeVisible()
  },
}

export const OneTapInstallFromApps: Story = {
  name: '10 Apps · one Install action starts work',
  args: { experience: 'shell', role: 'member', initialRoute: 'apps', memberName: 'Mara' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await userEvent.click(canvas.getByRole('button', { name: /Dice Roll/ }))
    await expect(canvas.getByRole('heading', { name: 'Dice Roll' })).toBeVisible()
    await userEvent.click(canvas.getByRole('button', { name: 'Update on Mara’s iPhone' }))
    await expect(canvas.getByRole('heading', { name: 'Connect the iPhone to install Dice Roll' })).toBeVisible()
    await expect(canvas.queryByRole('heading', { name: 'Choose an app' })).not.toBeInTheDocument()
    await waitFor(() => expect(canvas.getByRole('heading', { name: 'Installing Dice Roll' })).toBeVisible(), { timeout: 2500 })
  },
}

export const KeyboardOnlyAppChoice: Story = {
  name: '10b Accessibility · keyboard app choice',
  args: { experience: 'add-iphone', initialAssistantStep: 'choose', role: 'member', memberName: 'Mara' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const radios = canvas.getAllByRole('radio')
    await expect(radios).toHaveLength(3)
    await userEvent.tab()
    await expect(radios[0]).toHaveFocus()
    await userEvent.keyboard('{ArrowDown}')
    await expect(radios[1]).toBeChecked()
    await expect(canvas.getByRole('button', { name: 'Install Dice Roll' })).toBeVisible()
  },
}

export const VerifiedSafeToUnplug: Story = {
  name: '11 Install · verified and safe to unplug',
  args: { experience: 'add-iphone', role: 'member', initialAssistantStep: 'done', memberName: 'Mara' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByRole('heading', { name: 'Installed — you can unplug' })).toBeVisible()
    await expect(canvas.getByText('Completion chime would play')).toBeVisible()
    await expect(canvas.getByText('Best effort when the browser allows audio')).toBeVisible()
    await expect(canvas.getByText('Device verification represented')).toBeVisible()
  },
}

export const MobileMemberJourney390: Story = {
  name: '12 Mobile · member invitation at 390px',
  args: { experience: 'invitation', role: 'member', memberName: 'Mara' },
  parameters: {
    viewport: {
      defaultViewport: 'sideportPhone390',
      options: {
        sideportPhone390: { name: 'Sideport phone 390px', styles: { width: '390px', height: '844px' }, type: 'mobile' },
      },
    },
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByRole('heading', { name: 'Dragos invited you to Sideport' })).toBeVisible()
    await expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth)
  },
}

export const MobileOneCableJourney390: Story = {
  name: '12b Mobile · complete one-cable journey at 390px',
  args: { experience: 'add-iphone', initialAssistantStep: 'connect', role: 'member', memberName: 'Mara' },
  parameters: {
    viewport: {
      defaultViewport: 'sideportPhone390',
      options: {
        sideportPhone390: { name: 'Sideport phone 390px', styles: { width: '390px', height: '844px' }, type: 'mobile' },
      },
    },
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth)
    await userEvent.click(canvas.getByRole('button', { name: 'Start connecting' }))
    await waitFor(() => expect(canvas.getByRole('heading', { name: 'Turn on Developer Mode' })).toBeVisible(), { timeout: 2500 })
    await expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth)
    await userEvent.click(canvas.getByRole('button', { name: 'I restarted and reconnected' }))
    await userEvent.click(canvas.getByRole('button', { name: 'Install Cert Clock' }))
    await waitFor(() => expect(canvas.getByRole('heading', { name: 'Installed — you can unplug' })).toBeVisible(), { timeout: 3000 })
    await expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth)
  },
}

export const MobileShell320Reflow: Story = {
  name: '13 Accessibility · shell reflows at 320px',
  args: { experience: 'shell', role: 'member', initialRoute: 'apps' },
  parameters: {
    viewport: {
      defaultViewport: 'sideportPhone320',
      options: {
        sideportPhone320: { name: 'Sideport phone 320px', styles: { width: '320px', height: '720px' }, type: 'mobile' },
      },
    },
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await expect(canvas.getByRole('heading', { name: 'Apps' })).toBeVisible()
    await expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth)
    const mobileNavigation = canvas.getByRole('navigation', { name: 'Mobile Sideport navigation' })
    await expect(within(mobileNavigation).getAllByRole('button')).toHaveLength(4)
    await expect(within(mobileNavigation).getByRole('button', { name: 'Apps' })).toHaveAttribute('aria-current', 'page')
  },
}
