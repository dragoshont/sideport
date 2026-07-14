import { useEffect, useState } from 'react'
import { CheckCircle2, Info, KeyRound, Loader2, ShieldCheck } from 'lucide-react'
import { credentialJson, passkeyError, requestOptionsFromJson } from './passkeys'
import './canonical/CanonicalSideport.css'

type LoginPhase = 'checking' | 'ready' | 'working' | 'done' | 'error'

interface AuthenticationOptions {
  mode?: string
  nativePasskeyEnabled?: boolean
}

function returnPath(): string {
  const candidate = new URLSearchParams(window.location.search).get('returnUrl') ?? '/'
  return candidate.startsWith('/') && !candidate.startsWith('//') ? candidate : '/'
}

async function jsonRequest(path: string, init?: RequestInit): Promise<{ response: Response; body: Record<string, unknown> | null }> {
  const response = await fetch(path, { credentials: 'same-origin', cache: 'no-store', ...init })
  const text = await response.text()
  let body: Record<string, unknown> | null
  try { body = text ? JSON.parse(text) as Record<string, unknown> : null } catch { body = null }
  return { response, body }
}

function safeMessage(body: Record<string, unknown> | null, fallback: string): string {
  return typeof body?.message === 'string' ? body.message : fallback
}

export function PasskeyLogin() {
  const [phase, setPhase] = useState<LoginPhase>('checking')
  const [message, setMessage] = useState('Checking sign-in…')

  useEffect(() => {
    void checkMode()
    async function checkMode() {
      const result = await jsonRequest('/api/authentication/options')
      const options = (result.body ?? {}) as AuthenticationOptions
      if (!result.response.ok || options.mode !== 'passkey' || options.nativePasskeyEnabled !== true) {
        setMessage('Passkey sign-in is not available on this Sideport deployment.')
        setPhase('error')
        return
      }
      setPhase('ready')
    }
  }, [])

  async function signIn() {
    setPhase('working')
    try {
      const options = await jsonRequest('/api/authentication/native-passkey/options', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: '{}',
      })
      if (!options.response.ok || typeof options.body?.requestOptions !== 'string') {
        setMessage(safeMessage(options.body, 'Sideport could not start passkey sign-in.'))
        setPhase('error')
        return
      }
      const credential = await navigator.credentials.get(requestOptionsFromJson(options.body.requestOptions))
      if (!credential) throw new Error('Passkey sign-in did not finish.')
      const completed = await jsonRequest('/api/authentication/native-passkey/complete', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ credentialJson: credentialJson(credential) }),
      })
      if (!completed.response.ok) {
        setMessage(safeMessage(completed.body, 'Sideport could not verify this passkey.'))
        setPhase('error')
        return
      }
      setPhase('done')
      window.setTimeout(() => window.location.assign(returnPath()), 350)
    } catch (error) {
      setMessage(passkeyError(error, 'sign-in'))
      setPhase('ready')
    }
  }

  return <div className="spc-invitation" data-testid="runtime-passkey-login">
    <header><strong>Sideport</strong><span className="spc-eyebrow">Secure sign-in</span></header>
    <main>
      <div className="spc-invite-illustration"><ShieldCheck aria-hidden="true" size={44} /></div>
      <span className="spc-eyebrow">Welcome back</span>
      <h1>Sign in to Sideport</h1>
      <p className="spc-lead">Use the passkey saved on this device or in your password manager. There is no Sideport password to remember.</p>
      {phase === 'checking' || phase === 'working' ? <div role="status"><Loader2 aria-hidden="true" className="spin" size={22} /> {phase === 'working' ? 'Waiting for this device…' : message}</div> : null}
      {phase === 'ready' ? <button className="spc-button primary large" onClick={() => void signIn()} type="button"><KeyRound aria-hidden="true" size={18} /> Sign in with a passkey</button> : null}
      {phase === 'done' ? <div className="spc-invite-result" role="status"><CheckCircle2 aria-hidden="true" size={20} /><div><strong>Signed in</strong><span>Opening Sideport…</span></div></div> : null}
      {phase === 'error' ? <div className="spc-inline-note warning" role="alert"><Info aria-hidden="true" size={18} /><div><strong>Sign-in is unavailable.</strong><span>{message}</span></div></div> : null}
      <p className="spc-fine-print">Face ID, Touch ID, Windows Hello, Android screen lock, and synced password managers can all hold a passkey.</p>
    </main>
  </div>
}
