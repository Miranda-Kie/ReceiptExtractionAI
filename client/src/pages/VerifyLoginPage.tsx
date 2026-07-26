import { FormEvent, useEffect, useMemo, useState } from 'react'

import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'

import { useAuth, type LoginChallenge } from '../auth'

import { ThemeToggle, applyStoredTheme } from '../theme'



type LocationState = {

  challenge?: LoginChallenge

}



function secondsUntil(iso?: string): number {

  if (!iso) return 0

  const ms = Date.parse(iso) - Date.now()

  return Number.isFinite(ms) ? Math.max(0, Math.ceil(ms / 1000)) : 0

}



function formatCountdown(totalSeconds: number): string {

  const minutes = Math.floor(totalSeconds / 60)

  const seconds = totalSeconds % 60

  if (minutes <= 0) {

    return `${seconds}s`

  }

  return `${minutes}:${seconds.toString().padStart(2, '0')}`

}



export default function VerifyLoginPage() {

  const { user, loading, completeVerification, resendVerificationCode } = useAuth()

  const location = useLocation()

  const navigate = useNavigate()

  const initial = (location.state as LocationState | null)?.challenge ?? null



  const [challenge, setChallenge] = useState<LoginChallenge | null>(initial)

  const [code, setCode] = useState('')

  const [error, setError] = useState<string | null>(null)

  const [busy, setBusy] = useState(false)

  const [status, setStatus] = useState<string | null>(initial?.message ?? null)

  const [nowTick, setNowTick] = useState(0)



  useEffect(() => {

    applyStoredTheme()

  }, [])



  useEffect(() => {

    const id = window.setInterval(() => setNowTick((n) => n + 1), 1000)

    return () => window.clearInterval(id)

  }, [])



  const maskedEmail = useMemo(() => challenge?.maskedEmail ?? '', [challenge])

  const resendWaitSeconds = useMemo(

    () => secondsUntil(challenge?.canResendAtUtc),

    // nowTick forces a recompute each second

    // eslint-disable-next-line react-hooks/exhaustive-deps

    [challenge?.canResendAtUtc, nowTick],

  )

  const canResend = resendWaitSeconds <= 0



  if (!loading && user?.authenticated) {

    return <Navigate to="/" replace />

  }



  if (!challenge?.verificationToken) {

    return <Navigate to="/login" replace />

  }



  const activeChallenge = challenge



  async function onSubmit(e: FormEvent) {

    e.preventDefault()

    setBusy(true)

    setError(null)

    const err = await completeVerification(activeChallenge.verificationToken, code)

    setBusy(false)

    if (err) {

      setError(err)

      return

    }

    navigate('/', { replace: true })

  }



  async function onResend() {

    if (!canResend) {

      setError(

        `A verification code was already sent. You can request a new one in ${formatCountdown(resendWaitSeconds)}.`,

      )

      return

    }

    setBusy(true)

    setError(null)

    setStatus(null)

    const result = await resendVerificationCode(activeChallenge.verificationToken)

    setBusy(false)

    if (typeof result === 'string') {

      setError(result)

      return

    }

    setChallenge(result)

    setCode('')

    setStatus(result.message || (result.emailSent ? 'A new code was emailed.' : 'Code refreshed.'))

  }



  return (

    <main className="auth-shell">

      <section className="auth-card" aria-labelledby="verify-title">

        <span className="brand-mark">HST Receipts</span>

        <h1 id="verify-title">Check your email</h1>

        <p className="auth-lead">Enter the verification code sent to</p>

        <p className="auth-masked-email" aria-label={`Email ${maskedEmail}`}>

          {maskedEmail}

        </p>



        {challenge.emailSent === false && (

          <p className="auth-dev-code" role="status">

            {challenge.message ||

              'Email was not sent. Configure Smtp in appsettings.Development.local.json (Gmail App Password), or use the development code below.'}

          </p>

        )}

        {challenge.emailSent === true && status && (

          <p className="auth-ok" role="status">

            {status}

          </p>

        )}



        <form onSubmit={onSubmit} className="auth-form">

          <label>

            Verification code

            <input

              value={code}

              onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))}

              inputMode="numeric"

              autoComplete="one-time-code"

              placeholder="6-digit code"

              required

              minLength={6}

              maxLength={6}

            />

          </label>



          {challenge.devCode && (

            <p className="auth-dev-code" role="note">

              Development code (email not delivered): <strong>{challenge.devCode}</strong>

            </p>

          )}



          {error && (

            <p className="error" role="alert">

              {error}

            </p>

          )}



          <button type="submit" className="btn-stamp" disabled={busy || code.length < 6}>

            {busy ? 'Verifying…' : 'Verify and continue'}

          </button>

        </form>



        <button

          type="button"

          className="btn-ghost demo-btn"

          disabled={busy || !canResend}

          onClick={onResend}

          title={

            canResend

              ? 'Send a new verification code'

              : `You can resend after ${formatCountdown(resendWaitSeconds)}`

          }

        >

          {canResend ? 'Resend code' : `Resend code (${formatCountdown(resendWaitSeconds)})`}

        </button>



        <p className="demo-note">

          <Link to="/login">Back to sign in</Link>

        </p>



        <div className="auth-theme">

          <ThemeToggle />

        </div>

      </section>

    </main>

  )

}


