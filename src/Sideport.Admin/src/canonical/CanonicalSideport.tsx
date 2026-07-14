import { useEffect, useId, useRef, useState, type FormEvent, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from 'react'
import {
  Activity,
  Apple,
  Cable,
  Check,
  CheckCircle2,
  ChevronRight,
  CircleUserRound,
  Download,
  FileUp,
  GitBranch,
  HardDrive,
  Home,
  Info,
  Laptop,
  Package,
  Plus,
  RotateCw,
  Search,
  Settings,
  ShieldCheck,
  Smartphone,
  Sparkles,
  Users,
  Volume2,
  Wifi,
  X,
  type LucideIcon,
} from 'lucide-react'
import './CanonicalSideport.css'

export type CanonicalRole = 'owner' | 'member'
export type CanonicalRoute = 'home' | 'apps' | 'devices' | 'people' | 'activity' | 'settings'
export type CanonicalExperience = 'shell' | 'invitation' | 'owner-claim' | 'first-run' | 'add-iphone'
export type CanonicalAssistantStep = 'connect' | 'waiting' | 'prepare' | 'choose' | 'install-waiting' | 'installing' | 'done'
export type CanonicalInvitationState = 'ready' | 'expired' | 'used' | 'suspended' | 'recovery'
export type CanonicalOwnerClaimState = 'setup' | 'recovery'

export interface CanonicalSideportProps {
  role?: CanonicalRole
  experience?: CanonicalExperience
  initialRoute?: CanonicalRoute
  initialAssistantStep?: CanonicalAssistantStep
  invitationState?: CanonicalInvitationState
  ownerClaimState?: CanonicalOwnerClaimState
  memberName?: string
}

interface NavigationItem {
  id: CanonicalRoute
  label: string
  icon: LucideIcon
}

interface LibraryApp {
  id: string
  name: string
  description: string
  version: string
  initials: string
  tone: 'blue' | 'amber' | 'green'
  source: string
  installedOn: string
  release: string
}

const NAVIGATION: readonly NavigationItem[] = [
  { id: 'home', label: 'Home', icon: Home },
  { id: 'apps', label: 'Apps', icon: Package },
  { id: 'devices', label: 'Devices', icon: Smartphone },
  { id: 'people', label: 'People', icon: Users },
  { id: 'activity', label: 'Activity', icon: Activity },
  { id: 'settings', label: 'Settings', icon: Settings },
] as const

const LIBRARY_APPS: readonly LibraryApp[] = [
  {
    id: 'cert-clock',
    name: 'Cert Clock',
    description: 'See when your installed apps need a refresh.',
    version: '0.1.0',
    initials: 'CC',
    tone: 'blue',
    source: 'On this Sideport',
    installedOn: '3 iPhones',
    release: 'Up to date',
  },
  {
    id: 'dice-roll',
    name: 'Dice Roll',
    description: 'A small one-tap dice roller for people you trust.',
    version: '0.1.0',
    initials: 'DR',
    tone: 'amber',
    source: 'GitHub release',
    installedOn: '2 iPhones',
    release: 'Update available',
  },
  {
    id: 'concentration',
    name: 'Concentration',
    description: 'A simple card-matching memory game.',
    version: '0.1.0',
    initials: 'CO',
    tone: 'green',
    source: 'GitHub release',
    installedOn: '1 iPhone',
    release: 'Ready to install',
  },
] as const

const SEARCH_RESULTS: readonly { route: CanonicalRoute; eyebrow: string; label: string; detail: string; icon: LucideIcon }[] = [
  { route: 'apps', eyebrow: 'App', label: 'Cert Clock', detail: 'Version 0.1.0 · ready to install', icon: Package },
  { route: 'devices', eyebrow: 'iPhone', label: 'Mara’s iPhone', detail: 'Connected over home Wi-Fi', icon: Smartphone },
  { route: 'people', eyebrow: 'Member', label: 'Mara', detail: 'Active · 1 iPhone', icon: CircleUserRound },
  { route: 'activity', eyebrow: 'Activity', label: 'Cert Clock refreshed', detail: 'Today at 09:42', icon: RotateCw },
] as const

function AppIcon({ app, compact = false }: { app: LibraryApp; compact?: boolean }) {
  return <span aria-hidden="true" className={`spc-app-icon ${app.tone} ${compact ? 'compact' : ''}`}>{app.initials}</span>
}

function ProposedBadge() {
  return <span className="spc-proposed-badge"><Sparkles aria-hidden="true" size={13} /> Proposed experience</span>
}

function SimulationNotice() {
  return <div className="spc-simulation-note" role="note"><Info aria-hidden="true" size={16} /><span><strong>Storybook simulation</strong><small>No invitation, sign-in, account, device, app, or audio action occurs.</small></span></div>
}

function Brand() {
  return (
    <div className="spc-brand">
      <span className="spc-brand-mark"><Package aria-hidden="true" size={19} /></span>
      <span><strong>Sideport</strong><small>Apps for people you trust</small></span>
    </div>
  )
}

function PageHeading({ eyebrow, title, children, action }: { eyebrow?: string; title: string; children: ReactNode; action?: ReactNode }) {
  return (
    <header className="spc-page-heading">
      <div>
        {eyebrow ? <span className="spc-eyebrow">{eyebrow}</span> : null}
        <h1 tabIndex={-1}>{title}</h1>
        <p>{children}</p>
      </div>
      {action ? <div className="spc-page-action">{action}</div> : null}
    </header>
  )
}

function StatusPill({ children, tone = 'positive' }: { children: ReactNode; tone?: 'positive' | 'quiet' | 'warning' }) {
  return <span className={`spc-status-pill ${tone}`}><span aria-hidden="true" className="spc-status-dot" />{children}</span>
}

function HomePage({ role, onAddIPhone, onNavigate }: { role: CanonicalRole; onAddIPhone: (trigger: HTMLButtonElement) => void; onNavigate: (route: CanonicalRoute) => void }) {
  return (
    <div className="spc-page" data-page="home">
      <PageHeading eyebrow={role === 'owner' ? 'Your Sideport' : 'Welcome home'} title={role === 'owner' ? 'Apps and iPhones at a glance' : 'Your apps are ready'}>
        {role === 'owner'
          ? 'Sideport keeps watching for connected iPhones and handles approved app updates in the background.'
          : 'Sideport will attempt refreshes over paired home Wi-Fi and use the cable as the reliable fallback.'}
      </PageHeading>

      <section className="spc-focus-card" aria-labelledby="home-device-title">
        <div className="spc-device-visual"><Cable aria-hidden="true" size={34} /></div>
        <div className="spc-focus-copy">
          <StatusPill>Watching</StatusPill>
          <h2 id="home-device-title">{role === 'owner' ? 'Cable ready for any trusted iPhone' : 'Your iPhone is connected'}</h2>
          <p>{role === 'owner' ? 'Leave the cable attached to the Sideport host. A trusted iPhone is detected automatically when someone plugs it in.' : 'Home Wi-Fi available · cable ready when an update needs it'}</p>
        </div>
        <button className="spc-button secondary" onClick={() => onNavigate('devices')} type="button">View devices</button>
      </section>

      <div className="spc-two-column">
        <section className="spc-section" aria-labelledby="home-apps-title">
          <div className="spc-section-heading">
            <div><span className="spc-eyebrow">Apps</span><h2 id="home-apps-title">1 update available</h2></div>
            <button className="spc-text-button" onClick={() => onNavigate('apps')} type="button">Find apps <ChevronRight aria-hidden="true" size={16} /></button>
          </div>
          <div className="spc-list-row">
            <AppIcon app={LIBRARY_APPS[1]} compact />
            <div><strong>Dice Roll 0.1.1</strong><small>Available for Mara and Alex</small></div>
            <StatusPill tone="warning">Update</StatusPill>
          </div>
        </section>

        <section className="spc-section" aria-labelledby="home-next-title">
          <div className="spc-section-heading"><div><span className="spc-eyebrow">Devices</span><h2 id="home-next-title">3 people · 3 iPhones</h2></div></div>
          <p className="spc-section-copy">Mara is home on Wi-Fi. Alex is away. Sam’s iPhone needs the cable for one update.</p>
          {role === 'owner'
            ? <button className="spc-button secondary full" onClick={(event) => onAddIPhone(event.currentTarget)} type="button"><Plus aria-hidden="true" size={17} /> Add another iPhone</button>
            : <p className="spc-section-copy">Need another iPhone? Ask the Sideport owner to add it after checking Apple’s device limit.</p>}
        </section>
      </div>
    </div>
  )
}

function ImportAppPanel({ onClose }: { onClose: () => void }) {
  const [source, setSource] = useState<'upload' | 'storage' | 'github'>('upload')
  return (
    <section className="spc-import-panel" aria-labelledby="import-app-title">
      <div className="spc-section-heading"><div><span className="spc-eyebrow">Import app</span><h2 id="import-app-title">Where is the IPA?</h2></div><button aria-label="Close app import" className="spc-icon-button" onClick={onClose} type="button"><X aria-hidden="true" size={19} /></button></div>
      <div aria-label="IPA source" className="spc-source-choices" role="group">
        <button aria-pressed={source === 'upload'} onClick={() => setSource('upload')} type="button"><FileUp aria-hidden="true" size={21} /><span><strong>This computer</strong><small>Choose an IPA file</small></span></button>
        <button aria-pressed={source === 'storage'} onClick={() => setSource('storage')} type="button"><HardDrive aria-hidden="true" size={21} /><span><strong>On this Sideport</strong><small>Browse managed server storage</small></span></button>
        <button aria-pressed={source === 'github'} onClick={() => setSource('github')} type="button"><GitBranch aria-hidden="true" size={21} /><span><strong>GitHub release</strong><small>Public or selected private repository</small></span></button>
      </div>
      {source === 'upload' ? <div className="spc-source-detail"><strong>Choose an IPA from this computer</strong><span>The runtime will inspect its name, icon, bundle, version, and size before it becomes available.</span><button className="spc-button secondary" disabled type="button">Choose IPA in runtime</button></div> : null}
      {source === 'storage' ? <div className="spc-source-detail"><strong>Browse Sideport storage</strong><span>The runtime will list managed IPA files without asking a member for a server path.</span><div className="spc-mini-apps">{LIBRARY_APPS.map((app) => <span key={app.id}><AppIcon app={app} compact /><span><strong>{app.name}</strong><small>Version {app.version}</small></span></span>)}</div></div> : null}
      {source === 'github' ? <div className="spc-source-detail"><strong>Connect one selected repository</strong><span>Private access is requested only for the repository the owner selects.</span><dl className="spc-permission-list"><div><dt>Metadata</dt><dd>Read</dd></div><div><dt>Contents</dt><dd>Read</dd></div><div><dt>Write access</dt><dd>None</dd></div></dl><button className="spc-button secondary" disabled type="button">Connect GitHub in runtime</button></div> : null}
    </section>
  )
}

function AppsPage({ importOpen, memberName, onInstall, onToggleImport, role }: { importOpen: boolean; memberName: string; onInstall: (appId: string, memberName: string) => void; onToggleImport: () => void; role: CanonicalRole }) {
  const [query, setQuery] = useState('')
  const [targetAppId, setTargetAppId] = useState<string | null>(null)
  const targetHeadingRef = useRef<HTMLHeadingElement>(null)
  const targetTriggerRef = useRef<HTMLButtonElement>(null)
  const visibleApps = LIBRARY_APPS.filter((app) => `${app.name} ${app.description}`.toLowerCase().includes(query.toLowerCase()))
  const targetApp = LIBRARY_APPS.find((app) => app.id === targetAppId)
  useEffect(() => { if (targetAppId) targetHeadingRef.current?.focus() }, [targetAppId])

  return (
    <div className="spc-page" data-page="apps">
      <PageHeading
        eyebrow="Approved by the owner"
        title="Apps"
        action={role === 'owner' ? <button aria-expanded={importOpen} className="spc-button secondary" onClick={onToggleImport} type="button"><FileUp aria-hidden="true" size={17} /> Import app</button> : undefined}
      >Find an app, choose an iPhone, and install. Sideport keeps approved apps updated afterward.</PageHeading>
      {role === 'owner' && importOpen ? <ImportAppPanel onClose={onToggleImport} /> : null}
      <label className="spc-page-search">
        <Search aria-hidden="true" size={18} />
        <span className="spc-visually-hidden">Search apps</span>
        <input onChange={(event) => setQuery(event.currentTarget.value)} placeholder="Search approved apps" type="search" value={query} />
      </label>
      <div className="spc-filter-chips" aria-label="App filters" role="group"><button aria-pressed="true" type="button">All apps</button><button aria-pressed="false" type="button">Updates</button><button aria-pressed="false" type="button">Installed</button></div>
      <div className="spc-app-grid">
        {visibleApps.map((app) => (
          <article className="spc-app-card" key={app.id}>
            <div className="spc-app-card-top"><AppIcon app={app} /><StatusPill tone={app.release === 'Update available' ? 'warning' : 'quiet'}>{app.release}</StatusPill></div>
            <div><h2>{app.name}</h2><p>{app.description}</p></div>
            <div className="spc-app-meta"><span>Version {app.version} · {app.source}</span><span>{app.installedOn}</span></div>
            <button className="spc-button primary full" onClick={(event) => { if (role === 'owner') { targetTriggerRef.current = event.currentTarget; setTargetAppId(app.id) } else { onInstall(app.id, memberName) } }} type="button">{role === 'owner' ? 'Choose iPhone' : 'Install'}</button>
          </article>
        ))}
      </div>
      {role === 'owner' && targetApp ? <section className="spc-target-picker" aria-labelledby="target-picker-title"><div><span className="spc-eyebrow">Install {targetApp.name}</span><h2 id="target-picker-title" ref={targetHeadingRef} tabIndex={-1}>Choose the iPhone by name</h2><p>Sideport will wait for that exact iPhone on the cable before installation starts.</p></div><div role="group" aria-label="Target iPhone"><button className="spc-button secondary" onClick={() => onInstall(targetApp.id, 'Mara')} type="button"><Smartphone aria-hidden="true" size={17} /> Mara’s iPhone</button><button className="spc-button secondary" onClick={() => onInstall(targetApp.id, 'Alex')} type="button"><Smartphone aria-hidden="true" size={17} /> Alex’s iPhone</button><button aria-label="Cancel iPhone choice" className="spc-icon-button" onClick={() => { setTargetAppId(null); targetTriggerRef.current?.focus() }} type="button"><X aria-hidden="true" size={18} /></button></div></section> : null}
      {role === 'owner' ? (
        <aside className="spc-inline-note"><Info aria-hidden="true" size={18} /><div><strong>Need another app?</strong><span>Import an IPA from this computer, Sideport storage, or an approved public or private GitHub repository.</span></div></aside>
      ) : null}
    </div>
  )
}

function DevicesPage({ role, onAddIPhone }: { role: CanonicalRole; onAddIPhone: (trigger: HTMLButtonElement) => void }) {
  return (
    <div className="spc-page" data-page="devices">
      <PageHeading eyebrow="Always watching the Sideport cable" title="Devices" action={role === 'owner' ? <button className="spc-button primary" onClick={(event) => onAddIPhone(event.currentTarget)} type="button"><Plus aria-hidden="true" size={17} /> Add iPhone</button> : undefined}>
        {role === 'owner' ? 'See who owns each iPhone, where Sideport can reach it, and whether an app needs attention.' : 'Your iPhone and its approved Sideport apps.'}
      </PageHeading>
      <aside aria-label="USB port monitoring status" className="spc-port-monitor" aria-live="polite"><span className="spc-pulse-dot" /><div><strong>USB port monitor is active</strong><span>Plug in an already trusted iPhone and Sideport will recognize it automatically. A new iPhone starts the guided Trust setup.</span></div></aside>
      <section className="spc-section spc-device-list" aria-label="iPhones">
        <article className="spc-device-row">
          <span className="spc-round-icon"><Smartphone aria-hidden="true" size={23} /></span>
          <div><h2>{role === 'owner' ? 'Mara’s iPhone' : 'Your iPhone'}</h2><p>{role === 'owner' ? 'Mara · Member' : 'iPhone 15'} · 3 apps installed</p></div>
          <div className="spc-device-status"><StatusPill>Home Wi-Fi</StatusPill><small>Up to date · seen now</small></div>
          <button aria-label="Open iPhone details" className="spc-icon-button" type="button"><ChevronRight aria-hidden="true" size={20} /></button>
        </article>
        {role === 'owner' ? (
          <article className="spc-device-row">
            <span className="spc-round-icon"><Smartphone aria-hidden="true" size={23} /></span>
            <div><h2>Alex’s iPhone</h2><p>Alex · Member · 2 apps installed</p></div>
            <div className="spc-device-status"><StatusPill tone="quiet">Away</StatusPill><small>Up to date · seen yesterday</small></div>
            <button aria-label="Open Alex’s iPhone details" className="spc-icon-button" type="button"><ChevronRight aria-hidden="true" size={20} /></button>
          </article>
        ) : null}
        {role === 'owner' ? (
          <article className="spc-device-row">
            <span className="spc-round-icon"><Smartphone aria-hidden="true" size={23} /></span>
            <div><h2>Sam’s iPhone</h2><p>Sam · Member · 2 apps installed</p></div>
            <div className="spc-device-status"><StatusPill tone="warning">Cable needed</StatusPill><small>Dice Roll update waiting</small></div>
            <button aria-label="Open Sam’s iPhone details" className="spc-icon-button" type="button"><ChevronRight aria-hidden="true" size={20} /></button>
          </article>
        ) : null}
      </section>
      {role === 'member' ? <aside className="spc-inline-note"><Info aria-hidden="true" size={18} /><div><strong>Need another iPhone?</strong><span>Ask the Sideport owner. They will check available Apple device capacity before connecting it.</span></div></aside> : null}
      <aside className="spc-inline-note"><Cable aria-hidden="true" size={18} /><div><strong>The cable is only required for setup and reliable fallback.</strong><span>Sideport tries home Wi-Fi first. If a refresh cannot finish, reconnect this iPhone to the Sideport cable.</span></div></aside>
    </div>
  )
}

function PeoplePage({ role }: { role: CanonicalRole }) {
  const [email, setEmail] = useState('')
  const [invited, setInvited] = useState(false)
  const submitInvite = (event: FormEvent) => {
    event.preventDefault()
    if (email.trim()) setInvited(true)
  }

  return (
    <div className="spc-page" data-page="people">
      <PageHeading eyebrow="Your Sideport" title="People">
        {role === 'owner' ? 'Invite someone you trust. They sign in with a passkey and can use only approved apps on their own iPhone.' : 'People who share this Sideport with you.'}
      </PageHeading>
      {role === 'owner' ? (
        <section className="spc-invite-card" aria-labelledby="invite-title">
          <span className="spc-round-icon accent"><Users aria-hidden="true" size={23} /></span>
          <div className="spc-invite-copy"><h2 id="invite-title">Invite someone you trust</h2><p>We’ll create a private, single-use link. They will use Face ID, Touch ID, Windows Hello, or their password manager—never your Apple signing password.</p></div>
          {invited ? (
            <div className="spc-invite-result" role="status"><CheckCircle2 aria-hidden="true" size={20} /><div><strong>Invitation ready</strong><span>Copy the link and send it to {email}.</span></div><button className="spc-button secondary" type="button">Copy link</button></div>
          ) : (
            <form className="spc-invite-form" onSubmit={submitInvite}>
              <label htmlFor="member-email">Their email</label>
              <div><input id="member-email" onChange={(event) => setEmail(event.currentTarget.value)} placeholder="name@example.com" type="email" value={email} /><button className="spc-button primary" disabled={!email.trim()} type="submit">Create invitation</button></div>
              <small>Access: Member · app sources and Apple signing stay owner-only.</small>
            </form>
          )}
        </section>
      ) : (
        <aside className="spc-inline-note"><ShieldCheck aria-hidden="true" size={18} /><div><strong>You have Member access.</strong><span>You can use your iPhone and install approved apps. Another iPhone requires the owner to check capacity first.</span></div></aside>
      )}
      <section className="spc-section" aria-labelledby="people-list-title">
        <div className="spc-section-heading"><div><span className="spc-eyebrow">Members</span><h2 id="people-list-title">3 people</h2></div></div>
        {[
          ['Dragos', 'Owner · signing and member access'],
          ['Mara', role === 'member' ? 'You · Member · 1 iPhone' : 'Member · 1 iPhone'],
          ['Alex', 'Member · 1 iPhone'],
        ].map(([name, detail]) => <div className="spc-list-row" key={name}><span className="spc-avatar">{name[0]}</span><div><strong>{name}</strong><small>{detail}</small></div><StatusPill tone="quiet">Active</StatusPill></div>)}
      </section>
    </div>
  )
}

function ActivityPage({ role }: { role: CanonicalRole }) {
  const [technical, setTechnical] = useState(false)
  const [filter, setFilter] = useState<'all' | 'attention' | 'apps' | 'devices'>('all')
  return (
    <div className="spc-page" data-page="activity">
      <PageHeading eyebrow="What happened and who needs help" title="Activity" action={role === 'owner' ? <button aria-expanded={technical} className="spc-button secondary" onClick={() => setTechnical((value) => !value)} type="button">{technical ? 'Hide' : 'Show'} technical details</button> : undefined}>
        {role === 'owner' ? 'Updates across people, iPhones, apps, and access—in one chronological feed.' : 'Installs and updates for your iPhone, in plain language.'}
      </PageHeading>
      <div className="spc-filter-chips" aria-label="Activity filters" role="group">
        {([['all', 'All'], ['attention', 'Needs attention'], ['apps', 'Apps'], ['devices', 'Devices']] as const).map(([id, label]) => <button aria-pressed={filter === id} key={id} onClick={() => setFilter(id)} type="button">{label}</button>)}
      </div>
      <section className="spc-section spc-timeline" aria-label="Recent activity">
        {(filter === 'all' || filter === 'attention' || filter === 'devices') ? <><h2 className="spc-timeline-group">Needs attention</h2><article className="attention"><span className="spc-timeline-icon warning"><Cable aria-hidden="true" size={17} /></span><div><h2>Sam’s iPhone needs the cable</h2><p>Dice Roll could not update over Wi-Fi. Plug it into the Sideport host; the update starts automatically.</p>{role === 'owner' && technical ? <code>refresh waiting-for-usb · member sam · retry preserved</code> : null}<button className="spc-text-button" type="button">View device <ChevronRight aria-hidden="true" size={15} /></button></div><time>10 min ago</time></article></> : null}
        {filter !== 'attention' ? <h2 className="spc-timeline-group">Today</h2> : null}
        {(filter === 'all' || filter === 'apps') ? <article><span className="spc-timeline-icon success"><Check aria-hidden="true" size={17} /></span><div><h2>Cert Clock updated on Mara’s iPhone</h2><p>Version 0.1.1 installed and verified.</p>{role === 'owner' && technical ? <code>operation op_refresh_01 · member mara · device evidence verified</code> : null}</div><time>09:42</time></article> : null}
        {(filter === 'all' || filter === 'devices') ? <article><span className="spc-timeline-icon"><Smartphone aria-hidden="true" size={17} /></span><div><h2>{role === 'owner' ? 'Alex’s iPhone came home' : 'Your iPhone came home'}</h2><p>Sideport can reach it over paired Wi-Fi. No update is due.</p>{role === 'owner' && technical ? <code>transport network-usbmux · lockdown trusted</code> : null}</div><time>08:15</time></article> : null}
        {(filter === 'all' || filter === 'apps') ? <article><span className="spc-timeline-icon"><Download aria-hidden="true" size={17} /></span><div><h2>Dice Roll 0.1.1 became available</h2><p>Imported from the approved GitHub release. Installed devices can update automatically.</p>{role === 'owner' && technical ? <code>catalog source github · artifact inspected · owner action</code> : null}</div><time>07:30</time></article> : null}
        {role === 'owner' && filter === 'all' ? <><h2 className="spc-timeline-group">Earlier</h2><article><span className="spc-timeline-icon"><CircleUserRound aria-hidden="true" size={17} /></span><div><h2>Sam joined Sideport</h2><p>Member access activated · Sam’s iPhone was added and trusted.</p>{technical ? <code>membership accepted · resource owner member_sam</code> : null}</div><time>Monday</time></article></> : null}
      </section>
    </div>
  )
}

function SettingsPage({ role }: { role: CanonicalRole }) {
  const [technical, setTechnical] = useState(false)
  const [signingOpen, setSigningOpen] = useState(false)
  const [signingStep, setSigningStep] = useState<'summary' | 'reauth' | 'impact' | 'working' | 'done'>('summary')
  const [password, setPassword] = useState('storybook-demo')
  const [impactAccepted, setImpactAccepted] = useState(false)
  const signingHeadingRef = useRef<HTMLHeadingElement>(null)
  useEffect(() => { if (signingOpen) signingHeadingRef.current?.focus() }, [signingOpen, signingStep])
  useEffect(() => {
    if (signingStep !== 'working') return
    const timer = window.setTimeout(() => setSigningStep('done'), 800)
    return () => window.clearTimeout(timer)
  }, [signingStep])
  const closeSigning = () => {
    setSigningOpen(false)
    setSigningStep('summary')
    setPassword('storybook-demo')
    setImpactAccepted(false)
  }
  return (
    <div className="spc-page" data-page="settings">
      <PageHeading eyebrow="Simple by default" title="Settings">Your account, automatic refresh, and recovery.</PageHeading>
      <div className="spc-settings-list">
        <section className="spc-setting-row"><span className="spc-round-icon"><ShieldCheck aria-hidden="true" size={22} /></span><div><h2>Sign-in and recovery</h2><p>Managed by Authentik · passkeys stay on your trusted devices.</p></div><button className="spc-button secondary" type="button">Open Authentik</button></section>
        <section className="spc-setting-row"><span className="spc-round-icon"><RotateCw aria-hidden="true" size={22} /></span><div><h2>Automatic refresh</h2><p>On · paired Wi-Fi is attempted; the Sideport cable is the reliable fallback.</p></div><StatusPill>On</StatusPill></section>
        {role === 'owner' ? <section className="spc-setting-row"><span className="spc-round-icon"><Apple aria-hidden="true" size={22} /></span><div><h2>Signing</h2><p>d•••••@icloud.com · Personal Team returned by Apple</p></div><button aria-label="Review Apple signing" className="spc-button secondary" onClick={() => setSigningOpen(true)} type="button">Review</button></section> : null}
        {role === 'owner' ? <section className="spc-setting-row"><span className="spc-round-icon"><Laptop aria-hidden="true" size={22} /></span><div><h2>Sideport setup</h2><p>Completed · current services are ready.</p></div><button className="spc-button secondary" type="button">Review</button></section> : null}
      </div>
      {role === 'owner' ? <button aria-expanded={technical} className="spc-disclosure" onClick={() => setTechnical((value) => !value)} type="button"><Info aria-hidden="true" size={17} /> {technical ? 'Hide technical details' : 'Show technical details'} <ChevronRight aria-hidden="true" size={17} /></button> : null}
      {role === 'owner' && technical ? <section className="spc-technical-panel"><h2>Technical details</h2><dl><div><dt>Deployment</dt><dd>Docker · healthy</dd></div><div><dt>Device link</dt><dd>usbmuxd · available</dd></div><div><dt>Signer</dt><dd>Ready · credential redacted</dd></div><div><dt>Observability</dt><dd>Local activity only</dd></div></dl></section> : null}
      {role === 'owner' && signingOpen ? <div aria-label="Review Apple signing" aria-modal="true" className="spc-dialog-backdrop" role="dialog"><section className="spc-signing-dialog">
        <button aria-label="Close signing review" className="spc-icon-button spc-dialog-close" onClick={closeSigning} type="button"><X aria-hidden="true" size={20} /></button>
        {signingStep === 'summary' ? <><span className="spc-eyebrow">Owner-only signing</span><h1 ref={signingHeadingRef} tabIndex={-1}>Apple signing</h1><p className="spc-lead">Sideport uses one Apple account and one team for every approved app. Members never see or change this access.</p><div className="spc-signing-summary"><div><span>Apple account</span><strong>d•••••@icloud.com</strong></div><div><span>Apple Developer Team</span><strong>Dragos Personal Team</strong><small>Returned by Apple · selected automatically</small></div><div><span>Signing identity</span><strong>Ready until June 30, 2027</strong><small>Certificate ending A1B2</small></div></div><button className="spc-button secondary large" onClick={() => setSigningStep('reauth')} type="button">Change account or team</button><p className="spc-fine-print">Nothing changes until Sideport shows the exact impact and you confirm it.</p></> : null}
        {signingStep === 'reauth' ? <form onSubmit={(event) => { event.preventDefault(); setPassword(''); setSigningStep('impact') }}><span className="spc-eyebrow">Fresh Apple sign-in required</span><h1 ref={signingHeadingRef} tabIndex={-1}>Confirm your Apple account</h1><p className="spc-lead">Sign in again so Sideport can fetch the current teams and certificate inventory. This does not change signing yet.</p><label htmlFor="signing-account">Apple Account</label><input autoComplete="username" id="signing-account" readOnly type="email" value="dragos@icloud.com" /><label htmlFor="signing-password">Password</label><input autoComplete="current-password" id="signing-password" onChange={(event) => setPassword(event.currentTarget.value)} type="password" value={password} /><button className="spc-button primary large" disabled={!password} type="submit">Check current signing</button><button className="spc-text-button centered-button" onClick={() => setSigningStep('summary')} type="button">Back</button></form> : null}
        {signingStep === 'impact' ? <><span className="spc-eyebrow">Review before changing</span><h1 ref={signingHeadingRef} tabIndex={-1}>Replace the current signing identity?</h1><p className="spc-lead">Apple already has one development certificate that Sideport cannot reuse. Replacing it may affect apps signed elsewhere.</p><section className="spc-impact-card" aria-label="Exact signing impact"><div><strong>1 certificate</strong><span>Certificate ending A1B2 will be revoked</span></div><div><strong>3 Sideport apps</strong><span>They remain installed and will use the new identity on their next refresh</span></div><div><strong>3 iPhones · 3 profiles</strong><span>Registrations remain; profiles are recreated only when each app refreshes</span></div></section><label className="spc-confirm-row"><input checked={impactAccepted} onChange={(event) => setImpactAccepted(event.currentTarget.checked)} type="checkbox" /><span>I understand certificate A1B2 is the only certificate Sideport is authorized to replace.</span></label><button className="spc-button danger large" disabled={!impactAccepted} onClick={() => setSigningStep('working')} type="button">Replace signing identity</button><button className="spc-text-button centered-button" onClick={() => setSigningStep('summary')} type="button">Keep current signing</button></> : null}
        {signingStep === 'working' ? <div className="centered"><div className="spc-pulse-icon"><Apple aria-hidden="true" size={36} /></div><span className="spc-eyebrow">Do not close Sideport</span><h1 ref={signingHeadingRef} tabIndex={-1}>Preparing the new signing identity</h1><p className="spc-lead">Sideport is rechecking the certificate inventory, replacing only the approved certificate, and verifying the new identity.</p><div className="spc-wait-status"><RotateCw aria-hidden="true" size={18} /> Verifying with Apple…</div></div> : null}
        {signingStep === 'done' ? <div className="centered"><div className="spc-hero-icon success"><Check aria-hidden="true" size={38} /></div><span className="spc-eyebrow">Signing ready</span><h1 ref={signingHeadingRef} tabIndex={-1}>New signing identity verified</h1><p className="spc-lead">Sideport replaced certificate A1B2 and saved the new identity. Installed apps remain available and use it on their next refresh.</p><button className="spc-button primary large" onClick={closeSigning} type="button">Done</button></div> : null}
      </section></div> : null}
    </div>
  )
}

function SearchDialog({ onClose, onNavigate }: { onClose: () => void; onNavigate: (route: CanonicalRoute) => void }) {
  const inputRef = useRef<HTMLInputElement>(null)
  const dialogRef = useRef<HTMLDivElement>(null)
  const [query, setQuery] = useState('')
  useEffect(() => inputRef.current?.focus(), [])
  const results = SEARCH_RESULTS.filter((result) => `${result.eyebrow} ${result.label} ${result.detail}`.toLowerCase().includes(query.toLowerCase()))
  const handleKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      onClose()
      return
    }
    if (event.key !== 'Tab') return
    const focusable = Array.from(dialogRef.current?.querySelectorAll<HTMLElement>('button:not([disabled]), input:not([disabled])') ?? [])
    const first = focusable[0]
    const last = focusable[focusable.length - 1]
    if (!first || !last) return
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault()
      last.focus()
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault()
      first.focus()
    }
  }

  return (
    <div aria-label="Search Sideport" aria-modal="true" className="spc-dialog-backdrop" onKeyDown={handleKeyDown} ref={dialogRef} role="dialog">
      <div className="spc-search-dialog">
        <div className="spc-search-input"><Search aria-hidden="true" size={20} /><label className="spc-visually-hidden" htmlFor="global-search">Search Sideport</label><input id="global-search" onChange={(event) => setQuery(event.currentTarget.value)} placeholder="Search apps, iPhones, people, and activity" ref={inputRef} type="search" value={query} /><button aria-label="Close search" className="spc-icon-button" onClick={onClose} type="button"><X aria-hidden="true" size={20} /></button></div>
        <div className="spc-search-results">
          {results.map((result) => { const Icon = result.icon; return <button key={`${result.route}-${result.label}`} onClick={() => { onNavigate(result.route); onClose() }} type="button"><span className="spc-round-icon"><Icon aria-hidden="true" size={20} /></span><span><small>{result.eyebrow}</small><strong>{result.label}</strong><span>{result.detail}</span></span><ChevronRight aria-hidden="true" size={18} /></button> })}
          {!results.length ? <p>No matching apps, iPhones, people, or activity.</p> : null}
        </div>
      </div>
    </div>
  )
}

