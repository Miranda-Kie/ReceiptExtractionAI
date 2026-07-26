import { FormEvent, useEffect, useState } from 'react'
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { useAuth } from '../auth'
import { readJson } from '../http'
import { ThemeToggle, applyStoredTheme } from '../theme'

export default function ResetPasswordPage() {
  const [params] = useSearchParams()
  const location = useLocation()
  const navigate = useNavigate()
  const { logout } = useAuth()
  const token = params.get('token') || ''
  const inviteFromUrl =
    location.pathname.endsWith('/set-password') || params.get('invite') === '1'
  const [isInvite, setIsInvite] = useState(inviteFromUrl)
  const [username, setUsername] = useState<string | null>(null)
  const [maskedEmail, setMaskedEmail] = useState<string | null>(null)
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [linkError, setLinkError] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const [doneMessage, setDoneMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [goingToLogin, setGoingToLogin] = useState(false)
  const [loadingLink, setLoadingLink] = useState(Boolean(token))

  const title = isInvite ? 'Set password' : 'Reset password'
  const linkNoun = isInvite ? 'set-password' : 'reset'

  useEffect(() => {
    applyStoredTheme()
    setIsInvite(inviteFromUrl)
    if (!token) {
      setLinkError(
        inviteFromUrl
          ? 'Missing set-password token. Open the link from your welcome email.'
          : 'Missing reset token. Open the link from your email.',
      )
      setLoadingLink(false)
      return
    }

    setLoadingLink(true)
    setLinkError(null)
    fetch(`/api/password-reset/${encodeURIComponent(token)}`, { credentials: 'include' })
      .then(async (res) => {
        const data = await readJson<{
          error?: string
          username?: string
          maskedEmail?: string
          isSetPasswordInvite?: boolean
        }>(res)
        if (!res.ok) {
          setLinkError(
            data.error ||
              (inviteFromUrl
                ? 'Set-password link is invalid or expired. Ask an admin to create the account again or resend an invite.'
                : 'Reset link is invalid or expired. Request a new reset email.'),
          )
          setUsername(null)
          setMaskedEmail(null)
          return
        }
        const invite = Boolean(data.isSetPasswordInvite) || inviteFromUrl
        setIsInvite(invite)
        setUsername(typeof data.username === 'string' ? data.username : null)
        setMaskedEmail(typeof data.maskedEmail === 'string' ? data.maskedEmail : null)
      })
      .catch(() => {
        setLinkError(`Could not validate ${linkNoun} link.`)
        setUsername(null)
        setMaskedEmail(null)
      })
      .finally(() => setLoadingLink(false))
  }, [token, inviteFromUrl, linkNoun])

  async function goToSignIn() {
    setGoingToLogin(true)
    try {
      await logout()
    } finally {
      navigate('/login?signin=1', { replace: true, state: { emptyCredentials: true } })
    }
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setFormError(null)
    try {
      const res = await fetch('/api/password-reset/complete', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token, password }),
      })
      const data = await readJson<{
        error?: string
        message?: string
        maskedEmail?: string
        isSetPasswordInvite?: boolean
      }>(res)
      if (!res.ok) {
        setFormError(data.error || 'Could not save password.')
        return
      }
      if (typeof data.maskedEmail === 'string') {
        setMaskedEmail(data.maskedEmail)
      }
      if (typeof data.isSetPasswordInvite === 'boolean') {
        setIsInvite(data.isSetPasswordInvite)
      }
      setDoneMessage(
        typeof data.message === 'string'
          ? data.message
          : isInvite
            ? 'Account ready. Check your email for a registration confirmation.'
            : `A confirmation has been sent to ${data.maskedEmail || maskedEmail || 'your email'}.`,
      )
    } catch {
      setFormError('Could not save password. Check your connection and try again.')
    } finally {
      setBusy(false)
    }
  }

  const linkValid = Boolean(username) && !linkError
  const done = Boolean(doneMessage)

  return (
    <main className="auth-shell">
      <section className="auth-card" aria-labelledby="reset-title">
        <span className="brand-mark">HST Receipts</span>
        <h1 id="reset-title">{title}</h1>

        {loadingLink && (
          <p className="muted">
            {isInvite ? 'Checking set-password link…' : 'Checking reset link…'}
          </p>
        )}

        {!loadingLink && linkError && (
          <>
            <p className="error" role="alert">
              {linkError}
            </p>
            <p className="demo-note">
              {isInvite
                ? 'Ask an admin to add the user again so a fresh set-password email is sent.'
                : 'Ask an admin to send a new reset email from the Users page, then open the fresh link.'}
            </p>
            <p className="demo-note">
              <button
                type="button"
                className="linkish"
                disabled={goingToLogin}
                onClick={() => void goToSignIn()}
              >
                {goingToLogin ? 'Opening sign in…' : 'Back to sign in'}
              </button>
            </p>
          </>
        )}

        {!loadingLink && linkValid && username && !done && (
          <p className="auth-lead">
            {isInvite ? (
              <>
                Choose a password for <strong>{username}</strong>
              </>
            ) : (
              <>
                Account <strong>{username}</strong>
              </>
            )}
          </p>
        )}

        {!loadingLink && linkValid && done && (
          <>
            <p className="auth-ok" role="status">
              {doneMessage}
            </p>
            <p className="demo-note">
              <button
                type="button"
                className="linkish"
                disabled={goingToLogin}
                onClick={() => void goToSignIn()}
              >
                {goingToLogin ? 'Opening sign in…' : 'Back to sign in'}
              </button>
            </p>
          </>
        )}

        {!loadingLink && linkValid && !done && (
          <form onSubmit={onSubmit} className="auth-form" method="post">
            <label className="sr-only" htmlFor="reset-username">
              Username
            </label>
            <input
              id="reset-username"
              name="username"
              type="text"
              value={username || ''}
              readOnly
              autoComplete="username"
              className="sr-only"
              tabIndex={-1}
            />
            <label>
              {isInvite ? 'Password' : 'New password'}
              <div className="password-row">
                <input
                  name="password"
                  type={showPassword ? 'text' : 'password'}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  minLength={6}
                  required
                  autoComplete="new-password"
                />
                <button
                  type="button"
                  className="ghost"
                  onClick={() => setShowPassword((v) => !v)}
                >
                  {showPassword ? 'Hide' : 'Show'}
                </button>
              </div>
            </label>
            <p className="muted">
              {isInvite
                ? 'Must be at least 6 characters.'
                : 'Must be at least 6 characters and not match any previous password.'}
            </p>
            {formError && (
              <p className="error" role="alert">
                {formError}
              </p>
            )}
            <button type="submit" className="btn-stamp" disabled={busy}>
              {busy ? 'Saving…' : isInvite ? 'Set password' : 'Update password'}
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
