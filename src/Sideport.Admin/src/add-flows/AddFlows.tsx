import { useCallback, useEffect, useMemo, useRef, useState, type RefObject } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import * as Popover from '@radix-ui/react-popover'
import {
  Cable,
  CheckCircle2,
  ChevronRight,
  FileUp,
  GitBranch,
  HardDrive,
  Loader2,
  LockKeyhole,
  Package,
  Plus,
  RefreshCw,
  ShieldCheck,
  Smartphone,
  Volume2,
  X,
} from 'lucide-react'

export type AddAppSource = 'upload' | 'server' | 'github'

interface GlobalAddMenuProps {
  onAddIPhone: () => void
  onAddApp?: () => void
  triggerRef?: RefObject<HTMLButtonElement | null>
}

export function GlobalAddMenu({ onAddIPhone, onAddApp, triggerRef }: GlobalAddMenuProps) {
  const [open, setOpen] = useState(false)
  const localTriggerRef = useRef<HTMLButtonElement>(null)
  const resolvedTriggerRef = triggerRef ?? localTriggerRef
  const firstItemRef = useRef<HTMLButtonElement>(null)

  const choose = (action: () => void) => {
    setOpen(false)
    resolvedTriggerRef.current?.focus()
    action()
  }

  return (
    <Popover.Root onOpenChange={setOpen} open={open}>
      <Popover.Trigger asChild>
        <button aria-label="Add to Sideport" className="primary-action add-menu-trigger" data-testid="global-add-trigger" ref={resolvedTriggerRef} type="button">
          <Plus size={17} /> Add
        </button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          align="end"
          aria-label="Add to Sideport choices"
          className="add-menu-popover"
          data-testid="global-add-menu"
          onOpenAutoFocus={(event) => {
            event.preventDefault()
            firstItemRef.current?.focus()
          }}
          role="group"
          sideOffset={8}
        >
          <div className="add-menu-heading">Add to Sideport</div>
          <button className="add-menu-item" onClick={() => choose(onAddIPhone)} ref={firstItemRef} type="button">
            <span className="add-menu-icon"><Smartphone size={18} /></span>
            <span><strong>Add iPhone</strong><small>Connect once with USB</small></span>
            <ChevronRight size={16} />
          </button>
          {onAddApp ? <button className="add-menu-item" onClick={() => choose(onAddApp)} type="button">
            <span className="add-menu-icon"><FileUp size={18} /></span>
            <span><strong>Add app</strong><small>Choose where the app comes from</small></span>
            <ChevronRight size={16} />
          </button> : null}
          <Popover.Arrow className="add-menu-arrow" />
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  )
}

type IPhonePhase = 'idle' | 'waiting' | 'trust' | 'verifying' | 'selection' | 'accepted' | 'failed' | 'recovery'

export interface EnrollmentCandidate {
  udid?: string
  udidSuffix: string
  name: string
  productType?: string | null
  osVersion?: string | null
  connection: string
}

export interface EnrollmentOperation {
  operationId: string
  status: string
  stages: Array<{ id: string; status: string; message: string }>
  result?: { deviceEnrollment?: { selectedDeviceUdid?: string | null; inventoryState?: string | null } | null } | null
  error?: { code?: string; message?: string } | null
  candidateDevices?: EnrollmentCandidate[] | null
  retryable?: boolean
}

export interface AddIPhoneServices {
  start: (deviceUdid?: string) => Promise<EnrollmentOperation>
  read: (operationId: string) => Promise<EnrollmentOperation>
  retry?: (operationId: string) => Promise<EnrollmentOperation>
  selectCandidate?: (candidate: EnrollmentCandidate) => Promise<EnrollmentOperation>
}

export type IPhoneSoundCue = 'listening' | 'detected' | 'attention'
export type IPhoneSoundPlayer = (cue: IPhoneSoundCue) => void

interface AddIPhoneDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  demoMode: boolean
  autoStart?: boolean
  canMutate?: boolean
  services?: AddIPhoneServices
  onAccepted?: (operation: EnrollmentOperation | null) => void
  onContinue?: () => void
  resumeOperationId?: string | null
  persistenceKey?: string
  returnFocusRef?: RefObject<HTMLElement | null>
  memberName?: string
  soundPlayer?: IPhoneSoundPlayer
  attentionDelayMs?: number
}

const ACTIVE_ENROLLMENT_STATUSES = new Set(['queued', 'waiting', 'running'])

function enrollmentPhase(operation: EnrollmentOperation): IPhonePhase {
  if (operation.result?.deviceEnrollment?.inventoryState === 'accepted' || operation.status === 'succeeded') return 'accepted'
  if (operation.error?.code === 'device-selection-required' && operation.candidateDevices?.length) return 'selection'
  if (operation.status === 'recovery-required' || operation.error?.code === 'device-enrollment-recovery-required') return 'recovery'
  if (operation.status === 'failed' || operation.status === 'blocked' || operation.status === 'canceled') return 'failed'
  const activeStage = operation.stages.find((stage) => ['waiting', 'running'].includes(stage.status))?.id
  if (activeStage === 'request-pairing' || activeStage === 'await-user-trust') return 'trust'
  if (activeStage === 'verify-lockdown' || activeStage === 'accept-device') return 'verifying'
  return 'waiting'
}

function isResumableEnrollment(operation: EnrollmentOperation | null): boolean {
  return Boolean(operation && (ACTIVE_ENROLLMENT_STATUSES.has(operation.status) || operation.status === 'recovery-required' || operation.retryable))
}

function enrollmentStorageKey(persistenceKey: string | undefined): string | null {
  return persistenceKey ? `sideport.device-enrollment.v1:${persistenceKey}` : null
}

function readEnrollmentOperationId(key: string | null): string | null {
  if (!key) return null
  try {
    const value = window.sessionStorage.getItem(key)?.trim() ?? ''
    return value && value.length <= 256 ? value : null
  } catch {
    return null
  }
}