function SignedInShell({ memberName, role, initialRoute, onStartAssistant }: { memberName: string; role: CanonicalRole; initialRoute: CanonicalRoute; onStartAssistant: (step?: CanonicalAssistantStep, appId?: string, memberName?: string) => void }) {
  const [route, setRoute] = useState<CanonicalRoute>(initialRoute)
  const [searchOpen, setSearchOpen] = useState(false)
  const [addOpen, setAddOpen] = useState(false)
  const [importOpen, setImportOpen] = useState(false)
  const [phoneTargetOpen, setPhoneTargetOpen] = useState(false)
  const searchTriggerRef = useRef<HTMLButtonElement>(null)
  const addTriggerRef = useRef<HTMLButtonElement>(null)
  const phoneTargetTriggerRef = useRef<HTMLButtonElement>(null)
  const phoneTargetHeadingRef = useRef<HTMLHeadingElement>(null)
  useEffect(() => {
    const handleShortcut = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        setSearchOpen(true)
      }
    }
    window.addEventListener('keydown', handleShortcut)
    return () => window.removeEventListener('keydown', handleShortcut)
  }, [])
  useEffect(() => { if (phoneTargetOpen) phoneTargetHeadingRef.current?.focus() }, [phoneTargetOpen])

  const closeSearch = () => {
    setSearchOpen(false)
    searchTriggerRef.current?.focus()
  }
  const navigate = (nextRoute: CanonicalRoute) => {
    setRoute(nextRoute)
    setAddOpen(false)
    setPhoneTargetOpen(false)
  }
  const openAddIPhone = (trigger: HTMLButtonElement) => {
    phoneTargetTriggerRef.current = trigger
    setAddOpen(false)
    setPhoneTargetOpen(true)
  }
  const choosePhoneTarget = (targetMemberName: string) => {
    setPhoneTargetOpen(false)
    onStartAssistant('connect', 'cert-clock', targetMemberName)
  }
  const renderPage = () => {
    switch (route) {
      case 'apps': return <AppsPage importOpen={importOpen} memberName={memberName} onInstall={(appId, targetMemberName) => onStartAssistant('install-waiting', appId, targetMemberName)} onToggleImport={() => setImportOpen((value) => !value)} role={role} />
      case 'devices': return <DevicesPage onAddIPhone={openAddIPhone} role={role} />
      case 'people': return <PeoplePage role={role} />
      case 'activity': return <ActivityPage role={role} />
      case 'settings': return <SettingsPage role={role} />
      default: return <HomePage onAddIPhone={openAddIPhone} onNavigate={navigate} role={role} />
    }
  }

  return (
    <div className="spc-shell" data-role={role} data-testid="canonical-signed-in-shell">
      <aside aria-label="Primary Sideport sidebar" className="spc-sidebar">
        <Brand />
        <nav aria-label="Sideport navigation"><ul>{NAVIGATION.map((item) => { const Icon = item.icon; return <li key={item.id}><button aria-current={route === item.id ? 'page' : undefined} className={route === item.id ? 'active' : ''} onClick={() => navigate(item.id)} type="button"><Icon aria-hidden="true" size={19} /><span>{item.label}</span></button></li> })}</ul></nav>
        <div className="spc-sidebar-footer"><span className="spc-avatar">{role === 'owner' ? 'D' : 'M'}</span><span><strong>{role === 'owner' ? 'Dragos' : 'Mara'}</strong><small>{role === 'owner' ? 'Owner' : 'Member'}</small></span></div>
      </aside>

      <div className="spc-workspace">
        <header className="spc-topbar">
          <div className="spc-mobile-brand"><Brand /></div>
          <button aria-label="Search Sideport" className="spc-global-search" onClick={() => setSearchOpen(true)} ref={searchTriggerRef} type="button"><Search aria-hidden="true" size={18} /><span>Search Sideport</span><kbd>⌘ K</kbd></button>
          {role === 'owner' ? <div className="spc-add-wrap" onKeyDown={(event) => { if (event.key === 'Escape' && addOpen) { setAddOpen(false); addTriggerRef.current?.focus() } }}>
            <button aria-expanded={addOpen} aria-label="Add" className="spc-button primary" onClick={() => setAddOpen((value) => !value)} ref={addTriggerRef} type="button"><Plus aria-hidden="true" size={18} /> <span>Add</span></button>
            {addOpen ? <div aria-label="Add options" className="spc-add-menu" role="group"><button onClick={(event) => openAddIPhone(event.currentTarget)} type="button"><Smartphone aria-hidden="true" size={19} /><span><strong>Add iPhone</strong><small>Choose its member, then pair once</small></span></button><button onClick={() => { setRoute('apps'); setImportOpen(true); setAddOpen(false) }} type="button"><FileUp aria-hidden="true" size={19} /><span><strong>Import app</strong><small>Computer, Sideport storage, or GitHub</small></span></button><button onClick={() => { setRoute('people'); setAddOpen(false) }} type="button"><Users aria-hidden="true" size={19} /><span><strong>Invite someone you trust</strong><small>Create a private sign-in link</small></span></button></div> : null}
          </div> : null}
          <button aria-label="Open settings" className="spc-mobile-settings spc-icon-button" onClick={() => navigate('settings')} type="button"><Settings aria-hidden="true" size={20} /></button>
        </header>
        <nav aria-label="Mobile Sideport navigation" className="spc-mobile-nav"><ul>{NAVIGATION.filter((item) => item.id !== 'settings').map((item) => { const Icon = item.icon; return <li key={item.id}><button aria-current={route === item.id ? 'page' : undefined} onClick={() => navigate(item.id)} type="button"><Icon aria-hidden="true" size={20} /><span>{item.label}</span></button></li> })}</ul></nav>
        <main className="spc-main"><div className="spc-preview-line"><ProposedBadge /><span>Storybook fixture · no live account, device, or app changes</span></div>{phoneTargetOpen ? <section className="spc-target-picker" aria-labelledby="phone-target-picker-title"><div><span className="spc-eyebrow">Add iPhone</span><h2 id="phone-target-picker-title" ref={phoneTargetHeadingRef} tabIndex={-1}>Who will use this iPhone?</h2><p>Sideport checks that person’s active membership and Apple device capacity before pairing starts.</p></div><div role="group" aria-label="iPhone member"><button className="spc-button secondary" onClick={() => choosePhoneTarget('Mara')} type="button"><CircleUserRound aria-hidden="true" size={17} /> Mara</button><button className="spc-button secondary" onClick={() => choosePhoneTarget('Alex')} type="button"><CircleUserRound aria-hidden="true" size={17} /> Alex</button><button aria-label="Cancel member choice" className="spc-icon-button" onClick={() => { setPhoneTargetOpen(false); phoneTargetTriggerRef.current?.focus() }} type="button"><X aria-hidden="true" size={18} /></button></div></section> : null}{renderPage()}</main>
      </div>
      {searchOpen ? <SearchDialog onClose={closeSearch} onNavigate={navigate} /> : null}
    </div>
  )
}

