import { useEffect, useRef, useState } from 'react'
import { CheckCircle2, CircleUserRound, Info, Loader2, ShieldCheck, Users } from 'lucide-react'
import './canonical/CanonicalSideport.css'

type FlowKind = 'owner-claim' | 'invitation'
type Phase = 'exchanging' | 'sign-in' | 'preview' | 'accepting' | 'done' | 'error'

interface Preview {
  account?: { displayName?: string; email?: string }
  claim?: { kind?: string }
  invitation?: { role?: string; permissions?: string[] }
}

function apiPath(kind: FlowKind, action: 'handoff' | 'accept' | 'enrollment'): string {
  if (kind === 'owner-claim') return action === 'accept' ? '/api/workspace/owner-claims/accept' : '/api/workspace/owner-claims/handoff'
  if (action === 'accept') return '/api/workspace/invitations/accept'
  if (action === 'enrollment') return '/api/workspace/invitations/enrollment'
  return '/api/workspace/invitations/handoff'
}

function newKey(kind: FlowKind): string {
  const suffix = typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`
  return `ui-${kind}-accept-${suffix}`
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

export function WorkspaceHandoff({ kind }: { kind: FlowKind }) {
  const [phase, setPhase] = useState<Phase>('exchanging')
  const [preview, setPreview] = useState<Preview | null>(null)
  const [csrf, setCsrf] = useState('')
  const [message, setMessage] = useState('Checking this private link…')
  const started = useRef(false)
  const isOwner = kind === 'owner-claim'

  useEffect(() => {
    if (started.current) return
    started.current = true
    void begin()

    async function begin() {
      const fragment = window.location.hash.slice(1)
      window.history.replaceState(window.history.state, '', window.location.pathname)
      if (fragment) {
        const tokenField = isOwner ? 'claimToken' : 'invitationToken'
        const exchanged = await jsonRequest(apiPath(kind, 'handoff'), {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ [tokenField]: fragment }),
        })
        if (!exchanged.response.ok) {
          setMessage(safeMessage(exchanged.body, 'This private link is unavailable.'))
          setPhase('error')
          return
        }
      }

      const me = await jsonRequest('/api/me')
      const token = me.response.headers.get('X-Sideport-CSRF') ?? ''
      setCsrf(token)
      if (me.body?.authenticated !== true || me.body?.via !== 'oidc') {
        setPhase('sign-in')
        setMessage('Sign in through Authentik to continue.')
        return
      }

      const handoff = await jsonRequest(apiPath(kind, 'handoff'))
      if (!handoff.response.ok) {
        setMessage(safeMessage(handoff.body, 'This private handoff is unavailable.'))
        setPhase('error')
        return
      }
      setPreview((handoff.body ?? {}) as Preview)
      setPhase('preview')
    }
  }, [isOwner, kind])

  async function accept() {
    setPhase('accepting')
    const accepted = await jsonRequest(apiPath(kind, 'accept'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Sideport-CSRF': csrf },
      body: JSON.stringify({ idempotencyKey: newKey(kind) }),
    })
    if (!accepted.response.ok) {
      setMessage(safeMessage(accepted.body, 'Sideport could not finish this handoff.'))
      setPhase('error')
      return
    }
    setPhase('done')
    window.setTimeout(() => window.location.assign('/'), 500)
  }

  const account = preview?.account
  const recovering = preview?.claim?.kind === 'recovery'
  const title = isOwner
    ? recovering ? 'Recover Sideport owner access' : 'Finish setting up Sideport'
    : 'Join Sideport'

  return <div className="spc-invitation" data-testid={`runtime-${kind}`}>
    <header><strong>Sideport</strong><span className="spc-eyebrow">Private link</span></header>
    <main>
      <div className="spc-invite-illustration">{isOwner ? <ShieldCheck aria-hidden="true" size={44} /> : <Users aria-hidden="true" size={44} />}</div>
      <span className="spc-eyebrow">{isOwner ? 'Owner access' : 'Trusted access'}</span>
      <h1>{title}</h1>
      {phase === 'exchanging' || phase === 'accepting' ? <div role="status"><Loader2 aria-hidden="true" className="spin" size={22} /> {phase === 'accepting' ? 'Saving access…' : message}</div> : null}
      {phase === 'sign-in' ? <>
        <p className="spc-lead">{message} The private token has already been removed from the address bar and replaced with an opaque handoff cookie.</p>
        <button className="spc-button primary large" onClick={() => window.location.assign(`/login?returnUrl=${encodeURIComponent(window.location.pathname)}`)} type="button">Continue to sign in</button>
      </> : null}
      {phase === 'preview' ? <>
        <p className="spc-lead">Confirm the signed-in account before Sideport changes access.</p>
        <div className="spc-passkey-card"><CircleUserRound aria-hidden="true" size={24} /><div><strong>{account?.displayName || 'Signed-in account'}</strong><span>{account?.email || 'Authenticated through Authentik'}</span></div></div>
        <div className="spc-passkey-card"><ShieldCheck aria-hidden="true" size={24} /><div><strong>{isOwner ? 'Owner access' : 'Member access'}</strong><span>{isOwner ? 'Manage people, Apple signing, apps, iPhones, and settings.' : (preview?.invitation?.permissions ?? ['Choose approved apps', 'Use your own iPhone', 'Receive home Wi-Fi refreshes']).join(' · ')}</span></div></div>
        <button className="spc-button primary large" onClick={() => void accept()} type="button">{isOwner ? recovering ? 'Recover owner access' : 'Finish owner setup' : 'Join Sideport'}</button>
      </> : null}
      {phase === 'done' ? <div className="spc-invite-result" role="status"><CheckCircle2 aria-hidden="true" size={20} /><div><strong>Access saved</strong><span>Opening Sideport…</span></div></div> : null}
      {phase === 'error' ? <aside className="spc-inline-note warning" role="alert"><Info aria-hidden="true" size={18} /><div><strong>This link cannot continue.</strong><span>{message}</span></div></aside> : null}
      <p className="spc-fine-print">No API key, Apple password, or passkey is entered on this page.</p>
    </main>
  </div>
}
