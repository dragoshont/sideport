import { useCallback, useEffect, useRef, useState } from 'react'
import { CheckCircle2, CircleUserRound, Info, KeyRound, Loader2, ShieldCheck, Users } from 'lucide-react'
import { creationOptionsFromJson, credentialJson, passkeyError, requestOptionsFromJson } from './passkeys'
import './canonical/CanonicalSideport.css'

type FlowKind = 'owner-claim' | 'invitation'
type Phase = 'exchanging' | 'sign-in' | 'creating' | 'preview' | 'accepting' | 'done' | 'error'

interface Preview {
  account?: { displayName?: string; email?: string }
  claim?: { kind?: string }
  invitation?: { role?: string; permissions?: string[] }
}

interface AuthenticationOptions {
  mode?: 'passkey' | 'oidc' | 'none'
  providerLabel?: string
  loginLabel?: string
  enrollmentLabel?: string
  preferredMethod?: string
  enrollmentEnabled?: boolean
  nativePasskeyEnabled?: boolean
}

function apiPath(kind: FlowKind, action: 'handoff' | 'session' | 'accept' | 'enrollment' | 'native-options' | 'native-complete'): string {
  const root = kind === 'owner-claim' ? '/api/workspace/owner-claims' : '/api/workspace/invitations'
  if (action === 'handoff') return `${root}/handoff`
  if (action === 'session') return `${root}/handoff/session`
  if (action === 'accept') return `${root}/accept`
  if (action === 'enrollment') return `${root}/enrollment`
  return `${root}/native-passkey/${action === 'native-options' ? 'options' : 'complete'}`
}