function AssistantProgress({ step }: { step: CanonicalAssistantStep }) {
  const position = step === 'connect' || step === 'waiting' ? 1 : step === 'prepare' ? 2 : 3
  return (
    <div className="spc-assistant-progress" aria-label={`Step ${position} of 3`}>
      <span>Step {position} of 3</span>
      <div>{[1, 2, 3].map((item) => <i className={item <= position ? 'active' : ''} key={item} />)}</div>
    </div>
  )
}

function IPhoneAssistant({ initialAppId = 'cert-clock', initialStep = 'connect', memberName, onClose, onFinish }: { initialAppId?: string; initialStep?: CanonicalAssistantStep; memberName: string; onClose?: () => void; onFinish: () => void }) {
  const [step, setStep] = useState<CanonicalAssistantStep>(initialStep)
  const [selectedAppId, setSelectedAppId] = useState(initialAppId)
  const headingRef = useRef<HTMLHeadingElement>(null)
  const selectedApp = LIBRARY_APPS.find((app) => app.id === selectedAppId) ?? LIBRARY_APPS[0]
  useEffect(() => { headingRef.current?.focus() }, [step])
  useEffect(() => {
    if (step !== 'waiting' && step !== 'install-waiting' && step !== 'installing') return
    const next: CanonicalAssistantStep = step === 'waiting' ? 'prepare' : step === 'install-waiting' ? 'installing' : 'done'
    const timer = window.setTimeout(() => setStep(next), step === 'installing' ? 1200 : 900)
    return () => window.clearTimeout(timer)
  }, [step])

  return (
    <div className="spc-assistant" data-assistant-step={step} data-testid="canonical-iphone-assistant">
      <header className="spc-assistant-header"><Brand /><ProposedBadge />{onClose && step !== 'installing' ? <button aria-label="Close assistant" className="spc-icon-button" onClick={onClose} type="button"><X aria-hidden="true" size={20} /></button> : null}</header>
      <main className="spc-assistant-main">
        <SimulationNotice />
        {step !== 'done' ? <AssistantProgress step={step} /> : null}

        {step === 'connect' ? <section className="spc-assistant-content"><div className="spc-hero-icon"><Cable aria-hidden="true" size={38} /></div><span className="spc-eyebrow">Add {memberName}’s iPhone</span><h1 ref={headingRef} tabIndex={-1}>Connect the iPhone</h1><p className="spc-lead">Use the cable beside the Sideport computer. Keep this page open—Sideport will find and add the iPhone automatically.</p><ol className="spc-phone-steps"><li><span>1</span><div><strong>Plug in and unlock the iPhone</strong><small>Leave it connected until Sideport says you can unplug.</small></div></li><li><span>2</span><div><strong>Tap “Trust” on the iPhone</strong><small>Then enter the iPhone passcode. There is no Pair or Add button here.</small></div></li><li><span>3</span><div><strong>Keep the iPhone nearby</strong><small>Next, Sideport will guide Developer Mode and the restart.</small></div></li></ol><aside className="spc-inline-note"><Wifi aria-hidden="true" size={18} /><div><strong>Why the cable?</strong><span>It is required for first setup and installation. Later refreshes can use the same home Wi-Fi.</span></div></aside><button className="spc-button primary large" onClick={() => setStep('waiting')} type="button">Start connecting</button></section> : null}

        {step === 'waiting' ? <section className="spc-assistant-content centered" aria-live="polite"><div className="spc-pulse-icon"><Smartphone aria-hidden="true" size={38} /></div><span className="spc-eyebrow">Waiting for the iPhone</span><h1 ref={headingRef} tabIndex={-1}>Unlock it and tap Trust</h1><p className="spc-lead">Sideport is watching the cable. When Trust is complete, pairing and adding happen automatically.</p><div className="spc-wait-status" role="status"><RotateCw aria-hidden="true" size={18} /> Looking for a trusted iPhone…</div></section> : null}

        {step === 'prepare' ? <section className="spc-assistant-content"><div className="spc-hero-icon success"><CheckCircle2 aria-hidden="true" size={38} /></div><span className="spc-eyebrow">Simulated iPhone added automatically</span><h1 ref={headingRef} tabIndex={-1}>Turn on Developer Mode</h1><p className="spc-lead">This one iPhone setting allows approved Sideport apps to open. Apple requires a restart.</p><ol className="spc-phone-steps"><li><span>1</span><div><strong>Open Settings → Privacy &amp; Security</strong><small>Scroll down and tap Developer Mode.</small></div></li><li><span>2</span><div><strong>Turn it on, then restart</strong><small>The iPhone will ask you to confirm the restart.</small></div></li><li><span>3</span><div><strong>After restart, unlock and tap Enable</strong><small>Enter the passcode, then reconnect the cable if it was removed.</small></div></li></ol><aside className="spc-inline-note warning"><Info aria-hidden="true" size={18} /><div><strong>Sideport cannot read this setting yet.</strong><span>Continue only after you completed the steps on the iPhone.</span></div></aside><button className="spc-button primary large" onClick={() => setStep('choose')} type="button">I restarted and reconnected</button></section> : null}

        {step === 'choose' ? <section className="spc-assistant-content wide"><span className="spc-eyebrow">Ready to install</span><h1 ref={headingRef} tabIndex={-1}>Choose an app</h1><p className="spc-lead">These apps are approved by the Sideport owner. Choose one, then install once.</p><div className="spc-assistant-apps" role="radiogroup" aria-label="Approved apps">{LIBRARY_APPS.map((app) => <label className={selectedAppId === app.id ? 'selected' : ''} key={app.id}><input checked={selectedAppId === app.id} name="assistant-app" onChange={() => setSelectedAppId(app.id)} type="radio" value={app.id} /><AppIcon app={app} /><span><strong>{app.name}</strong><small>{app.description}</small><em>Version {app.version} · {app.source}</em></span><span className="spc-radio-mark"><Check aria-hidden="true" size={15} /></span></label>)}</div><div className="spc-smart-default"><RotateCw aria-hidden="true" size={18} /><span><strong>Automatic refresh is on</strong><small>Sideport attempts paired home Wi-Fi. Use the cable whenever a refresh cannot finish.</small></span></div><button className="spc-button primary large" onClick={() => setStep('installing')} type="button">Install {selectedApp.name}</button></section> : null}

        {step === 'install-waiting' ? <section className="spc-assistant-content centered" aria-live="polite"><div className="spc-pulse-icon"><Cable aria-hidden="true" size={38} /></div><span className="spc-eyebrow">Simulated USB readiness check</span><h1 ref={headingRef} tabIndex={-1}>Connect the iPhone to install {selectedApp.name}</h1><p className="spc-lead">Plug it into the Sideport cable, unlock it, and tap Trust if asked. Sideport waits for the reliable USB connection and then starts automatically.</p><div className="spc-wait-status" role="status"><RotateCw aria-hidden="true" size={18} /> Waiting for the Sideport cable…</div></section> : null}

        {step === 'installing' ? <section className="spc-assistant-content centered" aria-live="polite"><div className="spc-pulse-icon"><Package aria-hidden="true" size={38} /></div><span className="spc-eyebrow">Keep the cable connected</span><h1 ref={headingRef} tabIndex={-1}>Installing {selectedApp.name}</h1><p className="spc-lead">Sideport is preparing, signing, installing, and checking the app on the iPhone. No more choices are needed.</p><div aria-label="Install progress" aria-valuemax={100} aria-valuemin={0} aria-valuenow={75} className="spc-install-track" role="progressbar"><span /><span /><span /></div><div className="spc-wait-status"><RotateCw aria-hidden="true" size={18} /> Verifying the installed app…</div></section> : null}

        {step === 'done' ? <section className="spc-assistant-content centered success-screen"><div className="spc-success-orbit"><Check aria-hidden="true" size={42} /></div><span className="spc-eyebrow">Simulated device-verified result</span><h1 ref={headingRef} tabIndex={-1}>Installed — you can unplug</h1><p className="spc-lead">{selectedApp.name} {selectedApp.version} is ready on {memberName}’s iPhone. Sideport will attempt future refreshes over paired home Wi-Fi; reconnect the cable whenever one cannot finish.</p><div className="spc-completion-list"><span><Volume2 aria-hidden="true" size={19} /><strong>Completion chime would play</strong><small>Best effort when the browser allows audio</small></span><span><Wifi aria-hidden="true" size={19} /><strong>Paired Wi-Fi attempted</strong><small>Cable remains the reliable fallback</small></span><span><ShieldCheck aria-hidden="true" size={19} /><strong>Device verification represented</strong><small>Bundle, version, and expiry reread</small></span></div><aside className="spc-inline-note"><Info aria-hidden="true" size={18} /><div><strong>When opening the app the first time</strong><span>If iOS asks you to trust the developer profile, follow the message on the iPhone. Sideport verifies installation, not successful launch.</span></div></aside><button className="spc-button primary large" onClick={onFinish} type="button">Open Sideport</button></section> : null}
      </main>
    </div>
  )
}

