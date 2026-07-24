import { FormEvent, useEffect, useState } from 'react'
import { Navigate } from 'react-router-dom'
import { useAuth } from '../auth'
import { ThemeToggle, applyStoredTheme } from '../theme'

export default function LoginPage() {
  const { user, loading, login, loginDemo } = useAuth()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    applyStoredTheme()
  }, [])

  if (!loading && user?.authenticated) {
    return <Navigate to="/" replace />
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    const err = await login(username, password)
    setBusy(false)
    if (err) setError(err)
  }

  async function onDemo() {
    setBusy(true)
    setError(null)
    const err = await loginDemo()
    setBusy(false)
    if (err) setError(err)
  }

  return (
    <main className="auth-shell">
      <section className="auth-card" aria-labelledby="login-title">
        <span className="brand-mark">HST Receipts</span>
        <h1 id="login-title">Sign in</h1>
        <p className="auth-lead">Extract and export receipt data with OCR and optional AI learning.</p>
        <form onSubmit={onSubmit} className="auth-form">
          <label>
            Username
            <input
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              autoComplete="username"
              required
            />
          </label>
          <label>
            Password
            <div className="password-row">
              <input
                type={showPassword ? 'text' : 'password'}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
                required
              />
              <button
                type="button"
                className="ghost"
                onClick={() => setShowPassword((v) => !v)}
                aria-pressed={showPassword}
              >
                {showPassword ? 'Hide' : 'Show'}
              </button>
            </div>
          </label>
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