function newKey(kind: FlowKind | 'login'): string {
  const suffix = typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`
  return `ui-${kind}-${suffix}`
}

async function jsonRequest(path: string, init?: RequestInit): Promise<{ response: Response; body: Record<string, unknown> | null }> {
  const response = await fetch(path, { credentials: 'same-origin', cache: 'no-store', ...init })
  const text = await response.text()
  let body: Record<string, unknown> | null
  try { body = text ? JSON.parse(text) as Record<string, unknown> : null } catch { body = null }
  return { response, body }
}

function safeMessage(body: Record<string, unknown> | null, fallback: string): string {
  return typeof body?.message === 'string' ? body.message : typeof body?.error === 'string' ? body.error : fallback
}

function postJson(path: string, body: Record<string, unknown>, csrf = ''): Promise<{ response: Response; body: Record<string, unknown> | null }> {
  return jsonRequest(path, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(csrf ? { 'X-Sideport-CSRF': csrf } : {}),
    },
    body: JSON.stringify(body),
  })
}

export function WorkspaceHandoff({ kind }: { kind: FlowKind }) {
  const [phase, setPhase] = useState<Phase>('exchanging')
  const [preview, setPreview] = useState<Preview | null>(null)
  const [csrf, setCsrf] = useState('')
  const [message, setMessage] = useState('Checking this private link…')
  const [authentication, setAuthentication] = useState<AuthenticationOptions>({})
  const [enrollmentUrl, setEnrollmentUrl] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState('')
  const started = useRef(false)
  const isOwner = kind === 'owner-claim'
  const nativeMode = authentication.mode === 'passkey' && authentication.nativePasskeyEnabled === true

  const loadPreview = useCallback(async (meResult?: { response: Response; body: Record<string, unknown> | null }) => {
    const me = meResult ?? await jsonRequest('/api/me')
    setCsrf(me.response.headers.get('X-Sideport-CSRF') ?? '')
    const handoff = await jsonRequest(apiPath(kind, 'handoff'))
    if (!handoff.response.ok) {
      setMessage(safeMessage(handoff.body, 'This private handoff is unavailable.'))
      setPhase('error')
      return
    }
    setPreview((handoff.body ?? {}) as Preview)
    setPhase('preview')
  }, [kind])

  useEffect(() => {
    if (started.current) return
    started.current = true
    void begin()

    async function begin() {
      const authenticationResult = await jsonRequest('/api/authentication/options')
      const configuredAuthentication = (authenticationResult.body ?? {}) as AuthenticationOptions
      if (authenticationResult.response.ok) setAuthentication(configuredAuthentication)

      const fragment = window.location.hash.slice(1)
      window.history.replaceState(window.history.state, '', window.location.pathname)
      if (fragment) {
        const tokenField = isOwner ? 'claimToken' : 'invitationToken'
        const exchanged = await postJson(apiPath(kind, 'handoff'), { [tokenField]: fragment })
        if (!exchanged.response.ok) {
          setMessage(safeMessage(exchanged.body, 'This private link is unavailable.'))
          setPhase('error')
          return
        }
      }

      const me = await jsonRequest('/api/me')
      if (me.response.ok && me.body?.authenticated === true && (me.body?.via === 'oidc' || me.body?.via === 'passkey')) {
        await loadPreview(me)
        return
      }

      const handoffSession = await jsonRequest(apiPath(kind, 'session'))
      if (!handoffSession.response.ok) {
        setMessage(isOwner
          ? 'Open the one-time Owner setup link shown in Sideport’s startup logs. If it expired, create a new Owner link from the server.'
          : 'Open the complete private invitation link that was sent to you.')
        setPhase('error')
        return
      }

      if (configuredAuthentication.mode === 'oidc' && configuredAuthentication.enrollmentEnabled === true) {
        const enrollment = await postJson(apiPath(kind, 'enrollment'), { idempotencyKey: newKey(kind) })
        if (enrollment.response.ok && typeof enrollment.body?.enrollmentUrl === 'string')
          setEnrollmentUrl(enrollment.body.enrollmentUrl)
      }
      setPhase('sign-in')
      setMessage(configuredAuthentication.mode === 'passkey'
        ? 'Create a passkey using this device’s built-in security.'
        : `Sign in through ${configuredAuthentication.providerLabel ?? 'your identity provider'} to continue.`)
    }
  }, [isOwner, kind, loadPreview])

  async function createNativePasskey() {
    setPhase('creating')
    try {
      const profile = isOwner ? { displayName: displayName.trim(), email: email.trim() } : {}
      const options = await postJson(apiPath(kind, 'native-options'), profile)
      if (!options.response.ok || typeof options.body?.creationOptions !== 'string') {
        setMessage(safeMessage(options.body, 'Sideport could not start passkey creation.'))
        setPhase('error')
        return
      }
      const credential = await navigator.credentials.create(creationOptionsFromJson(options.body.creationOptions))
      if (!credential) throw new Error('Passkey creation did not finish.')
      const completed = await postJson(apiPath(kind, 'native-complete'), {
        ...profile,
        credentialJson: credentialJson(credential),
        idempotencyKey: newKey(kind),
      })
      if (!completed.response.ok) {
        setMessage(safeMessage(completed.body, 'Sideport could not save this passkey.'))
        setPhase('error')
        return
      }
      finish()
    } catch (error) {
      setMessage(passkeyError(error, 'create'))
      setPhase('sign-in')
    }
  }

  async function signInWithExistingPasskey() {
    setPhase('creating')
    try {
      const options = await postJson('/api/authentication/native-passkey/options', {})
      if (!options.response.ok || typeof options.body?.requestOptions !== 'string') {
        setMessage(safeMessage(options.body, 'Sideport could not start passkey sign-in.'))
        setPhase('error')
        return
      }
      const credential = await navigator.credentials.get(requestOptionsFromJson(options.body.requestOptions))
      if (!credential) throw new Error('Passkey sign-in did not finish.')
      const completed = await postJson('/api/authentication/native-passkey/complete', {
        credentialJson: credentialJson(credential),
        idempotencyKey: newKey('login'),
      })
      if (!completed.response.ok) {
        setMessage(safeMessage(completed.body, 'Sideport could not verify this passkey.'))
        setPhase('error')
        return
      }
      await loadPreview()
    } catch (error) {
      setMessage(passkeyError(error, 'sign-in'))
      setPhase('sign-in')
    }
  }

  async function accept() {
    setPhase('accepting')
    const accepted = await postJson(apiPath(kind, 'accept'), { idempotencyKey: newKey(kind) }, csrf)
    if (!accepted.response.ok) {
      setMessage(safeMessage(accepted.body, 'Sideport could not finish this handoff.'))
      setPhase('error')
      return
    }
    finish()
  }

  function finish() {
    setPhase('done')
    window.setTimeout(() => window.location.assign('/'), 500)
  }

  const account = preview?.account
  const recovering = preview?.claim?.kind === 'recovery'
  const title = isOwner
    ? recovering ? 'Recover Sideport owner access' : 'Finish setting up Sideport'
    : 'Join Sideport'
  const validOwnerProfile = displayName.trim().length > 0 && email.trim().length > 2

  return <div className="spc-invitation" data-testid={`runtime-${kind}`}>
    <header><strong>Sideport</strong><span className="spc-eyebrow">Private link</span></header>
    <main>
      <div className="spc-invite-illustration">{isOwner ? <ShieldCheck aria-hidden="true" size={44} /> : <Users aria-hidden="true" size={44} />}</div>
      <span className="spc-eyebrow">{isOwner ? 'Owner access' : 'Trusted access'}</span>
      <h1>{title}</h1>
      {phase === 'exchanging' || phase === 'creating' || phase === 'accepting' ? <div role="status"><Loader2 aria-hidden="true" className="spin" size={22} /> {phase === 'accepting' ? 'Saving access…' : phase === 'creating' ? 'Waiting for this device…' : message}</div> : null}
      {phase === 'sign-in' && nativeMode ? <>
        <p className="spc-lead">{message} Use Face ID, Touch ID, Windows Hello, or your password manager. Nothing extra to remember.</p>
        {isOwner ? <div className="spc-identity-form">
          <label><span>Name</span><input autoComplete="name" onChange={(event) => setDisplayName(event.currentTarget.value)} placeholder="Your name" value={displayName} /></label>
          <label><span>Email</span><input autoComplete="email" inputMode="email" onChange={(event) => setEmail(event.currentTarget.value)} placeholder="you@example.com" type="email" value={email} /></label>
        </div> : null}
        <button className="spc-button primary large" disabled={isOwner && !validOwnerProfile} onClick={() => void createNativePasskey()} type="button"><KeyRound aria-hidden="true" size={18} /> Create passkey</button>
        <button className="spc-text-button" onClick={() => void signInWithExistingPasskey()} type="button">Use an existing passkey</button>
      </> : null}
      {phase === 'sign-in' && !nativeMode ? <>
        <p className="spc-lead">{message} The private token has already been removed from the address bar.</p>
        {enrollmentUrl ? <button className="spc-button primary large" onClick={() => window.location.assign(enrollmentUrl)} type="button">{authentication.enrollmentLabel || 'Create passkey'}</button> : null}
        <button className={enrollmentUrl ? 'spc-button secondary large' : 'spc-button primary large'} onClick={() => window.location.assign(`/login?returnUrl=${encodeURIComponent(window.location.pathname)}`)} type="button">{authentication.loginLabel || `Continue with ${authentication.providerLabel || 'your account'}`}</button>
      </> : null}
      {phase === 'preview' ? <>
        <p className="spc-lead">Confirm the signed-in account before Sideport changes access.</p>
        <div className="spc-passkey-card"><CircleUserRound aria-hidden="true" size={24} /><div><strong>{account?.displayName || 'Signed-in account'}</strong><span>{account?.email || `Authenticated through ${authentication.providerLabel || 'your identity provider'}`}</span></div></div>
        <div className="spc-passkey-card"><ShieldCheck aria-hidden="true" size={24} /><div><strong>{isOwner ? 'Owner access' : 'Member access'}</strong><span>{isOwner ? 'Manage people, Apple signing, apps, iPhones, and settings.' : (preview?.invitation?.permissions ?? ['Choose approved apps', 'Use your own iPhone', 'Receive home Wi-Fi refreshes']).join(' · ')}</span></div></div>
        <button className="spc-button primary large" onClick={() => void accept()} type="button">{isOwner ? recovering ? 'Recover owner access' : 'Finish owner setup' : 'Join Sideport'}</button>
      </> : null}
      {phase === 'done' ? <div className="spc-invite-result" role="status"><CheckCircle2 aria-hidden="true" size={20} /><div><strong>Access saved</strong><span>Opening Sideport…</span></div></div> : null}
      {phase === 'error' ? <aside className="spc-inline-note warning" role="alert"><Info aria-hidden="true" size={18} /><div><strong>This link cannot continue.</strong><span>{message}</span></div></aside> : null}
      <p className="spc-fine-print">Your passkey stays on your trusted device. Sideport never receives your Apple password.</p>
    </main>
  </div>
}