function InvitationExperience({ memberName, onAccepted, onExistingSignIn, state }: { memberName: string; onAccepted: () => void; onExistingSignIn: () => void; state: CanonicalInvitationState }) {
  const headingId = useId()
  const [stage, setStage] = useState<'entry' | 'confirm'>('entry')
  const content: Record<CanonicalInvitationState, { eyebrow: string; title: string; lead: string }> = {
    ready: {
      eyebrow: 'Private Sideport invitation',
      title: 'Dragos invited you to Sideport',
      lead: `Sideport installs approved apps on ${memberName}’s iPhone and attempts refreshes over paired home Wi-Fi, with the cable as the reliable fallback.`,
    },
    expired: {
      eyebrow: 'Invitation expired',
      title: 'Ask Dragos for a new link',
      lead: 'This private invitation is no longer valid. Sideport has not created an account or changed any device.',
    },
    used: {
      eyebrow: 'Invitation already used',
      title: 'This invitation has already been used',
      lead: 'It cannot add another account. If you accepted it earlier, continue to Authentik; Sideport confirms the signed-in account before showing membership.',
    },
    suspended: {
      eyebrow: 'Access paused',
      title: 'Your Sideport access is paused',
      lead: 'No new installs or account actions are available. Ask Dragos to review your Member access.',
    },
    recovery: {
      eyebrow: 'Sign-in recovery',
      title: 'Recover your Authentik sign-in',
      lead: 'Authentik verifies your account and manages passkey recovery. Sideport cannot create, reset, or read your passkey.',
    },
  }

  if (state === 'ready' && stage === 'confirm') {
    return (
      <div className="spc-invitation" data-testid="canonical-member-invitation">
        <header><Brand /><ProposedBadge /></header>
        <main>
          <SimulationNotice />
          <div className="spc-invite-illustration"><CircleUserRound aria-hidden="true" size={44} /></div>
          <span className="spc-eyebrow">Signed in through Authentik</span>
          <h1 id={headingId}>Join Dragos’s Sideport?</h1>
          <p className="spc-lead">Check the account and access below before this invitation is used.</p>
          <div className="spc-passkey-card"><CircleUserRound aria-hidden="true" size={24} /><div><strong>Mara · mara@example.test</strong><span>This is the account that will receive Member access.</span></div></div>
          <div className="spc-passkey-card"><ShieldCheck aria-hidden="true" size={24} /><div><strong>Member access</strong><span>Install approved apps and add your first iPhone. Apple signing, app sources, and other people’s devices stay with Dragos.</span></div></div>
          <button className="spc-button primary large" onClick={onAccepted} type="button">Join Sideport</button>
          <button className="spc-text-button centered-button" onClick={() => setStage('entry')} type="button">Use a different account</button>
          <p className="spc-fine-print">This confirmation uses an opaque, short-lived handoff. The private invitation is not kept in browser storage.</p>
        </main>
      </div>
    )
  }

  const current = content[state]
  const showSignIn = state === 'ready' || state === 'used' || state === 'recovery'
  return (
    <div className="spc-invitation" data-testid="canonical-member-invitation">
      <header><Brand /><ProposedBadge /></header>
      <main>
        <SimulationNotice />
        <div className="spc-invite-illustration"><Users aria-hidden="true" size={44} /></div>
        <span className="spc-eyebrow">{current.eyebrow}</span>
        <h1 id={headingId}>{current.title}</h1>
        <p className="spc-lead">{current.lead}</p>
        {showSignIn ? <div className="spc-passkey-card"><ShieldCheck aria-hidden="true" size={24} /><div><strong>{state === 'recovery' ? 'Recovery stays with Authentik' : 'Continue to Authentik'}</strong><span>Authentik may use Face ID, Touch ID, Windows Hello, or your password manager. Sideport never receives your Apple password or passkey.</span></div></div> : null}
        {state === 'ready' ? <button className="spc-button primary large" onClick={() => setStage('confirm')} type="button">Continue to sign in</button> : null}
        {state === 'recovery' ? <button className="spc-button primary large" onClick={onExistingSignIn} type="button">Continue to sign-in recovery</button> : null}
        {state === 'used' ? <button className="spc-button primary large" onClick={onExistingSignIn} type="button">Continue to sign in</button> : null}
        {state === 'expired' ? <aside className="spc-inline-note"><Info aria-hidden="true" size={18} /><div><strong>A new invitation is required.</strong><span>Only the Sideport owner can create it.</span></div></aside> : null}
        {state === 'suspended' ? <aside className="spc-inline-note warning"><Info aria-hidden="true" size={18} /><div><strong>The owner must restore access.</strong><span>A new invitation cannot bypass a suspension.</span></div></aside> : null}
        {showSignIn ? <p className="spc-fine-print">Authentik owns sign-in and recovery. This is not official “Sign in with Apple.”</p> : null}
      </main>
    </div>
  )
}