function rememberEnrollmentOperationId(key: string | null, operationId: string | null): void {
  if (!key) return
  try {
    if (operationId) window.sessionStorage.setItem(key, operationId)
    else window.sessionStorage.removeItem(key)
  } catch {
    // The onboarding workflow remains the durable resume source.
  }
}

function iPhoneOwnerLabel(memberName: string | undefined): string {
  const name = memberName?.trim()
  return name ? `${name}’s iPhone` : 'your iPhone'
}

export function AddIPhoneDialog({ open, onOpenChange, demoMode, autoStart = false, canMutate = false, services, onAccepted, onContinue, resumeOperationId, persistenceKey, returnFocusRef, memberName, soundPlayer, attentionDelayMs = 8_000 }: AddIPhoneDialogProps) {
  const [phase, setPhase] = useState<IPhonePhase>('idle')
  const [operation, setOperation] = useState<EnrollmentOperation | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [waitingLong, setWaitingLong] = useState(false)
  const [pollRevision, setPollRevision] = useState(0)
  const acceptedNotifiedRef = useRef<string | null>(null)
  const autoStartHandledRef = useRef(false)
  const autoRecoveryAttemptedRef = useRef<string | null>(null)
  const detectedCuePlayedRef = useRef(false)
  const attentionCuePlayedRef = useRef(false)
  const audioContextRef = useRef<AudioContext | null>(null)
  const storageKey = enrollmentStorageKey(persistenceKey)
  const ownerLabel = iPhoneOwnerLabel(memberName)

  const playCue = useCallback((cue: IPhoneSoundCue) => {
    if (soundPlayer) {
      soundPlayer(cue)
      return
    }
    try {
      const AudioContextClass = window.AudioContext ?? (window as typeof window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
      if (!AudioContextClass) return
      const context = audioContextRef.current ?? new AudioContextClass()
      audioContextRef.current = context
      void context.resume()
      const patterns: Record<IPhoneSoundCue, Array<{ frequency: number; offset: number; duration: number; volume: number }>> = {
        listening: [
          { frequency: 440, offset: 0, duration: 0.09, volume: 0.055 },
          { frequency: 554, offset: 0.11, duration: 0.12, volume: 0.065 },
        ],
        detected: [
          { frequency: 523, offset: 0, duration: 0.1, volume: 0.07 },
          { frequency: 659, offset: 0.1, duration: 0.12, volume: 0.08 },
          { frequency: 784, offset: 0.22, duration: 0.16, volume: 0.075 },
        ],
        attention: [
          { frequency: 330, offset: 0, duration: 0.12, volume: 0.055 },
          { frequency: 262, offset: 0.16, duration: 0.16, volume: 0.05 },
        ],
      }
      for (const note of patterns[cue]) {
        const oscillator = context.createOscillator()
        const gain = context.createGain()
        const start = context.currentTime + note.offset
        oscillator.type = 'sine'
        oscillator.frequency.setValueAtTime(note.frequency, start)
        gain.gain.setValueAtTime(0.0001, start)
        gain.gain.exponentialRampToValueAtTime(note.volume, start + 0.015)
        gain.gain.exponentialRampToValueAtTime(0.0001, start + note.duration)
        oscillator.connect(gain).connect(context.destination)
        oscillator.start(start)
        oscillator.stop(start + note.duration + 0.02)
      }
    } catch {
      // Audio is best effort; visual and server-verified state remain authoritative.
    }
  }, [soundPlayer])
  const handleOpenChange = (nextOpen: boolean) => {
    if (!nextOpen) setWaitingLong(false)
    if (!nextOpen && !isResumableEnrollment(operation)) {
      setPhase('idle')
      setOperation(null)
      setError(null)
      acceptedNotifiedRef.current = null
    }
    onOpenChange(nextOpen)
  }

  useEffect(() => {
    if (!open) {
      autoStartHandledRef.current = false
      autoRecoveryAttemptedRef.current = null
      if (audioContextRef.current) {
        void audioContextRef.current.close()
        audioContextRef.current = null
      }
    }
  }, [open])

  useEffect(() => {
    if (!open || demoMode || !services) return
    const operationId = resumeOperationId ?? readEnrollmentOperationId(storageKey)
    if (!operationId || operation?.operationId === operationId) return
    let cancelled = false
    void services.read(operationId).then((next) => {
      if (cancelled) return
      setOperation(next)
      setPhase(enrollmentPhase(next))
      setError(next.error?.message ?? null)
      rememberEnrollmentOperationId(storageKey, isResumableEnrollment(next) ? next.operationId : null)
    }).catch((reason) => {
      if (!cancelled) {
        setError(reason instanceof Error ? reason.message : 'Sideport could not resume the iPhone connection yet.')
        window.setTimeout(() => setPollRevision((revision) => revision + 1), 1_500)
      }
    })
    return () => { cancelled = true }
  }, [demoMode, open, operation?.operationId, pollRevision, resumeOperationId, services, storageKey])

  useEffect(() => {
    if (!open || !demoMode) return
    const delay = phase === 'waiting' ? 650 : phase === 'trust' ? 1_100 : null
    if (delay === null) return
    const timeout = window.setTimeout(() => setPhase(phase === 'waiting' ? 'trust' : 'accepted'), delay)
    return () => window.clearTimeout(timeout)
  }, [demoMode, open, phase])

  useEffect(() => {
    if (!open || demoMode || !operation || !services || !ACTIVE_ENROLLMENT_STATUSES.has(operation.status)) return
    let cancelled = false
    const timeout = window.setTimeout(async () => {
      try {
        const next = await services.read(operation.operationId)
        if (cancelled) return
        setOperation(next)
        setPhase(enrollmentPhase(next))
        setError(next.error?.message ?? null)
        rememberEnrollmentOperationId(storageKey, isResumableEnrollment(next) ? next.operationId : null)
      } catch (reason) {
        if (!cancelled) {
          setError(`${reason instanceof Error ? reason.message : 'Sideport could not check the iPhone connection.'} Sideport will keep checking this connection.`)
          setPollRevision((revision) => revision + 1)
        }
      }
    }, 1_000)
    return () => {
      cancelled = true
      window.clearTimeout(timeout)
    }
  }, [demoMode, open, operation, pollRevision, services, storageKey])

  useEffect(() => {
    if (!open || demoMode || phase !== 'recovery' || !operation || !services?.retry || !canMutate) return
    if (autoRecoveryAttemptedRef.current === operation.operationId) return
    autoRecoveryAttemptedRef.current = operation.operationId
    setError(null)
    setPhase('waiting')
    void services.retry(operation.operationId).then((next) => {
      setOperation(next)
      setPhase(enrollmentPhase(next))
      setError(next.error?.message ?? null)
      rememberEnrollmentOperationId(storageKey, isResumableEnrollment(next) ? next.operationId : null)
    }).catch((reason) => {
      setPhase('recovery')
      setError(reason instanceof Error ? reason.message : 'Sideport is still waiting to verify this iPhone safely.')
    })
  }, [canMutate, demoMode, open, operation, phase, services, storageKey])

  useEffect(() => {
    if (!open || phase !== 'waiting') return
    const timeout = window.setTimeout(() => {
      setWaitingLong(true)
      if (!attentionCuePlayedRef.current) {
        attentionCuePlayedRef.current = true
        playCue('attention')
      }
    }, attentionDelayMs)
    return () => window.clearTimeout(timeout)
  }, [attentionDelayMs, open, phase, playCue])

  useEffect(() => {
    if (!open) return
    if (['trust', 'verifying', 'accepted'].includes(phase) && !detectedCuePlayedRef.current) {
      detectedCuePlayedRef.current = true
      playCue('detected')
    }
    if (['failed', 'recovery'].includes(phase) && !attentionCuePlayedRef.current) {
      attentionCuePlayedRef.current = true
      playCue('attention')
    }
  }, [open, phase, playCue])

  useEffect(() => {
    if (phase !== 'accepted') return
    const key = operation?.operationId ?? 'demo'
    if (acceptedNotifiedRef.current === key) return
    acceptedNotifiedRef.current = key
    onAccepted?.(operation)
  }, [onAccepted, operation, phase])

  const beginEnrollment = useCallback(async () => {
    setError(null)
    setWaitingLong(false)
    detectedCuePlayedRef.current = false
    attentionCuePlayedRef.current = false
    playCue('listening')
    if (demoMode) {
      setPhase('waiting')
      return
    }
    if (!services || !canMutate) return
    setPhase('waiting')
    try {
      const next = await services.start()
      setOperation(next)
      setPhase(enrollmentPhase(next))
      setError(next.error?.message ?? null)
      rememberEnrollmentOperationId(storageKey, isResumableEnrollment(next) ? next.operationId : null)
    } catch (reason) {
      setPhase('failed')
      setError(reason instanceof Error ? reason.message : 'Sideport could not start the iPhone connection.')
    }
  }, [canMutate, demoMode, playCue, services, storageKey])

  const autoStartEnrollment = () => {
    // Radix invokes this once for each explicit dialog opening. Using that
    // single-fire UI boundary avoids effect/remount races while the existing
    // operation store and polling continue to own resume behavior.
    if (!autoStart || autoStartHandledRef.current || phase !== 'idle' || operation) return
    // The onboarding trigger is disabled without this capability. If it changes
    // between the click and dialog mount, fail closed and start nothing.
    if (!demoMode && (!services || !canMutate)) return
    const resumableOperationId = resumeOperationId ?? readEnrollmentOperationId(storageKey)
    autoStartHandledRef.current = true
    if (resumableOperationId) return
    void beginEnrollment()
  }

  const chooseCandidate = async (candidate: EnrollmentCandidate) => {
    setError(null)
    if (demoMode) {
      setPhase('accepted')
      return
    }
    if (!services?.selectCandidate || !canMutate) {
      setError('Sideport cannot safely identify this iPhone from the shortened device number. Leave only one new iPhone connected, close this window, then start again.')
      return
    }
    setPhase('waiting')
    try {
      const next = await services.selectCandidate(candidate)
      setOperation(next)
      setPhase(enrollmentPhase(next))
      setError(next.error?.message ?? null)
      rememberEnrollmentOperationId(storageKey, isResumableEnrollment(next) ? next.operationId : null)
    } catch (reason) {
      setPhase('selection')
      setError(reason instanceof Error ? reason.message : 'Sideport could not safely match that iPhone. Leave only one new iPhone connected, then try again.')
    }
  }

  const activeIndex = phase === 'idle' || phase === 'waiting' || phase === 'selection' || phase === 'failed' || phase === 'recovery' ? 0 : phase === 'trust' ? 1 : phase === 'verifying' ? 2 : 3
  const connectionActive = ['waiting', 'trust', 'verifying', 'recovery'].includes(phase)
  const waitingTitle = phase === 'trust'
    ? `${ownerLabel} found`
    : phase === 'verifying'
      ? 'Trust received'
      : phase === 'recovery'
        ? `Still checking ${ownerLabel}`
        : `Waiting for ${ownerLabel}…`
  const waitingMessage = phase === 'trust'
    ? `Tap Trust on ${ownerLabel} and enter the iPhone passcode. Sideport will continue by itself.`
    : phase === 'verifying'
      ? `Keep ${ownerLabel} connected and unlocked while Sideport finishes the secure check.`
      : phase === 'recovery'
        ? `Keep ${ownerLabel} connected and unlocked. Sideport is checking the existing Trust request without pairing again.`
        : waitingLong
          ? 'Still waiting. Check that the iPhone is unlocked, charging, and connected with a data-capable USB cable.'
          : 'Connect it with USB and keep it unlocked. Sideport will notice it and continue automatically.'
  const progress = [
    { label: 'Connect', detail: phase === 'waiting' ? 'Listening' : 'USB' },
    { label: 'Trust', detail: 'On iPhone' },
    { label: 'Ready', detail: 'Automatic' },
  ]

  return (
    <Dialog.Root onOpenChange={handleOpenChange} open={open}>
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay add-flow-overlay" />
        <Dialog.Content
          className="dialog-content add-flow-dialog"
          data-testid="add-iphone-dialog"
          onOpenAutoFocus={autoStartEnrollment}
          onCloseAutoFocus={(event) => {
            if (!returnFocusRef?.current) return
            event.preventDefault()
            returnFocusRef.current.focus()
          }}
        >
          <div className="add-flow-title-row">
            <span className="add-flow-title-icon"><Smartphone size={21} /></span>
            <div>
              <Dialog.Title>Add an iPhone</Dialog.Title>
              <Dialog.Description>Connect once with USB. Sideport will wait, notice Trust, and add the iPhone automatically.</Dialog.Description>
            </div>
            <Dialog.Close asChild><button aria-label="Close Add iPhone" className="add-flow-close" type="button"><X size={18} /></button></Dialog.Close>
          </div>

          <ol aria-label="iPhone connection progress" className="add-flow-progress">
            {progress.map((step, index) => {
              const state = activeIndex > index ? 'done' : activeIndex === index && phase !== 'idle' ? 'active' : 'pending'
              return (
                <li className={state} key={step.label}>
                  <span>{state === 'done' ? <CheckCircle2 size={18} /> : state === 'active' ? <RefreshCw className="stage-spin" size={17} /> : index + 1}</span>
                  <strong>{step.label}</strong>
                  <small>{state === 'done' ? 'Done' : state === 'active' ? step.detail : 'Next'}</small>
                </li>
              )
            })}
          </ol>

          {phase === 'accepted' ? (
            <div className="add-flow-success" role="status">
              <CheckCircle2 size={22} />
              <div><strong>{ownerLabel} is ready</strong><span>Sideport verified Trust and added the iPhone automatically. Continue to the approved app library when you’re ready.</span></div>
            </div>
          ) : phase === 'selection' && operation?.candidateDevices?.length ? (
            <div className="add-flow-candidates" role="group" aria-label="Connected iPhones">
              <strong>Choose the iPhone to add</strong>
              <small>More than one new iPhone is connected. Sideport will pair only the one you choose.</small>
              {operation.candidateDevices.map((candidate) => (
                <button disabled={!demoMode && (!services?.selectCandidate || !canMutate)} key={`${candidate.name}:${candidate.udidSuffix}`} onClick={() => void chooseCandidate(candidate)} type="button">
                  <span className="add-menu-icon"><Smartphone size={18} /></span>
                  <span><strong>{candidate.name}</strong><small>{candidate.productType ?? 'iPhone'} · iOS {candidate.osVersion ?? 'unknown'} · …{candidate.udidSuffix}</small></span>
                  <ChevronRight size={17} />
                </button>
              ))}
              {!demoMode && !services?.selectCandidate && <p className="data-boundary-note">Sideport cannot safely expand these shortened device numbers. Leave only one new iPhone connected, close this window, then start again.</p>}
            </div>
          ) : connectionActive ? (
            <div aria-atomic="true" aria-live="polite" className={`add-flow-waiting ${phase === 'waiting' && waitingLong || phase === 'recovery' ? 'attention' : ''}`} role="status">
              <span className="add-flow-waiting-icon"><Smartphone aria-hidden="true" size={28} /></span>
              <div><strong>{waitingTitle}</strong><span>{waitingMessage}</span></div>
            </div>
          ) : (
            <div className="add-flow-guide">
              <div><span>1</span><div><strong>Connect and unlock</strong><small>Use a USB cable and keep the iPhone unlocked nearby.</small></div></div>
              <div><span>2</span><div><strong>Trust this computer</strong><small>When your iPhone asks, tap Trust and enter its passcode. Sideport continues automatically.</small></div></div>
            </div>
          )}

          <section className="add-flow-device-mode" aria-label="Developer Mode guidance">
            <strong>Before the first app</strong>
            <p>On the iPhone, open Settings → Privacy &amp; Security → Developer Mode. Turn it on, restart, then unlock, tap Enable, enter the passcode, and reconnect USB if needed.</p>
          </section>

          <details className="add-flow-advanced">
            <summary>Technical details</summary>
            <p>USB is used for the first trusted connection and install. After pairing, Sideport can also find the iPhone over the same Wi-Fi network, with USB as the fallback.</p>
          </details>

          <p className="add-flow-sound-note"><Volume2 aria-hidden="true" size={16} /> Sideport uses gentle sound cues for listening, detection, and attention when browser audio is available.</p>

          {error && (phase === 'failed' || phase === 'selection') && <p className="mutation-message error" role="alert">{error}</p>}
          {!demoMode && (!services || !canMutate) && <p className="mutation-message">Sign in to a protected Sideport session before adding an iPhone.</p>}
          <p aria-atomic="true" aria-live="polite" className="visually-hidden">
            {phase === 'waiting' ? `Waiting for ${ownerLabel}.` : phase === 'trust' ? `${ownerLabel} found. Tap Trust This Computer and enter the passcode.` : phase === 'recovery' ? `Sideport is checking the existing Trust request for ${ownerLabel} automatically.` : phase === 'accepted' ? `${ownerLabel} added to Sideport.` : ''}
          </p>

          <div className="dialog-actions add-flow-actions">
            <Dialog.Close asChild><button className="ghost-action add-flow-button" type="button">Close</button></Dialog.Close>
            {phase === 'accepted' && onContinue && <button className="primary-action add-flow-button" onClick={onContinue} type="button">Continue <ChevronRight size={17} /></button>}
            {phase !== 'accepted' && phase !== 'selection' && phase !== 'idle' && phase !== 'failed' && (
              <button className="primary-action add-flow-button" disabled type="button">Continue <ChevronRight size={17} /></button>
            )}
            {(phase === 'idle' || phase === 'failed') && (
              <button
                className="primary-action add-flow-button"
                data-testid="connect-iphone-intent"
                disabled={!demoMode && (!services || !canMutate)}
                onClick={() => void beginEnrollment()}
                type="button"
              >
                <Cable size={17} /> {phase === 'failed' ? 'Try again' : 'Connect iPhone'}
              </button>
            )}
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

export interface AddAppCatalogItem {
  id: string
  name: string
  purpose: string
  versionLabel: string
  status: string
  iconTone?: string
  icon?: string
  artifactSources?: Array<{ kind: string; label: string; repository?: string | null; releaseTag?: string | null }>
}

export interface AppImportRoot {
  id: string
  label: string
  available: boolean
}

export interface GitHubSource {
  id: string
  repository: string
  visibility: 'public' | 'private' | string
  status: string
}

export interface GitHubProviderCapability {
  kind: string
  supported: boolean
  allowedNow: boolean
  blockedReason?: string | null
}

export interface GitHubSourceSnapshot {
  capability: GitHubProviderCapability
  sources: GitHubSource[]
}

export interface GitHubReleaseAsset {
  assetId: number
  name: string
  sizeBytes: number
  digest?: string | null
  importable: boolean
}

export interface GitHubRelease {
  releaseId: number
  tag: string
  name: string
  prerelease: boolean
  assets: GitHubReleaseAsset[]
}

export interface GitHubReleasePage {
  sourceId: string
  repository: string
  releases: GitHubRelease[]
}

export interface GitHubConnection {
  id: string
  repository: string
  visibility: string
  status: string
  sourceId?: string | null
  authorizationUrl?: string | null
  error?: string | null
}

export interface GitHubCallbackResume {
  connectionId: string
  sourceId?: string | null
}

export interface AddAppServices {
  loadImportRoots: () => Promise<AppImportRoot[]>
  upload: (file: File) => Promise<AddAppCatalogItem>
  importFromRoot: (rootId: string, relativePath: string) => Promise<AddAppCatalogItem>
  loadGitHubSources: () => Promise<GitHubSourceSnapshot>
  connectGitHub: (repository: string, visibility: 'public' | 'private') => Promise<GitHubConnection>
  readGitHubConnection?: (connectionId: string) => Promise<GitHubConnection>
  loadGitHubReleases: (sourceId: string) => Promise<GitHubReleasePage>
  importGitHub: (sourceId: string, releaseId: number, asset: GitHubReleaseAsset) => Promise<AddAppCatalogItem>
}

interface AddAppDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onChooseApp: (app: AddAppCatalogItem) => void
  catalogApps?: AddAppCatalogItem[]
  services?: AddAppServices
  canImport?: boolean
  canManageGitHub?: boolean
  demoMode: boolean
  githubCallback?: GitHubCallbackResume | null
  returnFocusRef?: RefObject<HTMLElement | null>
}

type GithubAccess = 'public' | 'private'

const APP_SOURCES: Array<{ id: AddAppSource; label: string; detail: string; icon: typeof FileUp }> = [
  { id: 'upload', label: 'Choose a file', detail: 'An IPA saved on this computer.', icon: FileUp },
  { id: 'server', label: 'Sideport storage', detail: 'An IPA in a configured location.', icon: HardDrive },
  { id: 'github', label: 'GitHub release', detail: 'An IPA published by a repository.', icon: GitBranch },
]

const githubRepositoryPattern = /^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/
const demoGitHubSource: GitHubSource = { id: 'demo-sideport', repository: 'dragoshont/sideport', visibility: 'private', status: 'connected' }
const demoReleasePage: GitHubReleasePage = {
  sourceId: demoGitHubSource.id,
  repository: demoGitHubSource.repository,
  releases: [{
    releaseId: 17,
    tag: 'sample-apps',
    name: 'Sample apps',
    prerelease: false,
    assets: [{ assetId: 41, name: 'Cert-Clock.ipa', sizeBytes: 18_432, digest: 'sha256:demo', importable: true }],
  }],
}

function formatBytes(bytes: number): string {
  if (bytes < 1_024) return `${bytes} B`
  if (bytes < 1_048_576) return `${Math.round(bytes / 1_024)} KB`
  return `${(bytes / 1_048_576).toFixed(1)} MB`
}

export function AddAppDialog({
  open,
  onOpenChange,
  onChooseApp,
  catalogApps = [],
  services,
  canImport = false,
  canManageGitHub = false,
  demoMode,
  githubCallback,
  returnFocusRef,
}: AddAppDialogProps) {
  const [source, setSource] = useState<AddAppSource | null>(null)
  const [selectedCatalogAppId, setSelectedCatalogAppId] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [roots, setRoots] = useState<AppImportRoot[]>([])
  const [rootId, setRootId] = useState('')
  const [relativePath, setRelativePath] = useState('')
  const [githubSources, setGithubSources] = useState<GitHubSource[]>([])
  const [githubCapability, setGithubCapability] = useState<GitHubProviderCapability | null>(demoMode ? { kind: 'demo', supported: true, allowedNow: true } : null)
  const [rootLoadError, setRootLoadError] = useState<string | null>(null)
  const [githubLoadError, setGithubLoadError] = useState<string | null>(null)
  const [githubSourceId, setGithubSourceId] = useState('')
  const [releasePage, setReleasePage] = useState<GitHubReleasePage | null>(null)
  const [selectedReleaseId, setSelectedReleaseId] = useState<number | null>(null)
  const [selectedAssetId, setSelectedAssetId] = useState<number | null>(null)
  const [githubAccess, setGithubAccess] = useState<GithubAccess>('public')
  const [repository, setRepository] = useState('')
  const [repositoryTouched, setRepositoryTouched] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const repositoryValid = githubRepositoryPattern.test(repository.trim())
  const readyApps = useMemo(() => catalogApps.filter((app) => app.status === 'ready'), [catalogApps])
  const effectiveSelectedCatalogAppId = selectedCatalogAppId || (!source ? readyApps[0]?.id ?? '' : '')
  const availableRoots = demoMode ? [{ id: 'sideport-library', label: 'Sideport app storage', available: true }] : roots
  const availableGitHubSources = demoMode ? [demoGitHubSource] : githubSources
  const selectedAsset = releasePage?.releases
    .find((release) => release.releaseId === selectedReleaseId)?.assets
    .find((asset) => asset.assetId === selectedAssetId)

  const reset = () => {
    setSource(null)
    setSelectedCatalogAppId(readyApps[0]?.id ?? '')
    setFile(null)
    setRoots([])
    setRootId('')
    setRelativePath('')
    setGithubSources([])
    setGithubCapability(demoMode ? { kind: 'demo', supported: true, allowedNow: true } : null)
    setRootLoadError(null)
    setGithubLoadError(null)
    setGithubSourceId('')
    setReleasePage(null)
    setSelectedReleaseId(null)
    setSelectedAssetId(null)
    setGithubAccess('public')
    setRepository('')
    setRepositoryTouched(false)
    setBusy(false)
    setError(null)
    setSuccess(null)
  }

  const handleOpenChange = (nextOpen: boolean) => {
    if (!nextOpen) reset()
    onOpenChange(nextOpen)
  }

  useEffect(() => {
    if (!open) return
    if (demoMode) return
    if (!services) return
    let cancelled = false
    void services.loadImportRoots()
      .then((nextRoots) => {
        if (cancelled) return
        setRoots(nextRoots)
        setRootId(nextRoots.find((root) => root.available)?.id ?? '')
      })
      .catch((reason) => {
        if (!cancelled) setRootLoadError(reason instanceof Error ? reason.message : 'Sideport could not load configured storage.')
      })
    void services.loadGitHubSources()
      .then((snapshot) => {
        if (cancelled) return
        setGithubCapability(snapshot.capability)
        setGithubSources(snapshot.sources)
      })
      .catch((reason) => {
        if (!cancelled) setGithubLoadError(reason instanceof Error ? reason.message : 'Sideport could not load GitHub sources.')
      })
    return () => { cancelled = true }
  }, [demoMode, open, services])

  useEffect(() => {
    if (!open || demoMode || !githubCallback) return
    let cancelled = false
    let timer: number | undefined
    let attempts = 0
    const poll = async (
      readConnection: (connectionId: string) => Promise<GitHubConnection>,
      loadSourceReleases: (sourceId: string) => Promise<GitHubReleasePage>,
    ) => {
      try {
        const connection = await readConnection(githubCallback.connectionId)
        if (cancelled) return
        if (connection.status === 'connected') {
          if (!connection.sourceId) throw new Error('GitHub connected, but Sideport did not return an app source.')
          if (githubCallback.sourceId && githubCallback.sourceId !== connection.sourceId) {
            throw new Error('The GitHub callback did not match the connected Sideport source. Start the repository connection again.')
          }
          const nextSource = { id: connection.sourceId, repository: connection.repository, visibility: connection.visibility, status: connection.status }
          setGithubSources((current) => [nextSource, ...current.filter((item) => item.id !== nextSource.id)])
          setGithubSourceId(connection.sourceId)
          const page = await loadSourceReleases(connection.sourceId)
          if (cancelled) return
          setReleasePage(page)
          const firstRelease = page.releases.find((release) => release.assets.some((asset) => asset.importable))
          const firstAsset = firstRelease?.assets.find((asset) => asset.importable)
          setSelectedReleaseId(firstRelease?.releaseId ?? null)
          setSelectedAssetId(firstAsset?.assetId ?? null)
          setSuccess('GitHub repository connected')
          setBusy(false)
          return
        }
        if (connection.status === 'failed' || connection.status === 'expired') {
          throw new Error(connection.error || 'GitHub did not complete this repository connection. Start it again.')
        }
        if (connection.status === 'authorization-required') {
          throw new Error('GitHub still needs repository approval. Start the private repository connection again.')
        }
        attempts += 1
        if (attempts >= 30) throw new Error('GitHub is taking longer than expected. Close this window and try the connection again.')
        timer = window.setTimeout(() => void poll(readConnection, loadSourceReleases), 1_000)
      } catch (reason) {
        if (cancelled) return
        setBusy(false)
        setSuccess(null)
        setError(reason instanceof Error ? reason.message : 'Sideport could not resume this GitHub connection.')
      }
    }
    timer = window.setTimeout(() => {
      if (cancelled) return
      setSource('github')
      setSelectedCatalogAppId('')
      setBusy(true)
      setError(null)
      setSuccess('Finishing the GitHub connection…')
      if (!canManageGitHub || !services?.readGitHubConnection) {
        setBusy(false)
        setSuccess(null)
        setError('Sideport cannot resume this GitHub connection in the current session. Sign in, then open Add app again.')
        return
      }
      void poll(services.readGitHubConnection, services.loadGitHubReleases)
    }, 0)
    return () => {
      cancelled = true
      if (timer !== undefined) window.clearTimeout(timer)
    }
  }, [canManageGitHub, demoMode, githubCallback, open, services])

  const chooseSource = (next: AddAppSource) => {
    setSource(next)
    setSelectedCatalogAppId('')
    setError(null)
    setSuccess(null)
  }

  const loadReleases = async (nextSourceId: string) => {
    setGithubSourceId(nextSourceId)
    setReleasePage(null)
    setSelectedReleaseId(null)
    setSelectedAssetId(null)
    setBusy(true)
    setError(null)
    try {
      const page = demoMode ? { ...demoReleasePage, sourceId: nextSourceId } : await services!.loadGitHubReleases(nextSourceId)
      setReleasePage(page)
      const firstRelease = page.releases.find((release) => release.assets.some((asset) => asset.importable))
      const firstAsset = firstRelease?.assets.find((asset) => asset.importable)
      setSelectedReleaseId(firstRelease?.releaseId ?? null)
      setSelectedAssetId(firstAsset?.assetId ?? null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Sideport could not load GitHub releases.')
    } finally {
      setBusy(false)
    }
  }

  const connectRepository = async () => {
    setRepositoryTouched(true)
    if (!repositoryValid || (!demoMode && (!services || !canManageGitHub || githubCapability?.allowedNow !== true))) return
    setBusy(true)
    setError(null)
    try {
      if (demoMode) {
        setSuccess('GitHub source connected')
        await loadReleases(demoGitHubSource.id)
        return
      }
      const connection = await services!.connectGitHub(repository.trim(), githubAccess)
      if (connection.status === 'authorization-required' && connection.authorizationUrl) {
        window.location.assign(connection.authorizationUrl)
        return
      }
      if (!connection.sourceId) throw new Error(connection.error || 'GitHub did not return a connected source.')
      const nextSource = { id: connection.sourceId, repository: connection.repository, visibility: connection.visibility, status: connection.status }
      setGithubSources((current) => [nextSource, ...current.filter((item) => item.id !== nextSource.id)])
      setSuccess('GitHub source connected')
      await loadReleases(connection.sourceId)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Sideport could not connect this repository.')
    } finally {
      setBusy(false)
    }
  }

  const importApp = async () => {
    if (!source || (!demoMode && (!services || !canImport))) return
    setBusy(true)
    setError(null)
    setSuccess(null)
    try {
      let imported: AddAppCatalogItem
      if (demoMode) {
        imported = { id: 'demo-import', name: file?.name.replace(/\.ipa$/i, '') || selectedAsset?.name.replace(/\.ipa$/i, '') || 'Imported app', purpose: 'Ready to install.', versionLabel: 'Inspected', status: 'ready' }
      } else if (source === 'upload' && file) {
        imported = await services!.upload(file)
      } else if (source === 'server' && rootId && relativePath.trim()) {
        imported = await services!.importFromRoot(rootId, relativePath.trim())
      } else if (source === 'github' && githubSourceId && selectedReleaseId && selectedAsset) {
        imported = await services!.importGitHub(githubSourceId, selectedReleaseId, selectedAsset)
      } else {
        throw new Error('Choose an app source before importing.')
      }
      setSuccess(`${imported.name} is ready in Sideport.`)
      onChooseApp(imported)
      handleOpenChange(false)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Sideport could not import this app.')
    } finally {
      setBusy(false)
    }
  }

  const chooseExisting = () => {
    if (!effectiveSelectedCatalogAppId) return
    const selected = readyApps.find((app) => app.id === effectiveSelectedCatalogAppId)
    if (!selected) return
    onChooseApp(selected)
    handleOpenChange(false)
  }

  const importDisabled = busy || (!demoMode && (!services || !canImport)) ||
    (source === 'upload' && !file) ||
    (source === 'server' && (!rootId || !relativePath.trim())) ||
    (source === 'github' && (!githubSourceId || !selectedReleaseId || !selectedAsset))

  return (
    <Dialog.Root onOpenChange={handleOpenChange} open={open}>
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay add-flow-overlay" />
        <Dialog.Content
          className="dialog-content add-flow-dialog add-app-dialog"
          data-testid="add-app-dialog"
          onCloseAutoFocus={(event) => {
            if (!returnFocusRef?.current) return
            event.preventDefault()
            returnFocusRef.current.focus()
          }}
        >
          <div className="add-flow-title-row">
            <span className="add-flow-title-icon"><Package size={21} /></span>
            <div>
              <Dialog.Title>Choose or add an app</Dialog.Title>
              <Dialog.Description>Start with an app already in Sideport, or add one from a trusted source.</Dialog.Description>
            </div>
            <Dialog.Close asChild><button aria-label="Close Add app" className="add-flow-close" type="button"><X size={18} /></button></Dialog.Close>
          </div>

          {readyApps.length > 0 && (
            <section className="add-library-section" aria-labelledby="sideport-library-heading">
              <div className="add-section-heading"><div><strong id="sideport-library-heading">Available in Sideport</strong><small>Already inspected and ready to install.</small></div></div>
              <div className="add-library-list" role="radiogroup" aria-label="Available Sideport apps">
                {readyApps.map((app) => (
                  <button aria-checked={!source && effectiveSelectedCatalogAppId === app.id} className="add-library-app" key={app.id} onClick={() => { setSource(null); setSelectedCatalogAppId(app.id); setError(null) }} role="radio" type="button">
                    <span className={`app-icon tone-${app.iconTone ?? 'blue'}`}>{app.icon ? <img alt="" src={app.icon} /> : <Package size={18} />}</span>
                    <span><strong>{app.name}</strong><small>{app.purpose}</small><em>{app.versionLabel}{app.artifactSources?.[0]?.label ? ` · ${app.artifactSources[0].label}` : ''}</em></span>
                    {!source && effectiveSelectedCatalogAppId === app.id && <CheckCircle2 aria-hidden="true" size={19} />}
                  </button>
                ))}
              </div>
            </section>
          )}

          <section className="add-new-source" aria-labelledby="add-new-source-heading">
            <div className="add-section-heading"><div><strong id="add-new-source-heading">Add a new app</strong><small>Sideport validates every IPA before saving it.</small></div></div>
            <div aria-label="New app source" className="add-source-list" role="group">
              {APP_SOURCES.map((item) => {
                const Icon = item.icon
                const unavailable = item.id === 'github' && !demoMode && (githubCapability?.supported !== true || githubCapability.allowedNow !== true)
                return (
                  <button aria-pressed={source === item.id} className="add-source-choice" disabled={unavailable} key={item.id} onClick={() => chooseSource(item.id)} type="button">
                    <span className="add-menu-icon"><Icon size={18} /></span>
                    <span><strong>{item.label}</strong><small>{item.detail}</small></span>
                    {source === item.id && <CheckCircle2 aria-hidden="true" size={18} />}
                  </button>
                )
              })}
            </div>
            {rootLoadError && <p className="data-boundary-note">Configured storage is unavailable: {rootLoadError}</p>}
            {(githubLoadError || githubCapability?.blockedReason) && <p className="data-boundary-note">GitHub releases are unavailable: {githubCapability?.blockedReason ?? githubLoadError}</p>}
          </section>

          {source === 'upload' && (
            <label className="add-file-picker">
              <FileUp size={19} />
              <span><strong>{file?.name ?? 'Choose an IPA file'}</strong><small>{file ? 'Ready to inspect and import.' : 'The file stays in Sideport’s managed app library after import.'}</small></span>
              <input accept=".ipa,application/octet-stream" aria-label="IPA file" onChange={(event) => setFile(event.currentTarget.files?.[0] ?? null)} type="file" />
            </label>
          )}

          {source === 'server' && (
            <div className="add-root-panel">
              <label className="form-field"><span>Configured location</span><select aria-label="Configured Sideport location" onChange={(event) => setRootId(event.currentTarget.value)} value={rootId}><option value="">Choose a location</option>{availableRoots.map((root) => <option disabled={!root.available} key={root.id} value={root.id}>{root.label}{root.available ? '' : ' · unavailable'}</option>)}</select></label>
              <label className="form-field"><span>IPA file in this location</span><input aria-label="IPA file in configured location" onChange={(event) => setRelativePath(event.currentTarget.value)} placeholder="Apps/MyApp.ipa" value={relativePath} /></label>
              <p className="data-boundary-note">Only files inside locations configured by the Sideport owner can be imported. Host paths are never shown here.</p>
            </div>
          )}

          {source === 'github' && (
            <div className="github-source-panel">
              {availableGitHubSources.length > 0 && <div className="github-saved-sources" role="group" aria-label="Connected GitHub repositories">{availableGitHubSources.map((item) => <button aria-pressed={githubSourceId === item.id} key={item.id} onClick={() => void loadReleases(item.id)} type="button"><GitBranch size={17} /><span><strong>{item.repository}</strong><small>{item.visibility === 'private' ? 'Private selected repository' : 'Public repository'}</small></span><ChevronRight size={16} /></button>)}</div>}
              <details className="github-add-repository" open={availableGitHubSources.length === 0}>
                <summary>Add a repository</summary>
                <div className="github-access-options" role="group" aria-label="GitHub repository access">
                  <button aria-pressed={githubAccess === 'public'} onClick={() => setGithubAccess('public')} type="button">Public repository</button>
                  <button aria-pressed={githubAccess === 'private'} onClick={() => setGithubAccess('private')} type="button">Private selected repository</button>
                </div>
                <label className="form-field"><span>Repository</span><input aria-describedby={repositoryTouched && !repositoryValid ? 'github-repository-error' : undefined} aria-invalid={repositoryTouched && !repositoryValid} aria-label="GitHub repository" onBlur={() => setRepositoryTouched(true)} onChange={(event) => { setRepository(event.currentTarget.value); setRepositoryTouched(true) }} placeholder="owner/repository" value={repository} /></label>
                {repositoryTouched && !repositoryValid && <p className="field-error" id="github-repository-error">Enter one repository as owner/repository, without a URL.</p>}
                {githubAccess === 'private' && <div className="github-permissions"><LockKeyhole size={19} /><div><strong>Only the repository you select</strong><span><b>Metadata:</b> read · <b>Contents:</b> read · <b>Write access:</b> none</span><small>Sideport cannot push code, change settings, or read other private repositories.</small></div></div>}
                <button className="ghost-action github-connect-button" disabled={busy || !repositoryValid || (!demoMode && (!services || !canManageGitHub || githubCapability?.allowedNow !== true))} onClick={() => void connectRepository()} type="button">{busy ? <Loader2 className="stage-spin" size={17} /> : <ShieldCheck size={17} />}{githubAccess === 'private' ? 'Continue with GitHub' : 'Connect public repository'}</button>
              </details>
              {releasePage && <div className="github-release-list" role="radiogroup" aria-label={`IPA releases from ${releasePage.repository}`}>{releasePage.releases.flatMap((release) => release.assets.filter((asset) => asset.importable).map((asset) => <button aria-checked={selectedReleaseId === release.releaseId && selectedAssetId === asset.assetId} key={`${release.releaseId}:${asset.assetId}`} onClick={() => { setSelectedReleaseId(release.releaseId); setSelectedAssetId(asset.assetId) }} role="radio" type="button"><span className="add-menu-icon"><Package size={17} /></span><span><strong>{asset.name.replace(/\.ipa$/i, '')}</strong><small>{release.name || release.tag} · {formatBytes(asset.sizeBytes)}</small><em>{release.tag}{release.prerelease ? ' · prerelease' : ''}</em></span>{selectedReleaseId === release.releaseId && selectedAssetId === asset.assetId && <CheckCircle2 size={18} />}</button>))}</div>}
              <details className="add-flow-advanced"><summary>Permission details</summary><p>Private repositories use selected-repository access with Metadata read and Contents read only. Sideport accepts release IPA assets from the source you chose, not arbitrary download URLs.</p></details>
            </div>
          )}

          {success && <div className="add-flow-success" role="status"><CheckCircle2 size={21} /><div><strong>{success}</strong><span>You can install it on any accepted iPhone.</span></div></div>}
          {error && <p className="mutation-message error" role="alert">{error}</p>}
          {!demoMode && (!services || !canImport) && <p className="mutation-message">This protected Sideport session does not have permission to import apps.</p>}

          <div className="dialog-actions add-flow-actions">
            <Dialog.Close asChild><button className="ghost-action add-flow-button" type="button">Close</button></Dialog.Close>
            {!source ? (
              <button className="primary-action add-flow-button" disabled={!effectiveSelectedCatalogAppId} onClick={chooseExisting} type="button">Choose app <ChevronRight size={17} /></button>
            ) : (
              <button className="primary-action add-flow-button" disabled={importDisabled} onClick={() => void importApp()} type="button">{busy ? <Loader2 className="stage-spin" size={17} /> : <Package size={17} />}Import app</button>
            )}
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
