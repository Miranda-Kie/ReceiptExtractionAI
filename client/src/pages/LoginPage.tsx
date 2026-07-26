import { FormEvent, useEffect, useRef, useState } from 'react'
import { Navigate, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { useAuth, type LoginChallenge } from '../auth'
import { readJson } from '../http'
import { ThemeToggle, applyStoredTheme } from '../theme'

type LoginLocationState = {
  emptyCredentials?: boolean
}

function isChallenge(value: string | LoginChallenge | null): value is LoginChallenge {
  return typeof value === 'object' && value !== null && value.requiresVerification === true
}

function isAutofilled(el: HTMLInputElement | null): boolean {
  if (!el) return false
  try {
    return el.matches(':-webkit-autofill') || el.matches(':autofill')
  } catch {
    try {
      return el.matches(':-webkit-autofill')
    } catch {
      return false
    }
  }
}

export default function LoginPage() {
  const { user, loading, login, loginDemo, logout } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [searchParams] = useSearchParams()
  const usernameRef = useRef<HTMLInputElement>(null)
  const passwordRef = useRef<HTMLInputElement>(null)

  const fromEmailSignIn = searchParams.get('signin') === '1'
  const fromResetNav = Boolean((location.state as LoginLocationState | null)?.emptyCredentials)
  const preferEmptyRef = useRef(fromEmailSignIn || fromResetNav)
  const [preparingSignIn, setPreparingSignIn] = useState(fromEmailSignIn || fromResetNav)

  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  /** True only after the user typed or pasted — not Chrome password-manager fill. */
  const [passwordEnteredByUser, setPasswordEnteredByUser] = useState(false)
  const [passwordAutofilled, setPasswordAutofilled] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  /** Remount inputs after reset so the browser does not keep prior values. */
  const [fieldEpoch, setFieldEpoch] = useState(0)

  const [mode, setMode] = useState<'signin' | 'forgot'>('signin')
  const [forgotEmail, setForgotEmail] = useState('')
  const [forgotDone, setForgotDone] = useState<string | null>(null)
  const [forgotMaskedEmail, setForgotMaskedEmail] = useState<string | null>(null)
  const [forgotDevLink, setForgotDevLink] = useState<string | null>(null)

  const canShowPassword = passwordEnteredByUser && !passwordAutofilled && password.length > 0

  function openForgot() {
    setMode('forgot')
    setError(null)
    setForgotEmail('')
    setForgotDone(null)
    setForgotMaskedEmail(null)
    setForgotDevLink(null)
  }

  function backToSignIn() {
    setMode('signin')
    setError(null)
    setForgotEmail('')
    setForgotDone(null)
    setForgotMaskedEmail(null)
    setForgotDevLink(null)
  }

  async function onForgotRequest(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    setForgotDone(null)
    setForgotDevLink(null)
    try {
      const res = await fetch('/api/password-reset/request', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: forgotEmail.trim() }),
      })
      const data = await readJson<{
        error?: string
        maskedEmail?: string
        resetLink?: string
        message?: string
      }>(res)
      if (!res.ok) {
        setError(data.error || 'Could not start password reset.')
        return
      }
      setForgotMaskedEmail(typeof data.maskedEmail === 'string' ? data.maskedEmail : null)
      setForgotDevLink(typeof data.resetLink === 'string' ? data.resetLink : null)
      setForgotDone(
        typeof data.message === 'string'
          ? data.message
          : 'If that email is registered, a password reset link was sent.',
      )
    } finally {
      setBusy(false)
    }
  }

  function clearCredentials() {
    setUsername('')
    setPassword('')
    setPasswordEnteredByUser(false)
    setPasswordAutofilled(false)
    setShowPassword(false)
    if (usernameRef.current) usernameRef.current.value = ''
    if (passwordRef.current) passwordRef.current.value = ''
  }

  useEffect(() => {
    applyStoredTheme()
    clearCredentials()

    let cancelled = false
    async function prepareFreshSignIn() {
      if (!preferEmptyRef.current) {
        setPreparingSignIn(false)
        return
      }

      setPreparingSignIn(true)
      try {
        // Email "Sign in" / post-reset nav: drop any existing session so we stay on login.
        await logout()
      } finally {
        if (cancelled) return
        setFieldEpoch((n) => n + 1)
        clearCredentials()
        // Keep ?signin=1 (and emptyCredentials). Stripping the query remounts the page
        // without prefer-empty, and the browser password manager fills the form again.
        navigate('/login?signin=1', {
          replace: true,
          state: { emptyCredentials: true },
        })
        setPreparingSignIn(false)
      }
    }

    void prepareFreshSignIn()

    const delays = preferEmptyRef.current ? [0, 100, 300, 600, 1200] : [0, 250]
    const timers = delays.map((ms) => window.setTimeout(clearCredentials, ms))

    return () => {
      cancelled = true
      timers.forEach((id) => window.clearTimeout(id))
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- run once on mount for this navigation
  }, [])

  // Chrome may fill after paint; keep Show disabled while the field is autofilled.
  // After password reset, strip autofill so the form stays empty until the user types.
  useEffect(() => {
    const userEl = usernameRef.current
    const passEl = passwordRef.current
    if (!passEl) return

    const syncAutofill = () => {
      if (preferEmptyRef.current) {
        if (userEl?.value) userEl.value = ''
        if (passEl.value) passEl.value = ''
        if (username) setUsername('')
        if (password) setPassword('')
        setPasswordAutofilled(false)
        setPasswordEnteredByUser(false)
        setShowPassword(false)
        return
      }

      const filled = isAutofilled(passEl)
      setPasswordAutofilled(filled)
      if (filled) {
        setPasswordEnteredByUser(false)
        setShowPassword(false)
        if (passEl.value && password !== passEl.value) {
          setPassword(passEl.value)
        }
      }
    }

    syncAutofill()
    const id = window.setInterval(syncAutofill, 300)
    passEl.addEventListener('animationstart', syncAutofill)
    return () => {
      window.clearInterval(id)
      passEl.removeEventListener('animationstart', syncAutofill)
    }
  }, [password, username, fieldEpoch])

  useEffect(() => {
    if (!canShowPassword && showPassword) {
      setShowPassword(false)
    }
  }, [canShowPassword, showPassword])

  // Do not bounce into the app while preparing a fresh sign-in from email/reset.
  if (!loading && !preparingSignIn && user?.authenticated) {
    return <Navigate to="/" replace />
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    const result = await login(username, password)
    setBusy(false)
    if (isChallenge(result)) {
      clearCredentials()
      navigate('/verify', { replace: true, state: { challenge: result } })
      return
    }
    if (result) setError(result)
  }

  async function onDemo() {
    setBusy(true)
    setError(null)
    const err = await loginDemo()
    setBusy(false)
    if (err) setError(err)
  }

  if (preparingSignIn || loading) {
    return (
      <main className="auth-shell">
        <section className="auth-card" aria-labelledby="login-title">
          <span className="brand-mark">HST Receipts</span>
          <h1 id="login-title">Sign in</h1>
          <p className="muted">Opening sign in…</p>
        </section>
      </main>
    )
  }

  if (mode === 'forgot') {
    return (
      <main className="auth-shell">
        <section className="auth-card" aria-labelledby="forgot-title">
          <span className="brand-mark">HST Receipts</span>
          <h1 id="forgot-title">Reset password</h1>
          {!forgotDone && (
            <p className="auth-lead">
              Enter the email on your account. We will send a password reset link.
            </p>
          )}

          {forgotDone ? (
            <>
              <p className="auth-ok" role="status">
                {forgotDone}
              </p>
              {forgotMaskedEmail && (
                <p className="auth-masked-email" aria-label={`Email ${forgotMaskedEmail}`}>
                  Sent to <span className="auth-inline-mask">{forgotMaskedEmail}</span>
                </p>
              )}
              {forgotDevLink && (
                <p className="auth-dev-code" role="note">
                  Development reset link:{' '}
                  <a href={forgotDevLink}>{forgotDevLink}</a>
                </p>
              )}
              <button type="button" className="btn-stamp" onClick={backToSignIn}>
                Back to sign in
              </button>
            </>
          ) : (
            <form onSubmit={onForgotRequest} className="auth-form" autoComplete="off">
              <label>
                Registered email
                <input
                  type="email"
                  value={forgotEmail}
                  onChange={(e) => setForgotEmail(e.target.value)}
                  autoComplete="email"
                  required
                  placeholder="name@example.com"
                />
              </label>
              {error && (
                <p className="error" role="alert">
                  {error}
                </p>
              )}
              <button type="submit" className="btn-stamp" disabled={busy}>
                {busy ? 'Sending…' : 'Send reset link'}
              </button>
              <button type="button" className="btn-ghost demo-btn" disabled={busy} onClick={backToSignIn}>
                Back to sign in
              </button>
            </form>
          )}

          <div className="auth-theme">
            <ThemeToggle />
          </div>
        </section>
      </main>
    )
  }

  return (
    <main className="auth-shell">
      <section className="auth-card" aria-labelledby="login-title">
        <span className="brand-mark">HST Receipts</span>
        <h1 id="login-title">Sign in</h1>
        <p className="auth-lead">Extract and export receipt data with OCR.</p>

        <form
          key={fieldEpoch}
          onSubmit={onSubmit}
          className="auth-form"
          autoComplete="off"
        >
          <label>
            Username
            <input
              ref={usernameRef}
              name={`hst-login-username-${fieldEpoch}`}
              value={username}
              onChange={(e) => {
                preferEmptyRef.current = false
                setUsername(e.target.value)
              }}
              autoComplete="off"
              autoCorrect="off"
              autoCapitalize="off"
              spellCheck={false}
              required
              readOnly
              onFocus={(e) => e.currentTarget.removeAttribute('readOnly')}
            />
          </label>
          <label>
            Password
            <div className="password-row">
              <input
                ref={passwordRef}
                name={`hst-login-password-${fieldEpoch}`}
                type={canShowPassword && showPassword ? 'text' : 'password'}
                value={password}
                onChange={(e) => {
                  preferEmptyRef.current = false
                  setPassword(e.target.value)
                  if (!e.target.value) {
                    setPasswordEnteredByUser(false)
                    setPasswordAutofilled(false)
                    setShowPassword(false)
                  }
                }}
                onKeyDown={() => {
                  preferEmptyRef.current = false
                  setPasswordEnteredByUser(true)
                  setPasswordAutofilled(false)
                }}
                onPaste={() => {
                  preferEmptyRef.current = false
                  setPasswordEnteredByUser(true)
                  setPasswordAutofilled(false)
                }}
                autoComplete="new-password"
                required
                readOnly
                onFocus={(e) => e.currentTarget.removeAttribute('readOnly')}
              />
              <button
                type="button"
                className="ghost"
                disabled={!canShowPassword}
                title={
                  canShowPassword
                    ? showPassword
                      ? 'Hide password'
                      : 'Show password'
                    : 'Show is unavailable for passwords filled by the browser password manager'
                }
                onClick={() => setShowPassword((v) => !v)}
                aria-pressed={showPassword}
              >
                {showPassword && canShowPassword ? 'Hide' : 'Show'}
              </button>
            </div>
          </label>
          <p className="forgot-password-row">
            Forgot password?{' '}
            <button type="button" className="linkish" disabled={busy} onClick={openForgot}>
              Reset now
            </button>
          </p>
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
          <button type="submit" className="btn-stamp" disabled={busy}>
            {busy ? 'Signing in…' : 'Sign in'}
          </button>
        </form>

        <div className="auth-divider" role="separator">
          <span>or</span>
        </div>

        <button type="button" className="btn-ghost demo-btn" disabled={busy} onClick={onDemo}>
          {busy ? 'Starting…' : 'Continue as demo'}
        </button>
        <p className="demo-note">
          Demo skips sign-in. You can extract and download Excel, but cannot save to the database.
        </p>

        <div className="auth-theme">
          <ThemeToggle />
        </div>
      </section>
    </main>
  )
}