function OwnerClaimExperience({ onAccepted, state }: { onAccepted: () => void; state: CanonicalOwnerClaimState }) {
  const [confirming, setConfirming] = useState(false)
  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState('')
  const recovery = state === 'recovery'
  if (!recovery) return (
    <div className="spc-invitation" data-testid="canonical-owner-claim">
      <header><Brand /><ProposedBadge /></header>
      <main>
        <SimulationNotice />
        <div className="spc-invite-illustration"><ShieldCheck aria-hidden="true" size={44} /></div>
        <span className="spc-eyebrow">First setup</span>
        <h1>Finish setting up Sideport</h1>
        <p className="spc-lead">Create the Owner passkey on this device. There is no setup token or link to copy.</p>
        <form className="spc-identity-form" onSubmit={(event) => { event.preventDefault(); onAccepted() }}>
          <label><span>Name</span><input autoComplete="name" onChange={(event) => setDisplayName(event.currentTarget.value)} placeholder="Your name" value={displayName} /></label>
          <label><span>Email</span><input autoComplete="email" inputMode="email" onChange={(event) => setEmail(event.currentTarget.value)} placeholder="you@example.com" type="email" value={email} /></label>
          <button className="spc-button primary large" disabled={!displayName.trim() || !email.trim()} type="submit">Create passkey</button>
        </form>
        <p className="spc-fine-print">Your passkey stays on your trusted device. Sideport never receives your Apple password.</p>
      </main>
    </div>
  )
  return (
    <div className="spc-invitation" data-testid="canonical-owner-claim">
      <header><Brand /><ProposedBadge /></header>
      <main>
        <SimulationNotice />
        <div className="spc-invite-illustration"><ShieldCheck aria-hidden="true" size={44} /></div>
        <span className="spc-eyebrow">{confirming ? 'Signed in through Authentik' : 'Private owner recovery link'}</span>
        <h1>{confirming ? 'Recover owner access?' : 'Recover Sideport owner access'}</h1>
        <p className="spc-lead">{confirming ? 'Confirm the signed-in account before Sideport changes owner access.' : 'This short-lived, single-use link was created from the Sideport host. The long-lived recovery key stays out of the browser.'}</p>
        {confirming ? <>
          <div className="spc-passkey-card"><CircleUserRound aria-hidden="true" size={24} /><div><strong>Mara · mara@example.test</strong><span>This Authentik account will be the one active Sideport owner.</span></div></div>
          <div className="spc-passkey-card"><ShieldCheck aria-hidden="true" size={24} /><div><strong>Owner access</strong><span>Manage member access, Apple signing, approved apps, every iPhone, and technical settings.</span></div></div>
          <aside className="spc-inline-note warning"><Info aria-hidden="true" size={18} /><div><strong>Dragos will lose Owner access and be signed out.</strong><span>2 Members, 2 iPhones, 3 installed apps, and their activity stay in Sideport. No app is removed and automatic refresh history is retained.</span></div></aside>
          <button className="spc-button primary large" onClick={onAccepted} type="button">Recover owner access</button>
          <button className="spc-text-button centered-button" onClick={() => setConfirming(false)} type="button">Use a different account</button>
        </> : <>
          <aside className="spc-inline-note warning"><Info aria-hidden="true" size={18} /><div><strong>The current owner will be signed out.</strong><span>Apps, iPhones, signing state, and activity are retained. Review the exact impact before the host creates this link.</span></div></aside>
          <div className="spc-passkey-card"><ShieldCheck aria-hidden="true" size={24} /><div><strong>Continue to Authentik</strong><span>Authentik proves your account. Sideport then asks once more before granting Owner access.</span></div></div>
          <button className="spc-button primary large" onClick={() => setConfirming(true)} type="button">Continue to sign in</button>
        </>}
        <p className="spc-fine-print">No API key, Apple password, or passkey is entered on this page.</p>
      </main>
    </div>
  )
}

function FirstRunExperience({ onContinue }: { onContinue: () => void }) {
  const [technical, setTechnical] = useState(false)
  const [appleForm, setAppleForm] = useState(false)
  const [email, setEmail] = useState('owner@example.test')
  const [password, setPassword] = useState('storybook-demo')
  const headingRef = useRef<HTMLHeadingElement>(null)
  useEffect(() => { headingRef.current?.focus() }, [appleForm])
  const submitApple = (event: FormEvent) => {
    event.preventDefault()
    setPassword('')
    onContinue()
  }
  return (
    <div className="spc-first-run" data-testid="canonical-first-run">
      <header><Brand /><ProposedBadge /></header>
      <main>
        <SimulationNotice />
        {!appleForm ? <><div className="spc-setup-visual"><Laptop aria-hidden="true" size={42} /><span><Check aria-hidden="true" size={18} /></span></div><span className="spc-eyebrow">First-time setup</span><h1 ref={headingRef} tabIndex={-1}>Welcome to Sideport</h1><p className="spc-lead">This home server is ready. Connect one Apple account for signing, then use the nearby cable to install the first app.</p><section className="spc-readiness-list" aria-label="Setup readiness"><div><span className="spc-round-icon success"><Check aria-hidden="true" size={20} /></span><span><strong>Sideport is running</strong><small>Saved data and iPhone trust survive restarts.</small></span><StatusPill>Ready</StatusPill></div><div><span className="spc-round-icon"><Apple aria-hidden="true" size={20} /></span><span><strong>Apple signing</strong><small>Use the owner’s Apple account; members never see it.</small></span><StatusPill tone="warning">Next</StatusPill></div><div><span className="spc-round-icon"><Cable aria-hidden="true" size={20} /></span><span><strong>First iPhone and app</strong><small>The same guided cable assistant handles both.</small></span><StatusPill tone="quiet">Later</StatusPill></div></section><button aria-expanded={technical} className="spc-disclosure" onClick={() => setTechnical((value) => !value)} type="button"><Info aria-hidden="true" size={17} /> {technical ? 'Hide installation details' : 'How this installation is saved'} <ChevronRight aria-hidden="true" size={17} /></button>{technical ? <section className="spc-technical-panel"><h2>Installation details</h2><p>Docker keeps Sideport state, working files, anisette identity, and iPhone pairing records in persistent storage. The proposed Apple Container path is experimental and remains unverified until Phase 15; it is expected to use the official CLI network, the existing amd64 image under Rosetta, native anisette, persistent state roots, and the host usbmuxd socket. Sideport does not yet claim working Apple Container device installation, raw USB passthrough, or native arm64 support.</p></section> : null}<button className="spc-button primary large" onClick={() => setAppleForm(true)} type="button">Connect Apple account</button></> : <form className="spc-apple-form" onSubmit={submitApple}><div className="spc-hero-icon"><Apple aria-hidden="true" size={38} /></div><span className="spc-eyebrow">Owner signing access</span><h1 ref={headingRef} tabIndex={-1}>Connect your Apple account</h1><p className="spc-lead">Sideport uses this Apple Developer access to register iPhones, manage App IDs, certificates, and profiles, and sign approved apps. The protected credential stays in server-side custody and is separate from member sign-in.</p><div className="spc-inline-note warning" id="storybook-credential-warning" role="note"><Info aria-hidden="true" size={18} /><div><strong>Demo only — do not enter real credentials.</strong><span>These example fields make no request and store nothing.</span></div></div><label htmlFor="owner-apple-email">Demo Apple Account email</label><input aria-describedby="storybook-credential-warning" autoComplete="off" id="owner-apple-email" onChange={(event) => setEmail(event.currentTarget.value)} type="email" value={email} /><label htmlFor="owner-apple-password">Demo password</label><input aria-describedby="storybook-credential-warning" autoComplete="off" id="owner-apple-password" onChange={(event) => setPassword(event.currentTarget.value)} type="password" value={password} /><div className="spc-inline-note" role="note"><Apple aria-hidden="true" size={18} /><div><strong>Team selection is automatic.</strong><span>Sideport uses the only team Apple returns. A chooser appears only if Apple returns more than one.</span></div></div><button className="spc-button primary large" disabled={!email || !password} type="submit">Continue demo</button><button className="spc-text-button centered-button" onClick={() => { setPassword('storybook-demo'); setAppleForm(false) }} type="button">Back</button></form>}
      </main>
    </div>
  )
}

export function CanonicalSideport({
  role = 'owner',
  experience = 'shell',
  initialRoute = 'home',
  initialAssistantStep = 'connect',
  invitationState = 'ready',
  ownerClaimState = 'setup',
  memberName = 'Mara',
}: CanonicalSideportProps) {
  const [currentExperience, setCurrentExperience] = useState<CanonicalExperience>(experience)
  const [assistantStep, setAssistantStep] = useState<CanonicalAssistantStep>(initialAssistantStep)
  const [assistantAppId, setAssistantAppId] = useState('cert-clock')
  const [assistantMemberName, setAssistantMemberName] = useState(memberName)
  const [currentRole, setCurrentRole] = useState<CanonicalRole>(role)

  const startAssistant = (step: CanonicalAssistantStep = 'connect', appId = 'cert-clock', targetMemberName = memberName) => {
    setAssistantStep(step)
    setAssistantAppId(appId)
    setAssistantMemberName(targetMemberName)
    setCurrentExperience('add-iphone')
  }
  if (currentExperience === 'invitation') return <InvitationExperience memberName={memberName} onAccepted={() => { setCurrentRole('member'); startAssistant('connect') }} onExistingSignIn={() => { setCurrentRole('member'); setCurrentExperience('shell') }} state={invitationState} />
  if (currentExperience === 'owner-claim') return <OwnerClaimExperience onAccepted={() => { setCurrentRole('owner'); setCurrentExperience(ownerClaimState === 'setup' ? 'first-run' : 'shell') }} state={ownerClaimState} />
  if (currentExperience === 'first-run') return <FirstRunExperience onContinue={() => { setCurrentRole('owner'); startAssistant('connect') }} />
  if (currentExperience === 'add-iphone') return <IPhoneAssistant initialAppId={assistantAppId} initialStep={assistantStep} memberName={assistantMemberName} onClose={() => setCurrentExperience('shell')} onFinish={() => setCurrentExperience('shell')} />
  return <SignedInShell initialRoute={initialRoute} memberName={memberName} onStartAssistant={startAssistant} role={currentRole} />
}

export { NAVIGATION as CANONICAL_NAVIGATION }
